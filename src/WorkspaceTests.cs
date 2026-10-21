using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TermLens {
public static partial class Tests {
    static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static T Field<T>(object obj,string name) { return (T)obj.GetType().GetField(name,Private).GetValue(obj); }
    static Task WorkCall(MainWindow app,string name,params object[] args) { var method=typeof(MainWindow).GetMethod(name,Private); if(args.Length<method.GetParameters().Length) args=args.Concat(Enumerable.Repeat(Type.Missing,method.GetParameters().Length-args.Length)).ToArray(); return (Task)method.Invoke(app,args); }
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd,IntPtr dc,uint flags);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a,uint b,bool attach);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    static void Foreground(Form form) {
        uint ignored; uint previous=Native.GetWindowThreadProcessId(Native.GetForegroundWindow(),out ignored),current=GetCurrentThreadId();
        bool attached=previous!=current && AttachThreadInput(current,previous,true);
        try { form.TopMost=true; form.Show(); form.Activate(); Native.SetForegroundWindow(form.Handle); }
        finally { if(attached) AttachThreadInput(current,previous,false); }
    }
    static void Shot(Form form,string path) {
        form.Refresh(); Application.DoEvents();
        using(var bmp=new Bitmap(form.Width,form.Height)) { using(var g=Graphics.FromImage(bmp)) { IntPtr dc=g.GetHdc(); try { PrintWindow(form.Handle,dc,0); } finally { g.ReleaseHdc(dc); } } bmp.Save(path); }
    }
    public static int Workspace(string root) {
        string folder=TestRoot(root,"workspace"); int exit=0;
        Files.SaveKey(folder,"isolated-workspace-test");
        var settings=new Settings { ReadingWidth=560,ReadingFontSize=12,IncludeHistory=false,SpeechProvider="microsoft" }; Files.Save(folder,settings);
        var handlers=new List<Handler>(); bool waiting=false;
        var app=new MainWindow(folder,true,delegate {
            if(waiting) return new WaitingHandler();
            var handler=new Handler(Reply("perpendicular · 垂直的\n\nTwo lines are perpendicular when they meet at an angle of 90°, like a corner of a square.\n\nThe two edges of a page meet this way.")); handlers.Add(handler); return handler;
        });
        app.Shown+=async delegate {
            try {
                await Task.Delay(150);
                var ensure=typeof(MainWindow).GetMethod("EnsureWorkspace",Private);
                var window=(WorkspaceWindow)ensure.Invoke(app,new object[]{(Rectangle?)new Rectangle(130,110,150,20),null});
                window.Location=new Point(120,80); await Task.Delay(150);
                Check(window.Session.Mode==WorkMode.Explain,"Empty F13 workspace defaults to Explain");
                Check(Field<MouseSelection>(app,"mouse")==null && Field<SelectionBubble>(app,"bubble")==null,"No external mouse hook or selection toolbar is created");
                Check(Field<List<int>>(app,"registered").SequenceEqual(new[]{4}),"Only F13 is registered");
                Check(window.CloseButton.Width>=Look.S(28) && !string.IsNullOrEmpty(window.CloseButton.AccessibleName),"Compact close icon has a 28 logical pixel target and an accessible description");
                window.Composer.Text="perpendicular"; window.Send();
                for(int i=0;i<50 && (window.Session.Entries.Count==0 || !window.Session.Entries[0].Finished);i++) await Task.Delay(30);
                Check(window.Session.Entries.Count==1 && window.Session.Entries[0].Finished,"Enter-equivalent dispatch completes and appends the answer");
                Check(window.Composer.TextLength==0 && handlers[0].Body.Contains("disabled"),"Accepted draft clears and thinking stays disabled");
                window.Composer.Text="what is the difference?";
                var first=window.Session.Entries[0];
                window.AddQuote(new WorkQuote { Text="an angle of 90°",SourceId=first.Id,SourceText=first.Text });
                window.AddQuote(new WorkQuote { Text="a corner of a square",SourceId=first.Id,SourceText=first.Text });
                Check(window.Session.Mode==WorkMode.Ask && window.Composer.Text=="what is the difference?" && window.Session.Quotes.Count==2,"Multiple quotes switch to Ask without losing the draft");
                Check(handlers.Count==1,"Quoting does not send a request");
                Theme.TestDark=false; Theme.Refresh(); await Task.Delay(200); Shot(window,Path.Combine(folder,"workspace-light.png"));
                window.Send();
                for(int i=0;i<50 && (window.Session.Entries.Count<2 || !window.Session.Entries[1].Finished);i++) await Task.Delay(30);
                Check(window.Session.Entries.Count==2 && window.Session.Quotes.Count==0,"Ask appends in the same window and clears submitted quotes");
                Check(handlers[1].Body.Contains("an angle of 90") && handlers[1].Body.Contains("a corner of a square") && !handlers[1].Body.Contains("The two edges of a page"),"Ask sends explicit quotes without unrelated full answers");
                var body=Files.Json().Deserialize<Dictionary<string,object>>(handlers[1].Body);
                Check(((System.Collections.ArrayList)body["messages"]).Count==2,"Each query has its own system and user message, no implicit conversation");
                await WorkCall(app,"SendWorkspace",window,WorkMode.Polish,"i have implement all the function",new List<WorkQuote>(),false);
                Check(handlers[2].Body.Contains("ONLY the improved wording") && !handlers[2].Body.Contains("a corner of a square"),"Polish uses its own prompt and no stale quotations");
                Check(window.Session.Entries.Count==3,"Additional lookups append to the existing session");
                await Interaction(app,window,folder);
                await PopupStability(app,window,folder);
                // Test the actual message filter: a Shift tap versus an uppercase combination.
                Foreground(window); window.Composer.Focus(); await Task.Delay(100);
                Check(Native.GetForegroundWindow()==window.Handle,"Test workspace owns keyboard focus");
                Check(Native.WindowFromPoint(new Native.Point { X=window.Right-3,Y=window.Top+window.Height/2 })==window.Handle,"Resize border is not covered by docked child controls");
                var hitPoint=window.PointToScreen(new Point(window.ClientSize.Width-3,window.ClientSize.Height/2));
                var hit=Message.Create(window.Handle,0x84,IntPtr.Zero,new IntPtr((hitPoint.Y<<16)|(hitPoint.X&0xffff)));
                object[] hitArgs={hit}; typeof(WorkspaceWindow).GetMethod("WndProc",Private).Invoke(window,hitArgs);
                Check(((Message)hitArgs[0]).Result.ToInt32()==11,"Right edge exposes the native resize action");
                var chosenBox=window.Cards[0].Answer; chosenBox.Focus(); chosenBox.Select(0,13);
                typeof(Control).GetMethod("OnMouseUp",Private).Invoke(chosenBox,new object[]{new MouseEventArgs(MouseButtons.Left,1,30,15,0)});
                Check(!window.Quote.Visible,"Answer selection no longer opens a Quote popup");
                Field<SoftButton>(window.Cards[0],"ask").PerformClick();
                Check(window.Session.Quotes.Count==1 && window.Session.Quotes[0].SourceText==window.Cards[0].Entry.Text && window.Session.Quotes[0].Text==Markdown.PlainText(window.Cards[0].Entry.Answer),"Quote button collects the whole entry even when a short fragment is selected");
                window.AcceptDraft(); window.Composer.Focus();
                var filter=Field<ShiftTap>(window,"shift"); window.SetMode(WorkMode.Explain);
                var down=Message.Create(window.Composer.Handle,0x100,new IntPtr(0x10),IntPtr.Zero); var up=Message.Create(window.Composer.Handle,0x101,new IntPtr(0x10),IntPtr.Zero);
                filter.PreFilterMessage(ref down); filter.PreFilterMessage(ref up);
                Check(window.Session.Mode==WorkMode.Ask,"Shift alone cycles the selected mode");
                filter.PreFilterMessage(ref down); var letter=Message.Create(window.Composer.Handle,0x100,new IntPtr((int)Keys.A),IntPtr.Zero); filter.PreFilterMessage(ref letter); filter.PreFilterMessage(ref up);
                Check(window.Session.Mode==WorkMode.Ask,"Shift with another key does not cycle modes");
                var ime=Message.Create(window.Composer.Handle,0x10D,IntPtr.Zero,IntPtr.Zero);
                typeof(ComposerBox).GetMethod("WndProc",Private).Invoke(window.Composer,new object[]{ime});
                filter.PreFilterMessage(ref down); filter.PreFilterMessage(ref up);
                Check(window.Session.Mode==WorkMode.Ask,"IME composition prevents Shift mode changes");
                ime=Message.Create(window.Composer.Handle,0x10E,IntPtr.Zero,IntPtr.Zero); typeof(ComposerBox).GetMethod("WndProc",Private).Invoke(window.Composer,new object[]{ime}); await Task.Delay(180);
                Theme.TestDark=true; Theme.Refresh();
                Field<ComboBox>(app,"languageChoice").SelectedIndex=1;
                window.SetMode(WorkMode.Explain); await Task.Delay(100);
                Shot(window,Path.Combine(folder,"workspace-dark-zh.png"));
                Check(Files.Load(folder).UiLanguage=="zh" && Field<SoftButton>(window,"send").AccessibleName.Contains("解释"),"Language setting updates the icon description and persists");
                window.ApplyReading(18,640,0); await Task.Delay(100);
                Check(window.Cards.All(c=>c.Original.Height>0 && c.Answer.Height>0) && window.Composer.Font.Size==18,"Larger font reflows answer and composer");
                foreach(var card in window.Cards) {
                    card.Answer.Select(card.Answer.TextLength,0); card.Answer.ScrollToCaret();
                    var end=card.Answer.GetPositionFromCharIndex(card.Answer.TextLength);
                    Check(end.Y+TextRenderer.MeasureText("Ag",card.Answer.Font).Height<=card.Answer.Height+Look.S(4),"The last answer line fits at a larger font size");
                }
                // F13 on an answer opens a temporary dictionary, not another timeline record.
                var reading=window.Cards[0].Answer; Foreground(window); window.Timeline.AutoScrollPosition=Point.Empty; reading.Focus(); reading.Select(0,13);
                typeof(Control).GetMethod("OnMouseUp",Private).Invoke(reading,new object[]{new MouseEventArgs(MouseButtons.Left,1,30,15,0)});
                Check(!window.Quote.Visible,"Quote popup stays disabled before the real F13 test");
                Check(reading.Focused && Native.GetForegroundWindow()==window.Handle,"Selecting answer text keeps keyboard focus in the answer");
                // A toolbar button may retain focus after selecting text: the selection must survive.
                Field<SoftButton>(window,"settingsButton").Focus();
                Native.keybd_event((byte)Keys.F13,0,0,UIntPtr.Zero); Native.keybd_event((byte)Keys.F13,0,2,UIntPtr.Zero);
                for(int i=0;i<60;i++) { var opened=Field<QuickDictionary>(app,"dictionary"); if(opened!=null && opened.Visible && opened.Body.TextLength>0) break; await Task.Delay(50); }
                var mini=Field<QuickDictionary>(app,"dictionary");
                await Task.Delay(300);
                Check(mini!=null && !mini.IsDisposed && mini.Visible && window.Session.Entries.Count==3,"Internal F13 opens a temporary dictionary without adding a main record");
                Check(Native.GetForegroundWindow()==mini.Handle,"Temporary dictionary keeps focus after the F13 key is released");
                Shot(mini,Path.Combine(folder,"dictionary-dark.png"));
                Foreground(window); await Task.Delay(100);
                Check(mini.IsDisposed && window.Visible,"Clicking away dismisses only the temporary dictionary");
                window.Timeline.AutoScrollPosition=Point.Empty;
                window.Timeline.Wheel(-15,reading.Font); int small=-window.Timeline.AutoScrollPosition.Y;
                window.Timeline.AutoScrollPosition=Point.Empty;
                window.Timeline.Wheel(-120,reading.Font); int full=-window.Timeline.AutoScrollPosition.Y;
                Check(small>0 && full>small*5,"Small wheel deltas scroll proportionally rather than jumping a fixed distance");
                window.Timeline.AutoScrollPosition=new Point(0,200); int offset=-window.Timeline.AutoScrollPosition.Y;
                window.Composer.Focus(); reading.Focus();
                Check(-window.Timeline.AutoScrollPosition.Y==offset,"Focusing a long answer does not jump its parent timeline");
                int expected=offset; SendMessage(reading.Handle,0x20A,new IntPtr(15<<16),IntPtr.Zero);
                Check(-window.Timeline.AutoScrollPosition.Y<expected && expected+window.Timeline.AutoScrollPosition.Y<full,"Wheel messages over an answer reach the proportional timeline scroll");
                window.Cards[1].Answer.Focus(); window.Cards[1].Answer.Select(0,5); reading.Focus(); reading.Select(0,0);
                Rectangle clearedBounds; Check(window.SelectedReading(out clearedBounds)=="","Clearing the latest selection cannot reuse stale words from another answer");
                string spoken=""; window.SpeechRequested+=delegate(string value) { spoken=value; };
                // Detach real speech for the following UI-routing checks (no network or audible playback).
                var speechEvent=typeof(WorkspaceWindow).GetField("SpeechRequested",Private); speechEvent.SetValue(window,(Action<string>)(value=>spoken=value));
                reading.Select(0,13); Field<SoftButton>(window.Cards[0],"listen").PerformClick();
                Check(spoken==reading.SelectedText,"Answer Listen reads just the selected word when present");
                window.Composer.Text="my own draft"; Field<SoftButton>(window,"listenDraft").PerformClick();
                Check(spoken=="my own draft" && window.Session.Entries.Count==3,"Listen reads an unsent draft without creating a query");
                window.Composer.Text="unsent question"; window.AddQuote(new WorkQuote { Text="keep this",SourceId=first.Id,SourceText=first.Text });
                window.Close(); await Task.Delay(50);
                var saved=Files.Json().Deserialize<WorkSession>(File.ReadAllText(Path.Combine(folder,"last-session.json"),Files.Utf8));
                Check(saved.Entries.Count==3 && saved.Draft=="unsent question" && saved.Quotes.Count==1,"Close caches full session, input, and pending quotes");
                window=(WorkspaceWindow)ensure.Invoke(app,new object[]{null,null});
                Check(window.Session.Entries.Count==0 && window.Session.Mode==WorkMode.Explain,"New opening starts a new Explain session");
                window.Composer.Text="new draft"; window.SaveNow();
                Check(Files.Json().Deserialize<WorkSession>(File.ReadAllText(Path.Combine(folder,"last-session.json"),Files.Utf8)).Draft=="unsent question","New typing does not overwrite the last closed session");
                typeof(MainWindow).GetMethod("RestoreWorkspace",Private).Invoke(app,null);
                Check(window.Session.Entries.Count==3 && window.Composer.Text=="new draft","Last session restores records while keeping the current draft");
                waiting=true;
                var pending=WorkCall(app,"SendWorkspace",window,WorkMode.Explain,"pending",new List<WorkQuote>(),false);
                await Task.Delay(100); window.Close(); await pending;
                Check(Field<Dictionary<string,System.Threading.CancellationTokenSource>>(app,"workRequests").Count==0,"Closing cancels pending requests and releases their resources");
                app.Reveal(); Field<SettingsPages>(app,"tabs").SelectedIndex=2; await Task.Delay(150); Shot(app,Path.Combine(folder,"settings-dark-zh.png"));
                Field<ComboBox>(app,"speechProvider").SelectedIndex=0;
                Check(new Settings().SpeechProvider=="baidu-web" && !Field<ComboBox>(app,"speechVoice").Enabled,"Baidu web is the default and Microsoft-only controls are disabled for it");
                typeof(MainWindow).GetMethod("SaveSettings",Private).Invoke(app,new object[]{false});
                Check(Files.Load(folder).SpeechProvider=="baidu-web","Voice provider selection persists with Save settings");
                Field<SettingsPages>(app,"tabs").SelectedIndex=5; await Task.Delay(100); Shot(app,Path.Combine(folder,"voice-dark-zh.png"));
                Check(Field<CheckBox>(app,"speechPreload").Checked && Field<ComboBox>(app,"speechScope").SelectedIndex==2,"Audio preloading defaults to original text plus all answers");
                Theme.TestDark=false; Theme.Refresh(); Field<ComboBox>(app,"languageChoice").SelectedIndex=0; await Task.Delay(100); Shot(app,Path.Combine(folder,"voice-light-en.png"));
                await Network();
                waiting=false;
                window=(WorkspaceWindow)ensure.Invoke(app,new object[]{null,null});
                window.AddEntry(new WorkEntry { Text="context source alpha",Answer="context answer alpha",Finished=true });
                window.AddEntry(new WorkEntry { Text="context source beta",Answer="context answer beta",Finished=true });
                Field<CheckBox>(app,"quoteAllWindow").Checked=true;
                typeof(MainWindow).GetMethod("SaveSettings",Private).Invoke(app,new object[]{false});
                Check(Files.Load(folder).QuoteAllWindow,"Full Quote checkbox persists through the real settings save path");
                await WorkCall(app,"SendWorkspace",window,WorkMode.Ask,"compare these two concepts",new List<WorkQuote>(),false);
                Check(handlers.Last().Body.Contains("context source alpha") && handlers.Last().Body.Contains("context answer beta"),"Enabled Full Quote reaches the actual Ask request payload");
                Field<CheckBox>(app,"quoteAllWindow").Checked=false;
                await WorkCall(app,"SendWorkspace",window,WorkMode.Ask,"a separate question",new List<WorkQuote>(),false);
                Check(!handlers.Last().Body.Contains("context source alpha") && !handlers.Last().Body.Contains("context answer beta"),"Disabling Full Quote removes automatic references on the next request");
            } catch(Exception e) { exit=1; results.Add(e.ToString()); }
            finally {
                File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8);
                typeof(MainWindow).GetField("exiting",Private).SetValue(app,true); app.Close();
            }
        };
        Application.Run(app); return exit;
    }
}
}
