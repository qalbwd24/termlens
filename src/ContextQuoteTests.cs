using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TermLens {
public static partial class Tests {
    static WorkEntry ContextEntry(string id,string text,string answer) { return new WorkEntry { Id=id,Text=text,Answer=answer,Finished=true }; }
    public static int ContextQuotes(string root) {
        string folder=TestRoot(root,"context-quotes");
        try {
            Check(!new Settings().QuoteAllWindow,"Whole-window Ask context is off by default");
            var legacyFragments=new[]{new WorkQuote { SourceId="legacy",SourceText="old source",Text="first saved fragment" },new WorkQuote { SourceId="legacy",SourceText="old source",Text="second saved fragment" }};
            Check(ContextQuotePlanner.Build(WorkMode.Ask,false,new WorkEntry[0],legacyFragments,"pending").Quotes.Count==2,"Distinct explicit fragments in restored old sessions remain intact");
            var settings=Files.Json().Deserialize<Settings>(Files.Json().Serialize(new Settings { QuoteAllWindow=true }));
            Check(settings.QuoteAllWindow,"Whole-window option survives settings serialization");
            var a=ContextEntry("a","first source","first answer");
            var b=ContextEntry("b","second source","second answer");
            var entries=new List<WorkEntry> { a,b };
            var manual=new WorkQuote { SourceId="a",SourceText=a.Text,Text=a.Answer };
            var off=ContextQuotePlanner.Build(WorkMode.Ask,false,entries,new[]{manual},"pending");
            Check(off.Quotes.Count==1 && off.Quotes[0].SourceId=="a","Default-off Ask includes only the explicit quote");
            foreach(var mode in new[]{WorkMode.Explain,WorkMode.Polish})
                Check(ContextQuotePlanner.Build(mode,true,entries,new WorkQuote[0],"pending").Quotes.Count==0,"Automatic context applies only to Ask: "+mode);
            var all=ContextQuotePlanner.Build(WorkMode.Ask,true,entries,new[]{manual,manual.Copy()},"pending");
            Check(all.Quotes.Count==2 && all.Quotes[0].SourceId=="a" && all.Quotes[1].SourceId=="b","Explicit and automatic references are deduplicated by source");
            var chronological=ContextQuotePlanner.Build(WorkMode.Ask,true,entries,new WorkQuote[0],"pending");
            Check(chronological.Quotes.Select(q=>q.SourceId).SequenceEqual(new[]{"a","b"}),"Automatic references retain chronological order");
            var sameSource=new WorkQuote { SourceId="other-id",SourceText=" first   source ",Text="**first answer**" };
            Check(ContextQuotePlanner.Build(WorkMode.Ask,false,entries,new[]{manual,sameSource},"pending").Quotes.Count==1,"Equivalent source and plain-text answer are included once");
            var differentAnswer=ContextEntry("different",a.Text,"a different answer");
            Check(ContextQuotePlanner.Build(WorkMode.Ask,true,new[]{a,differentAnswer},new WorkQuote[0],"pending").Quotes.Count==2,"Independent answers to the same source question are preserved");
            var failed=ContextEntry("failed","failed source","partial answer"); failed.Error="Stopped";
            var unfinished=ContextEntry("unfinished","unfinished source","partial answer"); unfinished.Finished=false;
            var image=ContextEntry("image","image source","image answer"); image.ImageOnly=true;
            var pending=ContextEntry("pending","CURRENT QUESTION","pending answer");
            var excluded=ContextQuotePlanner.Build(WorkMode.Ask,true,new[]{a,failed,unfinished,image,pending},new WorkQuote[0],"pending");
            Check(excluded.Quotes.Count==2 && excluded.Quotes[0].SourceId=="a" && excluded.Quotes[1].SourceId=="image","Failed, unfinished and current pending entries are excluded; completed image answers remain available");
            Check(excluded.Quotes[1].Text=="image answer" && excluded.Quotes[1].SourceText.Contains("image pixels are not included"),"Image references preserve their answer and explicitly describe missing image pixels");
            string message=MainWindow.WithQuotes("CURRENT QUESTION",excluded.Quotes);
            Check(message.IndexOf("CURRENT QUESTION",StringComparison.Ordinal)==message.LastIndexOf("CURRENT QUESTION",StringComparison.Ordinal),"Current learner question appears only once");
            var longSource=new WorkQuote { SourceId="long",SourceText=new string('s',900),Text="answer" };
            var uncut=ContextQuotePlanner.Build(WorkMode.Ask,false,entries,new[]{longSource},"pending");
            Check(uncut.Quotes[0].SourceText.Length==900 && MainWindow.WithQuotes("question",uncut.Quotes).Contains(new string('s',900)),"Source text is no longer silently cut at 600 characters");
            var old=ContextEntry("old","old source",new string('o',600));
            var newest=ContextEntry("new","new source",new string('n',600));
            var bounded=ContextQuotePlanner.Build(WorkMode.Ask,true,new[]{old,newest},new[]{manual},"pending",720);
            Check(bounded.Quotes[0].SourceId=="a" && bounded.Quotes[0].Text==manual.Text,"Explicit quote gets budget before automatic context");
            Check(bounded.Quotes.Any(q=>q.SourceId=="new") && !bounded.Quotes.Any(q=>q.SourceId=="old"),"Recent context takes priority when the reference budget is full");
            Check(bounded.Cropped && bounded.JsonLength<=720 && bounded.Omitted+bounded.Shortened>0,"Budget truncation is bounded and explicitly reported");
            var escaped=new WorkQuote { SourceId="escaped",SourceText=new string('"',600),Text=String.Concat(Enumerable.Repeat("\\\n\t\"",300)) };
            var escapePlan=ContextQuotePlanner.Build(WorkMode.Ask,false,entries,new[]{escaped},"pending",512);
            Check(escapePlan.Cropped && escapePlan.JsonLength<=512,"Budget counts serialized JSON escaping, not just raw string length");
            Check(escaped.SourceText.Length==600 && a.Answer=="first answer" && manual.Text=="first answer","Planning does not mutate source records or explicit quotes");
            all.Quotes[0].Text="changed snapshot";
            Check(manual.Text=="first answer","Result quotes are independent copies");
            results.Add("Context planning is local and pure; no API, real settings, history or running instance was accessed.");
            File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); return 0;
        } catch(Exception e) { results.Add("FAIL: "+e); File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); return 1; }
    }
}
}
