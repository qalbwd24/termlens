using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TermLens {
public static partial class Tests {
    // Exercise WM_HOTKEY delivery and the production route without registering
    // a global shortcut or sending keyboard input to the running user's app.
    static async Task ReleaseSimulatedShortcut(MainWindow app,IntPtr source) {
        Check(Field<int>(app,"releaseRegisteredId")==0,"Simulated shortcut owns no global registration");
        var done=new TaskCompletionSource<bool>(); int received=0;
        using(var host=new ReleaseHotkeyMessageWindow()) {
            host.Pressed+=async delegate(int id) {
                try { received++; await app.RouteReleaseShortcut(source); done.TrySetResult(true); }
                catch(Exception error) { done.TrySetException(error); }
            };
            Native.PostMessage(host.Handle,0x312,new IntPtr(40),IntPtr.Zero);
            await done.Task;
        }
        Check(received==1 && Field<int>(app,"releaseRegisteredId")==0,"One simulated WM_HOTKEY invokes the shortcut route once without OS registration");
    }
    [DllImport("user32.dll",EntryPoint="AllowSetForegroundWindow")] static extern bool ReleaseTestAllowForeground(uint processId);
    sealed class ReleaseHotkeyTestHost : NativeWindow,IDisposable {
        public ReleaseHotkeyTestHost() { CreateHandle(new CreateParams { Caption="TermLens isolated hotkey conflict fixture",Parent=new IntPtr(-3) }); }
        public void Dispose() { if(Handle!=IntPtr.Zero) DestroyHandle(); }
    }
    static async Task ReleaseHotkeyWait(Func<bool> ready,int milliseconds,string failure) {
        var watch=Stopwatch.StartNew();
        while(!ready()) { if(watch.ElapsedMilliseconds>=milliseconds) throw new TimeoutException(failure); await Task.Delay(40); }
    }
    static void ReleaseHotkeyInvoke(MainWindow app,string method) {
        var found=typeof(MainWindow).GetMethod(method,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
        if(found==null) throw new MissingMethodException("MainWindow",method);
        found.Invoke(app,null);
    }
    static async Task<IntPtr> ReleaseHotkeyForeground(Process fixture) {
        fixture.Refresh(); IntPtr source=fixture.MainWindowHandle;
        Check(source!=IntPtr.Zero && Native.ProcessOf(source)==(uint)fixture.Id,"Selection fixture HWND belongs to the exact child PID");
        bool granted=ReleaseTestAllowForeground((uint)fixture.Id);
        results.Add("INFO: AllowSetForegroundWindow for fixture PID "+fixture.Id+" returned "+granted);
        for(int attempt=0;attempt<8;attempt++) {
            uint ignored; uint current=GetCurrentThreadId();
            uint target=Native.GetWindowThreadProcessId(source,out ignored);
            bool attached=target!=current && AttachThreadInput(current,target,true);
            try { ExternalForeground(source); Native.PostMessage(source,0x8002,IntPtr.Zero,IntPtr.Zero); }
            finally { if(attached) AttachThreadInput(current,target,false); }
            await Task.Delay(100);
            if(Native.GetForegroundWindow()==source) return source;
        }
        return source; // Caller still asserts real foreground ownership before input.
    }
    static async Task ReleaseHotkeyTrigger(MainWindow app,Process fixture,List<Handler> handlers,int expected,string stage,Keys trigger,uint modifiers=0,bool repeat=false) {
        IntPtr source=await ReleaseHotkeyForeground(fixture);
        Check(Native.GetForegroundWindow()==source,stage+": external fixture owns keyboard focus");
        Check(!Native.ModifiersDown(),stage+": no user modifier key is held during synthetic "+trigger);
        string trace=Path.Combine(UiTrace.Root,"ui-events.log");
        int messagesBefore=File.Exists(trace)?File.ReadAllLines(trace,Files.Utf8).Count(line=>line.Contains("release hotkey received id=")):0;
        try {
            if((modifiers&2)!=0) Native.keybd_event((byte)Keys.ControlKey,0,0,UIntPtr.Zero);
            if((modifiers&1)!=0) Native.keybd_event((byte)Keys.Menu,0,0,UIntPtr.Zero);
            if((modifiers&4)!=0) Native.keybd_event((byte)Keys.ShiftKey,0,0,UIntPtr.Zero);
            Native.keybd_event((byte)trigger,0,0,UIntPtr.Zero);
            if(repeat) for(int i=0;i<5;i++) { await Task.Delay(90); Native.keybd_event((byte)trigger,0,0,UIntPtr.Zero); }
        }
        finally {
            Native.keybd_event((byte)trigger,0,2,UIntPtr.Zero);
            if((modifiers&4)!=0) Native.keybd_event((byte)Keys.ShiftKey,0,2,UIntPtr.Zero);
            if((modifiers&1)!=0) Native.keybd_event((byte)Keys.Menu,0,2,UIntPtr.Zero);
            if((modifiers&2)!=0) Native.keybd_event((byte)Keys.ControlKey,0,2,UIntPtr.Zero);
        }
        await ReleaseHotkeyWait(delegate {
            var window=Field<ReleaseLookupWindow>(app,"releaseWindow");
            return handlers.Count>=expected && handlers[expected-1].Body!=null && window!=null && !window.IsDisposed && window.Current!=null && window.Current.Status=="complete";
        },10000,stage+": real "+trigger+" did not reach a completed mocked lookup");
        var current=Field<ReleaseLookupWindow>(app,"releaseWindow").Current;
        Check(current.Term.Contains("recursion") && current.Answer=="Isolated hotkey response "+expected,stage+": WM_HOTKEY routes through selection capture and the mock HTTP response");
        if(repeat) {
            await Task.Delay(180);
            int messagesAfter=File.ReadAllLines(trace,Files.Utf8).Count(line=>line.Contains("release hotkey received id="));
            Check(messagesAfter-messagesBefore==1 && handlers.Count==expected,"MOD_NOREPEAT: six key-down events before one key-up produce exactly one received hotkey message and one request");
        }
    }
    public static int ReleaseHotkey(string root) {
        string folder=TestRoot(root,"release-hotkey"); int exit=0;
        Keys trigger=Environment.GetCommandLineArgs().Contains("--test-trigger-f23")?Keys.F23:Keys.F13;
        MainWindow app=null; Process fixture=null; ReleaseHotkeyTestHost conflict=null;
        bool conflictRegistered=false; const int conflictId=5091; bool cleaned=false;
        var handlers=new List<Handler>();
        Action cleanup=delegate {
            if(cleaned) return; cleaned=true;
            if(conflict!=null) { if(conflictRegistered) Native.UnregisterHotKey(conflict.Handle,conflictId); conflict.Dispose(); conflict=null; }
            if(fixture!=null) {
                try {
                    if(!fixture.HasExited) {
                        fixture.Refresh(); IntPtr hwnd=fixture.MainWindowHandle;
                        if(hwnd!=IntPtr.Zero && Native.ProcessOf(hwnd)==(uint)fixture.Id) Native.PostMessage(hwnd,0x10,IntPtr.Zero,IntPtr.Zero);
                        if(!fixture.WaitForExit(4000)) { results.Add("FAIL: fixture PID "+fixture.Id+" did not close within 4 seconds; no force kill was used (fixture also has its own expiry timer)."); exit=1; }
                    }
                } catch(Exception e) { results.Add("FAIL: fixture cleanup "+e.Message); exit=1; }
                finally { fixture.Dispose(); fixture=null; }
            }
            if(app!=null && !app.IsDisposed) {
                try { ReleaseHotkeyInvoke(app,"SuspendReleaseHotkey"); } catch { }
                typeof(MainWindow).GetField("exiting",Private).SetValue(app,true); app.Close();
            }
        };
        try {
            Check(Program.IsRelease && Program.IsTest && Program.AllowTestHotkey && !Program.NoHotkey,"Requires release test mode with explicit --allow-test-hotkey and without --no-hotkey");
            results.Add("INFO: actual test trigger "+trigger+" (virtual key "+(int)trigger+"); all other hotkeys are left untouched.");
            Files.Save(folder,new Settings { TriggerKey=(int)trigger,TriggerModifiers=0,SpeechPreload=false,SpeechProvider="windows",ReadingTopMost=false });
            Files.SaveKey(folder,"isolated-release-hotkey-test-key");
            app=new MainWindow(folder,true,delegate {
                var handler=new Handler(Reply("Isolated hotkey response "+(handlers.Count+1))); handlers.Add(handler); return handler;
            });
            // Registration failure is caught here before Application.Run. A real
            // running copy is never stopped to free the test key.
            app.PrepareHotkey();
            app.Shown+=async delegate {
                try {
                    await Task.Delay(120);
                    var host=Field<NativeWindow>(app,"releaseHotkeyWindow");
                    Check(host!=null && host.Handle!=IntPtr.Zero && Field<int>(app,"releaseRegisteredId")!=0,trigger+" is registered on the dedicated message-only host");
                    IntPtr stableHost=host.Handle;
                    fixture=Process.Start(new ProcessStartInfo(Application.ExecutablePath,"--selection-fixture") { UseShellExecute=false,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=root });
                    await ReleaseHotkeyWait(delegate { fixture.Refresh(); return fixture.HasExited || fixture.MainWindowHandle!=IntPtr.Zero; },8000,"Selection fixture did not create its window");
                    Check(!fixture.HasExited,"Selection fixture child is running");
                    await ReleaseHotkeyTrigger(app,fixture,handlers,1,"Initial registration",trigger);
                    IntPtr oldSettingsHandle=app.Handle;
                    typeof(Control).GetMethod("RecreateHandle",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(app,null);
                    app.Hide(); await Task.Delay(100);
                    Check(Field<NativeWindow>(app,"releaseHotkeyWindow").Handle==stableHost,"Recreating the settings HWND leaves the hotkey HWND unchanged");
                    results.Add("INFO: settings HWND "+oldSettingsHandle+" -> "+app.Handle+"; dedicated hotkey HWND "+stableHost);
                    await ReleaseHotkeyTrigger(app,fixture,handlers,2,"After settings handle recreation",trigger);
                    ReleaseHotkeyInvoke(app,"SuspendReleaseHotkey");
                    Check(Field<int>(app,"releaseRegisteredId")==0,"Suspension clears the registered shortcut identity");
                    conflict=new ReleaseHotkeyTestHost();
                    conflictRegistered=Native.RegisterHotKey(conflict.Handle,conflictId,0x4000,(uint)trigger);
                    Check(conflictRegistered,"Suspension actually releases "+trigger+" at the OS level");
                    Native.UnregisterHotKey(conflict.Handle,conflictId); conflictRegistered=false;
                    ReleaseHotkeyInvoke(app,"ResumeReleaseHotkey");
                    Check(Field<int>(app,"releaseRegisteredId")!=0,"Resume registers the configured shortcut again");
                    int heldKey=0;
                    foreach(var key in new[]{Keys.F22,Keys.F23,Keys.F24}) {
                        if(Native.RegisterHotKey(conflict.Handle,conflictId,0x4003,(uint)key)) { heldKey=(int)key; conflictRegistered=true; break; }
                    }
                    Check(conflictRegistered,"An isolated Ctrl+Alt function-key combination is available for the conflict test");
                    int oldId=Field<int>(app,"releaseRegisteredId"); bool rejected=false;
                    try { app.ApplyReleaseHotkey(new Settings { TriggerKey=heldKey,TriggerModifiers=3 }); }
                    catch(InvalidOperationException) { rejected=true; }
                    Check(rejected && Field<int>(app,"releaseRegisteredId")==oldId && Field<int>(app,"releaseRegisteredKey")== (int)trigger,"A conflicting replacement is rejected while the old "+trigger+" registration remains");
                    Native.UnregisterHotKey(conflict.Handle,conflictId); conflictRegistered=false;
                    await ReleaseHotkeyTrigger(app,fixture,handlers,3,"After conflict rollback",trigger);
                    app.ApplyReleaseHotkey(new Settings { TriggerKey=heldKey,TriggerModifiers=3 });
                    Check(Field<int>(app,"releaseRegisteredKey")==heldKey && Field<int>(app,"releaseRegisteredModifiers")==3,"A free Ctrl+Alt+"+(Keys)heldKey+" combination registers successfully");
                    await ReleaseHotkeyTrigger(app,fixture,handlers,4,"Successful Ctrl+Alt combination",(Keys)heldKey,3);
                    app.ApplyReleaseHotkey(new Settings { TriggerKey=(int)trigger,TriggerModifiers=0 });
                    await ReleaseHotkeyTrigger(app,fixture,handlers,5,"Held "+trigger+" repeat suppression",trigger,0,true);
                    Check(handlers.Count==5,"Three normal "+trigger+" events, one Ctrl+Alt combination and one held "+trigger+" created exactly five mocked requests");
                    results.Add("All API responses used in-memory handlers; test key/profile/history are isolated. No personal key or running instance was read, stopped or modified.");
                } catch(Exception e) { results.Add("FAIL: "+e); exit=1; }
                finally { cleanup(); File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); }
            };
            Application.Run(app);
        } catch(Exception e) { results.Add("FAIL: "+e); exit=1; }
        finally { cleanup(); if(app!=null) app.Dispose(); File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); }
        return exit;
    }
}
}
