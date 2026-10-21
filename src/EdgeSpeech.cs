// Edge read-aloud protocol adapted from rany2/edge-tts (LGPL-3.0).
// https://github.com/rany2/edge-tts ; attribution and license in licenses/.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TermLens {
// Native adaptation of rany2/edge-tts's read-aloud protocol. No user credentials.
internal sealed class EdgeSpeech : IDisposable {
    const string TrustedToken="6A5AA1D4EAFF4E9FB37E23D68491D6F4";
    const string Version="143.0.3650.75";
    const string Endpoint="speech.platform.bing.com/consumer/speech/synthesize/readaloud";
    const string UserAgent="Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36 Edg/143.0.0.0";
    internal const int TextByteLimit=60*1024;
    const int MaxMessageBytes=1024*1024, MaxAudioBytes=32*1024*1024;
    static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
    readonly IWebProxy proxy;
    readonly CancellationTokenSource lifetime=new CancellationTokenSource();
    readonly object sync=new object();
    static readonly object factorySync=new object();
    static bool factoryRegistered;
    bool disposed;
    TimeSpan clockSkew;

    public EdgeSpeech(string proxy) {
        // Ignore legacy voice/system proxy settings. WebSocket and date-refresh
        // HTTP requests both use a direct connection.
        this.proxy=null;
    }

    public async Task<byte[]> Synthesize(string text,string voice,int rate,CancellationToken token) {
        CancellationTokenSource linked;
        lock(sync) {
            if(disposed) throw new ObjectDisposedException("EdgeSpeech");
            linked=CancellationTokenSource.CreateLinkedTokenSource(token,lifetime.Token);
        }
        using(linked) {
            linked.CancelAfter(TimeSpan.FromMinutes(5));
            try {
                linked.Token.ThrowIfCancellationRequested();
                string clean=CleanText(text??"");
                if(String.IsNullOrWhiteSpace(clean)) throw new ArgumentException("朗读文本不能为空。","text");
                if(clean.Length>1024*1024) throw new ArgumentException("朗读文本过长。","text");
                string fullVoice=NormalizeVoice(voice);
                using(var audio=new MemoryStream()) {
                    foreach(string part in Parts(clean)) {
                        byte[] bytes=await SynthesizePart(part,fullVoice,Math.Max(-50,Math.Min(50,rate)),linked.Token).ConfigureAwait(false);
                        if(audio.Length+bytes.Length>MaxAudioBytes) throw new InvalidDataException("Edge 朗读音频超过大小限制。");
                        audio.Write(bytes,0,bytes.Length);
                    }
                    return audio.ToArray();
                }
            } catch(OperationCanceledException) {
                if(!token.IsCancellationRequested && !lifetime.IsCancellationRequested)
                    throw new TimeoutException("Edge 朗读超时，请检查网络或代理后重试。");
                throw;
            }
        }
    }

    async Task<byte[]> SynthesizePart(string escaped,string voice,int rate,CancellationToken token) {
        for(int attempt=0;attempt<2;attempt++) {
            using(var socket=new ClientWebSocket()) {
                EnsureHandshakeFactory();
                socket.Options.Proxy=proxy;
                socket.Options.KeepAliveInterval=TimeSpan.FromSeconds(20);
                socket.Options.SetRequestHeader("Origin","chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold");
                // User-Agent is supplied as a HttpWebRequest property by the narrow
                // factory below: Framework rejects it through SetRequestHeader.
                socket.Options.SetRequestHeader("Pragma","no-cache");
                socket.Options.SetRequestHeader("Cache-Control","no-cache");
                socket.Options.SetRequestHeader("Accept-Language","en-US,en;q=0.9");
                socket.Options.SetRequestHeader("Cookie","muid="+Guid.NewGuid().ToString("N").ToUpperInvariant()+";");
                TimeSpan skew; lock(sync) skew=clockSkew;
                var uri=new Uri("wss://"+Endpoint+"/edge/v1?TrustedClientToken="+TrustedToken+"&ConnectionId="+Guid.NewGuid().ToString("N")+"&Sec-MS-GEC="+Gec(DateTime.UtcNow+skew)+"&Sec-MS-GEC-Version=1-"+Version);
                Exception connectError=null;
                using(var connect=CancellationTokenSource.CreateLinkedTokenSource(token)) {
                    connect.CancelAfter(TimeSpan.FromSeconds(20));
                    try { await socket.ConnectAsync(uri,connect.Token).ConfigureAwait(false); }
                    catch(Exception e) { connectError=e; }
                }
                if(connectError!=null) {
                    token.ThrowIfCancellationRequested();
                    DateTime serverDate;
                    if(attempt==0 && IsForbidden(connectError)) {
                        bool hasDate=TryServerDate(connectError,out serverDate);
                        if(!hasDate) {
                            DateTime? fetched=await FetchServerDate(token).ConfigureAwait(false);
                            hasDate=fetched.HasValue; if(hasDate) serverDate=fetched.Value;
                        }
                        if(hasDate) { lock(sync) clockSkew=serverDate-DateTime.UtcNow; continue; }
                    }
                    if(connectError is OperationCanceledException) throw new TimeoutException("Edge 朗读直连超时，请检查网络或稍后重试。",connectError);
                    throw new InvalidOperationException("Edge 朗读直连失败，请检查网络或稍后重试。",connectError);
                }
                using(token.Register(socket.Abort)) {
                    try {
                        await Send(socket,ConfigMessage(),token).ConfigureAwait(false);
                        await Send(socket,SsmlMessage(escaped,voice,rate),token).ConfigureAwait(false);
                        using(var audio=new MemoryStream()) {
                            int messages=0;
                            while(true) {
                                if(++messages>10000) throw new InvalidDataException("Edge 朗读返回过多消息。");
                                Received message=await Receive(socket,token).ConfigureAwait(false);
                                if(message.Type==WebSocketMessageType.Binary) {
                                    byte[] bytes=AudioPayload(message.Bytes);
                                    if(audio.Length+bytes.Length>MaxAudioBytes) throw new InvalidDataException("Edge 朗读音频超过大小限制。");
                                    audio.Write(bytes,0,bytes.Length);
                                } else {
                                    string content=Utf8.GetString(message.Bytes);
                                    int divider=content.IndexOf("\r\n\r\n",StringComparison.Ordinal);
                                    string headers=divider<0?content:content.Substring(0,divider);
                                    string path=Header(headers,"Path");
                                    if(path=="turn.end") {
                                        if(audio.Length==0) throw new InvalidDataException("Edge 朗读未返回音频，请检查声音设置。");
                                        return audio.ToArray();
                                    }
                                    if(path!="turn.start" && path!="response" && path!="audio.metadata")
                                        throw new InvalidDataException("Edge 朗读返回了未知消息。");
                                }
                            }
                        }
                    } catch(WebSocketException) { token.ThrowIfCancellationRequested(); throw; }
                }
            }
        }
        throw new InvalidOperationException("Edge 朗读连接失败。");
    }

    sealed class EdgeHandshakeFactory : IWebRequestCreate {
        public WebRequest Create(Uri uri) {
            // CreateDefault resolves only the scheme prefix, so this cannot recurse
            // into our longer endpoint prefix. The Framework wss factory retains
            // its own native WebSocket handshake and certificate validation.
            var request=WebRequest.CreateDefault(uri) as HttpWebRequest;
            if(request==null) throw new PlatformNotSupportedException("当前运行时不支持 Edge WebSocket 连接。");
            request.UserAgent=UserAgent;
            request.Proxy=null;
            return request;
        }
    }
    internal static void EnsureHandshakeFactory() {
        lock(factorySync) {
            if(factoryRegistered) return;
            // Called after constructing ClientWebSocket, whose static constructor
            // registers the Framework's standard wss scheme factory first.
            if(!WebRequest.RegisterPrefix("wss://"+Endpoint+"/edge/v1",new EdgeHandshakeFactory()))
                throw new InvalidOperationException("Edge WebSocket 连接工厂已被其他组件占用。");
            factoryRegistered=true;
        }
    }

    static async Task Send(ClientWebSocket socket,string message,CancellationToken token) {
        byte[] bytes=Utf8.GetBytes(message);
        if(bytes.Length>65536) throw new InvalidDataException("Edge 朗读请求超过服务限制。");
        await socket.SendAsync(new ArraySegment<byte>(bytes),WebSocketMessageType.Text,true,token).ConfigureAwait(false);
    }
    sealed class Received { public WebSocketMessageType Type; public byte[] Bytes; }
    static async Task<Received> Receive(ClientWebSocket socket,CancellationToken token) {
        using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token)) {
            deadline.CancelAfter(TimeSpan.FromSeconds(60));
            using(var message=new MemoryStream()) {
                byte[] buffer=new byte[8192];
                WebSocketMessageType? type=null;
                while(true) {
                    WebSocketReceiveResult result;
                    try { result=await socket.ReceiveAsync(new ArraySegment<byte>(buffer),deadline.Token).ConfigureAwait(false); }
                    catch(OperationCanceledException) { token.ThrowIfCancellationRequested(); throw new TimeoutException("Edge 朗读等待音频超时。"); }
                    if(result.MessageType==WebSocketMessageType.Close) throw new InvalidDataException("Edge 朗读在完成之前关闭了连接。");
                    if(type.HasValue && type.Value!=result.MessageType) throw new InvalidDataException("Edge 朗读消息分片类型不一致。");
                    type=result.MessageType;
                    if(message.Length+result.Count>MaxMessageBytes) throw new InvalidDataException("Edge 朗读消息超过大小限制。");
                    message.Write(buffer,0,result.Count);
                    if(result.EndOfMessage) return new Received { Type=type.Value,Bytes=message.ToArray() };
                }
            }
        }
    }

    internal static byte[] AudioPayload(byte[] message) {
        if(message==null || message.Length<2) throw new InvalidDataException("Edge 音频消息缺少头部长度。");
        int length=(message[0]<<8)|message[1];
        if(length>message.Length-2) throw new InvalidDataException("Edge 音频消息头部不完整。");
        string headers=Utf8.GetString(message,2,length);
        if(Header(headers,"Path")!="audio") throw new InvalidDataException("Edge 二进制消息不是音频。");
        string mime=Header(headers,"Content-Type");
        int count=message.Length-2-length;
        if(mime==null && count==0) return new byte[0];
        if(mime!="audio/mpeg" || count==0) throw new InvalidDataException("Edge 音频消息的格式或内容无效。");
        byte[] body=new byte[count]; Buffer.BlockCopy(message,2+length,body,0,count); return body;
    }
    static string Header(string headers,string key) {
        foreach(string line in headers.Split(new[] { "\r\n" },StringSplitOptions.RemoveEmptyEntries)) {
            int colon=line.IndexOf(':');
            if(colon>0 && String.Equals(line.Substring(0,colon),key,StringComparison.OrdinalIgnoreCase)) return line.Substring(colon+1).Trim();
        }
        return null;
    }
    internal static string Gec(DateTime utc) {
        long ticks=utc.ToUniversalTime().ToFileTimeUtc(); ticks-=ticks%3000000000L;
        using(var hash=SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.ASCII.GetBytes(ticks.ToString(CultureInfo.InvariantCulture)+TrustedToken))).Replace("-","");
    }
    internal static string NormalizeVoice(string voice) {
        if(String.IsNullOrWhiteSpace(voice)) voice="en-US-AriaNeural";
        voice=voice.Trim();
        Match m=Regex.Match(voice,@"^([a-z]{2,})-([A-Z]{2,})-([A-Za-z0-9-]+Neural)$");
        if(m.Success) {
            string region=m.Groups[2].Value,name=m.Groups[3].Value;
            int dash=name.IndexOf('-'); if(dash>=0) { region+="-"+name.Substring(0,dash); name=name.Substring(dash+1); }
            return "Microsoft Server Speech Text to Speech Voice ("+m.Groups[1].Value+"-"+region+", "+name+")";
        }
        if(!Regex.IsMatch(voice,@"^Microsoft Server Speech Text to Speech Voice \([A-Za-z-]+, [A-Za-z0-9]+Neural\)$")) throw new ArgumentException("Edge 朗读声音名称无效。","voice");
        return voice;
    }
    internal static string CleanText(string text) {
        var clean=new StringBuilder(text.Length);
        for(int i=0;i<text.Length;i++) {
            char c=text[i];
            if(Char.IsHighSurrogate(c)) {
                if(i+1<text.Length && Char.IsLowSurrogate(text[i+1])) { clean.Append(c); clean.Append(text[++i]); }
                else clean.Append(' ');
            } else if(Char.IsLowSurrogate(c) || c=='\ufffe' || c=='\uffff' || (c<32 && c!='\t' && c!='\n' && c!='\r')) clean.Append(' ');
            else clean.Append(c);
        }
        return clean.ToString();
    }
    static string Escape(string text) { return text.Replace("&","&amp;").Replace("<","&lt;").Replace(">","&gt;").Replace("\"","&quot;").Replace("'","&apos;"); }
    // Ordinary answers stay whole. Very large requests use a conservative 60 KiB
    // escaped-text budget; count UTF-8 per Unicode scalar and preserve text exactly.
    internal static List<string> Parts(string text) {
        var parts=new List<string>(); int start=0;
        while(start<text.Length) {
            int end=start,bytes=0,lastBoundary=-1;
            while(end<text.Length) {
                int width=Char.IsHighSurrogate(text[end]) && end+1<text.Length && Char.IsLowSurrogate(text[end+1])?2:1;
                int added=Utf8.GetByteCount(Escape(text.Substring(end,width)));
                if(bytes+added>TextByteLimit) break;
                bytes+=added; end+=width;
                if(Char.IsWhiteSpace(text[end-1]) || ".!?。！？；;".IndexOf(text[end-1])>=0) lastBoundary=end;
            }
            if(end<text.Length && lastBoundary>start && lastBoundary-start>(end-start)/2) end=lastBoundary;
            if(end==start) throw new InvalidDataException("无法拆分 Edge 朗读文本。");
            parts.Add(Escape(text.Substring(start,end-start))); start=end;
        }
        return parts;
    }
    static string Timestamp() { return DateTime.UtcNow.ToString("ddd MMM dd yyyy HH:mm:ss 'GMT+0000 (Coordinated Universal Time)'",CultureInfo.InvariantCulture); }
    static string ConfigMessage() {
        return "X-Timestamp:"+Timestamp()+"\r\nContent-Type:application/json; charset=utf-8\r\nPath:speech.config\r\n\r\n"+
            "{\"context\":{\"synthesis\":{\"audio\":{\"metadataoptions\":{\"sentenceBoundaryEnabled\":\"false\",\"wordBoundaryEnabled\":\"false\"},\"outputFormat\":\"audio-24khz-48kbitrate-mono-mp3\"}}}}\r\n";
    }
    internal static string SsmlMessage(string escaped,string voice,int rate) {
        return "X-RequestId:"+Guid.NewGuid().ToString("N")+"\r\nContent-Type:application/ssml+xml\r\nX-Timestamp:"+Timestamp()+"Z\r\nPath:ssml\r\n\r\n"+
            "<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-US'><voice name='"+Escape(voice)+"'><prosody pitch='+0Hz' rate='"+(rate>=0?"+":"")+rate.ToString(CultureInfo.InvariantCulture)+"%' volume='+0%'>"+escaped+"</prosody></voice></speak>";
    }
    static bool IsForbidden(Exception error) {
        for(Exception e=error;e!=null;e=e.InnerException) {
            var web=e as WebException; var response=web==null?null:web.Response as HttpWebResponse;
            if(response!=null && response.StatusCode==HttpStatusCode.Forbidden) return true;
            if(e.Message.IndexOf("403",StringComparison.Ordinal)>=0) return true;
        }
        return false;
    }
    static bool TryServerDate(Exception error,out DateTime date) {
        for(Exception e=error;e!=null;e=e.InnerException) {
            var web=e as WebException; var response=web==null?null:web.Response as HttpWebResponse;
            if(response!=null && DateTime.TryParse(response.Headers["Date"],CultureInfo.InvariantCulture,DateTimeStyles.AdjustToUniversal|DateTimeStyles.AssumeUniversal,out date)) return true;
        }
        date=default(DateTime); return false;
    }
    async Task<DateTime?> FetchServerDate(CancellationToken token) {
        var request=(HttpWebRequest)WebRequest.Create("https://"+Endpoint+"/voices/list?trustedclienttoken="+TrustedToken);
        request.Method="HEAD"; request.Proxy=proxy; request.UserAgent=UserAgent; request.Timeout=10000;
        using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token)) {
            deadline.CancelAfter(10000);
            using(deadline.Token.Register(request.Abort)) {
                HttpWebResponse response=null;
                try { response=(HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false); }
                catch(WebException e) { token.ThrowIfCancellationRequested(); response=e.Response as HttpWebResponse; }
                if(response==null) return null;
                using(response) {
                    DateTime date;
                    if(DateTime.TryParse(response.Headers["Date"],CultureInfo.InvariantCulture,DateTimeStyles.AdjustToUniversal|DateTimeStyles.AssumeUniversal,out date)) return date;
                    return null;
                }
            }
        }
    }
    public void Dispose() { lock(sync) { if(disposed) return; disposed=true; lifetime.Cancel(); } }
}
}
