using System;
using System.Collections.Generic;
using System.Linq;

namespace TermLens {
public sealed class SelectionEvent {
    public string Id,Text,NormalizedKey,Created,Kind,Source,SceneId,SceneName;
}
public sealed partial class HistoryStore {
    void InitSelectionLedger() {
        Run("CREATE TABLE IF NOT EXISTS selection_events(id TEXT PRIMARY KEY,text TEXT NOT NULL,normalized_key TEXT NOT NULL,created TEXT NOT NULL,kind TEXT NOT NULL,source TEXT NOT NULL,scene_id TEXT NOT NULL,scene_name TEXT NOT NULL)");
        Run("CREATE INDEX IF NOT EXISTS selection_events_created ON selection_events(created,id)");
        Run("CREATE INDEX IF NOT EXISTS selection_events_key ON selection_events(kind,normalized_key)");
        Run("CREATE TABLE IF NOT EXISTS selection_ledger_meta(key TEXT PRIMARY KEY,value TEXT NOT NULL)");
        if(Run("SELECT value FROM selection_ledger_meta WHERE key='backfill-version'").Count>0) return;
        Run("BEGIN IMMEDIATE");
        try {
            // Only a verified same-result link establishes that two IDs represent one event.
            foreach(var row in Run("SELECT q.id,q.text,q.created,q.kind,q.source,q.context FROM queries q WHERE q.kind IN('explain','quick') AND q.source IN('release-selection','release-context','legacy')"))
                RecordSelection(new Lookup { Id=row[0],Term=row[1],Created=row[2],Kind=row[3],Source=row[4],Context=row[5] });
            // A mapped legacy row is not imported into queries. Keep it only if
            // the link cannot establish that it is the same selection event.
            foreach(var row in Run("SELECT l.id,l.term,l.created,l.context FROM lookups l WHERE NOT EXISTS(SELECT 1 FROM queries q WHERE q.id=l.id)"))
                RecordSelection(new Lookup { Id=row[0],Term=row[1],Created=row[2],Context=row[3],Kind="explain",Source="legacy" });
            Run("INSERT INTO selection_ledger_meta VALUES('backfill-version','1')");
            Run("COMMIT");
        } catch { Run("ROLLBACK"); throw; }
    }
    public void RecordSelection(Lookup item) {
        if(item==null || String.IsNullOrWhiteSpace(item.Id)) throw new ArgumentException("Selection event requires an ID.","item");
        if(String.IsNullOrWhiteSpace(item.Term) || (item.Kind!="explain" && item.Kind!="quick") || (item.Source!="release-selection" && item.Source!="release-context" && item.Source!="legacy")) return;
        if(item.Source=="legacy" && VerifiedSelectionTarget(item.Id)!=null) return;
        var profile=ReleasePromptSnapshot.FromSaved(item.Context);
        // The earliest event is immutable: completion/cancellation cannot add another count
        // or replace the original selection/time with later cache metadata.
        Run("INSERT OR IGNORE INTO selection_events VALUES(?,?,?,?,?,?,?,?)",item.Id,item.Term,Key(item.Term),item.Created??DateTime.UtcNow.ToString("o"),item.Kind??"",item.Source??"",profile==null?"":profile.Id??"",profile==null?"":profile.Name??"");
    }
    string VerifiedSelectionTarget(string legacyId) {
        foreach(var row in Run("SELECT q.id,q.context,l.context FROM query_links link JOIN queries q ON q.id=link.query_id JOIN lookups l ON l.id=link.legacy_id WHERE link.legacy_id=? AND q.id<>link.legacy_id AND q.kind IN('explain','quick') AND q.source IN('release-selection','release-context') AND q.text=l.term AND q.answer=l.answer",legacyId)) {
            var target=ReleasePromptSnapshot.FromSaved(row[1]); var old=ReleasePromptSnapshot.FromSaved(row[2]);
            if(target!=null && old!=null && target.Id!=old.Id) continue;
            return row[0];
        }
        return null;
    }
    public void DeleteSelection(string id) {
        Run("DELETE FROM selection_events WHERE id=? OR id IN(SELECT query_id FROM query_links WHERE legacy_id=?) OR id IN(SELECT legacy_id FROM query_links WHERE query_id=?)",id,id,id);
    }
    public List<SelectionEvent> SelectionEvents(bool favoritesOnly=false) {
        string filter=favoritesOnly?" WHERE EXISTS(SELECT 1 FROM favorites f WHERE f.saved=1 AND f.favorite_key=s.kind||':'||s.normalized_key)":"";
        return Run("SELECT s.id,s.text,s.normalized_key,s.created,s.kind,s.source,s.scene_id,s.scene_name FROM selection_events s"+filter+" ORDER BY s.created,s.id")
            .Select(r=>new SelectionEvent { Id=r[0],Text=r[1],NormalizedKey=r[2],Created=r[3],Kind=r[4],Source=r[5],SceneId=r[6],SceneName=r[7] }).ToList();
    }
}
}
