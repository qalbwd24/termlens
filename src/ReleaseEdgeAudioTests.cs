using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TermLens {
public static partial class Tests {
    static async Task EdgeLiveWait(Task task,int milliseconds=45000) {
        if(await Task.WhenAny(task,Task.Delay(milliseconds))!=task) throw new TimeoutException("Fixed Edge audio test exceeded its deadline.");
        await task;
    }
    // Explicit online fixture: public synthetic text, fresh database/cache, and
    // only the existing proxy setting is read. No signing material is needed.
    public static int ReleaseEdgeAudioLive(string baseDir) {
        string folder=TestRoot(baseDir,"edge-audio-"+Guid.NewGuid().ToString("N").Substring(0,8)); int exit=0;
        const string word="perpendicular";
        const string answer="Perpendicular describes two lines or surfaces that meet at a right angle. A vertical line and a horizontal line are perpendicular when they form a ninety-degree angle. For example, the side of a rectangular sheet is perpendicular to its bottom edge. 中文意思是垂直的，两条直线相交形成直角。You can say that the support is perpendicular to the floor. The word describes direction relative to another object, not size. In a diagram, look for the small square that marks the right angle.";
        var form=new Form { Text="TermLens isolated Edge audio test",ShowInTaskbar=false,Width=340,Height=120 };
        var deadline=new CancellationTokenSource(120000);
        form.Shown+=async delegate {
            try {
                Check(Program.IsTest && !Program.AllowTestHotkey,"Edge live fixture runs in test mode without global shortcuts");
                Check(answer.Length>=400 && answer.Length<=600,"Full fixed explanation contains 400-600 English and Chinese characters");
                var settings=new Settings { SpeechProvider="edge",SpeechVoice="en-US-AndrewMultilingualNeural",SpeechRate=-10,SpeechProxy=Files.Load(Path.Combine(baseDir,"data")).SpeechProxy };
                var lookup=ReleaseAudioLookup(word); lookup.Answer="";
                using(var history=new HistoryStore(Path.Combine(folder,"history.sqlite3"))) {
                    history.RecordQuery(lookup.Id,lookup.Kind,"fixed-fixture",lookup.Term,"","","","fixed-fixture","",lookup.Created,"pending","");
                    var clock=Stopwatch.StartNew();
                    using(var native=new ReleaseAudioService(folder,history)) {
                        await EdgeLiveWait(native.PreloadAsync(word,"",settings,deadline.Token,lookup));
                        results.Add("TIMING: native provider cold original preload = "+clock.ElapsedMilliseconds+" ms");
                        var partial=Files.Json().Deserialize<ReleaseHistoryAudioManifest>(File.ReadAllText(Path.Combine(folder,"history-audio",lookup.Id,"manifest.json"),Files.Utf8));
                        Check(partial.WordComplete && partial.Word.Count==1 && !partial.ExplanationComplete,"Native Edge routing produces one complete original MP3 before an answer exists");
                    }
                    lookup.Answer=answer;
                    history.RecordQuery(lookup.Id,lookup.Kind,"fixed-fixture",lookup.Term,answer,"","","fixed-fixture","",lookup.Created,"complete","");
                    int calls=0;
                    using(var edge=new EdgeSpeech(settings.SpeechProxy)) {
                        using(var service=new ReleaseAudioService(folder,history,(p,t,c)=> { Interlocked.Increment(ref calls); return edge.Synthesize(t,p.Voice,p.Rate,c); })) {
                            clock.Restart();
                            await EdgeLiveWait(service.PreloadAsync(word,answer,settings,deadline.Token,lookup));
                            results.Add("TIMING: real full explanation preload = "+clock.ElapsedMilliseconds+" ms; native synthesis calls = "+calls);
                            Check(calls==1,"Full explanation uses one native synthesis call while the original reuses disk");
                            List<string> originalPaths,answerPaths;
                            Check(service.TryGetHistoryAudio(lookup,word,settings,out originalPaths) && originalPaths.Count==1,"Original MP3 passes disk length and SHA256 validation");
                            Check(service.TryGetHistoryAudio(lookup,answer,settings,out answerPaths) && answerPaths.Count==1,"Full normal answer is one MP3 asset with matching disk length and SHA256");
                            byte[] originalBytes=File.ReadAllBytes(originalPaths[0]),answerBytes=File.ReadAllBytes(answerPaths[0]);
                            Check(ReleaseAudioService.ValidAudio(originalBytes,".mp3") && ReleaseAudioService.ValidAudio(answerBytes,".mp3") && answerBytes.Length>originalBytes.Length,"Both real assets have MP3 headers and the full explanation contains more audio than the word");
                            string originalSample=Path.Combine(folder,"sample-original.mp3"),answerSample=Path.Combine(folder,"sample-full-answer.mp3");
                            File.Copy(originalPaths[0],originalSample); File.Copy(answerPaths[0],answerSample);
                            results.Add("SAMPLE ORIGINAL: "+originalSample); results.Add("SAMPLE FULL ANSWER: "+answerSample);
                            var manifest=Files.Json().Deserialize<ReleaseHistoryAudioManifest>(File.ReadAllText(Path.Combine(folder,"history-audio",lookup.Id,"manifest.json"),Files.Utf8));
                            Check(manifest.Provider=="edge" && manifest.WordComplete && manifest.ExplanationComplete && manifest.Explanation.Count==1,"Manifest identifies Edge and marks both complete groups ready");
                        }
                        using(var canceled=new CancellationTokenSource()) {
                            canceled.Cancel(); bool rejected=false; clock.Restart();
                            try { await EdgeLiveWait(edge.Synthesize(word,settings.SpeechVoice,settings.SpeechRate,canceled.Token),5000); }
                            catch(OperationCanceledException) { rejected=true; }
                            Check(rejected,"Native transport immediately rejects an already-canceled request");
                            results.Add("TIMING: already-canceled native synthesis = "+clock.ElapsedMilliseconds+" ms");
                        }
                    }
                    int offlineCalls=0;
                    using(var offline=new ReleaseAudioService(folder,history,(p,t,c)=> { Interlocked.Increment(ref offlineCalls); throw new IOException("Fixture forbids network synthesis."); })) {
                        clock.Restart(); await EdgeLiveWait(offline.PreloadAsync(word,answer,settings,deadline.Token,lookup),5000);
                        Check(offlineCalls==0,"Fresh service preloads both original and answer entirely from disk with network forbidden");
                        results.Add("TIMING: fresh-service disk-warm full preload = "+clock.ElapsedMilliseconds+" ms; synthesis calls = "+offlineCalls);
                        clock.Restart(); await EdgeLiveWait(offline.PlayAsync(word,settings,deadline.Token,null,lookup),15000);
                        Check(clock.ElapsedMilliseconds>=100,"Real original MP3 playback completes through MCI");
                        results.Add("TIMING: original MP3 playback completion = "+clock.ElapsedMilliseconds+" ms");
                        var began=new TaskCompletionSource<bool>();
                        Task playing=offline.PlayAsync(answer,settings,deadline.Token,null,lookup,()=>began.TrySetResult(true));
                        await EdgeLiveWait(began.Task,5000); await Task.Delay(250,deadline.Token);
                        Check(!playing.IsCompleted,"Full-answer MP3 remains active before the stop test");
                        offline.StopPlayback(); bool stopped=false;
                        try { await EdgeLiveWait(playing,5000); } catch(OperationCanceledException) { stopped=true; }
                        Check(stopped,"Stopping real full-answer MP3 cancels active playback");
                        await EdgeLiveWait(offline.PlayAsync(word,settings,deadline.Token,null,lookup),15000);
                        Check(offlineCalls==0,"Real playback restarts after stop and never fetches audio");
                    }
                    // Cancellation is deterministic: block a changed explanation
                    // before bytes arrive, preserving the already completed cache.
                    string manifestPath=Path.Combine(folder,"history-audio",lookup.Id,"manifest.json");
                    string beforeManifest=File.ReadAllText(manifestPath,Files.Utf8);
                    var revised=Files.Json().Deserialize<Lookup>(Files.Json().Serialize(lookup)); revised.Answer=answer+" This revised sentence is canceled before synthesis finishes.";
                    var pendingBegan=new TaskCompletionSource<bool>(); int pendingCalls=0;
                    using(var cancel=CancellationTokenSource.CreateLinkedTokenSource(deadline.Token))
                    using(var pending=new ReleaseAudioService(folder,history,async (p,t,c)=> { Interlocked.Increment(ref pendingCalls); pendingBegan.TrySetResult(true); await Task.Delay(Timeout.Infinite,c); return new byte[0]; })) {
                        Task work=pending.PreloadAsync("",revised.Answer,settings,cancel.Token,revised);
                        await EdgeLiveWait(pendingBegan.Task,5000); cancel.Cancel(); bool canceled=false;
                        try { await EdgeLiveWait(work,5000); } catch(OperationCanceledException) { canceled=true; }
                        Check(canceled && pendingCalls==1,"A canceled pending explanation exits promptly after one fetch attempt");
                        List<string> paths;
                        Check(File.ReadAllText(manifestPath,Files.Utf8)==beforeManifest && pending.TryGetHistoryAudio(lookup,word,settings,out paths) && pending.TryGetHistoryAudio(lookup,answer,settings,out paths),"Pending cancellation preserves the existing complete manifest and both validated cached assets");
                        Check(!pending.TryGetHistoryAudio(revised,revised.Answer,settings,out paths),"Canceled revised text is never claimed ready");
                    }
                }
                Check(!File.Exists(Path.Combine(folder,"translator-signing.dpapi")) && !File.Exists(Path.Combine(folder,"speechkey.dpapi")) && !File.Exists(Path.Combine(folder,"settings.json")),"Isolated Edge fixture contains no copied signer, speech key, or production settings");
                results.Add("INFO: Fixed public fixture text only. MCI completion verifies software playback; acoustic output and pronunciation require listening to the sample files.");
            } catch(Exception error) {
                results.Add("FAIL: Edge live fixture: "+ReleaseMicrosoftSafeError(error)+". Raw responses, proxy values and credentials omitted.");
                for(Exception item=error;item!=null;item=item.InnerException) {
                    results.Add("CAUSE: "+item.GetType().Name+"; STACK: "+item.StackTrace);
                    var http=item as System.Net.WebException; var response=http==null?null:http.Response as System.Net.HttpWebResponse;
                    if(response!=null) results.Add("HTTP STATUS: "+(int)response.StatusCode);
                    var socket=item as System.Net.WebSockets.WebSocketException;
                    if(socket!=null) results.Add("SOCKET CODE: "+socket.WebSocketErrorCode+"; NATIVE: "+socket.NativeErrorCode);
                }
                exit=1;
            }
            finally { deadline.Cancel(); File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); form.Close(); }
        };
        try { Application.Run(form); }
        finally { deadline.Cancel(); deadline.Dispose(); form.Dispose(); }
        return exit;
    }
}
}
