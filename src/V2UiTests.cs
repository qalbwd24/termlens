using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TermLens {
public static partial class Tests {
    static IEnumerable<Control> V2Children(Control root) {
        foreach(Control child in root.Controls) { yield return child; foreach(var nested in V2Children(child)) yield return nested; }
    }
    public static int V2Ui(string root) {
        string folder=TestRoot(root,"v2-ui"); int exit=0;
        var settings=new Settings { ReadingWidth=460,ReadingHeight=560,ReadingFontSize=12,ReadingTopMost=false,SpeechPreload=false };
        var session=new WorkSession();
        session.Entries.Add(new WorkEntry { Text="hello",Answer="**hello** · 你好\n\nA common greeting when you meet someone or begin a conversation.",Finished=true });
        session.Entries.Add(new WorkEntry { Text="What does perpendicular mean?",Answer="Two lines are **perpendicular** when they meet at a right angle: $90^{\\circ}$.\n\nThe two edges of a square meet this way.",Finished=true,Mode=WorkMode.Ask });
        session.Entries.Add(new WorkEntry { Text="how to say it more natural",Answer="How can I say this more naturally?",Finished=true,Mode=WorkMode.Polish });
        var window=new WorkspaceWindow(session,settings,folder);
        window.Shown+=async delegate {
            try {
                await Task.Delay(100); window.Timeline.AutoScrollPosition=Point.Empty;
                var header=Field<Panel>(window,"header"); var bottom=Field<Panel>(window,"bottom");
                Check(!V2Children(header).OfType<Button>().Any(b=>b.Text=="Fit"),"Fit removed from the header");
                foreach(string name in new[]{"settingsButton","last"}) Check(!String.IsNullOrEmpty(Field<SoftButton>(window,name).IconName),"Header action uses an icon: "+name);
                Check(Field<ToolTip>(window,"tips").InitialDelay==1000,"Header and composer hints wait one second");
                Check(header.Height<=Look.S(40) && bottom.Height<Look.S(160),"Compact header and empty composer preserve reading space");
                var frame=Field<FieldFrame>(window,"composerFrame");
                var composerViewport=Field<Panel>(window,"composerViewport");
                var modes=Field<FlowLayoutPanel>(window,"modeRow");
                var send=Field<SoftButton>(window,"send"); var draftListen=Field<SoftButton>(window,"listenDraft");
                Check(send.Parent==frame && modes.Parent==frame && draftListen.Parent==frame,"Composer contains its modes, send and draft audio");
                Check(!draftListen.Visible,"Empty composer hides redundant draft audio");
                Check(composerViewport.Bottom<modes.Top && modes.Bottom<=frame.ClientSize.Height,"Text and mode toolbar occupy separate areas inside composer");
                Check(window.Composer.Parent==composerViewport && window.Composer.Width-composerViewport.Width==SystemInformation.VerticalScrollBarWidth,"The viewport clips only the native editor scrollbar gutter");
                Check(Field<List<SoftButton>>(window,"modes").All(b=>!b.IconWithText && b.Width==Look.S(28)),"Modes use fixed-width icon-only controls");
                Check(Field<SoftButton>(window,"addImage").IconWithText,"Image keeps its icon and label");
                foreach(int logicalWidth in new[]{360,440,460}) {
                    window.Size=new Size(Look.S(logicalWidth),Look.S(560)); window.Composer.Clear(); await Task.Delay(30);
                    var outerBounds=window.Bounds; int frameHeight=frame.Height,sendTop=send.Top,layouts=window.TimelineLayoutCount;
                    IntPtr editorHandle=window.Composer.Handle;
                    string draft="what is wrong with you";
                    for(int i=1;i<=draft.Length;i++) { window.Composer.Text=draft.Substring(0,i); Check(frame.Height==frameHeight && send.Top==sendTop,"Typing short text keeps composer and send stable at "+logicalWidth+" / "+i); }
                    for(int i=draft.Length-1;i>=0;i--) window.Composer.Text=draft.Substring(0,i);
                    window.Composer.Text="现在输入中文"; window.Composer.Clear();
                    Check(frame.Height==frameHeight && window.Bounds==outerBounds,"Rapid typing and clearing keep the window and composer geometry fixed at "+logicalWidth);
                    Check(window.TimelineLayoutCount==layouts,"Same-line typing does not relayout the reading timeline at "+logicalWidth);
                    Check(window.Composer.Handle==editorHandle,"Typing preserves the native editor handle at "+logicalWidth);
                    window.Composer.Text="what is wrong with you";
                    Check(modes.Top==send.Top && draftListen.Top==send.Top && !modes.Bounds.IntersectsWith(draftListen.Bounds),"All actions remain on one line at "+logicalWidth);
                    Shot(window,Path.Combine(folder,"workspace-typing-"+logicalWidth+".png"));
                    window.Composer.Text=String.Concat(Enumerable.Repeat("a longer draft wraps naturally ",20));
                    Check(frame.Height>frameHeight && window.Bounds==outerBounds,"Only wrapped content expands the input area at "+logicalWidth);
                    Check(window.Composer.Handle==editorHandle,"Long text does not recreate the editor handle at "+logicalWidth);
                    window.Composer.SelectionStart=window.Composer.TextLength; window.Composer.ScrollToCaret();
                    int endY=window.Composer.GetPositionFromCharIndex(window.Composer.TextLength).Y;
                    Check(endY>=0 && endY<composerViewport.Height,"The end of a long draft remains reachable with the native scrollbar clipped");
                    window.Composer.Clear();
                }
                window.Size=new Size(Look.S(460),Look.S(560));
                foreach(var card in window.Cards) {
                    foreach(string action in new[]{"save","ask","listen","copy","polish","listenOriginal"}) {
                        var button=Field<SoftButton>(card,action);
                        Check(!String.IsNullOrEmpty(button.IconName) && !String.IsNullOrEmpty(button.AccessibleName),"Entry icon has an accessible action: "+action);
                    }
                    Check(Field<SoftButton>(card,"save").Visible,"All completed answer modes offer Save");
                    Check(Field<ToolTip>(card,"tips").InitialDelay==1000,"Entry hints wait one second");
                    Check(Field<FlowLayoutPanel>(card,"actions").Top>=card.Answer.Bottom,"Entry actions remain below their own answer");
                    Check(Field<SoftButton>(card,"listenOriginal").Top==card.Original.Top,"Original audio is next to queried text");
                }
                Shot(window,Path.Combine(folder,"workspace-dark.png"));
                var answer=window.Cards[0].Answer; int parses=answer.MarkdownParseCount;
                answer.Focus(); answer.Select(0,5);
                typeof(Control).GetMethod("OnMouseUp",Private).Invoke(answer,new object[]{new MouseEventArgs(MouseButtons.Left,1,10,10,0)});
                Check(!window.Quote.Visible,"Selecting text does not show the removed quote popup");
                answer.ContentHeight(answer.Width-16); answer.ContentHeight(answer.Width+16);
                Check(answer.MarkdownParseCount==parses,"Resizing reuses parsed Markdown"); window.LayoutTimeline();
                string quoted=""; window.EntryAction+=delegate(WorkEntry entry,string action) { quoted=action; };
                Field<SoftButton>(window.Cards[1],"save").PerformClick(); Check(quoted=="save","Ask Save icon dispatches its action");
                Field<SoftButton>(window.Cards[0],"ask").PerformClick();
                Check(window.Session.Quotes.Count==1 && window.Session.Mode==WorkMode.Ask,"Quote icon adds context without sending");
                Check(window.Session.Quotes[0].Text==Markdown.PlainText(session.Entries[0].Answer) && window.Session.Quotes[0].SourceText==session.Entries[0].Text,"Quote uses the complete source and answer, not the selected fragment");
                window.AcceptDraft();
                var fixedSize=window.Size;
                var streaming=new WorkEntry { Text="Long reading sample",Answer="",Finished=false };
                window.AddEntry(streaming);
                var watch=Stopwatch.StartNew(); var costs=new List<double>();
                for(int i=0;i<80;i++) {
                    streaming.Answer+="A small **example** preserves your reading position and selected text. ";
                    var one=Stopwatch.StartNew(); window.UpdateEntry(streaming); costs.Add(one.Elapsed.TotalMilliseconds);
                    if(i%8==0) await Task.Delay(1);
                }
                streaming.Finished=true; window.UpdateEntry(streaming); watch.Stop(); costs.Sort();
                results.Add("INFO: 80 growing Markdown refreshes, total="+watch.ElapsedMilliseconds+"ms p95="+costs[(int)(costs.Count*.95)-1].ToString("F2")+"ms; not display FPS or network latency.");
                Check(window.Size==fixedSize,"Streaming and completion do not resize the window");
                window.ApplyReading(12,420,520);
                Check(window.Width==Look.S(420) && window.Height==Look.S(520),"Explicit reading size settings still resize the window");
                var appliedSize=window.Size; streaming.Answer+=" More."; window.UpdateEntry(streaming);
                Check(window.Size==appliedSize,"Completion preserves explicitly applied window dimensions");
                var bar=Field<SlimScrollBar>(window,"scrollBar");
                Check(bar.Visible && bar.Maximum>0,"Long content exposes the slim scrollbar");
                bar.Value=bar.Maximum/2; Application.DoEvents();
                Check(Math.Abs(-window.Timeline.AutoScrollPosition.Y-bar.Value)<=1,"Slim scrollbar moves the real reading viewport");
                int offset=bar.Value; streaming.Answer+=" Another sentence."; window.UpdateEntry(streaming);
                Check(Math.Abs(-window.Timeline.AutoScrollPosition.Y-offset)<=1,"Updates preserve a reader scrolled away from the bottom");
                window.Composer.Text=String.Join("\r\n",Enumerable.Repeat("A longer draft remains editable and scrollable.",12));
                Check(window.Composer.ScrollBars==ScrollBars.Vertical,"Long draft exposes an input scrollbar");
                Check(draftListen.Visible && composerViewport.Bottom<send.Top && composerViewport.Bottom<draftListen.Top,"Long draft stays above its send and audio controls");
                Check(bottom.Height<window.ClientSize.Height*.6,"Long draft has a bounded height");
                window.Composer.Clear(); window.Size=new Size(Look.S(370),Look.S(470));
                window.Timeline.AutoScrollPosition=Point.Empty; await Task.Delay(80);
                Check(composerViewport.Bottom<modes.Top && !modes.Bounds.IntersectsWith(send.Bounds),"Narrow composer keeps text, modes and send separate");
                Check(modes.Controls.Cast<Control>().Where(c=>c.Visible).All(c=>c.Right<=modes.ClientSize.Width && c.Bottom<=modes.ClientSize.Height),"Narrow mode toolbar contains every button");
                foreach(var card in window.Cards) {
                    var actions=Field<FlowLayoutPanel>(card,"actions");
                    Check(actions.Controls.Cast<Control>().Where(c=>c.Visible).All(c=>c.Right<=actions.ClientSize.Width && c.Bottom<=actions.ClientSize.Height),"Icon row stays usable at narrow width");
                }
                Shot(window,Path.Combine(folder,"workspace-narrow.png"));
                Theme.TestDark=false; Theme.Refresh(); window.RefreshLanguage();
                window.Size=new Size(Look.S(460),Look.S(560)); window.Timeline.AutoScrollPosition=Point.Empty;
                await Task.Delay(80); Shot(window,Path.Combine(folder,"workspace-light.png"));
                Theme.TestDark=true; Theme.Refresh();
            } catch(Exception e) { results.Add("FAIL: "+e); exit=1; }
            finally { File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); window.Close(); }
        };
        Application.Run(window); window.Dispose(); return exit;
    }
}
}
