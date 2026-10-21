using System;
using System.IO;
using System.Windows.Forms;

namespace TermLens {
public static partial class Tests {
    public static int ReleaseMouseShortcut(string root) {
        string folder=TestRoot(root,"release-mouse-shortcut");
        try {
            int count=0; IntPtr captured=IntPtr.Zero;
            Action<IntPtr> callback=delegate(IntPtr value) { count++; captured=value; };
            var first=new IntPtr(123); var second=new IntPtr(456);
            using(var unbound=new TermLens.ReleaseMouseShortcut(0,0,callback)) {
                Check(!unbound.HookInstalled && !unbound.ProcessButton(5,true,0,false,first),"Unbound mouse shortcut installs no hook and passes input through");
            }
            foreach(int key in new[]{(int)Keys.XButton1,(int)Keys.XButton2}) {
                foreach(int modifiers in new[]{0,1,2,4,8,15}) {
                    using(var shortcut=new TermLens.ReleaseMouseShortcut(key,modifiers,callback,false)) {
                        int before=count;
                        Check(!shortcut.ProcessButton(key==5?6:5,true,modifiers,false,first),"Other side button passes through: "+key+"/"+modifiers);
                        Check(!shortcut.ProcessButton(key,true,modifiers^1,false,first),"Modifiers must match exactly: "+key+"/"+modifiers);
                        Check(!shortcut.ProcessButton(key,false,modifiers,false,first),"Unmatched release passes through");
                        Check(!shortcut.ProcessButton(key,true,modifiers,true,first),"Injected down passes through");
                        Check(shortcut.ProcessButton(key,true,modifiers,false,first) && count==before,"Matching down is swallowed without dispatch");
                        Check(shortcut.ProcessButton(key,true,modifiers^1,false,second) && count==before,"Repeated down is swallowed without changing source or dispatch");
                        Check(!shortcut.ProcessButton(key,false,modifiers,true,second) && count==before,"Injected up cannot complete the physical press");
                        Check(shortcut.ProcessButton(key,false,modifiers^1,false,second) && count==before+1 && captured==first,"Release stays swallowed after modifiers change and dispatches original foreground");
                        Check(!shortcut.ProcessButton(key,false,modifiers,false,second) && count==before+1,"Repeated release passes through without dispatch");
                    }
                }
            }
            var disposed=new TermLens.ReleaseMouseShortcut(5,0,callback,false);
            disposed.ProcessButton(5,true,0,false,first); disposed.Dispose(); disposed.Dispose();
            Check(!disposed.ProcessButton(5,false,0,false,second),"Disposal cancels pending dispatch and is idempotent");
            using(var throws=new TermLens.ReleaseMouseShortcut(5,0,delegate(IntPtr value) { throw new InvalidOperationException("test callback"); },false)) {
                Check(throws.ProcessButton(5,true,0,false,first) && throws.ProcessButton(5,false,0,false,first),"Callback errors do not escape or leak the consumed release");
            }
            // Install and remove the real hook only; never generate mouse/keyboard input.
            var native=new TermLens.ReleaseMouseShortcut(5,15,delegate(IntPtr value) { });
            try { Check(native.HookInstalled,"Native mouse hook installs successfully"); }
            finally { native.Dispose(); }
            Check(!native.HookInstalled,"Native mouse hook unhooks successfully");
            if(Program.AllowTestHotkey) {
                Files.Save(folder,new Settings { TriggerKey=5,TriggerModifiers=15,SpeechPreload=false });
                using(var app=new MainWindow(folder,true)) {
                    IntPtr window=app.Handle; app.ApplyReleaseHotkey(new Settings { TriggerKey=5,TriggerModifiers=15 });
                    var initial=Field<TermLens.ReleaseMouseShortcut>(app,"releaseMouseShortcut");
                    Check(initial!=null && initial.HookInstalled,"Application registers the configured side button");
                    app.ApplyReleaseHotkey(new Settings { TriggerKey=6,TriggerModifiers=15 });
                    Check(!initial.HookInstalled && Field<TermLens.ReleaseMouseShortcut>(app,"releaseMouseShortcut").HookInstalled,"Changing side buttons replaces the previous hook");
                    app.SuspendReleaseHotkey();
                    Check(Field<TermLens.ReleaseMouseShortcut>(app,"releaseMouseShortcut")==null && Field<int>(app,"releaseRegisteredId")==0,"Recording suspends the mouse binding completely");
                    app.ResumeReleaseHotkey();
                    Check(Field<int>(app,"releaseRegisteredKey")==5 && Field<TermLens.ReleaseMouseShortcut>(app,"releaseMouseShortcut").HookInstalled,"Canceling recording restores the saved mouse binding");
                    var restored=Field<TermLens.ReleaseMouseShortcut>(app,"releaseMouseShortcut");
                    app.ApplyReleaseHotkey(new Settings { TriggerKey=(int)Keys.F24,TriggerModifiers=15 });
                    Check(!restored.HookInstalled && Field<TermLens.ReleaseMouseShortcut>(app,"releaseMouseShortcut")==null,"Switching back to keyboard removes the mouse hook");
                    app.ApplyReleaseHotkey(new Settings { TriggerKey=6,TriggerModifiers=15 });
                    app.SuspendReleaseHotkey();
                    Check(Field<int>(app,"releaseRegisteredId")==0,"Mouse and keyboard transitions clean up both registrations");
                }
            }
            File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); return 0;
        } catch(Exception e) { results.Add(e.ToString()); File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); return 1; }
    }
}
}
