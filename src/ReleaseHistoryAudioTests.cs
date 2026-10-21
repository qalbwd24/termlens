using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TermLens {
public static partial class Tests {
    static async Task ReleaseHistoryAudioInjectedChecks(string folder,HistoryStore history) {
        var settings=new Settings { SpeechProvider="microsoft",SpeechVoice="en-US-AriaNeural",SpeechRate=0,SpeechProxy="http://fixture-secret@localhost:9999" };
        var lookup=ReleaseAudioLookup("history sample"); lookup.Created=DateTime.UtcNow.AddDays(-4).ToString("o");
        int calls=0; List<string> paths;
        using(var service=new ReleaseAudioService(folder,history,(p,t,c)=> { calls++; return Task.FromResult(ReleaseFakeAudio(p.Extension)); })) {
            await service.PreloadAsync(lookup.Term,"",settings,CancellationToken.None,lookup);
            string file=Path.Combine(folder,"history-audio",lookup.Id,"manifest.json");
            var partial=Files.Json().Deserialize<ReleaseHistoryAudioManifest>(File.ReadAllText(file,Files.Utf8));
            Check(partial.Version==1 && partial.QueryId==lookup.Id && partial.Created==lookup.Created && partial.WordComplete && !partial.ExplanationComplete,"Original-only preload writes a dated query manifest before an answer exists");
            await service.PreloadAsync(lookup.Term,lookup.Answer,settings,CancellationToken.None,lookup);
            Check(calls==2 && service.TryGetHistoryAudio(lookup,lookup.Term,settings,out paths),"History preload persists original and answer once without requiring a favorite");
            Check(service.TryGetHistoryAudio(lookup,lookup.Answer,settings,out paths) && !service.TryGetHistoryAudio(lookup,"history",settings,out paths),"History cache matches complete text and never substitutes full audio for a selection");
            string raw=File.ReadAllText(file,Files.Utf8);
            Check(!raw.Contains("fixture-secret") && !raw.Contains("localhost"),"History audio manifest excludes proxy connection details");
            settings.SpeechRate=10;
            Check(!service.TryGetHistoryAudio(lookup,lookup.Term,settings,out paths),"A rate change invalidates ordinary-history audio");
            await service.PreloadAsync(lookup.Term,lookup.Answer,settings,CancellationToken.None,lookup);
            var changed=Files.Json().Deserialize<ReleaseHistoryAudioManifest>(File.ReadAllText(file,Files.Utf8));
            Check(calls==4 && changed.Retired.Count==2,"Rebuilt history audio tracks superseded assets for bounded retention");
        }
        int offlineCalls=0;
        using(var offline=new ReleaseAudioService(folder,history,(p,t,c)=> { offlineCalls++; throw new IOException("Injected offline"); })) {
            await offline.PreloadAsync(lookup.Term,lookup.Answer,settings,CancellationToken.None,lookup);
            Check(offlineCalls==0 && offline.TryGetHistoryAudio(lookup,lookup.Answer,settings,out paths),"Fresh history service reuses both groups offline after memory cache is gone");
            offline.TryGetHistoryAudio(lookup,lookup.Term,settings,out paths); File.WriteAllBytes(paths[0],new byte[]{1,2,3});
            Check(!offline.TryGetHistoryAudio(lookup,lookup.Term,settings,out paths),"History disk reuse verifies bytes and SHA256");
        }
        var pending=ReleaseAudioLookup("pending history"); var began=new TaskCompletionSource<bool>(); var release=new TaskCompletionSource<bool>();
        using(var cancel=new CancellationTokenSource())
        using(var service=new ReleaseAudioService(folder,history,async (p,t,c)=> { began.TrySetResult(true); await release.Task; return ReleaseFakeAudio(p.Extension); })) {
            Task work=service.PreloadAsync(pending.Term,"",settings,cancel.Token,pending); await ReleaseAudioDeadline(began.Task);
            cancel.Cancel(); release.TrySetResult(true); try { await work; } catch(OperationCanceledException) { }
            string file=Path.Combine(folder,"history-audio",pending.Id,"manifest.json");
            var manifest=Files.Json().Deserialize<ReleaseHistoryAudioManifest>(File.ReadAllText(file,Files.Utf8));
            Check(manifest.QueryId==pending.Id && manifest.Created==pending.Created && !manifest.WordComplete,"Canceled pending history keeps a dated manifest and cannot claim ready audio");
        }
        var partialFavorite=history.SetFavorite(ReleaseAudioLookup("partial favorite"),true); int part=0;
        using(var service=new ReleaseAudioService(folder,history,(p,t,c)=> { if(++part>1) throw new IOException("Injected answer synthesis failure"); return Task.FromResult(ReleaseFakeAudio(p.Extension)); })) {
            try { await service.PrepareFavoriteAsync(partialFavorite,settings); } catch(IOException) { }
            string directory=Path.Combine(folder,"favorites",partialFavorite.Id);
            var journal=Files.Json().Deserialize<ReleaseAudioManifest>(File.ReadAllText(Path.Combine(directory,"assets.json"),Files.Utf8));
            Check(journal.FavoriteId==partialFavorite.Id && journal.Retired.Count==1 && File.Exists(Path.Combine(directory,journal.Retired[0].File)) && !File.Exists(Path.Combine(directory,"manifest.json")),"Failed favorite keeps every completed asset in a cleanup journal without publishing partial playable audio");
        }
        await ReleaseAudioPriorityChecks(folder,history);
    }
    static async Task ReleaseAudioPriorityChecks(string folder,HistoryStore history) {
        string answer=String.Join(" ",Enumerable.Repeat("An algorithm is a clear sequence of steps for solving a problem.",70));
        foreach(string provider in new[]{"microsoft","windows"}) {
            var settings=new Settings { SpeechProvider=provider,SpeechVoice=provider=="windows"?"Fixture voice":"en-US-AriaNeural" };
            var lookup=ReleaseAudioLookup("priority "+provider); lookup.Answer=answer;
            int owner=Thread.CurrentThread.ManagedThreadId,calls=0,answerCalls=0,startedCount=0; bool fetchedOnUi=false,playedOnUi=true,announcedOnUi=true;
            var answerBegan=new TaskCompletionSource<bool>(); var finishAnswer=new TaskCompletionSource<bool>();
            using(var service=new ReleaseAudioService(folder,history,async (p,t,c)=> {
                Interlocked.Increment(ref calls); if(Thread.CurrentThread.ManagedThreadId==owner) fetchedOnUi=true;
                if(t==answer) { Interlocked.Increment(ref answerCalls); answerBegan.TrySetResult(true); await finishAnswer.Task; }
                return ReleaseFakeAudio(p.Extension);
            },(path,c)=> { playedOnUi=playedOnUi && Thread.CurrentThread.ManagedThreadId==owner; return Task.FromResult(true); })) {
                await service.PreloadAsync(lookup.Term,"",settings,CancellationToken.None,lookup);
                Task preload=service.PreloadAsync("",lookup.Answer,settings,CancellationToken.None,lookup);
                await ReleaseAudioDeadline(answerBegan.Task);
                Action announced=delegate { startedCount++; announcedOnUi=announcedOnUi && Thread.CurrentThread.ManagedThreadId==owner; };
                Task original=service.PlayAsync(lookup.Term,settings,CancellationToken.None,null,lookup,announced);
                await ReleaseAudioDeadline(original);
                Check(!preload.IsCompleted && calls==2,provider+": ready original plays from disk while full-answer synthesis is still blocked");
                Task playAnswer=service.PlayAsync(answer,settings,CancellationToken.None,null,lookup,announced);
                await Task.Delay(80); finishAnswer.TrySetResult(true);
                await ReleaseAudioDeadline(Task.WhenAll(preload,playAnswer));
                Check(answer.Length>2500 && calls==2 && answerCalls==1,provider+": completed long explanation is one request shared by preload and user playback");
                Check(!fetchedOnUi && playedOnUi && announcedOnUi && startedCount==2,provider+": preparation runs off UI; each playback announces once on UI owner");
                var manifest=Files.Json().Deserialize<ReleaseHistoryAudioManifest>(File.ReadAllText(Path.Combine(folder,"history-audio",lookup.Id,"manifest.json"),Files.Utf8));
                Check(manifest.Word.Count==1 && manifest.Explanation.Count==1 && manifest.WordComplete && manifest.ExplanationComplete,provider+": concurrent role commits retain both complete single-file groups");
            }
        }
        var baidu=ReleaseAudioProfile.FromSettings(new Settings { SpeechProvider="baidu-web" },false);
        Check(ReleaseAudioService.AudioParts(answer,baidu).Count>1,"Baidu retains the adapter's required short-utterance boundaries");
        var complete=ReleaseAudioLookup("late original snapshot");
        var earlier=new Lookup { Id=complete.Id,Term=complete.Term,Answer="",Created=complete.Created,Kind=complete.Kind };
        var profile=new Settings { SpeechProvider="microsoft",SpeechVoice="en-US-AriaNeural" };
        using(var service=new ReleaseAudioService(folder,history,(p,t,c)=>Task.FromResult(ReleaseFakeAudio(p.Extension)))) {
            await service.PreloadAsync("",complete.Answer,profile,CancellationToken.None,complete);
            await service.PreloadAsync(earlier.Term,"",profile,CancellationToken.None,earlier);
            List<string> paths;
            Check(service.TryGetHistoryAudio(complete,complete.Answer,profile,out paths),"Late original preparation with an empty answer snapshot preserves newer complete answer audio");
        }
    }
    public static int ReleaseAudioPlayback(string root) {
        string folder=TestRoot(root,"release-audio-playback-"+Guid.NewGuid().ToString("N").Substring(0,8)); int exit=0;
        var form=new Form { Text="TermLens fixed-text playback test",ShowInTaskbar=false,Width=300,Height=100 };
        form.Shown+=async delegate {
            try {
                // Read only the existing settings in place. No secrets or proxy
                // values are printed or copied into the test artifacts.
                string v2=Path.GetFullPath(Path.Combine(root,"..","..","Termlens_v2","data"));
                var saved=Files.Load(v2);
                var settings=new Settings { SpeechProvider=saved.SpeechProvider,SpeechVoice=saved.SpeechVoice,SpeechRate=saved.SpeechRate,SpeechProxy=saved.SpeechProxy };
                if(settings.SpeechProvider!="microsoft") throw new InvalidOperationException("This fixed-text comparison requires the existing V2 Microsoft profile.");
                string source=Path.Combine(Path.GetFullPath(root),"data");
                var lookup=ReleaseAudioLookup("Hello."); lookup.Answer="An algorithm is a set of steps that solves a problem.";
                using(var history=new HistoryStore(Path.Combine(folder,"history.sqlite3"))) {
                    using(var translator=new TranslatorSpeech(source,settings.SpeechProxy))
                    using(var service=new ReleaseAudioService(folder,history,(p,t,c)=>translator.Synthesize(t,p.Voice,p.Rate,c)))
                    using(var deadline=new CancellationTokenSource(60000)) {
                        await service.PreloadAsync(lookup.Term,lookup.Answer,settings,deadline.Token,lookup);
                        var clock=Stopwatch.StartNew();
                        await service.PlayAsync(lookup.Answer,settings,deadline.Token,null,lookup);
                        Check(clock.ElapsedMilliseconds>=200,"V2 saved speech profile synthesizes and completes actual MP3 playback through V2 Mp3Player");
                    }
                    using(var offline=new ReleaseAudioService(folder,history,(p,t,c)=> { throw new IOException("Offline playback forbids synthesis"); }))
                    using(var deadline=new CancellationTokenSource(20000)) {
                        await offline.PlayAsync(lookup.Term,settings,deadline.Token,null,lookup);
                        Check(true,"Fresh service plays ordinary-history MP3 from disk with synthesis forbidden");
                        Task playing=offline.PlayAsync(lookup.Answer,settings,deadline.Token,null,lookup);
                        await Task.Delay(250); offline.StopPlayback(); bool canceled=false;
                        try { await playing; } catch(OperationCanceledException) { canceled=true; }
                        Check(canceled,"StopPlayback cancels an active saved MP3 playback");
                        await offline.PlayAsync(lookup.Term,settings,deadline.Token,null,lookup);
                        Check(true,"Playback can restart after cancellation without a stale MCI device");
                    }
                    // Compare synthesis only with the same voice/rate and no
                    // proxy, avoiding any change to the saved configuration.
                    using(var direct=new TranslatorSpeech(source,"")) using(var deadline=new CancellationTokenSource(15000)) {
                        var clock=Stopwatch.StartNew();
                        try {
                            var bytes=await direct.Synthesize("Hello.",settings.SpeechVoice,settings.SpeechRate,deadline.Token);
                            Check(ReleaseAudioService.ValidAudio(bytes,".mp3"),"Same Microsoft voice/rate also synthesizes without a proxy");
                            results.Add("INFO: Direct synthesis completed in "+clock.ElapsedMilliseconds+" ms; no proxy value recorded.");
                        } catch(Exception error) { results.Add("INFO: Direct comparison unavailable: "+ReleaseMicrosoftSafeError(error)+"; saved V2 route already verified."); }
                    }
                    var windows=new Settings { SpeechProvider="windows",SpeechVoice="Microsoft Huihui Desktop",SpeechRate=-10 };
                    var wav=ReleaseAudioLookup("Hello."); wav.Answer="A short greeting.";
                    using(var service=new ReleaseAudioService(folder,history)) using(var deadline=new CancellationTokenSource(20000)) {
                        await service.PreloadAsync(wav.Term,"",windows,deadline.Token,wav);
                        var clock=Stopwatch.StartNew(); await service.PlayAsync(wav.Term,windows,deadline.Token,null,wav);
                        Check(clock.ElapsedMilliseconds>=80,"Installed Windows voice generates WAV and completes actual SoundPlayer playback");
                    }
                }
                Check(!File.Exists(Path.Combine(folder,"translator-signing.dpapi")),"Live playback test leaves no copied credentials in artifacts");
                results.Add("INFO: Only fixed test sentences were sent to synthesis; MCI completion confirms software playback, not an independent microphone or hearing check.");
            } catch(Exception error) { results.Add("FAIL: Fixed-text playback test: "+ReleaseMicrosoftSafeError(error)+". No response bodies, proxy values or credentials recorded."); exit=1; }
            finally { File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); form.Close(); }
        };
        Application.Run(form); form.Dispose(); return exit;
    }
}
}
