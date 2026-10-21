using System;
using System.Diagnostics;
using System.IO;

namespace TermLens {
public static partial class Tests {
    public static int V2Performance(string root) {
        string folder=TestRoot(root,"v2-performance"); int exit=0;
        try {
            var writer=new SessionFileWriter();
            var session=new WorkSession { Draft="A draft with 中文 and an unsent question",Scroll=35 };
            session.Entries.Add(new WorkEntry { Text="cache",Answer="A **stored** result.",Finished=true });
            string working=Path.Combine(folder,"working-session.json"),closed=Path.Combine(folder,"last-session.json");
            Check(writer.Save(working,session),"First session snapshot is written");
            string first=File.ReadAllText(working,Files.Utf8);
            var watch=Stopwatch.StartNew(); int writes=0;
            for(int i=0;i<100;i++) if(writer.Save(working,session)) writes++;
            watch.Stop();
            Check(writes==0 && !File.Exists(working+".bak"),"Repeated unchanged saves do not replace the session file");
            results.Add("INFO: 100 unchanged save checks including JSON serialization took "+watch.Elapsed.TotalMilliseconds.ToString("F2")+" ms; disk writes="+writes+"; not display FPS.");
            session.Draft="An edited draft"; session.Scroll=120;
            session.Entries[0].Answer+="\nA completed explanation.";
            Check(writer.Save(working,session),"Changed draft, scroll and answer trigger a new snapshot");
            Check(File.ReadAllText(working+".bak",Files.Utf8)==first,"Atomic save preserves the previous snapshot as backup");
            var restored=Files.Json().Deserialize<WorkSession>(File.ReadAllText(working,Files.Utf8));
            Check(restored.Draft==session.Draft && restored.Scroll==120 && restored.Entries[0].Answer==session.Entries[0].Answer,"Persisted session restores the latest draft, scroll and answer");
            Check(writer.Save(closed,session) && File.ReadAllText(closed,Files.Utf8)==File.ReadAllText(working,Files.Utf8),"Closing snapshot is saved independently from working snapshot");
            File.Delete(working);
            Check(writer.Save(working,session) && File.Exists(working),"A missing session file is recreated even when content is unchanged");
            string retryDirectory=Path.Combine(folder,"retry"),retryPath=Path.Combine(retryDirectory,"session.json");
            bool failed=false;
            try { writer.Save(retryPath,session); } catch(DirectoryNotFoundException) { failed=true; }
            Directory.CreateDirectory(retryDirectory);
            Check(failed && writer.Save(retryPath,session),"A failed write does not suppress a later successful retry");

            var cache=new FormulaPictureCache(3); string value;
            cache.Put("hot","picture-hot"); cache.Put("cold","picture-cold"); cache.Put("invalid",null);
            Check(cache.TryGet("hot",out value) && value=="picture-hot","Reading a formula refreshes its cache priority");
            cache.Put("new","picture-new");
            Check(cache.Count==3 && !cache.TryGet("cold",out value) && cache.TryGet("hot",out value),"Overflow evicts only the least recently used formula and retains hot formulas");
            Check(cache.TryGet("invalid",out value) && value==null,"Invalid formula results remain cached to avoid repeated parsing");
            cache.Put("hot","replacement"); cache.Put("another","picture-another");
            Check(cache.Count==3 && cache.TryGet("hot",out value) && value=="replacement" && !cache.TryGet("new",out value),"Updating a cached formula refreshes its priority without growing the cache");
            string picture=MathRender.Picture(@"\frac{7}{13}",13,517,true);
            int rendered=MathRender.RenderedCount;
            Check(picture!=null && MathRender.Picture(@"\frac{7}{13}",13,517,true)==picture && MathRender.RenderedCount==rendered,"Real formula rendering reuses the cached picture on repeat");
        } catch(Exception e) { exit=1; results.Add(e.ToString()); }
        File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8);
        return exit;
    }
}
}
