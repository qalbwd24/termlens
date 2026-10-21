using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace TermLens {
public static class Look {
    public static Color Canvas { get { return Theme.Dark?Color.FromArgb(19,25,25):Color.FromArgb(239,243,240); } }
    public static Color Field { get { return Theme.Dark?Color.FromArgb(36,45,43):Color.FromArgb(245,248,245); } }
    public static Color Ink { get { return Theme.Dark?Color.FromArgb(231,235,233):Color.FromArgb(35,42,48); } }
    public static Color Muted { get { return Theme.Dark?Color.FromArgb(160,172,167):Color.FromArgb(116,125,130); } }
    public static Color Accent { get { return Theme.Dark?Color.FromArgb(100,206,182):Color.FromArgb(25,112,100); } }
    public static Color Surface { get { return Theme.Dark?Color.FromArgb(28,33,32):Color.FromArgb(253,253,251); } }
    public static Color Border { get { return Theme.Dark?Color.FromArgb(66,77,72):Color.FromArgb(224,228,224); } }
    public static Color Error { get { return Theme.Dark?Color.FromArgb(255,162,142):Color.FromArgb(172,73,58); } }
    static float scale=0;
    public static float Scale { get { if(scale==0) using(var g=Graphics.FromHwnd(IntPtr.Zero)) scale=g.DpiX/96f; return scale; } }
    public static int S(float n) { return (int)Math.Round(n*Scale); }
    public static GraphicsPath Round(RectangleF r,float radius) {
        var p=new GraphicsPath(); float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));
        p.AddArc(r.Left,r.Top,d,d,180,90); p.AddArc(r.Right-d,r.Top,d,d,270,90); p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); p.AddArc(r.Left,r.Bottom-d,d,d,90,90); p.CloseFigure(); return p;
    }
    sealed class ShapeState { public Size Size; public int Radius; public Region Region; }
    static readonly ConditionalWeakTable<Form,ShapeState> shapes=new ConditionalWeakTable<Form,ShapeState>();
    public static void Shape(Form form,int radius) {
        if(form.Width<=0 || form.Height<=0) return;
        var state=shapes.GetValue(form,delegate(Form ignored) { return new ShapeState(); });
        if(state.Size==form.Size && state.Radius==radius && Object.ReferenceEquals(state.Region,form.Region)) return;
        using(var p=Round(new Rectangle(0,0,form.Width,form.Height),S(radius))) {
            var old=form.Region; form.Region=new Region(p); if(old!=null) old.Dispose();
            state.Size=form.Size; state.Radius=radius; state.Region=form.Region;
        }
    }
    // Prefer below the full selection. At a screen edge, try a side or above.
    // Never clamp a button back on top of the selected text.
    public static Point? Below(Size size,Rectangle selection,Rectangle work,int gap,bool allowOverlap) {
        int x=Math.Max(work.Left,Math.Min(selection.Left,work.Right-size.Width));
        var candidates=new[] {
            new Point(x,selection.Bottom+gap),
            new Point(selection.Right+gap,Math.Max(work.Top,Math.Min(selection.Top,work.Bottom-size.Height))),
            new Point(selection.Left-gap-size.Width,Math.Max(work.Top,Math.Min(selection.Top,work.Bottom-size.Height))),
            new Point(x,selection.Top-gap-size.Height)
        };
        foreach(var pt in candidates) { var r=new Rectangle(pt,size); if(work.Contains(r) && !r.IntersectsWith(selection)) return pt; }
        if(!allowOverlap) return null;
        return new Point(x,Math.Max(work.Top,Math.Min(selection.Bottom+gap,work.Bottom-size.Height)));
    }
}
public sealed class SoftButton : Button {
    bool hover;
    public bool Primary;
    string iconName;
    public string IconName { get { return iconName; } set { iconName=value; Invalidate(); } }
    public bool Quiet { get; set; }
    bool iconWithText;
    public bool IconWithText { get { return iconWithText; } set { iconWithText=value; Invalidate(); } }
    public SoftButton(string text,bool primary,bool focusable=true) {
        Text=text; Primary=primary; Cursor=Cursors.Hand; Font=new Font("Microsoft YaHei UI",9,FontStyle.Regular);
        FlatStyle=FlatStyle.Flat; FlatAppearance.BorderSize=0; BackColor=Color.Transparent;
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.SupportsTransparentBackColor,true);
        Height=Look.S(30); Width=Look.S(70); TabStop=focusable;
        if(!focusable) SetStyle(ControlStyles.Selectable,false);
        MouseEnter+=delegate { hover=true; Invalidate(); }; MouseLeave+=delegate { hover=false; Invalidate(); };
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaintBackground(e);
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        Color fill=Primary?(hover?Color.FromArgb(230,90,145,130):Color.FromArgb(225,25,112,100)):(Theme.Dark?(hover?Color.FromArgb(190,62,76,68):Color.FromArgb(175,43,53,48)):(hover?Color.FromArgb(228,230,237,232):Color.FromArgb(220,239,243,239)));
        if(!Enabled) fill=Theme.Dark?Color.FromArgb(35,42,38):Color.FromArgb(243,245,242);
        if(!Quiet || Primary || hover || Focused)
            using(var path=Look.Round(new RectangleF(0,0,Width-1,Height-1),Look.S(7))) using(var b=new SolidBrush(fill)) e.Graphics.FillPath(b,path);
        Color ink=!Enabled?Look.Muted:Primary?Color.White:Look.Ink;
        if(!String.IsNullOrEmpty(IconName) && IconWithText) {
            int iconSize=Look.S(16),gap=Look.S(4),padding=Look.S(6);
            var textFlags=TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine;
            int textWidth=TextRenderer.MeasureText(e.Graphics,Text,Font,Size.Empty,textFlags).Width;
            textWidth=Math.Min(textWidth,Math.Max(0,ClientSize.Width-padding*2-iconSize-gap));
            int start=Math.Max(padding,(ClientSize.Width-iconSize-gap-textWidth)/2);
            V2Icons.Draw(e.Graphics,IconName,new Rectangle(start,(ClientSize.Height-iconSize)/2,iconSize,iconSize),ink,16f,0);
            TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(start+iconSize+gap,0,textWidth,ClientSize.Height),ink,textFlags|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
        }
        else if(!String.IsNullOrEmpty(IconName)) V2Icons.Draw(e.Graphics,IconName,ClientRectangle,ink);
        else TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix);
        if(Focused) ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-4,-4));
    }
}
public sealed class ReadingBox : RichTextBox {
    public event Action<int> ZoomRequested;
    protected override void WndProc(ref Message m) {
        if(m.Msg==0x20A && (m.WParam.ToInt64()&8)!=0) {
            int delta=(short)((m.WParam.ToInt64()>>16)&0xffff);
            if(ZoomRequested!=null && delta!=0) ZoomRequested(delta>0?1:-1);
            return; // Do not let RichEdit apply its temporary, unremembered zoom.
        }
        base.WndProc(ref m);
    }
}
public sealed class ResultPopup : Form {
    public event Action SettingsRequested, SaveRequested, StopRequested;
    public event Action<string> QuestionRequested, WordRequested, RefineRequested;
    public event Action<Rectangle> SelectionOffered;
    public event Action ParentRequested;
    public event Action<string> SpeechRequested;
    public event Action SpeechStopRequested;
    public event Action UserClosed;
    public event Action<float> FontSizeChanged;
    public event Action<Size> PreferredSizeChanged;
    readonly Label brand, status;
    readonly RichTextBox term, answer;
    readonly SoftButton close, pin, fit, save, copy, settings, stop, ask, back, listen, refine;
    string speechSelection="";
    bool speaking;
    readonly TextBox question;
    readonly FieldFrame questionFrame;
    readonly Label questionLabel;
    readonly ContextMenuStrip wordMenu=new ContextMenuStrip();
    Font normal=new Font("Microsoft YaHei UI",11), bold=new Font("Microsoft YaHei UI",11,FontStyle.Bold);
    Font heading=new Font("Microsoft YaHei UI",15);
    int preferredWidth=480, preferredHeight;
    public float ReadingFontSize { get { return normal.Size; } }
    public bool IsComplete { get { return !busy && !error && content.Length>0; } }
    public string RawAnswer { get { FlushAnswer(); return content; } }
    public string DraftQuestion { get { return question.Text; } set { question.Text=value??""; } }
    readonly Timer renderTimer=new Timer { Interval=80 };
    readonly ToolTip tips=new ToolTip();
    Rectangle anchor;
    bool pinned, busy, error;
    string content="";
    bool rendering;
    long openedAt;
    bool hasLookup;
    bool teacherMode, branchBusy;
    bool sizing, manualSize, manualPosition;
    int naturalHeight, originalHeight;
    string pending;
    public string OfferedSelection { get; private set; }
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd,int msg,IntPtr wp,IntPtr lp);
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    public string AnswerText { get { return answer.Text; } }
    public string SelectedAnswer { get { return term.ContainsFocus?term.SelectedText:answer.ContainsFocus?answer.SelectedText:""; } }
    public bool CanStudy { get { return !busy && !error && !branchBusy && content.Length>0; } }
    public string OriginalText { get { return term.Text; } }
    public int OriginalContentHeight { get { return originalHeight; } }
    public int OriginalReadingHeight { get { return term.ClientSize.Height; } }
    public bool IsPinned { get { return pinned; } }
    public bool HasLookup { get { return hasLookup; } }
    public int ContentHeight { get { return naturalHeight; } }
    public int ReadingHeight { get { return answer.ClientSize.Height; } }
    public ResultPopup() {
        AutoScaleMode=AutoScaleMode.None; FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false; TopMost=true;
        StartPosition=FormStartPosition.Manual; BackColor=Look.Surface; ForeColor=Look.Ink; Font=normal;
        DoubleBuffered=true; SetStyle(ControlStyles.ResizeRedraw,true); Padding=new Padding(Look.S(24)); Text="TermLens lookup";
        brand=new Label { Text="TERMLENS",Font=new Font("Segoe UI",8,FontStyle.Bold),ForeColor=Look.Accent,AutoSize=false };
        term=new ReadingBox { ReadOnly=true,BorderStyle=BorderStyle.None,BackColor=Look.Surface,ForeColor=Look.Ink,Font=heading,DetectUrls=false,WordWrap=true,ScrollBars=RichTextBoxScrollBars.Vertical,HideSelection=true,TabStop=true };
        term.ContentsResized+=delegate(object sender,ContentsResizedEventArgs e) { originalHeight=e.NewRectangle.Height; };
        status=new Label { Font=new Font("Segoe UI",8),ForeColor=Look.Muted,UseMnemonic=false,AutoEllipsis=true };
        answer=new ReadingBox { ReadOnly=true,BorderStyle=BorderStyle.None,BackColor=Look.Surface,ForeColor=Look.Ink,Font=normal,DetectUrls=false,ScrollBars=RichTextBoxScrollBars.Vertical,TabStop=true,HideSelection=true,WordWrap=true };
        answer.ContentsResized+=delegate(object sender,ContentsResizedEventArgs e) { naturalHeight=e.NewRectangle.Height; };
        close=new SoftButton("×",false); pin=new SoftButton("Pin",false); fit=new SoftButton("Fit",false); save=new SoftButton("☆ Save",true); copy=new SoftButton("Copy",false); settings=new SoftButton("Settings",false); stop=new SoftButton("Stop",false);
        back=new SoftButton("Back",false) { Visible=false }; back.Click+=delegate { if(ParentRequested!=null) ParentRequested(); };
        listen=new SoftButton("Listen",false) { Enabled=false };
        listen.Click+=delegate { if(speaking) { if(SpeechStopRequested!=null) SpeechStopRequested(); } else if(SpeechRequested!=null) SpeechRequested(speechSelection.Length>0?speechSelection:answer.Text); };
        tips.SetToolTip(listen,"Read selected text, or the whole answer. Click again to stop.");
        term.SelectionChanged+=delegate { if(!rendering && term.ContainsFocus) speechSelection=term.SelectedText; };
        answer.SelectionChanged+=delegate { if(!rendering && answer.ContainsFocus) speechSelection=answer.SelectedText; };
        term.Enter+=delegate { speechSelection=term.SelectedText; }; answer.Enter+=delegate { speechSelection=answer.SelectedText; };
        questionLabel=new Label { Text="Teacher · Enter to send",Font=new Font("Segoe UI",8),ForeColor=Look.Muted,AutoEllipsis=true };
        refine=new SoftButton("Refine wording",false) { Enabled=false };
        refine.Click+=delegate { if(CanStudy && RefineRequested!=null) RefineRequested(OriginalText); };
        tips.SetToolTip(refine,"Improve the original text at the top of this window. Opens a separate result.");
        question=new ComposerBox { MaxLength=4000,Font=new Font("Microsoft YaHei UI",10) };
        questionFrame=new FieldFrame(question,true) { Dock=DockStyle.None,Padding=new Padding(Look.S(12),Look.S(8),Look.S(12),Look.S(8)) };
        ask=new SoftButton("Ask",true) { Enabled=false }; ask.Click+=delegate { SubmitQuestion(); };
        question.TextChanged+=delegate { UpdateStudyActions(); };
        ((ComposerBox)question).Submitted+=SubmitQuestion;
        tips.SetToolTip(question,"Enter to send · Shift+Enter for a new line");
        string menuSelection="";
        wordMenu.Items.Add("Explain selected text (F13)",null,delegate { if(CanStudy && menuSelection.Length>0 && WordRequested!=null) WordRequested(menuSelection); });
        wordMenu.Items.Add("Read selected text",null,delegate { if(menuSelection.Length>0 && SpeechRequested!=null) SpeechRequested(menuSelection); });
        wordMenu.Opening+=delegate(object sender,System.ComponentModel.CancelEventArgs e) { var rich=wordMenu.SourceControl as RichTextBox; menuSelection=rich==null?"":rich.SelectedText; e.Cancel=!CanStudy || menuSelection.Length==0; };
        term.ContextMenuStrip=wordMenu; answer.ContextMenuStrip=wordMenu;
        term.MouseUp+=OfferSelection; answer.MouseUp+=OfferSelection;
        term.HandleCreated+=delegate { DisableAutomaticFont(term); }; answer.HandleCreated+=delegate { DisableAutomaticFont(answer); };
        close.Click+=delegate { CloseReading(); };
        pin.Visible=false; pinned=true;
        ((ReadingBox)term).ZoomRequested+=ChangeFontSize; ((ReadingBox)answer).ZoomRequested+=ChangeFontSize;
        fit.Click+=delegate { FitToAnswer(); };
        tips.SetToolTip(fit,"Fit window to answer"); tips.SetToolTip(pin,"Keep this answer open"); tips.SetToolTip(brand,"Drag to move the window");
        save.Click+=delegate { if(SaveRequested!=null) SaveRequested(); };
        copy.Click+=delegate { try { if(content.Length>0) Clipboard.SetText(answer.Text); SetStatus("Copied",false); } catch { SetStatus("Clipboard is busy. Try again.",true); } };
        settings.Click+=delegate { if(SettingsRequested!=null) SettingsRequested(); };
        stop.Click+=delegate { if(StopRequested!=null) StopRequested(); };
        Controls.AddRange(new Control[] { brand,term,status,answer,close,pin,fit,save,copy,settings,stop,questionLabel,questionFrame,ask,back,listen,refine });
        MouseEventHandler drag=delegate(object sender,MouseEventArgs e) { if(e.Button==MouseButtons.Left) { manualPosition=true; ReleaseCapture(); SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero); } };
        brand.MouseDown+=drag;
        SizeChanged+=delegate { if(Width>0 && Height>0) { Look.Shape(this,14); LayoutContent(); Invalidate(true); } };
        MinimumSize=new Size(Look.S(360),Look.S(340));
        Size=new Size(Look.S(440),Look.S(360));
        renderTimer.Tick+=delegate { renderTimer.Stop(); FlushAnswer(); };
        VisibleChanged+=delegate { UiTrace.Write("popup visible="+Visible+" bounds="+Bounds); if(Visible) Theme.Apply(this); else if(SpeechStopRequested!=null) SpeechStopRequested(); };
        Theme.Apply(this);
    }
    // Kept as a separate entry point so the legacy lookup card uses the same
    // bottom-layer wallpaper host as the workspace window during migration.
    public void ApplyAppearance(Settings value) { WindowAppearance.Apply(this,value,false); }
    public void CloseReading() { FlushAnswer(); if(UserClosed!=null) UserClosed(); if(busy && StopRequested!=null) StopRequested(); Hide(); }
    void ChangeFontSize(int step) { float value=Math.Max(9,Math.Min(28,normal.Size+step)); ApplyReading(value,preferredWidth,preferredHeight); if(FontSizeChanged!=null) FontSizeChanged(value); }
    public void ApplyReading(float points,int width,int height) {
        preferredWidth=Math.Max(360,Math.Min(1400,width)); preferredHeight=Math.Max(0,Math.Min(2000,height));
        points=Math.Max(9,Math.Min(28,points));
        if(Math.Abs(normal.Size-points)>.01) {
            var oldNormal=normal; var oldBold=bold; var oldHeading=heading;
            normal=new Font("Microsoft YaHei UI",points); bold=new Font("Microsoft YaHei UI",points,FontStyle.Bold); heading=new Font("Microsoft YaHei UI",points+4);
            Font=normal; term.Font=term.TextLength>90?normal:heading; answer.Font=normal;
            Render(content); oldNormal.Dispose(); oldBold.Dispose(); oldHeading.Dispose();
        }
        manualSize=false;
        var work=Screen.FromRectangle(Bounds).WorkingArea;
        Width=Math.Min(Look.S(preferredWidth),work.Width-Look.S(32));
        if(hasLookup) ResizeForContent();
    }
    static void DisableAutomaticFont(RichTextBox box) {
        // RichEdit otherwise carries the CJK fallback font into following Latin text.
        long options=SendMessage(box.Handle,0x479,IntPtr.Zero,IntPtr.Zero).ToInt64();
        SendMessage(box.Handle,0x478,IntPtr.Zero,new IntPtr(options & ~2L));
    }
    public void RefreshTheme() {
        brand.ForeColor=Look.Accent; status.ForeColor=error?Look.Error:Look.Muted; questionLabel.ForeColor=Look.Muted;
        wordMenu.BackColor=Look.Surface; wordMenu.ForeColor=Look.Ink;
        // Recolor labels without rebuilding text, so selection and scroll stay in place.
        rendering=true; int start=answer.SelectionStart,length=answer.SelectionLength;
        foreach(Match m in Regex.Matches(answer.Text,@"(?m)^(?:•\s*)?(Meaning|Example):")) { answer.Select(m.Index,m.Length); answer.SelectionColor=Look.Accent; }
        answer.Select(start,length); rendering=false;
    }
    public void SetSpeaking(bool value,string message) { speaking=value; listen.Text=value?"Stop audio":"Listen"; listen.Enabled=value || (!busy && !error && content.Length>0); if(message!=null) SetStatus(message,false); }
    public void SetStudyMode(bool teacher,bool hasParent) { teacherMode=teacher; brand.Text=teacher?"TEACHER":"TERMLENS"; back.Visible=hasParent; save.Visible=!teacher; }
    public void SetRefineMode() { brand.Text="REFINE"; }
    public void SetBranchBusy(bool value) { branchBusy=value; UpdateStudyActions(); }
    public void KeepOpen() { pinned=true; pin.Text="Pinned"; }
    void UpdateStudyActions() { ask.Enabled=CanStudy && !String.IsNullOrWhiteSpace(question.Text); refine.Enabled=CanStudy && !String.IsNullOrWhiteSpace(OriginalText); }
    void SubmitQuestion() { if(CanStudy && !String.IsNullOrWhiteSpace(question.Text) && QuestionRequested!=null) QuestionRequested(question.Text.Trim()); }
    void OfferSelection(object sender,MouseEventArgs e) {
        var rich=sender as RichTextBox;
        if(e.Button!=MouseButtons.Left || !CanStudy || rich==null || rich.SelectionLength==0 || SelectionOffered==null) return;
        OfferedSelection=rich.SelectedText;
        var a=rich.GetPositionFromCharIndex(rich.SelectionStart); var b=rich.GetPositionFromCharIndex(rich.SelectionStart+rich.SelectionLength-1);
        int left=a.Y==b.Y?Math.Min(a.X,b.X):0, right=a.Y==b.Y?Math.Max(a.X,b.X)+rich.Font.Height:rich.ClientSize.Width;
        var top=rich.PointToScreen(new Point(left,Math.Min(a.Y,b.Y)));
        SelectionOffered(new Rectangle(top,new Size(Math.Max(1,right-left),Math.Abs(b.Y-a.Y)+rich.Font.Height+2)));
    }
    public void PlaceBeside(Rectangle parent) {
        var work=Screen.FromRectangle(parent).WorkingArea; int gap=Look.S(12);
        int x=parent.Right+gap;
        if(x+Width>work.Right) x=parent.Left-gap-Width;
        if(x<work.Left) x=Math.Min(work.Right-Width,parent.Left+Look.S(32));
        Location=new Point(Math.Max(work.Left,x),Math.Max(work.Top,Math.Min(parent.Top+Look.S(12),work.Bottom-Height)));
        manualPosition=true;
    }
    protected override CreateParams CreateParams { get { var p=base.CreateParams; p.ExStyle|=0x80; p.ClassStyle|=0x20000; return p; } }
    protected override bool ProcessCmdKey(ref Message msg,Keys keyData) {
        if(keyData==Keys.Escape) { CloseReading(); return true; }
        if(refine.ContainsFocus && (keyData==Keys.Enter || keyData==(Keys.Control|Keys.Enter))) { SubmitQuestion(); question.Focus(); return true; }
        return base.ProcessCmdKey(ref msg,keyData);
    }
    protected override void WndProc(ref Message m) {
        if(m.Msg==0x84) {
            long xy=m.LParam.ToInt64(); var pt=PointToClient(new Point((short)(xy&0xffff),(short)((xy>>16)&0xffff)));
            if(pt.X>=Width-Look.S(18) && pt.Y>=Height-Look.S(18)) { m.Result=new IntPtr(17); return; }
            int edge=Look.S(7); bool left=pt.X<edge,right=pt.X>=Width-edge,top=pt.Y<edge,bottom=pt.Y>=Height-edge;
            int hit=top?(left?13:right?14:12):bottom?(left?16:right?17:15):left?10:right?11:0;
            if(hit!=0) { m.Result=new IntPtr(hit); return; }
            if(pt.Y<Look.S(44)) { m.Result=new IntPtr(2); return; }
        }
        if(m.Msg==0x214) { manualSize=true; manualPosition=true; }
        if(m.Msg==0x216) manualPosition=true;
        base.WndProc(ref m);
        if(m.Msg==0x232 && manualSize) {
            preferredWidth=(int)Math.Round(Width/Look.Scale); preferredHeight=(int)Math.Round(Height/Look.Scale);
            if(PreferredSizeChanged!=null) PreferredSizeChanged(new Size(preferredWidth,preferredHeight));
        }
    }
    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.Clear(BackColor); base.OnPaint(e); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(var p=Look.Round(new RectangleF(0.5f,0.5f,Width-1.5f,Height-1.5f),Look.S(14))) using(var pen=new Pen(Look.Border)) e.Graphics.DrawPath(pen,p);
        using(var pen=new Pen(Look.Border)) e.Graphics.DrawLine(pen,Look.S(24),Height-Look.S(184),Width-Look.S(24),Height-Look.S(184));
        using(var pen=new Pen(Look.Muted)) for(int i=0;i<3;i++) e.Graphics.DrawLine(pen,Width-Look.S(9+i*3),Height-Look.S(9),Width-Look.S(9),Height-Look.S(9+i*3));
    }
    void LayoutContent() {
        if(answer==null) return;
        int pad=Look.S(24), width=ClientSize.Width-2*pad;
        brand.SetBounds(pad,Look.S(20),Look.S(110),Look.S(18));
        back.SetBounds(Look.S(132),Look.S(14),Look.S(52),Look.S(28));
        close.SetBounds(Width-pad-Look.S(28),Look.S(14),Look.S(28),Look.S(28));
        pin.SetBounds(close.Left-Look.S(60),Look.S(14),Look.S(54),Look.S(28));
        fit.SetBounds(close.Left-Look.S(54),Look.S(14),Look.S(48),Look.S(28));
        term.Width=width;
        if(term.IsHandleCreated) SendMessage(term.Handle,0x441,IntPtr.Zero,IntPtr.Zero);
        var work=Screen.FromRectangle(manualPosition?Bounds:anchor).WorkingArea;
        int sourceLimit=Math.Max(Look.S(80),work.Height-Look.S(320)-Math.Min(naturalHeight,Look.S(280)));
        int titleHeight=Math.Max(Look.S(28),Math.Min(sourceLimit,originalHeight+Look.S(6)));
        term.SetBounds(pad,Look.S(54),width,titleHeight);
        int bodyTop=term.Bottom+Look.S(20);
        int minimumHeight=Math.Max(Look.S(340),bodyTop+Look.S(236));
        if(MinimumSize.Height!=minimumHeight) MinimumSize=new Size(Look.S(360),minimumHeight);
        answer.SetBounds(pad,bodyTop,width,Math.Max(Look.S(24),Height-Look.S(202)-bodyTop));
        questionLabel.SetBounds(pad,Height-Look.S(173),width-Look.S(128),Look.S(18));
        refine.SetBounds(Width-pad-Look.S(120),Height-Look.S(180),Look.S(120),Look.S(28));
        questionFrame.SetBounds(pad,Height-Look.S(149),width-Look.S(76),Look.S(50));
        ask.SetBounds(questionFrame.Right+Look.S(8),questionFrame.Top,Look.S(68),Look.S(50));
        status.SetBounds(pad,Height-Look.S(72),width-Look.S(60),Look.S(19));
        stop.SetBounds(Width-pad-Look.S(52),Height-Look.S(77),Look.S(52),Look.S(26));
        int y=Height-Look.S(44);
        copy.SetBounds(pad,y,Look.S(52),Look.S(30)); save.SetBounds(copy.Right+Look.S(8),y,Look.S(72),Look.S(30));
        listen.SetBounds(save.Right+Look.S(8),y,Look.S(84),Look.S(30));
        settings.SetBounds(Width-pad-Look.S(64),y,Look.S(64),Look.S(30));
    }
    public void BeginLookup(string text,Rectangle selection) {
        UiTrace.Write("popup begin");
        if(SpeechStopRequested!=null) SpeechStopRequested(); speechSelection=""; SetSpeaking(false,null); listen.Enabled=false;
        hasLookup=true; manualSize=false; manualPosition=false; renderTimer.Stop(); pending=null; branchBusy=false; question.Clear(); OfferedSelection="";
        anchor=selection; term.Font=text.Length>90?normal:heading; term.Text=text; term.Select(0,0); term.ScrollToCaret();
        busy=true; error=false; content=""; answer.Text=""; save.Enabled=false; copy.Enabled=false; stop.Visible=true; UpdateStudyActions();
        status.Text="Reading your selection…"; status.ForeColor=Look.Muted;
        var work=Screen.FromRectangle(anchor).WorkingArea;
        MaximumSize=new Size(work.Width-Look.S(24),work.Height-Look.S(24));
        Size=new Size(Math.Min(Look.S(preferredWidth),MaximumSize.Width),Math.Min(Look.S(Math.Max(360,preferredHeight)),MaximumSize.Height)); LayoutContent(); ShowExisting();
        manualPosition=true; // Keep the card's anchor stable as streamed text grows.
    }
    public void ShowExisting() {
        if(!hasLookup) return;
        openedAt=System.Diagnostics.Stopwatch.GetTimestamp();
        Place(); Show(); LayoutContent(); ResizeForContent(); Activate(); Native.SetForegroundWindow(Handle); answer.Focus();
    }
    public void PointerDown(Point point) { PointerDown(point,System.Diagnostics.Stopwatch.GetTimestamp()); }
    public void PointerDown(Point point,long occurredAt) {
        // Reading windows close only through an explicit close action.
    }
    public void SetAnswer(string text) {
        pending=text;
        if(!busy) FlushAnswer(); else if(!renderTimer.Enabled) renderTimer.Start();
    }
    public void FlushAnswer() { if(pending==null) return; content=pending; pending=null; copy.Enabled=content.Length>0; Render(content); ResizeForContent(); }
    public void SetStatus(string text,bool isError) { status.Text=text; error=isError; status.ForeColor=isError?Look.Error:Look.Muted; LayoutContent(); }
    public void Complete(bool saved) { renderTimer.Stop(); FlushAnswer(); busy=false; stop.Visible=false; save.Enabled=!teacherMode; listen.Enabled=true; SetSaved(saved); SetStatus("Ready  ·  Select a word or ask below",false); UpdateStudyActions(); ResizeForContent(); }
    public void Failed(string message) { renderTimer.Stop(); FlushAnswer(); busy=false; stop.Visible=false; save.Enabled=false; if(content.Length==0) { content=message; Render(message); } SetStatus("Could not finish  ·  Try again",true); tips.SetToolTip(status,message); UpdateStudyActions(); ResizeForContent(); }
    public void SetSaved(bool saved) { save.Text=saved?"★ Saved":"☆ Save"; }
    void ResizeForContent() {
        if(sizing || manualSize) return;
        sizing=true;
        try {
            var work=Screen.FromRectangle(manualPosition?Bounds:anchor).WorkingArea;
            int maxHeight=Math.Max(MinimumSize.Height,work.Height-Look.S(32));
            int maxWidth=Math.Min(Look.S(Math.Max(760,preferredWidth)),work.Width-Look.S(32));
            LayoutContent(); MeasureAnswer();
            while(answer.Top+naturalHeight+Look.S(214)>maxHeight && Width<maxWidth) {
                Width=Math.Min(maxWidth,Width+Look.S(80)); LayoutContent(); MeasureAnswer();
            }
            int wanted=Math.Max(Look.S(Math.Max(340,preferredHeight)),answer.Top+naturalHeight+Look.S(214));
            // Grow smoothly while streaming; final layout can shrink once.
            int height=Math.Min(maxHeight,busy?Math.Max(Height,wanted):wanted);
            if(Height!=height) Height=height;
            Place(); Invalidate(true);
        } finally { sizing=false; }
    }
    void MeasureAnswer() { if(answer.IsHandleCreated) SendMessage(answer.Handle,0x441,IntPtr.Zero,IntPtr.Zero); }
    public void FitToAnswer() { manualSize=false; preferredHeight=0; Width=Math.Min(Look.S(preferredWidth),MaximumSize.Width>0?MaximumSize.Width:Look.S(preferredWidth)); ResizeForContent(); if(PreferredSizeChanged!=null) PreferredSizeChanged(new Size(preferredWidth,0)); }
    void Place() {
        var work=Screen.FromRectangle(manualPosition?Bounds:anchor).WorkingArea;
        if(!manualPosition) { var point=Look.Below(Size,anchor,work,Look.S(10),true); if(point.HasValue) Location=point.Value; }
        else Location=new Point(Math.Max(work.Left,Math.Min(Left,work.Right-Width)),Math.Max(work.Top,Math.Min(Top,work.Bottom-Height)));
        Look.Shape(this,14); LayoutContent();
    }
    void Render(string text) {
        if(rendering) return; rendering=true;
        int selectedStart=answer.SelectionStart,selectedLength=answer.SelectionLength;
        int firstLine=answer.IsHandleCreated?(int)SendMessage(answer.Handle,0xCE,IntPtr.Zero,IntPtr.Zero):0;
        if(answer.IsHandleCreated) SendMessage(answer.Handle,0xB,IntPtr.Zero,IntPtr.Zero);
        try {
            answer.Clear();
            answer.SelectionColor=Look.Ink;
            string[] lines=text.Replace("\r\n","\n").Split('\n');
            for(int n=0;n<lines.Length;n++) {
                string line=Regex.Replace(lines[n],@"^#{1,6}\s+",""); line=Regex.Replace(line,@"^\s*[-*]\s+","• ");
                int pos=0;
                foreach(Match m in Regex.Matches(line,@"\*\*(.+?)\*\*")) {
                    answer.SelectionFont=normal; answer.AppendText(line.Substring(pos,m.Index-pos)); answer.SelectionFont=bold; answer.AppendText(m.Groups[1].Value); pos=m.Index+m.Length;
                }
                answer.SelectionFont=normal; answer.AppendText(line.Substring(pos));
                if(n<lines.Length-1) answer.AppendText("\n");
            }
            // Make compact labels scan clearly even when the model uses plain text.
            foreach(Match m in Regex.Matches(answer.Text,@"(?m)^(?:•\s*)?(Meaning|Example):")) { answer.Select(m.Index,m.Length); answer.SelectionFont=bold; answer.SelectionColor=Look.Accent; }
            answer.Select(Math.Min(selectedStart,answer.TextLength),Math.Min(selectedLength,Math.Max(0,answer.TextLength-selectedStart)));
            if(answer.IsHandleCreated) { int currentLine=(int)SendMessage(answer.Handle,0xCE,IntPtr.Zero,IntPtr.Zero); SendMessage(answer.Handle,0xB6,IntPtr.Zero,new IntPtr(firstLine-currentLine)); }
        } finally { if(answer.IsHandleCreated) { SendMessage(answer.Handle,0xB,new IntPtr(1),IntPtr.Zero); answer.Invalidate(); } rendering=false; }
    }
    protected override void Dispose(bool disposing) { if(disposing) { renderTimer.Dispose(); tips.Dispose(); wordMenu.Dispose(); normal.Dispose(); bold.Dispose(); heading.Dispose(); } base.Dispose(disposing); }
}
}
