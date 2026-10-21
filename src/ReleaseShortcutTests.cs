using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TermLens {
public static partial class Tests {
    [DllImport("user32.dll",EntryPoint="SendMessageW")] static extern IntPtr ShortcutTestSendMessage(IntPtr handle,int message,IntPtr wParam,IntPtr lParam);
    public static int ReleaseShortcut(string root) {
        string folder=Path.Combine(root,"tests","release-shortcut-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6));
        Directory.CreateDirectory(folder); var report=new List<string>(); int exit=0;
        Action<bool,string> check=delegate(bool ok,string text) { if(!ok) throw new InvalidOperationException(text); report.Add("PASS: "+text); };
        using(var form=new Form { Text="TermLens shortcut capture test",ShowInTaskbar=false,StartPosition=FormStartPosition.CenterScreen,ClientSize=new Size(480,150) })
        using(var editor=new ReleaseShortcutInput { Bounds=new Rectangle(16,16,440,28) })
        using(var next=new Button { Text="Focus target",Bounds=new Rectangle(16,72,150,32) }) {
            form.Controls.Add(editor); form.Controls.Add(next);
            int starts=0,ends=0; editor.CaptureStarted+=delegate { starts++; }; editor.CaptureEnded+=delegate { ends++; };
            Action<Keys> command=delegate(Keys key) {
                Message message=Message.Create(editor.Handle,0x100,new IntPtr((int)(key&Keys.KeyCode)),IntPtr.Zero);
                if((key&(Keys.Control|Keys.Alt|Keys.Shift))==Keys.None) {
                    // This is WinForms' actual command-key preprocessing route.
                    check(editor.PreProcessMessage(ref message),"Recording consumes command key "+key);
                } else {
                    // Modifier state is explicit, avoiding any dependence on keys
                    // the user happens to be holding during this isolated test.
                    var method=typeof(ReleaseShortcutInput).GetMethod("ProcessCmdKey",BindingFlags.Instance|BindingFlags.NonPublic);
                    object[] arguments={message,key}; check((bool)method.Invoke(editor,arguments),"Recording consumes explicit combination "+key);
                }
            };
            Action<Keys> keyUp=delegate(Keys key) {
                // Address only our control. This never sends keyboard input to
                // the desktop, registers a hotkey, or targets another process.
                ShortcutTestSendMessage(editor.Handle,0x101,new IntPtr((int)key),IntPtr.Zero);
            };
            form.Shown+=delegate {
                try {
                    editor.SetShortcut((int)Keys.F13,0);
                    string error;
                    check(ReleaseShortcutInput.TryValidate((int)Keys.F13,0,out error),"F13 is accepted");
                    check(!ReleaseShortcutInput.TryValidate((int)Keys.F12,0,out error) && !ReleaseShortcutInput.TryValidate((int)Keys.F12,3,out error),"F12 is rejected with and without modifiers");
                    check(!ReleaseShortcutInput.TryValidate((int)Keys.A,0,out error) && !ReleaseShortcutInput.TryValidate((int)Keys.Return,0,out error) && !ReleaseShortcutInput.TryValidate((int)Keys.Back,0,out error),"Unmodified typing keys are rejected");
                    editor.BeginCapture(); command(Keys.ControlKey|Keys.Control);
                    check(editor.IsRecording && editor.KeyCode==(int)Keys.F13 && ends==0,"Modifier-only input does not commit or restore the hotkey early");
                    command(Keys.A|Keys.Control|Keys.Alt);
                    check(editor.IsRecording && editor.KeyCode==(int)Keys.F13 && ends==0,"Ctrl+Alt+A waits for the primary key release");
                    command(Keys.A|Keys.Control|Keys.Alt); command(Keys.B|Keys.Control|Keys.Alt);
                    keyUp(Keys.B);
                    check(editor.IsRecording && ends==0,"Repeats and a different key release do not replace or finish the candidate");
                    keyUp(Keys.A);
                    check(!editor.IsRecording && editor.KeyCode==(int)Keys.A && editor.Modifiers==3 && ends==1,"Targeted WM_KEYUP commits the recorded combination and restores once");
                    editor.BeginCapture(); command(Keys.F13); keyUp(Keys.F13);
                    check(!editor.IsRecording && editor.KeyCode==(int)Keys.F13 && ends==2,"F13 passes framework preprocessing and targeted key-up");
                    editor.SetShortcut((int)Keys.A,3); editor.BeginCapture(); command(Keys.F8); command(Keys.Escape);
                    check(!editor.IsRecording && editor.KeyCode==(int)Keys.A && editor.Modifiers==3 && ends==3,"Escape cancels a pending candidate and restores the prior shortcut");
                    editor.BeginCapture(); command(Keys.F9);
                    check(next.Focus(),"Test can move focus to its own second control");
                    check(!editor.IsRecording && editor.KeyCode==(int)Keys.A && ends==4,"Real control focus loss cancels capture and restores once");
                    editor.BeginCapture(); command(Keys.Tab|Keys.Control); keyUp(Keys.Tab);
                    check(!editor.IsRecording && editor.KeyCode==(int)Keys.Tab && editor.Modifiers==2,"Ctrl+Tab is captured without focus navigation");
                    editor.BeginCapture(); command(Keys.Return|Keys.Alt); keyUp(Keys.Return);
                    check(!editor.IsRecording && editor.KeyCode==(int)Keys.Return && editor.Modifiers==1,"Alt+Enter is captured without invoking a default button");
                    check(ReleaseShortcutInput.TryValidate((int)Keys.XButton1,0,out error) && ReleaseShortcutInput.TryValidate((int)Keys.XButton2,3,out error),"Both side buttons accept bare and modified bindings");
                    check(!ReleaseShortcutInput.TryValidate((int)Keys.LButton,0,out error),"Normal mouse clicks cannot replace the query shortcut");
                    editor.BeginCapture();
                    ShortcutTestSendMessage(editor.Handle,0x20B,new IntPtr(1<<16),IntPtr.Zero);
                    check(editor.IsRecording,"Mouse down waits for release");
                    ShortcutTestSendMessage(editor.Handle,0x20C,new IntPtr(2<<16),IntPtr.Zero);
                    check(editor.IsRecording,"Other side button release does not commit capture");
                    ShortcutTestSendMessage(editor.Handle,0x20C,new IntPtr(1<<16),IntPtr.Zero);
                    check(!editor.IsRecording && editor.KeyCode==(int)Keys.XButton1 && editor.Text=="鼠标后退键","Native side-button messages record the back button");
                    editor.BeginCapture();
                    Native.PostMessage(next.Handle,0x20B,new IntPtr(2<<16),IntPtr.Zero);
                    Native.PostMessage(next.Handle,0x20C,new IntPtr(2<<16),IntPtr.Zero);
                    Application.DoEvents();
                    check(!editor.IsRecording && editor.KeyCode==(int)Keys.XButton2,"Recording button accepts side keys even when the cursor stays over another control");
                    editor.BeginCapture(); editor.AcceptCandidate((int)Keys.XButton2,2); editor.CompleteKeyRelease((int)Keys.XButton2);
                    check(editor.Modifiers==2 && editor.Text=="Ctrl + 鼠标前进键","Modified side button is displayed clearly");
                    var saved=new Settings { TriggerKey=editor.KeyCode,TriggerModifiers=editor.Modifiers }; Files.Save(folder,saved);
                    var loaded=Files.Load(folder); check(loaded.TriggerKey==(int)Keys.XButton2 && loaded.TriggerModifiers==2,"Mouse shortcut survives settings save/load");
                    check(starts==ends,"Every capture start has exactly one restoration event");
                    report.Add("No global hotkey was registered; no external hook or desktop keyboard input was used.");
                } catch(Exception failure) { report.Add("FAIL: "+failure); exit=1; }
                finally { editor.CancelCapture(); form.Close(); }
            };
            Application.Run(form);
        }
        File.WriteAllLines(Path.Combine(folder,"results.txt"),report,Files.Utf8); return exit;
    }
}
}
