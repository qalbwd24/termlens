using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TermLens {
public static partial class Tests {
    sealed class VoiceProbeHandler:DelegatingHandler {
        public VoiceProbeHandler():base(new HttpClientHandler { Proxy=new WebProxy("http://127.0.0.1:7897") }) { }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken token) {
            var res=await base.SendAsync(req,token);
            if(res.StatusCode==HttpStatusCode.BadRequest && req.RequestUri.AbsolutePath=="/cognitiveservices/v1") results.Add("Fixed-sample diagnostic: "+await res.Content.ReadAsStringAsync());
            return res;
        }
    }
    sealed class VoiceHandler:HttpMessageHandler {
        public int Auth,Audio; public string Ssml;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken token) {
            token.ThrowIfCancellationRequested();
            if(req.RequestUri.Host=="dev.microsofttranslator.com") {
                Auth++; Check(req.Headers.Contains("X-MT-Signature"),"Translator sign-in is signed");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent("{\"r\":\"eastus\",\"t\":\"test-token\"}") };
            }
            Audio++; Ssml=await req.Content.ReadAsStringAsync();
            if(Audio==1) return new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content=new StringContent("") };
            var body=new ByteArrayContent(new byte[1000]); body.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue("audio/mpeg");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content=body };
        }
    }
    public static int Speech(string root,bool live) {
        string folder=TestRoot(root,live?"voice-live":"voice");
        try {
            string original=String.Concat(Enumerable.Repeat("A long sentence 中文 😀. ",600));
            var parts=MainWindow.SpeechParts(original);
            Check(String.Concat(parts)==original && parts.All(p=>p.Length<=2500),"Long readings keep every character and stay within request limits");
            Check(parts.All(p=>!Char.IsHighSurrogate(p[p.Length-1])),"Audio chunks never split a Unicode character");
            var handler=new VoiceHandler();
            using(var service=new TranslatorSpeech("","",handler,new byte[32])) {
                byte[] audio=service.Synthesize("a < b & 中文","en-US-AvaMultilingualNeural",-10,CancellationToken.None).GetAwaiter().GetResult();
                Check(handler.Auth==2 && handler.Audio==2 && audio.Length==1000,"Expired token refreshes once and retries synthesis");
                Check(handler.Ssml.Contains("&lt;") && handler.Ssml.Contains("&amp;") && handler.Ssml.Contains("-10%"),"Speech markup safely escapes text and keeps configured speed");
                service.Synthesize("next","en-US-AvaMultilingualNeural",0,CancellationToken.None).GetAwaiter().GetResult();
                Check(handler.Auth==2,"Later readings reuse the cached voice token");
                var cancel=new CancellationTokenSource(); cancel.Cancel();
                try { service.Synthesize("cancel","en-US-AvaMultilingualNeural",0,cancel.Token).GetAwaiter().GetResult(); throw new Exception("Cancellation ignored"); }
                catch(OperationCanceledException) { Check(handler.Audio==3,"Stopped request cannot fetch or play late audio"); }
            }
            if(live) {
                using(var service=new TranslatorSpeech(Path.Combine(root,"data"),"http://127.0.0.1:7897",new VoiceProbeHandler())) {
                    byte[] audio=service.Synthesize("An algorithm is a set of steps. 算法就是一组步骤。","en-US-AvaMultilingualNeural",-10,CancellationToken.None).GetAwaiter().GetResult();
                    string path=Path.Combine(folder,"sample.mp3"); File.WriteAllBytes(path,audio);
                    Check(audio.Length>1000,"Live Microsoft Translator returned bilingual MP3 audio");
                    CheckPlayback(path);
                }
            }
            File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); return 0;
        } catch(Exception e) { results.Add("FAIL: "+e.GetType().Name+" "+e.Message); File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); return 1; }
    }
    static void CheckPlayback(string path) {
        Exception failure=null; var host=new Form { Opacity=0,ShowInTaskbar=false };
        host.Shown+=async delegate {
            try {
                using(var player=new Mp3Player()) await player.Play(path,CancellationToken.None);
                Check(true,"Windows MP3 playback reached the end without an error");
                using(var player=new Mp3Player()) using(var cancel=new CancellationTokenSource(300)) {
                    try { await player.Play(path,cancel.Token); throw new Exception("Audio cancellation ignored"); }
                    catch(OperationCanceledException) { Check(true,"Stop interrupts active Windows playback"); }
                }
            } catch(Exception e) { failure=e; }
            finally { host.Close(); }
        };
        Application.Run(host); if(failure!=null) throw failure;
    }
    public static int ThemeUi(string root) {
        string folder=TestRoot(root,"theme"); int exit=0; var card=new ResultPopup();
        card.Shown+=async delegate {
            try {
                card.SetAnswer("Meaning: A function (a small computer job) calls itself to solve a smaller part of a problem.\n\nExample: Count down from three to zero, then stop."); card.Complete(false);
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                var rich=(RichTextBox)typeof(ResultPopup).GetField("answer",flags).GetValue(card);
                var source=(RichTextBox)typeof(ResultPopup).GetField("term",flags).GetValue(card);
                source.Select(source.Text.IndexOf("so the"),2);
                Check(source.SelectionFont!=null && source.SelectionFont.Name=="Microsoft YaHei UI","English after Chinese parentheses keeps the reading font");
                card.SetAnswer("Meaning: A function (函数) calls itself.\n\nExample: Count down to zero, then stop.");
                rich.Select(rich.Text.IndexOf("calls itself"),12);
                Check(rich.SelectionFont!=null && rich.SelectionFont.Name=="Microsoft YaHei UI" && !rich.SelectionFont.Bold,"Answer font after Chinese stays consistent and regular");
                var listen=(Button)typeof(ResultPopup).GetField("listen",flags).GetValue(card);
                string spoken=""; card.SpeechRequested+=delegate(string text) { spoken=text; };
                rich.Focus(); rich.Select(rich.Text.IndexOf("function"),8); listen.Focus(); listen.PerformClick();
                Check(spoken=="function","Listen retains selected word when its button takes focus");
                rich.Focus(); rich.Select(0,0); listen.Focus(); listen.PerformClick();
                Check(spoken==rich.Text,"Listen reads the full answer when selection is empty");
                foreach(bool dark in new[]{true,false}) {
                    Theme.TestDark=dark; Theme.Refresh(); Theme.Apply(card); card.Refresh(); await Task.Delay(150);
                    Check(card.BackColor==Look.Surface && rich.ForeColor==Look.Ink && Theme.Dark==dark,"Card follows "+(dark?"dark":"light")+" theme");
                    using(var bmp=new Bitmap(card.Width,card.Height)) { using(var g=Graphics.FromImage(bmp)) g.CopyFromScreen(card.Location,Point.Empty,card.Size); bmp.Save(Path.Combine(folder,dark?"dark.png":"light.png")); }
                }
                card.Size=card.MinimumSize;
                var settings=(Button)typeof(ResultPopup).GetField("settings",flags).GetValue(card);
                Check(listen.Right<=settings.Left,"Listen and Settings do not overlap at minimum width");
                card.SetSpeaking(true,"Reading aloud…"); bool stopped=false; card.SpeechStopRequested+=delegate { stopped=true; }; listen.PerformClick();
                Check(stopped,"Listen becomes a Stop audio control during loading or playback");
                stopped=false; card.Hide(); Check(stopped,"Closing the reading window stops its audio");
            } catch(Exception e) { results.Add(e.ToString()); exit=1; }
            finally { Theme.TestDark=null; Theme.Refresh(); File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); card.Close(); }
        };
        card.BeginLookup("The problem is assigning a local variable (局部变量), so the call fails. Each name must have a value before you use it.",new Rectangle(150,100,120,20)); Application.Run(card); return exit;
    }
}
}
