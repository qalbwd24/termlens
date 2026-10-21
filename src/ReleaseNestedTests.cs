using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TermLens {
public static partial class Tests {
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")] static extern int ReleaseNestedWindowLong(IntPtr window,int index);
    static void ReleaseNestedForeground(Form form) {
        uint ignored; uint previous=Native.GetWindowThreadProcessId(Native.GetForegroundWindow(),out ignored),current=GetCurrentThreadId();
        bool attached=previous!=0 && previous!=current && AttachThreadInput(current,previous,true);
        // Foreground(Form) sets TopMost=true; these tests must preserve the actual
        // pin state while granting focus, including the unpinned creation case.
        try { form.Show(); form.Activate(); Native.SetForegroundWindow(form.Handle); }
        finally { if(attached) AttachThreadInput(current,previous,false); }
    }
    static void ReleaseNestedTopMost(ReleaseLookupWindow primary,ReleaseLookupWindow secondary,bool expected,string stage) {
        Check(!primary.IsDisposed && !secondary.IsDisposed && primary.TopMost==expected && secondary.TopMost==expected,stage+": primary and secondary managed TopMost states agree");
        const int extendedStyle=-20,topmost=0x8;
        Check(((ReleaseNestedWindowLong(primary.Handle,extendedStyle)&topmost)!=0)==expected && ((ReleaseNestedWindowLong(secondary.Handle,extendedStyle)&topmost)!=0)==expected,stage+": both native HWNDs have the expected WS_EX_TOPMOST state");
    }
    static async Task ReleaseNestedSelect(ReleaseLookupWindow window,string word) {
        ReleaseNestedForeground(window); window.Answer.Focus();
        int at=window.Answer.Text.IndexOf(word,StringComparison.Ordinal);
        Check(at>=0,"The requested nested word exists in the visible answer");
        window.Answer.Select(at,word.Length); await Task.Delay(80);
        Check(Native.GetForegroundWindow()==window.Handle && window.Answer.SelectedText==word,"Nested capture starts from the exact focused result and selected text");
    }
    static Dictionary<string,object> ReleaseNestedContext(Lookup item,string actualUser) {
        var saved=Files.Json().Deserialize<Dictionary<string,object>>(item.Context);
        Check(Convert.ToString(saved["requestUserMessage"])==actualUser,"Nested history context matches the actual request payload");
        return (Dictionary<string,object>)saved["lookupContext"];
    }
    static void ReleaseNestedHints(ReleaseLookupWindow window) {
        var tips=Field<ToolTip>(window,"tips");
        Check(String.IsNullOrEmpty(tips.GetToolTip(window.Answer)) && String.IsNullOrEmpty(tips.GetToolTip(window.Original)),"Reading text has no hover tooltip");
        Check(tips.InitialDelay==1000 && tips.ReshowDelay==1000 && !String.IsNullOrWhiteSpace(tips.GetToolTip(Field<SoftButton>(window,"copy"))),"Icon action hints retain their one-second delay and text");
    }
    public static int ReleaseNested(string root) {
        string folder=TestRoot(root,"release-nested"); int exit=0;
        Files.Save(folder,new Settings { Prompt="Isolated nested lookup prompt.",SpeechPreload=false,SpeechProvider="windows",ReadingTopMost=false,ReadingWidth=420,ReadingHeight=360 });
        Files.SaveKey(folder,"isolated-release-nested-test-key");
        var rig=new ReleaseRequestRig(); MainWindow app=null;
        try {
            Check(Program.IsRelease && Program.IsTest && !Program.AllowTestHotkey,"Nested tests require release test mode without global hotkey permission");
            app=new MainWindow(folder,true,()=>new ReleaseRequestHandler(rig));
            app.Shown+=async delegate {
                try {
                    Check(Field<int>(app,"releaseRegisteredId")==0,"Nested tests do not register a global shortcut");
                    var anchor=new Rectangle(260,150,20,20);
                    rig.Answer="Root explanation contains branchword and anotherword.";
                    await app.RunReleaseLookup("root-source-text",null,anchor);
                    var primary=Field<ReleaseLookupWindow>(app,"releaseWindow");
                    Check(primary!=null && primary.Current.Status=="complete","The external lookup creates a completed primary result");
                    primary.Bounds=new Rectangle(primary.Left,primary.Top,Look.S(440),Look.S(380));
                    var bounds=primary.Bounds; var rootItem=primary.Current;
                    string original=primary.Original.Text,answer=primary.Answer.Text;
                    Action unchanged=delegate {
                        Check(Object.ReferenceEquals(primary,Field<ReleaseLookupWindow>(app,"releaseWindow")) && Object.ReferenceEquals(primary.Current,rootItem) && primary.Original.Text==original && primary.Answer.Text==answer && primary.Bounds==bounds,"Nested lookup preserves the primary item, visible content and exact bounds");
                    };
                    ReleaseNestedHints(primary);
                    await ReleaseNestedSelect(primary,"branchword");
                    rig.Answer="Branch explanation contains leafword."; int firstRequest=rig.Bodies.Count;
                    await WorkCall(app,"CaptureRelease",primary.Handle);
                    var secondary=Field<ReleaseLookupWindow>(app,"releaseSecondaryWindow");
                    Check(secondary!=null && !Object.ReferenceEquals(primary,secondary) && secondary.Current.Term=="branchword" && secondary.Current.Status=="complete","Selection within the primary answer opens a separate completed secondary result");
                    var oldGeometry=Files.Load(folder);
                    var chosenSize=new Size(Look.S(410),Look.S(350));
                    secondary.Size=chosenSize;
                    typeof(Form).GetMethod("OnResizeEnd",Private).Invoke(secondary,new object[]{EventArgs.Empty});
                    var remembered=Files.Load(folder);
                    Check(remembered.SecondaryWindowWidth==410 && remembered.SecondaryWindowHeight==350,"Secondary manual resize is persisted in logical pixels");
                    Check(remembered.WorkspaceWidth==oldGeometry.WorkspaceWidth && remembered.WorkspaceHeight==oldGeometry.WorkspaceHeight && primary.Bounds==bounds,"Secondary resizing does not overwrite primary geometry");
                    secondary.Size=new Size(Look.S(340),Look.S(270));
                    typeof(Form).GetMethod("OnResizeBegin",Private).Invoke(secondary,new object[]{EventArgs.Empty});
                    typeof(Form).GetMethod("OnResizeEnd",Private).Invoke(secondary,new object[]{EventArgs.Empty});
                    remembered=Files.Load(folder);
                    Check(remembered.SecondaryWindowWidth==410 && remembered.SecondaryWindowHeight==350,"Moving a temporarily clamped child does not replace the preferred size");
                    secondary.Size=chosenSize;
                    using(var reopened=new ReleaseLookupWindow(remembered,true)) {
                        Check(reopened.Size==chosenSize,"Secondary size is restored from the on-disk profile in a fresh window");
                    }
                    Application.DoEvents();
                    string secondaryShot=Path.Combine(folder,"secondary-black-outline.png");
                    SettingsShot(secondary,secondaryShot); CheckReadingOutline(secondaryShot);
                    ReleaseNestedTopMost(primary,secondary,false,"Secondary created from an unpinned primary");
                    // Invoke on the UI thread without moving keyboard focus to
                    // the primary, which would dismiss the transient secondary.
                    Field<SoftButton>(primary,"pin").PerformClick();
                    Check(Object.ReferenceEquals(secondary,Field<ReleaseLookupWindow>(app,"releaseSecondaryWindow")),"Pinning the primary preserves the existing secondary window");
                    ReleaseNestedTopMost(primary,secondary,true,"Primary pin toggled on with an existing child");
                    Field<SoftButton>(primary,"pin").PerformClick();
                    ReleaseNestedTopMost(primary,secondary,false,"Primary pin toggled off with an existing child");
                    unchanged(); ReleaseNestedHints(secondary);
                    var first=ReleaseNestedContext(secondary.Current,ReleaseMessage(rig.Bodies[firstRequest],1));
                    Check(Convert.ToString(first["rootSourceId"])==rootItem.Id && Convert.ToString(first["rootOriginal"])==rootItem.Term && Convert.ToString(first["rootAnswer"])==rootItem.Answer,"First nested request retains the primary source and answer");
                    string firstAnswer=secondary.Current.Answer;
                    await ReleaseNestedSelect(secondary,"leafword");
                    rig.Answer="Leaf explanation contains budword."; int nextRequest=rig.Bodies.Count;
                    await WorkCall(app,"CaptureRelease",secondary.Handle);
                    Check(Object.ReferenceEquals(secondary,Field<ReleaseLookupWindow>(app,"releaseSecondaryWindow")) && secondary.Current.Term=="leafword","Selection inside the secondary result reuses that same secondary window");
                    var second=ReleaseNestedContext(secondary.Current,ReleaseMessage(rig.Bodies[nextRequest],1));
                    Check(Convert.ToString(second["rootSourceId"])==rootItem.Id && Convert.ToString(second["rootOriginal"])==rootItem.Term && Convert.ToString(second["rootAnswer"])==rootItem.Answer && Convert.ToString(second["parentQuery"])=="branchword" && Convert.ToString(second["parentAnswer"])==firstAnswer,"Recursive lookup preserves the root and updates only the immediate parent");
                    unchanged();
                    await ReleaseNestedSelect(secondary,"budword");
                    string leafAnswer=secondary.Current.Answer;
                    rig.Answer="Bud explanation."; int thirdRequest=rig.Bodies.Count;
                    await WorkCall(app,"CaptureRelease",secondary.Handle);
                    var third=ReleaseNestedContext(secondary.Current,ReleaseMessage(rig.Bodies[thirdRequest],1));
                    Check(Object.ReferenceEquals(secondary,Field<ReleaseLookupWindow>(app,"releaseSecondaryWindow")) && Convert.ToString(third["rootOriginal"])==rootItem.Term && Convert.ToString(third["parentQuery"])=="leafword" && Convert.ToString(third["parentAnswer"])==leafAnswer && Convert.ToString(third["parentAnswer"])!=firstAnswer,"Further recursion remains two windows and replaces the immediate parent instead of accumulating ancestors");
                    unchanged();
                    ReleaseNestedForeground(primary);
                    await ReleaseWait(()=>Field<ReleaseLookupWindow>(app,"releaseSecondaryWindow")==null);
                    Check(secondary.IsDisposed && primary.Visible && !primary.IsDisposed,"Losing focus closes only the secondary window"); unchanged();
                    remembered=Files.Load(folder);
                    Check(remembered.SecondaryWindowWidth==410 && remembered.SecondaryWindowHeight==350,"Focus-loss dismissal retains the chosen secondary dimensions");

                    var context=LookupContext.FromSource(rootItem.Id,rootItem.Term,rootItem.Answer,"pending-child");
                    Field<SoftButton>(primary,"pin").PerformClick();
                    rig.Answer="Child created while the primary is pinned.";
                    await app.RunReleaseLookup("pinned-child",context,anchor,true);
                    var pinnedChild=Field<ReleaseLookupWindow>(app,"releaseSecondaryWindow");
                    Check(pinnedChild.Size==chosenSize,"A subsequent contextual lookup reopens at the independently remembered size");
                    ReleaseNestedTopMost(primary,pinnedChild,true,"Secondary created from a pinned primary");
                    unchanged();
                    ReleaseNestedForeground(primary);
                    await ReleaseWait(()=>Field<ReleaseLookupWindow>(app,"releaseSecondaryWindow")==null);
                    Check(pinnedChild.IsDisposed && primary.TopMost,"A pinned secondary still closes on focus loss without unpinning its primary");
                    Field<SoftButton>(primary,"pin").PerformClick();
                    rig.DelayNext=true; int gateAt=rig.Gates.Count;
                    Task pending=app.RunReleaseLookup("pending-child",context,anchor,true);
                    await ReleaseWait(()=>rig.Gates.Count==gateAt+1);
                    var closingChild=Field<ReleaseLookupWindow>(app,"releaseSecondaryWindow");
                    ReleaseNestedForeground(closingChild); await Task.Delay(60); ReleaseNestedForeground(primary);
                    await ReleaseWait(()=>Field<ReleaseLookupWindow>(app,"releaseSecondaryWindow")==null);
                    Check(rig.Gates[gateAt].Canceled,"Secondary focus loss cancels its pending request");
                    rig.Gates[gateAt].Completion.TrySetResult(ReleaseRequestRig.Response("LATE CHILD MUST NOT REOPEN"));
                    await pending;
                    Check(closingChild.IsDisposed && Field<ReleaseLookupWindow>(app,"releaseSecondaryWindow")==null,"Late child completion does not recreate a dismissed secondary window"); unchanged();

                    rig.Answer="Child before external lookup.";
                    await app.RunReleaseLookup("external-reset-child",context,anchor,true);
                    var resetChild=Field<ReleaseLookupWindow>(app,"releaseSecondaryWindow");
                    rig.Answer="Replacement primary explanation.";
                    await app.RunReleaseLookup("replacement-source",null,anchor);
                    Check(resetChild.IsDisposed && Field<ReleaseLookupWindow>(app,"releaseSecondaryWindow")==null && primary.Current.Term=="replacement-source","A new external lookup clears the old secondary result");

                    // Hold the primary request while completing a child. Distinct
                    // cancellation channels must let both settle independently.
                    rig.DelayNext=true; int primaryGate=rig.Gates.Count;
                    Task pendingPrimary=app.RunReleaseLookup("independent-primary",null,anchor);
                    await ReleaseWait(()=>rig.Gates.Count==primaryGate+1);
                    rig.Answer="Independent child answer.";
                    await app.RunReleaseLookup("independent-child",context,anchor,true);
                    var independentChild=Field<ReleaseLookupWindow>(app,"releaseSecondaryWindow");
                    Check(!rig.Gates[primaryGate].Canceled && primary.Current.Term=="independent-primary" && primary.Current.Status=="pending" && independentChild.Current.Status=="complete","A child request neither cancels nor overwrites the pending primary request");
                    rig.Gates[primaryGate].Completion.TrySetResult(ReleaseRequestRig.Response("Independent primary answer."));
                    await pendingPrimary;
                    Check(primary.Current.Answer=="Independent primary answer." && independentChild.Current.Term=="independent-child" && independentChild.Current.Answer=="Independent child answer.","Primary completion leaves the child result intact");

                    rig.DelayNext=true; int closingGate=rig.Gates.Count;
                    Task closing=app.RunReleaseLookup("close-with-parent",context,anchor,true);
                    await ReleaseWait(()=>rig.Gates.Count==closingGate+1);
                    var finalChild=Field<ReleaseLookupWindow>(app,"releaseSecondaryWindow");
                    primary.Close();
                    Check(primary.IsDisposed && finalChild.IsDisposed && Field<ReleaseLookupWindow>(app,"releaseWindow")==null && Field<ReleaseLookupWindow>(app,"releaseSecondaryWindow")==null && rig.Gates[closingGate].Canceled,"Closing the primary also closes the secondary and cancels its request");
                    rig.Gates[closingGate].Completion.TrySetResult(ReleaseRequestRig.Response("CLOSED FAMILY MUST NOT REOPEN"));
                    await closing;
                    Check(Field<ReleaseLookupWindow>(app,"releaseWindow")==null && Field<ReleaseLookupWindow>(app,"releaseSecondaryWindow")==null && Field<int>(app,"releaseRegisteredId")==0,"Late completion leaves both windows closed and the real global hotkey untouched");
                    results.Add("INFO: All profiles, keys and requests are isolated test data; no network or user instance was used.");
                } catch(Exception e) { results.Add("FAIL: "+e); exit=1; }
                finally {
                    typeof(MainWindow).GetField("exiting",Private).SetValue(app,true);
                    typeof(MainWindow).GetMethod("CloseReleaseWorkflow",Private).Invoke(app,null);
                    foreach(var gate in rig.Gates) gate.Completion.TrySetResult(ReleaseRequestRig.Response("test cleanup"));
                    app.Close(); File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8);
                }
            };
            Application.Run(app);
        } catch(Exception e) { results.Add("FAIL: "+e); exit=1; }
        finally { if(app!=null) app.Dispose(); File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); }
        return exit;
    }
}
}
