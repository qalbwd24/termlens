using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TermLens {
public static partial class Tests {
    // Explicit opt-in online test. All text/cache files are synthetic; signing
    // material is read in place by TranslatorSpeech and never copied or logged.
    public static int ReleaseAudioLatency(string root) {
        string folder=TestRoot(root,"release-audio-latency"); int exit=0,requests=0;
        const string word="perpendicular";
        const string answer="Perpendicular describes two lines, edges, or surfaces that meet at a right angle. In a drawing, a vertical line and a horizontal line are perpendicular when their intersection forms a ninety-degree angle. For example, the side of a rectangular sheet is perpendicular to its bottom edge. The Chinese meaning is 垂直的. In a sentence, you can say that the support is perpendicular to the floor. Keep the relationship between the two objects clear: the word describes their direction relative to each other, not their size. This is a fixed synthetic explanation used only to measure audio preparation.";
        ReleaseAudioService service=null; TranslatorSpeech translator=null; WebSpeech web=null;
        var lifetime=new CancellationTokenSource();
        var form=new Form { Text="TermLens isolated audio latency test",Width=420,Height=180,ShowInTaskbar=false };
        var heartbeat=new System.Windows.Forms.Timer { Interval=25 };
        var elapsed=Stopwatch.StartNew(); double last=0,maxGap=0; int beats=0;
        heartbeat.Tick+=delegate { double current=elapsed.Elapsed.TotalMilliseconds; if(last>0) maxGap=Math.Max(maxGap,current-last); last=current; beats++; };
        form.Shown+=async delegate {
            lifetime.CancelAfter(TimeSpan.FromSeconds(60)); last=elapsed.Elapsed.TotalMilliseconds; heartbeat.Start();
            try {
                Check(Program.IsTest && !Program.AllowTestHotkey,"Latency test uses explicit test mode without any global shortcut permission");
                Check(answer.Length>=400 && answer.Length<=800,"The mixed-language fixture is 400-800 characters and contains no user text");
                string realData=Path.Combine(root,"data");
                var settings=Files.Load(realData);
                // Keep proxy/voice/rate private. Only public fixed fixture text is
                // sent; do not serialize settings into the test output folder.
                if(settings.SpeechProvider=="microsoft") translator=new TranslatorSpeech(realData,settings.SpeechProxy);
                else if(settings.SpeechProvider=="baidu-web") web=new WebSpeech();
                else throw new NotSupportedException("The selected provider is not an online voice provider.");
                Func<ReleaseAudioProfile,string,CancellationToken,Task<byte[]>> fetch=async delegate(ReleaseAudioProfile profile,string text,CancellationToken token) {
                    Interlocked.Increment(ref requests);
                    if(translator!=null) return await translator.Synthesize(text,profile.Voice,profile.Rate,token).ConfigureAwait(false);
                    return await web.Synthesize(text,profile.Voice,profile.Rate,token).ConfigureAwait(false);
                };
                var lookup=new Lookup { Id="fixed-latency-fixture",Term=word,Answer="",Created=DateTime.UtcNow.ToString("o"),Kind="explain",Status="complete" };
                service=new ReleaseAudioService(folder,null,fetch);
                var watch=Stopwatch.StartNew(); int before=requests;
                await service.PreloadAsync(word,"",settings,lifetime.Token,lookup);
                results.Add("TIMING: cold original synthesis and managed disk cache = "+watch.ElapsedMilliseconds+" ms; TTS calls = "+(requests-before));
                Check(requests-before==1,"A fixed original word needs one cold TTS synthesis call");
                service.Dispose(); service=new ReleaseAudioService(folder,null,fetch);
                watch.Restart(); before=requests;
                await service.PreloadAsync(word,"",settings,lifetime.Token,lookup);
                results.Add("TIMING: original disk-warm preload in a fresh service = "+watch.ElapsedMilliseconds+" ms; TTS calls = "+(requests-before));
                Check(requests==before,"A fresh service reuses the original word from disk without a network call");
                lookup.Answer=answer; watch.Restart(); before=requests;
                await service.PreloadAsync(word,answer,settings,lifetime.Token,lookup);
                results.Add("TIMING: complete mixed-language explanation preload = "+watch.ElapsedMilliseconds+" ms; TTS calls = "+(requests-before));
                var manifest=Files.Json().Deserialize<ReleaseHistoryAudioManifest>(File.ReadAllText(Path.Combine(folder,"history-audio",lookup.Id,"manifest.json"),Files.Utf8));
                Check(manifest.WordComplete && manifest.ExplanationComplete && manifest.Explanation.Count>0,"The manifest marks both full original and explanation audio complete");
                if(translator!=null) Check(requests-before==1 && manifest.Explanation.Count==1,"Microsoft synthesizes this complete mixed-language explanation as one asset and one TTS call");
                else results.Add("INFO: The current website provider retains its provider-specific short-part limits.");
                watch.Restart(); before=requests;
                await service.PreloadAsync(word,answer,settings,lifetime.Token,lookup);
                results.Add("TIMING: repeated full preload = "+watch.ElapsedMilliseconds+" ms; TTS calls = "+(requests-before));
                Check(requests==before,"Repeating the full preload adds zero network synthesis requests");
                // Let the UI timer record one final interval after continuation.
                await Task.Delay(80,lifetime.Token);
                results.Add("UI HEARTBEAT: interval=25 ms; ticks="+beats+"; longest observed gap="+Math.Round(maxGap,1)+" ms (measurement only, not a timing pass threshold).");
                Check(beats>0,"The UI message loop continued processing timer callbacks during the test");
                results.Add("INFO: Only fixed fixture text was synthesized; no long answer was played. No credentials, private proxy, profile snapshot or user text were copied to the test folder.");
            } catch(Exception error) {
                // Never write raw service responses or error strings: they may
                // contain a URL, proxy details, authorization data or identifiers.
                var http=Regex.Match(error.Message??"",@"\bHTTP\s+\d{3}\b",RegexOptions.IgnoreCase);
                results.Add("FAIL: "+error.GetType().Name+(http.Success?" "+http.Value:"")+"; initiated TTS calls="+requests); exit=1;
            } finally {
                heartbeat.Stop(); lifetime.Cancel();
                if(service!=null) { service.Dispose(); service=null; }
                if(translator!=null) { translator.Dispose(); translator=null; }
                if(web!=null) { web.Dispose(); web=null; }
                File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); form.Close();
            }
        };
        try { Application.Run(form); }
        finally { heartbeat.Dispose(); lifetime.Cancel(); lifetime.Dispose(); form.Dispose(); }
        return exit;
    }
}
}
