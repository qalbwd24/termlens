using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace TermLens {
public static partial class Tests {
    public static int EdgeSpeechProtocol(string root) {
        string folder=TestRoot(root,"edge-speech-protocol");
        try {
            using(var socket=new System.Net.WebSockets.ClientWebSocket()) {
                EdgeSpeech.EnsureHandshakeFactory();
                var handshake=System.Net.WebRequest.Create("wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1") as System.Net.HttpWebRequest;
                Check(handshake!=null && handshake.UserAgent.Contains("Edg/143.0.0.0"),"Edge narrow native WebSocket factory supplies required User-Agent without restricted-header API");
                Check(handshake.Proxy==null,"Edge native handshake bypasses the system proxy");
                try { handshake.Abort(); }
                catch(System.Net.WebException canceled) { if(canceled.Status!=System.Net.WebExceptionStatus.RequestCanceled) throw; }
            }
            DateTime time=new DateTime(2026,10,3,12,34,56,DateTimeKind.Utc);
            Check(EdgeSpeech.Gec(time)=="FE3E7E30542288F7142FEFF9CE5EFF3EC6EA7BB687F4CD698C271301945DD939","Edge GEC uses 1601 epoch, five-minute floor and uppercase SHA256");
            Check(EdgeSpeech.Gec(time)==EdgeSpeech.Gec(new DateTime(2026,10,3,12,30,0,DateTimeKind.Utc)) && EdgeSpeech.Gec(time)!=EdgeSpeech.Gec(new DateTime(2026,10,3,12,35,0,DateTimeKind.Utc)),"Edge GEC changes at five-minute boundary");
            string voice=EdgeSpeech.NormalizeVoice("en-US-AndrewMultilingualNeural");
            Check(voice=="Microsoft Server Speech Text to Speech Voice (en-US, AndrewMultilingualNeural)","Edge short voice name becomes server voice name");
            Check(EdgeSpeech.NormalizeVoice("zh-CN-liaoning-XiaobeiNeural")=="Microsoft Server Speech Text to Speech Voice (zh-CN-liaoning, XiaobeiNeural)","Edge regional voice names preserve locale suffix");
            string original="All of this answer stays together. <angle> & 'quote' \"text\" 中文 😀\n"+new String('x',9000);
            var parts=EdgeSpeech.Parts(original);
            Check(parts.Count==1,"Edge ordinary long answers use a single SSML request");
            string message=EdgeSpeech.SsmlMessage(parts[0],voice,-10);
            var document=new XmlDocument(); document.LoadXml(message.Substring(message.IndexOf("\r\n\r\n",StringComparison.Ordinal)+4));
            Check(document.DocumentElement.InnerText==original,"Edge SSML escapes XML and retains Unicode and every character");
            Check(message.Contains("rate='-10%'"),"Edge SSML uses configured percentage rate");
            string cleaned=EdgeSpeech.CleanText("A\0B\u000bC\ud800D\uffffE\n\t😀");
            Check(cleaned=="A B C D E\n\t😀","Edge incompatible controls and isolated surrogates become spaces");
            string huge=String.Concat(Enumerable.Repeat("中文😀<&> 'quoted' sentence.\n",5000));
            var many=EdgeSpeech.Parts(huge);
            var roundtrip=new StringBuilder();
            foreach(string part in many) {
                Check(Encoding.UTF8.GetByteCount(part)<=EdgeSpeech.TextByteLimit,"Edge large-answer parts obey escaped UTF8 budget");
                string wire=EdgeSpeech.SsmlMessage(part,voice,0);
                Check(Encoding.UTF8.GetByteCount(wire)<=65536,"Edge full SSML stays below conservative 64 KiB request budget");
                var xml=new XmlDocument(); xml.LoadXml("<text>"+part+"</text>"); roundtrip.Append(xml.DocumentElement.InnerText);
            }
            Check(many.Count>1 && roundtrip.ToString()==huge,"Edge large-request splitting preserves text, Unicode and XML entities");
            byte[] payload={0xff,0xf3,0x84,0x00,0x2c,0x5a};
            Check(EdgeSpeech.AudioPayload(EdgeFrame("Path:audio\r\nContent-Type:audio/mpeg\r\nX-RequestId:abc\r\n",payload)).SequenceEqual(payload),"Edge binary audio starts exactly after two-byte length plus header bytes");
            Check(EdgeSpeech.AudioPayload(EdgeFrame("Path:audio\r\n",new byte[0])).Length==0,"Edge empty binary audio terminator is accepted");
            Check(EdgeInvalid(new byte[] { 0 }),"Edge truncated binary prefix is rejected");
            Check(EdgeInvalid(new byte[] { 0,20,65 }),"Edge truncated binary headers are rejected");
            Check(EdgeInvalid(EdgeFrame("Path:audio\r\n",payload)),"Edge audio without content type is rejected");
            Check(EdgeInvalid(EdgeFrame("Path:audio\r\nContent-Type:audio/mpeg\r\n",new byte[0])),"Edge empty audio chunk is rejected");
            Check(EdgeInvalid(EdgeFrame("Path:audio.metadata\r\nContent-Type:audio/mpeg\r\n",payload)),"Edge non-audio binary messages are rejected");
            Check(EdgeInvalid(EdgeFrame("Path:audio\r\nContent-Type:text/plain\r\n",payload)),"Edge unexpected audio MIME type is rejected");
            using(var service=new EdgeSpeech("")) {
                using(var cancel=new System.Threading.CancellationTokenSource()) {
                    cancel.Cancel(); bool canceled=false;
                    try { service.Synthesize("cancel without networking",voice,0,cancel.Token).GetAwaiter().GetResult(); } catch(OperationCanceledException) { canceled=true; }
                    Check(canceled,"Edge canceled synthesis stops before networking");
                }
                service.Dispose(); bool rejected=false;
                try { service.Synthesize("disposed",voice,0,System.Threading.CancellationToken.None).GetAwaiter().GetResult(); } catch(ObjectDisposedException) { rejected=true; }
                Check(rejected,"Edge disposed adapter rejects new synthesis");
            }
            File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); return 0;
        } catch(Exception e) { results.Add("FAIL: "+e.GetType().Name+": "+e.Message); File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); return 1; }
    }
    static byte[] EdgeFrame(string headers,byte[] payload) {
        byte[] header=Encoding.UTF8.GetBytes(headers),message=new byte[2+header.Length+payload.Length];
        message[0]=(byte)(header.Length>>8); message[1]=(byte)header.Length;
        Buffer.BlockCopy(header,0,message,2,header.Length); Buffer.BlockCopy(payload,0,message,2+header.Length,payload.Length); return message;
    }
    static bool EdgeInvalid(byte[] message) { try { EdgeSpeech.AudioPayload(message); return false; } catch(InvalidDataException) { return true; } }
}
}
