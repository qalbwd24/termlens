using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TermLens {
public static class ReleaseWindowHistoryTests {
    static T Field<T>(object target,string name) { return (T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target); }
    public static int Run(string root) {
        string folder=Path.Combine(root,"tests","release-window-history-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")); Directory.CreateDirectory(folder);
        var report=new List<string>(); int exit=0;
        Action<bool,string> check=(ok,label)=> { if(!ok) throw new Exception(label); report.Add("PASS: "+label); };
        var settings=new Settings { ReadingFontSize=11,ReadingWidth=440,ReadingHeight=370,ReadingTopMost=false };
        var window=new ReleaseLookupWindow(settings);
        window.Shown+=async delegate {
            try {
                var query=new Lookup { Id="current",Term="current query",Answer=String.Join("\n\n",Enumerable.Repeat("Original reading paragraph with enough text to scroll.",35)),Status="pending",Context="{}" };
                var old=new Lookup { Id="history",Term="a very long historical selected sentence "+new string('x',220),Answer="The historical answer.",Status="complete",Context="{}" };
                var entries=new List<Lookup> { old,new Lookup { Id="other",Term="second word",Answer="Another meaning.",Status="complete",Context="{}" } };
                int requests=0,viewChanges=0; window.HistoryProvider=()=> { requests++; return entries; }; window.HistoryViewChanged+=()=>viewChanges++;
                window.ShowLookup(query,null); await Task.Delay(80);
                var timeline=Field<TimelinePanel>(window,"timeline"); timeline.AutoScrollPosition=new Point(0,180); await Task.Delay(30); int previous=-timeline.AutoScrollPosition.Y;
                check(previous>0,"Original reading position is scrollable before history navigation");
                var historyButton=Field<SoftButton>(window,"historyButton"); historyButton.PerformClick();
                check(window.IsHistoryList && window.Current==null && historyButton.Primary && requests==1,"History button opens highlighted list state with no actionable lookup");
                check(!Field<SoftButton>(window,"save").Visible && !Field<SoftButton>(window,"readAnswer").Visible,"List state hides favorite and audio actions");
                check(Field<Panel>(window,"footer").Height==0,"History list uses the full reading height without an empty action footer");
                check((ReleaseHistoryList.RowTextFlags&TextFormatFlags.SingleLine)!=0 && (ReleaseHistoryList.RowTextFlags&TextFormatFlags.EndEllipsis)!=0,"Long history selections use one line with trailing ellipsis");
                var list=Field<ReleaseHistoryList>(window,"windowHistoryList");
                using(var bitmap=new Bitmap(list.Width,list.Height)) { list.DrawToBitmap(bitmap,list.ClientRectangle); bitmap.Save(Path.Combine(folder,"history-list.png")); }
                using(var bitmap=new Bitmap(window.Width,window.Height)) { window.DrawToBitmap(bitmap,window.ClientRectangle); bitmap.Save(Path.Combine(folder,"history-window.png")); }
                window.SetNotice("History test notice",true); check(Field<Panel>(window,"footer").Height>0,"History list reserves a compact footer when a notice must be shown"); window.SetNotice("",false);
                window.ShowHistoryDetail(old); check(!window.IsHistoryList && window.IsBrowsingHistory && window.Current.Id==old.Id && historyButton.IconName=="back","Opening a history snapshot changes the same button to vector return-to-list");
                old.Answer="Externally changed record"; query.Answer+="\n\nLatest streamed words."; window.UpdateAnswer(); window.Finish(true,"query finished");
                check(window.Answer.Text.Contains("The historical answer.") && !window.Answer.Text.Contains("Latest streamed words"),"Background streaming and finish do not overwrite historical answer");
                check(window.Current.Answer=="The historical answer." && !Object.ReferenceEquals(window.Current,old),"Historical detail owns a stable Lookup snapshot");
                Lookup saved=null; string spoken=null; window.SaveRequested+=()=>saved=window.Current; window.SpeechRequested+=text=>spoken=text;
                Field<SoftButton>(window,"save").PerformClick(); Field<SoftButton>(window,"readAnswer").PerformClick();
                check(saved!=null && saved.Id=="history" && spoken=="The historical answer.","Favorite and audio actions target the displayed historical record");
                historyButton.PerformClick(); check(window.IsHistoryList && requests==2,"Detail back button reloads the list so favorite state can refresh");
                query.Status="complete"; historyButton.PerformClick(); await Task.Delay(40);
                check(!window.IsBrowsingHistory && Object.ReferenceEquals(window.Current,query) && window.Answer.Text.Contains("Latest streamed words"),"List history button restores the original query with its newest streamed answer");
                check(Math.Abs(-timeline.AutoScrollPosition.Y-previous)<=2,"Returning to current query restores the previous reading scroll position");
                check(Field<Label>(window,"notice").Text=="query finished","Background completion notice appears only after returning to original query");
                check(viewChanges==4,"Navigation announces each view transition without making model requests");
                window.ToggleHistory(); window.ShowLookup(new Lookup { Id="new",Term="new query",Answer="new answer",Status="complete" },null);
                check(!window.IsBrowsingHistory && window.Current.Id=="new" && window.OriginalLookup.Id=="new","A new external lookup resets history browsing to the new query");
                int revision=window.DisplayRevision;
                string payload=new string('c',1024*1024);
                var largeHistory=Enumerable.Range(0,10000).Select(i=>new Lookup { Id="large-"+i,Term="word "+i,Answer=payload,Context=payload,Prompt=payload,Status="complete" }).ToList();
                window.HistoryProvider=()=>largeHistory;
                var elapsed=System.Diagnostics.Stopwatch.StartNew(); window.ToggleHistory(); elapsed.Stop();
                var rows=Field<List<Lookup>>(list,"items");
                check(rows.Count==10000 && rows.All(row=>row.Answer==null && row.Context==null && row.Prompt==null),"Ten thousand list rows keep metadata only without serializing answer/context payloads");
                report.Add("INFO: Ten-thousand-row UI load took "+elapsed.ElapsedMilliseconds+" ms; timing is informational, not a machine-dependent pass threshold.");
                check(window.DisplayRevision>revision && window.ParentContext==null,"History navigation advances its revision and clears hidden reading context");
                var cloned=(Lookup)typeof(ReleaseLookupWindow).GetMethod("HistorySnapshot",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{largeHistory[0]});
                check(!Object.ReferenceEquals(cloned,largeHistory[0]) && Object.ReferenceEquals(cloned.Context,payload) && Object.ReferenceEquals(cloned.Answer,payload),"Detail snapshot isolates object fields while sharing immutable large strings");
                window.HistoryItemProvider=item=> { throw new IOException("Fixture detail unavailable"); };
                window.ShowHistoryDetail(rows[0]);
                var footer=Field<Panel>(window,"footer"); var notice=Field<Label>(window,"notice");
                check(window.IsHistoryList && window.Current==null && footer.Height==Look.S(32) && notice.Visible && notice.Bottom<=footer.ClientSize.Height,"Detail-load failure stays in history list with a fully visible compact error notice");
                bool metadataRequest=false;
                string detailContext=LookupContext.Build("child",LookupContext.FromSource("parent-id","root source","parent explanation"),null).ContextJson;
                window.HistoryItemProvider=item=> { metadataRequest=item.Answer==null && item.Context==null; return new Lookup { Id=item.Id,Term=item.Term,Answer="Lazily loaded meaning.",Context=detailContext,Status="complete",Saved=true }; };
                window.ShowHistoryDetail(rows[0]);
                check(metadataRequest && window.Current.Answer=="Lazily loaded meaning." && window.Current.Saved && window.ParentContext!=null,"Detail provider loads the selected record and its context only when opened");
                window.ToggleHistory(); check(window.ParentContext==null,"Returning to history list clears the detail context before secondary selection");
                window.HistoryItemProvider=null; window.RestoreOriginalLookup();
                var retained=window.Current; window.HistoryProvider=()=> { throw new IOException("Fixture list unavailable"); }; window.ToggleHistory();
                check(!window.IsBrowsingHistory && Object.ReferenceEquals(window.Current,retained) && notice.ForeColor==Look.Error,"History-list failure preserves the active lookup and reports the error visibly");
                using(var secondary=new ReleaseLookupWindow(settings,true)) {
                    secondary.ShowLookup(query,null); secondary.ToggleHistory();
                    check(!secondary.IsBrowsingHistory && !Field<SoftButton>(secondary,"historyButton").Visible,"Secondary lookup windows never expose history navigation");
                }
                report.Add("INFO: UI fixture only; no model requests, audio synthesis, audio playback or clipboard operations.");
            } catch(Exception error) { report.Add("FAIL: "+error); exit=1; }
            finally { File.WriteAllLines(Path.Combine(folder,"results.txt"),report,Files.Utf8); window.Close(); }
        };
        Application.Run(window); window.Dispose(); return exit;
    }
}
}
