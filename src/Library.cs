using System;
using System.Collections.Generic;
using System.Linq;

namespace TermLens {
public sealed class Favorite {
    public string Id,QueryId,Kind,Term,Explanation,SourceText,Context,Created,Updated;
    public bool Saved;
    public string AudioState="pending",AudioError="",Voice="";
    public List<string> WordAudio=new List<string>(),ExplanationAudio=new List<string>();
}
public sealed partial class HistoryStore {
    void InitLibrary() {
        Run("CREATE TABLE IF NOT EXISTS queries(id TEXT PRIMARY KEY,kind TEXT NOT NULL,source TEXT NOT NULL,text TEXT NOT NULL,answer TEXT NOT NULL,source_text TEXT NOT NULL,context TEXT NOT NULL,model TEXT NOT NULL,prompt TEXT NOT NULL,created TEXT NOT NULL,status TEXT NOT NULL,error TEXT NOT NULL)");
        Run("CREATE INDEX IF NOT EXISTS queries_kind_created ON queries(kind,created DESC)");
        if(!Run("PRAGMA table_info(queries)").Any(r=>r[1]=="term_key")) {
            Run("ALTER TABLE queries ADD COLUMN term_key TEXT NOT NULL DEFAULT ''");
            foreach(var row in Run("SELECT id,text FROM queries")) Run("UPDATE queries SET term_key=? WHERE id=?",Key(row[1]),row[0]);
        }
        Run("CREATE TABLE IF NOT EXISTS query_links(legacy_id TEXT PRIMARY KEY,query_id TEXT NOT NULL)");
        Run("CREATE TABLE IF NOT EXISTS favorites(id TEXT PRIMARY KEY,favorite_key TEXT UNIQUE NOT NULL,kind TEXT NOT NULL,term TEXT NOT NULL,saved INTEGER NOT NULL,record TEXT NOT NULL)");
        var newLegacy=Run("SELECT l.id,l.term,l.created FROM lookups l WHERE NOT EXISTS(SELECT 1 FROM query_links q WHERE q.legacy_id=l.id) AND NOT EXISTS(SELECT 1 FROM queries q WHERE q.id=l.id)");
        Run("INSERT OR IGNORE INTO queries SELECT l.id,'explain','legacy',l.term,l.answer,'',l.context,l.model,l.prompt,l.created,'complete','',l.term_key FROM lookups l WHERE NOT EXISTS(SELECT 1 FROM query_links q WHERE q.legacy_id=l.id)");
        foreach(var row in Run("SELECT l.id,l.term,l.answer,l.created,l.context FROM terms t JOIN lookups l ON l.id=(SELECT id FROM lookups WHERE term_key=t.term_key ORDER BY created DESC LIMIT 1) WHERE t.saved=1")) {
            if(FindFavorite("explain",row[1])==null) SetFavorite(new Lookup { Id=row[0],Term=row[1],Answer=row[2],Created=row[3],Context=row[4] },true);
        }
        InitSelectionLedger();
        // Existing import already checks lookup IDs; only newly imported rows need
        // another event after the one-time ledger backfill has been completed.
        foreach(var row in newLegacy) RecordSelection(new Lookup { Id=row[0],Term=row[1],Created=row[2],Kind="explain",Source="legacy" });
    }
    public void RecordQuery(string id,string kind,string source,string text,string answer,string sourceText,string context,string model,string prompt,string created,string status,string error,string legacyId=null) {
        Run("INSERT OR REPLACE INTO queries VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?)",id,kind,source,text??"",answer??"",sourceText??"",context??"{}",model??"",prompt??"",created??DateTime.UtcNow.ToString("o"),status,error??"",Key(text??""));
        if(!String.IsNullOrEmpty(legacyId) && legacyId!=id) Run("INSERT OR REPLACE INTO query_links VALUES(?,?)",legacyId,id);
        RecordSelection(new Lookup { Id=id,Kind=kind,Source=source,Term=text,Created=created,Context=context });
        if(!String.IsNullOrEmpty(legacyId) && legacyId!=id) {
            if(VerifiedSelectionTarget(legacyId)==id) Run("DELETE FROM selection_events WHERE id=?",legacyId);
        }
    }
    public List<Lookup> Queries(string query,bool savedOnly,int limit,string kind="") {
        string pattern="%"+(query??"").Replace("!","!!").Replace("%","!%").Replace("_","!_")+"%";
        return Run("SELECT q.id,q.text,q.answer,q.created,q.model,q.prompt,q.context,q.kind,q.source,q.source_text,q.status,COALESCE(f.saved,0) FROM queries q LEFT JOIN favorites f ON f.favorite_key=q.kind||':'||q.term_key WHERE (q.text LIKE ? ESCAPE '!' OR q.answer LIKE ? ESCAPE '!') AND (?='' OR q.kind=?) AND (?='0' OR f.saved=1) ORDER BY q.created DESC LIMIT "+Math.Max(1,Math.Min(1000000,limit)),pattern,pattern,kind,kind,savedOnly?"1":"0")
            .Select(r=>new Lookup { Id=r[0],Term=r[1],Answer=r[2],Created=r[3],Model=r[4],Prompt=r[5],Context=r[6],Kind=r[7],Source=r[8],SourceText=r[9],Status=r[10],Saved=r[11]=="1" }).ToList();
    }
    static string FavoriteKey(string kind,string text) { return kind+":"+Key(text); }
    public Favorite FindFavorite(string kind,string text) { var rows=Run("SELECT record FROM favorites WHERE favorite_key=?",FavoriteKey(kind,text)); return rows.Count==0?null:Files.Json().Deserialize<Favorite>(rows[0][0]); }
    public Favorite SetFavorite(Lookup lookup,bool saved) {
        var favorite=FindFavorite(lookup.Kind,lookup.Term); string now=DateTime.UtcNow.ToString("o");
        if(favorite==null) favorite=new Favorite { Id=Guid.NewGuid().ToString("N"),Created=now,Kind=lookup.Kind,Term=lookup.Term };
        if(saved && (favorite.Explanation!=lookup.Answer || favorite.Term!=lookup.Term)) { favorite.WordAudio.Clear(); favorite.ExplanationAudio.Clear(); favorite.AudioState="pending"; favorite.AudioError=""; }
        if(saved) { favorite.QueryId=lookup.Id; favorite.Term=lookup.Term; favorite.Explanation=lookup.Answer; favorite.SourceText=lookup.SourceText??""; favorite.Context=lookup.Context??"{}"; }
        favorite.Saved=saved; favorite.Updated=now; StoreFavorite(favorite);
        if(lookup.Kind=="explain") SaveWord(lookup.Term,saved);
        return favorite;
    }
    public void StoreFavorite(Favorite item) { Run("INSERT OR REPLACE INTO favorites VALUES(?,?,?,?,?,?)",item.Id,FavoriteKey(item.Kind,item.Term),item.Kind,item.Term,item.Saved?"1":"0",Files.Json().Serialize(item)); }
    public List<Favorite> Favorites() { return Run("SELECT record FROM favorites WHERE saved=1 ORDER BY rowid").Select(row=>Files.Json().Deserialize<Favorite>(row[0])).ToList(); }
}
}
