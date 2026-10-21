using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TermLens {
public static class LookupContextTests {
    public static int Run(string root) {
        string folder=Path.Combine(root,"tests","lookup-context-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));
        Directory.CreateDirectory(folder); var results=new List<string>();
        Action<bool,string> check=delegate(bool value,string message) { if(!value) throw new Exception(message); results.Add("PASS: "+message); };
        try {
            var recent=new List<string>{"geometry"};
            var ordinary=LookupContext.Build("perpendicular",null,recent);
            check(ordinary.UserText=="perpendicular" && ordinary.SourceText=="" && !ordinary.Truncated,"Ordinary lookup text stays unchanged");
            var rootContext=LookupContext.FromSource("entry-root","The sides meet at a right angle.","The sides are **perpendicular**.","perpendicular");
            var request=LookupContext.Build("perpendicular",rootContext,recent);
            check(request.UserText.Contains("The sides meet at a right angle.") && request.UserText.Contains("The sides are **perpendicular**."),"Internal lookup includes its selected entry's original text and answer");
            var stored=Files.Json().Deserialize<Dictionary<string,object>>(request.ContextJson);
            string expected=Requests.UserText(request.UserText,request.RecentTerms);
            check((string)stored["requestUserMessage"]==expected,"Stored context records the exact final user message including recent terms");
            var body=Requests.Body(new Settings(),request.UserText,request.RecentTerms);
            var encoded=Files.Json().Deserialize<Dictionary<string,object>>(Files.Json().Serialize(body));
            var messages=(ArrayList)encoded["messages"];
            check((string)((Dictionary<string,object>)messages[messages.Count-1])["content"]==expected,"Actual request-body payload matches the saved context snapshot");
            var first=rootContext.Next("perpendicular","First temporary child response", "angle");
            var second=first.Next("angle","Immediate parent says an angle is formed by two rays.","rays");
            var nested=LookupContext.Build("rays",second);
            check(nested.UserText.Contains("entry-root") && nested.UserText.Contains("Immediate parent says") && !nested.UserText.Contains("First temporary child response"),"Nested lookup retains root plus immediate parent without accumulating earlier children");
            var duplicate=LookupContext.Build("sides",rootContext.Next("The sides meet at a right angle.","The sides are perpendicular."));
            var duplicateSaved=Files.Json().Deserialize<Dictionary<string,object>>(duplicate.ContextJson);
            var duplicateContext=(Dictionary<string,object>)duplicateSaved["lookupContext"];
            check((string)duplicateContext["parentQuery"]=="" && (string)duplicateContext["parentAnswer"]=="","Equivalent parent and root are deduplicated despite Markdown formatting");
            var restored=LookupContext.FromSaved(nested.ContextJson);
            check(restored!=null && LookupContext.Build("rays",restored).UserText==nested.UserText,"Saved lookup restores the actual root and parent context");
            check(LookupContext.FromSaved("[\"older-term\"]")==null,"Legacy recent-terms-only history remains readable");
            var longRoot=LookupContext.FromSource("large",new string('"',24000),new string('x',14000)+"FOCUS-WORD "+new string('y',1000),"FOCUS-WORD");
            var focused=LookupContext.Build("FOCUS-WORD",longRoot);
            check(focused.UserText.Contains("FOCUS-WORD") && focused.Truncated,"Oversized source is bounded and explicitly marked as shortened");
            var boundedContext=longRoot;
            for(int i=0;i<40;i++) boundedContext=boundedContext.Next("parent-"+i,new string('\\',6000)+" current", "current");
            var bounded=LookupContext.Build("current",boundedContext);
            var savedBounded=Files.Json().Deserialize<Dictionary<string,object>>(bounded.ContextJson);
            check(Files.Json().Serialize(savedBounded["lookupContext"]).Length<=LookupContext.MaxReferenceCharacters,"Forty nested selections remain within serialized reference budget including escapes");
            check(bounded.UserText.Contains("parent-39") && !bounded.UserText.Contains("parent-38"),"Only the direct parent is retained after repeated selection");
            check(LookupContext.Build("perpendicular",rootContext,recent).UserText==request.UserText,"Building child context never mutates the root snapshot");
            using(var history=new HistoryStore(Path.Combine(folder,"history.sqlite3"))) {
                var item=new Lookup { Id="context-query",Term="rays",Answer="A ray starts at one point.",Created=DateTime.UtcNow.ToString("o"),Kind="quick",Source="quick",SourceText=nested.SourceText,Context=nested.ContextJson,Model="test",Prompt="test" };
                history.RecordQuery(item.Id,item.Kind,item.Source,item.Term,item.Answer,item.SourceText,item.Context,item.Model,item.Prompt,item.Created,"complete","",null);
                check(history.Queries("",false,10).Any(q=>q.Id==item.Id && q.Context==nested.ContextJson),"Isolated history preserves the full actual context snapshot");
                var favorite=history.SetFavorite(item,true);
                check(favorite.Context==nested.ContextJson && favorite.SourceText==nested.SourceText,"Isolated favorite preserves actual source context");
            }
            results.Add("No API request, user database, startup entry or running instance was touched.");
            File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); return 0;
        } catch(Exception e) { results.Add("FAIL: "+e); File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); return 1; }
    }
}
}
