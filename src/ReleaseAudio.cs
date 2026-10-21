using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Speech.Synthesis;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TermLens {
public sealed class ReleaseAudioProfile {
    public string Provider,Voice,Proxy,Extension;
    public int Rate;
    public static ReleaseAudioProfile FromSettings(Settings saved,bool resolveWindowsVoice=true) {
        if(saved==null) throw new ArgumentNullException("saved");
        var p=new ReleaseAudioProfile { Provider=(saved.SpeechProvider??"baidu-web").Trim(),Voice=(saved.SpeechVoice??"").Trim(),Proxy=saved.SpeechProxy??"",Rate=Math.Max(-50,Math.Min(50,saved.SpeechRate)) };
        p.Extension=p.Provider=="windows"?".wav":".mp3";
        if(p.Provider=="baidu-web") { p.Voice="baidu-auto-en-zh"; p.Proxy=""; }
        if(p.Provider=="microsoft" && p.Voice.Length==0) p.Voice="en-US-AriaNeural";
        if(p.Provider=="edge" && p.Voice.Length==0) p.Voice="en-US-AndrewMultilingualNeural";
        if(p.Provider=="edge" || p.Provider=="microsoft") p.Proxy="";
        if(p.Provider=="windows") {
            p.Proxy="";
            if(resolveWindowsVoice) using(var synthesizer=new SpeechSynthesizer()) {
                var voices=synthesizer.GetInstalledVoices().Where(v=>v.Enabled).ToList();
                var choice=voices.FirstOrDefault(v=>v.VoiceInfo.Name==p.Voice)
                    ??voices.FirstOrDefault(v=>v.VoiceInfo.Culture.TwoLetterISOLanguageName=="en")??voices.FirstOrDefault();
                if(choice==null) throw new InvalidOperationException("未找到可用的 Windows 系统声音。");
                p.Voice=choice.VoiceInfo.Name;
            }
        }
        return p;
    }
    public string Fingerprint(string text) { return ReleaseAudioService.Hash(Provider+"\n"+Voice+"\n"+Rate+"\n"+Extension+"\n"+ReleaseAudioService.SpeechText(text)); }
    internal string CacheIdentity { get { return ReleaseAudioService.Hash(Fingerprint("")+"\n"+Proxy); } }
}
public sealed class ReleaseAudioAsset {
    public string File,Sha256; public long Bytes;
}
public sealed class ReleaseAudioManifest {
    public int Version=1,Rate;
    public string FavoriteId,Provider,Voice,Created,OriginalFingerprint,ExplanationFingerprint;
    public List<ReleaseAudioAsset> Word=new List<ReleaseAudioAsset>(),Explanation=new List<ReleaseAudioAsset>(),Retired=new List<ReleaseAudioAsset>();
}
public sealed class ReleaseHistoryAudioManifest {
    public int Version=1,Rate;
    public string QueryId,Provider,Voice,Created,OriginalFingerprint,ExplanationFingerprint;
    public bool WordComplete,ExplanationComplete;
    public List<ReleaseAudioAsset> Word=new List<ReleaseAudioAsset>(),Explanation=new List<ReleaseAudioAsset>(),Retired=new List<ReleaseAudioAsset>();
}
// Construct on the UI thread. Favorite database checks/commits and status events
// run on that captured context. Synthesis and file IO never touch UI controls.
public sealed class ReleaseAudioService : IDisposable {
    sealed class Ticket { public Favorite Snapshot; public CancellationTokenSource Cancel; }
    readonly string root,favoritesRoot,playbackRoot,historyAudioRoot;
    readonly HistoryStore history;
    readonly SynchronizationContext owner;
    readonly object sync=new object();
    readonly object assetSync=new object();
    readonly CancellationTokenSource lifetime=new CancellationTokenSource();
    readonly Dictionary<string,ReleaseAudioProfile> profiles=new Dictionary<string,ReleaseAudioProfile>();
    readonly Dictionary<string,TranslatorSpeech> translators=new Dictionary<string,TranslatorSpeech>();
    readonly Dictionary<string,EdgeSpeech> edgeVoices=new Dictionary<string,EdgeSpeech>();
    readonly Dictionary<string,Ticket> favorites=new Dictionary<string,Ticket>();
    readonly Dictionary<string,SemaphoreSlim> historyGates=new Dictionary<string,SemaphoreSlim>();
    readonly Func<ReleaseAudioProfile,string,CancellationToken,Task<byte[]>> fetchOverride;
    readonly Func<string,CancellationToken,Task> playOverride;
    readonly SpeechCache cache;
    WebSpeech web;
    CancellationTokenSource playbackCancel;
    ReleaseAudioPlayer player;
    bool disposed;
    public event Action<Favorite> FavoriteAudioChanged;
    public ReleaseAudioService(string dataRoot,HistoryStore history,Func<ReleaseAudioProfile,string,CancellationToken,Task<byte[]>> fetchOverride=null,Func<string,CancellationToken,Task> playOverride=null) {
        root=Path.GetFullPath(dataRoot); this.history=history; this.fetchOverride=fetchOverride; this.playOverride=playOverride; owner=SynchronizationContext.Current;
        favoritesRoot=Path.Combine(root,"favorites"); playbackRoot=Path.Combine(root,"audio-playback"); historyAudioRoot=Path.Combine(root,"history-audio");
        EnsureNoLinks(root);
        cache=new SpeechCache(Fetch,DisposeProviders,maxItems:160,maxBytes:32L*1024*1024,delayMs:0);
    }
    public static string SpeechText(string text) { return Markdown.PlainText(text??"").Trim(); }
    public static string Hash(string text) { return Hash(Encoding.UTF8.GetBytes(text??"")); }
    static string Hash(byte[] bytes) { using(var hash=SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant(); }
    static Favorite Copy(Favorite item) { return Files.Json().Deserialize<Favorite>(Files.Json().Serialize(item)); }
    ReleaseAudioProfile Profile(Settings saved) {
        var profile=ReleaseAudioProfile.FromSettings(saved,fetchOverride==null);
        if(fetchOverride==null && profile.Provider!="windows" && profile.Provider!="baidu-web" && profile.Provider!="microsoft" && profile.Provider!="edge") throw new InvalidOperationException("不支持此朗读服务："+profile.Provider);
        lock(sync) { if(disposed) throw new ObjectDisposedException("ReleaseAudioService"); profiles[profile.CacheIdentity]=profile; }
        return profile;
    }
    async Task<byte[]> Fetch(string text,string identity,int ignoredRate,CancellationToken token) {
        ReleaseAudioProfile profile; lock(sync) profile=profiles[identity];
        byte[] result;
        if(fetchOverride!=null) result=await fetchOverride(profile,text,token).ConfigureAwait(false);
        else if(profile.Provider=="windows") result=await WindowsWave(text,profile,token).ConfigureAwait(false);
        else if(profile.Provider=="baidu-web") {
            if(web==null) web=new WebSpeech();
            result=await web.Synthesize(text,profile.Voice,profile.Rate,token).ConfigureAwait(false);
        } else if(profile.Provider=="edge") {
            EdgeSpeech service;
            if(!edgeVoices.TryGetValue(profile.Proxy,out service)) { service=new EdgeSpeech(profile.Proxy); edgeVoices.Add(profile.Proxy,service); }
            result=await service.Synthesize(text,profile.Voice,profile.Rate,token).ConfigureAwait(false);
        } else {
            if(!File.Exists(Path.Combine(root,"translator-signing.dpapi"))) throw new InvalidOperationException("微软在线（实验）缺少本机配置文件，不能生成音频；请配置后重试或选择其他朗读方式。");
            TranslatorSpeech service;
            if(!translators.TryGetValue(profile.Proxy,out service)) { service=new TranslatorSpeech(root,profile.Proxy); translators.Add(profile.Proxy,service); }
            result=await service.Synthesize(text,profile.Voice,profile.Rate,token).ConfigureAwait(false);
        }
        token.ThrowIfCancellationRequested();
        if(!ValidAudio(result,profile.Extension)) throw new InvalidDataException("朗读服务未返回有效音频。");
        return result;
    }
    internal static bool ValidAudio(byte[] bytes,string extension) {
        if(bytes==null) return false;
        if(extension==".wav") return bytes.Length>=44 && Encoding.ASCII.GetString(bytes,0,4)=="RIFF" && Encoding.ASCII.GetString(bytes,8,4)=="WAVE";
        return bytes.Length>256 && (bytes[0]=='I' && bytes[1]=='D' && bytes[2]=='3' || bytes[0]==255 && (bytes[1]&0xe0)==0xe0);
    }
    static Task<byte[]> WindowsWave(string text,ReleaseAudioProfile profile,CancellationToken token) {
        return Task.Run(async delegate {
            token.ThrowIfCancellationRequested();
            using(var bytes=new MemoryStream()) using(var voice=new SpeechSynthesizer()) {
                if(!String.IsNullOrEmpty(profile.Voice)) voice.SelectVoice(profile.Voice);
                voice.Rate=Math.Max(-5,Math.Min(5,profile.Rate/10)); voice.SetOutputToWaveStream(bytes);
                var completed=new TaskCompletionSource<bool>();
                voice.SpeakCompleted+=delegate(object sender,SpeakCompletedEventArgs e) {
                    if(e.Cancelled) completed.TrySetCanceled(); else if(e.Error!=null) completed.TrySetException(e.Error); else completed.TrySetResult(true);
                };
                using(token.Register(delegate { try { voice.SpeakAsyncCancelAll(); } catch(ObjectDisposedException) { } })) {
                    token.ThrowIfCancellationRequested(); voice.SpeakAsync(text); if(token.IsCancellationRequested) voice.SpeakAsyncCancelAll(); await completed.Task.ConfigureAwait(false); token.ThrowIfCancellationRequested();
                    voice.SetOutputToNull(); return bytes.ToArray();
                }
            }
        },token);
    }
    internal static List<string> AudioParts(string text,ReleaseAudioProfile profile) {
        if(profile.Provider=="baidu-web") return WebSpeech.Parts(text);
        // Microsoft and Windows accept the completed explanation as one
        // utterance. Only the Baidu adapter requires short chunks.
        return String.IsNullOrEmpty(text)?new List<string>():new List<string> { text };
    }
    async Task<byte[]> Segment(string text,ReleaseAudioProfile profile,CancellationToken token,int priority) {
        while(true) {
            bool preempted=false; byte[] bytes=null;
            try {
                bytes=await cache.GetPrioritizedAsync(text,profile.CacheIdentity,profile.Rate,token,priority).ConfigureAwait(false);
            } catch(OperationCanceledException) {
                token.ThrowIfCancellationRequested();
                if(disposed || lifetime.IsCancellationRequested) throw;
                preempted=true;
            }
            if(preempted) { await Task.Delay(30,token).ConfigureAwait(false); continue; }
            if(bytes.Length==0) throw new OperationCanceledException("Audio background preparation deferred.");
            return bytes;
        }
    }
    public Task PreloadAsync(string original,string answer,Settings saved,CancellationToken token,Lookup lookup=null) {
        return Task.Run(()=>PreloadCore(original,answer,saved,token,lookup),token);
    }
    async Task PreloadCore(string original,string answer,Settings saved,CancellationToken token,Lookup lookup) {
        var profile=Profile(saved);
        using(var cancel=CancellationTokenSource.CreateLinkedTokenSource(token,lifetime.Token)) {
            string[] texts={SpeechText(original),SpeechText(answer)};
            for(int i=0;i<texts.Length;i++) {
                string text=texts[i]; int priority=i==0?1:0;
                string role=HistoryRole(lookup,text);
                if(role!=null) { await EnsureHistoryAudio(lookup,text,role,profile,cancel.Token,priority).ConfigureAwait(false); continue; }
                foreach(string part in AudioParts(text,profile)) { cancel.Token.ThrowIfCancellationRequested(); await Segment(part,profile,cancel.Token,priority).ConfigureAwait(false); }
            }
        }
    }
    static string HistoryRole(Lookup lookup,string text) {
        if(lookup==null || String.IsNullOrWhiteSpace(text)) return null;
        if(text==SpeechText(lookup.Term)) return "word";
        if(text==SpeechText(lookup.Answer)) return "explanation";
        return null;
    }
    string HistoryDirectory(string id) {
        if(String.IsNullOrEmpty(id) || !Regex.IsMatch(id,@"\A[A-Za-z0-9_-]{1,128}\z")) throw new IOException("历史记录标识无效。");
        string path=Path.GetFullPath(Path.Combine(historyAudioRoot,id));
        if(!path.StartsWith(historyAudioRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new IOException("历史音频路径越界。");
        EnsureNoLinks(path); return path;
    }
    ReleaseHistoryAudioManifest ReadHistoryManifest(string directory,string id) {
        try {
            string path=Path.Combine(directory,"manifest.json"); EnsureNoLinks(path);
            if(!File.Exists(path) || new FileInfo(path).Length>2*1024*1024) return null;
            var manifest=Files.Json().Deserialize<ReleaseHistoryAudioManifest>(File.ReadAllText(path,Files.Utf8));
            return manifest!=null && manifest.Version==1 && manifest.QueryId==id?manifest:null;
        } catch(IOException) { } catch(UnauthorizedAccessException) { } catch(ArgumentException) { } catch(InvalidOperationException) { }
        return null;
    }
    bool TryHistory(Lookup lookup,string text,ReleaseAudioProfile profile,out List<string> paths) {
        paths=null; string role=HistoryRole(lookup,text); if(role==null) return false;
        try {
            string directory=HistoryDirectory(lookup.Id); var manifest=ReadHistoryManifest(directory,lookup.Id);
            if(manifest==null || manifest.Provider!=profile.Provider || manifest.Voice!=profile.Voice || manifest.Rate!=profile.Rate) return false;
            if(role=="word" && manifest.WordComplete && manifest.OriginalFingerprint==profile.Fingerprint(text)) return ReadAssets(directory,manifest.Word,out paths);
            if(role=="explanation" && manifest.ExplanationComplete && manifest.ExplanationFingerprint==profile.Fingerprint(text)) return ReadAssets(directory,manifest.Explanation,out paths);
        } catch(IOException) { } catch(UnauthorizedAccessException) { } catch(ArgumentException) { } catch(InvalidOperationException) { }
        return false;
    }
    public bool TryGetHistoryAudio(Lookup lookup,string text,Settings saved,out List<string> paths) { return TryHistory(lookup,SpeechText(text),Profile(saved),out paths); }
    async Task<List<string>> EnsureHistoryAudio(Lookup lookup,string text,string role,ReleaseAudioProfile profile,CancellationToken token,int priority) {
        var snapshot=Files.Json().Deserialize<Lookup>(Files.Json().Serialize(lookup));
        string directory=HistoryDirectory(snapshot.Id); SemaphoreSlim gate;
        List<string> existing; if(TryHistory(snapshot,text,profile,out existing)) return existing;
        lock(sync) { if(!historyGates.TryGetValue(snapshot.Id,out gate)) { gate=new SemaphoreSlim(1,1); historyGates.Add(snapshot.Id,gate); } }
        await gate.WaitAsync(token).ConfigureAwait(false);
        try {
            token.ThrowIfCancellationRequested(); Directory.CreateDirectory(directory);
            if(!File.Exists(Path.Combine(directory,"manifest.json"))) CommitHistoryManifest(directory,new ReleaseHistoryAudioManifest { QueryId=snapshot.Id,Created=snapshot.Created??DateTime.UtcNow.ToString("o") },token);
        } finally { gate.Release(); }
        // The query gate protects only small disk transactions, never synthesis.
        // A user request can join/promote an identical in-flight cache job while
        // another role is preparing, and ready original audio remains readable.
        var prepared=new List<byte[]>();
        foreach(string part in AudioParts(text,profile)) prepared.Add(await Segment(part,profile,token,priority).ConfigureAwait(false));
        await gate.WaitAsync(token).ConfigureAwait(false);
        try {
            if(TryHistory(snapshot,text,profile,out existing)) return existing;
            token.ThrowIfCancellationRequested(); Directory.CreateDirectory(directory);
            var manifest=ReadHistoryManifest(directory,snapshot.Id)??new ReleaseHistoryAudioManifest { QueryId=snapshot.Id,Created=snapshot.Created??DateTime.UtcNow.ToString("o") };
            manifest.Word=manifest.Word??new List<ReleaseAudioAsset>(); manifest.Explanation=manifest.Explanation??new List<ReleaseAudioAsset>(); manifest.Retired=manifest.Retired??new List<ReleaseAudioAsset>();
            string originalFingerprint=profile.Fingerprint(snapshot.Term),answerFingerprint=profile.Fingerprint(snapshot.Answer);
            bool profileChanged=manifest.Provider!=profile.Provider || manifest.Voice!=profile.Voice || manifest.Rate!=profile.Rate;
            if(profileChanged || role=="word" && manifest.OriginalFingerprint!=originalFingerprint) { manifest.Retired.AddRange(manifest.Word); manifest.Word.Clear(); manifest.WordComplete=false; }
            if(profileChanged || role=="explanation" && manifest.ExplanationFingerprint!=answerFingerprint) { manifest.Retired.AddRange(manifest.Explanation); manifest.Explanation.Clear(); manifest.ExplanationComplete=false; }
            manifest.Provider=profile.Provider; manifest.Voice=profile.Voice; manifest.Rate=profile.Rate;
            // Original prewarming may have captured the lookup before its
            // answer arrived. Its late commit must not clear a newer answer.
            if(role=="word") manifest.OriginalFingerprint=originalFingerprint;
            else manifest.ExplanationFingerprint=answerFingerprint;
            var assets=role=="word"?manifest.Word:manifest.Explanation;
            manifest.Retired.AddRange(assets); assets.Clear();
            if(role=="word") manifest.WordComplete=false; else manifest.ExplanationComplete=false;
            CommitHistoryManifest(directory,manifest,token);
            int index=0;
            foreach(byte[] bytes in prepared) {
                token.ThrowIfCancellationRequested();
                string digest=Hash(bytes),name=role+"-"+profile.Fingerprint(text).Substring(0,20)+"-"+(index++).ToString("D3")+"-"+digest.Substring(0,20)+profile.Extension;
                WriteBytes(AssetPath(directory,name),bytes,token);
                assets.Add(new ReleaseAudioAsset { File=name,Sha256=digest,Bytes=bytes.LongLength });
                // A completed disk write must remain tracked even if the
                // lookup was closed between the write and this commit.
                CommitHistoryManifest(directory,manifest,CancellationToken.None);
                token.ThrowIfCancellationRequested();
            }
            if(role=="word") manifest.WordComplete=true; else manifest.ExplanationComplete=true;
            CommitHistoryManifest(directory,manifest,token);
            return assets.Select(a=>AssetPath(directory,a.File)).ToList();
        } finally { gate.Release(); }
    }
    static void CommitHistoryManifest(string directory,ReleaseHistoryAudioManifest manifest,CancellationToken token) {
        token.ThrowIfCancellationRequested(); EnsureNoLinks(directory);
        var active=new HashSet<string>(manifest.Word.Concat(manifest.Explanation).Select(a=>a.File),StringComparer.OrdinalIgnoreCase);
        manifest.Retired=manifest.Retired.Where(a=>!active.Contains(a.File)).GroupBy(a=>a.File,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).ToList();
        Files.Atomic(Path.Combine(directory,"manifest.json"),Files.Json().Serialize(manifest));
    }
    static void EnsureNoLinks(string path) {
        for(string current=Path.GetFullPath(path); current!=null; current=Path.GetDirectoryName(current))
            if((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0) throw new IOException("音频目录不支持符号链接或目录联接。");
    }
    string FavoriteDirectory(string id) {
        if(String.IsNullOrEmpty(id) || !Regex.IsMatch(id,@"\A[A-Za-z0-9_-]{1,128}\z")) throw new IOException("收藏标识无效。");
        string directory=Path.GetFullPath(Path.Combine(favoritesRoot,id));
        if(!directory.StartsWith(favoritesRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new IOException("收藏路径越界。");
        EnsureNoLinks(directory); return directory;
    }
    string AssetPath(string directory,string file) {
        if(String.IsNullOrEmpty(file) || Path.GetFileName(file)!=file) throw new IOException("音频清单路径无效。");
        string path=Path.Combine(directory,file); EnsureNoLinks(path); return path;
    }
    static void WriteBytes(string path,byte[] bytes,CancellationToken token) {
        token.ThrowIfCancellationRequested(); string temp=path+".tmp-"+Guid.NewGuid().ToString("N");
        try {
            File.WriteAllBytes(temp,bytes); token.ThrowIfCancellationRequested();
            if(File.Exists(path)) { if(Hash(File.ReadAllBytes(path))!=Hash(bytes)) { EnsureNoLinks(path); File.Replace(temp,path,null); } }
            else File.Move(temp,path);
        } finally { if(File.Exists(temp)) File.Delete(temp); }
    }
    bool ReadAssets(string directory,List<ReleaseAudioAsset> assets,out List<string> paths) {
        paths=new List<string>(); if(assets==null || assets.Count==0) return false;
        foreach(var asset in assets) {
            if(asset==null || String.IsNullOrEmpty(asset.Sha256)) return false;
            string path=AssetPath(directory,asset.File);
            if(!File.Exists(path)) return false;
            var info=new FileInfo(path); if(info.Length!=asset.Bytes || info.Length<1 || info.Length>64L*1024*1024) return false;
            if(!String.Equals(Hash(File.ReadAllBytes(path)),asset.Sha256,StringComparison.Ordinal)) return false;
            paths.Add(path);
        }
        return true;
    }
    bool TryStored(Favorite favorite,string text,ReleaseAudioProfile profile,out List<string> paths) {
        paths=null; if(favorite==null || !favorite.Saved) return false;
        try {
            string directory=FavoriteDirectory(favorite.Id),path=Path.Combine(directory,"manifest.json"); EnsureNoLinks(path);
            if(!File.Exists(path) || new FileInfo(path).Length>1024*1024) return false;
            var manifest=Files.Json().Deserialize<ReleaseAudioManifest>(File.ReadAllText(path,Files.Utf8));
            if(manifest==null || manifest.Version!=1 || manifest.FavoriteId!=favorite.Id || manifest.Provider!=profile.Provider || manifest.Voice!=profile.Voice || manifest.Rate!=profile.Rate) return false;
            string fingerprint=profile.Fingerprint(text);
            if(fingerprint==manifest.OriginalFingerprint) return ReadAssets(directory,manifest.Word,out paths);
            if(fingerprint==manifest.ExplanationFingerprint) return ReadAssets(directory,manifest.Explanation,out paths);
        } catch(IOException) { } catch(UnauthorizedAccessException) { } catch(ArgumentException) { } catch(InvalidOperationException) { } catch(FormatException) { }
        return false;
    }
    public bool TryGetFavoriteAudio(Favorite favorite,string text,Settings saved,out List<string> paths) { return TryStored(favorite,SpeechText(text),Profile(saved),out paths); }
    public Task PlayAsync(string text,Settings saved,CancellationToken token,Favorite favorite=null,Lookup lookup=null,Action playbackStarted=null) {
        StopPlayback();
        var cancel=CancellationTokenSource.CreateLinkedTokenSource(token,lifetime.Token);
        lock(sync) { if(disposed) { cancel.Dispose(); throw new ObjectDisposedException("ReleaseAudioService"); } playbackCancel=cancel; }
        return Task.Run(()=>PlayCore(text,saved,cancel,favorite,lookup,playbackStarted));
    }
    async Task PlayCore(string text,Settings saved,CancellationTokenSource cancel,Favorite favorite,Lookup lookup,Action playbackStarted) {
        var temporary=new List<string>();
        int announced=0;
        Action started=delegate {
            if(Interlocked.Exchange(ref announced,1)!=0 || playbackStarted==null) return;
            OnOwner(delegate { if(!cancel.IsCancellationRequested) playbackStarted(); return true; });
        };
        try {
            cancel.Token.ThrowIfCancellationRequested(); text=SpeechText(text); if(text.Length==0) return;
            var profile=Profile(saved);
            List<string> stored;
            if(TryStored(favorite,text,profile,out stored)) {
                foreach(string path in stored) await PlayFile(path,cancel.Token,started).ConfigureAwait(false);
                return;
            }
            string role=HistoryRole(lookup,text);
            if(role!=null) {
                stored=await EnsureHistoryAudio(lookup,text,role,profile,cancel.Token,2).ConfigureAwait(false);
                foreach(string path in stored) await PlayFile(path,cancel.Token,started).ConfigureAwait(false);
                return;
            }
            EnsureNoLinks(playbackRoot); Directory.CreateDirectory(playbackRoot);
            foreach(string part in AudioParts(text,profile)) {
                byte[] bytes=await Segment(part,profile,cancel.Token,2).ConfigureAwait(false); cancel.Token.ThrowIfCancellationRequested();
                string path=Path.Combine(playbackRoot,Guid.NewGuid().ToString("N")+profile.Extension); temporary.Add(path);
                WriteBytes(path,bytes,cancel.Token); await PlayFile(path,cancel.Token,started).ConfigureAwait(false);
            }
        } finally {
            foreach(string path in temporary) try { if(File.Exists(path)) File.Delete(path); } catch(IOException) { } catch(UnauthorizedAccessException) { }
            lock(sync) { if(playbackCancel==cancel) playbackCancel=null; }
            cancel.Dispose();
        }
    }
    async Task PlayFile(string path,CancellationToken token,Action started) {
        // V2 opens and polls MCI on the UI thread. Keep that contract even though
        // synthesis and disk IO intentionally continue on worker threads.
        byte[] waveBytes=Path.GetExtension(path).Equals(".wav",StringComparison.OrdinalIgnoreCase)?File.ReadAllBytes(path):null;
        await OnOwner(delegate { return PlayFileOnOwner(path,token,waveBytes,started); }).Unwrap().ConfigureAwait(false);
    }
    async Task PlayFileOnOwner(string path,CancellationToken token,byte[] waveBytes,Action started) {
        if(playOverride!=null) { token.ThrowIfCancellationRequested(); started(); await playOverride(path,token); return; }
        var active=new ReleaseAudioPlayer(); lock(sync) { token.ThrowIfCancellationRequested(); player=active; }
        try { await active.PlayAsync(path,token,waveBytes,started); }
        finally { active.Dispose(); lock(sync) if(player==active) player=null; }
    }
    public void StopPlayback() {
        lock(sync) {
            if(playbackCancel!=null) playbackCancel.Cancel();
            if(player!=null) { player.Dispose(); player=null; }
        }
    }
    Task<T> OnOwner<T>(Func<T> action) {
        if(owner==null || SynchronizationContext.Current==owner) {
            try { return Task.FromResult(action()); } catch(Exception error) { var failed=new TaskCompletionSource<T>(); failed.SetException(error); return failed.Task; }
        }
        var done=new TaskCompletionSource<T>(); owner.Post(delegate(object ignored) { try { done.SetResult(action()); } catch(Exception e) { done.SetException(e); } },null); return done.Task;
    }
    bool IsTicketCurrent(Ticket ticket,out Favorite current) {
        current=null; if(disposed || ticket.Cancel.IsCancellationRequested) return false;
        lock(sync) { Ticket active; if(!favorites.TryGetValue(ticket.Snapshot.Id,out active) || active!=ticket) return false; }
        current=history.FindFavorite(ticket.Snapshot.Kind,ticket.Snapshot.Term);
        return current!=null && current.Saved && current.Id==ticket.Snapshot.Id && current.QueryId==ticket.Snapshot.QueryId && current.Updated==ticket.Snapshot.Updated && current.Term==ticket.Snapshot.Term && current.Explanation==ticket.Snapshot.Explanation;
    }
    void Publish(Favorite current) { var listeners=FavoriteAudioChanged; if(listeners==null) return; foreach(Action<Favorite> listener in listeners.GetInvocationList()) try { listener(Copy(current)); } catch(Exception) { /* A status observer cannot invalidate persisted audio. */ } }
    public async Task PrepareFavoriteAsync(Favorite favorite,Settings saved) {
        if(favorite==null || !favorite.Saved) return;
        if(history==null) throw new InvalidOperationException("收藏音频需要历史数据库。");
        var ticket=new Ticket { Snapshot=Copy(favorite),Cancel=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token) };
        lock(sync) { if(disposed) { ticket.Cancel.Dispose(); throw new ObjectDisposedException("ReleaseAudioService"); } Ticket old; if(favorites.TryGetValue(favorite.Id,out old)) old.Cancel.Cancel(); favorites[favorite.Id]=ticket; }
        try {
            Exception failure=null;
            try {
            var profile=await Task.Run(()=>Profile(saved)).ConfigureAwait(false);
            bool accepted=await OnOwner(delegate {
                Favorite current; if(!IsTicketCurrent(ticket,out current)) return false;
                current.AudioState="pending"; current.AudioError=""; history.StoreFavorite(current); Publish(current); return true;
            }).ConfigureAwait(false);
            if(!accepted) return;
            string original=SpeechText(ticket.Snapshot.Term),answer=SpeechText(ticket.Snapshot.Explanation);
            if(original.Length==0 || answer.Length==0) throw new InvalidOperationException("收藏原文和解释必须完整，才能保存音频。");
            string directory=FavoriteDirectory(ticket.Snapshot.Id); Directory.CreateDirectory(directory);
            var manifest=new ReleaseAudioManifest { FavoriteId=ticket.Snapshot.Id,Provider=profile.Provider,Voice=profile.Voice,Rate=profile.Rate,Created=DateTime.UtcNow.ToString("o"),OriginalFingerprint=profile.Fingerprint(original),ExplanationFingerprint=profile.Fingerprint(answer) };
            manifest.Word=await SaveParts(ticket.Snapshot,original,"word",profile,directory,ticket.Cancel.Token).ConfigureAwait(false);
            manifest.Explanation=await SaveParts(ticket.Snapshot,answer,"explanation",profile,directory,ticket.Cancel.Token).ConfigureAwait(false);
            bool mayCommit=await OnOwner(delegate {
                Favorite current; if(!IsTicketCurrent(ticket,out current)) return false;
                return true;
            }).ConfigureAwait(false);
            if(!mayCommit) return;
            // Disk work stays off the UI thread. Serialize final file commits
            // with cancellation checks so an older job cannot replace a newer
            // favorite revision; recheck the live DB again before marking ready.
            lock(assetSync) {
                ticket.Cancel.Token.ThrowIfCancellationRequested();
                EnsureNoLinks(directory);
                string manifestPath=Path.Combine(directory,"manifest.json"); EnsureNoLinks(manifestPath);
                if(File.Exists(manifestPath) && new FileInfo(manifestPath).Length<=2*1024*1024) {
                    try {
                        var previous=Files.Json().Deserialize<ReleaseAudioManifest>(File.ReadAllText(manifestPath,Files.Utf8));
                        if(previous!=null && previous.Version==1 && previous.FavoriteId==manifest.FavoriteId) {
                            var active=new HashSet<string>(manifest.Word.Concat(manifest.Explanation).Select(a=>a.File),StringComparer.OrdinalIgnoreCase);
                            manifest.Retired=(previous.Word??new List<ReleaseAudioAsset>()).Concat(previous.Explanation??new List<ReleaseAudioAsset>()).Concat(previous.Retired??new List<ReleaseAudioAsset>()).Where(a=>a!=null && !active.Contains(a.File)).GroupBy(a=>a.File,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).ToList();
                        }
                    } catch(ArgumentException) { } catch(InvalidOperationException) { }
                }
                Files.Atomic(manifestPath,Files.Json().Serialize(manifest));
            }
            await OnOwner(delegate {
                Favorite current; if(!IsTicketCurrent(ticket,out current)) return false;
                current.WordAudio=manifest.Word.Select(a=>Path.Combine("favorites",current.Id,a.File)).ToList();
                current.ExplanationAudio=manifest.Explanation.Select(a=>Path.Combine("favorites",current.Id,a.File)).ToList();
                current.Voice=profile.Provider+":"+profile.Voice; current.AudioState="ready"; current.AudioError="";
                history.StoreFavorite(current); Publish(current); return true;
            }).ConfigureAwait(false);
        } catch(OperationCanceledException) { }
        catch(Exception error) { failure=error; }
        if(failure!=null) {
            await OnOwner(delegate {
                Favorite current; if(!IsTicketCurrent(ticket,out current)) return false;
                current.AudioState="failed"; current.AudioError=failure.Message; history.StoreFavorite(current); Publish(current); return true;
            }).ConfigureAwait(false);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
        } finally {
            lock(sync) { Ticket current; if(favorites.TryGetValue(favorite.Id,out current) && current==ticket) favorites.Remove(favorite.Id); }
            ticket.Cancel.Dispose();
        }
    }
    async Task<List<ReleaseAudioAsset>> SaveParts(Favorite favorite,string text,string role,ReleaseAudioProfile profile,string directory,CancellationToken token) {
        List<string> stored;
        if(TryStored(favorite,text,profile,out stored)) return stored.Select(path=>new ReleaseAudioAsset { File=Path.GetFileName(path),Sha256=Hash(File.ReadAllBytes(path)),Bytes=new FileInfo(path).Length }).ToList();
        var assets=new List<ReleaseAudioAsset>(); int partIndex=0;
        foreach(string part in AudioParts(text,profile)) {
            byte[] bytes=await Segment(part,profile,token,0).ConfigureAwait(false); token.ThrowIfCancellationRequested();
            string digest=Hash(bytes),name=role+"-"+profile.Fingerprint(text).Substring(0,20)+"-"+(partIndex++).ToString("D3")+"-"+digest.Substring(0,20)+profile.Extension;
            var asset=new ReleaseAudioAsset { File=name,Sha256=digest,Bytes=bytes.LongLength };
            // The journal is independent of the playable manifest. A failed or
            // canceled save can retain cleanup metadata without publishing a
            // partial favorite, or invalidating an existing complete favorite.
            lock(assetSync) {
                token.ThrowIfCancellationRequested();
                string journalPath=Path.Combine(directory,"assets.json"); EnsureNoLinks(journalPath);
                var journal=File.Exists(journalPath)?Files.Json().Deserialize<ReleaseAudioManifest>(File.ReadAllText(journalPath,Files.Utf8)):new ReleaseAudioManifest { FavoriteId=favorite.Id,Created=favorite.Created };
                if(journal==null || journal.Version!=1 || journal.FavoriteId!=favorite.Id) throw new IOException("收藏音频资产清单无效。");
                journal.Retired=journal.Retired??new List<ReleaseAudioAsset>();
                journal.Retired.RemoveAll(a=>a!=null && a.File==name); journal.Retired.Add(asset);
                // Predeclare the exact managed filename; missing files are safe
                // during cancellation and never become playable assets.
                Files.Atomic(journalPath,Files.Json().Serialize(journal));
                WriteBytes(AssetPath(directory,name),bytes,token);
            }
            assets.Add(asset);
        }
        return assets;
    }
    public void CancelFavorite(string favoriteId) { lock(sync) { Ticket ticket; if(favoriteId!=null && favorites.TryGetValue(favoriteId,out ticket)) ticket.Cancel.Cancel(); } }
    void DisposeProviders() { if(web!=null) web.Dispose(); foreach(var service in translators.Values) service.Dispose(); translators.Clear(); foreach(var service in edgeVoices.Values) service.Dispose(); edgeVoices.Clear(); }
    public void Dispose() {
        lock(sync) { if(disposed) return; disposed=true; lifetime.Cancel(); foreach(var task in favorites.Values) task.Cancel.Cancel(); }
        StopPlayback(); cache.Dispose();
        // Do not dispose lifetime while async jobs still use its linked token.
    }
}
// Keep MP3 playback identical to V2. The legacy waveaudio MCI driver rejects
// long cache paths (error 304); SoundPlayer consumes the WAV bytes directly.
sealed class ReleaseAudioPlayer : IDisposable {
    readonly object sync=new object(); bool disposed; Mp3Player mp3; System.Media.SoundPlayer wave;
    public async Task PlayAsync(string path,CancellationToken token,byte[] waveBytes=null,Action started=null) {
        token.ThrowIfCancellationRequested();
        if(!Path.GetExtension(path).Equals(".wav",StringComparison.OrdinalIgnoreCase)) {
            lock(sync) { if(disposed) throw new OperationCanceledException(); mp3=new Mp3Player(); }
            Task playing=mp3.Play(path,token); if(!playing.IsFaulted && !playing.IsCanceled && started!=null) started();
            await playing; return;
        }
        using(var stream=new MemoryStream(waveBytes??File.ReadAllBytes(path),false))
        using(var active=new System.Media.SoundPlayer(stream)) {
            active.Load(); lock(sync) { if(disposed) throw new OperationCanceledException(); wave=active; }
            try {
                using(token.Register(delegate { try { active.Stop(); } catch(ObjectDisposedException) { } })) {
                    await Task.Run(delegate { token.ThrowIfCancellationRequested(); if(started!=null) started(); active.PlaySync(); },token);
                    token.ThrowIfCancellationRequested();
                }
            } finally {
                active.Stop(); lock(sync) if(wave==active) wave=null;
            }
        }
    }
    public void Dispose() { lock(sync) { if(disposed) return; disposed=true; if(mp3!=null) mp3.Dispose(); if(wave!=null) wave.Stop(); } }
}
}
