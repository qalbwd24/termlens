using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TermLens {
public sealed class NumberField : Panel {
    public NumberField(NumericUpDown value) {
        Size=new Size(150,38);
        value.Visible=false;
        var text=new TextBox { BorderStyle=BorderStyle.None,TextAlign=HorizontalAlignment.Center,Text=value.Value.ToString(),Font=new Font("Segoe UI",11),Bounds=new Rectangle(39,9,72,24) };
        var down=new SoftButton("−",false) { Bounds=new Rectangle(0,0,34,36) };
        var up=new SoftButton("+",false) { Bounds=new Rectangle(116,0,34,36) };
        Action commit=delegate { decimal n; if(Decimal.TryParse(text.Text,out n)) value.Value=Math.Max(value.Minimum,Math.Min(value.Maximum,n)); text.Text=value.Value.ToString(); };
        down.Click+=delegate { commit(); value.Value=Math.Max(value.Minimum,value.Value-value.Increment); };
        up.Click+=delegate { commit(); value.Value=Math.Min(value.Maximum,value.Value+value.Increment); };
        text.Leave+=delegate { commit(); }; text.KeyDown+=delegate(object sender,KeyEventArgs e) { if(e.KeyCode==Keys.Enter) { e.SuppressKeyPress=true; commit(); } };
        value.ValueChanged+=delegate { text.Text=value.Value.ToString(); };
        Controls.AddRange(new Control[]{value,text,down,up});
    }
}
public sealed class ComposerBox : TextBox {
    public event Action Submitted;
    public Func<bool> PasteImage;
    bool composing;
    long compositionEnded;
    public bool CompositionActive { get { return composing || (compositionEnded!=0 && (System.Diagnostics.Stopwatch.GetTimestamp()-compositionEnded)*1000/System.Diagnostics.Stopwatch.Frequency<150); } }
    [System.Runtime.InteropServices.DllImport("imm32.dll")] static extern IntPtr ImmGetContext(IntPtr hwnd);
    [System.Runtime.InteropServices.DllImport("imm32.dll")] static extern bool ImmReleaseContext(IntPtr hwnd,IntPtr context);
    [System.Runtime.InteropServices.DllImport("imm32.dll")] static extern bool ImmSetOpenStatus(IntPtr context,bool open);
    [System.Runtime.InteropServices.DllImport("imm32.dll")] static extern bool ImmSetConversionStatus(IntPtr context,uint conversion,uint sentence);
    public ComposerBox() { Multiline=true; AcceptsReturn=true; ScrollBars=ScrollBars.None; ImeMode=ImeMode.Off; BorderStyle=BorderStyle.None; }
    void CloseIme() {
        if(!IsHandleCreated) return;
        IntPtr context=ImmGetContext(Handle); if(context==IntPtr.Zero) return;
        try { ImmSetOpenStatus(context,false); ImmSetConversionStatus(context,0,0); } finally { ImmReleaseContext(Handle,context); }
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); CloseIme(); }
    protected override void OnEnter(EventArgs e) { base.OnEnter(e); ImeMode=ImeMode.Off; CloseIme(); }
    protected override void WndProc(ref Message m) {
        if(m.Msg==0x302 && PasteImage!=null && PasteImage()) return;
        if(m.Msg==0x10D) composing=true;
        if(m.Msg==0x10E) { composing=false; compositionEnded=System.Diagnostics.Stopwatch.GetTimestamp(); }
        base.WndProc(ref m);
    }
    protected override bool ProcessCmdKey(ref Message msg,Keys keys) {
        if((keys==(Keys.Control|Keys.V) || keys==(Keys.Shift|Keys.Insert)) && PasteImage!=null && PasteImage()) return true;
        return base.ProcessCmdKey(ref msg,keys);
    }
    protected override void OnKeyDown(KeyEventArgs e) {
        if(e.KeyCode==Keys.Enter && !e.Shift) {
            bool justCommitted=compositionEnded!=0 && (System.Diagnostics.Stopwatch.GetTimestamp()-compositionEnded)*1000/System.Diagnostics.Stopwatch.Frequency<100;
            if(!composing) { e.SuppressKeyPress=true; if(!justCommitted && Submitted!=null) Submitted(); }
        }
        base.OnKeyDown(e);
    }
}
public sealed class FieldFrame : Panel {
    public FieldFrame(Control editor,bool multiline) {
        DoubleBuffered=true; SetStyle(ControlStyles.SupportsTransparentBackColor,true); BackColor=Color.Transparent; Padding=new Padding(Look.S(12),Look.S(multiline?12:10),Look.S(12),Look.S(multiline?12:10));
        if(editor is TextBox) ((TextBox)editor).BorderStyle=BorderStyle.None;
        if(editor is RichTextBox) ((RichTextBox)editor).BorderStyle=BorderStyle.None;
        editor.Dock=DockStyle.Fill; Controls.Add(editor); Dock=multiline?DockStyle.Fill:DockStyle.Top;
        if(!multiline) Height=Look.S(44);
    }
    protected override void OnPaintBackground(PaintEventArgs e) {
        base.OnPaintBackground(e); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        // The frame is a translucent surface over BackdropSurface.  The native
        // editor inside it remains a readable field while the rounded margin
        // and border participate in the wallpaper layer.
        var field=Theme.Dark?Color.FromArgb(218,36,45,43):Color.FromArgb(225,245,248,245);
        using(var path=Look.Round(new RectangleF(0,0,Width-1,Height-1),10)) using(var brush=new SolidBrush(field)) e.Graphics.FillPath(brush,path);
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(var path=Look.Round(new RectangleF(.5f,.5f,Width-1.5f,Height-1.5f),8)) using(var pen=new Pen(Look.Border)) e.Graphics.DrawPath(pen,path);
    }
}
public sealed class SettingsPages : UserControl {
    public readonly List<Panel> TabPages=new List<Panel>();
    readonly List<SoftButton> links=new List<SoftButton>();
    // Page identity stays in registration order. Only navigation is grouped.
    static readonly int[][] groupPages={new[]{0,3},new[]{1,4,7},new[]{6,5,2}};
    static readonly string[] namesEn={"Read","Explain prompt","Connection","History & saved","Ask prompt","Voice","Reading appearance","Polish prompt"};
    static readonly string[] namesZh={"查询","解释提示词","连接与语言","历史与收藏","提问提示词","朗读","阅读外观","润色提示词"};
    static readonly string[] descriptionsEn={
        "Look up a word, explain a passage, or ask a question.",
        "Choose how TermLens explains words, sentences, and technical ideas.",
        "Set your model connection, API key, and interface language.",
        "Search previous queries, review saved items, and export your library.",
        "Choose the style and depth of answers to your questions.",
        "Choose a voice and manage playback and audio caching.",
        "Adjust reading size, window preferences, and scrolling.",
        "Choose how TermLens improves your wording without answering the draft."
    };
    static readonly string[] descriptionsZh={
        "查询单词、解释段落，或提出问题。",
        "设置单词、句子与技术概念的解释方式。",
        "配置模型连接、API 密钥与界面语言。",
        "检索查询记录，查看收藏并导出学习资料。",
        "设置提问回答的表达方式与详细程度。",
        "选择声音，调整播放与音频缓存。",
        "调整阅读字号、窗口偏好与滚动速度。",
        "设置润色方式，只改进表达，不回答草稿中的问题。"
    };
    readonly FlowLayoutPanel navigation=new OpaqueNavigation {
        Dock=DockStyle.Left,Width=Look.S(158),FlowDirection=FlowDirection.TopDown,
        WrapContents=false,AutoScroll=true,Padding=new Padding(Look.S(6),Look.S(12),Look.S(8),Look.S(8)),Name="SettingsNavigation"
    };
    readonly Panel content=new OpaquePanel { Dock=DockStyle.Fill,Padding=new Padding(Look.S(22),Look.S(16),Look.S(20),Look.S(14)),Name="SettingsContent" };
    readonly Panel pageHeader=new OpaquePanel { Dock=DockStyle.Top,Height=Look.S(76),Name="SettingsPageHeader" };
    readonly Label heading=new Label { AutoSize=false,Font=new Font("Microsoft YaHei UI",15,FontStyle.Bold),UseMnemonic=false };
    readonly MutedLabel description=new MutedLabel { Font=new Font("Microsoft YaHei UI",8.5f),UseMnemonic=false };
    readonly Panel body=new OpaquePanel { Dock=DockStyle.Fill,Name="SettingsPageBody" };
    readonly List<MutedLabel> groups=new List<MutedLabel>();
    bool arranging;
    int selected=-1;
    public event EventHandler SelectedIndexChanged;
    // Settings use solid surfaces. Their former wallpaper-era transparent panels
    // could replay sibling/old-page pixels while a page or theme was changing.
    public static bool UsesOpaqueSurface(Control control) {
        for(Control current=control;current!=null;current=current.Parent)
            if(current is MainWindow || current is SettingsPages || current.Name=="SettingsShell") return true;
        return false;
    }
    public static Color OpaqueBackColor(Control control) {
        if(control is TextBoxBase || control is ComboBox || control is ListBox || control is NumericUpDown || (control.Parent is FieldFrame)) return Look.Field;
        for(Control current=control;current!=null;current=current.Parent) {
            if(current.Name=="SettingsNavigation") return Look.Canvas;
            if(current.Name=="SettingsContent" || current.Name=="SettingsPageHeader" || current.Name=="SettingsPageBody" || current.Name.StartsWith("SettingsPage",StringComparison.Ordinal) && !(current is SettingsPages)) return Look.Surface;
        }
        return Look.Canvas;
    }
    public static void ApplyOpaqueSurface(Control root) {
        root.BackColor=OpaqueBackColor(root);
        foreach(Control child in root.Controls) ApplyOpaqueSurface(child);
    }
    sealed class OpaquePanel : Panel {
        public OpaquePanel() { DoubleBuffered=true; ResizeRedraw=true; }
        protected override void OnPaintBackground(PaintEventArgs e) {
            using(var brush=new SolidBrush(OpaqueBackColor(this))) e.Graphics.FillRectangle(brush,ClientRectangle);
        }
    }
    sealed class OpaqueNavigation : FlowLayoutPanel {
        public OpaqueNavigation() { DoubleBuffered=true; ResizeRedraw=true; }
        protected override void OnPaintBackground(PaintEventArgs e) {
            using(var brush=new SolidBrush(Look.Canvas)) e.Graphics.FillRectangle(brush,ClientRectangle);
        }
    }
    protected override void OnPaintBackground(PaintEventArgs e) {
        using(var brush=new SolidBrush(Look.Canvas)) e.Graphics.FillRectangle(brush,ClientRectangle);
    }
    // Theme.Apply assigns general foreground colors recursively. Draw supporting
    // text with the current muted palette so its hierarchy survives theme changes.
    sealed class MutedLabel : Label {
        protected override void OnPaintBackground(PaintEventArgs e) {
            using(var background=new SolidBrush(OpaqueBackColor(this))) e.Graphics.FillRectangle(background,ClientRectangle);
        }
        protected override void OnPaint(PaintEventArgs e) {
            TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,Look.Muted,
                TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding);
        }
    }
    public int SelectedIndex {
        get { return selected; }
        set {
            if(value<0 || value>=TabPages.Count || selected==value) return;
            selected=value;
            body.SuspendLayout();
            try {
                foreach(var page in TabPages) page.Visible=false;
                for(int i=0;i<links.Count;i++) { links[i].Primary=i==value; links[i].Invalidate(); }
                var active=TabPages[value]; active.BringToFront(); active.Visible=true;
                UpdateHeader(); ApplyOpaqueSurface(this);
            } finally { body.ResumeLayout(true); }
            UiTrace.Write("settings page="+value+" \""+TabPages[value].Text+"\"");
            if(SelectedIndexChanged!=null) SelectedIndexChanged(this,EventArgs.Empty);
            // Erase the previous page and its children before the next frame.
            Invalidate(true); if(IsHandleCreated) { navigation.Update(); content.Update(); Update(); }
        }
    }
    public SettingsPages() {
        DoubleBuffered=true; AutoScaleMode=AutoScaleMode.None;
        pageHeader.Controls.Add(heading); pageHeader.Controls.Add(description);
        content.Controls.Add(body); content.Controls.Add(pageHeader);
        Controls.Add(content); Controls.Add(navigation);
        for(int i=0;i<groupPages.Length;i++) groups.Add(new MutedLabel {
            AutoSize=false,Height=Look.S(25),Font=new Font("Microsoft YaHei UI",8.5f,FontStyle.Bold),
            Margin=new Padding(Look.S(6),i==0?0:Look.S(16),0,Look.S(3)),UseMnemonic=false
        });
        pageHeader.SizeChanged+=delegate { ArrangeHeader(); };
        navigation.SizeChanged+=delegate { ArrangeNavigation(); };
        UpdateGroupNames();
    }
    public void ClickPage(int index) { if(index>=0 && index<links.Count) links[index].PerformClick(); }
    public Panel AddPage(string title) {
        int index=TabPages.Count;
        if(index>=namesEn.Length) throw new InvalidOperationException("Register a navigation group before adding a new settings page.");
        var page=new OpaquePanel { Text=UiLanguage.T(namesEn[index],namesZh[index]),Dock=DockStyle.Fill,AutoScroll=true,Visible=false,Name="SettingsPage"+index };
        TabPages.Add(page); body.Controls.Add(page);
        var button=new SoftButton(page.Text,false) {
            Height=Look.S(35),Margin=new Padding(0,0,0,Look.S(3)),Font=new Font("Microsoft YaHei UI",9),Quiet=true,
            AccessibleName=page.Text,Name="SettingsNav"+index
        };
        button.Click+=delegate { SelectedIndex=index; }; links.Add(button);
        RebuildNavigation();
        if(selected<0) SelectedIndex=0;
        return page;
    }
    void RebuildNavigation() {
        navigation.SuspendLayout();
        try {
            navigation.Controls.Clear();
            for(int group=0;group<groupPages.Length;group++) {
                bool added=false;
                foreach(int index in groupPages[group]) {
                    if(index>=links.Count) continue;
                    if(!added) { navigation.Controls.Add(groups[group]); added=true; }
                    navigation.Controls.Add(links[index]);
                }
            }
            ArrangeNavigation();
        } finally { navigation.ResumeLayout(); }
    }
    void ArrangeNavigation() {
        // Always reserve scrollbar width: introducing a vertical scrollbar must
        // not force a horizontal scrollbar or clip a translated navigation label.
        int width=Math.Max(Look.S(96),navigation.ClientSize.Width-navigation.Padding.Horizontal-SystemInformation.VerticalScrollBarWidth);
        foreach(var link in links) link.Width=width;
        foreach(var group in groups) group.Width=Math.Max(1,width-group.Margin.Left);
    }
    void UpdateGroupNames() {
        string[] en={"WORKSPACE","RESPONSES","PREFERENCES"};
        string[] zh={"工作区","回答方式","偏好设置"};
        for(int i=0;i<groups.Count;i++) groups[i].Text=UiLanguage.T(en[i],zh[i]);
    }
    void UpdateHeader() {
        if(selected<0 || selected>=TabPages.Count) return;
        heading.Text=TabPages[selected].Text;
        description.Text=UiLanguage.T(descriptionsEn[selected],descriptionsZh[selected]);
        ArrangeHeader();
    }
    void ArrangeHeader() {
        if(arranging || pageHeader.ClientSize.Width<1) return;
        arranging=true;
        try {
            int width=pageHeader.ClientSize.Width;
            int titleHeight=TextRenderer.MeasureText(heading.Text,heading.Font,new Size(width,Int32.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding).Height;
            titleHeight=Math.Max(Look.S(30),titleHeight);
            int hintHeight=TextRenderer.MeasureText(description.Text,description.Font,new Size(width,Int32.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding).Height;
            hintHeight=Math.Max(description.Font.Height,hintHeight);
            heading.SetBounds(0,0,width,titleHeight);
            description.SetBounds(0,titleHeight+Look.S(5),width,hintHeight);
            pageHeader.Height=description.Bottom+Look.S(18);
        } finally { arranging=false; }
    }
    public void RefreshLanguage() {
        for(int i=0;i<links.Count;i++) {
            string label=UiLanguage.T(namesEn[i],namesZh[i]);
            links[i].Text=label; links[i].AccessibleName=label; TabPages[i].Text=label;
        }
        UpdateGroupNames(); UpdateHeader(); ArrangeNavigation();
    }
}
}