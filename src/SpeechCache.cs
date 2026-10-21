using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TermLens {
// Memory only: bounded LRU audio plus a single worker shared by prefetch and playback.
public sealed class SpeechCache : IDisposable {
    sealed class Cached { public byte[] Bytes; public DateTime Used,Expires; }
    sealed class Job {
        public string Key,Text,Voice;
        public int Rate,Waiters,Priority;
        public bool Foreground;
        public readonly CancellationTokenSource Cancel=new CancellationTokenSource();
        public readonly TaskCompletionSource<byte[]> Done=new TaskCompletionSource<byte[]>();
        public Job() { Done.Task.ContinueWith(t=>{ var observed=t.Exception; },TaskContinuationOptions.OnlyOnFaulted); }
    }
    readonly object sync=new object();
    readonly Dictionary<string,Cached> cache=new Dictionary<string,Cached>();
    readonly Dictionary<string,Job> pending=new Dictionary<string,Job>();
    readonly LinkedList<Job> queue=new LinkedList<Job>();
    readonly Func<string,string,int,CancellationToken,Task<byte[]>> fetch;
    readonly Action disposeService;
    readonly int maxItems; readonly long maxBytes;
    readonly TimeSpan lifetime;
    readonly int delayMs;
    long bytes;
    bool disposed,worker,serviceDisposed;
    DateTime backoff;
    Job active;
    public SpeechCache(Func<string,string,int,CancellationToken,Task<byte[]>> fetch,Action disposeService=null,int maxItems=128,long maxBytes=16*1024*1024,int delayMs=180,TimeSpan? lifetime=null) {
        this.fetch=fetch; this.disposeService=disposeService; this.maxItems=maxItems; this.maxBytes=maxBytes; this.delayMs=delayMs; this.lifetime=lifetime??TimeSpan.FromMinutes(30);
    }
    static string Key(string text,string voice,int rate) { return voice+"\n"+rate+"\n"+text.Trim(); }
    public long CachedBytes { get { lock(sync) return bytes; } }
    public int CachedCount { get { lock(sync) return cache.Count; } }
    void Remove(string key) { bytes-=cache[key].Bytes.Length; cache.Remove(key); }
    void Trim() {
        foreach(var key in cache.Where(p=>p.Value.Expires<=DateTime.UtcNow).Select(p=>p.Key).ToArray()) Remove(key);
        while(cache.Count>maxItems || bytes>maxBytes) Remove(cache.OrderBy(p=>p.Value.Used).First().Key);
    }
    public bool Contains(string text,string voice,int rate) { lock(sync) { Trim(); return cache.ContainsKey(Key(text,voice,rate)); } }
    void CancelJob(Job job) {
        Job found; if(pending.TryGetValue(job.Key,out found) && found==job) pending.Remove(job.Key);
        queue.Remove(job); job.Cancel.Cancel(); job.Done.TrySetCanceled();
        if(job!=active) job.Cancel.Dispose();
    }
    public void CancelPreloads() { lock(sync) foreach(var job in pending.Values.Where(j=>!j.Foreground).ToArray()) CancelJob(job); }
    public void Clear() {
        lock(sync) { cache.Clear(); bytes=0; foreach(var job in pending.Values.ToArray()) CancelJob(job); }
    }
    public Task<byte[]> PreloadAsync(string text,string voice,int rate) { return Request(text,voice,rate,false,CancellationToken.None); }
    public Task<byte[]> GetAsync(string text,string voice,int rate,CancellationToken token) { return Request(text,voice,rate,true,token); }
    // Release: answer prefetch=0, original prefetch=1, explicit playback=2.
    // A promotion shares the pending request; cancellation belongs to each
    // waiter so stopping playback cannot discard another live consumer.
    public Task<byte[]> GetPrioritizedAsync(string text,string voice,int rate,CancellationToken token,int priority) {
        priority=Math.Max(0,Math.Min(2,priority));
        return Request(text,voice,rate,priority>0,token,priority,true);
    }
    void Enqueue(Job job) {
        var next=queue.First;
        while(next!=null && next.Value.Priority>=job.Priority) next=next.Next;
        if(next==null) queue.AddLast(job); else queue.AddBefore(next,job);
    }
    Task<byte[]> Request(string text,string voice,int rate,bool foreground,CancellationToken token,int priority=-1,bool tracked=false) {
        if(priority<0) priority=foreground?2:0;
        token.ThrowIfCancellationRequested(); text=(text??"").Trim();
        if(text.Length==0) return Task.FromResult(new byte[0]);
        Job job;
        lock(sync) {
            if(disposed) throw new ObjectDisposedException("SpeechCache");
            Trim(); string key=Key(text,voice,rate); Cached stored;
            if(cache.TryGetValue(key,out stored)) { stored.Used=DateTime.UtcNow; return Task.FromResult(stored.Bytes); }
            if(!foreground && DateTime.UtcNow<backoff) return Task.FromResult(new byte[0]);
            if(!pending.TryGetValue(key,out job)) {
                if(!foreground && queue.Count(j=>!j.Foreground)>=4) CancelJob(queue.First(j=>!j.Foreground));
                job=new Job { Key=key,Text=text,Voice=voice,Rate=rate,Foreground=foreground,Priority=priority }; pending.Add(key,job);
                Enqueue(job);
            } else if(foreground) {
                job.Foreground=true;
                job.Priority=Math.Max(job.Priority,priority);
                if(queue.Remove(job)) Enqueue(job);
            }
            if(foreground || tracked) {
                job.Waiters++;
                if(active!=null && active!=job && active.Priority<priority) CancelJob(active);
            }
            if(!worker) { worker=true; Task.Run((Func<Task>)Run); }
        }
        return foreground || tracked?Wait(job,token):job.Done.Task;
    }
    async Task<byte[]> Wait(Job job,CancellationToken token) {
        try {
            var canceled=new TaskCompletionSource<bool>();
            using(token.Register(delegate { canceled.TrySetResult(true); })) {
                if(await Task.WhenAny(job.Done.Task,canceled.Task).ConfigureAwait(false)!=job.Done.Task) token.ThrowIfCancellationRequested();
                token.ThrowIfCancellationRequested(); return await job.Done.Task.ConfigureAwait(false);
            }
        } finally {
            lock(sync) {
                job.Waiters--;
                Job found;
                if(job.Waiters==0 && !job.Done.Task.IsCompleted && pending.TryGetValue(job.Key,out found) && found==job) CancelJob(job);
            }
        }
    }
    async Task Run() {
        while(true) {
            Job job;
            lock(sync) {
                if(queue.Count==0 || disposed) { active=null; worker=false; DisposeServiceIfReady(); return; }
                job=queue.First.Value; queue.RemoveFirst(); active=job;
            }
            try {
                if(!job.Foreground && delayMs>0) await Task.Delay(delayMs,job.Cancel.Token).ConfigureAwait(false);
                job.Cancel.Token.ThrowIfCancellationRequested();
                byte[] data=await fetch(job.Text,job.Voice,job.Rate,job.Cancel.Token).ConfigureAwait(false);
                job.Cancel.Token.ThrowIfCancellationRequested();
                lock(sync) {
                    if(!disposed && !job.Cancel.IsCancellationRequested) {
                        if(data.Length>0 && data.Length<=maxBytes) {
                            if(cache.ContainsKey(job.Key)) Remove(job.Key);
                            cache[job.Key]=new Cached { Bytes=data,Used=DateTime.UtcNow,Expires=DateTime.UtcNow+lifetime }; bytes+=data.Length; Trim();
                        }
                        job.Done.TrySetResult(data);
                    } else job.Done.TrySetCanceled();
                }
            } catch(OperationCanceledException) { job.Done.TrySetCanceled(); }
            catch(Exception e) {
                lock(sync) {
                    backoff=DateTime.UtcNow.AddSeconds(20);
                    foreach(var waiting in queue.Where(j=>!j.Foreground).ToArray()) CancelJob(waiting);
                }
                job.Done.TrySetException(e);
            } finally {
                lock(sync) { Job found; if(pending.TryGetValue(job.Key,out found) && found==job) pending.Remove(job.Key); active=null; job.Cancel.Dispose(); }
            }
        }
    }
    void DisposeServiceIfReady() { if(disposed && !worker && !serviceDisposed) { serviceDisposed=true; if(disposeService!=null) disposeService(); } }
    public void Dispose() {
        lock(sync) { if(disposed) return; disposed=true; cache.Clear(); bytes=0; foreach(var job in pending.Values.ToArray()) CancelJob(job); DisposeServiceIfReady(); }
    }
}
}
