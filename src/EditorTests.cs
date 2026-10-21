using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TermLens {
public static partial class Tests {
    static void ForegroundHandle(IntPtr handle) {
        uint ignored; uint previous=Native.GetWindowThreadProcessId(Native.GetForegroundWindow(),out ignored),current=GetCurrentThreadId();
        bool attached=previous!=current && AttachThreadInput(current,previous,true);
        try { Native.SetForegroundWindow(handle); } finally { if(attached) AttachThreadInput(current,previous,false); }
    }
    public static int EditorWorkflow(string root) {
        string folder=TestRoot(root,"editor"); int exit=0; IntPtr editor=IntPtr.Zero,browser=IntPtr.Zero;
        Files.SaveKey(folder,"isolated-editor-test-key"); Files.Save(folder,new Settings { IncludeHistory=false });
        var app=new MainWindow(folder,true,delegate { return new Handler(Reply("A small test answer.")); });
        var fixture=new Form { Text="TermLens isolated editable-input fixture",Size=new Size(640,300),StartPosition=FormStartPosition.Manual,Location=new Point(150,150) };
        var edit=new TextBox { Multiline=true,Dock=DockStyle.Fill,Text="i want to search this word",Font=new Font("Segoe UI",14) }; fixture.Controls.Add(edit);
        app.Shown+=async delegate {
            System.Windows.Forms.IDataObject original=null;
            try {
                await Task.Delay(100); original=Clipboard.GetDataObject();
                fixture.Show(); Foreground(fixture); edit.Focus(); edit.SelectAll(); await Task.Delay(100);
                Check(Native.GetForegroundWindow()==fixture.Handle,"Editable fixture owns foreground before capture");
                var captured=await Selection.Capture(fixture.Handle);
                Check(captured.Text==edit.Text && captured.Editable && !captured.CodeEditor,"Editable external selection is classified as Polish");
                fixture.Hide();
                string profile=Path.Combine(folder,"vscode-profile"), user=Path.Combine(profile,"User"); Directory.CreateDirectory(user);
                File.WriteAllText(Path.Combine(user,"settings.json"),"{\"workbench.startupEditor\":\"none\",\"security.workspace.trust.enabled\":false,\"window.restoreWindows\":\"none\",\"telemetry.telemetryLevel\":\"off\",\"editor.accessibilitySupport\":\"off\",\"editor.copyWithSyntaxHighlighting\":false,\"editor.emptySelectionClipboard\":true}",Files.Utf8);
                string name="termlens-capture-"+DateTime.Now.ToString("HHmmss")+".py";
                string source=Path.Combine(folder,name); File.WriteAllText(source,"distance = 90\nprint(distance)\n",Files.Utf8);
                var start=new ProcessStartInfo(@"D:\Microsoft VS Code\Code.exe","--user-data-dir \""+profile+"\" --extensions-dir \""+Path.Combine(folder,"extensions")+"\" --disable-extensions --skip-welcome --skip-release-notes --new-window \""+source+"\"") { UseShellExecute=false,WindowStyle=ProcessWindowStyle.Hidden };
                using(var child=Process.Start(start)) { }
                for(int i=0;i<200 && editor==IntPtr.Zero;i++) {
                    foreach(var p in Process.GetProcessesByName("Code")) using(p) { if(p.MainWindowTitle.Contains(name)) { editor=p.MainWindowHandle; break; } }
                    if(editor==IntPtr.Zero) await Task.Delay(100);
                }
                Check(editor!=IntPtr.Zero,"Isolated real VS Code fixture opened");
                ForegroundHandle(editor); await Task.Delay(250);
                Check(Native.GetForegroundWindow()==editor,"VS Code fixture owns foreground before sending test keys");
                SendKeys.SendWait("^1"); SendKeys.SendWait("^{HOME}"); SendKeys.SendWait("+{END}"); await Task.Delay(200);
                Clipboard.SetText("TermLens clipboard preservation sentinel");
                captured=await Selection.Capture(editor);
                Check(captured.Text=="distance = 90" && captured.CodeEditor && !captured.Editable,"VS Code selected code captures exactly and routes to Explain");
                Check(Clipboard.GetText()=="TermLens clipboard preservation sentinel","Code capture preserves the clipboard");
                captured=await Selection.CopySelection(editor,true);
                Check(captured.Text=="distance = 90","VS Code clipboard fallback accepts a real selected code line");
                SendKeys.SendWait("{RIGHT}"); await Task.Delay(100);
                bool empty=false; try { await Selection.CopySelection(editor,true); } catch(NoSelectionException) { empty=true; }
                Check(empty,"VS Code whole-line copy with no selection is rejected");
                Check(Clipboard.GetText()=="TermLens clipboard preservation sentinel","Empty-selection fallback also restores the clipboard");
                await WorkCall(app,"CaptureWorkspace",editor);
                var window=Field<WorkspaceWindow>(app,"workspace");
                Check(window!=null && window.Session.Entries.Count==0 && window.Session.Mode==WorkMode.Explain,"F13 without a VS Code selection opens empty Explain input");
                ForegroundHandle(editor); await Task.Delay(100);
                Check(Native.GetForegroundWindow()==editor,"VS Code still owns the fixture window");
                SendKeys.SendWait("^{HOME}"); SendKeys.SendWait("+{END}");
                Native.keybd_event((byte)Keys.F13,0,0,UIntPtr.Zero); Native.keybd_event((byte)Keys.F13,0,2,UIntPtr.Zero);
                for(int i=0;i<100 && (window.Session.Entries.Count==0 || !window.Session.Entries[0].Finished);i++) await Task.Delay(50);
                Check(window.Session.Entries.Count==1 && window.Session.Entries[0].Text=="distance = 90" && window.Session.Entries[0].Mode==WorkMode.Explain,"Real global F13 translates selected VS Code code into the existing workspace");
                Check(Native.GetForegroundWindow()==window.Handle,"Global F13 brings the result window to the foreground");
                string browserTitle="TermLens browser fixture "+DateTime.Now.ToString("HHmmss");
                string html=Path.Combine(folder,"browser-fixture.html");
                File.WriteAllText(html,"<!doctype html><meta charset='utf-8'><title>"+browserTitle+"</title><style>body{font:24px Arial;padding:50px}input{font:24px Arial;width:500px}</style><p id='reading' tabindex='0'>perpendicular</p><input id='draft' value='how i search this word' onfocus='this.select()'><script>window.onload=function(){var p=document.getElementById('reading');p.focus();var r=document.createRange();r.selectNodeContents(p);var s=getSelection();s.removeAllRanges();s.addRange(r);};</script>",Files.Utf8);
                var edgeStart=new ProcessStartInfo(@"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe","--user-data-dir=\""+Path.Combine(folder,"edge-profile")+"\" --no-first-run --no-default-browser-check --app=\""+new Uri(html).AbsoluteUri+"\"") { UseShellExecute=false,WindowStyle=ProcessWindowStyle.Hidden };
                using(var edge=Process.Start(edgeStart)) { }
                for(int i=0;i<150 && browser==IntPtr.Zero;i++) {
                    foreach(var p in Process.GetProcessesByName("msedge")) using(p) { if(p.MainWindowTitle.Contains(browserTitle)) { browser=p.MainWindowHandle; break; } }
                    if(browser==IntPtr.Zero) await Task.Delay(100);
                }
                Check(browser!=IntPtr.Zero,"Isolated real Edge reading/input fixture opened");
                ForegroundHandle(browser); await Task.Delay(250);
                Check(Native.GetForegroundWindow()==browser,"Edge fixture owns foreground before capture");
                captured=await Selection.Capture(browser);
                Check(captured.Text=="perpendicular" && !captured.Editable,"Browser reading selection routes to Explain");
                SendKeys.SendWait("{TAB}"); await Task.Delay(150);
                captured=await Selection.Capture(browser);
                Check(captured.Text=="how i search this word" && captured.Editable,"Browser input selection routes to Polish");
            } catch(Exception e) { exit=1; results.Add(e.ToString()); }
            finally {
                if(editor!=IntPtr.Zero) Native.PostMessage(editor,0x10,IntPtr.Zero,IntPtr.Zero);
                if(browser!=IntPtr.Zero) Native.PostMessage(browser,0x10,IntPtr.Zero,IntPtr.Zero);
                fixture.Dispose();
                if(original!=null) try { Clipboard.SetDataObject(original,true); } catch { results.Add("Clipboard restoration unavailable after test"); }
                File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8);
                typeof(MainWindow).GetField("exiting",Private).SetValue(app,true); app.Close();
            }
        };
        Application.Run(app); return exit;
    }
    public static int DictionaryLive(string root) {
        string folder=TestRoot(root,"dictionary-live");
        try {
            var settings=Files.Load(Path.Combine(root,"data")); settings.Prompt=Settings.DefaultPrompt; settings.CarefulAnswers=false;
            string key=Files.LoadKey(Path.Combine(root,"data"));
            foreach(string term in new[]{"perpendicular","distance = 90\nprint(distance)"}) {
                var clock=Stopwatch.StartNew(); long first=-1;
                string answer=Requests.Complete(settings,key,term,new List<string>(),delegate(string content){if(first<0) first=clock.ElapsedMilliseconds;},CancellationToken.None,null).GetAwaiter().GetResult();
                string file=term=="perpendicular"?"perpendicular":"code";
                File.WriteAllText(Path.Combine(folder,file+".txt"),answer,Files.Utf8);
                Check(answer.Length>10 && !answer.StartsWith("Meaning:"),file+" follows the new dictionary prompt");
                results.Add(file+" first text "+first+"ms; completed "+clock.ElapsedMilliseconds+"ms");
            }
            settings.Prompt=settings.TeacherPrompt;
            string question="make sentence with this word";
            string quoted=MainWindow.WithQuotes(question,new List<WorkQuote>{new WorkQuote { Text="perpendicular · 垂直的",SourceText="perpendicular" }});
            string teacher=Requests.Complete(settings,key,quoted,new List<string>(),delegate(string content){},CancellationToken.None,null).GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(folder,"teacher.txt"),teacher,Files.Utf8);
            Check(teacher.Replace("**","").StartsWith("Corrected English:") && teacher.Contains("perpendicular"),"Teacher corrects the question and uses the quoted word");
            File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); return 0;
        } catch(Exception e) { File.WriteAllText(Path.Combine(folder,"results.txt"),"Live check failed: "+e.GetType().Name,Files.Utf8); return 1; }
    }
}
}
