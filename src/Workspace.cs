using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace TermLens {
public enum WorkMode { Explain, Ask, Polish }
public sealed class WorkQuote {
    public string Text, SourceId, SourceText;
    public WorkQuote Copy() { return new WorkQuote { Text=Text,SourceId=SourceId,SourceText=SourceText }; }
}
public sealed class WorkEntry {
    public string Id=Guid.NewGuid().ToString("N"), Text="", Answer="", Error="";
    public WorkMode Mode;
    public bool Finished, Saved;
    public bool ImageOnly;
    public List<StudyImage> Images=new List<StudyImage>();
    public List<WorkQuote> Quotes=new List<WorkQuote>();
}
public sealed class WorkSession {
    public List<WorkEntry> Entries=new List<WorkEntry>();
    public List<WorkQuote> Quotes=new List<WorkQuote>();
    public string Draft="";
    public List<StudyImage> DraftImages=new List<StudyImage>();
    public WorkMode Mode=WorkMode.Explain;
    public int Scroll;
}
// A single, common text renderer avoids RichEdit choosing a different font after CJK.
public sealed class StudyText : RichTextBox {
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd,int msg,IntPtr wp,IntPtr lp);
    [StructLayout(LayoutKind.Sequential)] struct ScrollPoint { public int X,Y; }
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd,int msg,IntPtr wp,ref ScrollPoint lp);
    public event Action<int> ZoomRequested;
    public event Action WheelMoved;
    int naturalHeight;
    int measuredWidth=-1,measuredHeight;
    readonly Dictionary<float,Font> readingFonts=new Dictionary<float,Font>();
    string markdownSource,markdownSignature;
    MarkdownDocument markdownDocument;
    internal int MarkdownParseCount { get; private set; }
    float readingPoints=11;
    public bool MarkdownEnabled { get { return markdownSource!=null; } }
    internal int MarkdownRenderCount { get; private set; }
    public bool ChangingContent { get; private set; }
    public StudyText() {
        ReadOnly=true; BorderStyle=BorderStyle.None; DetectUrls=false; WordWrap=true;
        ScrollBars=RichTextBoxScrollBars.None; HideSelection=true; TabStop=true;
        readingFonts[11]=new Font("Microsoft YaHei UI",11); Font=readingFonts[11]; BackColor=Look.Surface; ForeColor=Look.Ink;
        ContentsResized+=delegate(object sender,ContentsResizedEventArgs e) { naturalHeight=e.NewRectangle.Height; };
        TextChanged+=delegate { measuredWidth=-1; };
        FontChanged+=delegate { measuredWidth=-1; };
        HandleCreated+=delegate { long opts=SendMessage(Handle,0x479,IntPtr.Zero,IntPtr.Zero).ToInt64(); SendMessage(Handle,0x478,IntPtr.Zero,new IntPtr(opts&~2L)); };
    }
    public void ApplyReadingFont(float points) {
        points=Math.Max(9,Math.Min(28,points));
        readingPoints=points;
        Font next;
        if(!readingFonts.TryGetValue(points,out next)) { next=new Font("Microsoft YaHei UI",points); readingFonts.Add(points,next); }
        if(Font.Equals(next)) { if(MarkdownEnabled) RenderMarkdown(false); return; }
        Font=next; SelectAll(); SelectionFont=next; Select(0,0);
        if(MarkdownEnabled) RenderMarkdown(true);
    }
    protected override void Dispose(bool disposing) {
        // RichEdit can retain a Font after an equal assignment or reuse it on zoom.
        // Own our fonts until the native control has been destroyed; never dispose
        // a Font obtained from the control's getter while that control is alive.
        base.Dispose(disposing);
        if(disposing) { foreach(var font in readingFonts.Values) font.Dispose(); readingFonts.Clear(); }
    }
    public void SetContent(string value) {
        markdownSource=null; markdownSignature=null; markdownDocument=null;
        value=(value??"").Replace("\r\n","\n"); if(value==Text) return;
        int start=SelectionStart,length=SelectionLength;
        var scroll=new ScrollPoint(); SendMessage(Handle,0x4DD,IntPtr.Zero,ref scroll);
        // Append streaming text, retaining the reader's selection and viewport.
        ChangingContent=true; SendMessage(Handle,0xB,IntPtr.Zero,IntPtr.Zero);
        try {
            if(value.StartsWith(Text,StringComparison.Ordinal)) { Select(TextLength,0); SelectionFont=Font; SelectionColor=Look.Ink; AppendText(value.Substring(TextLength)); }
            else { Text=value; SelectAll(); SelectionFont=Font; SelectionColor=Look.Ink; }
            Select(Math.Min(start,TextLength),Math.Min(length,Math.Max(0,TextLength-start)));
        } finally {
            SendMessage(Handle,0x4DE,IntPtr.Zero,ref scroll);
            ChangingContent=false; SendMessage(Handle,0xB,new IntPtr(1),IntPtr.Zero); Invalidate();
        }
    }
    public void SetMarkdown(string value) {
        value=(value??"").Replace("\r\n","\n");
        if(markdownSource==value) { RenderMarkdown(false); return; }
        markdownSource=value; markdownSignature=null; markdownDocument=null; RenderMarkdown(false);
    }
    public void RefreshMarkdownTheme() { if(MarkdownEnabled) RenderMarkdown(true); }
    void RenderMarkdown(bool force) {
        string signature=readingPoints.ToString(System.Globalization.CultureInfo.InvariantCulture)+":"+Width+":"+Theme.Dark;
        if(!force && markdownSignature==signature) return;
        int start=SelectionStart,length=SelectionLength; string selected=SelectedText;
        var scroll=new ScrollPoint(); SendMessage(Handle,0x4DD,IntPtr.Zero,ref scroll);
        ChangingContent=true; SendMessage(Handle,0xB,IntPtr.Zero,IntPtr.Zero);
        try {
            if(markdownDocument==null) { markdownDocument=Markdown.Parse(markdownSource); MarkdownParseCount++; }
            Rtf=markdownDocument.Rtf(readingPoints,Math.Max(60,ClientSize.Width));
            MarkdownRenderCount++;
            if(length>0 && selected.Length>0) {
                int nearest=-1;
                for(int found=Text.IndexOf(selected,StringComparison.Ordinal);found>=0;found=Text.IndexOf(selected,found+1,StringComparison.Ordinal)) {
                    if(nearest<0 || Math.Abs(found-start)<Math.Abs(nearest-start)) nearest=found;
                }
                if(nearest>=0) start=nearest; else length=0;
            }
            Select(Math.Min(start,TextLength),Math.Min(length,Math.Max(0,TextLength-start)));
            markdownSignature=signature; measuredWidth=-1;
        } finally {
            SendMessage(Handle,0x4DE,IntPtr.Zero,ref scroll);
            ChangingContent=false; SendMessage(Handle,0xB,new IntPtr(1),IntPtr.Zero); Invalidate();
        }
    }
    public int ContentHeight(int width) {
        Width=Math.Max(Look.S(60),width);
        if(MarkdownEnabled) RenderMarkdown(false);
        if(measuredWidth==Width) return measuredHeight;
        if(TextLength==0) return Look.S(25);
        // RichEdit's actual wrapping, not a separate font-measurement algorithm.
        SendMessage(Handle,0x441,IntPtr.Zero,IntPtr.Zero); // EM_REQUESTRESIZE uses the actual DPI and line spacing.
        int h=naturalHeight+Look.S(10);
        measuredWidth=Width; measuredHeight=Math.Max(Font.Height+Look.S(10),h);
        return measuredHeight;
    }
    public Rectangle SelectionAnchor() {
        int end=Math.Min(TextLength,SelectionStart+SelectionLength);
        Point a=GetPositionFromCharIndex(SelectionStart),b=GetPositionFromCharIndex(end);
        Point screen=PointToScreen(new Point(a.Y==b.Y?Math.Min(a.X,b.X):0,Math.Min(a.Y,b.Y)));
        return new Rectangle(screen,new Size(a.Y==b.Y?Math.Max(8,Math.Abs(b.X-a.X)):Width,Math.Abs(b.Y-a.Y)+Font.Height));
    }
    protected override void WndProc(ref Message m) {
        if(m.Msg==0x20A) {
            int delta=(short)((m.WParam.ToInt64()>>16)&0xffff);
            if((m.WParam.ToInt64()&8)!=0) { if(ZoomRequested!=null) ZoomRequested(delta>0?1:-1); return; }
            Control p=Parent;
            while(p!=null && !(p is TimelinePanel)) p=p.Parent;
            if(p!=null) { ((TimelinePanel)p).Wheel(delta,Font); if(WheelMoved!=null) WheelMoved(); return; }
        }
        base.WndProc(ref m);
    }
}
public sealed class TimelinePanel : Panel {
    [DllImport("user32.dll")] static extern bool ShowScrollBar(IntPtr hwnd,int bar,bool show);
    [DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr hwnd,IntPtr rect,IntPtr region,uint flags);
    [StructLayout(LayoutKind.Sequential)] struct PaintRect { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr hwnd,ref PaintRect rect,IntPtr region,uint flags);
    double wheelRemainder;
    bool repainting,settingScroll,hidingHorizontal;
    public int SpeedPercent=40;
    public event Action UserScrolled;
    public event Action PositionChanged;
    public TimelinePanel() { AutoScroll=true; DoubleBuffered=true; }
    protected override void OnLayout(LayoutEventArgs e) {
        base.OnLayout(e);
        if(IsHandleCreated && !hidingHorizontal) { hidingHorizontal=true; try { ShowScrollBar(Handle,0,false); } finally { hidingHorizontal=false; } }
    }
    protected override Point ScrollToControl(Control activeControl) { return DisplayRectangle.Location; }
    public new Point AutoScrollPosition {
        get { return base.AutoScrollPosition; }
        set {
            var before=base.AutoScrollPosition;
            settingScroll=true; try { base.AutoScrollPosition=value; } finally { settingScroll=false; }
            RepaintScrolled(before.Y-base.AutoScrollPosition.Y);
            if(PositionChanged!=null) PositionChanged();
        }
    }
    public void RepaintViewport() {
        if(repainting || !IsHandleCreated || !Visible) return;
        repainting=true;
        try { RedrawWindow(Handle,IntPtr.Zero,IntPtr.Zero,0x1|0x4|0x80|0x100); }
        finally { repainting=false; }
    }
    void RepaintScrolled(int delta) {
        if(delta==0 || repainting || !IsHandleCreated || !Visible) return;
        if(Math.Abs(delta)>=ClientSize.Height) { RepaintViewport(); return; }
        int strip=Math.Min(ClientSize.Height,Math.Abs(delta)+2);
        var rect=new PaintRect { Left=0,Top=delta>0?ClientSize.Height-strip:0,Right=ClientSize.Width,Bottom=delta>0?ClientSize.Height:strip };
        repainting=true;
        try { RedrawWindow(Handle,ref rect,IntPtr.Zero,0x1|0x4|0x80|0x100); }
        finally { repainting=false; }
    }
    protected override void OnScroll(ScrollEventArgs e) { base.OnScroll(e); if(!settingScroll) RepaintScrolled(e.NewValue-e.OldValue); if(PositionChanged!=null) PositionChanged(); }
    public void ScrollBy(int delta) { AutoScrollPosition=new Point(0,Math.Max(0,Math.Min(Math.Max(0,AutoScrollMinSize.Height-ClientSize.Height),-AutoScrollPosition.Y+delta))); }
    public void Wheel(int delta,Font font) {
        int lines=SystemInformation.MouseWheelScrollLines;
        if(lines==0) return;
        double distance=lines<0?ClientSize.Height*.85:Math.Max(1,lines)*font.GetHeight(Look.Scale*96);
        wheelRemainder-=delta/120.0*distance*Math.Max(10,Math.Min(150,SpeedPercent))/100.0; int pixels=(int)wheelRemainder; wheelRemainder-=pixels;
        ScrollBy(pixels);
        if(UserScrolled!=null) UserScrolled();
    }
    protected override void OnMouseWheel(MouseEventArgs e) { Wheel(e.Delta,Font); }
}
public sealed class QuoteBubble : Form {
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr handle,int command);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr handle,IntPtr after,int x,int y,int width,int height,uint flags);
    readonly SoftButton quote=new SoftButton("Quote",true,false);
    Action chosen;
    public QuoteBubble() {
        FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false; StartPosition=FormStartPosition.Manual;
        AutoScaleMode=AutoScaleMode.None; ClientSize=new Size(Look.S(88),Look.S(38)); Padding=new Padding(3);
        Controls.Add(quote);
        quote.Click+=delegate { var action=chosen; Hide(); if(action!=null) action(); };
        Look.Shape(this,9);
    }
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override void SetVisibleCore(bool value) { base.SetVisibleCore(false); }
    protected override CreateParams CreateParams { get { var p=base.CreateParams; p.ExStyle|=0x08000000; return p; } }
    protected override void WndProc(ref Message m) {
        if(m.Msg==0x21) { m.Result=new IntPtr(3); return; } // MA_NOACTIVATE: quoting must not steal the reading selection.
        base.WndProc(ref m);
    }
    public void Offer(Form owner,Rectangle anchor,Action action) {
        // Compatibility only: selection is reserved for copying and F13 lookup.
        // Whole-entry quoting is available from the entry's own action button.
        chosen=null; Hide();
    }
}
public sealed class EntryCard : UserControl {
    public readonly WorkEntry Entry;
    public readonly StudyText Original=new StudyText(), Answer=new StudyText();
    readonly Label badge=new Label { AutoSize=false }, state=new Label { AutoSize=false,AutoEllipsis=true,UseMnemonic=false };
    readonly FlowLayoutPanel actions=new FlowLayoutPanel { WrapContents=false,AutoScroll=false };
    readonly FlowLayoutPanel imageRow=new FlowLayoutPanel { WrapContents=false };
    readonly SoftButton copy=new SoftButton("Copy",false), ask=new SoftButton("Quote",false), explain=new SoftButton("Explain",false), polish=new SoftButton("Polish",false), save=new SoftButton("Save",false), listen=new SoftButton("Listen",false), cancel=new SoftButton("Stop",false), retry=new SoftButton("Retry",false);
    readonly SoftButton listenOriginal=new SoftButton("▶ Original",false);
    readonly Label quoteInfo=new Label { AutoSize=false,UseMnemonic=false };
    readonly ToolTip tips=new ToolTip();
    public event Action QuoteCleared, LayoutNeeded;
    public event Action<WorkEntry,string> ActionRequested;
    public event Action<int> ZoomRequested;
    public event Action<string> SpeechRequested;
    public long LastSelectionTime { get; private set; }
    StudyText selectedBox;
    bool laying;
    public EntryCard(WorkEntry entry,float font,string imageRoot=null) {
        Entry=entry; DoubleBuffered=true;
        Controls.AddRange(new Control[]{badge,listenOriginal,Original,imageRow,quoteInfo,Answer,state,actions});
        if(entry.Images==null) entry.Images=new List<StudyImage>();
        if(imageRoot!=null) foreach(var image in entry.Images) imageRow.Controls.Add(new ImageTile(imageRoot,image,false));
        imageRow.Visible=imageRow.Controls.Count>0;
        badge.Visible=false; Original.AccessibleName=UiLanguage.T("Queried text","查询原文"); Answer.AccessibleName=UiLanguage.T("Answer","回答");
        state.Font=new Font("Segoe UI",8); quoteInfo.Font=new Font("Microsoft YaHei UI",9);
        actions.Controls.AddRange(new Control[]{save,ask,listen,copy,polish,explain,cancel,retry});
        foreach(SoftButton b in actions.Controls) { b.Width=Look.S(28); b.Height=Look.S(28); b.Margin=new Padding(0,0,Look.S(4),0); }
        copy.Click+=delegate { try { Clipboard.SetText(Entry.Answer); state.Text=UiLanguage.T("Copied","已复制"); } catch { state.Text=UiLanguage.T("Clipboard busy","剪贴板忙，请重试"); } };
        ask.Click+=delegate { Act("ask"); }; explain.Click+=delegate { Act("explain"); }; polish.Click+=delegate { Act("polish"); };
        save.Click+=delegate { Act("save"); };
        listen.Click+=delegate { if(SpeechRequested!=null) SpeechRequested(String.IsNullOrWhiteSpace(Answer.SelectedText)?Markdown.PlainText(Entry.Answer):Answer.SelectedText); };
        listenOriginal.Click+=delegate { if(SpeechRequested!=null) SpeechRequested(String.IsNullOrWhiteSpace(Original.SelectedText)?Entry.Text:Original.SelectedText); };
        cancel.Click+=delegate { Act("stop"); }; retry.Click+=delegate { Act("retry"); };
        foreach(var text in new[]{Original,Answer}) {
            text.MouseUp+=delegate { Offer(text); };
            text.KeyUp+=delegate(object sender,KeyEventArgs e) { if(e.Shift || e.Control) Offer(text); };
            text.SelectionChanged+=delegate {
                if(text.ChangingContent || !text.Focused) return;
                selectedBox=text; LastSelectionTime=System.Diagnostics.Stopwatch.GetTimestamp();
                if(text.SelectionLength==0 && QuoteCleared!=null) QuoteCleared();
            };
            text.ZoomRequested+=delegate(int step) { if(ZoomRequested!=null) ZoomRequested(step); };
        }
        ApplyFont(font); RefreshContent();
    }
    void Act(string action) { if(ActionRequested!=null) ActionRequested(Entry,action); }
    void Offer(StudyText text) {
        if(String.IsNullOrWhiteSpace(text.SelectedText)) return;
        selectedBox=text; LastSelectionTime=System.Diagnostics.Stopwatch.GetTimestamp();
    }
    public string Selected { get { return selectedBox==null?"":selectedBox.SelectedText; } }
    public Rectangle SelectedBounds { get { return selectedBox==null?Rectangle.Empty:selectedBox.SelectionAnchor(); } }
    public void ApplyFont(float points) {
        Original.ApplyReadingFont(Math.Max(9,points-1)); Answer.ApplyReadingFont(points);
        Relayout();
    }
    public void RefreshContent() {
        Original.SetContent(Entry.Text); Answer.SetMarkdown(Entry.Answer);
        badge.Text=UiLanguage.Mode(Entry.Mode).ToUpperInvariant(); badge.ForeColor=Look.Accent;
        quoteInfo.Text=Entry.Quotes.Count==0?"":String.Join("\n",Entry.Quotes.Select((q,i)=>"“"+q.Text+"”"));
        state.Text=!String.IsNullOrEmpty(Entry.Error)?Entry.Error:Entry.Finished?"":UiLanguage.T("Answering…","正在回答…");
        state.ForeColor=Entry.Error.Length>0?Look.Error:Look.Muted;
        V2Icons.Set(copy,"copy",UiLanguage.T("Copy answer","复制回答"),tips);
        V2Icons.Set(ask,"quote",UiLanguage.T("Quote answer and ask a follow-up","引用回答并追问"),tips);
        V2Icons.Set(polish,"polish",UiLanguage.T("Polish the original wording","润色原文"),tips);
        V2Icons.Set(explain,"explain",UiLanguage.T("Explain the original text","解释原文"),tips); explain.Visible=Entry.Mode!=WorkMode.Explain;
        V2Icons.Set(save,Entry.Saved?"saved":"save",UiLanguage.T(Entry.Saved?"Saved; click to remove":"Save this entry",Entry.Saved?"已收藏，点击取消":"收藏这条记录"),tips);
        V2Icons.Set(listen,"listen",UiLanguage.T("Read answer or selected words","朗读回答或选中的词"),tips);
        V2Icons.Set(listenOriginal,"listen",UiLanguage.T("Read the queried text","朗读查询原文"),tips);
        V2Icons.Set(cancel,"stop",UiLanguage.T("Stop generating","停止生成"),tips);
        V2Icons.Set(retry,"retry",UiLanguage.T("Retry this request","重试这条查询"),tips);
        tips.SetToolTip(state,state.Text);
        copy.Enabled=ask.Enabled=listen.Enabled=Entry.Answer.Length>0;
        save.Visible=Entry.Images.Count==0; save.Enabled=Entry.Finished && Entry.Answer.Length>0;
        listenOriginal.Enabled=!Entry.ImageOnly;
        polish.Visible=Entry.Mode!=WorkMode.Polish; cancel.Visible=!Entry.Finished && Entry.Error.Length==0;
        retry.Visible=Entry.Error.Length>0;
        tips.SetToolTip(ask,UiLanguage.T("Quote this answer and ask a follow-up","引用本条回答并继续提问"));
        Relayout();
    }
    public void Relayout() {
        if(laying || Width<100) return; laying=true; int previousHeight=Height;
        try {
            int pad=Look.S(14),w=Math.Max(Look.S(100),Width-pad*2),y=Look.S(10),icon=Look.S(26);
            badge.Visible=false;
            int originalWidth=w-icon-Look.S(8);
            if(Entry.Text.IndexOf('\n')<0 && Entry.Text.Length<120) originalWidth=Math.Min(originalWidth,TextRenderer.MeasureText(Entry.Text+" ",Original.Font,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Width+Look.S(6));
            originalWidth=Math.Max(Look.S(24),originalWidth);
            Original.SetBounds(pad+Look.S(7),y,originalWidth,Original.ContentHeight(originalWidth));
            listenOriginal.SetBounds(Math.Min(Width-pad-icon,Original.Right+Look.S(4)),y,icon,icon);
            y+=Math.Max(Original.Height,icon)+Look.S(8);
            if(imageRow.Controls.Count>0) { imageRow.SetBounds(pad,y,w,Look.S(64)); y+=Look.S(72); }
            quoteInfo.Visible=Entry.Quotes.Count>0;
            if(Entry.Quotes.Count>0) {
                int h=TextRenderer.MeasureText(quoteInfo.Text,quoteInfo.Font,new Size(w,Int32.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix).Height+Look.S(12);
                quoteInfo.SetBounds(pad,y,w,h); y+=h+Look.S(8);
            }
            Answer.Visible=Entry.Answer.Length>0;
            if(Entry.Answer.Length>0) { Answer.SetBounds(pad,y,w,Answer.ContentHeight(w)); y=Answer.Bottom+Look.S(4); }
            state.Visible=state.Text.Length>0;
            if(state.Text.Length>0) { state.SetBounds(pad,y,w,Look.S(20)); y+=Look.S(22); }
            // Two rows are possible at a narrow width or a large display scale.
            actions.WrapContents=true; actions.SetBounds(pad,y,w,Look.S(28));
            int n=4+(Entry.Mode!=WorkMode.Polish?1:0)+(Entry.Mode!=WorkMode.Explain?1:0)+(!Entry.Finished && Entry.Error.Length==0?1:0)+(Entry.Error.Length>0?1:0);
            int perRow=Math.Max(1,w/Look.S(32)); actions.Height=Look.S(28)*Math.Max(1,(n+perRow-1)/perRow);
            Height=actions.Bottom+Look.S(10);
        } finally { laying=false; }
        if(Height!=previousHeight && LayoutNeeded!=null) LayoutNeeded();
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);
        using(var pen=new Pen(Look.Border)) e.Graphics.DrawLine(pen,Look.S(14),Height-1,Width-Look.S(14),Height-1);
        using(var pen=new Pen(Look.Accent,Look.S(2))) e.Graphics.DrawLine(pen,Look.S(14),Original.Top+Look.S(3),Look.S(14),Original.Bottom-Look.S(3));
    }
    protected override void Dispose(bool disposing) { if(disposing) tips.Dispose(); base.Dispose(disposing); }
}
// A tap is local to the focused TermLens window. Modified shortcuts and IME commits win.
public sealed class ShiftTap : IMessageFilter,IDisposable {
    readonly Form owner; readonly Func<bool> composing; readonly Action tap;
    bool armed,combined;
    public ShiftTap(Form owner,Func<bool> composing,Action tap) { this.owner=owner; this.composing=composing; this.tap=tap; Application.AddMessageFilter(this); }
    public bool PreFilterMessage(ref Message m) {
        if(!owner.Visible || Native.GetForegroundWindow()!=owner.Handle) { armed=false; return false; }
        int code=unchecked((int)m.WParam.ToInt64()); bool shift=code==0x10 || code==0xA0 || code==0xA1;
        if(m.Msg==0x100 || m.Msg==0x104) {
            if(shift && !armed) { armed=true; combined=Native.Down(0x11)||Native.Down(0x12)||Native.Down(0x5b)||Native.Down(0x5c)||composing(); }
            else if(!shift && armed) combined=true;
        }
        if(m.Msg==0x201 || m.Msg==0x204 || m.Msg==0x207 || m.Msg==0x20A || m.Msg==0x10D) combined=true;
        if((m.Msg==0x101 || m.Msg==0x105) && shift) { bool run=armed&&!combined&&!composing(); armed=false; if(run) tap(); }
        return false;
    }
    public void Dispose() { Application.RemoveMessageFilter(this); }
}
public sealed class WorkspaceWindow : Form {
    public readonly TimelinePanel Timeline=new TimelinePanel();
    public readonly ComposerBox Composer=new ComposerBox { MaxLength=12000 };
    public readonly List<EntryCard> Cards=new List<EntryCard>();
    public readonly QuoteBubble Quote=new QuoteBubble();
    public readonly SoftButton CloseButton=new SoftButton("×",false);
    public readonly SoftButton PinButton=new SoftButton("Pin",true);
    readonly Panel header=new Panel(),bottom=new Panel(),timelineHost=new Panel();
    readonly SlimScrollBar scrollBar=new SlimScrollBar();
    readonly Label title=new Label(),hint=new Label(),empty=new Label(),notice=new Label();
    readonly FlowLayoutPanel modeRow=new FlowLayoutPanel { WrapContents=false },quoteRow=new FlowLayoutPanel { WrapContents=true,AutoScroll=true };
    readonly FlowLayoutPanel imageRow=new FlowLayoutPanel { WrapContents=false };
    readonly SoftButton addImage=new SoftButton("＋ Image",false);
    readonly string imageRoot;
    readonly SoftButton settingsButton=new SoftButton("Settings",false),last=new SoftButton("Last session",false),send=new SoftButton("Explain",true),stopVoice=new SoftButton("Stop audio",false);
    readonly SoftButton listenDraft=new SoftButton("▶ Listen",false);
    readonly List<SoftButton> modes=new List<SoftButton>();
    readonly FieldFrame composerFrame;
    readonly Panel composerViewport=new Panel { Name="ComposerViewport" };
    readonly ShiftTap shift;
    readonly ReadingSelection selection;
    readonly Timer autosave=new Timer { Interval=600 };
    readonly ToolTip tips=new ToolTip();
    bool layoutBusy,internalSize,closing,follow=true,moving,syncingScroll,geometryInitialized,speechActive;
    float points=11; int preferredWidth=540,preferredHeight,lastLeft=int.MinValue,lastTop=int.MinValue;
    Size ownMinimum;
    bool hasSavedGeometry; Rectangle savedGeometry;
    internal int TimelineLayoutCount { get; private set; }
    internal int ComposerLayoutCount { get; private set; }
    public WorkSession Session { get; private set; }
    public event Action<WorkMode,string,List<WorkQuote>> Submitted;
    public event Action SettingsRequested,LastRequested,SaveSessionRequested,UserClosed,StopVoiceRequested;
    public event Action<float,int,int> ReadingChanged;
    public event Action<int,int,int,int> GeometryChanged;
    public event Action<bool> KeepOnTopChanged;
    public event Action<WorkEntry,string> EntryAction;
    public event Action<string> SpeechRequested;
    public WorkspaceWindow(WorkSession session,Settings settings,string imageRoot=null) {
        this.imageRoot=imageRoot;
        Session=session??new WorkSession();
        if(Session.DraftImages==null) Session.DraftImages=new List<StudyImage>();
        Timeline.SpeedPercent=Math.Max(10,Math.Min(150,settings.ScrollSpeed));
        Text="TermLens"; FormBorderStyle=FormBorderStyle.None; ControlBox=false; ShowInTaskbar=true;
        StartPosition=FormStartPosition.Manual; AutoScaleMode=AutoScaleMode.None;
        // Leave a real form border for hit testing; docked child panels otherwise consume it.
        Padding=new Padding(Look.S(5));
        MinimumSize=ownMinimum=new Size(Look.S(360),Look.S(340)); DoubleBuffered=true;
        Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        title.Font=new Font("Segoe UI",9,FontStyle.Bold); title.Text="TermLens  /  v2";
        header.Controls.AddRange(new Control[]{title,settingsButton,last,PinButton,CloseButton});
        SetPinned(settings.ReadingTopMost);
        PinButton.Click+=delegate { SetPinned(!TopMost); if(KeepOnTopChanged!=null) KeepOnTopChanged(TopMost); };
        Controls.AddRange(new Control[]{timelineHost,header,bottom});
        header.Dock=DockStyle.Top; header.Height=Look.S(38); bottom.Dock=DockStyle.Bottom;
        timelineHost.Name="ReadingViewport"; timelineHost.Dock=DockStyle.Fill; timelineHost.Controls.Add(Timeline); timelineHost.Controls.Add(scrollBar);
        Timeline.Dock=DockStyle.None; Timeline.Controls.Add(empty);
        scrollBar.Width=Look.S(8); scrollBar.Anchor=AnchorStyles.Top|AnchorStyles.Right|AnchorStyles.Bottom;
        timelineHost.Resize+=delegate { LayoutTimelineViewport(); };
        Timeline.PositionChanged+=SyncScrollBar;
        scrollBar.ValueChanged+=delegate { if(syncingScroll) return; Timeline.AutoScrollPosition=new Point(0,scrollBar.Value); follow=NearBottom(); ClearReadingSelection(); Session.Scroll=scrollBar.Value; ScheduleSave(); };
        empty.Font=new Font("Microsoft YaHei UI",14); empty.TextAlign=ContentAlignment.MiddleCenter;
        composerFrame=new FieldFrame(Composer,true) { Dock=DockStyle.None };
        Composer.Dock=DockStyle.None;
        // Keep the native editing width and handle stable at the overflow edge.
        // Changing ScrollBars while typing recreates the TextBox handle.
        Composer.ScrollBars=ScrollBars.Vertical;
        // Clip the native scrollbar gutter without toggling its style or
        // rebuilding the editor handle as the draft grows and shrinks.
        composerViewport.Controls.Add(Composer);
        composerFrame.Controls.AddRange(new Control[]{composerViewport,modeRow,send,listenDraft});
        bottom.Controls.AddRange(new Control[]{quoteRow,imageRow,composerFrame,hint,notice,stopVoice});
        foreach(WorkMode mode in Enum.GetValues(typeof(WorkMode))) {
            var selected=mode; var button=new SoftButton(UiLanguage.Mode(mode),false) { Width=Look.S(70),Height=Look.S(26),Margin=new Padding(0,0,Look.S(4),0) };
            button.Click+=delegate { SetMode(selected); Composer.Focus(); }; modes.Add(button); modeRow.Controls.Add(button);
        }
        addImage.Size=new Size(Look.S(28),Look.S(26)); addImage.Margin=new Padding(0); addImage.Visible=imageRoot!=null; modeRow.Controls.Add(addImage);
        addImage.Click+=delegate { using(var dialog=new OpenFileDialog { Filter="Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif",Multiselect=true,Title=UiLanguage.T("Choose an image","选择图片") }) { if(dialog.ShowDialog(this)==DialogResult.OK) AttachFiles(dialog.FileNames); } };
        Composer.PasteImage=PasteClipboardImage;
        Composer.AllowDrop=true; Composer.DragEnter+=delegate(object sender,DragEventArgs e) { if(e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect=DragDropEffects.Copy; };
        Composer.DragDrop+=delegate(object sender,DragEventArgs e) { AttachFiles((string[])e.Data.GetData(DataFormats.FileDrop)); };
        hint.Font=new Font("Segoe UI",8); notice.Font=new Font("Microsoft YaHei UI",9); notice.AutoEllipsis=true;
        Composer.Text=Session.Draft??""; Composer.Submitted+=Send;
        Composer.TextChanged+=delegate { Session.Draft=Composer.Text; LayoutAll(true); ScheduleSave(); };
        Composer.Enter+=delegate { ClearReadingSelection(); };
        listenDraft.Click+=delegate { if(!Composer.CompositionActive && SpeechRequested!=null && !String.IsNullOrWhiteSpace(Composer.Text)) SpeechRequested(String.IsNullOrWhiteSpace(Composer.SelectedText)?Composer.Text:Composer.SelectedText); };
        send.Click+=delegate { Send(); }; CloseButton.Click+=delegate { Close(); };
        settingsButton.Click+=delegate { Quote.Hide(); if(SettingsRequested!=null) SettingsRequested(); };
        last.Click+=delegate { if(LastRequested!=null) LastRequested(); };
        stopVoice.Visible=false;
        stopVoice.Click+=delegate { if(StopVoiceRequested!=null) StopVoiceRequested(); SetSpeaking(false); };
        // Scroll also fires during native layout. Wheel input and the local mouse
        // filter clear selections; a layout-driven scroll must not erase a lookup.
        Timeline.Scroll+=delegate { follow=NearBottom(); Session.Scroll=-Timeline.AutoScrollPosition.Y; ScheduleSave(); };
        Timeline.UserScrolled+=delegate { follow=NearBottom(); ClearReadingSelection(); Session.Scroll=-Timeline.AutoScrollPosition.Y; ScheduleSave(); };
        Resize+=delegate { LayoutAll(); };
        ResizeEnd+=delegate { preferredWidth=(int)Math.Round(Width/Look.Scale); preferredHeight=(int)Math.Round(Height/Look.Scale); SaveGeometry(); ReportReading(); };
        MouseDown+=delegate { Quote.Hide(); };
        title.MouseDown+=Drag; header.MouseDown+=Drag;
        Deactivate+=delegate { ClearReadingSelection(); };
        Move+=delegate { Quote.Hide(); };
        shift=new ShiftTap(this,delegate { return Composer.CompositionActive; },delegate { SetMode((WorkMode)(((int)Session.Mode+1)%3)); });
        selection=new ReadingSelection(this);
        autosave.Tick+=delegate { autosave.Stop(); SaveNow(); };
        FormClosing+=delegate { closing=true; Quote.Hide(); SaveNow(); if(UserClosed!=null) UserClosed(); };
        foreach(var entry in Session.Entries.ToArray()) AddCard(entry,false);
        ApplyReading(settings.ReadingFontSize,settings.ReadingWidth,settings.ReadingHeight);
        // Validated here, applied in OnLoad after WinForms finishes auto-scaling.
        hasSavedGeometry=WindowGeometry.TryRestore(settings.WorkspaceLeft,settings.WorkspaceTop,settings.WorkspaceWidth,settings.WorkspaceHeight,ownMinimum,out savedGeometry);
        RefreshLanguage(); RenderQuotes(); RenderImages(); Theme.Apply(this); ApplyAppearance(settings);
        Shown+=delegate { LayoutAll(); Timeline.AutoScrollPosition=new Point(0,Session.Scroll); Composer.Focus(); };
    }
    protected override void OnLoad(EventArgs e) {
        base.OnLoad(e);
        // ownMinimum already comes from Look.S(), so it is in device units and
        // must not be divided by the scale again. Only cap it to the screen.
        var work=Screen.FromControl(this).WorkingArea;
        MinimumSize=new Size(Math.Max(320,Math.Min(ownMinimum.Width,(int)Math.Round(work.Width*0.92))),
                             Math.Max(300,Math.Min(ownMinimum.Height,(int)Math.Round(work.Height*0.92))));
        if(hasSavedGeometry && WindowState==FormWindowState.Normal) { Bounds=savedGeometry; lastLeft=savedGeometry.Left; lastTop=savedGeometry.Top; }
    }
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h,int m,IntPtr w,IntPtr l);
    void Drag(object sender,MouseEventArgs e) { if(e.Button==MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero); } }
    public void RefreshLanguage() {
        V2Icons.Set(settingsButton,"settings",UiLanguage.T("Settings","设置"),tips);
        V2Icons.Set(last,"history",UiLanguage.T("Restore last session","恢复上次会话"),tips);
        V2Icons.Set(CloseButton,"close",UiLanguage.T("Close and keep this session","关闭并保留会话"),tips);
        RefreshPin();
        hint.Text=""; hint.Visible=false;
        empty.Text=UiLanguage.T("Read. Ask. Write.\n\nType here, or select text and press F13.","阅读 · 提问 · 表达\n\n在下面输入，或选中文字后按 F13。");
        V2Icons.Set(stopVoice,"stop",UiLanguage.T("Stop audio","停止朗读"),tips);
        V2Icons.Set(listenDraft,"listen",UiLanguage.T("Read draft without sending","朗读草稿，不提交查询"),tips);
        addImage.IconName="image"; addImage.IconWithText=true; addImage.Quiet=true; addImage.Text=UiLanguage.T("+ Image","+ 图片");
        addImage.AccessibleName=UiLanguage.T("Attach image; Ctrl+V pastes a screenshot","添加图片；Ctrl+V 粘贴截图");
        tips.SetToolTip(addImage,addImage.AccessibleName);
        tips.SetToolTip(Composer,UiLanguage.T("Enter: send; Shift+Enter: new line; Shift: switch mode","Enter 发送；Shift+Enter 换行；Shift 切换模式"));
        CloseButton.AccessibleName=UiLanguage.T("Close and keep this session","关闭并保留本次记录");
        for(int i=0;i<modes.Count;i++) {
            modes[i].Text=UiLanguage.Mode((WorkMode)i); modes[i].IconName=i==0?"explain":i==1?"ask":"polish"; modes[i].IconWithText=false;
            modes[i].AccessibleName=modes[i].Text; tips.SetToolTip(modes[i],modes[i].Text+UiLanguage.T(" mode; Shift switches mode","模式；Shift 切换模式"));
        }
        foreach(var card in Cards) card.RefreshContent();
        SetMode(Session.Mode); RenderQuotes();
    }
    void RefreshPin() {
        V2Icons.Set(PinButton,"pin",UiLanguage.T(TopMost?"Unpin window":"Pin window",TopMost?"取消置顶":"置顶窗口"),tips); PinButton.Primary=TopMost; PinButton.Invalidate();
        PinButton.AccessibleName=UiLanguage.T("Keep on top","窗口置顶");
        PinButton.AccessibleDescription=UiLanguage.T(TopMost?"On; click to turn off":"Off; click to turn on",TopMost?"已开启，点击取消置顶":"已关闭，点击置顶");
        tips.SetToolTip(PinButton,PinButton.AccessibleDescription);
        tips.SetToolTip(last,UiLanguage.T("Last session","上次窗口"));
    }
    public void SetPinned(bool enabled) { TopMost=enabled; RefreshPin(); }
    public void ApplyAppearance(Settings value) {
        WindowAppearance.Apply(this,value,false);
    }
    public void SetMode(WorkMode mode) {
        Session.Mode=mode; for(int i=0;i<modes.Count;i++) { modes[i].Primary=i==(int)mode; modes[i].Invalidate(); }
        V2Icons.Set(send,"send",UiLanguage.T("Send: ","发送：")+UiLanguage.Mode(mode)+UiLanguage.T(" (Enter)","（Enter）"),tips); send.Quiet=false; ScheduleSave();
    }
    public void Send() {
        if(Composer.CompositionActive || (String.IsNullOrWhiteSpace(Composer.Text) && Session.DraftImages.Count==0)) return;
        if(Submitted!=null) Submitted(Session.Mode,Composer.Text.Trim(),Session.Quotes.Select(q=>q.Copy()).ToList());
    }
    // Called only after request validation succeeds; failed validation keeps the draft.
    public void AcceptDraft() { Composer.Clear(); Session.Quotes.Clear(); Session.DraftImages.Clear(); RenderQuotes(); RenderImages(); SaveNow(); }
    public bool PasteClipboardImage() {
        if(imageRoot==null) return false;
        try {
            if(!Clipboard.ContainsImage()) return false;
            using(var image=Clipboard.GetImage()) { if(image!=null) AttachImage(image,"Screenshot"); }
            return true;
        } catch(Exception e) { SetNotice(UiLanguage.T("Could not paste image: ","无法粘贴图片：")+e.Message); return true; }
    }
    public void AttachImage(Image image,string name) {
        if(imageRoot==null) return;
        try { if(Session.DraftImages.Count>=ImageInput.MaximumCount) throw new Exception(UiLanguage.T("Attach up to 4 images per question.","每次最多添加 4 张图片。")); Session.DraftImages.Add(ImageInput.Store(imageRoot,image,name)); RenderImages(); SetNotice(""); SaveNow(); Composer.Focus(); }
        catch(Exception e) { SetNotice(e.Message); }
    }
    public void AttachFiles(string[] paths) {
        if(imageRoot==null || paths==null) return;
        foreach(string path in paths) {
            try { if(Session.DraftImages.Count>=ImageInput.MaximumCount) throw new Exception(UiLanguage.T("Attach up to 4 images per question.","每次最多添加 4 张图片。")); Session.DraftImages.Add(ImageInput.StoreFile(imageRoot,path)); }
            catch(Exception e) { SetNotice(e.Message); break; }
        }
        RenderImages(); SaveNow(); Composer.Focus();
    }
    void RenderImages() {
        foreach(Control control in imageRow.Controls.Cast<Control>().ToArray()) { imageRow.Controls.Remove(control); control.Dispose(); }
        if(imageRoot!=null) foreach(var image in Session.DraftImages) {
            var current=image; var tile=new ImageTile(imageRoot,image,true); tile.Removed+=delegate { Session.DraftImages.Remove(current); RenderImages(); SaveNow(); }; imageRow.Controls.Add(tile);
        }
        imageRow.Visible=imageRow.Controls.Count>0; LayoutAll(); FitContent();
    }
    public void RestoreDraftImages(IEnumerable<StudyImage> images) {
        if(images!=null) foreach(var image in images) if(Session.DraftImages.Count<ImageInput.MaximumCount && !Session.DraftImages.Any(item=>item.Id==image.Id)) Session.DraftImages.Add(image);
        RenderImages();
    }
    public void AddQuote(WorkQuote quote) {
        ClearReadingSelection();
        if(Session.Quotes.Any(q=>q.SourceId==quote.SourceId && q.Text==quote.Text)) { Composer.Focus(); return; }
        if(Session.Quotes.Count>=16) { SetNotice(UiLanguage.T("Remove a quote before adding more.","引用已较多，请先移除一些。")); return; }
        Session.Quotes.Add(quote.Copy()); SetMode(WorkMode.Ask); RenderQuotes(); Composer.Focus(); SaveNow();
    }
    void RenderQuotes() {
        foreach(Control c in quoteRow.Controls.Cast<Control>().ToArray()) { quoteRow.Controls.Remove(c); c.Dispose(); }
        foreach(var quote in Session.Quotes.ToArray()) {
            var current=quote; string preview=Regex.Replace(quote.Text,@"\s+"," ");
            var chip=new SoftButton("“"+(preview.Length>25?preview.Substring(0,25)+"…":preview)+"”  ×",false) { Width=Look.S(205),Height=Look.S(34),Margin=new Padding(0,0,Look.S(6),Look.S(5)),AccessibleName=UiLanguage.T("Remove quote: ","移除引用：")+quote.Text };
            tips.SetToolTip(chip,quote.Text+"\n"+UiLanguage.T("Click to remove","点击移除"));
            chip.Click+=delegate { Session.Quotes.Remove(current); RenderQuotes(); SaveNow(); };
            quoteRow.Controls.Add(chip);
        }
        quoteRow.Visible=Session.Quotes.Count>0; LayoutAll(); FitContent();
    }
    public EntryCard AddEntry(WorkEntry entry) { Session.Entries.Add(entry); return AddCard(entry,true); }
    EntryCard AddCard(WorkEntry entry,bool toBottom) {
        var card=new EntryCard(entry,points,imageRoot); Cards.Add(card); Timeline.Controls.Add(card);
        card.Original.Enter+=delegate { ClearReadingSelection(card.Original); };
        card.Answer.Enter+=delegate { ClearReadingSelection(card.Answer); };
        card.LayoutNeeded+=delegate { if(!layoutBusy) LayoutTimeline(); };
        card.QuoteCleared+=delegate { Quote.Hide(); };
        card.ActionRequested+=delegate(WorkEntry item,string action) {
            // Explicit quoting always includes the complete source and answer,
            // independent of any selection used for F13 or speech.
            if(action=="ask") AddQuote(new WorkQuote { Text=Markdown.PlainText(item.Answer),SourceId=item.Id,SourceText=item.Text });
            else if(EntryAction!=null) EntryAction(item,action);
        };
        card.ZoomRequested+=ChangeFont;
        card.SpeechRequested+=delegate(string text) { if(SpeechRequested!=null) SpeechRequested(text); };
        Theme.Apply(card); LayoutTimeline();
        if(toBottom) { follow=true; FitContent(); Timeline.AutoScrollPosition=new Point(0,Math.Max(0,Timeline.AutoScrollMinSize.Height-Timeline.ClientSize.Height)); }
        return card;
    }
    public void UpdateEntry(WorkEntry entry) {
        var card=Cards.FirstOrDefault(c=>c.Entry==entry); if(card==null || IsDisposed) return;
        bool tail=follow && NearBottom(); card.RefreshContent();
        // Keep window geometry steady while tokens arrive. The timeline still grows.
        if(entry.Finished || entry.Error.Length>0) FitContent();
        if(tail && Cards.Count>0) Timeline.AutoScrollPosition=new Point(0,Math.Max(0,Timeline.AutoScrollMinSize.Height-Timeline.ClientSize.Height));
        Timeline.RepaintViewport(); SyncScrollBar();
        if(entry.Finished || entry.Error.Length>0) SaveNow();
    }
    bool NearBottom() { return -Timeline.AutoScrollPosition.Y+Timeline.ClientSize.Height>=Timeline.AutoScrollMinSize.Height-Look.S(70); }
    public void ApplyReading(float font,int width,int height) {
        bool explicitSizeChange=geometryInitialized && (Math.Max(360,width)!=preferredWidth || height!=preferredHeight);
        points=Math.Max(9,Math.Min(28,font)); preferredWidth=Math.Max(360,width); preferredHeight=height;
        Composer.Font=new Font("Microsoft YaHei UI",points);
        foreach(var card in Cards) card.ApplyFont(points);
        if(explicitSizeChange) geometryInitialized=false;
        FitContent(); LayoutAll(); Timeline.RepaintViewport();
        if(explicitSizeChange) SaveGeometry();
    }
    void ChangeFont(int step) { points=Math.Max(9,Math.Min(28,points+step)); ApplyReading(points,preferredWidth,preferredHeight); ReportReading(); }
    void ReportReading() { if(!internalSize && ReadingChanged!=null) ReadingChanged(points,preferredWidth,preferredHeight); }
    public void FitContent() {
        if(internalSize || closing || moving || geometryInitialized) return; internalSize=true;
        try {
            var work=Screen.FromControl(this).WorkingArea;
            int width=Math.Min(Look.S(preferredWidth),work.Width);
            int height=Math.Max(Look.S(340),preferredHeight>0?Look.S(preferredHeight):Math.Min(Look.S(560),(int)(work.Height*.7)));
            Size=new Size(width,Math.Min(work.Height-Look.S(20),height));
            if(Visible) Location=new Point(Math.Max(work.Left,Math.Min(Left,work.Right-Width)),Math.Max(work.Top,Math.Min(Top,work.Bottom-Height)));
            geometryInitialized=true;
        } finally { internalSize=false; }
    }
    void LayoutAll(bool composerOnly=false) {
        if(layoutBusy || ClientSize.Width<100) return; layoutBusy=true;
        int previousBottomHeight=bottom.Height; Size previousViewport=timelineHost.ClientSize;
        try {
            ComposerLayoutCount++;
            int p=Look.S(12),w=Math.Max(100,ClientSize.Width-Padding.Horizontal),icon=Look.S(28),gap=Look.S(4);
            CloseButton.SetBounds(w-p-icon,Look.S(5),icon,icon);
            settingsButton.SetBounds(CloseButton.Left-gap-icon,Look.S(5),icon,icon);
            last.SetBounds(settingsButton.Left-gap-icon,Look.S(5),icon,icon);
            PinButton.SetBounds(last.Left-gap-icon,Look.S(5),icon,icon);
            title.SetBounds(p,Look.S(10),Math.Max(Look.S(40),PinButton.Left-p-Look.S(8)),Look.S(22));
            int y=Look.S(6);
            if(imageRow.Visible) { imageRow.SetBounds(p,y,w-p*2,Look.S(64)); y+=Look.S(72); }
            if(quoteRow.Visible) {
                int chipWidth=Math.Min(Look.S(230),(w-p*2-SystemInformation.VerticalScrollBarWidth-Look.S(18))/2);
                foreach(Control chip in quoteRow.Controls) chip.Width=chipWidth;
                int rows=Math.Min(2,(Session.Quotes.Count+1)/2);
                quoteRow.SetBounds(p,y,w-p*2,Look.S(rows*39+2)); y+=quoteRow.Height+Look.S(6);
            }
            bool hasDraft=!String.IsNullOrWhiteSpace(Composer.Text);
            listenDraft.Visible=hasDraft;
            int frameWidth=w-p*2,sendWidth=Look.S(32),actionHeight=Look.S(28),actionGap=Look.S(4);
            // Text uses the full upper area; a separate action band inside the
            // frame keeps modes on the left and sending/playback on the right.
            // Reserve playback even when empty, so the first typed character
            // cannot move Send or change the action band's number of rows.
            int actionsWidth=sendWidth+Look.S(28)+actionGap;
            int editorWidth=Math.Max(Look.S(60),frameWidth-composerFrame.Padding.Horizontal);
            int leftActionsWidth=0;
            for(int i=0;i<modes.Count;i++) {
                var button=modes[i];
                button.Width=Look.S(28);
                button.Height=actionHeight; button.Margin=new Padding(0,0,i==modes.Count-1 && imageRoot==null?0:gap,0);
                leftActionsWidth+=button.Width+button.Margin.Right;
            }
            addImage.Width=Math.Max(Look.S(54),TextRenderer.MeasureText(addImage.Text,addImage.Font,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Width+Look.S(addImage.IconWithText?32:12));
            addImage.Height=actionHeight; addImage.Margin=new Padding(0);
            if(imageRoot!=null) leftActionsWidth+=addImage.Width;
            bool separateActions=leftActionsWidth+actionsWidth+Look.S(8)>editorWidth;
            int modeWidth=separateActions?editorWidth:Math.Max(1,editorWidth-actionsWidth-Look.S(8));
            modeRow.WrapContents=true;
            // Count actual flow rows rather than assuming translated labels fit.
            int modeRows=1,occupied=0;
            foreach(Control control in modeRow.Controls) {
                if(control==addImage && imageRoot==null) continue;
                int itemWidth=control.Width+control.Margin.Horizontal;
                if(occupied>0 && occupied+itemWidth>modeWidth) { modeRows++; occupied=0; }
                occupied+=itemWidth;
            }
            int modeHeight=modeRows*actionHeight;
            int bandHeight=modeHeight+(separateActions?actionGap+actionHeight:0);
            // Ask the native editor for its actual wrapped lines after settling
            // width. No extra space or guessed scrollbar gutter is measured.
            int nativeEditorWidth=editorWidth+SystemInformation.VerticalScrollBarWidth;
            if(Composer.Width!=nativeEditorWidth) Composer.Width=nativeEditorWidth;
            int lines=Math.Max(1,Composer.GetLineFromCharIndex(Composer.TextLength)+1);
            int lineHeight=Composer.Font.Height;
            if(lines>1) {
                int second=Composer.GetFirstCharIndexFromLine(1);
                if(second>=0) {
                    int actual=Composer.GetPositionFromCharIndex(second).Y-Composer.GetPositionFromCharIndex(0).Y;
                    if(actual>0) lineHeight=actual;
                }
            }
            int maxLines=Math.Max(2,Math.Min(5,(ClientSize.Height/3)/Math.Max(1,lineHeight)));
            int textHeight=Math.Min(lines,maxLines)*lineHeight+Look.S(2);
            int editorHeight=textHeight;
            int inputHeight=editorHeight+composerFrame.Padding.Vertical+Look.S(6)+bandHeight;
            composerFrame.SetBounds(p,y,frameWidth,inputHeight);
            composerViewport.SetBounds(composerFrame.Padding.Left,composerFrame.Padding.Top,editorWidth,editorHeight);
            Composer.SetBounds(0,0,nativeEditorWidth,editorHeight);
            int bandTop=composerViewport.Bottom+Look.S(6);
            modeRow.SetBounds(composerFrame.Padding.Left,bandTop,modeWidth,modeHeight);
            int actionY=bandTop+(separateActions?modeHeight+actionGap:0);
            send.SetBounds(frameWidth-composerFrame.Padding.Right-sendWidth,actionY,sendWidth,actionHeight);
            listenDraft.SetBounds(send.Left-actionGap-Look.S(28),actionY,Look.S(28),actionHeight);
            y+=inputHeight+Look.S(6);
            bool hasNotice=notice.Text.Length>0;
            notice.Visible=hasNotice; stopVoice.Visible=speechActive;
            if(hasNotice || speechActive) {
                stopVoice.SetBounds(w-p-Look.S(28),y,Look.S(28),Look.S(28));
                notice.SetBounds(p,y,Math.Max(10,w-p*2-(speechActive?Look.S(36):0)),Look.S(28));
                y+=Look.S(32);
            }
            bottom.Height=y;
            if(!composerOnly && Width>0 && Height>0) Look.Shape(this,12);
        } finally { layoutBusy=false; }
        if(!composerOnly || bottom.Height!=previousBottomHeight || timelineHost.ClientSize!=previousViewport) {
            LayoutTimelineViewport(); LayoutTimeline();
        }
    }
    void LayoutTimelineViewport() {
        if(timelineHost.ClientSize.Width<=0 || timelineHost.ClientSize.Height<=0) return;
        // The native scroll gutter is clipped by this host. Its replacement is
        // a narrow, themed, fully draggable scrollbar; native scrolling remains.
        Timeline.SetBounds(0,0,timelineHost.ClientSize.Width+SystemInformation.VerticalScrollBarWidth,timelineHost.ClientSize.Height);
        scrollBar.SetBounds(Math.Max(0,timelineHost.ClientSize.Width-Look.S(8)),0,Look.S(8),timelineHost.ClientSize.Height);
        scrollBar.BringToFront(); SyncScrollBar();
    }
    void SyncScrollBar() {
        if(syncingScroll || IsDisposed) return; syncingScroll=true;
        try { scrollBar.Viewport=Timeline.ClientSize.Height; scrollBar.Maximum=Math.Max(0,Timeline.AutoScrollMinSize.Height-Timeline.ClientSize.Height); scrollBar.Value=-Timeline.AutoScrollPosition.Y; scrollBar.Visible=scrollBar.Maximum>0; scrollBar.Update(); }
        finally { syncingScroll=false; }
    }
    public void LayoutTimeline() {
        if(layoutBusy || IsDisposed) return; layoutBusy=true;
        try {
            TimelineLayoutCount++;
            int y=Look.S(4),scroll=-Timeline.AutoScrollPosition.Y;
            // Equal outside margins: the right margin contains the slim bar,
            // while the matching left margin keeps the reading column centered.
            int side=Look.S(8);
            int width=Math.Max(Look.S(100),timelineHost.ClientSize.Width-side*2);
            Timeline.SuspendLayout();
            foreach(var card in Cards) { if(card.Width!=width) { card.Width=width; card.Relayout(); } card.Location=new Point(side,y-scroll); y+=card.Height; }
            Timeline.AutoScrollMinSize=new Size(0,y+Look.S(8));
            empty.Visible=Cards.Count==0; empty.SetBounds(Look.S(20),Look.S(16),Math.Max(100,width-Look.S(40)),Math.Max(Look.S(140),Timeline.ClientSize.Height-Look.S(32)));
            Timeline.ResumeLayout();
        } finally { layoutBusy=false; }
        SyncScrollBar();
    }
    public string SelectedReading(out Rectangle bounds) {
        var card=Cards.OrderByDescending(c=>c.LastSelectionTime).FirstOrDefault();
        if(card!=null && card.Selected.Length>0) {
            bounds=Rectangle.Intersect(card.SelectedBounds,Timeline.RectangleToScreen(Timeline.ClientRectangle));
            if(bounds.IsEmpty) bounds=new Rectangle(Cursor.Position,new Size(1,1));
            return card.Selected;
        }
        bounds=Rectangle.Empty; return "";
    }
    public void OpenAt(Rectangle anchor) {
        if(!Visible) { var work=Screen.FromRectangle(anchor).WorkingArea; Location=Look.Below(Size,anchor,work,Look.S(8),true)??work.Location; }
        UiTrace.Write("workspace foreground="+WindowActivation.Show(this));
    }
    public void ClearReadingSelection(StudyText keep=null) {
        Quote.Hide();
        foreach(var card in Cards) foreach(var text in new[]{card.Original,card.Answer})
            if(text!=keep && text.SelectionLength>0) text.Select(text.SelectionStart,0);
    }
    public void FocusInput(bool resetMode) { Quote.Hide(); if(resetMode) SetMode(WorkMode.Explain); Composer.Focus(); }
    public void SetNotice(string text) { if(notice.Text==text) return; notice.Text=text; tips.SetToolTip(notice,text); LayoutAll(); }
    public void SetSpeaking(bool active) { if(speechActive==active) return; speechActive=active; LayoutAll(); }
    public void SaveNow() { Session.Draft=Composer.Text; Session.Scroll=-Timeline.AutoScrollPosition.Y; if(SaveSessionRequested!=null) SaveSessionRequested(); }
    void ScheduleSave() { if(closing) return; autosave.Stop(); autosave.Start(); }
    // Fired after any move or resize so the rectangle can be persisted.
    void SaveGeometry() {
        try {
            if(closing || WindowState!=FormWindowState.Normal || Width<=0 || Height<=0) return;
            if(GeometryChanged!=null) GeometryChanged(Left,Top,Width,Height);
        } catch { }
    }
    protected override bool ProcessCmdKey(ref Message msg,Keys keyData) {
        if(keyData==Keys.Escape) { Quote.Hide(); return true; }
        if(keyData==(Keys.Control|Keys.Add) || keyData==(Keys.Control|Keys.Oemplus)) { ChangeFont(1); return true; }
        if(keyData==(Keys.Control|Keys.Subtract) || keyData==(Keys.Control|Keys.OemMinus)) { ChangeFont(-1); return true; }
        return base.ProcessCmdKey(ref msg,keyData);
    }
    protected override void WndProc(ref Message m) {
        if(m.Msg==0x231) moving=true; // WM_ENTERSIZEMOVE
        if(m.Msg==0x232) { moving=false; SaveGeometry(); Invalidate(true); } // WM_EXITSIZEMOVE
        const int hitTest=0x84;
        if(m.Msg==hitTest) {
            base.WndProc(ref m);
            long packed=m.LParam.ToInt64(); Point p=PointToClient(new Point((short)(packed&0xffff),(short)((packed>>16)&0xffff)));
            int edge=Look.S(6),corner=Look.S(14);
            bool l=p.X<edge,r=p.X>=ClientSize.Width-edge,t=p.Y<edge,b=p.Y>=ClientSize.Height-edge;
            if((l||p.X<corner) && t || l && p.Y<corner) m.Result=new IntPtr(13);
            else if((r||p.X>=ClientSize.Width-corner) && t || r && p.Y<corner) m.Result=new IntPtr(14);
            else if((l||p.X<corner) && b || l && p.Y>=ClientSize.Height-corner) m.Result=new IntPtr(16);
            else if((r||p.X>=ClientSize.Width-corner) && b || r && p.Y>=ClientSize.Height-corner) m.Result=new IntPtr(17);
            else if(l) m.Result=new IntPtr(10); else if(r) m.Result=new IntPtr(11); else if(t) m.Result=new IntPtr(12); else if(b) m.Result=new IntPtr(15);
            return;
        }
        base.WndProc(ref m);
    }
    protected override void Dispose(bool disposing) {
        if(disposing) { shift.Dispose(); selection.Dispose(); autosave.Dispose(); Quote.Dispose(); tips.Dispose(); }
        base.Dispose(disposing);
    }
}
public sealed class QuickDictionary : Form {
    public event Action SaveRequested;
    readonly SoftButton save=new SoftButton("☆ Save",false);
    public bool Saved { get; private set; }
    public string QueryText { get; private set; }
    public string AnswerText { get; private set; }
    public void SetSaved(bool value,bool ready=true) { Saved=value; save.Enabled=ready; save.Text=UiLanguage.T(value?"★ Saved":"☆ Save",value?"★ 已收藏":"☆ 收藏"); }
    public readonly StudyText Body=new StudyText();
    readonly Label title=new Label(),status=new Label();
    readonly SoftButton close=new SoftButton("×",false);
    readonly SoftButton listen=new SoftButton("▶",false);
    public event Action Dismissed;
    public event Action<string> SpeechRequested;
    public event Action SpeechStopRequested;
    bool speaking;
    readonly Timer renderTimer=new Timer { Interval=70 };
    string pendingAnswer;
    Rectangle lookupArea,lookupAnchor;
    bool finalSized;
    public string Selected { get { return Body.SelectedText; } }
    public QuickDictionary(float points) {
        Text="TermLens · Dictionary"; FormBorderStyle=FormBorderStyle.None; ControlBox=false;
        ShowInTaskbar=false; TopMost=true; StartPosition=FormStartPosition.Manual; AutoScaleMode=AutoScaleMode.None;
        MinimumSize=new Size(Look.S(300),Look.S(180)); Size=new Size(Look.S(380),Look.S(255));
        title.Font=new Font("Segoe UI",9,FontStyle.Bold); title.AutoEllipsis=true;
        Body.ApplyReadingFont(Math.Max(10,Math.Min(18,points))); Body.ScrollBars=RichTextBoxScrollBars.Vertical;
        DoubleBuffered=true;
        renderTimer.Tick+=delegate { renderTimer.Stop(); FlushAnswer(false); };
        status.Font=new Font("Segoe UI",8); status.AutoEllipsis=true;
        Controls.AddRange(new Control[]{title,Body,status,close,listen,save});
        save.Click+=delegate { if(SaveRequested!=null) SaveRequested(); };
        listen.Click+=delegate { if(speaking) { if(SpeechStopRequested!=null) SpeechStopRequested(); } else if(SpeechRequested!=null) SpeechRequested(String.IsNullOrWhiteSpace(Body.SelectedText)?title.Text:Body.SelectedText); };
        listen.AccessibleName=UiLanguage.T("Listen to this word","朗读当前词语");
        close.Click+=delegate { Close(); };
        Deactivate+=delegate {
            // Wait until the current focus change settles; Show/Activate can briefly deactivate.
            if(IsDisposed || Disposing) return;
            BeginInvoke((Action)delegate { if(!IsDisposed && Visible && Native.GetForegroundWindow()!=Handle) { UiTrace.Write("dictionary dismissed: focus moved"); Close(); } });
        };
        FormClosed+=delegate { if(Dismissed!=null) Dismissed(); };
        Resize+=delegate { Arrange(); }; Theme.Apply(this); Arrange();
    }
    public void Begin(string text,Rectangle anchor) {
        QueryText=text??""; AnswerText="";
        SetSaved(false,false);
        renderTimer.Stop(); pendingAnswer=null; finalSized=false;
        title.Text=text; Body.SetContent(""); status.Text=UiLanguage.T("Answering…","正在解释…");
        lookupAnchor=anchor; lookupArea=Screen.FromRectangle(anchor).WorkingArea;
        Size=new Size(Look.S(380),Math.Min(lookupArea.Height/2,Look.S(255)));
        Location=Look.Below(Size,anchor,lookupArea,Look.S(7),true)??lookupArea.Location;
        if(!Visible) Show(); Activate(); Native.SetForegroundWindow(Handle); Body.Focus();
    }
    public void UpdateAnswer(string text,bool complete) {
        if(IsDisposed) return; pendingAnswer=text??""; AnswerText=pendingAnswer;
        if(complete) { renderTimer.Stop(); FlushAnswer(true); }
        else if(!renderTimer.Enabled) renderTimer.Start();
    }
    void FlushAnswer(bool complete) {
        if(IsDisposed || pendingAnswer==null) return;
        Body.SetMarkdown(pendingAnswer); pendingAnswer=null;
        status.Text=UiLanguage.T(complete?"Click outside to close · F13 to look up a selection":"Answering…",complete?"点击外部关闭 · 选词后 F13 继续查词":"正在解释…");
        if(complete && !finalSized) {
            finalSized=true;
            int limit=Math.Min(lookupArea.Height/2,lookupArea.Bottom-Top);
            // If placed above the selected word, never grow downward over it.
            if(Bottom<=lookupAnchor.Top && Left<lookupAnchor.Right && Right>lookupAnchor.Left) limit=Math.Min(limit,lookupAnchor.Top-Look.S(7)-Top);
            int ideal=Body.ContentHeight(ClientSize.Width-Look.S(32))+Look.S(88);
            Height=Math.Max(MinimumSize.Height,Math.Min(limit,Math.Max(Look.S(180),ideal)));
            Arrange();
        }
    }
    public void Fail(string error) { if(IsDisposed) return; renderTimer.Stop(); FlushAnswer(false); status.Text=error; status.ForeColor=Look.Error; if(Body.TextLength==0) Body.Text=error; }
    public void SetSpeaking(bool active,string message) { if(IsDisposed) return; speaking=active; listen.Text=active?"■":"▶"; if(message!=null) status.Text=message; }
    public void ApplyAppearance(Settings value) { WindowAppearance.Apply(this,value,true); }
    void Arrange() {
        title.SetBounds(Look.S(16),Look.S(14),ClientSize.Width-Look.S(108),Look.S(24));
        listen.SetBounds(ClientSize.Width-Look.S(88),Look.S(5),Look.S(38),Look.S(38));
        close.SetBounds(ClientSize.Width-Look.S(45),Look.S(5),Look.S(38),Look.S(38));
        Body.SetBounds(Look.S(16),Look.S(50),ClientSize.Width-Look.S(32),Math.Max(20,ClientSize.Height-Look.S(84)));
        save.SetBounds(ClientSize.Width-Look.S(95),ClientSize.Height-Look.S(34),Look.S(80),Look.S(28));
        status.SetBounds(Look.S(16),ClientSize.Height-Look.S(26),ClientSize.Width-Look.S(117),Look.S(20));
        if(Width>0 && Height>0) Look.Shape(this,10);
    }
    protected override bool ProcessCmdKey(ref Message msg,Keys keyData) { if(keyData==Keys.Escape) { Close(); return true; } return base.ProcessCmdKey(ref msg,keyData); }
    protected override void Dispose(bool disposing) { if(disposing) renderTimer.Dispose(); base.Dispose(disposing); }
}
}


