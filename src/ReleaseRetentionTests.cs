using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace TermLens {
public static class ReleaseRetentionTests {
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    [return:MarshalAs(UnmanagedType.U1)] static extern bool CreateSymbolicLink(string link,string target,int flags);
    static string AudioFixture(string root,string id,DateTime created,bool favorite=false,bool retired=false) {
        string folder=Path.Combine(root,favorite?"favorites":"history-audio",id); Directory.CreateDirectory(folder);
        Func<string,ReleaseAudioAsset> asset=delegate(string name) {
            byte[] bytes=Encoding.UTF8.GetBytes("Isolated fixture audio "+name); File.WriteAllBytes(Path.Combine(folder,name),bytes);
            string hash; using(var sha=SHA256.Create()) hash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
            return new ReleaseAudioAsset { File=name,Bytes=bytes.Length,Sha256=hash };
        };
        var word=new List<ReleaseAudioAsset>{asset("word-0.mp3")};
        var old=new List<ReleaseAudioAsset>(); if(retired) old.Add(asset("previous-profile-0.mp3"));
        File.WriteAllText(Path.Combine(folder,"manifest.json"),Files.Json().Serialize(new { Version=1,QueryId=favorite?null:id,FavoriteId=favorite?id:null,Created=created.ToString("o"),Word=word,Explanation=new List<ReleaseAudioAsset>(),Retired=old,WordComplete=false,ExplanationComplete=false }),Files.Utf8);
        return folder;
    }
    static string PartialFavoriteFixture(string root,string id,DateTime created) {
        string folder=AudioFixture(root,id,created,true);
        string playable=Path.Combine(folder,"manifest.json"),journal=Path.Combine(folder,"assets.json");
        var manifest=Files.Json().Deserialize<ReleaseAudioManifest>(File.ReadAllText(playable,Files.Utf8));
        manifest.Retired.AddRange(manifest.Word); manifest.Word.Clear();
        File.WriteAllText(playable,Files.Json().Serialize(manifest),Files.Utf8);
        File.Move(playable,journal); File.Copy(journal,journal+".bak");
        // A previous journal owns the existing partial file, while the latest
        // journal also declares a segment canceled before its file was written.
        manifest.Retired.Clear(); manifest.Retired.Add(new ReleaseAudioAsset { File="not-written-yet.mp3",Bytes=128,Sha256=new string('0',64) });
        File.WriteAllText(journal,Files.Json().Serialize(manifest),Files.Utf8);
        return folder;
    }
    public static int Run(string root) {
        string folder=Path.Combine(root,"tests","release-retention-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));
        Directory.CreateDirectory(folder); var results=new List<string>();
        Action<bool,string> check=delegate(bool ok,string message) { if(!ok) throw new Exception(message); results.Add("PASS: "+message); };
        try {
            DateTime now=new DateTime(2030,2,20,12,0,0,DateTimeKind.Utc),boundary=now.AddDays(-15),old=boundary.AddTicks(-1);
            string database=Path.Combine(folder,"history.sqlite3");
            using(var history=new HistoryStore(database)) {
                Func<string,DateTime,Lookup> add=delegate(string id,DateTime created) {
                    var item=new Lookup { Id=id,Term="term-"+id,Answer="answer-"+id,Kind="explain",Source="release-selection",Created=created.ToString("o"),Model="fixture",Prompt="fixture",Context="{}",Status="complete" };
                    history.RecordQuery(item.Id,item.Kind,item.Source,item.Term,item.Answer,item.Term,item.Context,item.Model,item.Prompt,item.Created,item.Status,""); return item;
                };
                var expired=add("expired",old); add("boundary",boundary); add("fresh",now.AddDays(-1));
                var saved=add("saved",old); var favorite=history.SetFavorite(saved,true);
                var canceled=add("canceled",old); var canceledFavorite=history.SetFavorite(canceled,true); history.SetFavorite(canceled,false);
                var canceledFresh=add("canceled-fresh",now.AddDays(-1)); var canceledFreshFavorite=history.SetFavorite(canceledFresh,true); history.SetFavorite(canceledFresh,false);
                var savedPartial=add("saved-partial",old); var savedPartialFavorite=history.SetFavorite(savedPartial,true);
                var canceledPartial=add("canceled-partial",old); var canceledPartialFavorite=history.SetFavorite(canceledPartial,true); history.SetFavorite(canceledPartial,false);
                history.RecordQuery("invalid-date","explain","release-selection","bad-date","answer","","{}","fixture","fixture","unknown","complete","");
                history.RecordQuery("foreign","explain","unrecognized-import","foreign","answer","","{}","fixture","fixture",old.ToString("o"),"complete","");
                var legacy=history.Add("mapped source","mapped answer","fixture","fixture","{}");
                history.Run("UPDATE lookups SET created=? WHERE id=?",old.ToString("o"),legacy.Id);
                history.RecordQuery("mapped-query","explain","release-selection",legacy.Term,legacy.Answer,legacy.Term,"{}","fixture","fixture",old.ToString("o"),"complete","",legacy.Id);
                var ambiguous=history.Add("other legacy source","other legacy answer","fixture","fixture","{}");
                history.RecordQuery("ambiguous-query","explain","release-selection","different source","different answer","","{}","fixture","fixture",old.ToString("o"),"complete","",ambiguous.Id);
                string expiredAudio=AudioFixture(folder,expired.Id,old),savedAudio=AudioFixture(folder,saved.Id,old),favoriteAudio=AudioFixture(folder,favorite.Id,old,true);
                string canceledAudio=AudioFixture(folder,canceledFavorite.Id,now,true),canceledFreshAudio=AudioFixture(folder,canceledFreshFavorite.Id,old,true);
                string savedPartialAudio=PartialFavoriteFixture(folder,savedPartialFavorite.Id,old),canceledPartialAudio=PartialFavoriteFixture(folder,canceledPartialFavorite.Id,now),orphanPartialAudio=PartialFavoriteFixture(folder,"orphan-partial-favorite",old);
                string orphan=AudioFixture(folder,"orphan-pending",old,false,true),orphanFresh=AudioFixture(folder,"orphan-fresh",now),audioBoundary=AudioFixture(folder,"orphan-boundary",boundary);
                string withBackup=AudioFixture(folder,"orphan-backup",old); File.Copy(Path.Combine(withBackup,"manifest.json"),Path.Combine(withBackup,"manifest.json.bak"));
                string unknown=AudioFixture(folder,"unknown-files",old); File.WriteAllText(Path.Combine(unknown,"user-note.txt"),"Preserve unknown files",Files.Utf8);
                string changed=AudioFixture(folder,"changed-audio",old); File.AppendAllText(Path.Combine(changed,"word-0.mp3"),"modified",Files.Utf8);
                string unmanifested=Path.Combine(folder,"history-audio","no-manifest"); Directory.CreateDirectory(unmanifested); File.WriteAllText(Path.Combine(unmanifested,"keep.txt"),"unmanaged",Files.Utf8);
                string mismatch=AudioFixture(folder,"mismatch",old); File.WriteAllText(Path.Combine(mismatch,"manifest.json"),Files.Json().Serialize(new { Version=1,QueryId="another-id",Created=old.ToString("o"),Word=new object[0],Explanation=new object[0] }),Files.Utf8);
                string linkTarget=Path.Combine(folder,"link-target"); Directory.CreateDirectory(linkTarget); File.WriteAllText(Path.Combine(linkTarget,"keep.txt"),"must survive",Files.Utf8);
                string link=Path.Combine(folder,"history-audio","linked-directory"); bool linked=CreateSymbolicLink(link,linkTarget,3);
                var result=history.ApplyReleaseRetention(folder,now);
                check(result.ExpiredQueryIds.Contains("expired") && result.ExpiredQueryIds.Contains("canceled") && !result.CurrentQueryIds.Contains("expired"),"Ordinary history older than fifteen days is deleted using its original creation time");
                check(result.CurrentQueryIds.Contains("boundary") && result.CurrentQueryIds.Contains("fresh"),"The exact fifteen-day boundary and newer records remain");
                check(result.CurrentQueryIds.Contains("saved") && history.FindFavorite(saved.Kind,saved.Term).Saved && Directory.Exists(savedAudio) && Directory.Exists(favoriteAudio),"Saved favorites, their full snapshots, associated queries and both audio locations remain indefinitely");
                check(history.FindFavorite(canceled.Kind,canceled.Term)==null && result.ExpiredFavoriteIds.Contains(canceledFavorite.Id) && !Directory.Exists(canceledAudio),"An old query's canceled favorite snapshot and managed audio expire even when the favorite manifest was created recently");
                check(history.FindFavorite(canceledFresh.Kind,canceledFresh.Term)!=null && Directory.Exists(canceledFreshAudio),"A canceled favorite whose original query is still within fifteen days stays available");
                check(history.FindFavorite(savedPartial.Kind,savedPartial.Term).Saved && Directory.Exists(savedPartialAudio) && File.Exists(Path.Combine(savedPartialAudio,"word-0.mp3")) && !File.Exists(Path.Combine(savedPartialAudio,"manifest.json")),"A saved favorite with failed or canceled partial synthesis keeps its journal and audio indefinitely without a playable manifest");
                check(!Directory.Exists(canceledPartialAudio) && !Directory.Exists(orphanPartialAudio),"Expired unsaved and orphan favorite partial audio is removed using assets.json plus its backup, allowing declared but unwritten segments");
                check(result.CurrentQueryIds.Contains("invalid-date") && result.CurrentQueryIds.Contains("foreign"),"Unknown dates and foreign sources are preserved");
                check(!result.CurrentQueryIds.Contains("mapped-query") && history.Run("SELECT id FROM lookups WHERE id=?",legacy.Id).Count==0 && history.Run("SELECT legacy_id FROM query_links WHERE legacy_id=?",legacy.Id).Count==0,"An expired release query removes only its verified legacy lookup and mapping");
                check(result.CurrentQueryIds.Contains("ambiguous-query") && history.Run("SELECT id FROM lookups WHERE id=?",ambiguous.Id).Count==1,"A mismatching legacy mapping is retained for safety");
                check(!Directory.Exists(expiredAudio) && !Directory.Exists(orphan) && !Directory.Exists(withBackup) && Directory.Exists(orphanFresh) && Directory.Exists(audioBoundary),"Expired manifested cache and partial orphan audio, including retired profiles and backups, are removed while recent and boundary caches remain");
                check(Directory.Exists(unknown) && File.Exists(Path.Combine(unknown,"word-0.mp3")) && Directory.Exists(changed) && Directory.Exists(unmanifested) && Directory.Exists(mismatch),"Unknown, changed, unmanifested and identity-mismatched directories are never partially deleted");
                if(linked) check(Directory.Exists(link) && File.Exists(Path.Combine(linkTarget,"keep.txt")),"Reparse-point directories and their targets are untouched");
                else results.Add("SKIP: This process could not create the isolated symlink fixture; production code still rejects reparse points.");
                var freshResult=history.ApplyReleaseRetention(folder,now);
                check(freshResult.ExpiredQueryIds.Count==0 && freshResult.ExpiredFavoriteIds.Count==0,"Repeating retention at the same time is idempotent");
                history.SetFavorite(saved,false);
                history.SetFavorite(savedPartial,false);
                var unpinned=history.ApplyReleaseRetention(folder,now);
                check(unpinned.ExpiredQueryIds.Contains(saved.Id) && unpinned.ExpiredFavoriteIds.Contains(favorite.Id) && history.FindFavorite(saved.Kind,saved.Term)==null && !Directory.Exists(savedAudio) && !Directory.Exists(favoriteAudio),"Canceling an expired favorite makes its query, snapshot and both managed audio groups eligible immediately");
                check(unpinned.ExpiredFavoriteIds.Contains(savedPartialFavorite.Id) && !Directory.Exists(savedPartialAudio),"Canceling an expired partial-audio favorite removes its retained journal and partial file");
                var later=history.ApplyReleaseRetention(folder,now.AddTicks(1));
                check(later.ExpiredQueryIds.Contains("boundary") && !Directory.Exists(audioBoundary),"Moving past the exact fifteen-day boundary expires the ordinary record and orphan cache");
            }
            using(var reopened=new HistoryStore(database)) check(reopened.Run("SELECT id FROM queries WHERE id='mapped-query'").Count==0 && reopened.Run("SELECT text FROM queries WHERE text='mapped source'").Count==0,"Reopening the database cannot re-import a deleted mapped legacy result");
            SelectionLedgerTests.Run(folder,check);
            results.Add("Only a newly created tests directory was used; no real profile, user database or running application was read or deleted.");
            File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); return 0;
        } catch(Exception e) { results.Add("FAIL: "+e); File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); return 1; }
    }
}
}
