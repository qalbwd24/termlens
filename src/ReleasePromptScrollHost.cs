using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TermLens {
// Retains the native multiline editor and its scrolling behavior. The viewport
// clips only its native scrollbar; scrolling never changes text or selection.
public sealed class ReleasePromptScrollHost : Panel {
    public readonly TextBox Editor;
    public readonly SlimScrollBar Scrollbar=new SlimScrollBar();
    readonly Panel viewport=new Panel();
    readonly Timer watch=new Timer { Interval=80 };
    bool syncing,laying,disposing;
    const int GetLineCount=0xBA,GetFirstVisibleLine=0xCE,LineScroll=0xB6,GetRect=0xB2;
    [StructLayout(LayoutKind.Sequential)] struct EditRect { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll",EntryPoint="SendMessageW")] static extern IntPtr EditMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll",EntryPoint="SendMessageW")] static extern IntPtr EditRectangle(IntPtr window,int message,IntPtr wParam,ref EditRect rectangle);

    public ReleasePromptScrollHost(TextBox editor) {
        if(editor==null) throw new ArgumentNullException("editor");
        Editor=editor; Dock=DockStyle.Fill; Margin=Padding.Empty;
        AutoScroll=false; viewport.AutoScroll=false; viewport.Margin=Padding.Empty;
        Editor.Multiline=true; Editor.ScrollBars=ScrollBars.Vertical;
        Editor.Dock=DockStyle.None; Editor.Anchor=AnchorStyles.Top|AnchorStyles.Left;
        Editor.BorderStyle=BorderStyle.None;
        Scrollbar.AccessibleName="Scroll prompt";
        viewport.Controls.Add(Editor); Controls.Add(viewport); Controls.Add(Scrollbar);
        Scrollbar.ValueChanged+=ScrollChanged;
        Editor.TextChanged+=EditorChanged; Editor.FontChanged+=EditorChanged;
        Editor.SizeChanged+=EditorChanged; Editor.HandleCreated+=EditorHandleCreated;
        Editor.HandleDestroyed+=EditorHandleDestroyed;
        Editor.KeyUp+=EditorKeyUp; Editor.MouseUp+=EditorMouseUp;
        Editor.MouseWheel+=EditorMouseWheel;
        watch.Tick+=WatchTick;
        LayoutEditor(); UpdateWatch();
    }
    int LineHeight { get { return Math.Max(1,Editor.Font.Height); } }
    void LayoutEditor() {
        if(laying || disposing || Editor==null || Editor.IsDisposed) return;
        laying=true;
        try {
            int bar=Math.Min(ClientSize.Width,Look.S(10));
            viewport.SetBounds(0,0,Math.Max(0,ClientSize.Width-bar),ClientSize.Height);
            Scrollbar.SetBounds(viewport.Right,0,bar,ClientSize.Height);
            // A vertical native scrollbar stays fully to the right of the
            // viewport, while the complete native text client remains visible.
            Editor.SetBounds(0,0,viewport.ClientSize.Width+SystemInformation.VerticalScrollBarWidth,viewport.ClientSize.Height);
            Scrollbar.BringToFront();
        } finally { laying=false; }
        SyncScroll();
    }
    public void SyncScroll() {
        if(syncing || laying || disposing || !Visible || !IsHandleCreated || Editor.IsDisposed || !Editor.IsHandleCreated) return;
        syncing=true;
        try {
            int height=LineHeight;
            int lines=Math.Max(1,EditMessage(Editor.Handle,GetLineCount,IntPtr.Zero,IntPtr.Zero).ToInt32());
            int first=Math.Max(0,EditMessage(Editor.Handle,GetFirstVisibleLine,IntPtr.Zero,IntPtr.Zero).ToInt32());
            var rectangle=new EditRect(); EditRectangle(Editor.Handle,GetRect,IntPtr.Zero,ref rectangle);
            int visiblePixels=Math.Max(1,rectangle.Bottom-rectangle.Top);
            int visibleLines=Math.Max(1,visiblePixels/height);
            Scrollbar.Viewport=visibleLines*height;
            Scrollbar.Maximum=Math.Max(first,Math.Max(0,lines-visibleLines))*height;
            Scrollbar.Value=first*height;
            Scrollbar.Visible=Scrollbar.Maximum>0;
        } finally { syncing=false; }
    }
    void ScrollChanged(object sender,EventArgs args) {
        if(syncing || disposing || !Editor.IsHandleCreated || Editor.IsDisposed) return;
        int first=EditMessage(Editor.Handle,GetFirstVisibleLine,IntPtr.Zero,IntPtr.Zero).ToInt32();
        int target=(int)Math.Round(Scrollbar.Value/(double)LineHeight);
        if(target!=first) EditMessage(Editor.Handle,LineScroll,IntPtr.Zero,new IntPtr(target-first));
        // Read back the native clamp. EM_LINESCROLL does not move the caret,
        // replace selected text, or reset undo history.
        SyncScroll();
    }
    void UpdateWatch() {
        if(disposing) return;
        watch.Enabled=Visible && IsHandleCreated && Editor!=null && Editor.IsHandleCreated && !Editor.IsDisposed;
        if(watch.Enabled) SyncScroll();
    }
    void WatchTick(object sender,EventArgs args) { SyncScroll(); }
    void EditorChanged(object sender,EventArgs args) { SyncScroll(); }
    void EditorHandleCreated(object sender,EventArgs args) { LayoutEditor(); UpdateWatch(); }
    void EditorHandleDestroyed(object sender,EventArgs args) { watch.Stop(); }
    void EditorKeyUp(object sender,KeyEventArgs args) { SyncScroll(); }
    void EditorMouseUp(object sender,MouseEventArgs args) { SyncScroll(); }
    void EditorMouseWheel(object sender,MouseEventArgs args) { SyncScroll(); }
    protected override void OnLayout(LayoutEventArgs args) { base.OnLayout(args); LayoutEditor(); }
    protected override void OnVisibleChanged(EventArgs args) { base.OnVisibleChanged(args); UpdateWatch(); }
    protected override void OnHandleCreated(EventArgs args) { base.OnHandleCreated(args); LayoutEditor(); UpdateWatch(); }
    protected override void OnHandleDestroyed(EventArgs args) { watch.Stop(); base.OnHandleDestroyed(args); }
    protected override void Dispose(bool disposingManaged) {
        if(disposingManaged && !disposing) {
            disposing=true; watch.Stop(); watch.Tick-=WatchTick; watch.Dispose();
            Scrollbar.ValueChanged-=ScrollChanged;
            Editor.TextChanged-=EditorChanged; Editor.FontChanged-=EditorChanged;
            Editor.SizeChanged-=EditorChanged; Editor.HandleCreated-=EditorHandleCreated;
            Editor.HandleDestroyed-=EditorHandleDestroyed;
            Editor.KeyUp-=EditorKeyUp; Editor.MouseUp-=EditorMouseUp; Editor.MouseWheel-=EditorMouseWheel;
        }
        base.Dispose(disposingManaged);
    }
}
}
