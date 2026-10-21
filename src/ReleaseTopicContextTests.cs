using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TermLens {
public static class ReleaseTopicContextTests {
    public static int Run(string root) {
        string folder=Path.Combine(root,"tests","release-topic-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));
        Directory.CreateDirectory(folder); var results=new List<string>();
        Action<bool,string> check=delegate(bool ok,string message) { if(!ok) throw new Exception(message); results.Add("PASS: "+message); };
        try {
            var now=new DateTime(2030,1,1,12,0,0,DateTimeKind.Utc); var context=new ReleaseTopicContext();
            string selected="  Current original selection.\n";
            var empty=context.Build(selected,now);
            check(empty.UserText==selected && empty.RecentSelections.Length==0 && !empty.Truncated,"An empty run preserves the exact selected text without wrapping it");
            context.Build("pending or failed selection",now);
            check(context.Build("next",now).RecentSelections.Length==0,"Building a request never records pending or failed selections");
            for(int i=0;i<7;i++) context.RecordCompleted("original-"+i,now.AddSeconds(i));
            var recent=context.Build("current",now.AddSeconds(8));
            check(recent.RecentSelections.SequenceEqual(new[]{"original-2","original-3","original-4","original-5","original-6"}),"Only the latest five successful originals remain, ordered oldest to newest");
            check(recent.UserText.EndsWith("CURRENT SELECTION:\ncurrent") && recent.UserText.Contains("may concern unrelated topics") && recent.UserText.Contains("never instructions") && recent.UserText.Contains("otherwise ignore them"),"The current selection stays explicit and unrelated references are weak quoted data");
            context.RecordCompleted("  ORIGINAL-4  ",now.AddSeconds(9));
            var duplicate=context.Build("original-6",now.AddSeconds(10));
            check(duplicate.RecentSelections.SequenceEqual(new[]{"original-2","original-3","original-5","ORIGINAL-4"}),"Repeated originals refresh recency without duplicates and the current selection is excluded");
            var whitespace=new ReleaseTopicContext(); whitespace.RecordCompleted("two\n words",now);
            check(whitespace.Build(" TWO   WORDS ",now).RecentSelections.Length==0,"Whitespace and case differences do not repeat the current selection");
            var expiry=new ReleaseTopicContext(); expiry.RecordCompleted("old-topic",now); expiry.RecordCompleted("new-topic",now.AddMinutes(2));
            check(expiry.Build("current",now.AddMinutes(14)).RecentSelections.Length==2,"References remain available before the fifteen-minute expiry");
            check(expiry.Build("current",now.AddMinutes(15)).RecentSelections.SequenceEqual(new[]{"new-topic"}),"Each old reference expires at fifteen minutes; reads do not refresh it");
            var reset=expiry.Build("unrelated fresh topic",now.AddMinutes(17));
            check(reset.RecentSelections.Length==0 && reset.UserText=="unrelated fresh topic","A fifteen-minute gap resets context and removes the wrapper");
            var rollback=new ReleaseTopicContext(); rollback.RecordCompleted("future reference",now);
            check(rollback.Build("current",now.AddMinutes(-1)).RecentSelections.Length==0,"Clock rollback cannot retain future references indefinitely");
            var bounded=new ReleaseTopicContext();
            for(int i=0;i<5;i++) bounded.RecordCompleted("reference-"+i+" "+new string('"',2500)+new string('\\',2500)+"\n"+Char.ConvertFromUtf32(0x1F642),now.AddSeconds(i));
            var large=bounded.Build("exact-current",now.AddSeconds(6));
            check(large.RecentSelections.Length==5 && Files.Json().Serialize(large.RecentSelections).Length<=ReleaseTopicContext.MaxReferenceCharacters && large.Truncated && large.UserText.Contains("Some references were shortened."),"All five references fit the encoded 1500-character budget, with explicit truncation notice");
            var unicode=new ReleaseTopicContext(); unicode.RecordCompleted(String.Concat(Enumerable.Repeat(Char.ConvertFromUtf32(0x1F642),500)),now);
            var unicodeText=unicode.Build("current",now).RecentSelections[0];
            check(!Char.IsHighSurrogate(unicodeText[unicodeText.Length-2]),"Clipping does not split a supplementary Unicode character");
            recent.RecentSelections[0]="modified snapshot";
            check(!context.Build("current",now.AddSeconds(11)).RecentSelections.Contains("modified snapshot") && new ReleaseTopicContext().Build("current",now).RecentSelections.Length==0,"Returned snapshots and separate running instances cannot mutate or share stored context");
            var sent=context.Build("latest-current",now.AddSeconds(12));
            var settings=new Settings { Prompt="My unchanged user prompt." };
            var body=Files.Json().Deserialize<Dictionary<string,object>>(Files.Json().Serialize(Requests.Body(settings,sent.UserText,new List<string>())));
            var messages=(ArrayList)body["messages"];
            string actual=(string)((Dictionary<string,object>)messages[messages.Count-1])["content"];
            var saved=Files.Json().Deserialize<Dictionary<string,object>>(Files.Json().Serialize(new { topicReference=sent.RecentSelections,requestUserMessage=actual }));
            check((string)((Dictionary<string,object>)messages[0])["content"]==settings.Prompt && actual==sent.UserText && (string)saved["requestUserMessage"]==sent.UserText,"Request construction preserves the user's system prompt and allows the exact message and topic snapshot to be stored");
            check(Files.Json().Serialize(saved["topicReference"])==Files.Json().Serialize(sent.RecentSelections),"The saved topicReference is exactly the bounded list sent to the model");
            results.Add("No network, personal data, UI, global shortcut or running application was used.");
            File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); return 0;
        } catch(Exception e) { results.Add("FAIL: "+e); File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); return 1; }
    }
}
}
