using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TermLens {
public static class SelectionLedgerTests {
    public static void Run(string folder,Action<bool,string> check) {
        string database=Path.Combine(folder,"selection-ledger.sqlite3");
        DateTime now=new DateTime(2030,2,20,12,0,0,DateTimeKind.Utc);
        string original="  可变对象\r\nmutable objects 😀  ";
        using(var history=new HistoryStore(database)) {
            var profile=PromptLibrary.CreateTechnical();
            var first=new Lookup { Id="repeat-1",Term=original,Created=now.AddDays(-16).ToString("o"),Kind="explain",Source="release-selection",Context=ReleasePromptSnapshot.Attach("{}",profile) };
            history.RecordSelection(first); history.RecordSelection(first);
            history.RecordQuery(first.Id,first.Kind,first.Source,first.Term,"cached explanation","root source",first.Context,"fixture","prompt snapshot",first.Created,"complete","");
            var second=new Lookup { Id="repeat-2",Term=original,Created=now.ToString("o"),Kind="explain",Source="release-context" };
            history.RecordSelection(second); // canceled before an answer is saved
            var events=history.SelectionEvents();
            check(events.Count==2 && events.All(e=>e.Text==original) && events[0].SceneId==profile.Id && events[0].SceneName==profile.Name,"Same text keeps separate event IDs, exact Chinese/whitespace, time and scene; completion is idempotent");
            var retained=history.RetainReleaseHistory(now);
            check(retained.ExpiredQueryIds.Contains(first.Id) && history.SelectionEvents().Count==2 && history.Run("SELECT id FROM queries WHERE id=?",first.Id).Count==0,"Expired full content is removed while both original selections survive, including canceled lookup");
            string[] columns=history.Run("PRAGMA table_info(selection_events)").Select(r=>r[1]).ToArray();
            check(columns.Length==8 && !columns.Contains("answer") && !columns.Contains("context") && !columns.Contains("prompt"),"Long-term event schema contains only selection and small metadata, never answer/context/prompt/audio");
            var old=history.Add("mapped selection","old answer","fixture","fixture","{}");
            // Simulate the old ledger backfill before a current release result establishes its link.
            history.RecordSelection(old);
            history.RecordQuery("mapped-current","explain","release-selection",old.Term,old.Answer,"","{}","fixture","fixture",now.ToString("o"),"complete","",old.Id);
            check(history.SelectionEvents().Count(e=>e.Text==old.Term)==1 && history.SelectionEvents().Any(e=>e.Id=="mapped-current"),"Verified legacy mapping keeps one canonical event instead of two IDs for the same lookup");
            history.RecordSelection(old);
            check(history.SelectionEvents().Count(e=>e.Text==old.Term)==1,"Later legacy completion cannot re-add an alias event after a verified mapping");
            history.RecordQuery("foreign-event","explain","unknown-import","foreign","answer","","{}","fixture","fixture",now.ToString("o"),"complete","");
            history.RecordQuery("ask-event","ask","release-selection","ask text","answer","","{}","fixture","fixture",now.ToString("o"),"complete","");
            check(!history.SelectionEvents().Any(e=>e.Id=="foreign-event" || e.Id=="ask-event"),"Unknown sources and non-Explain v2 records are excluded from selection statistics");
            var otherProfile=PromptLibrary.CreateEveryday();
            var differentScene=history.Add("same words different scene","same answer","fixture","fixture",ReleasePromptSnapshot.Attach("{}",otherProfile)); history.RecordSelection(differentScene);
            history.RecordQuery("scene-current","explain","release-selection",differentScene.Term,differentScene.Answer,"",first.Context,"fixture","fixture",now.ToString("o"),"complete","",differentScene.Id);
            check(history.SelectionEvents().Count(e=>e.Text==differentScene.Term)==2,"A link with equal words and answers but two different known scenes is not merged");
            var saved=new Lookup { Id="saved-selection",Term="permanent",Answer="full content",Kind="explain",Source="release-selection",Created=now.AddDays(-30).ToString("o"),Context="{}" };
            history.RecordQuery(saved.Id,saved.Kind,saved.Source,saved.Term,saved.Answer,"source",saved.Context,"fixture","prompt",saved.Created,"complete",""); history.SetFavorite(saved,true);
            history.RetainReleaseHistory(now);
            check(history.Run("SELECT answer FROM queries WHERE id=?",saved.Id)[0][0]==saved.Answer && history.SelectionEvents(true).Any(e=>e.Id==saved.Id),"Saved selection retains its complete query and appears in favorites-only event export");
            history.SetFavorite(saved,false); history.RetainReleaseHistory(now);
            check(history.Run("SELECT id FROM queries WHERE id=?",saved.Id).Count==0 && history.SelectionEvents().Any(e=>e.Id==saved.Id) && !history.SelectionEvents(true).Any(e=>e.Id==saved.Id),"Canceling an old favorite clears full content and favorites filter but keeps its event");
            history.DeleteSelection(second.Id);
            check(!history.SelectionEvents().Any(e=>e.Id==second.Id),"Explicit user deletion removes the selected event rather than treating it as cache expiration");
            check(history.Run("PRAGMA integrity_check")[0][0]=="ok","Selection ledger database passes SQLite integrity_check");
        }
        using(var reopened=new HistoryStore(database)) {
            check(reopened.SelectionEvents().Count(e=>e.Text==original)==1 && reopened.Run("SELECT id FROM queries WHERE id='repeat-1'").Count==0,"Restart neither recreates expired answers nor duplicates or resurrects deleted events");
            // Construct a pre-ledger migration fixture, leaving source data intact.
            reopened.Run("DELETE FROM selection_ledger_meta"); reopened.Run("DELETE FROM selection_events");
        }
        using(var migrated=new HistoryStore(database)) {
            check(migrated.SelectionEvents().Count(e=>e.Text=="mapped selection")==1 && migrated.SelectionEvents().Any(e=>e.Id=="mapped-current"),"Initial ledger backfill respects verified legacy ID mappings");
            check(!migrated.SelectionEvents().Any(e=>e.Id=="foreign-event" || e.Id=="ask-event") && migrated.SelectionEvents().Count(e=>e.Text=="same words different scene")==2,"Backfill excludes unknown/non-Explain records and preserves distinct scene selections");
            check(migrated.Run("PRAGMA integrity_check")[0][0]=="ok","Migrated selection database passes SQLite integrity_check");
        }
    }
}
}
