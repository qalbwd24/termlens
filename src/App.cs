using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TermLens {
public sealed partial class MainWindow : Form {
    public static readonly uint ShowMessage=Native.RegisterWindowMessage("TermLens.ShowWindow.v1");
    public static readonly uint ExitMessage=Native.RegisterWindowMessage("TermLens.Exit.v1");
    readonly string root;
    readonly Func<HttpMessageHandler> requestHandlers;
    Settings settings;
    string apiKey;
    readonly HistoryStore history;
    readonly SettingsPages tabs=new SettingsPages { Dock=DockStyle.Fill };
    readonly TextBox input=new ComposerBox { Dock=DockStyle.Fill, MaxLength=12000, ImeMode=ImeMode.Off };
    readonly RichTextBox output=new RichTextBox { ReadOnly=true, Dock=DockStyle.Fill, BackColor=Color.White, BorderStyle=BorderStyle.None, DetectUrls=false };
    readonly TextBox prompt=new TextBox { Multiline=true, ScrollBars=ScrollBars.Vertical, Dock=DockStyle.Fill, AcceptsReturn=true, ImeMode=ImeMode.On };
    readonly TextBox teacherPrompt=new TextBox { Multiline=true, ScrollBars=ScrollBars.Vertical, Dock=DockStyle.Fill, AcceptsReturn=true, ImeMode=ImeMode.On };
    readonly TextBox refinePrompt=new TextBox { Multiline=true, ScrollBars=ScrollBars.Vertical, Dock=DockStyle.Fill, AcceptsReturn=true, ImeMode=ImeMode.On };
    readonly Label status=new Label { Dock=DockStyle.Fill, TextAlign=ContentAlignment.MiddleLeft, AutoEllipsis=true, Padding=new Padding(8,0,8,0) };
    readonly Label details=new Label { AutoSize=true, Text="" };
    readonly Button explain=new SoftButton("",true), stop=new SoftButton("",false), saveWord=new SoftButton("",false);
    readonly TextBox endpoint=new TextBox(), model=new TextBox(), key=new TextBox();
    readonly CheckBox include=new CheckBox { Text="Include recent lookup terms / 附加近期查词", AutoSize=true };
    readonly CheckBox floating=new CheckBox { Text="Show selection button / 划词后显示按钮", AutoSize=true };
    readonly CheckBox proxy=new CheckBox { Text="Use Windows proxy / 使用系统代理", AutoSize=true };
    readonly CheckBox careful=new CheckBox { Text="Careful answers / 更严谨的解释",AutoSize=true };
    readonly CheckBox quoteAllWindow=new CheckBox { Text="Full Quote: include completed entries in this window",AutoSize=true };
    readonly NumericUpDown recentCount=new NumericUpDown { Minimum=0,Maximum=50,Width=Look.S(65) }, recentHours=new NumericUpDown { Minimum=1,Maximum=720,Width=Look.S(65) }, maxTokens=new NumericUpDown { Minimum=128,Maximum=16000,Increment=128,Width=Look.S(100) };
    readonly TextBox search=new TextBox { Width=Look.S(270) };
    readonly CheckBox savedOnly=new CheckBox { Text="Saved words only / 只看生词", AutoSize=true };
    readonly ListBox list=new ListBox { Dock=DockStyle.Fill, HorizontalScrollbar=false, IntegralHeight=false, BorderStyle=BorderStyle.None, DrawMode=DrawMode.OwnerDrawFixed };
    readonly RichTextBox historyAnswer=new RichTextBox { ReadOnly=true, Dock=DockStyle.Fill, BackColor=Color.White, DetectUrls=false };
    NotifyIcon tray;
    SelectionBubble bubble;
    bool hasMainGeometry; Rectangle savedMain;
    ResultPopup popup=new ResultPopup();
    MouseSelection mouse;
    CancellationTokenSource request;
    Lookup current;
    bool capturing, exiting;
    readonly List<int> registered=new List<int>();
    public MainWindow(string root,bool startHidden=false,Func<HttpMessageHandler> requestHandlers=null) {
        this.requestHandlers=requestHandlers;
        UiTrace.Root=root; UiTrace.Write("startup tray="+startHidden);
        UiTrace.Write("windows app identity="+WindowsAppIdentity.Current()+" previous="+WindowsAppIdentity.Previous);
        this.root=root; settings=Files.Load(root); apiKey=Files.LoadKey(root); Theme.SetPreferred(settings.DarkMode);
        history=new HistoryStore(Path.Combine(root,"history.sqlite3"));
        Text=Program.IsRelease?"Termlens release":"TermLens v2 · CS & Math"; StartPosition=FormStartPosition.CenterScreen; Size=new Size(1000,820); MinimumSize=new Size(900,700);
        Font=new Font("Microsoft YaHei UI",9); AutoScaleDimensions=new SizeF(96,96); AutoScaleMode=AutoScaleMode.Dpi; BackColor=Color.FromArgb(246,248,251);
        // Remembered rectangle; validated and applied in OnLoad, after WinForms
        // has finished its own DPI auto-scaling.
        Rectangle saved;
        hasMainGeometry=WindowGeometry.TryRestore(settings.MainLeft,settings.MainTop,settings.MainWidth,settings.MainHeight,new Size(640,480),out saved);
        savedMain=saved;
        if(hasMainGeometry) StartPosition=FormStartPosition.Manual;
        Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        var outer=new TableLayoutPanel { Name="SettingsShell",Dock=DockStyle.Fill, RowCount=3, ColumnCount=1, Padding=new Padding(16) };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute,40)); outer.RowStyles.Add(new RowStyle(SizeType.Percent,100)); outer.RowStyles.Add(new RowStyle(SizeType.Absolute,28));
        var title=new Label { Text="TERMLENS  /  Settings & library", Dock=DockStyle.Fill, TextAlign=ContentAlignment.MiddleLeft, UseMnemonic=false, Font=new Font("Microsoft YaHei UI",10,FontStyle.Bold), ForeColor=Look.Ink };
        status.Font=new Font("Segoe UI",8.5f);
        outer.Controls.Add(title,0,0); outer.Controls.Add(tabs,0,1); outer.Controls.Add(status,0,2); Controls.Add(outer);
        if(Program.IsRelease) { title.Text="Termlens"; UiLanguage.Current="zh"; BuildReleaseSettings(); ApplyReleaseSettings(); }
        else { BuildRead(); BuildPrompt(); BuildConnection(); BuildHistory(); BuildTeacherPrompt(); BuildVoice(); BuildReadingPreferences(); BuildRefinePrompt(); ApplySettings(); }
        StyleSettingsEditors(); Theme.Apply(this); ApplyAppearance(settings); HandleCreated+=delegate { Theme.Apply(this); ApplyAppearance(settings); };
        if(Program.IsRelease) InitReleaseWorkflow();
        else { ConnectCard(popup,null,false); ConnectInput(); InitWorkflow(); }
        Shown+=delegate {
            StartCapture();
            if(Program.IsRelease) {
                SetStatus(String.IsNullOrEmpty(apiKey)?"填写 API Key 后即可划词解释。":"已就绪，选中文字后按快捷键。",false);
                if(startHidden || (!Program.IsTest && !Program.OpenSettings && !String.IsNullOrEmpty(apiKey))) Hide();
                if(!String.IsNullOrEmpty(releaseHotkeyStartupError)) {
                    SelectReleasePage(3); Reveal(); SetStatus(releaseHotkeyStartupError,true);
                }
            } else {
                SetStatus(String.IsNullOrEmpty(apiKey)?UiLanguage.T("Add your API key on Connection, then Save settings.","在连接页面填写 API 密钥并保存。"):UiLanguage.T("Ready · Select text and press F13.","就绪 · 选中文字后按 F13。"),false);
                if(String.IsNullOrEmpty(apiKey)) tabs.SelectedIndex=2;
                if(startHidden) Hide(); else if(!Program.IsTest && !Program.OpenSettings && !String.IsNullOrEmpty(apiKey)) { Hide(); EnsureWorkspace().FocusInput(true); }
            }
        };
        FormClosing+=OnClosing;
    }
    // WinForms scales form metrics by the display DPI. The source constants assume
    // 96 DPI, so a plain Size(1000,820) turns into ~1730x1387 on this display.
    // Undo that factor so both the default size and the minimum land where intended.
    float DpiScale() {
        try { return AutoScaleFactor.Width>0 ? AutoScaleFactor.Width : 1f; } catch { return 1f; }
    }
    protected override void OnLoad(EventArgs e) {
        base.OnLoad(e);
        float scale=DpiScale();
        var work=Screen.FromControl(this).WorkingArea;
        // Cap the enforced minimum FIRST: the constructor value has already been
        // scaled by the display DPI (900x700 became 1557x1177) and would clamp
        // every size set below it.
        var floor=new Size(Math.Min(Look.S(Program.IsRelease?740:860),(int)Math.Round(work.Width*0.96)),Math.Min(Look.S(Program.IsRelease?520:600),(int)Math.Round(work.Height*0.96)));
        MinimumSize=floor;
        // First run (nothing remembered yet): pick a size that suits the screen
        // instead of letting the DPI scale the 96-DPI design constants up.
        if(!hasMainGeometry) {
            Size=new Size(Math.Min(Look.S(Program.IsRelease?800:920),work.Width),Math.Min(Look.S(Program.IsRelease?580:680),work.Height));
            Location=new Point(work.Left+(work.Width-Width)/2,work.Top+(work.Height-Height)/2);
        }
        // Restore the remembered rectangle only after the minimum is final,
        // otherwise the stale minimum would clamp it back up.
        if(hasMainGeometry && WindowState==FormWindowState.Normal) {
            Size=new Size(Math.Max(floor.Width,Math.Min(savedMain.Width,work.Width)),Math.Max(floor.Height,Math.Min(savedMain.Height,work.Height)));
            Location=new Point(Math.Max(work.Left,Math.Min(savedMain.Left,work.Right-Width)),Math.Max(work.Top,Math.Min(savedMain.Top,work.Bottom-Height)));
        }
        UiTrace.Write("geo default saved="+hasMainGeometry+" scale="+scale.ToString("F3")+" min="+MinimumSize.Width+"x"+MinimumSize.Height+" bounds="+Bounds.Width+"x"+Bounds.Height+"@"+Bounds.Left+","+Bounds.Top);
    }
    Panel Page(string name) { return tabs.AddPage(name); }
    static Button Button(string text,Action action) { var b=new SoftButton(text,false) { AutoSize=true,MinimumSize=new Size(Look.S(95),Look.S(34)) }; b.Click+=delegate { action(); }; return b; }
    // AutoSize + GrowAndShrink together with WrapContents is a circular request:
    // wrapping needs the width, and GrowAndShrink derives the width from the
    // children, so a page using this inside a TableLayoutPanel relaid out for
    // seconds. The rows that hold this are sized absolutely, so the panel can take
    // a fixed height and leave the width to its cell.
    static FlowLayoutPanel Flow() { return new SettingsActionRow(); }
    void StyleSettingsEditors() {
        foreach(var editor in new TextBoxBase[]{input,output,prompt,teacherPrompt,refinePrompt,historyAnswer}) editor.Font=new Font("Microsoft YaHei UI",10);
        historyAnswer.BorderStyle=BorderStyle.None;
        list.Font=new Font("Segoe UI",9.5f); list.ItemHeight=Look.S(32);
        if(Program.IsRelease) return; // Release owns its compact history renderer.
        list.DrawItem+=delegate(object sender,DrawItemEventArgs e) {
            if(e.Index<0 || e.Index>=list.Items.Count) return;
            bool selected=(e.State&DrawItemState.Selected)!=0;
            using(var brush=new SolidBrush(selected?Look.Field:Look.Surface)) e.Graphics.FillRectangle(brush,e.Bounds);
            if(selected) using(var brush=new SolidBrush(Look.Accent)) e.Graphics.FillRectangle(brush,e.Bounds.Left,e.Bounds.Top,Look.S(3),e.Bounds.Height);
            var rect=Rectangle.Inflate(e.Bounds,-Look.S(10),0);
            TextRenderer.DrawText(e.Graphics,Convert.ToString(list.Items[e.Index]),list.Font,rect,Look.Ink,TextFormatFlags.EndEllipsis|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix);
            e.DrawFocusRectangle();
        };
    }
    void BuildRead() {
        var page=Page("Read / 阅读");
        var panel=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=5 };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); panel.RowStyles.Add(new RowStyle(SizeType.Absolute,115)); panel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); panel.RowStyles.Add(new RowStyle(SizeType.Percent,100)); panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label { Text="Selected text / 所选内容 — Chinese input is supported",AutoSize=true },0,0);
        panel.Controls.Add(new FieldFrame(input,true),0,1);
        var actions=Flow();
        explain.Text="Explain / 解释"; explain.AutoSize=true; explain.Height=Look.S(34); explain.Click+=async delegate { await Explain(); };
        stop.Text="Stop / 停止"; stop.AutoSize=true; stop.Height=Look.S(34); stop.Enabled=false; stop.Click+=delegate { if(request!=null) request.Cancel(); };
        saveWord.Text="☆ Save word / 加入生词"; saveWord.AutoSize=true; saveWord.Height=Look.S(34); saveWord.Enabled=false; saveWord.Click+=delegate { ToggleCurrent(); };
        actions.Controls.Add(explain); actions.Controls.Add(stop); actions.Controls.Add(saveWord);
        actions.Controls.Add(Button("Copy answer",delegate { if(output.Text.Length>0) { try { Clipboard.SetText(output.Text); SetStatus("Answer copied.",false); } catch { SetStatus("Clipboard busy. Try again.",true); } } }));
        panel.Controls.Add(actions,0,2); output.Font=new Font("Microsoft YaHei UI",11); panel.Controls.Add(new FieldFrame(output,true),0,3); panel.Controls.Add(details,0,4); page.Controls.Add(panel);
        ((ComposerBox)input).Submitted+=async delegate { await Explain(); };
    }
    void BuildPrompt() {
        var page=Page("Prompt / 提示词"); var panel=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=5 };
        // Same treatment as the Teacher and Polish pages: the column is a
        // percentage and the hint row has a fixed height with a non-AutoSize
        // label, so neither the column width nor the row height waits on the
        // other's wrap result.
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute,Look.S(62))); panel.RowStyles.Add(new RowStyle(SizeType.Percent,100)); panel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); panel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label { Text="Your instructions control the answer. Edit in English or Chinese.\nSelected text is sent separately; no special placeholder is needed.",Dock=DockStyle.Fill,AutoSize=false,Padding=new Padding(0,0,0,8) },0,0);
        panel.Controls.Add(new FieldFrame(prompt,true),0,1);
        panel.Controls.Add(include,0,2);
        var limits=Flow(); limits.Controls.Add(new Label { Text="Recent terms",AutoSize=true }); limits.Controls.Add(recentCount); limits.Controls.Add(new Label { Text="within hours",AutoSize=true }); limits.Controls.Add(recentHours); panel.Controls.Add(limits,0,3);
        var actions=Flow(); actions.Controls.Add(Button("Save settings / 保存",delegate { SaveSettings(true); })); actions.Controls.Add(Button("Preview request",Preview)); actions.Controls.Add(Button("CS / Math default",delegate { if(MessageBox.Show(this,"Replace the editor text with the default CS / Math prompt?","Prompt",MessageBoxButtons.YesNo)==DialogResult.Yes) prompt.Text=Settings.DefaultPrompt.Replace("\r\n","\n").Replace("\n","\r\n"); }));
        panel.Controls.Add(actions,0,4); page.Controls.Add(panel);
    }
    void BuildConnection() {
        var page=Page("Connection / 连接"); var panel=new TableLayoutPanel { Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,Padding=new Padding(4) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        AddRow(panel,0,"API base URL",endpoint); AddRow(panel,1,"Model / 模型",model);
        key.UseSystemPasswordChar=true; AddRow(panel,2,"API key / 密钥",key); key.ImeMode=ImeMode.Disable;
        var show=new CheckBox { Text="Show key / 显示密钥",AutoSize=true }; show.CheckedChanged+=delegate { key.UseSystemPasswordChar=!show.Checked; }; AddRow(panel,3,"",show);
        languageChoice.Items.AddRange(new object[]{"English","中文"});
        var languages=Flow(); languageChoice.Visible=false; languageEnglish.Width=Look.S(110); languageChinese.Width=Look.S(100);
        languageEnglish.Click+=delegate { languageChoice.SelectedIndex=0; }; languageChinese.Click+=delegate { languageChoice.SelectedIndex=1; };
        languages.Controls.AddRange(new Control[]{languageChoice,languageEnglish,languageChinese});
        AddRow(panel,4,"Max output tokens",maxTokens); AddRow(panel,5,"",proxy); AddRow(panel,6,"Interface language",languages);
        var info=new Label { Text="Your key is protected for your Windows account.\nTest connection sends a short paid request.",AutoSize=true,MaximumSize=new Size(Look.S(425),0),Padding=new Padding(0,12,0,12) }; AddRow(panel,7,"",info);
        var actions=Flow(); actions.Height=Look.S(48); actions.Controls.Add(Button("Save settings / 保存",delegate { SaveSettings(true); })); actions.Controls.Add(Button("Test connection",async delegate { if(SaveSettings(false)) { input.Text="Explain the word algorithm in one short sentence."; tabs.SelectedIndex=0; await Explain(); } }));
        AddRow(panel,8,"",actions); AddRow(panel,9,"",careful); page.Controls.Add(panel);
    }
    void BuildTeacherPrompt() {
        var page=Page("Teacher / 英语老师");
        var panel=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=4 };
        // An unstyled single column behaves as AutoSize, so the widest child sets
        // its width and the row leaves the page. A percentage column plus a
        // fixed-height, non-AutoSize hint row avoids the AutoSize feedback loop
        // described at WindowGeometry.MinimumForSize's sibling comments: an
        // AutoSize row asks the label for its height, the label needs the column
        // width in order to wrap, and the circle repeats.
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute,Look.S(56))); panel.RowStyles.Add(new RowStyle(SizeType.Percent,100)); panel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label { Text="Ask sends your question and quoted entries. Full Quote adds completed text entries from this window.\nExplicit quotes take priority; you will be notified if long context is shortened.",AutoSize=false,Dock=DockStyle.Fill,Font=new Font("Segoe UI",8.5f),Padding=new Padding(0,0,0,12) },0,0);
        panel.Controls.Add(new FieldFrame(teacherPrompt,true),0,1);
        var actions=Flow(); actions.Controls.Add(Button("Save settings / 保存",delegate { SaveSettings(true); }));
        actions.Controls.Add(Button("Teacher default",delegate { teacherPrompt.Text=Settings.DefaultTeacherPrompt.Replace("\r\n","\n").Replace("\n","\r\n"); }));
        panel.Controls.Add(quoteAllWindow,0,2); panel.Controls.Add(actions,0,3); page.Controls.Add(panel);
    }
    void BuildRefinePrompt() {
        var page=Page("Polish wording / 润色表达");
        var panel=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=3 };
        // Same treatment as the Teacher page: a percentage column with a
        // fixed-height, non-AutoSize hint row, so the column width and the
        // label's wrapped height cannot depend on each other.
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute,Look.S(56))); panel.RowStyles.Add(new RowStyle(SizeType.Percent,100)); panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label { Text="Polish improves a sentence or search query without answering it.\nChoose a mode, then press Enter. Shift alone switches modes.",AutoSize=false,Dock=DockStyle.Fill,Padding=new Padding(0,0,0,12) },0,0);
        panel.Controls.Add(new FieldFrame(refinePrompt,true),0,1);
        var actions=Flow(); actions.Controls.Add(Button("Save settings / 保存",delegate { SaveSettings(true); }));
        actions.Controls.Add(Button("Polish default",delegate { refinePrompt.Text=Settings.DefaultRefinePrompt.Replace("\r\n","\n").Replace("\n","\r\n"); }));
        panel.Controls.Add(actions,0,2); page.Controls.Add(panel);
    }
    static void AddRow(TableLayoutPanel panel,int row,string label,Control control) {
        panel.RowCount=row+1; panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label { Text=label,AutoSize=true,Margin=new Padding(0,6,12,6) },0,row);
        if(control is TextBox) control=new FieldFrame(control,false);
        if(control is NumericUpDown) control=new NumberField((NumericUpDown)control);
        control.Margin=new Padding(0,4,0,4); panel.Controls.Add(control,1,row);
    }
    void BuildHistory() {
        var page=Page("History / 历史"); var panel=new TableLayoutPanel { Dock=DockStyle.Fill,RowCount=3,ColumnCount=1 };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); panel.RowStyles.Add(new RowStyle(SizeType.Percent,100)); panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var filters=Flow(); filters.Controls.Add(search); filters.Controls.Add(savedOnly); panel.Controls.Add(filters,0,0);
        var split=new SplitContainer { Dock=DockStyle.Fill,Orientation=Orientation.Horizontal,SplitterDistance=185 }; split.Panel1.Controls.Add(list); split.Panel2.Controls.Add(historyAnswer); panel.Controls.Add(split,0,1);
        var actions=Flow(); actions.Controls.Add(Button("Open lookup",OpenHistory)); actions.Controls.Add(Button("☆ Save / Unsave",delegate { var item=list.SelectedItem as Lookup; if(item!=null) { var favorite=history.SetFavorite(item,!item.Saved); if(!item.Saved) PrepareFavoriteAudio(favorite); RefreshHistory(); } })); actions.Controls.Add(Button("Export JSON",Export)); actions.Controls.Add(Button("Export Anki",ExportAnki));
        panel.Controls.Add(actions,0,2); page.Controls.Add(panel);
        search.TextChanged+=delegate { RefreshHistory(); }; savedOnly.CheckedChanged+=delegate { RefreshHistory(); };
        list.SelectedIndexChanged+=delegate { var item=list.SelectedItem as Lookup; historyAnswer.Text=item==null?"":item.Term+"\n"+item.Created+"   ·   "+item.Model+"\n\n"+item.Answer; };
        list.DoubleClick+=delegate { OpenHistory(); }; tabs.SelectedIndexChanged+=delegate { if(tabs.SelectedIndex==3) RefreshHistory(); };
    }
    void ApplySettings() {
        endpoint.Text=settings.BaseUrl; model.Text=settings.Model; key.Text=apiKey; prompt.Text=settings.Prompt.Replace("\r\n","\n").Replace("\n","\r\n");
        teacherPrompt.Text=(settings.TeacherPrompt??Settings.DefaultTeacherPrompt).Replace("\r\n","\n").Replace("\n","\r\n");
        refinePrompt.Text=(settings.RefinePrompt??Settings.DefaultRefinePrompt).Replace("\r\n","\n").Replace("\n","\r\n");
        include.Checked=settings.IncludeHistory; recentCount.Value=settings.HistoryCount; recentHours.Value=settings.HistoryHours;
        floating.Checked=settings.FloatingButton; proxy.Checked=settings.UseSystemProxy; maxTokens.Value=settings.MaxTokens; careful.Checked=settings.CarefulAnswers;
        quoteAllWindow.Checked=settings.QuoteAllWindow;
        if(!speechVoice.Items.Contains(settings.SpeechVoice)) speechVoice.Items.Add(settings.SpeechVoice);
        SetReadingControls();
        speechVoice.SelectedItem=settings.SpeechVoice; speechRate.Value=Math.Max(-50,Math.Min(50,settings.SpeechRate)); speechProxy.Text=settings.SpeechProxy??"";
        speechProvider.SelectedIndex=settings.SpeechProvider=="microsoft"?1:0;
        speechPreload.Checked=settings.SpeechPreload; speechScope.SelectedIndex=settings.SpeechPreloadScope=="original"?0:settings.SpeechPreloadScope=="all"?2:1;
    }
    Settings ReadSettings() {
        if(Program.IsRelease) return ReadReleaseSettings();
        var s=new Settings { BaseUrl=endpoint.Text.Trim(), Model=model.Text.Trim(), Prompt=prompt.Text.Replace("\r\n","\n"),TeacherPrompt=teacherPrompt.Text.Replace("\r\n","\n"), IncludeHistory=include.Checked, HistoryCount=(int)recentCount.Value, HistoryHours=(int)recentHours.Value, FloatingButton=floating.Checked, UseSystemProxy=proxy.Checked, MaxTokens=(int)maxTokens.Value,CarefulAnswers=careful.Checked,DarkMode=settings.DarkMode };
        s.SpeechVoice=Convert.ToString(speechVoice.SelectedItem); s.SpeechRate=(int)speechRate.Value; s.SpeechProxy=speechProxy.Text.Trim();
        s.SpeechProvider=BaiduVoice?"baidu-web":"microsoft";
        s.SpeechPreload=speechPreload.Checked; s.SpeechPreloadScope=SelectedSpeechScope;
        s.RefinePrompt=refinePrompt.Text.Replace("\r\n","\n");
        s.UiLanguage=languageChoice.SelectedIndex==1?"zh":"en"; s.FloatingButton=false;
        s.ReadingFontSize=(float)readingSize.Value; s.ReadingWidth=(int)readingWidth.Value; s.ReadingHeight=(int)readingHeight.Value;
        s.ScrollSpeed=(int)scrollSpeed.Value;
        s.QuoteAllWindow=quoteAllWindow.Checked;
        s.ReadingTopMost=readingTopMost.Checked;
        s.Validate(); return s;
    }
    bool SaveSettings(bool report) {
        if(Program.IsRelease) {
            var previous=settings;
            try {
                var s=ReadReleaseSettings(); s.Validate(); if(releaseHotkeySuspended) ResumeReleaseHotkey(); ApplyReleaseHotkey(s);
                Files.SaveKey(root,key.Text.Trim()); Files.Save(root,s); CancelReleaseAudio(false); settings=s; apiKey=key.Text.Trim(); SyncReleaseSpeechControls();
                Theme.SetPreferred(s.DarkMode); Theme.Apply(this); ApplyAppearance(s); RefreshReleaseLookupSettings(); MarkReleasePromptLibrarySaved();
                if(report) SetStatus("设置已保存，下次查询将使用当前提示词。",false);
                return true;
            } catch(Exception e) {
                try { ApplyReleaseHotkey(previous); } catch { }
                SetStatus("未能保存："+e.Message,true); return false;
            }
        }
        try { var s=ReadSettings(); Files.SaveKey(root,key.Text.Trim()); Files.Save(root,s); settings=s; apiKey=key.Text.Trim(); if(!s.FloatingButton && bubble!=null) bubble.Hide(); if(report) SetStatus("Settings saved. Your prompt will be used for the next lookup.",false); return true; }
        catch(Exception e) { SetStatus("Settings not saved: "+e.Message,true); return false; }
    }
    public void SetStatus(string text,bool error) { if(IsDisposed) return; status.Text=text; status.ForeColor=error?Look.Error:Look.Muted; }
    void StartCapture() {
        PrepareHotkey();
        if(Program.IsRelease) {
            var releaseMenu=new ContextMenuStrip();
            releaseMenu.Items.Add("历史与收藏",null,delegate { ShowReleaseHistory(); });
            releaseMenu.Items.Add("设置",null,delegate { Reveal(); });
            AddStartupMenu(releaseMenu); startAtSignIn.Text="开机启动";
            releaseMenu.Items.Add("退出",null,delegate { exiting=true; Close(); });
            tray=new NotifyIcon { Icon=Icon,Text=Program.NoHotkey?"Termlens · 测试（无快捷键）":"Termlens",Visible=true,ContextMenuStrip=releaseMenu };
            tray.DoubleClick+=delegate { Reveal(); };
            UiTrace.Write("release hotkey registered="+(releaseRegisteredId!=0)+" no-hotkey="+Program.NoHotkey);
            return;
        }
        UiTrace.Write("F13 registered="+registered.Contains(4));
        var menu=new ContextMenuStrip();
        Action<string,Action> add=delegate(string label,Action action) { var item=menu.Items.Add(UiLanguage.Translate(label),null,delegate { action(); }); item.Tag=label; };
        add("Open TermLens",delegate { EnsureWorkspace().FocusInput(true); });
        add("Last session",RestoreWorkspace);
        add("Settings",Reveal);
        AddStartupMenu(menu); startAtSignIn.Tag="Start at sign-in";
        add("Exit",delegate { exiting=true; Close(); });
        tray=new NotifyIcon { Icon=Icon,Text=Program.DisplayName+(Program.NoHotkey?" · no hotkey":" · F13"),Visible=true,ContextMenuStrip=menu };
        tray.DoubleClick+=delegate { EnsureWorkspace().FocusInput(true); };
        ApplyLanguage();
        if(Program.NoHotkey) BeginInvoke((Action)delegate { SetStatus(UiLanguage.T("Running without F13 (--no-hotkey). Other instances are unchanged.","已使用 --no-hotkey 启动，不注册 F13，不影响其他实例。"),false); });
    }
    internal void PrepareHotkey() {
        if(Program.IsRelease) {
            try { ApplyReleaseHotkey(settings); releaseHotkeyStartupError=null; }
            catch(InvalidOperationException e) {
                if(Program.IsTest) throw;
                releaseHotkeyStartupError=e.Message; UiTrace.Write("release shortcut unavailable at startup");
            }
            return;
        }
        if(Program.NoHotkey || (Program.IsTest && !Program.AllowTestHotkey) || registered.Contains(4)) return;
        Register(4,(uint)Keys.F13,0x4000);
        if(!registered.Contains(4)) throw new InvalidOperationException("F13 is already in use. Exit its owner normally, or start this copy with --no-hotkey. No other instance was closed.");
    }
    void Register(int id,uint keyCode,uint modifiers=0x4003) { if(Native.RegisterHotKey(Handle,id,modifiers,keyCode)) registered.Add(id); }
    protected override void WndProc(ref Message m) {
        if(m.Msg==(int)ExitMessage) { UiTrace.Write("exit command received"); exiting=true; Close(); return; }
        if(m.Msg==(int)ShowMessage) { if(Program.IsRelease || m.WParam==new IntPtr(1)) Reveal(); else EnsureWorkspace().FocusInput(true); return; }
        if(m.Msg==0x312) {
            int id=m.WParam.ToInt32(); var source=Native.GetForegroundWindow();
            if(Program.IsRelease) { /* Global hotkeys use the independent message window. */ }
            else if(id==4) BeginInvoke((Action)(async delegate { await CaptureWorkspace(source); }));
        }
        base.WndProc(ref m);
    }
    public void Reveal() {
        TopMost=Program.IsRelease ? releaseWindow!=null && !releaseWindow.IsDisposed && releaseWindow.TopMost : workspace!=null && !workspace.IsDisposed && workspace.TopMost;
        WindowActivation.Show(this);
    }
    async Task CaptureSelected(IntPtr source,bool clipboard,Rectangle? selectedBounds=null,bool inputWhenEmpty=false,SelectionAction action=SelectionAction.Explain) {
        if(inputWhenEmpty && quickInput.IsHandleCreated && source==quickInput.Handle) { ShowQuickInput(); return; }
        var activeCard=studyCards.Keys.FirstOrDefault(c=>!c.IsDisposed && c.Handle==source);
        if(!clipboard && activeCard!=null) {
            string selected=activeCard.SelectedAnswer;
            if(String.IsNullOrWhiteSpace(selected)) { if(inputWhenEmpty) ShowQuickInput(); else activeCard.SetStatus("Select a word in the original text or answer first.",false); return; }
            await RunStudyCard(activeCard,selected,action==SelectionAction.Ask,action==SelectionAction.Refine); return;
        }
        UiTrace.Write("capture requested busy="+(capturing || request!=null));
        if(capturing || request!=null) { if(popup.HasLookup) popup.ShowExisting(); SetStatus("A lookup is running. Stop it or wait for the answer.",false); return; }
        capturing=true;
        try {
            bubble.Hide(); CaptureResult result;
            Rectangle anchor=selectedBounds??(!clipboard?await Selection.AccessibleBounds(source):null)??Selection.ApproximateBounds(Cursor.Position,Cursor.Position);
            if(clipboard) result=Selection.Checked(Clipboard.ContainsText()?Clipboard.GetText():"","Clipboard");
            else if(Native.ProcessOf(source)==(uint)Process.GetCurrentProcess().Id) result=Selection.Checked(inputWhenEmpty?input.SelectedText:popup.ContainsFocus && popup.SelectedAnswer.Length>0?popup.SelectedAnswer:input.SelectedText.Length>0?input.SelectedText:input.Text,"Input window");
            else result=await Selection.Capture(source);
            UiTrace.Write("capture ready"); input.Text=result.Text; Hide(); SetStatus(result.Method,false);
            if(action==SelectionAction.Explain) await Explain(true,anchor);
            else await RunStudyCard(null,result.Text,action==SelectionAction.Ask,action==SelectionAction.Refine,anchor);
        } catch(NoSelectionException) { if(inputWhenEmpty) ShowQuickInput(); else { Hide(); popup.BeginLookup("No selected text",selectedBounds??Selection.ApproximateBounds(Cursor.Position,Cursor.Position)); popup.Failed("Select text first, or press F13 to type it."); } }
        catch(Exception e) { Hide(); popup.BeginLookup("Could not read selection",selectedBounds??Selection.ApproximateBounds(Cursor.Position,Cursor.Position)); popup.Failed(e.Message); SetStatus(e.Message,true); }
        finally { capturing=false; }
    }
    async Task Explain(bool compact=false,Rectangle? selectedBounds=null) {
        var window=EnsureWorkspace(selectedBounds);
        await SendWorkspace(window,WorkMode.Explain,input.Text,new List<WorkQuote>(),false);
    }
    void UpdateSaved() { saveWord.Text=current!=null && current.Saved?"★ Saved / 已加入":"☆ Save word / 加入生词"; if(current!=null) popup.SetSaved(current.Saved); }
    void ToggleCurrent() { if(current==null) return; try { history.SaveWord(current.Term,!current.Saved); current.Saved=!current.Saved; var favorite=history.SetFavorite(current,current.Saved); if(current.Saved) PrepareFavoriteAudio(favorite); UpdateSaved(); } catch(Exception e) { SetStatus(e.Message,true); } }
    void RefreshHistory() { try { list.BeginUpdate(); list.Items.Clear(); foreach(var item in history.Queries(search.Text,savedOnly.Checked,300)) list.Items.Add(item); } catch(Exception e) { SetStatus(e.Message,true); } finally { list.EndUpdate(); } }
    void OpenHistory() { var item=list.SelectedItem as Lookup; if(item==null) return; var mode=item.Kind=="ask"?WorkMode.Ask:item.Kind=="polish"?WorkMode.Polish:WorkMode.Explain; var window=EnsureWorkspace(); window.AddEntry(new WorkEntry { Text=item.Term,Answer=item.Answer,Mode=mode,Finished=true,Saved=item.Saved }); window.SaveNow(); }
    void Preview() {
        try {
            var s=ReadSettings(); var terms=history.Recent(s,input.Text);
            using(var dialog=new Form { Text="Request preview — no API call",Size=new Size(700,620),StartPosition=FormStartPosition.CenterParent,Font=Font }) {
                dialog.Controls.Add(new TextBox { Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,Text="SYSTEM INSTRUCTIONS:\r\n"+s.Prompt.Replace("\n","\r\n")+"\r\n\r\nUSER CONTENT:\r\n"+Requests.UserText(input.Text,terms).Replace("\n","\r\n") }); dialog.ShowDialog(this);
            }
        } catch(Exception e) { SetStatus(e.Message,true); }
    }
    void Export() {
        using(var dialog=new SaveFileDialog { Filter="JSON files|*.json",FileName="termlens-history-"+DateTime.Now.ToString("yyyyMMdd")+".json" }) {
            if(dialog.ShowDialog(this)!=DialogResult.OK) return;
            try { Files.Atomic(dialog.FileName,Files.Json().Serialize(history.Queries(search.Text,savedOnly.Checked,1000000))); SetStatus("Exported matching query records. API key is not included.",false); }
            catch(Exception e) { SetStatus(e.Message,true); }
        }
    }
    void ExportAnki() {
        using(var dialog=new SaveFileDialog { Filter="Anki UTF-8 text|*.txt",FileName="termlens-favorites-"+DateTime.Now.ToString("yyyyMMdd")+".txt" }) {
            if(dialog.ShowDialog(this)!=DialogResult.OK) return;
            try {
                string folder=Path.Combine(Path.GetDirectoryName(dialog.FileName),Path.GetFileNameWithoutExtension(dialog.FileName)+"-media"); Directory.CreateDirectory(folder);
                var lines=new List<string> { "#separator:Tab","#html:true","#columns:ID\tWord\tMeaning\tSource\tContext\tWord audio\tExplanation audio\tTags" };
                foreach(var item in history.Favorites()) {
                    var word=new List<string>(); foreach(string relative in item.WordAudio) { string source=Path.Combine(root,relative); string name=Path.GetFileName(source); if(File.Exists(source)) { File.Copy(source,Path.Combine(folder,name),true); word.Add("[sound:"+name+"]"); } }
                    var explanation=new List<string>(); foreach(string relative in item.ExplanationAudio) { string source=Path.Combine(root,relative); string name=Path.GetFileName(source); if(File.Exists(source)) { File.Copy(source,Path.Combine(folder,name),true); explanation.Add("[sound:"+name+"]"); } }
                    lines.Add(String.Join("\t",new[]{AnkiEscape(item.Id),AnkiEscape(item.Term),AnkiEscape(item.Explanation),AnkiEscape(item.SourceText),AnkiEscape(item.Context),AnkiEscape(String.Join(" ",word)),AnkiEscape(String.Join(" ",explanation)),"termlens "+AnkiEscape(item.Kind)}));
                }
                File.WriteAllLines(dialog.FileName,lines,Files.Utf8); SetStatus("Anki text and media exported. Import the text with Allow HTML enabled; keep its media folder beside the file.",false);
            } catch(Exception e) { SetStatus(e.Message,true); }
        }
    }
    static string AnkiEscape(string value) { return (value??"").Replace("\t"," ").Replace("\r","<br>").Replace("\n","<br>"); }
    void OnClosing(object sender,FormClosingEventArgs e) {
        if(!exiting && e.CloseReason==CloseReason.UserClosing) { e.Cancel=true; Hide(); return; }
        exiting=true; if(Program.IsRelease) CloseReleaseWorkflow(); else CloseWorkflow(); quickInput.Dispose(); StopSpeech(); DisposeSpeechCache(); if(request!=null) request.Cancel(); if(mouse!=null) mouse.Dispose(); if(bubble!=null) bubble.Dispose();
        foreach(var card in studyCards.Values) { if(card.Request!=null) card.Request.Cancel(); card.Window.Dispose(); }
        if(Program.IsRelease) DisposeReleaseHotkey();
        foreach(int id in registered) Native.UnregisterHotKey(Handle,id);
        if(tray!=null) { tray.Visible=false; tray.Dispose(); } history.Dispose();
        SaveWindowGeometry();
    }
    // Persist the real window rectangle; RestoreBounds keeps the normal size when
    // the window is closed while maximized or minimized.
    void SaveWindowGeometry() {
        try {
            var b=RestoreBounds;
            if(b.Width<=0 || b.Height<=0) return;
            var saved=Files.Load(root);
            saved.MainLeft=b.Left; saved.MainTop=b.Top; saved.MainWidth=b.Width; saved.MainHeight=b.Height;
            saved.WorkspaceLeft=settings.WorkspaceLeft; saved.WorkspaceTop=settings.WorkspaceTop;
            saved.WorkspaceWidth=settings.WorkspaceWidth; saved.WorkspaceHeight=settings.WorkspaceHeight;
            Files.Save(root,saved);
        } catch { }
    }
}
public static class Program {
    public static bool IsTest { get; private set; }
    public static bool OpenSettings { get; private set; }
    public static bool AllowTestHotkey { get; private set; }
    public static bool NoHotkey { get; private set; }
    public static bool IsRelease { get { return InstanceCoordinator.IsReleaseDirectory(AppDomain.CurrentDomain.BaseDirectory); } }
    public static string DisplayName { get { return IsRelease?"Termlens release":"TermLens v2"; } }
    static Mutex testMarker;
    [STAThread] public static int Main(string[] args) {
        IsTest=args.Any(a=>a.EndsWith("-test") || a.EndsWith("-check") || a.EndsWith("-fixture") || a.EndsWith("-live"));
        OpenSettings=args.Contains("--settings"); AllowTestHotkey=args.Contains("--allow-test-hotkey"); NoHotkey=args.Contains("--no-hotkey");
        if(IsTest) testMarker=new Mutex(false,"Local\\TermLens.Test."+Process.GetCurrentProcess().Id);
        WindowsAppIdentity.Configure(IsRelease,IsTest);
        ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
        Native.SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        Theme.Start();
        string baseDir=AppDomain.CurrentDomain.BaseDirectory;
        if(args.Contains("--wpf")) {
            // The experimental WPF shell was archived to src\wpf\ and is no longer
            // compiled. Fail loudly instead of silently starting the WinForms shell.
            if(!args.Contains("--quiet")) MessageBox.Show("The WPF shell was removed from this build.\nIt now lives uncompiled in src\\wpf\\.","TermLens",MessageBoxButtons.OK,MessageBoxIcon.Information);
            return 2;
        }
        if(args.Contains("--startup-enable") || args.Contains("--startup-disable")) {
            try { StartupOption.Current().SetEnabled(args.Contains("--startup-enable")); return 0; }
            catch(Exception e) { if(!args.Contains("--quiet")) MessageBox.Show(e.Message,"TermLens startup",MessageBoxButtons.OK,MessageBoxIcon.Information); return 1; }
        }
        // termlens_ds: the WinForms shell is the only desktop entry.
        // The experimental WPF shell lives in src\wpf\ and is not compiled.
#if !SHARE
        if(args.Length>0 && args[0]=="--reading-format-test") return Tests.ReadingFormat(baseDir);
        if(args.Length>0 && args[0]=="--scenario-acceptance-test") return Tests.ScenarioAcceptance(baseDir);
        if(args.Length>0 && args[0]=="--prompt-library-test") return Tests.PromptLibraryStorage(baseDir);
        if(args.Length>0 && args[0]=="--release-prompt-library-ui-test") return Tests.ReleasePromptLibraryUi(baseDir);
        if(args.Length>0 && args[0]=="--edge-speech-test") return Tests.EdgeSpeechProtocol(baseDir);
        if(args.Length>0 && args[0]=="--release-edge-audio-live") return Tests.ReleaseEdgeAudioLive(baseDir);
        if(args.Length>0 && args[0]=="--release-window-history-test") return ReleaseWindowHistoryTests.Run(baseDir);
        if(args.Length>0 && args[0]=="--release-audio-button-test") return ReleaseAudioButtonTests.Run(baseDir);
        if(args.Length>0 && args[0]=="--release-audio-latency-live") return Tests.ReleaseAudioLatency(baseDir);
        if(args.Length>0 && args[0]=="--release-retention-test") return ReleaseRetentionTests.Run(baseDir);
        if(args.Length>0 && args[0]=="--release-audio-playback-test") return Tests.ReleaseAudioPlayback(baseDir);
        if(args.Length>0 && args[0]=="--release-audio-microsoft-live") return Tests.ReleaseAudioMicrosoft(baseDir);
        if(args.Length>0 && args[0]=="--release-audio-test") return Tests.ReleaseAudio(baseDir);
        if(args.Length>0 && args[0]=="--release-audio-windows-test") return Tests.ReleaseAudioWindows(baseDir);
        if(args.Length>0 && args[0]=="--release-topic-test") return ReleaseTopicContextTests.Run(baseDir);
        if(args.Length>0 && args[0]=="--release-scroll-test") return Tests.ReleaseScroll(baseDir);
        if(args.Length>0 && args[0]=="--release-nested-test") return Tests.ReleaseNested(baseDir);
        if(args.Length>0 && args[0]=="--release-hotkey-test") return Tests.ReleaseHotkey(baseDir);
        if(args.Length>0 && args[0]=="--release-shortcut-test") return Tests.ReleaseShortcut(baseDir);
        if(args.Length>0 && args[0]=="--release-mouse-shortcut-test") return Tests.ReleaseMouseShortcut(baseDir);
        if(args.Length>0 && args[0]=="--release-workflow-test") return Tests.ReleaseWorkflow(baseDir);
        if(args.Length>0 && args[0]=="--release-settings-test") return Tests.ReleaseSettings(baseDir);
        if(args.Length>0 && args[0]=="--lookup-context-test") return LookupContextTests.Run(baseDir);
        if(args.Length>0 && args[0]=="--instance-policy-test") return InstanceCoordinator.PolicyTest(baseDir);
        if(args.Length>0 && args[0]=="--v2-performance-test") return Tests.V2Performance(baseDir);
        if(args.Length>0 && args[0]=="--v2-ui-test") return Tests.V2Ui(baseDir);
        if(args.Length>0 && args[0]=="--v2-settings-test") return Tests.V2Settings(baseDir);
        if(args.Length>0 && args[0]=="--context-quotes-test") return Tests.ContextQuotes(baseDir);
        if(args.Length>0 && args[0]=="--library-test") return Tests.Library(baseDir);
        if(args.Length>0 && args[0]=="--layout-test") return Tests.Layout(baseDir);
        if(args.Length>0 && args[0]=="--pages-test") return Tests.Pages(baseDir);
        if(args.Length>0 && args[0]=="--image-test") return Tests.Images(baseDir,false);
        if(args.Length>0 && args[0]=="--image-live") return Tests.Images(baseDir,true);
        if(args.Length>0 && args[0]=="--markdown-test") return Tests.MarkdownUi(baseDir);
        if(args.Length>0 && args[0]=="--rendering-test") return Tests.Rendering(baseDir);
        if(args.Length>0 && args[0]=="--font-regression-test") return Tests.ReadingFonts(baseDir);
        if(args.Length>0 && args[0]=="--web-speech-test") return Tests.WebVoice(baseDir);
        if(args.Length>0 && args[0]=="--speech-cache-test") return Tests.SpeechCaching(baseDir,false);
        if(args.Length>0 && args[0]=="--speech-cache-live") return Tests.SpeechCaching(baseDir,true);
        if(args.Length>0 && args[0]=="--workspace-test") return Tests.Workspace(baseDir);
        if(args.Length>0 && args[0]=="--editor-test") return Tests.EditorWorkflow(baseDir);
        if(args.Length>0 && args[0]=="--dictionary-check") return Tests.DictionaryLive(baseDir);
        if(args.Length>0 && args[0]=="--startup-test") return Tests.Startup(baseDir);
        if(args.Length>0 && args[0]=="--self-test") return Tests.Run(baseDir);
        if(args.Length>0 && args[0]=="--ui-test") return Tests.Ui(baseDir);
        if(args.Length>0 && args[0]=="--selection-fixture") { Tests.Fixture(); return 0; }
        if(args.Length>0 && args[0]=="--focus-fixture") { Tests.Fixture(false); return 0; }
        if(args.Length>0 && args[0]=="--capture-test") return Tests.Capture(baseDir);
        if(args.Length>0 && args[0]=="--popup-test") return Tests.Popup(root:baseDir);
        if(args.Length>0 && args[0]=="--hotkey-test") return Tests.Hotkeys(baseDir);
        if(args.Length>0 && args[0]=="--study-test") return Tests.Study(baseDir);
        if(args.Length>0 && args[0]=="--reading-test") return Tests.Reading(baseDir);
        if(args.Length>0 && args[0]=="--teacher-check") return Tests.LiveTeacher(baseDir);
        if(args.Length>0 && args[0]=="--refine-check") return Tests.LiveRefine(baseDir);
        if(args.Length>0 && args[0]=="--speech-test") return Tests.Speech(baseDir,false);
        if(args.Length>0 && args[0]=="--speech-check") return Tests.Speech(baseDir,true);
        if(args.Length>0 && args[0]=="--theme-test") return Tests.ThemeUi(baseDir);
        if(args.Length>0 && args[0]=="--prompt-check") return Tests.LivePrompt(baseDir);
#endif
        if(IsTest) return 2; // Unknown test switches must never start a real instance.
        return InstanceCoordinator.Run(baseDir,args);
    }
}
}
