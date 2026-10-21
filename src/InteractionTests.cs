using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TermLens {
public static partial class Tests {
    static void ExternalForeground(IntPtr handle) {
        uint ignored,current=GetCurrentThreadId(),previous=Native.GetWindowThreadProcessId(Native.GetForegroundWindow(),out ignored);
        bool attached=previous!=0 && previous!=current && AttachThreadInput(current,previous,true);
        try { ShowWindow(handle,9); Native.SetForegroundWindow(handle); }
        finally { if(attached) AttachThreadInput(current,previous,false); }
    }
    static async Task Interaction(MainWindow app,WorkspaceWindow window,string folder) {
        Process fixture=null;
        try {
            window.TopMost=false;
            fixture=Process.Start(new ProcessStartInfo(Application.ExecutablePath,"--focus-fixture") { UseShellExecute=false,WindowStyle=ProcessWindowStyle.Hidden });
            IntPtr source=IntPtr.Zero;
            for(int i=0;i<40;i++) { await Task.Delay(50); fixture.Refresh(); source=fixture.MainWindowHandle; if(source!=IntPtr.Zero) break; }
            Check(source!=IntPtr.Zero,"External window created for existing-window F13 checks");
            for(int attempt=0;attempt<2;attempt++) {
                if(attempt==1) window.WindowState=FormWindowState.Minimized;
                ExternalForeground(source); await Task.Delay(200);
                ExternalForeground(source);
                SendMessage(source,0x8001,IntPtr.Zero,IntPtr.Zero); await Task.Delay(80);
                var edit=FindWindowEx(source,IntPtr.Zero,null,null);
                Check(SendMessage(edit,0xB0,IntPtr.Zero,IntPtr.Zero).ToInt64()==0,"External fixture selection is empty before F13");
                Check(Native.GetForegroundWindow()==source,"A different process is foreground before F13");
                Native.keybd_event((byte)Keys.F13,0,0,UIntPtr.Zero); Native.keybd_event((byte)Keys.F13,0,2,UIntPtr.Zero);
                for(int i=0;i<70 && Native.GetForegroundWindow()!=window.Handle;i++) await Task.Delay(50);
                Check(Native.GetForegroundWindow()==window.Handle && window.WindowState!=FormWindowState.Minimized,"F13 restores and foregrounds the existing window: "+(attempt==0?"covered":"minimized"));
                Check(Object.ReferenceEquals(window,Field<WorkspaceWindow>(app,"workspace")) && window.Session.Entries.Count==3,"F13 reuses existing records without making an empty query");
            }
        } finally { if(fixture!=null) { if(!fixture.HasExited) { fixture.CloseMainWindow(); fixture.WaitForExit(3000); } fixture.Dispose(); } }
        Foreground(window); window.Timeline.AutoScrollPosition=Point.Empty;
        var original=window.Cards[0].Original; var answer=window.Cards[0].Answer;
        original.Focus(); original.Select(0,5); answer.Focus();
        Check(original.SelectionLength==0 && original.HideSelection,"Moving from source to answer removes the old blue selection");
        answer.Select(0,8); window.Composer.Focus();
        Check(answer.SelectionLength==0,"Focusing the composer clears reading selections");
        answer.Focus(); answer.Select(0,8); window.Timeline.Wheel(-120,answer.Font);
        Check(answer.SelectionLength==0 && !window.Quote.Visible,"Scrolling dismisses both the selection and Quote bubble");
        window.Timeline.AutoScrollPosition=Point.Empty; window.Timeline.SpeedPercent=100; window.Timeline.Wheel(-120,answer.Font); int normal=-window.Timeline.AutoScrollPosition.Y;
        window.Timeline.AutoScrollPosition=Point.Empty; window.Timeline.SpeedPercent=40; window.Timeline.Wheel(-120,answer.Font); int gentle=-window.Timeline.AutoScrollPosition.Y;
        Check(gentle>0 && Math.Abs(gentle-normal*.4)<=2,"Default wheel distance is 40 percent of the system distance");
        var speed=Field<NumericUpDown>(app,"scrollSpeed"); speed.Value=30;
        Check(Files.Load(folder).ScrollSpeed==30 && window.Timeline.SpeedPercent==30,"Scroll-speed changes apply immediately and persist"); speed.Value=40;
        var watch=Stopwatch.StartNew();
        for(int i=0;i<100;i++) { window.Timeline.Wheel(i%2==0?-15:15,answer.Font); window.Timeline.Update(); }
        results.Add("INFO: 100 small wheel updates on this machine: "+watch.ElapsedMilliseconds+" ms; this is not a physical smoothness rating.");

        // Reuse the real preload orchestrator with an isolated in-memory synthesizer.
        var fetched=new List<string>();
        var cache=new SpeechCache((t,v,r,c)=>{ fetched.Add(t); return Task.FromResult(new byte[64]); },null,128,16000,0);
        typeof(MainWindow).GetField("speechCache",Private).SetValue(app,cache);
        typeof(MainWindow).GetField("speechServiceProxy",Private).SetValue(app,"");
        typeof(MainWindow).GetField("speechServiceProvider",Private).SetValue(app,"microsoft");
        typeof(MainWindow).GetField("preloadGroup",Private).SetValue(app,"long-test");
        File.WriteAllText(Path.Combine(folder,"translator-signing.dpapi"),"isolated test marker",Files.Utf8);
        string longAnswer=String.Concat(Enumerable.Repeat("A right angle is the corner of a square. ",160));
        await WorkCall(app,"QueueSpeechPreload","long-test",longAnswer);
        var expectedParts=MainWindow.SpeechParts(longAnswer.Trim());
        Check(expectedParts.Count>=3 && expectedParts.All(p=>cache.Contains(p,Convert.ToString(Field<ComboBox>(app,"speechVoice").SelectedItem),-10)),"Complete long answers preload every chunk beyond the former 1200-character cutoff");
        fetched.Clear(); cache.Clear();
        typeof(MainWindow).GetMethod("PreloadSpeechAnswer",Private).Invoke(app,new object[]{"long-test",WorkMode.Ask,"A complete teacher answer."});
        for(int i=0;i<30 && !cache.Contains("A complete teacher answer.",Convert.ToString(Field<ComboBox>(app,"speechVoice").SelectedItem),-10);i++) await Task.Delay(10);
        Check(fetched.Count==1,"All-answer scope also preloads Ask answers");
        File.Delete(Path.Combine(folder,"translator-signing.dpapi")); cache.Dispose();
        typeof(MainWindow).GetField("speechCache",Private).SetValue(app,null);
        typeof(MainWindow).GetField("preloadGroup",Private).SetValue(app,"");
    }
}
}
