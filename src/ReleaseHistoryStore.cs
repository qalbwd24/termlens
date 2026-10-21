using System.Collections.Generic;
using System.Linq;

namespace TermLens {
public sealed partial class HistoryStore {
    public List<Lookup> ReleaseHistoryIndex() {
        return Run("SELECT q.id,q.text,q.created,q.kind,q.status,COALESCE(f.saved,0) FROM queries q LEFT JOIN favorites f ON f.favorite_key=q.kind||':'||q.term_key ORDER BY q.created DESC")
            .Select(r=>new Lookup { Id=r[0],Term=r[1],Created=r[2],Kind=r[3],Status=r[4],Saved=r[5]=="1" }).ToList();
    }
    public Lookup ReleaseHistoryEntry(string id) {
        return Run("SELECT q.id,q.text,q.answer,q.created,q.model,q.prompt,q.context,q.kind,q.source,q.source_text,q.status,COALESCE(f.saved,0) FROM queries q LEFT JOIN favorites f ON f.favorite_key=q.kind||':'||q.term_key WHERE q.id=?",id)
            .Select(r=>new Lookup { Id=r[0],Term=r[1],Answer=r[2],Created=r[3],Model=r[4],Prompt=r[5],Context=r[6],Kind=r[7],Source=r[8],SourceText=r[9],Status=r[10],Saved=r[11]=="1" }).FirstOrDefault();
    }
}
}
