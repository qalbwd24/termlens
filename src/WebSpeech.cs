using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TermLens {
// Public website pronunciation endpoint, not the paid Baidu Cloud service.
public sealed class WebSpeech : IDisposable {
    readonly HttpClient client;
    public WebSpeech(HttpMessageHandler handler=null) {
        client=new HttpClient(handler??new HttpClientHandler { UseProxy=false,AllowAutoRedirect=false });
        client.Timeout=TimeSpan.FromSeconds(15);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TermLens/1.2");
    }
    static bool Chinese(char c) { return c>='\u3400' && c<='\u9fff'; }
    // Keep Chinese glosses separate from English and stay below the website's short-text range.
    public static List<string> Parts(string text) {
        var spans=new List<string>(); var buffer=new StringBuilder(); int language=0;
        foreach(char c in (text??"")) {
            int next=Chinese(c)?1:((c>='a'&&c<='z')||(c>='A'&&c<='Z'))?2:0;
            if(next!=0 && language!=0 && next!=language) { spans.Add(buffer.ToString()); buffer.Clear(); }
            buffer.Append(c); if(next!=0) language=next;
        }
        if(buffer.Length>0) spans.Add(buffer.ToString());
        var result=new List<string>();
        foreach(string span in spans) {
            string remaining=span.Trim(); int limit=HasChinese(remaining)?60:160;
            while(remaining.Length>0) {
                int take=Math.Min(limit,remaining.Length);
                if(take<remaining.Length) {
                    int boundary=-1;
                    for(int i=take-1;i>=take/3;i--) if(".!?。！？;；\n".IndexOf(remaining[i])>=0) { boundary=i+1; break; }
                    if(boundary<0) for(int i=take-1;i>=take/3;i--) if(Char.IsWhiteSpace(remaining[i])) { boundary=i+1; break; }
                    if(boundary>0) take=boundary;
                    if(Char.IsHighSurrogate(remaining[take-1])) take--;
                }
                string part=remaining.Substring(0,take).Trim();
                if(part.Length>0) result.Add(part);
                remaining=remaining.Substring(take).TrimStart();
            }
        }
        return result;
    }
    static bool HasChinese(string text) { foreach(char c in text) if(Chinese(c)) return true; return false; }
    public async Task<byte[]> Synthesize(string text,string voice,int rate,CancellationToken cancel) {
        string language=HasChinese(text)?"zh":"en";
        int speed=Math.Max(0,Math.Min(10,5+(int)Math.Round(rate/10.0)));
        string url="https://fanyi.baidu.com/gettts?lan="+language+"&text="+Uri.EscapeDataString(text)+"&spd="+speed+"&source=web";
        using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel)) {
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using(var response=await client.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,timeout.Token).ConfigureAwait(false)) {
            if(!response.IsSuccessStatusCode) throw new Exception(UiLanguage.T("Baidu web voice is unavailable. Try again or switch voice service in Settings.","百度网页朗读暂时不可用，可重试或在设置中切换语音服务。"));
            using(timeout.Token.Register(delegate { response.Dispose(); })) {
                byte[] bytes=await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                timeout.Token.ThrowIfCancellationRequested();
                bool mp3=bytes.Length>256 && ((bytes[0]=='I' && bytes[1]=='D' && bytes[2]=='3') || (bytes[0]==255 && (bytes[1]&0xe0)==0xe0));
                if(!mp3) throw new Exception(UiLanguage.T("Baidu returned no usable audio. Try Microsoft in Voice settings.","百度没有返回可用音频，可在朗读设置中切换微软。"));
                return bytes;
            }
        }
        }
    }
    public void Dispose() { client.Dispose(); }
}
}
