using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TermLens {
public sealed partial class MainWindow {
    sealed class ReleaseScenarioButton : Button {
        public bool Selected;
        public ReleaseScenarioButton(string text) {
            Text=text; AccessibleName=text; Cursor=Cursors.Hand; FlatStyle=FlatStyle.Flat;
            FlatAppearance.BorderSize=0; Font=new Font("Microsoft YaHei UI",9);
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
        }
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.Clear(BackColor);
            var fill=Selected?Color.FromArgb(25,112,100):Look.Field;
            using(var path=Look.Round(new RectangleF(0,0,Width-1,Height-1),Look.S(7)))
            using(var brush=new SolidBrush(fill)) e.Graphics.FillPath(brush,path);
            TextRenderer.DrawText(e.Graphics,Text,Font,Rectangle.Inflate(ClientRectangle,-Look.S(8),0),Selected?Color.White:Look.Ink,
                TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
            if(Focused) ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-4,-4));
        }
    }
    sealed class ReleaseScenarioStrip : FlowLayoutPanel {
        public ReleaseScenarioStrip() { FlowDirection=FlowDirection.LeftToRight; WrapContents=false; AutoScroll=true; }
        protected override void OnMouseWheel(MouseEventArgs e) {
            if(HorizontalScroll.Visible) AutoScrollPosition=new Point(Math.Max(0,-AutoScrollPosition.X-e.Delta/120*Look.S(150)),0);
            else base.OnMouseWheel(e);
        }
    }
    readonly ReleaseScenarioStrip releasePromptScenarios=new ReleaseScenarioStrip { Dock=DockStyle.Top,Height=Look.S(58),AccessibleName="阅读场景，点击选择" };
    readonly SoftButton releasePromptPrimary=new SoftButton("一级：转述与解释",true);
    readonly SoftButton releasePromptSecondaryButton=new SoftButton("二级：转述与语境解释",false);
    readonly ToolTip releasePromptTips=new ToolTip { InitialDelay=600,ReshowDelay=200 };
    string releasePromptAppliedId;
    Label releasePromptPurpose;
    bool releasePromptSecondary;
    internal bool ReleasePromptSecondary { get { return releasePromptSecondary; } }
    readonly List<PromptPreset> releasePromptDrafts=new List<PromptPreset>();
    Label releasePromptLibraryState;
    string releasePromptSelectedId;
    bool releasePromptLibraryUpdating,releasePromptLibraryDirty;
    internal string SelectedReleasePromptPresetId { get { return releasePromptSelectedId; } }
    internal bool ReleasePromptLibraryDirty { get { return releasePromptLibraryDirty; } }
    internal List<PromptPreset> ReleasePromptDrafts { get { StoreReleasePromptDraft(); return releasePromptDrafts.Select(PromptLibrary.Clone).ToList(); } }

    void BuildReleasePromptLibrary() {
        var page=releasePages[1];
        prompt.Dock=DockStyle.Fill; prompt.Font=new Font("Microsoft YaHei UI",10); prompt.Multiline=true;
        prompt.WordWrap=true; prompt.ScrollBars=ScrollBars.Vertical;
        releasePromptScenarios.Resize+=delegate { LayoutReleasePromptScenarios(); };
        Disposed+=delegate { releasePromptTips.Dispose(); };
        var actions=new SettingsActionRow { Dock=DockStyle.Top,Height=Look.S(40) };
        var create=new SoftButton("新建",false) { Width=Look.S(72),Height=Look.S(32) };
        var copy=new SoftButton("另存为",false) { Width=Look.S(80),Height=Look.S(32) };
        var rename=new SoftButton("重命名",false) { Width=Look.S(80),Height=Look.S(32) };
        var delete=new SoftButton("删除",false) { Width=Look.S(72),Height=Look.S(32) };
        create.Click+=delegate { PromptLibraryNameAction("新建阅读场景",NextReleasePromptName("新阅读场景"),name=>CreateReleasePromptPreset(name,false)); };
        copy.Click+=delegate { var current=CurrentReleasePromptDraft(); PromptLibraryNameAction("另存为阅读场景",NextReleasePromptName(current.Name+" 副本"),name=>CreateReleasePromptPreset(name,true)); };
        rename.Click+=delegate { PromptLibraryNameAction("重命名阅读场景",CurrentReleasePromptDraft().Name,RenameReleasePromptPreset); };
        delete.Click+=delegate { try { DeleteReleasePromptPreset(releasePromptSelectedId,true); } catch(Exception error) { SetStatus(error.Message,true); } };
        actions.Controls.AddRange(new Control[]{create,copy,rename,delete});
        var levels=new Panel { Dock=DockStyle.Bottom,Height=Look.S(54) };
        releasePromptPrimary.Height=releasePromptSecondaryButton.Height=Look.S(32);
        releasePromptPrimary.Location=Point.Empty;
        releasePromptPrimary.Click+=delegate { SelectReleasePromptLevel(false); };
        releasePromptSecondaryButton.Click+=delegate { SelectReleasePromptLevel(true); };
        releasePromptPurpose=ReleaseNote("先转述，再说明含义和背景。",20);
        releasePromptPurpose.Dock=DockStyle.Bottom;
        levels.Controls.AddRange(new Control[]{releasePromptPrimary,releasePromptSecondaryButton,releasePromptPurpose});
        levels.Resize+=delegate {
            int half=(levels.ClientSize.Width-Look.S(8))/2;
            releasePromptPrimary.Width=half; releasePromptSecondaryButton.Width=half;
            releasePromptSecondaryButton.Location=new Point(half+Look.S(8),0);
        };
        var header=new Panel { Dock=DockStyle.Top,Height=Look.S(152) };
        header.Controls.Add(actions); header.Controls.Add(releasePromptScenarios); header.Controls.Add(levels);
        actions.Dock=DockStyle.Top;
        var footer=new Panel { Dock=DockStyle.Bottom,Height=Look.S(46) };
        var reset=new SoftButton("恢复当前级别默认",false) { Width=Look.S(152),Height=Look.S(32),Location=new Point(0,Look.S(7)) };
        reset.Click+=delegate { RestoreReleasePromptLevel(); };
        releasePromptLibraryState=ReleaseNote("保存并应用后，两级提示词一起生效。",38);
        releasePromptLibraryState.Dock=DockStyle.Fill;
        var resetHolder=new Panel { Dock=DockStyle.Left,Width=Look.S(164) }; resetHolder.Controls.Add(reset);
        footer.Controls.Add(releasePromptLibraryState); footer.Controls.Add(resetHolder);
        page.Controls.Add(new FieldFrame(new ReleasePromptScrollHost(prompt),true)); page.Controls.Add(footer); page.Controls.Add(header);
        prompt.TextChanged+=delegate {
            if(releasePromptLibraryUpdating || releaseApplying || releasePromptDrafts.Count==0) return;
            StoreReleasePromptDraft(); MarkReleasePromptLibraryDirty();
        };
    }
    internal void SelectReleasePromptLevel(bool secondary) {
        if(releasePromptSecondary==secondary) return;
        StoreReleasePromptDraft(); releasePromptSecondary=secondary;
        releasePromptPrimary.Primary=!secondary; releasePromptSecondaryButton.Primary=secondary;
        releasePromptPrimary.Invalidate(); releasePromptSecondaryButton.Invalidate();
        releasePromptPurpose.Text=secondary?"先直接转述，再解释语境；必要难词可加括号释义。":"先转述，再解释；全英文，不加括号释义。";
        LoadReleasePromptDraft();
    }
    internal void RestoreReleasePromptLevel() {
        var current=CurrentReleasePromptDraft();
        var defaults=current.TemplateId=="technical"?PromptLibrary.CreateTechnical():current.TemplateId=="everyday"?PromptLibrary.CreateEveryday():PromptLibrary.New("默认",Settings.ReleaseDefaultPrompt);
        prompt.Text=PromptEditorText(releasePromptSecondary?defaults.SecondaryText:defaults.Text);
        SetStatus("已恢复当前场景、当前级别的默认提示词；保存并应用后生效。",false);
    }
    static string PromptEditorText(string text) { return (text??"").Replace("\r\n","\n").Replace("\n","\r\n"); }
    PromptPreset CurrentReleasePromptDraft() {
        var current=releasePromptDrafts.FirstOrDefault(p=>p.Id==releasePromptSelectedId);
        if(current==null) throw new InvalidOperationException("请选择一个阅读场景。");
        return current;
    }
    void StoreReleasePromptDraft() {
        if(releasePromptLibraryUpdating || releasePromptDrafts.Count==0) return;
        var current=releasePromptDrafts.FirstOrDefault(p=>p.Id==releasePromptSelectedId);
        if(current!=null) { if(releasePromptSecondary) current.SecondaryText=prompt.Text.Replace("\r\n","\n"); else current.Text=prompt.Text.Replace("\r\n","\n"); }
    }
    void LoadReleasePromptDraft() {
        releasePromptLibraryUpdating=true;
        try { var current=CurrentReleasePromptDraft(); prompt.Text=PromptEditorText(releasePromptSecondary?current.SecondaryText:current.Text); prompt.Select(0,0); prompt.ScrollToCaret(); }
        finally { releasePromptLibraryUpdating=false; }
    }
    void LayoutReleasePromptScenarios() {
        int width=Math.Max(Look.S(110),(releasePromptScenarios.ClientSize.Width-Look.S(18))/3);
        foreach(Control card in releasePromptScenarios.Controls) card.Size=new Size(width,Look.S(36));
    }
    void PaintReleasePromptSelection() {
        foreach(ReleaseScenarioButton card in releasePromptScenarios.Controls) { card.Selected=(string)card.Tag==releasePromptSelectedId; card.Invalidate(); }
    }
    void RefreshReleasePromptChoices() {
        releasePromptScenarios.SuspendLayout();
        foreach(Control card in releasePromptScenarios.Controls.Cast<Control>().ToList()) { releasePromptTips.SetToolTip(card,null); card.Dispose(); }
        releasePromptScenarios.Controls.Clear();
        releasePromptScenarios.AutoScroll=releasePromptDrafts.Count>3;
        foreach(var draft in releasePromptDrafts) {
            var card=new ReleaseScenarioButton(draft.Name) { Tag=draft.Id,Height=Look.S(36),Margin=new Padding(0,0,Look.S(6),0) };
            string id=draft.Id; card.Click+=delegate { SelectReleasePromptPreset(id); };
            releasePromptTips.SetToolTip(card,draft.Name); releasePromptScenarios.Controls.Add(card);
        }
        LayoutReleasePromptScenarios(); releasePromptScenarios.ResumeLayout(); PaintReleasePromptSelection();
        var selected=releasePromptScenarios.Controls.Cast<Control>().FirstOrDefault(c=>(string)c.Tag==releasePromptSelectedId);
        if(selected!=null) releasePromptScenarios.ScrollControlIntoView(selected);
        LoadReleasePromptDraft();
    }
    void ApplyReleasePromptLibrary() {
        var source=Files.Json().Deserialize<Settings>(Files.Json().Serialize(settings)); PromptLibrary.Ensure(source);
        releasePromptDrafts.Clear(); releasePromptDrafts.AddRange(source.PromptPresets.Select(PromptLibrary.Clone));
        releasePromptSelectedId=source.ActivePromptPresetId; releasePromptAppliedId=source.ActivePromptPresetId;
        RefreshReleasePromptChoices(); MarkReleasePromptLibrarySaved();
    }
    void ReadReleasePromptLibrary(Settings candidate) {
        StoreReleasePromptDraft();
        candidate.PromptPresets=releasePromptDrafts.Select(PromptLibrary.Clone).ToList();
        candidate.ActivePromptPresetId=releasePromptSelectedId;
        candidate.Prompt=CurrentReleasePromptDraft().Text;
    }
    void MarkReleasePromptLibraryDirty() {
        releasePromptLibraryDirty=true;
        UpdateReleasePromptState();
    }
    public void MarkReleasePromptLibrarySaved() {
        releasePromptLibraryDirty=false;
        releasePromptAppliedId=releasePromptSelectedId; UpdateReleasePromptState();
    }
    void UpdateReleasePromptState() {
        if(releasePromptLibraryState==null || releasePromptDrafts.Count==0) return;
        var applied=settings.PromptPresets==null?null:settings.PromptPresets.FirstOrDefault(p=>p.Id==releasePromptAppliedId);
        string name=applied==null?"当前场景":applied.Name;
        releasePromptLibraryState.Text=releasePromptLibraryDirty?"尚未应用；点击保存并应用。正在使用："+name:"正在使用："+name+"。两级提示词已应用。";
    }
    string ValidateReleasePromptName(string name,string exceptId=null) {
        name=(name??"").Trim();
        if(name.Length==0 || name.Length>64) throw new InvalidOperationException("存档名称应为 1 至 64 个字符。");
        if(releasePromptDrafts.Any(p=>p.Id!=exceptId && String.Equals(p.Name,name,StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("已有同名存档，请使用不同的名称。");
        return name;
    }
    string NextReleasePromptName(string basis) {
        basis=(basis??"新阅读场景").Trim(); if(basis.Length>55) basis=basis.Substring(0,55);
        string name=basis; int suffix=2;
        while(releasePromptDrafts.Any(p=>String.Equals(p.Name,name,StringComparison.OrdinalIgnoreCase))) name=basis+" "+suffix++;
        return name;
    }
    internal void SelectReleasePromptPreset(string id) {
        if(!releasePromptDrafts.Any(p=>p.Id==id)) throw new InvalidOperationException("找不到此阅读场景。");
        if(releasePromptSelectedId==id) return;
        StoreReleasePromptDraft(); releasePromptSelectedId=id; PaintReleasePromptSelection();
        var selected=releasePromptScenarios.Controls.Cast<Control>().FirstOrDefault(c=>(string)c.Tag==id);
        if(selected!=null) releasePromptScenarios.ScrollControlIntoView(selected);
        LoadReleasePromptDraft(); MarkReleasePromptLibraryDirty();
    }
    internal void CreateReleasePromptPreset(string name,bool copyCurrent) {
        name=ValidateReleasePromptName(name);
        if(releasePromptDrafts.Count>=100) throw new InvalidOperationException("最多保存 100 个阅读场景。");
        StoreReleasePromptDraft();
        var added=PromptLibrary.New(name,copyCurrent?CurrentReleasePromptDraft().Text:Settings.ReleaseDefaultPrompt);
        if(copyCurrent) { var current=CurrentReleasePromptDraft(); added.SecondaryText=current.SecondaryText; added.TemplateId=current.TemplateId; }
        releasePromptDrafts.Add(added); releasePromptSelectedId=added.Id; RefreshReleasePromptChoices(); MarkReleasePromptLibraryDirty();
    }
    internal void RenameReleasePromptPreset(string name) {
        var current=CurrentReleasePromptDraft(); name=ValidateReleasePromptName(name,current.Id);
        if(current.Name==name) return;
        StoreReleasePromptDraft(); current.Name=name; RefreshReleasePromptChoices(); MarkReleasePromptLibraryDirty();
    }
    internal void DeleteReleasePromptPreset(string id,bool confirm) {
        if(releasePromptDrafts.Count<=1) throw new InvalidOperationException("至少保留一个阅读场景。");
        var target=releasePromptDrafts.FirstOrDefault(p=>p.Id==id);
        if(target==null) throw new InvalidOperationException("找不到此阅读场景。");
        if(confirm && MessageBox.Show(this,"删除存档“"+target.Name+"”？保存设置后生效。","删除阅读场景",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes) return;
        StoreReleasePromptDraft(); int index=releasePromptDrafts.IndexOf(target); releasePromptDrafts.Remove(target);
        if(releasePromptSelectedId==id) releasePromptSelectedId=releasePromptDrafts[Math.Min(index,releasePromptDrafts.Count-1)].Id;
        RefreshReleasePromptChoices(); MarkReleasePromptLibraryDirty();
    }
    void PromptLibraryNameAction(string title,string initial,Action<string> action) {
        using(var dialog=new Form { Text=title,FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterParent,ClientSize=new Size(Look.S(340),Look.S(125)),MinimizeBox=false,MaximizeBox=false,ShowInTaskbar=false,Font=Font }) {
            var caption=new Label { Text="存档名称（例如：计算机英语、学术阅读）",AutoSize=true,Location=new Point(Look.S(12),Look.S(12)) };
            var name=new TextBox { Text=initial,MaxLength=64,Location=new Point(Look.S(12),Look.S(38)),Width=Look.S(316) };
            var accept=new SoftButton("确定",true) { Location=new Point(Look.S(160),Look.S(80)),Size=new Size(Look.S(80),Look.S(32)) };
            var cancel=new SoftButton("取消",false) { Location=new Point(Look.S(248),Look.S(80)),Size=new Size(Look.S(80),Look.S(32)),DialogResult=DialogResult.Cancel };
            accept.Click+=delegate { try { action(name.Text); dialog.DialogResult=DialogResult.OK; } catch(Exception error) { MessageBox.Show(dialog,error.Message,title,MessageBoxButtons.OK,MessageBoxIcon.Information); } };
            dialog.Controls.AddRange(new Control[]{caption,name,accept,cancel}); dialog.AcceptButton=accept; dialog.CancelButton=cancel;
            Theme.Apply(dialog); dialog.Shown+=delegate { name.Focus(); name.SelectAll(); }; dialog.ShowDialog(this);
        }
    }
}
}

