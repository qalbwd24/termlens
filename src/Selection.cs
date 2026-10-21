using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Forms;

namespace TermLens {
public static class Native {
    public delegate IntPtr HookProc(int code, IntPtr wp, IntPtr lp);
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct MouseInfo { public Point Point; public uint Data, Flags, Time; public IntPtr Extra; }
    [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wp, IntPtr lp);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string className,string title);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd,uint message,IntPtr wp,IntPtr lp);
    public static uint ProcessOf(IntPtr hwnd) { uint pid; GetWindowThreadProcessId(hwnd,out pid); return pid; }
    public static bool ModifiersDown() { return Down(0x11) || Down(0x12) || Down(0x10) || Down(0x5b) || Down(0x5c); }
    public static bool Down(int key) { return (GetAsyncKeyState(key) & 0x8000) != 0; }
    public static void Copy() { keybd_event(0x11,0,0,UIntPtr.Zero); keybd_event(0x43,0,0,UIntPtr.Zero); keybd_event(0x43,0,2,UIntPtr.Zero); keybd_event(0x11,0,2,UIntPtr.Zero); }
}
public sealed class CaptureResult {
    public string Text, Method;
    public bool Editable, CodeEditor;
    public CaptureResult(string text, string method) { Text=text; Method=method; }
}
public sealed class NoSelectionException : Exception {
    // A failed clipboard probe cannot prove that the user selected nothing.
    public bool ConfirmedEmpty { get; private set; }
    public NoSelectionException(bool confirmedEmpty=false):base("No selected text was found.") { ConfirmedEmpty=confirmedEmpty; }
}
public static class Selection {
    static Task<string> activeProbe;
    static Task<Rectangle?> boundsProbe;
    static Task<bool> editProbe;
    public static bool IsCodeEditor(IntPtr source) {
        try { using(var p=Process.GetProcessById((int)Native.ProcessOf(source))) return p.ProcessName.Equals("Code",StringComparison.OrdinalIgnoreCase) || p.ProcessName.StartsWith("Code -",StringComparison.OrdinalIgnoreCase) || p.ProcessName.Equals("VSCodium",StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }
    public static async Task<bool> IsEditable(IntPtr source) {
        if(IsCodeEditor(source)) return false;
        if(editProbe!=null && !editProbe.IsCompleted) return false;
        uint pid=Native.ProcessOf(source);
        editProbe=Task.Run(delegate {
            try {
                var el=AutomationElement.FocusedElement;
                if(el==null || (uint)el.Current.ProcessId!=pid || el.Current.IsPassword) return false;
                object pattern;
                if(el.TryGetCurrentPattern(ValuePattern.Pattern,out pattern)) return !((ValuePattern)pattern).Current.IsReadOnly;
                if(el.TryGetCurrentPattern(TextPattern.Pattern,out pattern)) {
                    var readOnly=((TextPattern)pattern).DocumentRange.GetAttributeValue(TextPattern.IsReadOnlyAttribute);
                    return readOnly is bool && !(bool)readOnly;
                }
            } catch { }
            return false;
        });
        var probe=editProbe;
        return await Task.WhenAny(probe,Task.Delay(200))==probe && await probe;
    }
    public const string Password = "\0PASSWORD";
    public static async Task<Rectangle?> AccessibleBounds(IntPtr source,int timeoutMs=300) {
        if(boundsProbe!=null && !boundsProbe.IsCompleted) return null;
        uint pid=Native.ProcessOf(source);
        boundsProbe=Task.Run(delegate {
            try {
                var el=AutomationElement.FocusedElement;
                if(el==null || (uint)el.Current.ProcessId!=pid || el.Current.IsPassword) return (Rectangle?)null;
                for(int depth=0;depth<4 && el!=null;depth++) {
                    object pattern;
                    if(el.TryGetCurrentPattern(TextPattern.Pattern,out pattern)) {
                        Rectangle combined=Rectangle.Empty;
                        foreach(var range in ((TextPattern)pattern).GetSelection()) {
                            if(range.CompareEndpoints(System.Windows.Automation.Text.TextPatternRangeEndpoint.Start,range,System.Windows.Automation.Text.TextPatternRangeEndpoint.End)==0) continue;
                            foreach(var r in range.GetBoundingRectangles()) {
                                if(r.IsEmpty || r.Width<=0 || r.Height<=0 || Double.IsInfinity(r.X) || Double.IsNaN(r.X)) continue;
                                var rect=Rectangle.FromLTRB((int)Math.Floor(r.Left),(int)Math.Floor(r.Top),(int)Math.Ceiling(r.Right),(int)Math.Ceiling(r.Bottom));
                                combined=combined.IsEmpty?rect:Rectangle.Union(combined,rect);
                            }
                        }
                        if(!combined.IsEmpty) return (Rectangle?)combined;
                    }
                    el=TreeWalker.ControlViewWalker.GetParent(el); if(el!=null && (uint)el.Current.ProcessId!=pid) break;
                }
            } catch { }
            return (Rectangle?)null;
        });
        var probe=boundsProbe;
        if(await Task.WhenAny(probe,Task.Delay(timeoutMs))==probe) return await probe;
        return null;
    }
    public static Rectangle ApproximateBounds(Point start,Point end) {
        return Rectangle.FromLTRB(Math.Min(start.X,end.X),Math.Min(start.Y,end.Y)-Look.S(12),Math.Max(start.X,end.X)+1,Math.Max(start.Y,end.Y)+Look.S(18));
    }
    public static async Task<string> AccessibleSelection(IntPtr source) {
        // Do not create unbounded workers if a third-party accessibility provider hangs.
        if (activeProbe != null && !activeProbe.IsCompleted) return null;
        uint pid = Native.ProcessOf(source);
        activeProbe = Task.Run(delegate {
            try {
                var el = AutomationElement.FocusedElement;
                if (el == null || (uint)el.Current.ProcessId != pid) return null;
                if (el.Current.IsPassword) return Password;
                for (int depth = 0; depth < 4 && el != null; depth++) {
                    object pattern;
                    if (el.TryGetCurrentPattern(TextPattern.Pattern,out pattern)) {
                        var ranges = ((TextPattern)pattern).GetSelection();
                        if (ranges != null && ranges.Length > 0) {
                            string text = ranges[0].GetText(12001);
                            if (!String.IsNullOrWhiteSpace(text)) return text;
                        }
                        // A real empty selection is different from unavailable accessibility.
                        // Do not send Ctrl+C here: editors may copy the whole current line.
                        return "";
                    }
                    el = TreeWalker.ControlViewWalker.GetParent(el);
                    if (el != null && (uint)el.Current.ProcessId != pid) break;
                }
            } catch { }
            return null;
        });
        if (await Task.WhenAny(activeProbe, Task.Delay(350)) == activeProbe) return await activeProbe;
        return null;
    }
    public static async Task<CaptureResult> Capture(IntPtr source) {
        if (source == IntPtr.Zero || Native.GetForegroundWindow() != source) throw new Exception("The source window changed. Select the text again, then press F13.");
        bool code=IsCodeEditor(source);
        var editTask=IsEditable(source);
        string accessible = await AccessibleSelection(source);
        if (Native.GetForegroundWindow()!=source) throw new Exception("The source window changed during capture. Select the text again.");
        if (accessible == Password) throw new Exception("Password fields are excluded.");
        // VS Code can expose an empty accessibility range for a real code selection.
        // Its clipboard metadata distinguishes this from copying an unselected line.
        if (accessible == "" && !code) throw new NoSelectionException(true);
        var result=!String.IsNullOrWhiteSpace(accessible)?Checked(accessible,"Windows selection"):await CopySelection(source,code);
        result.CodeEditor=code; result.Editable=await editTask;
        return result;
    }
    public static CaptureResult Checked(string text, string method) {
        if (String.IsNullOrWhiteSpace(text)) throw new NoSelectionException();
        if (text.Length > 12000) throw new Exception("Select less than 12,000 characters for a lookup.");
        return new CaptureResult(text.Trim(),method);
    }
    static DataObject Snapshot() {
        var original = Clipboard.GetDataObject();
        var copy = new DataObject();
        if (original == null) return copy;
        foreach (string format in original.GetFormats(false)) {
            object value = original.GetData(format,false);
            if (value == null) throw new Exception("The clipboard contains data that cannot be safely copied. Use Ctrl+C followed by Ctrl+Alt+V instead.");
            copy.SetData(format,false,value);
        }
        return copy;
    }
    public static async Task<CaptureResult> CopySelection(IntPtr source,bool requireEditorMetadata=false) {
        for (int i=0; i<30 && Native.ModifiersDown(); i++) await Task.Delay(25);
        if (Native.ModifiersDown()) throw new Exception("Release Ctrl, Alt, Shift and Windows keys, then try again.");
        if (Native.GetForegroundWindow()!=source) throw new Exception("The source window changed. Select the text again.");
        DataObject original;
        uint before = Native.GetClipboardSequenceNumber();
        try { original = Snapshot(); }
        catch { throw new Exception("Clipboard is busy or cannot be preserved. Copy the text yourself, then press Ctrl+Alt+V."); }
        if (before != Native.GetClipboardSequenceNumber()) throw new Exception("Clipboard changed. Try again.");
        uint copied=before; string text=null; bool restored=true,confirmedEmpty=false; Exception captureError=null;
        try {
            Native.Copy();
            for(int i=0;i<25;i++) {
                await Task.Delay(35);
                copied=Native.GetClipboardSequenceNumber();
                if(copied!=before) {
                    try {
                        if(Clipboard.ContainsText()) text=Clipboard.GetText();
                        var editorData=Clipboard.GetData("vscode-editor-data") as string;
                        if(editorData==null) {
                            var webData=Clipboard.GetData("Chromium Web Custom MIME Data Format") as System.IO.MemoryStream;
                            if(webData!=null) {
                                string decoded=System.Text.Encoding.Unicode.GetString(webData.ToArray()); int index=decoded.IndexOf("vscode-editor-data",StringComparison.Ordinal);
                                if(index>=0) editorData=decoded.Substring(index,Math.Min(2048,decoded.Length-index));
                            }
                        }
                        if(editorData!=null && System.Text.RegularExpressions.Regex.IsMatch(editorData,"\"isFromEmptySelection\"\\s*:\\s*true")) { text=null; confirmedEmpty=true; }
                        if(requireEditorMetadata && editorData==null) text=null;
                    } catch { continue; }
                    break;
                }
                if(Native.GetForegroundWindow()!=source) break;
            }
        } catch(Exception e) { captureError=e; }
        {
            if(copied!=before && Native.GetClipboardSequenceNumber()==copied) {
                restored=false;
                for(int i=0;i<4;i++) {
                    if(Native.GetClipboardSequenceNumber()!=copied) { restored=true; break; }
                    try { Clipboard.SetDataObject(original,true); restored=true; break; } catch { }
                    await Task.Delay(40);
                }
            }
        }
        if(captureError!=null) throw captureError;
        if(!restored) throw new Exception("Text was copied but Windows blocked clipboard restoration. Your selected text is on the clipboard; use Ctrl+Alt+V to continue.");
        if(Native.GetForegroundWindow()!=source) throw new Exception("The source window changed during capture. Select the text again.");
        if(confirmedEmpty) throw new NoSelectionException(true);
        // Apps without TextPattern often do nothing on Ctrl+C when no text is
        // selected. A quiet probe with unchanged source and clipboard is the
        // normal no-selection branch; never reuse the pre-existing clipboard.
        // Updated non-text payloads or missing editor metadata remain failures.
        if(copied==before && Native.GetClipboardSequenceNumber()==before && Native.GetForegroundWindow()==source) throw new NoSelectionException(true);
        return Checked(text,"Copied selection; clipboard restored");
    }
}
public enum SelectionAction { Explain, Ask, Refine }
public sealed class SelectionBubble : Form {
    public IntPtr Source;
    public Rectangle SelectionBounds;
    public event Action<IntPtr> Requested;
    public event Action<IntPtr,SelectionAction> ActionRequested;
    readonly Timer life = new Timer();
    readonly ToolTip tips=new ToolTip { InitialDelay=350,ReshowDelay=100 };
    int hovered=-1;
    public Rectangle ActionBounds(int index) { return new Rectangle(Look.S(5+index*88),Look.S(5),Look.S(84),Look.S(32)); }
    int Hit(Point point) { for(int i=0;i<3;i++) if(ActionBounds(i).Contains(point)) return i; return -1; }
    public SelectionBubble() {
        FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false; TopMost=true; StartPosition=FormStartPosition.Manual;
        AutoScaleMode=AutoScaleMode.None; Size=new Size(Look.S(270),Look.S(42)); BackColor=Look.Surface; DoubleBuffered=true;
        Cursor=Cursors.Hand; Font=new Font("Segoe UI",9,FontStyle.Regular); AccessibleName="Selection actions: Explain, Ask teacher, Refine wording";
        MouseUp+=delegate(object sender,MouseEventArgs e) {
            int hit=Hit(e.Location); if(e.Button!=MouseButtons.Left || hit<0) return;
            UiTrace.Write("selection action="+(SelectionAction)hit); var src=Source; Hide(); life.Stop();
            if(ActionRequested!=null) ActionRequested(src,(SelectionAction)hit);
            if(hit==0 && Requested!=null) Requested(src);
        };
        MouseEnter+=delegate { life.Stop(); };
        MouseMove+=delegate(object sender,MouseEventArgs e) {
            int next=Hit(e.Location); if(hovered==next) return; hovered=next; Invalidate();
            tips.SetToolTip(this,next==0?"Explain the selected text":next==1?"Ask your English teacher about the selected text":next==2?"Improve the selected wording":"");
        };
        MouseLeave+=delegate { hovered=-1; life.Start(); Invalidate(); };
        life.Interval=9000; life.Tick+=delegate { Hide(); life.Stop(); };
        Look.Shape(this,12);
    }
    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.Clear(Look.Surface); e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using(var outline=Look.Round(new RectangleF(0,0,Width-1,Height-1),Look.S(12))) using(var pen=new Pen(Look.Border)) e.Graphics.DrawPath(pen,outline);
        string[] labels={"Explain","Ask","Refine"};
        for(int i=0;i<3;i++) {
            var r=ActionBounds(i);
            Color fill=i==0?(hovered==i?Color.FromArgb(22,128,111):Color.FromArgb(25,112,100)):(hovered==i?Look.Field:Look.Surface);
            Color ink=i==0?Color.White:Look.Ink;
            using(var path=Look.Round(r,Look.S(8))) using(var brush=new SolidBrush(fill)) e.Graphics.FillPath(brush,path);
            int x=r.Left+Look.S(10),y=r.Top+Look.S(10),w=Look.S(12),h=Look.S(12);
            using(var pen=new Pen(i==0?Color.White:Look.Accent,Math.Max(1.2f,Look.Scale*1.3f))) {
                if(i==0) { e.Graphics.DrawRectangle(pen,x,y,w,h); e.Graphics.DrawLine(pen,x+w/2,y,x+w/2,y+h); }
                else if(i==1) { using(var path=Look.Round(new Rectangle(x,y,w,h-Look.S(3)),Look.S(2))) e.Graphics.DrawPath(pen,path); e.Graphics.DrawLine(pen,x+Look.S(3),y+h-Look.S(3),x+Look.S(1),y+h); }
                else { e.Graphics.DrawLine(pen,x,y+h,x+w,y); e.Graphics.DrawLine(pen,x+Look.S(3),y+h,x+w+Look.S(3),y); e.Graphics.DrawLine(pen,x,y+h,x+Look.S(3),y+h); }
            }
            TextRenderer.DrawText(e.Graphics,labels[i],Font,new Rectangle(r.Left+Look.S(29),r.Top,r.Width-Look.S(32),r.Height),ink,TextFormatFlags.VerticalCenter|TextFormatFlags.HorizontalCenter|TextFormatFlags.NoPadding);
        }
    }
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams { get { var p=base.CreateParams; p.ExStyle|=0x08000000|0x80; p.ClassStyle|=0x20000; return p; } }
    protected override void WndProc(ref Message m) { if(m.Msg==0x21) { m.Result=new IntPtr(3); return; } base.WndProc(ref m); }
    public void Present(IntPtr source, Point point) {
        Present(source,Selection.ApproximateBounds(point,point));
    }
    public void Present(IntPtr source,Rectangle selected) {
        Source=source; SelectionBounds=selected; var area=Screen.FromRectangle(selected).WorkingArea;
        var position=Look.Below(Size,selected,area,Look.S(8),false);
        if(!position.HasValue) { Hide(); return; }
        Location=position.Value; hovered=-1; BackColor=Look.Surface; Invalidate();
        Show(); life.Stop(); life.Start();
    }
    protected override void Dispose(bool disposing) { if(disposing) { life.Dispose(); tips.Dispose(); } base.Dispose(disposing); }
}
public sealed class MouseSelection : IDisposable {
    public event Action<Point,long> PointerPressed;
    readonly Control dispatch;
    readonly SelectionBubble bubble;
    readonly Func<bool> enabled;
    readonly Native.HookProc callback;
    IntPtr hook, source;
    Point down, previousDown;
    bool downSet, doubleClick;
    long previousTime;
    int generation;
    readonly uint own=(uint)Process.GetCurrentProcess().Id;
    public bool Installed { get { return hook!=IntPtr.Zero; } }
    public MouseSelection(Control dispatch,SelectionBubble bubble,Func<bool> enabled) {
        this.dispatch=dispatch; this.bubble=bubble; this.enabled=enabled;
        callback=Hook; hook=Native.SetWindowsHookEx(14,callback,Native.GetModuleHandle(null),0);
    }
    IntPtr Hook(int code,IntPtr wp,IntPtr lp) {
        try {
            if(code>=0 && (wp.ToInt32()==0x201 || wp.ToInt32()==0x202)) {
                var data=(Native.MouseInfo)Marshal.PtrToStructure(lp,typeof(Native.MouseInfo));
                var pt=new Point(data.Point.X,data.Point.Y);
                IntPtr target=Native.GetAncestor(Native.WindowFromPoint(data.Point),2);
                if(wp.ToInt32()==0x201) {
                    long occurredAt=Stopwatch.GetTimestamp();
                    dispatch.BeginInvoke((Action)delegate { if(PointerPressed!=null) PointerPressed(pt,occurredAt); });
                    if(Native.ProcessOf(target)==own) { downSet=false; return Native.CallNextHookEx(hook,code,wp,lp); }
                    generation++; int g=generation;
                    dispatch.BeginInvoke((Action)delegate { if(g==generation) bubble.Hide(); });
                    if(!enabled() || Native.ModifiersDown()) { downSet=false; return Native.CallNextHookEx(hook,code,wp,lp); }
                    long now=Environment.TickCount & 0xffffffffL;
                    doubleClick=(now-previousTime>=0 && now-previousTime<Native.GetDoubleClickTime() && Math.Abs(pt.X-previousDown.X)<5 && Math.Abs(pt.Y-previousDown.Y)<5);
                    previousDown=pt; previousTime=now; down=pt; source=target; downSet=true;
                } else if(downSet) {
                    downSet=false;
                    if(!enabled() || source!=target || (Math.Abs(pt.X-down.X)<7 && Math.Abs(pt.Y-down.Y)<7 && !doubleClick)) return Native.CallNextHookEx(hook,code,wp,lp);
                    var selectedSource=source; var start=down; int g=generation;
                    dispatch.BeginInvoke((Action)(async delegate {
                        await Task.Delay(60);
                        if(dispatch.IsDisposed || g!=generation || !enabled() || Native.GetForegroundWindow()!=selectedSource) return;
                        var bounds=await Selection.AccessibleBounds(selectedSource,90);
                        if(!dispatch.IsDisposed && g==generation && enabled() && Native.GetForegroundWindow()==selectedSource) bubble.Present(selectedSource,bounds??Selection.ApproximateBounds(start,pt));
                    }));
                }
            }
        } catch { /* The mouse must always remain usable. */ }
        return Native.CallNextHookEx(hook,code,wp,lp);
    }
    public void Dispose() { generation++; if(hook!=IntPtr.Zero) { Native.UnhookWindowsHookEx(hook); hook=IntPtr.Zero; } }
}
}
