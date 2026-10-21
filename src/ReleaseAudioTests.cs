using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TermLens {
public static partial class Tests {
    static byte[] ReleaseFakeAudio(string extension) {
        var bytes=new byte[320];
        var header=Encoding.ASCII.GetBytes(extension==".wav"?"RIFF":"ID3"); Array.Copy(header,bytes,header.Length);
        if(extension==".wav") Array.Copy(Encoding.ASCII.GetBytes("WAVE"),0,bytes,8,4);
        return bytes;
    }
    static Lookup ReleaseAudioLookup(string term) { return new Lookup { Id=Guid.NewGuid().ToString("N"),Kind="explain",Term=term,Answer="A short **meaning**.",Created=DateTime.UtcNow.ToString("o"),Status="complete" }; }
    static async Task ReleaseAudioDeadline(Task task) {
        if(await Task.WhenAny(task,Task.Delay(5000))!=task) throw new Exception("Release audio fixture timed out.");
        await task;
    }
    public static int ReleaseAudio(string root) { return ReleaseAudioHarness(root,false); }
    public static int ReleaseAudioWindows(string root) { return ReleaseAudioHarness(root,true); }
    static string ReleaseMicrosoftSafeError(Exception error) {
        // Never log the response body, headers, signing material or bearer token.
        string kind=error.GetType().Name;
        var status=System.Text.RegularExpressions.Regex.Match(error.Message??"",@"\bHTTP\s+([1-5][0-9]{2})\b");
        var mci=System.Text.RegularExpressions.Regex.Match(error.Message??"",@"Windows WAV (open|play|status) failed \(MCI [0-9]+\)");
        return kind+(status.Success?"; HTTP "+status.Groups[1].Value:"")+(mci.Success?"; "+mci.Value:"");
    }
    public static int ReleaseAudioMicrosoft(string root) {
        string folder=TestRoot(root,"release-audio-microsoft-live-"+Guid.NewGuid().ToString("N").Substring(0,8)); int exit=0;
        var form=new Form { Text="TermLens fixed-text Microsoft audio test",ShowInTaskbar=false,Width=300,Height=100 };
        form.Shown+=async delegate {
            try {
                string source=Path.Combine(Path.GetFullPath(root),"data");
                if(!File.Exists(Path.Combine(source,"translator-signing.dpapi"))) {
                    results.Add("FAIL: Microsoft live test requires the existing local translator-signing.dpapi configuration; it is missing."); exit=1; return;
                }
                var settings=new Settings { SpeechProvider="microsoft",SpeechVoice="en-US-AriaNeural",SpeechRate=0,SpeechProxy="",SpeechPreload=true };
                var lookup=new Lookup { Id=Guid.NewGuid().ToString("N"),Kind="explain",Term="Hello.",Answer="A short greeting.",Created=DateTime.UtcNow.ToString("o"),Status="complete" };
                using(var history=new HistoryStore(Path.Combine(folder,"history.sqlite3"))) {
                    history.RecordQuery(lookup.Id,lookup.Kind,"fixed-audio-test",lookup.Term,lookup.Answer,"","{}","","",lookup.Created,"complete","");
                    var favorite=history.SetFavorite(lookup,true);
                    // Use the existing configuration in place; never copy it into
                    // test artifacts. Only this fixed text is sent online.
                    using(var translator=new TranslatorSpeech(source,settings.SpeechProxy))
                    using(var service=new ReleaseAudioService(folder,history,async (profile,text,token)=> {
                        try { return await translator.Synthesize(text,profile.Voice,profile.Rate,token).ConfigureAwait(false); }
                        catch(OperationCanceledException) { throw new OperationCanceledException("Microsoft fixed-text synthesis canceled."); }
                        catch(Exception error) { throw new IOException("Microsoft fixed-text synthesis failed: "+ReleaseMicrosoftSafeError(error)); }
                    })) {
                        await service.PreloadAsync(lookup.Term,lookup.Answer,settings,CancellationToken.None);
                        await service.PrepareFavoriteAsync(favorite,settings);
                        favorite=history.FindFavorite(lookup.Kind,lookup.Term);
                        Check(favorite.AudioState=="ready" && favorite.WordAudio.Count>0 && favorite.ExplanationAudio.Count>0,"Microsoft fixed-text original and explanation both persist as ready audio");
                        Check(favorite.WordAudio.Concat(favorite.ExplanationAudio).All(p=>p.EndsWith(".mp3",StringComparison.OrdinalIgnoreCase) && ReleaseAudioService.ValidAudio(File.ReadAllBytes(Path.Combine(folder,p)),".mp3")),"Both saved Microsoft audio groups have nonempty MP3 payloads");
                        var manifest=Files.Json().Deserialize<ReleaseAudioManifest>(File.ReadAllText(Path.Combine(folder,"favorites",favorite.Id,"manifest.json"),Files.Utf8));
                        Check(manifest.Provider=="microsoft" && manifest.Voice=="en-US-AriaNeural" && manifest.Word.Count==favorite.WordAudio.Count && manifest.Explanation.Count==favorite.ExplanationAudio.Count,"Microsoft manifest preserves provider, voice and both complete audio groups");
                    }
                    int forbiddenFetches=0;
                    using(var offline=new ReleaseAudioService(folder,history,(profile,text,token)=> { forbiddenFetches++; throw new IOException("Offline verification forbids synthesis."); })) {
                        List<string> original,explanation;
                        Check(offline.TryGetFavoriteAudio(favorite,lookup.Term,settings,out original) && original.Count>0,"Fresh offline service verifies original file lengths and SHA256 hashes");
                        Check(offline.TryGetFavoriteAudio(favorite,lookup.Answer,settings,out explanation) && explanation.Count>0,"Fresh offline service verifies explanation file lengths and SHA256 hashes");
                        await offline.PrepareFavoriteAsync(favorite,settings);
                        Check(forbiddenFetches==0,"Microsoft favorite can be reused from disk without online synthesis");
                    }
                }
                Check(!File.Exists(Path.Combine(folder,"translator-signing.dpapi")),"Live test leaves no copied signing configuration in its output folder");
                results.Add("INFO: Synthesized only Hello. and A short greeting. with the experimental Microsoft provider. No audio playback or user selection was used.");
            } catch(Exception error) { results.Add("FAIL: Microsoft fixed-text audio test failed ("+ReleaseMicrosoftSafeError(error)+"). No response or credential details recorded."); exit=1; }
            finally { File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); form.Close(); }
        };
        Application.Run(form); form.Dispose(); return exit;
    }
    static int ReleaseAudioHarness(string root,bool localWindows) {
        string folder=TestRoot(root,localWindows?"release-audio-windows":"release-audio"); int exit=0;
        var form=new Form { Text="TermLens isolated audio test",ShowInTaskbar=false,Width=300,Height=100 };
        form.Shown+=async delegate {
            try {
                using(var history=new HistoryStore(Path.Combine(folder,"history.sqlite3"))) {
                    if(localWindows) {
                        var settings=new Settings { SpeechProvider="windows",SpeechVoice="",SpeechRate=0 };
                        var favorite=history.SetFavorite(ReleaseAudioLookup("Hello"),true);
                        using(var service=new ReleaseAudioService(folder,history)) {
                            await ReleaseAudioDeadline(service.PrepareFavoriteAsync(favorite,settings));
                            var stored=history.FindFavorite("explain","Hello"); List<string> paths;
                            Check(stored.AudioState=="ready" && service.TryGetFavoriteAudio(stored,"Hello",settings,out paths),"Installed Windows voice generates and persists original WAV");
                            Check(stored.WordAudio.Concat(stored.ExplanationAudio).All(p=>p.EndsWith(".wav") && ReleaseAudioService.ValidAudio(File.ReadAllBytes(Path.Combine(folder,p)),".wav")),"Both Windows original and explanation files have WAV headers");
                            using(var deadline=new CancellationTokenSource(20000)) {
                                var clock=System.Diagnostics.Stopwatch.StartNew();
                                await service.PlayAsync("Hello",settings,deadline.Token,stored);
                                Check(clock.ElapsedMilliseconds>=80,"Long-path cached Windows WAV completes actual SoundPlayer playback");
                                Task playing=service.PlayAsync(stored.Explanation,settings,deadline.Token,stored);
                                await Task.Delay(200); service.StopPlayback(); bool canceled=false;
                                try { await playing; } catch(OperationCanceledException) { canceled=true; }
                                Check(canceled,"StopPlayback cancels active cached Windows WAV playback");
                                await service.PlayAsync("Hello",settings,deadline.Token,stored);
                                Check(true,"Windows WAV playback restarts after cancellation");
                            }
                        }
                        results.Add("INFO: Installed Windows fixed-text speech synthesis and actual cached WAV playback exercised; no network.");
                    } else {
                        await ReleaseAudioInjectedChecks(folder,history);
                        await ReleaseHistoryAudioInjectedChecks(folder,history);
                        results.Add("INFO: All synthesis was injected. No network, credentials, installed voices, or playback were used.");
                    }
                }
            } catch(Exception error) { results.Add("FAIL: "+error); exit=1; }
            finally { File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); form.Close(); }
        };
        Application.Run(form); form.Dispose(); return exit;
    }
    static async Task ReleaseAudioInjectedChecks(string folder,HistoryStore history) {
        var settings=new Settings { SpeechProvider="microsoft",SpeechVoice="en-US-AriaNeural",SpeechRate=0,SpeechProxy="http://fixture-only:fake-password@localhost:9999" };
        var lookup=ReleaseAudioLookup("sample"); var favorite=history.SetFavorite(lookup,true);
        int calls=0,ownerThread=Thread.CurrentThread.ManagedThreadId,statusCount=0; bool wrongThread=false;
        using(var service=new ReleaseAudioService(folder,history,(profile,text,token)=> { Interlocked.Increment(ref calls); return Task.FromResult(ReleaseFakeAudio(profile.Extension)); })) {
            service.FavoriteAudioChanged+=delegate { statusCount++; if(Thread.CurrentThread.ManagedThreadId!=ownerThread) wrongThread=true; };
            await ReleaseAudioDeadline(service.PreloadAsync(lookup.Term,lookup.Answer,settings,CancellationToken.None)); int afterPreload=calls;
            await ReleaseAudioDeadline(service.PrepareFavoriteAsync(favorite,settings));
            Check(afterPreload==2 && calls==afterPreload,"Favorite persistence reuses the original and explanation preloaded bytes");
            favorite=history.FindFavorite(lookup.Kind,lookup.Term); List<string> paths;
            Check(favorite.AudioState=="ready" && favorite.WordAudio.Count==1 && favorite.ExplanationAudio.Count==1,"Ready favorite stores both original and explanation audio paths");
            Check(statusCount>=2 && !wrongThread,"Favorite state callbacks execute on captured Windows Forms context");
            Check(service.TryGetFavoriteAudio(favorite,lookup.Term,settings,out paths) && paths.Count==1,"Manifest resolves original audio offline");
            Check(!service.TryGetFavoriteAudio(favorite,"sam",settings,out paths),"Partial selection does not incorrectly reuse full original audio");
            string manifest=File.ReadAllText(Path.Combine(folder,"favorites",favorite.Id,"manifest.json"),Files.Utf8);
            Check(!manifest.Contains("fake-password") && !manifest.Contains("localhost") && !manifest.Contains("SpeechProxy"),"Manifest omits proxy credentials and connection settings");
            settings.SpeechProxy="http://localhost:9998";
            await ReleaseAudioDeadline(service.PreloadAsync(lookup.Term,"",settings,CancellationToken.None));
            Check(calls==afterPreload,"Ignored legacy proxy changes reuse the direct-connection audio cache");
            settings.SpeechRate=10; Check(!service.TryGetFavoriteAudio(favorite,lookup.Term,settings,out paths),"Saved audio requires matching speech rate"); settings.SpeechRate=0;
            settings.SpeechVoice="en-US-AvaNeural"; Check(!service.TryGetFavoriteAudio(favorite,lookup.Term,settings,out paths),"Saved audio requires matching voice"); settings.SpeechVoice="en-US-AriaNeural";
        }
        int offlineCalls=0;
        using(var service=new ReleaseAudioService(folder,history,(profile,text,token)=> { offlineCalls++; throw new IOException("Fixture is offline"); })) {
            await ReleaseAudioDeadline(service.PrepareFavoriteAsync(favorite,settings));
            Check(offlineCalls==0,"A fresh service reuses a valid on-disk manifest without synthesis");
            List<string> paths; Check(service.TryGetFavoriteAudio(favorite,lookup.Term,settings,out paths),"Offline manifest remains usable after process cache reset");
            File.WriteAllBytes(paths[0],new byte[]{1,2,3});
            Check(!service.TryGetFavoriteAudio(favorite,lookup.Term,settings,out paths),"Corrupted saved audio is rejected by length/hash validation");
        }
        using(var repair=new ReleaseAudioService(folder,history,(profile,text,token)=>Task.FromResult(ReleaseFakeAudio(profile.Extension)))) {
            await ReleaseAudioDeadline(repair.PrepareFavoriteAsync(favorite,settings)); List<string> repaired;
            Check(repair.TryGetFavoriteAudio(favorite,lookup.Term,settings,out repaired),"Regeneration atomically repairs a corrupted content-addressed audio file");
        }
        var waveSettings=new Settings { SpeechProvider="windows",SpeechVoice="Fixture voice",SpeechRate=0 };
        var wave=history.SetFavorite(ReleaseAudioLookup("wave"),true);
        using(var service=new ReleaseAudioService(folder,history,(profile,text,token)=>Task.FromResult(ReleaseFakeAudio(profile.Extension)))) {
            await ReleaseAudioDeadline(service.PrepareFavoriteAsync(wave,waveSettings));
            wave=history.FindFavorite("explain","wave"); Check(wave.WordAudio.All(p=>p.EndsWith(".wav")),"Windows profile persists WAV assets rather than MP3 extensions");
        }
        var staleLookup=ReleaseAudioLookup("late"); var stale=history.SetFavorite(staleLookup,true);
        var started=new TaskCompletionSource<bool>(); var release=new TaskCompletionSource<bool>();
        using(var service=new ReleaseAudioService(folder,history,async (profile,text,token)=> { started.TrySetResult(true); await release.Task; return ReleaseFakeAudio(profile.Extension); })) {
            Task old=service.PrepareFavoriteAsync(stale,settings); await ReleaseAudioDeadline(started.Task);
            history.SetFavorite(staleLookup,false); service.CancelFavorite(stale.Id); release.TrySetResult(true); await ReleaseAudioDeadline(old);
            Check(!history.FindFavorite(staleLookup.Kind,staleLookup.Term).Saved,"Unsave cannot be reversed by late synthesis completion");
            Check(!File.Exists(Path.Combine(folder,"favorites",stale.Id,"manifest.json")),"Canceled favorite cannot publish a late ready manifest");
        }
        var revisedLookup=ReleaseAudioLookup("revision"); var revised=history.SetFavorite(revisedLookup,true);
        started=new TaskCompletionSource<bool>(); release=new TaskCompletionSource<bool>();
        using(var service=new ReleaseAudioService(folder,history,async (profile,text,token)=> { started.TrySetResult(true); await release.Task; return ReleaseFakeAudio(profile.Extension); })) {
            Task old=service.PrepareFavoriteAsync(revised,settings); await ReleaseAudioDeadline(started.Task);
            history.SetFavorite(revisedLookup,false); revisedLookup.Answer="The newly saved explanation.";
            var replacement=history.SetFavorite(revisedLookup,true); Task current=service.PrepareFavoriteAsync(replacement,settings);
            release.TrySetResult(true); await ReleaseAudioDeadline(Task.WhenAll(old,current));
            var latest=history.FindFavorite("explain","revision"); List<string> paths;
            Check(latest.Saved && latest.AudioState=="ready" && latest.Explanation==revisedLookup.Answer && service.TryGetFavoriteAudio(latest,revisedLookup.Answer,settings,out paths),"Unsave then resave persists only the new favorite revision");
            Check(!service.TryGetFavoriteAudio(latest,revised.Explanation,settings,out paths),"Stale explanation audio cannot replace the new manifest");
        }
        var sharedLookup=ReleaseAudioLookup("shared"); var shared=history.SetFavorite(sharedLookup,true);
        started=new TaskCompletionSource<bool>(); release=new TaskCompletionSource<bool>();
        using(var service=new ReleaseAudioService(folder,history,async (profile,text,token)=> { started.TrySetResult(true); await release.Task; return ReleaseFakeAudio(profile.Extension); }))
        using(var windowLifetime=new CancellationTokenSource()) {
            Task saved=service.PrepareFavoriteAsync(shared,settings); await ReleaseAudioDeadline(started.Task);
            Task preload=service.PreloadAsync(sharedLookup.Term,"",settings,windowLifetime.Token);
            windowLifetime.Cancel(); release.TrySetResult(true);
            try { await preload; } catch(OperationCanceledException) { }
            await ReleaseAudioDeadline(saved);
            Check(history.FindFavorite(sharedLookup.Kind,sharedLookup.Term).AudioState=="ready","Closing the lookup cancels only its preload and preserves confirmed favorite work");
        }
        var failedLookup=ReleaseAudioLookup("broken"); var failed=history.SetFavorite(failedLookup,true);
        using(var service=new ReleaseAudioService(folder,history,(profile,text,token)=>Task.FromResult(new byte[]{1,2,3}))) {
            bool rejected=false; try { await ReleaseAudioDeadline(service.PrepareFavoriteAsync(failed,settings)); } catch(InvalidDataException) { rejected=true; }
            Check(rejected && history.FindFavorite("explain","broken").AudioState=="failed","Invalid provider bytes fail visibly and never become ready audio");
        }
        var disposedLookup=ReleaseAudioLookup("shutdown"); var disposedFavorite=history.SetFavorite(disposedLookup,true);
        started=new TaskCompletionSource<bool>(); release=new TaskCompletionSource<bool>();
        var shuttingDown=new ReleaseAudioService(folder,history,async (profile,text,token)=> { started.TrySetResult(true); await release.Task; return ReleaseFakeAudio(profile.Extension); });
        Task pending=shuttingDown.PrepareFavoriteAsync(disposedFavorite,settings); await ReleaseAudioDeadline(started.Task);
        shuttingDown.Dispose(); release.TrySetResult(true); await ReleaseAudioDeadline(pending);
        Check(history.FindFavorite("explain","shutdown").AudioState!="ready" && !File.Exists(Path.Combine(folder,"favorites",disposedFavorite.Id,"manifest.json")),"Application shutdown prevents late favorite ready commits");
    }
}
}
