using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace TermLens {
public sealed class ReleaseRetentionResult {
    public readonly List<string> ExpiredQueryIds=new List<string>(),ExpiredFavoriteIds=new List<string>(),DeletedAudioDirectories=new List<string>(),Warnings=new List<string>();
    public readonly HashSet<string> CurrentQueryIds=new HashSet<string>(StringComparer.Ordinal),ProtectedQueryIds=new HashSet<string>(StringComparer.Ordinal),ProtectedFavoriteIds=new HashSet<string>(StringComparer.Ordinal);
}
public sealed partial class HistoryStore {
    // Call on the database-owning thread, outside an existing transaction. Only
    // release-owned records are eligible; unknown sources/dates stay untouched.
    public ReleaseRetentionResult ApplyReleaseRetention(string dataRoot,DateTime utcNow) {
        var result=RetainReleaseHistory(utcNow);
        try {
            ReleaseRetention.CleanAudio(dataRoot,utcNow,result);
        } catch(Exception e) { result.Warnings.Add("Audio retention skipped: "+e.Message); }
        return result;
    }
    public ReleaseRetentionResult RetainReleaseHistory(DateTime utcNow) {
        var result=new ReleaseRetentionResult(); DateTime cutoff=utcNow.ToUniversalTime().AddDays(-15);
        Run("BEGIN IMMEDIATE");
        try {
            var protectedIds=new HashSet<string>(StringComparer.Ordinal);
            var protectedKeys=new HashSet<string>(StringComparer.Ordinal);
            var unsaved=new List<Favorite>();
            foreach(var row in Run("SELECT id,saved,record FROM favorites")) {
                Favorite item;
                try { item=Files.Json().Deserialize<Favorite>(row[2]); if(item==null || item.Id!=row[0]) throw new InvalidDataException("Favorite identity mismatch"); }
                catch { throw new InvalidDataException("Retention stopped because a favorite snapshot cannot be safely read."); }
                if(row[1]=="1") {
                    if(String.IsNullOrEmpty(item.QueryId)) protectedKeys.Add(FavoriteKey(item.Kind,item.Term??""));
                    else {
                        protectedIds.Add(item.QueryId);
                        foreach(var link in Run("SELECT query_id FROM query_links WHERE legacy_id=?",item.QueryId)) protectedIds.Add(link[0]);
                    }
                } else unsaved.Add(item);
            }
            foreach(var item in unsaved) {
                var rows=Run("SELECT created FROM queries WHERE id=? UNION ALL SELECT q.created FROM queries q JOIN query_links l ON l.query_id=q.id WHERE l.legacy_id=? UNION ALL SELECT created FROM lookups WHERE id=?",item.QueryId,item.QueryId,item.QueryId);
                string created=rows.Count>0?rows[0][0]:item.Created;
                if(!ReleaseRetention.IsExpired(created,cutoff)) continue;
                Run("DELETE FROM favorites WHERE id=? AND saved=0",item.Id); result.ExpiredFavoriteIds.Add(item.Id);
            }
            var candidates=Run("SELECT id,kind,source,text,answer,created,term_key FROM queries");
            foreach(var row in candidates) {
                string id=row[0];
                if(!ReleaseRetention.IsExpired(row[5],cutoff) || protectedIds.Contains(id) || protectedKeys.Contains(row[1]+":"+row[6])) continue;
                if(row[2]!="release-selection" && row[2]!="release-context" && row[2]!="legacy") continue;
                // The mapping must identify a matching lookup. Retain ambiguous
                // data rather than remove a foreign legacy row or re-import it.
                var linked=Run("SELECT legacy_id FROM query_links WHERE query_id=?",id).Select(r=>r[0]).ToList();
                if(row[2]=="legacy" && !linked.Contains(id)) linked.Add(id);
                bool safe=true;
                foreach(string legacy in linked) {
                    if(protectedIds.Contains(legacy)) { safe=false; break; }
                    var old=Run("SELECT term,answer FROM lookups WHERE id=?",legacy);
                    if(old.Count>0 && (old[0][0]!=row[3] || old[0][1]!=row[4])) { safe=false; break; }
                    if(legacy!=id && old.Count>0 && VerifiedSelectionTarget(legacy)!=id) { safe=false; break; }
                    if(legacy!=id && Run("SELECT id FROM queries WHERE id=?",legacy).Count>0) { safe=false; break; }
                }
                if(!safe) { result.Warnings.Add("Kept an ambiguous legacy-linked query: "+id); continue; }
                // Archive only the selection before atomically removing full cache content.
                RecordSelection(new Lookup { Id=id,Kind=row[1],Source=row[2],Term=row[3],Created=row[5] });
                foreach(string legacy in linked) Run("DELETE FROM lookups WHERE id=?",legacy);
                Run("DELETE FROM query_links WHERE query_id=?",id);
                Run("DELETE FROM queries WHERE id=?",id); result.ExpiredQueryIds.Add(id);
                Run("DELETE FROM terms WHERE term_key=? AND saved=0 AND NOT EXISTS(SELECT 1 FROM lookups WHERE lookups.term_key=terms.term_key) AND NOT EXISTS(SELECT 1 FROM queries WHERE queries.term_key=terms.term_key)",row[6]);
            }
            foreach(var row in Run("SELECT id FROM queries")) { result.CurrentQueryIds.Add(row[0]); result.ProtectedQueryIds.Add(row[0]); }
            foreach(string id in protectedIds) result.ProtectedQueryIds.Add(id);
            foreach(var row in Run("SELECT id FROM favorites")) result.ProtectedFavoriteIds.Add(row[0]);
            Run("COMMIT");
        } catch { Run("ROLLBACK"); throw; }
        return result;
    }
}
public static class ReleaseRetention {
    sealed class Manifest {
        public Manifest() { }
        public int Version;
        public string QueryId,FavoriteId,Created;
        public List<ReleaseAudioAsset> Word,Explanation,Retired;
    }
    internal static bool IsExpired(string created,DateTime cutoff) {
        DateTimeOffset parsed;
        return DateTimeOffset.TryParse(created,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out parsed) && parsed.UtcDateTime<cutoff;
    }
    static bool SafeId(string id) { return !String.IsNullOrEmpty(id) && Regex.IsMatch(id,@"\A[A-Za-z0-9_-]{1,128}\z"); }
    static void NoLinks(string path) {
        for(string current=Path.GetFullPath(path); current!=null; current=Path.GetDirectoryName(current))
            if((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0) throw new IOException("Reparse point is not eligible for retention cleanup.");
    }
    static string Digest(string path) {
        using(var stream=File.OpenRead(path)) using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
    }
    static Manifest ReadManifest(string path,string id,bool favorite) {
        NoLinks(path); var info=new FileInfo(path);
        if(info.Length>1024*1024) throw new InvalidDataException("Oversized audio manifest");
        var manifest=Files.Json().Deserialize<Manifest>(File.ReadAllText(path,Files.Utf8));
        if(manifest==null || manifest.Version!=1 || (favorite?manifest.FavoriteId:manifest.QueryId)!=id || manifest.Word==null || manifest.Explanation==null) throw new InvalidDataException("Unrecognized audio manifest");
        return manifest;
    }
    // Only exact managed leaf directories with verifiable manifests/assets are
    // removed. No recursive deletion, unknown files or directory traversal.
    // The result is an immutable database snapshot for the caller until cleanup
    // completes; no SQLite access occurs here, so this overload may run off UI.
    public static void CleanAudio(string dataRoot,DateTime utcNow,ReleaseRetentionResult result) {
        if(result==null) throw new ArgumentNullException("result");
        CleanAudio(dataRoot,utcNow,result.ProtectedQueryIds,result.ProtectedFavoriteIds,result);
    }
    public static void CleanAudio(string dataRoot,DateTime utcNow,ISet<string> protectedQueryIds,ISet<string> protectedFavoriteIds,ReleaseRetentionResult result) {
        if(result==null) throw new ArgumentNullException("result");
        if(protectedQueryIds==null || protectedFavoriteIds==null) throw new ArgumentNullException("protectedIds");
        string root=Path.GetFullPath(dataRoot); NoLinks(root);
        DateTime cutoff=utcNow.ToUniversalTime().AddDays(-15);
        foreach(bool favorite in new[]{false,true}) {
            string parent=Path.Combine(root,favorite?"favorites":"history-audio");
            NoLinks(parent); if(!Directory.Exists(parent)) continue;
            foreach(string path in Directory.GetDirectories(parent)) {
                string id=Path.GetFileName(path);
                if(!SafeId(id) || (favorite?protectedFavoriteIds:protectedQueryIds).Contains(id)) continue;
                try {
                    NoLinks(path);
                    if(!String.Equals(Path.GetDirectoryName(Path.GetFullPath(path)),parent,StringComparison.OrdinalIgnoreCase)) throw new IOException("Audio path escaped its managed parent");
                    string manifestPath=Path.Combine(path,"manifest.json");
                    if(favorite && !File.Exists(manifestPath)) manifestPath=Path.Combine(path,"assets.json");
                    if(!File.Exists(manifestPath)) continue;
                    var manifest=ReadManifest(manifestPath,id,favorite);
                    bool recordedExpired=(favorite?result.ExpiredFavoriteIds:result.ExpiredQueryIds).Contains(id);
                    if(!recordedExpired && !IsExpired(manifest.Created,cutoff)) continue;
                    var allowed=new Dictionary<string,ReleaseAudioAsset>(StringComparer.OrdinalIgnoreCase);
                    Action<Manifest> add=delegate(Manifest value) {
                        foreach(var asset in value.Word.Concat(value.Explanation).Concat(value.Retired??new List<ReleaseAudioAsset>())) {
                            if(asset==null || String.IsNullOrEmpty(asset.File) || !Regex.IsMatch(asset.File,@"\A[A-Za-z0-9][A-Za-z0-9._-]{0,180}\.(mp3|wav)\z",RegexOptions.IgnoreCase) || asset.Bytes<0 || !Regex.IsMatch(asset.Sha256??"",@"\A[0-9a-fA-F]{64}\z")) throw new InvalidDataException("Unrecognized audio asset");
                            ReleaseAudioAsset previous;
                            if(allowed.TryGetValue(asset.File,out previous) && (previous.Bytes!=asset.Bytes || !String.Equals(previous.Sha256,asset.Sha256,StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Conflicting asset manifests");
                            allowed[asset.File]=asset;
                        }
                    };
                    var metadata=new HashSet<string>(favorite?new[]{"manifest.json","manifest.json.bak","assets.json","assets.json.bak"}:new[]{"manifest.json","manifest.json.bak"},StringComparer.OrdinalIgnoreCase);
                    add(manifest);
                    foreach(string name in metadata) {
                        string metadataPath=Path.Combine(path,name);
                        if(File.Exists(metadataPath) && !String.Equals(metadataPath,manifestPath,StringComparison.OrdinalIgnoreCase)) add(ReadManifest(metadataPath,id,favorite));
                    }
                    if(Directory.GetDirectories(path).Length!=0) throw new IOException("Unknown audio subdirectory");
                    string[] files=Directory.GetFiles(path);
                    foreach(string file in files) {
                        NoLinks(file); string name=Path.GetFileName(file);
                        if(metadata.Contains(name)) continue;
                        ReleaseAudioAsset asset;
                        if(!allowed.TryGetValue(name,out asset) || new FileInfo(file).Length!=asset.Bytes || !String.Equals(Digest(file),asset.Sha256,StringComparison.OrdinalIgnoreCase)) throw new IOException("Unknown or modified file in managed audio directory");
                    }
                    // Validate the whole directory before deleting any member.
                    // Remove the primary manifest last so interrupted cleanup can
                    // be retried with missing listed assets on a later launch.
                    foreach(string file in files.Where(file=>!metadata.Contains(Path.GetFileName(file)))) { NoLinks(file); File.Delete(file); }
                    foreach(string file in files.Where(file=>metadata.Contains(Path.GetFileName(file)) && !String.Equals(file,manifestPath,StringComparison.OrdinalIgnoreCase))) { NoLinks(file); File.Delete(file); }
                    NoLinks(manifestPath); File.Delete(manifestPath); NoLinks(path); Directory.Delete(path,false);
                    result.DeletedAudioDirectories.Add(path);
                } catch(Exception e) { result.Warnings.Add("Audio directory retained ("+id+"): "+e.Message); }
            }
        }
    }
}
}
