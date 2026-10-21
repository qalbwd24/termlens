using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TermLens {
public static partial class Tests {
    delegate bool ReleaseWindowVisitor(IntPtr window,IntPtr state);
    [DllImport("user32.dll",EntryPoint="EnumWindows")] static extern bool ReleaseEnumWindows(ReleaseWindowVisitor visitor,IntPtr state);
    [DllImport("user32.dll",EntryPoint="GetWindowText",CharSet=CharSet.Unicode)] static extern int ReleaseWindowText(IntPtr window,StringBuilder text,int length);
    static IntPtr ReleaseFixtureHandle(Process process) {
        if(process==null || process.HasExited) return IntPtr.Zero;
        IntPtr result=IntPtr.Zero; uint processId=(uint)process.Id;
        ReleaseEnumWindows(delegate(IntPtr window,IntPtr state) {
            if(Native.ProcessOf(window)!=processId) return true;
            var title=new StringBuilder(256); ReleaseWindowText(window,title,title.Capacity);
            if(title.ToString()!="TermLens selection test fixture") return true;
            result=window; return false;
        },IntPtr.Zero);
        return result;
    }
    static async Task<bool> CloseReleaseFixture(Process process) {
        if(process==null || process.HasExited) return true;
        IntPtr window=ReleaseFixtureHandle(process);
        if(window!=IntPtr.Zero && Native.ProcessOf(window)==(uint)process.Id) Native.PostMessage(window,0x10,IntPtr.Zero,IntPtr.Zero);
        for(int i=0;i<50 && !process.HasExited;i++) await Task.Delay(40);
        return process.HasExited;
    }
    sealed class ReleaseRequestGate {
        internal readonly TaskCompletionSource<HttpResponseMessage> Completion=new TaskCompletionSource<HttpResponseMessage>();
        internal bool Canceled;
    }
    sealed class ReleaseRequestRig {
        internal readonly List<string> Bodies=new List<string>(),Secrets=new List<string>();
        internal readonly List<ReleaseRequestGate> Gates=new List<ReleaseRequestGate>();
        internal bool DelayNext,FailNext;
        internal string Answer="A **simple explanation** of the selected word.";
        internal static HttpResponseMessage Response(string answer) {
            string content=Files.Json().Serialize(new { choices=new[]{new { delta=new { content=answer },finish_reason="stop" }} });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent("data: "+content+"\n\ndata: [DONE]\n",Encoding.UTF8,"text/event-stream") };
        }
    }
    sealed class ReleaseRequestHandler : HttpMessageHandler {
        readonly ReleaseRequestRig rig;
        internal ReleaseRequestHandler(ReleaseRequestRig rig) { this.rig=rig; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) {
            rig.Bodies.Add(await request.Content.ReadAsStringAsync()); rig.Secrets.Add(request.Headers.Authorization.Parameter);
            if(rig.FailNext) { rig.FailNext=false; return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content=new StringContent("Mock selection-ledger request failure") }; }
            if(!rig.DelayNext) return ReleaseRequestRig.Response(rig.Answer);
            rig.DelayNext=false; var gate=new ReleaseRequestGate(); rig.Gates.Add(gate);
            // Intentionally ignore cancellation until manually released: stale
            // network results must not replace the active result window.
            using(token.Register(delegate { gate.Canceled=true; })) return await gate.Completion.Task;
        }
    }
    static string ReleaseMessage(string body,int index) {
        var json=Files.Json().Deserialize<Dictionary<string,object>>(body);
        return Convert.ToString(((Dictionary<string,object>)((ArrayList)json["messages"])[index])["content"]);
    }
    static async Task ReleaseWait(Func<bool> ready) {
        for(int i=0;i<100 && !ready();i++) await Task.Delay(10);
        if(!ready()) throw new Exception("Release mock did not reach its expected state.");
    }
    public static int ReleaseWorkflow(string root) {
        string folder=TestRoot(root,"release-workflow"); int exit=0;
        var config=new Settings { Prompt="Saved release prompt.",IncludeHistory=true,SpeechPreload=false,SpeechProvider="windows",ReadingTopMost=false,ReadingWidth=420,ReadingHeight=340 };
        Files.Save(folder,config); Files.SaveKey(folder,"release-test-key");
        var rig=new ReleaseRequestRig();
        var app=new MainWindow(folder,true,()=>new ReleaseRequestHandler(rig));
        app.Shown+=async delegate {
            Process fixture=null;
            try {
                Check(Program.IsTest && !Program.AllowTestHotkey,"Release workflow uses test mode without hotkey permission");
                Check(Field<int>(app,"releaseRegisteredId")==0,"No global shortcut is registered by release workflow tests");
                Check(Field<ReleaseLookupWindow>(app,"releaseWindow")==null,"Starting release does not create an input or result window");
                var history=Field<HistoryStore>(app,"history");
                history.Add("unrelated-old-word","Unrelated previous answer","model","prompt","{}");
                Field<TextBox>(app,"prompt").Text="UNSAVED PROMPT SHOULD NOT BE SENT";
                Field<TextBox>(app,"key").Text="unsaved-key";
                Field<TextBox>(app,"endpoint").Text="not a valid URL";
                var anchor=new Rectangle(250,120,1,1);
                await app.RunReleaseLookup("first-word",null,anchor);
                var window=Field<ReleaseLookupWindow>(app,"releaseWindow");
                Check(window!=null && window.Current.Status=="complete","Selected-text lookup completes in the dedicated result window");
                Check(ReleaseMessage(rig.Bodies[0],0).StartsWith(config.Prompt) && !rig.Bodies[0].Contains("UNSAVED PROMPT") && rig.Secrets[0]=="release-test-key","Requests use saved settings and saved key rather than unsaved editors");
                string primarySystem=ReleaseMessage(rig.Bodies[0],0);
                Check(primarySystem.Contains(ReleasePrompt.OutputConstraints) && primarySystem.Contains("Markdown when helpful") && primarySystem.Contains("minimal code illustrating a concept are allowed") && primarySystem.Contains("no LaTeX, formulas, equations or symbolic derivations") && primarySystem.Contains("Do not execute code") && primarySystem.Contains("no file, network or other external effects") && !primarySystem.Contains("TEMPORARY SELECTED-WORD LOOKUP"),"Primary request allows Markdown and minimal concept code while retaining math, execution and external-effect restrictions");
                Check(ReleaseMessage(rig.Bodies[0],1)=="first-word" && !rig.Bodies[0].Contains("unrelated-old-word"),"External selections do not inherit unrelated recent history");
                Check(window.Current.SourceText=="first-word","Ordinary lookup history preserves its selected source text");
                Check(history.SelectionEvents().Count==1 && history.SelectionEvents()[0].Id==window.Current.Id,"A successful lookup records one selected-text event without duplicating it on completion");
                Check(!V2Children(window).Any(c=>c is ComposerBox) && !V2Children(window).OfType<Button>().Any(c=>c.Text=="Ask" || c.Text=="Polish"),"Result window has no composer, Ask or Polish controls");
                Check(Field<ToolTip>(window,"tips").InitialDelay==1000,"Result actions retain one-second hints");
                Foreground(window); await Task.Delay(100); SettingsShot(window,Path.Combine(folder,"result-normal.png"));
                window.Bounds=new Rectangle(window.Left,window.Top,Look.S(410),Look.S(440)); var stable=window.Bounds;
                var context=LookupContext.FromSource(window.Current.Id,window.Current.Term,window.Current.Answer,"simple");
                await app.RunReleaseLookup("simple",context,anchor);
                Check(Object.ReferenceEquals(window,Field<ReleaseLookupWindow>(app,"releaseWindow")) && window.Current.Term=="simple","A new lookup replaces content in the same result window");
                Check(window.Size==stable.Size,"Replacing content preserves the manually set window size");
                var work=Screen.FromRectangle(anchor).WorkingArea;
                var nextAnchor=new Rectangle(work.Right-60,work.Bottom-60,25,20);
                var previousPosition=window.Location;
                window.ShowAt(nextAnchor);
                Check(window.Location!=previousPosition && window.Size==stable.Size,"A new selection repositions the release popup while retaining its size");
                Check(work.Contains(window.Bounds) && !window.Bounds.IntersectsWith(nextAnchor),"Popup stays on screen and avoids the selected text when space permits");
                string nestedUser=ReleaseMessage(rig.Bodies[1],1);
                Check(nestedUser.Contains("first-word") && nestedUser.Contains("simple explanation") && nestedUser.EndsWith("simple"),"Nested selected-word requests carry source and parent answer context");
                var record=history.Queries("simple",false,20).First(x=>x.Id==window.Current.Id);
                var savedContext=Files.Json().Deserialize<Dictionary<string,object>>(record.Context);
                Check(Convert.ToString(savedContext["requestUserMessage"])==nestedUser,"History stores the exact contextual user message sent to the mock API");
                Field<SoftButton>(window,"save").PerformClick();
                var favorite=history.FindFavorite("quick","simple");
                Check(favorite!=null && favorite.Saved && favorite.Context==record.Context,"Favorite retains the same actual lookup context");
                app.OpenReleaseLookup(record);
                Check(window.ParentContext!=null && Object.ReferenceEquals(window,Field<ReleaseLookupWindow>(app,"releaseWindow")),"Opening contextual history restores its context in the same result window");
                rig.Answer=String.Concat(Enumerable.Repeat("A long **explanation** with enough text to scroll while the window stays still.\n",80));
                var lengthy=LookupContext.FromSource("long-source",new string('a',4500),new string('b',7500),"term");
                window.ShowAt(anchor); stable=window.Bounds;
                await app.RunReleaseLookup("long-answer",lengthy,anchor);
                Check(window.Bounds==stable,"A long response does not resize or move the anchored result window");
                Check(Field<Label>(window,"notice").Text.Length>0,"Bounded long context leaves a small completion notice");
                window.Size=new Size(Look.S(340),Look.S(330)); Application.DoEvents();
                var footer=Field<Panel>(window,"footer"); var timeline=Field<TimelinePanel>(window,"timeline");
                var originalVoice=Field<SoftButton>(window,"readOriginal");
                Check(originalVoice.Parent==window.Original.Parent && window.Original.Right<=originalVoice.Left,"Original audio action sits beside its source without covering text");
                Check(footer.Controls.Cast<Control>().Where(c=>c.Visible).All(c=>c.Left>=0 && c.Right<=footer.ClientSize.Width && c.Bottom<=footer.ClientSize.Height),"Result actions remain inside the narrow window");
                Check(timeline.AutoScrollMinSize.Height>timeline.ClientSize.Height,"Long explanations remain scrollable in a narrow result window");
                timeline.AutoScrollPosition=Point.Empty; Foreground(window); await Task.Delay(100); SettingsShot(window,Path.Combine(folder,"result-long-narrow.png"));
                rig.Answer="A **simple explanation** of the selected word.";

                rig.DelayNext=true; Task stale=app.RunReleaseLookup("stale-query",null,anchor);
                await ReleaseWait(()=>rig.Gates.Count==1);
                string staleSelectionId=window.Current.Id;
                Check(history.SelectionEvents().Count(x=>x.Id==staleSelectionId)==1,"Selection is recorded before a pending API request completes");
                await app.RunReleaseLookup("newest-query",null,anchor);
                Check(rig.Gates[0].Canceled,"Starting a new lookup cancels the previous request token");
                rig.Gates[0].Completion.TrySetResult(ReleaseRequestRig.Response("STALE RESPONSE MUST NOT REPLACE THE NEW RESULT"));
                await stale;
                Check(window.Current.Term=="newest-query" && !window.Answer.Text.Contains("STALE RESPONSE"),"A late canceled response cannot overwrite the newest result");
                Check(history.Queries("stale-query",false,10).Count==0,"Canceled superseded requests are not recorded as successful lookups");
                Check(history.SelectionEvents().Count(x=>x.Id==staleSelectionId)==1,"Canceling a request preserves exactly one event for its selected text");
                rig.FailNext=true;
                await app.RunReleaseLookup("failed-selection",null,anchor);
                string failedSelectionId=window.Current.Id;
                Check(window.Current.Status=="failed" && history.SelectionEvents().Count(x=>x.Id==failedSelectionId)==1,"A failed request retains one selection event without duplicating it in failure history");
                await app.RunReleaseLookup("newest-query",null,anchor);

                var originalBeforeHistory=window.Current; int requestCountBeforeHistory=rig.Bodies.Count;
                window.ToggleHistory();
                Check(window.IsHistoryList && window.HistoryProvider().Any(entry=>entry.Id==record.Id),"Main result history provider returns stored contextual and primary queries");
                window.ShowHistoryDetail(record);
                Check(window.Current.Id==record.Id && window.ParentContext!=null && window.Current.Saved,"Historical detail restores its stored context and favorite state");
                window.ToggleHistory(); window.ToggleHistory();
                Check(Object.ReferenceEquals(window.Current,originalBeforeHistory) && rig.Bodies.Count==requestCountBeforeHistory,"History navigation returns to the same original query without making a model request");

                rig.DelayNext=true; Task closing=app.RunReleaseLookup("closing-query",null,anchor);
                await ReleaseWait(()=>rig.Gates.Count==2);
                window.ToggleHistory(); window.ShowHistoryDetail(record); window.SetSpeaking(true,"fixture audio");
                Field<SoftButton>(window,"stop").PerformClick();
                Check(!rig.Gates[1].Canceled && window.OriginalLookup.Status=="pending","Stopping audio in history detail leaves the original model request running");
                window.Close();
                Check(rig.Gates[1].Canceled,"Closing the result window cancels its request");
                rig.Gates[1].Completion.TrySetResult(ReleaseRequestRig.Response("CLOSED RESPONSE MUST NOT REOPEN WINDOW"));
                await closing;
                Check(Field<ReleaseLookupWindow>(app,"releaseWindow")==null && window.IsDisposed,"Late completion does not reopen a closed result window");
                Check(Field<int>(app,"releaseRegisteredId")==0,"Mock requests and closing leave the global hotkey untouched");

                fixture=Process.Start(new ProcessStartInfo(Application.ExecutablePath,"--selection-fixture --no-hotkey") { UseShellExecute=false,WindowStyle=ProcessWindowStyle.Hidden,CreateNoWindow=true });
                IntPtr fixtureHandle=IntPtr.Zero;
                for(int i=0;i<100 && fixtureHandle==IntPtr.Zero;i++) { await Task.Delay(50); fixtureHandle=ReleaseFixtureHandle(fixture); }
                Check(fixtureHandle!=IntPtr.Zero && Native.ProcessOf(fixtureHandle)==(uint)fixture.Id,"External selection fixture is identified by its exact process and window title");
                ExternalForeground(fixtureHandle);
                Native.PostMessage(fixtureHandle,0x8002,IntPtr.Zero,IntPtr.Zero); await Task.Delay(200); ExternalForeground(fixtureHandle);
                Check(Native.GetForegroundWindow()==fixtureHandle,"External fixture owns foreground before native capture; no physical click is used");
                int captureRequest=rig.Bodies.Count;
                await WorkCall(app,"CaptureRelease",fixtureHandle);
                window=Field<ReleaseLookupWindow>(app,"releaseWindow");
                const string fixtureText="recursion 递归 — fixture selection";
                Check(window!=null && window.Current.Status=="complete" && window.Current.Term==fixtureText,"CaptureRelease obtains the complete Chinese and English external selection");
                Check(window.Current.Kind=="explain" && ReleaseMessage(rig.Bodies[captureRequest],0).StartsWith(config.Prompt) && ReleaseMessage(rig.Bodies[captureRequest],1).EndsWith(fixtureText),"An editable external text field still uses Explain, never Polish");
                string topicPayload=ReleaseMessage(rig.Bodies[captureRequest],1);
                Check(topicPayload.Contains("newest-query") && !topicPayload.Contains("stale-query") && !topicPayload.Contains("closing-query") && !topicPayload.Contains("unrelated-old-word"),"Primary requests use recent completed originals but exclude canceled requests and database history from other sessions");
                Native.PostMessage(fixtureHandle,0x8001,IntPtr.Zero,IntPtr.Zero); await Task.Delay(100); ExternalForeground(fixtureHandle);
                string settingsBeforeShortcut=File.ReadAllText(Path.Combine(folder,"settings.json"),Files.Utf8);
                int requestsBeforeEmpty=rig.Bodies.Count;
                int eventsBeforeEmpty=history.SelectionEvents().Count;
                await ReleaseSimulatedShortcut(app,fixtureHandle);
                Check(Field<ReleaseLookupWindow>(app,"releaseWindow")==null && window.IsDisposed && !app.Visible && rig.Bodies.Count==requestsBeforeEmpty,"An empty external selection closes the visible reading window before opening settings");
                // Closing a reading window legitimately persists its geometry.
                // Compare after that close when testing the settings toggle.
                settingsBeforeShortcut=File.ReadAllText(Path.Combine(folder,"settings.json"),Files.Utf8);
                ExternalForeground(fixtureHandle);
                await ReleaseSimulatedShortcut(app,fixtureHandle);
                Check(app.Visible && Field<int>(app,"releasePage")==1 && Field<Label>(app,"releaseHeading").Text=="提示词","Confirmed empty external selection opens the actual prompt archive settings page");
                Check(rig.Bodies.Count==requestsBeforeEmpty && File.ReadAllText(Path.Combine(folder,"settings.json"),Files.Utf8)==settingsBeforeShortcut,"Empty-selection shortcut sends no model request and does not save settings");
                ExternalForeground(fixtureHandle);
                await ReleaseSimulatedShortcut(app,fixtureHandle);
                Check(!app.Visible && rig.Bodies.Count==requestsBeforeEmpty,"A second empty external selection hides settings without a request");
                ExternalForeground(fixtureHandle);
                await ReleaseSimulatedShortcut(app,fixtureHandle);
                Check(app.Visible && Field<int>(app,"releasePage")==1 && rig.Bodies.Count==requestsBeforeEmpty,"The next empty external selection reopens the scene chooser exactly once");
                var settingsPrompt=Field<TextBox>(app,"prompt");
                string draftBeforeToggle=settingsPrompt.Text;
                settingsPrompt.Text="UNSAVED DRAFT RETAINED BY TOGGLE";
                Foreground(app); settingsPrompt.Focus(); settingsPrompt.SelectAll();
                await ReleaseSimulatedShortcut(app,settingsPrompt.Handle);
                Check(!app.Visible && settingsPrompt.Text=="UNSAVED DRAFT RETAINED BY TOGGLE" && rig.Bodies.Count==requestsBeforeEmpty,"A selected settings editor hides the window, preserves its draft and never queries the highlighted prompt");
                settingsPrompt.Text=draftBeforeToggle;
                app.ShowReleasePromptLibrary(); Foreground(app);
                var shortcutInput=Field<ReleaseShortcutInput>(app,"releaseTrigger");
                shortcutInput.BeginCapture();
                await WorkCall(app,"CaptureRelease",app.Handle);
                Check(app.Visible && shortcutInput.IsRecording && rig.Bodies.Count==requestsBeforeEmpty,"An active shortcut recording keeps settings visible and is not swallowed by the toggle");
                shortcutInput.CancelCapture(); app.Hide();
                ExternalForeground(fixtureHandle);
                Clipboard.SetText("OLD CLIPBOARD MUST NOT BECOME A QUERY");
                uint unchangedClipboard=Native.GetClipboardSequenceNumber();
                bool quietEmpty=false;
                try { await Selection.CopySelection(fixtureHandle); } catch(NoSelectionException e) { quietEmpty=e.ConfirmedEmpty; }
                Check(quietEmpty && Native.GetClipboardSequenceNumber()==unchangedClipboard && Clipboard.GetText()=="OLD CLIPBOARD MUST NOT BECOME A QUERY" && rig.Bodies.Count==requestsBeforeEmpty,"A quiet empty clipboard probe confirms the toggle branch without querying or changing old clipboard text");
                Check(history.SelectionEvents().Count==eventsBeforeEmpty,"Empty selections, settings drafts and shortcut recording create no selection-ledger events");
                app.ShowReleasePromptLibrary();
                Native.PostMessage(fixtureHandle,0x8002,IntPtr.Zero,IntPtr.Zero); await Task.Delay(100); ExternalForeground(fixtureHandle);
                await ReleaseSimulatedShortcut(app,fixtureHandle);
                Check(app.Visible && rig.Bodies.Count==requestsBeforeEmpty+1 && Field<ReleaseLookupWindow>(app,"releaseWindow").Current.Term==fixtureText,"An external selection still queries normally while settings are visible");
                window=Field<ReleaseLookupWindow>(app,"releaseWindow");
                app.Hide();
                await WorkCall(app,"CaptureRelease",IntPtr.Zero);
                Check(!app.Visible && rig.Bodies.Count==requestsBeforeEmpty+1,"An invalid capture source reports an error and does not open prompt settings");
                Check(!new NoSelectionException().ConfirmedEmpty && new NoSelectionException(true).ConfirmedEmpty,"Unsuccessful capture and positively empty selection remain distinct");
                Check(await CloseReleaseFixture(fixture),"Only the spawned fixture is closed normally with WM_CLOSE");
                fixture.Dispose(); fixture=null;
                string parentTerm=window.Current.Term,parentAnswer=window.Current.Answer;
                Foreground(window); window.Answer.Focus();
                int selectedAt=window.Answer.Text.IndexOf("simple",StringComparison.Ordinal);
                Check(selectedAt>=0,"Fixture answer contains the nested lookup word");
                window.Answer.Select(selectedAt,6); await Task.Delay(60);
                Check(Native.GetForegroundWindow()==window.Handle && window.Answer.SelectedText=="simple","Internal selected answer text is focused before direct capture invocation");
                int nestedCapture=rig.Bodies.Count;
                await WorkCall(app,"CaptureRelease",window.Handle);
                var secondaryWindow=Field<ReleaseLookupWindow>(app,"releaseSecondaryWindow");
                string childSystem=ReleaseMessage(rig.Bodies[nestedCapture],0);
                Check(childSystem.StartsWith(Settings.ReleaseSecondaryDefaultPrompt) && childSystem.Contains("IMMEDIATE PARENT") && childSystem.Contains(ReleasePrompt.OutputConstraints) && childSystem.Contains("no LaTeX, formulas, equations or symbolic derivations") && childSystem.Contains("Minimal concept-explanation code is allowed") && childSystem.Contains("No ordinary examples") && !childSystem.Contains("one or two short sentences") && !childSystem.Contains(config.Prompt),"Secondary request uses its separate contextual prompt, allows minimal concept code and excludes ordinary examples without a rigid length limit or the primary prompt");
                string capturedUser=ReleaseMessage(rig.Bodies[nestedCapture],1);
                Check(secondaryWindow.Current.Kind=="quick" && secondaryWindow.Current.Term=="simple" && window.Current.Term==parentTerm && capturedUser.Contains(parentTerm) && capturedUser.Contains("simple explanation"),"CaptureRelease opens a secondary result with parent context and preserves the original window");
                var capturedRecord=Files.Json().Deserialize<Dictionary<string,object>>(secondaryWindow.Current.Context);
                var capturedContext=(Dictionary<string,object>)capturedRecord["lookupContext"];
                Check(Convert.ToString(capturedContext["rootOriginal"])==parentTerm && Convert.ToString(capturedContext["rootAnswer"])==parentAnswer,"Native internal capture preserves the full parent source and answer in stored context");
                Foreground(secondaryWindow); await Task.Delay(100); SettingsShot(secondaryWindow,Path.Combine(folder,"result-captured-context.png"));
                secondaryWindow.Original.Select(0,0); secondaryWindow.Answer.Select(0,0);
                int requestsBeforeInternalEmpty=rig.Bodies.Count;
                await ReleaseSimulatedShortcut(app,secondaryWindow.Handle);
                Check(secondaryWindow.IsDisposed && Field<ReleaseLookupWindow>(app,"releaseSecondaryWindow")==null && !window.IsDisposed && window.Visible && !app.Visible && rig.Bodies.Count==requestsBeforeInternalEmpty,"An empty secondary selection closes only the secondary reading window without a model request");
                Foreground(window); window.Original.Select(0,0); window.Answer.Select(0,0);
                await ReleaseSimulatedShortcut(app,window.Handle);
                Check(window.IsDisposed && Field<ReleaseLookupWindow>(app,"releaseWindow")==null && !app.Visible && rig.Bodies.Count==requestsBeforeInternalEmpty,"An empty main-answer selection closes the main reading window without opening settings or querying");
                await ReleaseScenarioSnapshotWorkflow(app,rig,history,anchor);
                results.Add("INFO: All requests used an in-memory HttpMessageHandler. No network, voice playback or global shortcut activation was performed.");
            } catch(Exception e) { results.Add("FAIL: "+e); exit=1; }
            finally {
                if(fixture!=null) {
                    try {
                        if(!fixture.HasExited) {
                            IntPtr owned=ReleaseFixtureHandle(fixture);
                            if(owned!=IntPtr.Zero && Native.ProcessOf(owned)==(uint)fixture.Id) Native.PostMessage(owned,0x10,IntPtr.Zero,IntPtr.Zero);
                            if(!fixture.WaitForExit(2000)) { results.Add("FAIL: The test fixture did not exit after WM_CLOSE; no process was killed."); exit=1; }
                        }
                    }
                    catch(Exception cleanup) { results.Add("FAIL: Fixture cleanup: "+cleanup.Message); exit=1; }
                    finally { fixture.Dispose(); }
                }
                foreach(var gate in rig.Gates) gate.Completion.TrySetResult(ReleaseRequestRig.Response("test cleanup"));
                typeof(MainWindow).GetField("exiting",Private).SetValue(app,true); app.Close();
                File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8);
            }
        };
        Application.Run(app); app.Dispose(); return exit;
    }
}
}
