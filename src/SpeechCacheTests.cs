using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TermLens {
public static partial class Tests {
    static async Task CacheChecks() {
        int calls=0,inFlight=0,peak=0;
        Func<string,string,int,CancellationToken,Task<byte[]>> fetch=async delegate(string text,string voice,int rate,CancellationToken token) {
            Interlocked.Increment(ref calls); int count=Interlocked.Increment(ref inFlight); peak=Math.Max(peak,count);
            try { await Task.Delay(text=="slow"?2000:50,token).ConfigureAwait(false); return new byte[400]; }
            finally { Interlocked.Decrement(ref inFlight); }
        };
        using(var cache=new SpeechCache(fetch,null,4,1200,0)) {
            Task<byte[]> preload=cache.PreloadAsync("perpendicular","voice-a",0);
            byte[] audio=await cache.GetAsync("perpendicular","voice-a",0,CancellationToken.None); await preload;
            Check(calls==1 && audio.Length==400,"Clicking during prefetch reuses the in-flight request");
            await cache.GetAsync(" perpendicular ","voice-a",0,CancellationToken.None);
            Check(calls==1,"Repeated listening uses cached audio without a network request");
            await cache.GetAsync("perpendicular","voice-b",0,CancellationToken.None);
            await cache.GetAsync("perpendicular","voice-b",10,CancellationToken.None);
            Check(calls==3,"Different voice and speed use distinct cached audio");
            await cache.GetAsync("next","voice-b",10,CancellationToken.None);
            Check(cache.CachedBytes<=1200 && cache.CachedCount==3 && !cache.Contains("perpendicular","voice-a",0),"Least-recently-used audio is evicted within the memory budget");
            var cancel=new CancellationTokenSource();
            var one=cache.GetAsync("shared","voice-a",0,cancel.Token);
            var two=cache.GetAsync("shared","voice-a",0,CancellationToken.None); cancel.Cancel();
            try { await one; throw new Exception("Cancellation ignored"); } catch(OperationCanceledException) { }
            await two;
            Check(cache.Contains("shared","voice-a",0),"One canceled listener does not cancel another listener of the same clip");
            var background=cache.PreloadAsync("slow","voice-a",0); await Task.Delay(40);
            var watch=Stopwatch.StartNew(); await cache.GetAsync("urgent","voice-a",0,CancellationToken.None);
            try { await background; throw new Exception("Background was not preempted"); } catch(OperationCanceledException) { }
            Check(watch.ElapsedMilliseconds<1000 && peak==1,"Clicked audio preempts slow prefetch while keeping one network request at a time");
            var stopped=cache.PreloadAsync("slow","voice-a",0); await Task.Delay(20); cache.CancelPreloads();
            try { await stopped; throw new Exception("Preload cancel ignored"); } catch(OperationCanceledException) { }
            Check(!cache.Contains("slow","voice-a",0),"Superseded prefetch is canceled and not cached");
            cache.Clear(); Check(cache.CachedBytes==0 && cache.CachedCount==0,"Clear cache releases all retained clips");
        }
        using(var cache=new SpeechCache(fetch,null,10,16000,0,TimeSpan.FromMilliseconds(20))) {
            await cache.GetAsync("expire","voice-a",0,CancellationToken.None); int before=calls;
            await Task.Delay(35); await cache.GetAsync("expire","voice-a",0,CancellationToken.None);
            Check(calls==before+1,"Expired clips are fetched again");
        }
        int errors=0;
        using(var cache=new SpeechCache(delegate(string t,string v,int r,CancellationToken token) { errors++; var failed=new TaskCompletionSource<byte[]>(); failed.SetException(new Exception("Unavailable")); return failed.Task; },null,10,16000,0)) {
            try { await cache.PreloadAsync("first","voice",0); } catch(Exception) { }
            await cache.PreloadAsync("second","voice",0);
            Check(errors==1,"Service failure pauses background prefetch instead of retrying continuously");
            var deferred=await cache.GetPrioritizedAsync("deferred","voice",0,CancellationToken.None,0);
            Check(errors==1 && deferred.Length==0,"Release answer prefetch respects the same service-error backoff");
            try { await cache.GetAsync("second","voice",0,CancellationToken.None); } catch(Exception) { }
            Check(errors==2,"Explicit playback can retry during background backoff");
        }
        using(var cache=new SpeechCache(fetch,null,10,16000,100)) {
            var jobs=new List<Task<byte[]>>(); for(int i=0;i<20;i++) jobs.Add(cache.PreloadAsync("burst-"+i,"v",0));
            int done=0; foreach(var job in jobs) { try { await job; done++; } catch(OperationCanceledException) { } }
            Check(done<=5 && done>0,"Rapid requests keep at most one active and four queued prefetches");
        }
        var started=new TaskCompletionSource<bool>();
        int priorityCalls=0;
        using(var cache=new SpeechCache(async (text,voice,rate,token)=> {
            Interlocked.Increment(ref priorityCalls);
            if(text=="answer") { started.TrySetResult(true); await Task.Delay(2000,token); }
            else await Task.Delay(40,token);
            return new byte[400];
        },null,10,16000,0)) {
            var answer=cache.GetPrioritizedAsync("answer","voice",0,CancellationToken.None,0);
            await started.Task;
            var clock=Stopwatch.StartNew();
            await cache.GetPrioritizedAsync("original","voice",0,CancellationToken.None,1);
            bool preempted=false; try { await answer; } catch(OperationCanceledException) { preempted=true; }
            Check(preempted && clock.ElapsedMilliseconds<1000,"Original prewarming preempts a slow answer prefetch");
            int before=priorityCalls;
            using(var canceled=new CancellationTokenSource()) {
                var preload=cache.GetPrioritizedAsync("shared-priority","voice",0,CancellationToken.None,1);
                var play=cache.GetPrioritizedAsync("shared-priority","voice",0,canceled.Token,2);
                canceled.Cancel(); try { await play; } catch(OperationCanceledException) { }
                await preload;
                Check(priorityCalls==before+1 && cache.Contains("shared-priority","voice",0),"Playback promotes the original's existing request and stopping it preserves the prefetch consumer");
            }
            using(var canceled=new CancellationTokenSource()) {
                var prefetch=cache.GetPrioritizedAsync("answer","voice",0,canceled.Token,0);
                canceled.Cancel(); bool observed=false; try { await prefetch; } catch(OperationCanceledException) { observed=true; }
                Check(observed,"Background prefetch observes lookup cancellation");
            }
        }
    }
    public static int SpeechCaching(string root,bool live) {
        string folder=TestRoot(root,live?"speech-cache-live":"speech-cache");
        try {
            if(!live) CacheChecks().GetAwaiter().GetResult();
            else {
                var config=Files.Load(Path.Combine(root,"data"));
                using(var service=new TranslatorSpeech(Path.Combine(root,"data"),config.SpeechProxy))
                using(var cache=new SpeechCache(service.Synthesize,null)) {
                    var clock=Stopwatch.StartNew();
                    byte[] first=cache.PreloadAsync("perpendicular",config.SpeechVoice,config.SpeechRate).GetAwaiter().GetResult();
                    long cold=clock.ElapsedMilliseconds; clock.Restart();
                    byte[] hit=cache.GetAsync("perpendicular",config.SpeechVoice,config.SpeechRate,CancellationToken.None).GetAwaiter().GetResult();
                    long warm=clock.ElapsedMilliseconds;
                    Check(Object.ReferenceEquals(first,hit) && hit.Length>1000,"Live Microsoft audio is reused directly from memory");
                    string path=Path.Combine(folder,"perpendicular.mp3"); File.WriteAllBytes(path,hit);
                    results.Add("Fixed word: "+hit.Length+" bytes; prepare "+cold+" ms; cached retrieval "+warm+" ms. Playback-device start time is separate.");
                    CheckPlayback(path);
                }
            }
            File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); return 0;
        } catch(Exception e) { results.Add("FAIL: "+e.GetType().Name+": "+e.Message); File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); return 1; }
    }
}
}
