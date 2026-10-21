using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace TermLens {
public sealed partial class MainWindow {
    readonly List<Panel> releasePages=new List<Panel>();
    readonly List<SoftButton> releaseNavigation=new List<SoftButton>();
    readonly Panel releaseContent=new Panel { Dock=DockStyle.Fill,Name="SettingsContent",Padding=new Padding(Look.S(18)) };
    readonly Label releaseHeading=new Label { Dock=DockStyle.Top,Height=Look.S(38),Font=new Font("Microsoft YaHei UI",15,FontStyle.Bold) };
    readonly Panel releaseBody=new Panel { Dock=DockStyle.Fill };
    readonly Panel releaseFooter=new Panel { Dock=DockStyle.Bottom,Height=Look.S(48) };
    readonly CheckBox releaseShowKey=new CheckBox { Text="显示密钥",AutoSize=true };
    readonly CheckBox releaseDark=new CheckBox { Text="深色外观",AutoSize=true };
    readonly CheckBox releaseStartup=new CheckBox { Text="登录 Windows 后在托盘启动",AutoSize=true };
    readonly NumericUpDown releaseFont=new NumericUpDown { Minimum=10,Maximum=24,DecimalPlaces=0,Width=Look.S(96) };
    readonly NumericUpDown releaseRate=new NumericUpDown { Minimum=-50,Maximum=50,Increment=5,Width=Look.S(96) };
    readonly ComboBox releaseSpeechProvider=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList,Width=Look.S(190) };
    readonly ComboBox releaseInstalledVoice=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList,Width=Look.S(270),DropDownWidth=Look.S(360) };
    readonly ComboBox releaseMicrosoftVoice=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList,Width=Look.S(270) };
    readonly CheckBox releasePreload=new CheckBox { Text="预缓存音频",AutoSize=true };
    readonly CheckBox releaseSaveAudio=new CheckBox { Text="收藏时保存音频",AutoSize=true };
    Panel releaseMicrosoftVoiceRow;
    Panel releaseVoiceRow;
    Label releaseVoiceNote;
    readonly ReleaseShortcutInput releaseTrigger=new ReleaseShortcutInput { Width=Look.S(240) };
    Label releaseShortcutHint;
    readonly Label releaseCount=new Label { Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true };
    readonly SoftButton releaseFavorite=new SoftButton("收藏",false);
    readonly Timer releaseSearchDelay=new Timer { Interval=220 };
    readonly Timer releaseListScrollWatch=new Timer { Interval=100 };
    readonly Panel releaseListViewport=new Panel { Dock=DockStyle.Fill };
    readonly SlimScrollBar releaseListScroll=new SlimScrollBar { Dock=DockStyle.Right,Width=Look.S(9),AccessibleName="历史记录滚动" };
    readonly Font releaseTimeFont=new Font("Microsoft YaHei UI",8);
    bool releaseListSyncing;
    Panel releaseAdvanced;
    SoftButton releaseAdvancedToggle;
    bool releaseApplying;
    bool releaseEdgeVoiceInitialized;
    int releasePage;
    static readonly string[] releaseTitles={"连接","提示词","阅读与音频","快捷键与启动","历史与收藏"};

    public void BuildReleaseSettings() {
        var parent=tabs.Parent as TableLayoutPanel;
        if(parent==null) throw new InvalidOperationException("Settings shell is not ready.");
        var cell=parent.GetPositionFromControl(tabs); parent.Controls.Remove(tabs); tabs.Visible=false;
        var shell=new Panel { Dock=DockStyle.Fill,Name="SettingsShell" };
        var navigation=new FlowLayoutPanel { Dock=DockStyle.Left,Width=Look.S(130),FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(0,Look.S(10),Look.S(10),0),Name="SettingsNavigation" };
        releaseContent.Controls.Add(releaseBody); releaseContent.Controls.Add(releaseFooter); releaseContent.Controls.Add(releaseHeading);
        shell.Controls.Add(releaseContent); shell.Controls.Add(navigation); parent.Controls.Add(shell,cell.Column,cell.Row);
        for(int i=0;i<releaseTitles.Length;i++) {
            int index=i;
            var button=new SoftButton(releaseTitles[i],false) { Width=Look.S(114),Height=Look.S(36),Margin=new Padding(0,0,0,Look.S(6)),Quiet=true };
            button.Click+=delegate { SelectReleasePage(index); }; navigation.Controls.Add(button); releaseNavigation.Add(button);
            var page=new Panel { Dock=DockStyle.Fill,Visible=false,AutoScroll=true,Name="SettingsPageRelease"+i };
            releasePages.Add(page); releaseBody.Controls.Add(page);
        }
        var save=new SoftButton("保存并应用",true) { Width=Look.S(112),Height=Look.S(34),Anchor=AnchorStyles.Right|AnchorStyles.Top };
        save.Click+=delegate { if(SaveSettings(true)) SetStatus("设置已保存。",false); };
        releaseFooter.Controls.Add(save); releaseFooter.Resize+=delegate { save.Location=new Point(Math.Max(0,releaseFooter.ClientSize.Width-save.Width),Look.S(8)); };
        BuildReleaseConnection(); BuildReleasePrompt(); BuildReleaseReading(); BuildReleaseShortcuts(); BuildReleaseHistoryPage();
        SelectReleasePage(0);
        Disposed+=delegate { releaseSearchDelay.Dispose(); releaseListScrollWatch.Dispose(); releaseTimeFont.Dispose(); };
    }
    static Label ReleaseNote(string text,int height=42) {
        return new Label { Text=text,Dock=DockStyle.Top,Height=Look.S(height),Font=new Font("Microsoft YaHei UI",9),Padding=new Padding(0,Look.S(4),0,0),UseMnemonic=false };
    }
    static Panel ReleaseRow(string label,Control editor,int height=38) {
        var row=new Panel { Dock=DockStyle.Top,Height=Look.S(height) };
        var caption=new Label { Text=label,Dock=DockStyle.Left,Width=Look.S(92),TextAlign=ContentAlignment.MiddleLeft,UseMnemonic=false };
        editor.Location=new Point(Look.S(100),Look.S(8));
        if(editor is TextBox) { editor.Width=Look.S(280); editor.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right; row.Resize+=delegate { editor.Width=Math.Max(Look.S(80),row.ClientSize.Width-Look.S(108)); }; }
        row.Controls.Add(editor); row.Controls.Add(caption); return row;
    }
    static void ReleaseStack(Panel target,params Control[] controls) {
        for(int i=controls.Length-1;i>=0;i--) target.Controls.Add(controls[i]);
    }
    void BuildReleaseConnection() {
        var page=releasePages[0]; key.UseSystemPasswordChar=true; key.ImeMode=ImeMode.Disable; key.Font=new Font("Microsoft YaHei UI",10);
        releaseShowKey.CheckedChanged+=delegate { key.UseSystemPasswordChar=!releaseShowKey.Checked; };
        releaseAdvancedToggle=new SoftButton("高级连接设置 ▸",false) { Dock=DockStyle.Top,Height=Look.S(34),Quiet=true };
        releaseAdvanced=new Panel { Dock=DockStyle.Top,Height=Look.S(138),Visible=false };
        proxy.Text="使用 Windows 系统代理"; proxy.AutoSize=true;
        ReleaseStack(releaseAdvanced,ReleaseRow("API 地址",endpoint),ReleaseRow("模型",model),ReleaseRow("",proxy));
        releaseAdvancedToggle.Click+=delegate { releaseAdvanced.Visible=!releaseAdvanced.Visible; releaseAdvancedToggle.Text=releaseAdvanced.Visible?"高级连接设置 ▾":"高级连接设置 ▸"; page.PerformLayout(); };
        ReleaseStack(page,ReleaseNote("默认使用 DeepSeek。填写 DeepSeek API Key 后即可开始查询。",48),ReleaseRow("API Key",key),ReleaseRow("",releaseShowKey,36),releaseAdvancedToggle,releaseAdvanced,ReleaseNote("密钥仅保存在本机，并由当前 Windows 账户加密保护。",48));
    }
    void BuildReleasePrompt() {
        BuildReleasePromptLibrary();
    }
    void BuildReleaseReading() {
        releaseFont.Font=new Font("Microsoft YaHei UI",10); releaseRate.Font=releaseFont.Font;
        releaseSpeechProvider.Items.AddRange(new object[]{"百度在线朗读","Windows 系统语音","微软在线（实验）","Edge TTS（在线）"});
        releaseMicrosoftVoice.Items.AddRange(new object[]{"en-US-AriaNeural","en-US-GuyNeural","en-US-JennyNeural","en-GB-SoniaNeural","en-US-AndrewMultilingualNeural","en-US-EmmaMultilingualNeural"});
        releaseMicrosoftVoice.SelectedIndex=0;
        releaseMicrosoftVoiceRow=ReleaseRow("在线声音",releaseMicrosoftVoice);
        releaseInstalledVoice.Items.Add("自动选择（优先英语）");
        try {
            using(var synthesizer=new System.Speech.Synthesis.SpeechSynthesizer()) {
                foreach(var voice in synthesizer.GetInstalledVoices().Where(v=>v.Enabled))
                    releaseInstalledVoice.Items.Add(voice.VoiceInfo.Name);
            }
        } catch { /* Keep online reading available when Windows speech is absent. */ }
        releaseInstalledVoice.SelectedIndex=0;
        releaseVoiceRow=ReleaseRow("系统声音",releaseInstalledVoice);
        releaseVoiceNote=ReleaseNote("在线预缓存会将原文和回答发送给所选语音服务；收藏音频保存在本机。",44);
        var audioOptions=new FlowLayoutPanel { Dock=DockStyle.Top,Height=Look.S(32),WrapContents=false };
        audioOptions.Controls.AddRange(new Control[]{releasePreload,releaseSaveAudio});
        releaseSpeechProvider.SelectedIndexChanged+=delegate {
            if(releaseSpeechProvider.SelectedIndex==3 && !releaseApplying && !releaseEdgeVoiceInitialized) {
                releaseMicrosoftVoice.SelectedItem="en-US-AndrewMultilingualNeural";
                releaseEdgeVoiceInitialized=true;
            }
            UpdateReleaseVoiceSelection();
        };
        ReleaseStack(releasePages[2],ReleaseNote("字号和外观会应用到解释弹窗。",32),ReleaseRow("字号",releaseFont),ReleaseRow("外观",releaseDark),ReleaseRow("朗读方式",releaseSpeechProvider),releaseVoiceRow,releaseMicrosoftVoiceRow,ReleaseRow("语速",releaseRate),audioOptions,releaseVoiceNote);
        UpdateReleaseVoiceSelection();
    }
    void UpdateReleaseVoiceSelection() {
        if(releaseSpeechProvider.SelectedIndex==1 && releaseInstalledVoice.Items.Count<=1) {
            releaseSpeechProvider.SelectedIndex=0;
            if(!releaseApplying) SetStatus("未找到可用的 Windows 系统声音，已保留在线朗读。",true);
        }
        releaseVoiceRow.Visible=releaseSpeechProvider.SelectedIndex==1;
        releaseMicrosoftVoiceRow.Visible=releaseSpeechProvider.SelectedIndex==2 || releaseSpeechProvider.SelectedIndex==3;
        releaseVoiceNote.Text=releaseSpeechProvider.SelectedIndex==3
            ?"Edge TTS 使用普通直连，不使用代理。预缓存会发送原文和回答；收藏音频保存在本机。"
            :releaseSpeechProvider.SelectedIndex==2
            ?"微软实验接入使用普通直连，依赖本机签名配置。预缓存会发送原文和回答；收藏音频保存在本机。"
            :"在线预缓存会将原文和回答发送给所选语音服务；收藏音频保存在本机。";
    }
    void BuildReleaseShortcuts() {
        releaseTrigger.CaptureStarted+=SuspendReleaseHotkey;
        releaseTrigger.CaptureEnded+=ResumeReleaseHotkey;
        releaseShortcutHint=ReleaseNote("点击录入后按键盘组合键或鼠标侧键；Esc 取消。",42);
        releaseTrigger.HintChanged+=delegate { releaseShortcutHint.Text=releaseTrigger.Hint; };
        var record=new SoftButton("录入快捷键",false) { Width=Look.S(130),Height=Look.S(32) };
        record.Click+=delegate { releaseTrigger.BeginCapture(); };
        releaseStartup.CheckedChanged+=delegate {
            if(releaseApplying) return;
            try { StartupOption.Current().SetEnabled(releaseStartup.Checked); SetStatus(releaseStartup.Checked?"已启用登录后在托盘启动。":"已关闭登录后启动。",false); }
            catch(Exception error) { SetStatus(error.Message,true); releaseApplying=true; try { releaseStartup.Checked=StartupOption.Current().Enabled; } catch { releaseStartup.Checked=false; } finally { releaseApplying=false; } }
        };
        ReleaseStack(releasePages[3],ReleaseNote("选中文字后，按快捷键显示解释。",36),ReleaseRow("触发键",releaseTrigger),ReleaseRow("",record,40),releaseShortcutHint,ReleaseNote("侧键绑定后代替前进/后退；录入时暂停旧键。",40),ReleaseRow("开机启动",releaseStartup,42),ReleaseNote("开机启动立即生效，快捷键需保存。",38));
    }
    void BuildReleaseHistoryPage() {
        var page=releasePages[4];
        var filters=new Panel { Dock=DockStyle.Top,Height=Look.S(42) };
        var searchLabel=new Label { Text="搜索",Location=new Point(0,Look.S(9)),AutoSize=true };
        search.Location=new Point(Look.S(40),Look.S(5)); search.Width=Look.S(230); search.Font=new Font("Microsoft YaHei UI",10);
        savedOnly.Text="仅收藏"; savedOnly.AutoSize=true;
        filters.Controls.AddRange(new Control[]{searchLabel,search,savedOnly});
        filters.Resize+=delegate { int width=Math.Max(Look.S(100),filters.Width-Look.S(155)); search.Width=width; savedOnly.Location=new Point(search.Right+Look.S(10),Look.S(8)); };
        var split=new SplitContainer { Size=new Size(Look.S(500),Look.S(360)),Dock=DockStyle.Fill,Orientation=Orientation.Horizontal,SplitterWidth=Look.S(7),Panel1MinSize=Look.S(70),Panel2MinSize=Look.S(90) };
        list.Dock=DockStyle.Fill; historyAnswer.Dock=DockStyle.Fill; historyAnswer.Font=new Font("Microsoft YaHei UI",10); historyAnswer.ReadOnly=true;
        // Keep ListBox's native scrolling/keyboard behavior, but place its native
        // scrollbar outside a clipping viewport; the narrow sibling is draggable.
        var listHost=new Panel { Dock=DockStyle.Fill };
        list.Dock=DockStyle.None; list.HorizontalScrollbar=false; list.IntegralHeight=false;
        releaseListViewport.Controls.Add(list); listHost.Controls.Add(releaseListViewport); listHost.Controls.Add(releaseListScroll);
        split.Panel1.Controls.Add(listHost); split.Panel2.Controls.Add(historyAnswer);
        historyAnswer.ScrollBars=RichTextBoxScrollBars.None;
        releaseListViewport.Resize+=delegate {
            list.SetBounds(0,0,Math.Max(1,releaseListViewport.ClientSize.Width)+SystemInformation.VerticalScrollBarWidth,Math.Max(1,releaseListViewport.ClientSize.Height));
            SyncReleaseListScroll();
        };
        releaseListScroll.ValueChanged+=delegate {
            if(releaseListSyncing || list.Items.Count==0) return;
            list.TopIndex=Math.Min(list.Items.Count-1,releaseListScroll.Value); list.Invalidate();
        };
        releaseListScrollWatch.Tick+=delegate { SyncReleaseListScroll(); };
        page.VisibleChanged+=delegate { releaseListScrollWatch.Enabled=page.Visible; if(page.Visible) SyncReleaseListScroll(); };
        list.SelectedIndexChanged+=delegate { SyncReleaseListScroll(); };
        list.KeyUp+=delegate { SyncReleaseListScroll(); };
        list.MouseWheel+=delegate { if(IsHandleCreated) BeginInvoke((Action)SyncReleaseListScroll); };
        list.DrawItem+=DrawReleaseHistoryItem;
        var actions=new SettingsActionRow { Dock=DockStyle.Bottom,Height=Look.S(42) };
        var open=new SoftButton("查看弹窗",false) { Width=Look.S(90),Height=Look.S(32) };
        releaseFavorite.Width=Look.S(90); releaseFavorite.Height=Look.S(32);
        var delete=new SoftButton("删除记录",false) { Width=Look.S(90),Height=Look.S(32) };
        var export=new SoftButton("导出 JSON",false) { Width=Look.S(98),Height=Look.S(32) };
        open.Click+=delegate { var item=list.SelectedItem as Lookup; if(item!=null) OpenReleaseLookup(item); };
        list.DoubleClick+=delegate { var item=list.SelectedItem as Lookup; if(item!=null) OpenReleaseLookup(item); };
        releaseFavorite.Click+=delegate { var item=list.SelectedItem as Lookup; if(!CanFavoriteReleaseLookup(item)) return; try { var saved=history.SetFavorite(item,!item.Saved); SaveReleaseFavoriteAudio(saved); RefreshReleaseHistory(); } catch(Exception e) { SetStatus(e.Message,true); } };
        delete.Click+=delegate {
            var item=list.SelectedItem as Lookup; if(item==null) return;
            if(MessageBox.Show(this,"删除当前选中的这一条查询记录？相关收藏将同时取消。","删除记录",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes) return;
            try { DeleteReleaseHistoryItem(item); RefreshReleaseHistory(); SetStatus("已删除选中的记录。",false); } catch(Exception e) { SetStatus(e.Message,true); }
        };
        export.Click+=delegate { ExportReleaseHistory(); };
        actions.Controls.AddRange(new Control[]{open,releaseFavorite,delete,export});
        var countRow=new Panel { Dock=DockStyle.Bottom,Height=Look.S(28) }; countRow.Controls.Add(releaseCount);
        page.Controls.Add(split); page.Controls.Add(actions); page.Controls.Add(countRow); page.Controls.Add(filters);
        list.SelectedIndexChanged+=delegate { var item=list.SelectedItem as Lookup; historyAnswer.Text=item==null?"":item.Term+"\r\n"+item.Created+"\r\n\r\n"+item.Answer; releaseFavorite.Text=item!=null && item.Saved?"取消收藏":"收藏"; open.Enabled=delete.Enabled=item!=null; releaseFavorite.Enabled=CanFavoriteReleaseLookup(item); };
        releaseSearchDelay.Tick+=delegate { releaseSearchDelay.Stop(); RefreshReleaseHistory(); };
        search.TextChanged+=delegate { releaseSearchDelay.Stop(); releaseSearchDelay.Start(); }; savedOnly.CheckedChanged+=delegate { RefreshReleaseHistory(); };
    }
    void SyncReleaseListScroll() {
        if(releaseListSyncing || list.IsDisposed || !list.IsHandleCreated) return;
        releaseListSyncing=true;
        try {
            int shown=Math.Max(1,list.ClientSize.Height/Math.Max(1,list.ItemHeight));
            releaseListScroll.Viewport=shown; releaseListScroll.Maximum=Math.Max(0,list.Items.Count-shown);
            releaseListScroll.Value=list.Items.Count==0?0:list.TopIndex;
            releaseListScroll.Visible=releaseListScroll.Maximum>0;
        } finally { releaseListSyncing=false; }
    }
    void DrawReleaseHistoryItem(object sender,DrawItemEventArgs e) {
        if(e.Index<0 || e.Index>=list.Items.Count) return;
        var item=list.Items[e.Index] as Lookup; if(item==null) return;
        bool selected=(e.State&DrawItemState.Selected)!=0;
        Color background=selected?(Theme.Dark?Color.FromArgb(43,65,58):Color.FromArgb(226,241,233)):list.BackColor;
        using(var brush=new SolidBrush(background)) e.Graphics.FillRectangle(brush,e.Bounds);
        int width=Math.Min(e.Bounds.Width,releaseListViewport.ClientSize.Width),pad=Look.S(8),icon=Look.S(16);
        if(selected) using(var brush=new SolidBrush(Look.Accent)) e.Graphics.FillRectangle(brush,e.Bounds.Left,e.Bounds.Top,Look.S(3),e.Bounds.Height);
        if(item.Saved) V2Icons.Draw(e.Graphics,"saved",new Rectangle(pad,e.Bounds.Top+(e.Bounds.Height-icon)/2,icon,icon),Look.Accent,16f,0);
        string stamp=""; DateTime created;
        if(DateTime.TryParse(item.Created,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.RoundtripKind,out created)) stamp=created.ToLocalTime().ToString("MM-dd HH:mm");
        int dateWidth=TextRenderer.MeasureText(stamp,releaseTimeFont,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Width;
        int left=pad+icon+Look.S(7),textWidth=Math.Max(1,width-left-dateWidth-pad*2);
        string text=System.Text.RegularExpressions.Regex.Replace(item.Term??"",@"\s+"," ").Trim();
        TextRenderer.DrawText(e.Graphics,text,list.Font,new Rectangle(left,e.Bounds.Top,textWidth,e.Bounds.Height),Look.Ink,TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(e.Graphics,stamp,releaseTimeFont,new Rectangle(Math.Max(left,width-pad-dateWidth),e.Bounds.Top,dateWidth,e.Bounds.Height),Look.Muted,TextFormatFlags.NoPadding|TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter);
        if((e.State&DrawItemState.Focus)!=0) ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(1,e.Bounds.Top,Math.Max(1,width-2),Math.Max(1,e.Bounds.Height-1)),Look.Ink,background);
    }
    internal static bool CanFavoriteReleaseLookup(Lookup item) { return item!=null && String.Equals(item.Status,"complete",StringComparison.OrdinalIgnoreCase) && !String.IsNullOrWhiteSpace(item.Answer); }
    void SelectReleasePage(int index) {
        if(index<0 || index>=releasePages.Count) return;
        releasePage=index; releaseBody.SuspendLayout();
        try { for(int i=0;i<releasePages.Count;i++) { releasePages[i].Visible=i==index; releaseNavigation[i].Primary=i==index; releaseNavigation[i].Invalidate(); } releasePages[index].BringToFront(); releaseHeading.Text=releaseTitles[index]; releaseFooter.Visible=index!=4; }
        finally { releaseBody.ResumeLayout(true); }
        if(index==4) RefreshReleaseHistory(); SettingsPages.ApplyOpaqueSurface(releaseContent); releaseContent.Invalidate(true);
    }
    public void ShowReleaseHistory() { SelectReleasePage(4); Reveal(); }
    public void ShowReleasePromptLibrary() { SelectReleasePage(1); Reveal(); }
    public void ApplyReleaseSettings() {
        releaseApplying=true;
        try {
            endpoint.Text=settings.BaseUrl; model.Text=settings.Model; key.Text=apiKey;
            ApplyReleasePromptLibrary(); proxy.Checked=settings.UseSystemProxy;
            releaseFont.Value=Math.Max(10,Math.Min(24,(decimal)settings.ReadingFontSize)); releaseRate.Value=Math.Max(-50,Math.Min(50,settings.SpeechRate)); releaseDark.Checked=settings.DarkMode;
            releaseSpeechProvider.SelectedIndex=settings.SpeechProvider=="windows"?1:settings.SpeechProvider=="microsoft"?2:settings.SpeechProvider=="edge"?3:0; speechRate.Value=releaseRate.Value;
            releasePreload.Checked=settings.SpeechPreload; releaseSaveAudio.Checked=settings.SpeechSaveWithFavorites;
            if(settings.SpeechProvider=="microsoft" || settings.SpeechProvider=="edge") {
                string onlineVoice=String.IsNullOrEmpty(settings.SpeechVoice)?(settings.SpeechProvider=="edge"?"en-US-AndrewMultilingualNeural":"en-US-AriaNeural"):settings.SpeechVoice;
                if(!releaseMicrosoftVoice.Items.Contains(onlineVoice)) releaseMicrosoftVoice.Items.Add(onlineVoice);
                releaseMicrosoftVoice.SelectedItem=onlineVoice;
                if(settings.SpeechProvider=="edge") releaseEdgeVoiceInitialized=true;
            }
            releaseInstalledVoice.SelectedIndex=releaseInstalledVoice.Items.Contains(settings.SpeechVoice??"")?releaseInstalledVoice.Items.IndexOf(settings.SpeechVoice):0; UpdateReleaseVoiceSelection();
            releaseTrigger.SetShortcut(settings.TriggerKey,settings.TriggerModifiers);
            try { releaseStartup.Checked=StartupOption.Current().Enabled; } catch { releaseStartup.Checked=false; }
            releaseAdvanced.Visible=settings.BaseUrl!=new Settings().BaseUrl || settings.Model!=new Settings().Model;
            releaseAdvancedToggle.Text=releaseAdvanced.Visible?"高级连接设置 ▾":"高级连接设置 ▸";
        } finally { releaseApplying=false; }
        SyncReleaseSpeechControls(); RefreshReleaseHistory();
    }
    public void SyncReleaseSpeechControls() {
        speechPreload.Checked=false;
        if(speechScope.Items.Count==0) speechScope.Items.Add("仅原文"); speechScope.SelectedIndex=0;
        if(speechProvider.Items.Count==0) speechProvider.Items.AddRange(new object[]{"在线朗读","Windows 系统语音"});
        speechProvider.SelectedIndex=settings.SpeechProvider=="windows"?1:0;
        speechRate.Value=Math.Max(-50,Math.Min(50,settings.SpeechRate));
        if(!speechVoice.Items.Contains(settings.SpeechVoice??"")) speechVoice.Items.Add(settings.SpeechVoice??"");
        speechVoice.SelectedItem=settings.SpeechVoice??""; speechProxy.Text=settings.SpeechProxy??"";
    }
    public Settings ReadReleaseSettings() {
        var candidate=Files.Json().Deserialize<Settings>(Files.Json().Serialize(settings));
        candidate.BaseUrl=endpoint.Text.Trim(); candidate.Model=model.Text.Trim(); ReadReleasePromptLibrary(candidate);
        if(String.IsNullOrWhiteSpace(candidate.BaseUrl) || String.IsNullOrWhiteSpace(candidate.Model)) throw new InvalidOperationException("API 地址和模型不能为空。");
        if(String.IsNullOrWhiteSpace(candidate.Prompt)) throw new InvalidOperationException("提示词不能为空；可点击恢复默认提示词。");
        candidate.UseSystemProxy=proxy.Checked; candidate.ReadingFontSize=(float)releaseFont.Value; candidate.DarkMode=releaseDark.Checked; candidate.SpeechRate=(int)releaseRate.Value;
        candidate.UiLanguage="zh"; candidate.FloatingButton=false; candidate.IncludeHistory=false; candidate.QuoteAllWindow=false; candidate.SpeechProvider=releaseSpeechProvider.SelectedIndex==1?"windows":releaseSpeechProvider.SelectedIndex==2?"microsoft":releaseSpeechProvider.SelectedIndex==3?"edge":"baidu-web";
        candidate.SpeechVoice=candidate.SpeechProvider=="microsoft" || candidate.SpeechProvider=="edge"?Convert.ToString(releaseMicrosoftVoice.SelectedItem):releaseInstalledVoice.SelectedIndex>0?Convert.ToString(releaseInstalledVoice.SelectedItem):"";
        candidate.SpeechPreload=releasePreload.Checked; candidate.SpeechSaveWithFavorites=releaseSaveAudio.Checked;
        if(releaseTrigger.IsRecording) releaseTrigger.CancelCapture(); candidate.TriggerKey=releaseTrigger.KeyCode; candidate.TriggerModifiers=releaseTrigger.Modifiers;
        candidate.Validate(); return candidate;
    }
    public void RefreshReleaseHistory() {
        if(releasePages.Count==0 || history==null || IsDisposed) return;
        string selected=(list.SelectedItem as Lookup)==null?null:((Lookup)list.SelectedItem).Id;
        try {
            var items=history.Queries(search.Text,savedOnly.Checked,500);
            list.BeginUpdate(); try { list.Items.Clear(); foreach(var item in items.Take(500)) list.Items.Add(item); } finally { list.EndUpdate(); }
            int selectedIndex=-1; for(int i=0;i<list.Items.Count;i++) if(((Lookup)list.Items[i]).Id==selected) { selectedIndex=i; break; }
            list.SelectedIndex=selectedIndex>=0?selectedIndex:(list.Items.Count>0?0:-1);
            if(list.Items.Count==0) { historyAnswer.Text="暂无记录。选中文字并按快捷键即可开始。"; releaseFavorite.Enabled=false; }
            releaseCount.Text="显示 "+items.Count+" 条（最多 500 条） · 解释及音频保留 15 天；选词记录与收藏长期保存。"; SyncReleaseListScroll();
        } catch(Exception e) { SetStatus("读取历史失败："+e.Message,true); }
    }
    public void DeleteReleaseHistoryItem(Lookup item) {
        if(item==null || String.IsNullOrEmpty(item.Id)) return;
        var audioFavorite=history.FindFavorite(item.Kind,item.Term);
        if(audioFavorite!=null && releaseAudio!=null) releaseAudio.CancelFavorite(audioFavorite.Id);
        history.Run("BEGIN IMMEDIATE");
        try {
            history.DeleteSelection(item.Id);
            var favorite=history.FindFavorite(item.Kind,item.Term);
            if(favorite!=null && favorite.QueryId==item.Id) { history.Run("DELETE FROM favorites WHERE id=?",favorite.Id); if(item.Kind=="explain") history.SaveWord(item.Term,false); }
            history.Run("DELETE FROM lookups WHERE id=? OR id IN (SELECT legacy_id FROM query_links WHERE query_id=?)",item.Id,item.Id);
            history.Run("DELETE FROM teacher_answers WHERE id=?",item.Id); history.Run("DELETE FROM refinements WHERE id=?",item.Id);
            history.Run("DELETE FROM query_links WHERE query_id=? OR legacy_id=?",item.Id,item.Id);
            history.Run("DELETE FROM queries WHERE id=?",item.Id);
            string termKey=HistoryStore.Key(item.Term);
            history.Run("UPDATE terms SET count=(SELECT COUNT(*) FROM lookups WHERE term_key=?) WHERE term_key=?",termKey,termKey);
            history.Run("DELETE FROM terms WHERE term_key=? AND NOT EXISTS(SELECT 1 FROM lookups WHERE term_key=?)",termKey,termKey);
            history.Run("COMMIT");
        } catch { history.Run("ROLLBACK"); throw; }
    }
    public string ReleaseHistoryJson(bool favoritesOnly) {
        var favorites=history.Favorites();
        var favoriteKeys=new HashSet<string>(favorites.Select(f=>f.Kind+":"+HistoryStore.Key(f.Term)),StringComparer.Ordinal);
        string[] columns={"id","kind","source","text","answer","source_text","context","model","prompt","created","status","error"};
        var records=new List<Dictionary<string,object>>();
        foreach(var row in history.Run("SELECT id,kind,source,text,answer,source_text,context,model,prompt,created,status,error FROM queries ORDER BY created DESC")) {
            bool saved=favoriteKeys.Contains(row[1]+":"+HistoryStore.Key(row[3])); if(favoritesOnly && !saved) continue;
            var record=new Dictionary<string,object>(); for(int i=0;i<columns.Length;i++) record[columns[i]]=row[i]; record["saved"]=saved; records.Add(record);
        }
        var selections=history.SelectionEvents(favoritesOnly).Select(item=>new {
            id=item.Id,text=item.Text,normalized_key=item.NormalizedKey,created=item.Created,
            kind=item.Kind,source=item.Source,scene_id=item.SceneId,scene_name=item.SceneName
        }).ToArray();
        var result=new Dictionary<string,object> { {"format","termlens-history"},{"version",2},{"exported_at",DateTime.UtcNow.ToString("o")},{"scope",favoritesOnly?"favorites":"all"},{"records",records},{"favorites",favorites},{"selection_events",selections} };
        return new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength=Int32.MaxValue,RecursionLimit=100 }.Serialize(result);
    }
    void ExportReleaseHistory() {
        using(var dialog=new SaveFileDialog { Filter="JSON 文件 (*.json)|*.json",DefaultExt="json",AddExtension=true,FileName="Termlens-"+(savedOnly.Checked?"favorites":"history")+"-"+DateTime.Now.ToString("yyyyMMdd")+".json" }) {
            if(dialog.ShowDialog(this)!=DialogResult.OK) return;
            try { Files.Atomic(dialog.FileName,ReleaseHistoryJson(savedOnly.Checked)); SetStatus("已导出"+(savedOnly.Checked?"所有收藏记录":"全部历史记录")+"，不受搜索条件影响。",false); } catch(Exception e) { SetStatus("导出失败："+e.Message,true); }
        }
    }
}
}
