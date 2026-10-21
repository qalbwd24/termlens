using System;
using System.IO;
using System.Linq;

namespace TermLens {
public static partial class Tests {
    public static int Library(string root) {
        string folder=TestRoot(root,"library"); int exit=0;
        try {
            var store=new HistoryStore(Path.Combine(folder,"history.sqlite3"));
            store.RecordQuery("q-explain","explain","workspace","perpendicular","A simple answer.","perpendicular in code","{\"recent\":[\"angle\"]}","deepseek-flash","prompt","2026-09-30T00:00:00Z","complete","");
            store.RecordQuery("q-ask","ask","workspace","make a sentence","Use it in a sentence.","","{}","deepseek-flash","teacher","2026-09-30T00:01:00Z","complete","");
            store.RecordQuery("q-polish","polish","workspace","why code fails","Why does the code fail?","","{}","deepseek-flash","polish","2026-09-30T00:02:00Z","complete","");
            store.RecordQuery("q-quick","quick","quick","recursion","A function calls itself.","","{}","deepseek-flash","prompt","2026-09-30T00:03:00Z","complete","");
            var all=store.Queries("",false,20); Check(all.Count==4 && all.Select(item=>item.Kind).Distinct().Count()==4,"All query classes share one searchable record format with a category");
            var quick=all.Single(item=>item.Kind=="quick"); var favorite=store.SetFavorite(quick,true); Check(store.Favorites().Count==1 && store.FindFavorite("quick","recursion").Saved,"Quick dictionary records can be favorited independently of text lookup terms");
            favorite.AudioState="ready"; favorite.WordAudio.Add("favorites/"+favorite.Id+"/word-0.mp3"); favorite.ExplanationAudio.Add("favorites/"+favorite.Id+"/explanation-0.mp3"); store.StoreFavorite(favorite); var restored=store.FindFavorite("quick","recursion"); Check(restored.AudioState=="ready" && restored.WordAudio.Count==1 && restored.ExplanationAudio.Count==1,"Favorites preserve separate word and explanation audio references");
            File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8);
        } catch(Exception e) { exit=1; results.Add(e.ToString()); File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); }
        return exit;
    }
}
}
