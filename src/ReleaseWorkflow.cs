using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TermLens {
public sealed partial class MainWindow {
    ReleaseLookupWindow releaseWindow;
    ReleaseLookupWindow releaseSecondaryWindow;
    CancellationTokenSource releaseRequest;
    CancellationTokenSource releaseSecondaryRequest;
    int releaseGeneration;
    int releaseSecondaryGeneration;
    bool releaseCapturing;
    string releaseSpeechGroup="";
    string releaseSecondarySpeechGroup="";
    ReleaseTopicContext releaseTopics=new ReleaseTopicContext();
    string releaseTopicProfile="";

    void InitReleaseWorkflow() { releaseGeneration=0; InitReleaseRetention(); }
    void ReleaseNotice(string text,bool error) {
        SetStatus(text,error);
        if(releaseWindow!=null && !releaseWindow.IsDisposed && releaseWindow.Visible) releaseWindow.SetNotice(text,error);
        else if(tray!=null && tray.Visible) tray.ShowBalloonTip(1800,"TermLens",text,error?ToolTipIcon.Warning:ToolTipIcon.Info);
    }
    void CancelReleaseActivity(bool secondary=false) {
        CancelReleaseAudio(secondary);
        if(!secondary) CloseReleaseSecondary();
        if(secondary) releaseSecondaryGeneration++; else releaseGeneration++;
        var request=secondary?releaseSecondaryRequest:releaseRequest;
        if(secondary) releaseSecondaryRequest=null; else releaseRequest=null;
        if(request!=null) request.Cancel();
        var group=secondary?releaseSecondarySpeechGroup:releaseSpeechGroup;
        if(group.Length>0) StopSpeechPreload(group);
        if(secondary) releaseSecondarySpeechGroup=""; else releaseSpeechGroup="";
        var speakingWindow=speechWindow as ReleaseLookupWindow;
        if(!secondary || speakingWindow!=null && speakingWindow.IsTransient) StopSpeech();
    }
    void CloseReleaseSecondary() {
        var window=releaseSecondaryWindow; releaseSecondaryWindow=null;
        if(window!=null && !window.IsDisposed) { CancelReleaseActivity(true); window.Close(); window.Dispose(); }
    }
    ReleaseLookupWindow EnsureReleaseWindow(bool secondary=false) {
        var existing=secondary?releaseSecondaryWindow:releaseWindow;
        if(existing!=null && !existing.IsDisposed) return existing;
        var window=new ReleaseLookupWindow(settings,secondary);
        if(secondary) { releaseSecondaryWindow=window; window.Owner=releaseWindow; window.SyncOwnerLayer(); } else releaseWindow=window;
        window.SettingsRequested+=delegate { Reveal(); };
        if(secondary) window.GeometryChanged+=delegate(Rectangle bounds) {
            int width=(int)Math.Round(bounds.Width/Look.Scale),height=(int)Math.Round(bounds.Height/Look.Scale);
            if(width<=0 || height<=0) return;
            settings.SecondaryWindowWidth=width; settings.SecondaryWindowHeight=height;
            try {
                var saved=Files.Load(root); saved.SecondaryWindowWidth=width; saved.SecondaryWindowHeight=height;
                Files.Save(root,saved);
            } catch(Exception error) { SetStatus(error.Message,true); }
        };
        if(!secondary) {
            window.HistoryProvider=delegate { MaintainReleaseHistory(); return history.ReleaseHistoryIndex(); };
            window.HistoryItemProvider=delegate(Lookup entry) { return history.ReleaseHistoryEntry(entry.Id)??entry; };
            window.HistoryViewChanged+=delegate {
                CloseReleaseSecondary(); StopReleaseWindowPlayback(window);
                if(window.Current!=null) { var favorite=history.FindFavorite(window.Current.Kind,window.Current.Term); window.Current.Saved=favorite!=null && favorite.Saved; }
            };
            window.PinChanged+=delegate(bool pinned) {
                PersistTopMost(pinned);
                if(releaseSecondaryWindow!=null && !releaseSecondaryWindow.IsDisposed) releaseSecondaryWindow.SyncOwnerLayer();
            };
            window.GeometryChanged+=delegate(Rectangle bounds) { PersistWorkspaceGeometry(bounds.Left,bounds.Top,bounds.Width,bounds.Height); };
        }
        window.FontSizeChanged+=delegate(float font) {
            if(secondary) return;
            settings.ReadingFontSize=font;
            try { var saved=Files.Load(root); saved.ReadingFontSize=font; Files.Save(root,saved); }
            catch(Exception e) { window.SetNotice(e.Message,true); }
        };
        window.StopRequested+=delegate {
            if(window.IsBrowsingHistory) { StopReleaseWindowPlayback(window); window.SetSpeaking(false,UiLanguage.T("Audio stopped","朗读已停止")); return; }
            CancelReleaseActivity(secondary);
            if(window.OriginalLookup!=null && window.OriginalLookup.Status=="pending") { window.OriginalLookup.Status="stopped"; window.UpdateAnswer(); window.Finish(false,UiLanguage.T("Stopped. Select text and use the shortcut to try again.","已停止。选中文字后按快捷键可重新查询。")); }
            else window.SetSpeaking(false,UiLanguage.T("Audio stopped","朗读已停止"));
        };
        window.SpeechRequested+=async delegate(string text) {
            await SpeakReleaseAudio(window,text);
        };
        window.CancelSpeechRequested+=delegate { StopReleaseWindowPlayback(window); window.SetSpeaking(false,null); };
        window.SaveRequested+=delegate {
            var item=window.Current; if(item==null || item.Status!="complete") return;
            try {
                var found=history.FindFavorite(item.Kind,item.Term);
                item.Saved=!(found!=null && found.Saved);
                var saved=history.SetFavorite(item,item.Saved); SaveReleaseFavoriteAudio(saved); window.RefreshActions(); RefreshReleaseHistory();
            } catch(Exception e) { window.SetNotice(e.Message,true); }
        };
        window.FormClosed+=delegate {
            if(secondary) { if(releaseSecondaryWindow==window) { releaseSecondaryWindow=null; CancelReleaseActivity(true); } }
            else if(releaseWindow==window) { CancelReleaseActivity(); releaseWindow=null; }
        };
        return window;
    }
    public void RefreshReleaseLookupSettings() {
        if(releaseWindow!=null && !releaseWindow.IsDisposed) releaseWindow.ApplySettings(settings);
        if(releaseSecondaryWindow!=null && !releaseSecondaryWindow.IsDisposed) releaseSecondaryWindow.ApplySettings(settings);
    }
    public void OpenReleaseLookup(Lookup item) {
        if(item==null || exiting) return;
        CancelReleaseActivity();
        var window=EnsureReleaseWindow();
        var favorite=history.FindFavorite(item.Kind,item.Term); item.Saved=favorite!=null && favorite.Saved;
        window.ShowLookup(item,LookupContext.FromSaved(item.Context)); window.ShowAt(new Rectangle(Cursor.Position,new Size(1,1)));
    }
    void CloseReleaseWorkflow() {
        CancelReleaseActivity();
        DisposeReleaseAudio();
        var window=releaseWindow; releaseWindow=null;
        if(window!=null && !window.IsDisposed) { window.Close(); window.Dispose(); }
    }
    async Task CaptureRelease(IntPtr source) {
        if(releaseCapturing || exiting) return;
        // A shortcut being recorded belongs to its input control. Settings
        // editors never become reading selections, even if text is highlighted.
        if(releaseHotkeySuspended || releaseTrigger.IsRecording) return;
        if(IsHandleCreated && (source==Handle || Native.GetAncestor(source,2)==Handle)) { Hide(); return; }
        UiTrace.Write("release capture started sourcePid="+Native.ProcessOf(source));
        releaseCapturing=true; int captureGeneration=releaseGeneration;
        string text=null; Rectangle anchor=new Rectangle(Cursor.Position,new Size(1,1)); LookupContext context=null;
        bool secondary=false;
        PromptPreset profile=null; bool legacyProfileFallback=false;
        try {
            var reading=releaseSecondaryWindow!=null && !releaseSecondaryWindow.IsDisposed && source==releaseSecondaryWindow.Handle?releaseSecondaryWindow:
                releaseWindow!=null && !releaseWindow.IsDisposed && source==releaseWindow.Handle?releaseWindow:null;
            if(reading!=null) {
                secondary=true;
                text=reading.SelectedText(out anchor);
                if(String.IsNullOrWhiteSpace(text)) { reading.Close(); return; }
                var parent=reading.Current;
                profile=parent==null?null:ReleasePromptSnapshot.FromSaved(parent.Context);
                legacyProfileFallback=profile==null;
                profile=profile??ReleasePromptSnapshot.Active(settings);
                if(parent!=null) context=reading.ParentContext==null
                    ?LookupContext.FromSource(parent.Id,parent.Term,parent.Answer,text)
                    :reading.ParentContext.Next(parent.Term,parent.Answer,text);
                text=Selection.Checked(text,"Explanation selection").Text;
            } else {
                // Freeze both prompts before awaiting an external capture. A
                // settings change while capturing cannot change this query.
                profile=ReleasePromptSnapshot.Active(settings);
                if(Native.ProcessOf(source)==(uint)Process.GetCurrentProcess().Id) throw new NoSelectionException();
                var bounds=Selection.AccessibleBounds(source,200);
                var captured=await Selection.Capture(source); text=captured.Text;
                anchor=(await bounds)??anchor;
            }
            UiTrace.Write("release selection captured length="+(text??"").Length+" contextual="+(context!=null));
            if(exiting || captureGeneration!=releaseGeneration) return;
        } catch(NoSelectionException e) {
            UiTrace.Write("release capture no selection");
            if(exiting || captureGeneration!=releaseGeneration) return;
            if(e.ConfirmedEmpty) {
                if(Visible) Hide();
                else if(releaseWindow!=null && !releaseWindow.IsDisposed && releaseWindow.Visible) releaseWindow.Close();
                else if(releaseSecondaryWindow!=null && !releaseSecondaryWindow.IsDisposed && releaseSecondaryWindow.Visible) releaseSecondaryWindow.Close();
                else ShowReleasePromptLibrary();
            }
            else ReleaseNotice(UiLanguage.T("Could not confirm the selection. Select the text again, then try the lookup shortcut.","未能确认选区，请重新选中文字后再按查词快捷键。"),true);
            return;
        } catch(Exception e) { UiTrace.Write("release capture failed type="+e.GetType().Name); ReleaseNotice(e.Message,true); return; }
        finally { releaseCapturing=false; }
        await RunReleaseLookup(text,context,anchor,secondary,profile,legacyProfileFallback);
    }
    internal async Task RunReleaseLookup(string text,LookupContext context,Rectangle anchor,bool secondary=false,PromptPreset profile=null,bool legacyProfileFallback=false) {
        Settings use; LookupContextRequest query;
        ReleaseTopicContext topicContext=null;
        string secret=(apiKey??"").Trim();
        try {
            text=Selection.Checked(text,"Selected text").Text;
            use=Files.Json().Deserialize<Settings>(Files.Json().Serialize(settings)); use.IncludeHistory=false;
            profile=profile==null?ReleasePromptSnapshot.Active(use):PromptLibrary.Clone(profile);
            if(String.IsNullOrWhiteSpace(secret)) throw new Exception(UiLanguage.T("Add your API key in Settings first.","请先在设置中填写 API 密钥。"));
            if(String.IsNullOrWhiteSpace(use.Prompt)) throw new Exception(UiLanguage.T("Add a lookup prompt in Settings.","请先在设置中填写查词提示词。"));
            use.Prompt=ReleasePrompt.Build(profile,secondary,context!=null);
            query=LookupContext.Build(text,context,null);
            if(!secondary && context==null) {
                string signature=ReleasePromptSnapshot.Signature(profile);
                if(signature!=releaseTopicProfile) {
                    releaseTopics=new ReleaseTopicContext(); releaseTopicProfile=signature;
                }
                topicContext=releaseTopics;
                var topic=topicContext.Build(text,DateTime.UtcNow);
                query.UserText=topic.UserText;
                var snapshot=Files.Json().Deserialize<Dictionary<string,object>>(query.ContextJson);
                snapshot["topicReference"]=topic.RecentSelections;
                snapshot["requestUserMessage"]=query.UserText;
                query.ContextJson=Files.Json().Serialize(snapshot);
            }
            query.ContextJson=ReleasePromptSnapshot.Attach(query.ContextJson,profile,legacyProfileFallback);
        } catch(Exception e) {
            UiTrace.Write("release request validation failed type="+e.GetType().Name);
            ReleaseNotice(e.Message,true);
            if(String.IsNullOrWhiteSpace(secret)) { SelectReleasePage(0); Reveal(); }
            return;
        }
        CancelReleaseActivity(secondary);
        var cancel=new CancellationTokenSource();
        if(secondary) releaseSecondaryRequest=cancel; else releaseRequest=cancel;
        int generation=secondary?releaseSecondaryGeneration:releaseGeneration;
        Func<bool> active=()=>!exiting && !cancel.IsCancellationRequested && generation==(secondary?releaseSecondaryGeneration:releaseGeneration);
        var item=new Lookup { Id=Guid.NewGuid().ToString("N"),Term=text,Answer="",Created=DateTime.UtcNow.ToString("o"),Model=use.Model,Prompt=use.Prompt,Context=query.ContextJson,SourceText=String.IsNullOrEmpty(query.SourceText)?text:query.SourceText,Kind=context==null?"explain":"quick",Source=context==null?"release-selection":"release-context",Status="pending" };
        history.RecordSelection(item);
        var window=EnsureReleaseWindow(secondary); window.ShowLookup(item,context); window.ShowAt(anchor);
        if(secondary) releaseSecondarySpeechGroup=item.Id; else releaseSpeechGroup=item.Id;
        PreloadReleaseAudio(window,text,"",item);
        bool dirty=false;
        var render=new System.Windows.Forms.Timer { Interval=85 };
        render.Tick+=delegate { if(dirty && active() && !window.IsDisposed) { dirty=false; window.UpdateAnswer(); } }; render.Start();
        try {
            item.Answer=await Requests.Complete(use,secret,query.UserText,query.RecentTerms,delegate(string full) {
                if(active()) { item.Answer=full; dirty=true; }
            },cancel.Token,requestHandlers==null?null:requestHandlers());
            if(!active() || window.IsDisposed) return;
            item.Status="complete";
            if(topicContext!=null) topicContext.RecordCompleted(text,DateTime.UtcNow);
            UiTrace.Write("release request completed length="+item.Answer.Length);
            string legacyId=null;
            if(context==null) { var legacy=history.Add(item.Term,item.Answer,item.Model,item.Prompt,item.Context); legacyId=legacy.Id; }
            history.RecordQuery(item.Id,item.Kind,item.Source,item.Term,item.Answer,item.SourceText,item.Context,item.Model,item.Prompt,item.Created,"complete","",legacyId);
            var favorite=history.FindFavorite(item.Kind,item.Term); item.Saved=favorite!=null && favorite.Saved;
            window.UpdateAnswer(); window.Finish(true,query.Truncated?UiLanguage.T("Long context was shortened around relevant text.","已按相关位置截取较长上下文。"):""); RefreshReleaseHistory();
            PreloadReleaseAudio(window,text,Markdown.PlainText(item.Answer),item);
        } catch(Exception e) {
            if(active() && !window.IsDisposed) {
                item.Status="failed"; string message=Requests.SafeError(e.Message,secret);
                UiTrace.Write("release request failed type="+e.GetType().Name);
                window.UpdateAnswer(); window.Finish(false,message); StopSpeechPreload(item.Id);
                try { history.RecordQuery(item.Id,item.Kind,item.Source,item.Term,item.Answer,item.SourceText,item.Context,item.Model,item.Prompt,item.Created,"failed",message); RefreshReleaseHistory(); } catch { }
            }
        } finally {
            render.Stop(); render.Dispose();
            if(secondary) { if(releaseSecondaryRequest==cancel) releaseSecondaryRequest=null; }
            else if(releaseRequest==cancel) releaseRequest=null;
            cancel.Dispose();
        }
    }
}

public sealed partial class ReleaseLookupWindow : Form {
    readonly bool transient;
    internal bool IsTransient { get { return transient; } }
    public readonly StudyText Original=new StudyText(),Answer=new StudyText();
    readonly Panel header=new Panel(),footer=new Panel(),viewport=new Panel { Name="ReleaseReadingViewport" };
    readonly TimelinePanel timeline=new TimelinePanel();
    readonly SlimScrollBar scrollbar=new SlimScrollBar();
    readonly Label title=new Label { Text="TermLens" },notice=new Label { AutoEllipsis=true };
    readonly SoftButton save=new SoftButton("Save",false),copy=new SoftButton("Copy",false),readOriginal=new SoftButton("Original",false),readAnswer=new SoftButton("Answer",false),stop=new SoftButton("Stop",false),pin=new SoftButton("Pin",false),settingsButton=new SoftButton("Settings",false),close=new SoftButton("Close",false);
    readonly ToolTip tips=new ToolTip { InitialDelay=1000,ReshowDelay=1000,AutoPopDelay=6000 };
    bool laying,syncing,speaking;
    float readingPoints;
    StudyText selection;
    SoftButton activeSpeechButton;
    Size shapedWindowSize;
    int shapedWindowRadius;
    Size secondaryPreferredSize;
    Size secondaryResizeStart;
    bool trackingSecondaryResize;
    public Lookup Current { get; private set; }
    public LookupContext ParentContext { get; private set; }
    public event Action SaveRequested,SettingsRequested,StopRequested;
    public event Action<string> SpeechRequested;
    public event Action CancelSpeechRequested;
    public event Action<bool> PinChanged;
    public event Action<Rectangle> GeometryChanged;
    public event Action<float> FontSizeChanged;
    public ReleaseLookupWindow(Settings settings,bool secondary=false) {
        transient=secondary;
        Text="TermLens"; FormBorderStyle=FormBorderStyle.None; AutoScaleMode=AutoScaleMode.None;
        StartPosition=FormStartPosition.Manual; ShowInTaskbar=!transient; DoubleBuffered=true; Padding=new Padding(Look.S(5));
        MinimumSize=new Size(Look.S(340),Look.S(260));
        Size=new Size(Look.S(Math.Max(360,settings.ReadingWidth)),Look.S(settings.ReadingHeight>0?Math.Max(260,settings.ReadingHeight):520));
        Rectangle bounds;
        if(WindowGeometry.TryRestore(settings.WorkspaceLeft,settings.WorkspaceTop,settings.WorkspaceWidth,settings.WorkspaceHeight,MinimumSize,out bounds)) Size=bounds.Size;
        if(transient) {
            MinimumSize=new Size(Look.S(300),Look.S(220));
            int width=settings.SecondaryWindowWidth>0?settings.SecondaryWindowWidth:360;
            int height=settings.SecondaryWindowHeight>0?settings.SecondaryWindowHeight:320;
            secondaryPreferredSize=new Size(Look.S(Math.Max(300,Math.Min(4000,width))),Look.S(Math.Max(220,Math.Min(4000,height))));
            Size=secondaryPreferredSize; title.Text="TermLens · 词义";
        }
        SetStyle(ControlStyles.ResizeRedraw,true);
        Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        header.Dock=DockStyle.Top; header.Height=Look.S(38); footer.Dock=DockStyle.Bottom; footer.Height=Look.S(64); viewport.Dock=DockStyle.Fill;
        Controls.AddRange(new Control[]{viewport,header,footer});
        viewport.Controls.AddRange(new Control[]{timeline,scrollbar}); timeline.Controls.AddRange(new Control[]{Original,readOriginal,Answer});
        header.Controls.AddRange(new Control[]{title,pin,settingsButton,close});
        footer.Controls.AddRange(new Control[]{save,copy,readAnswer,stop,notice});
        title.Font=new Font("Segoe UI",9,FontStyle.Bold); notice.Font=new Font("Microsoft YaHei UI",9);
        foreach(var text in new[]{Original,Answer}) {
            var box=text;
            box.Enter+=delegate { selection=box; var other=box==Original?Answer:Original; other.Select(other.SelectionStart,0); };
            box.SelectionChanged+=delegate { if(!box.ChangingContent && box.Focused) selection=box; };
            box.ZoomRequested+=delegate(int amount) { SetFontSize(readingPoints+amount); if(FontSizeChanged!=null) FontSizeChanged(readingPoints); };
        }
        save.Click+=delegate { if(SaveRequested!=null) SaveRequested(); };
        copy.Click+=delegate { try { if(Current!=null && !String.IsNullOrEmpty(Current.Answer)) Clipboard.SetText(Current.Answer); SetNotice(UiLanguage.T("Copied","已复制"),false); } catch { SetNotice(UiLanguage.T("Clipboard busy. Try again.","剪贴板忙，请重试。"),true); } };
        readOriginal.Click+=delegate { SpeakSelection(Original,Current==null?"":Current.Term); };
        readAnswer.Click+=delegate { SpeakSelection(Answer,Current==null?"":Markdown.PlainText(Current.Answer)); };
        stop.Click+=delegate { if(StopRequested!=null) StopRequested(); };
        pin.Click+=delegate { TopMost=!TopMost; pin.Primary=TopMost; pin.Invalidate(); if(PinChanged!=null) PinChanged(TopMost); };
        settingsButton.Click+=delegate { if(SettingsRequested!=null) SettingsRequested(); };
        close.Click+=delegate { Close(); };
        title.MouseDown+=Drag; header.MouseDown+=Drag;
        timeline.PositionChanged+=SyncScroll;
        scrollbar.ValueChanged+=delegate { if(!syncing) timeline.AutoScrollPosition=new Point(0,scrollbar.Value); };
        Resize+=delegate { LayoutWindow(); };
        ResizeBegin+=delegate { if(transient) { secondaryResizeStart=Size; trackingSecondaryResize=true; } };
        ResizeEnd+=delegate {
            if(WindowState!=FormWindowState.Normal) return;
            if(transient && (!trackingSecondaryResize || Size!=secondaryResizeStart)) secondaryPreferredSize=Size;
            trackingSecondaryResize=false;
            if(GeometryChanged!=null) GeometryChanged(transient?new Rectangle(Location,secondaryPreferredSize):Bounds);
        };
        FormClosing+=delegate {
            if(WindowState==FormWindowState.Normal && GeometryChanged!=null)
                GeometryChanged(transient?new Rectangle(Location,secondaryPreferredSize):Bounds);
        };
        Deactivate+=delegate {
            if(!transient || IsDisposed || Disposing || !IsHandleCreated) return;
            BeginInvoke((Action)delegate {
                if(!IsDisposed && !Disposing && Visible && Native.GetForegroundWindow()!=Handle) Close();
            });
        };
        InitWindowHistory(); ApplySettings(settings); LayoutWindow();
    }
    void SpeakSelection(StudyText box,string fallback) {
        var button=box==Original?readOriginal:readAnswer;
        if(speaking && activeSpeechButton==button) {
            SetSpeaking(false,UiLanguage.T("Audio stopped","朗读已停止"));
            PaintSpeechFeedback(button);
            if(CancelSpeechRequested!=null) CancelSpeechRequested();
            return;
        }
        if(SpeechRequested==null) return;
        string text=String.IsNullOrWhiteSpace(box.SelectedText)?fallback:box.SelectedText;
        if(String.IsNullOrWhiteSpace(text)) return;
        if(speaking && CancelSpeechRequested!=null) CancelSpeechRequested();
        activeSpeechButton=button; speaking=true; SetNotice(UiLanguage.T("Preparing audio…","正在准备音频…"),false); RefreshActions(); PaintSpeechFeedback(button);
        try { SpeechRequested(text); }
        catch { SetSpeaking(false,null); throw; }
    }
    void PaintSpeechFeedback(SoftButton button) {
        // Update only these two small dirty controls before invoking the audio
        // callback; do not pump messages or reflow the reading content.
        if(button.IsHandleCreated && button.Visible) button.Update();
        if(notice.IsHandleCreated && notice.Visible) notice.Update();
    }
    void UpdateActionIcon(SoftButton button,string icon,string label,bool primary) {
        bool iconChanged=button.IconName!=icon || button.AccessibleName!=label || !button.Quiet;
        bool highlightChanged=button.Primary!=primary; button.Primary=primary;
        if(iconChanged) V2Icons.Set(button,icon,label,tips);
        else if(highlightChanged) button.Invalidate();
    }
    public string SelectedText(out Rectangle anchor) {
        if(IsHistoryList) { anchor=new Rectangle(Cursor.Position,new Size(1,1)); return ""; }
        var box=selection;
        if(box==null || box.SelectionLength==0) box=Answer.SelectionLength>0?Answer:Original;
        anchor=box.SelectionLength>0?box.SelectionAnchor():new Rectangle(Cursor.Position,new Size(1,1));
        return box.SelectedText;
    }
    public void ApplySettings(Settings settings) {
        TopMost=transient?(Owner!=null && Owner.TopMost):settings.ReadingTopMost; timeline.SpeedPercent=settings.ScrollSpeed;
        pin.Visible=settingsButton.Visible=!transient;
        SetFontSize(settings.ReadingFontSize);
        V2Icons.Set(save,"save",UiLanguage.T("Save this result","收藏此结果"),tips);
        V2Icons.Set(copy,"copy",UiLanguage.T("Copy the full answer","复制完整回答"),tips);
        V2Icons.Set(readOriginal,"listen",UiLanguage.T("Read the original text or its selected words","朗读原文或其中选中的词"),tips);
        V2Icons.Set(readAnswer,"listen",UiLanguage.T("Read the answer or its selected words","朗读回答或其中选中的词"),tips);
        V2Icons.Set(stop,"stop",UiLanguage.T("Stop lookup and audio","停止查询与朗读"),tips);
        V2Icons.Set(pin,"pin",UiLanguage.T("Keep on top","窗口置顶"),tips);
        V2Icons.Set(settingsButton,"settings",UiLanguage.T("Settings","设置"),tips);
        V2Icons.Set(close,"close",UiLanguage.T("Close result","关闭结果"),tips);
        tips.SetToolTip(Answer,null); tips.SetToolTip(Original,null);
        pin.Primary=TopMost; Theme.Apply(this); ApplyWindowHistoryTheme(); Original.ForeColor=Look.Muted; RefreshActions(); LayoutWindow();
    }
    internal void SyncOwnerLayer() {
        if(transient && Owner!=null && !Owner.IsDisposed) TopMost=Owner.TopMost;
    }
    void SetFontSize(float points) {
        readingPoints=Math.Max(9,Math.Min(28,points));
        Original.ApplyReadingFont(Math.Max(9,readingPoints-1)); Answer.ApplyReadingFont(readingPoints); LayoutWindow();
    }
    public void ShowAt(Rectangle anchor) {
        SyncOwnerLayer();
        // Each selection determines placement; only the preferred size is reused.
        // Streaming updates never call ShowAt and cannot move the window.
        var work=Screen.FromRectangle(anchor).WorkingArea;
        MinimumSize=new Size(Math.Min(Look.S(transient?300:340),work.Width),Math.Min(Look.S(transient?220:260),work.Height));
        var preferred=transient?secondaryPreferredSize:Size;
        Size=new Size(Math.Min(preferred.Width,work.Width),Math.Min(preferred.Height,work.Height));
        Location=Look.Below(Size,anchor,work,Look.S(8),true)??work.Location;
        WindowActivation.Show(this);
    }
    public void ShowLookup(Lookup item,LookupContext context) {
        ResetWindowHistory(item,context);
        Current=item; ParentContext=context; speaking=false; selection=null;
        Original.SetContent(item.Term??""); Answer.SetMarkdown(item.Answer??"");
        Original.Select(0,0); Answer.Select(0,0);
        SetNotice(item.Status=="pending"?UiLanguage.T("Explaining…","正在解释…"):"",false);
        LayoutWindow(); timeline.AutoScrollPosition=Point.Empty; RefreshActions();
    }
    public void UpdateAnswer() {
        if(IsBrowsingHistory) return;
        if(IsDisposed || Current==null) return;
        // Appending an answer must not move the reader, even if the previous
        // content fitted entirely or the reader happened to be at its old end.
        int scroll=Math.Max(0,-timeline.AutoScrollPosition.Y);
        Answer.SetMarkdown(Current.Answer??""); LayoutContent();
        timeline.AutoScrollPosition=new Point(0,scroll);
        RefreshActions();
    }
    public void Finish(bool success,string message) { FinishOriginalNotice(success,message); if(!IsBrowsingHistory) SetNotice(message,!success); RefreshActions(); }
    public void SetNotice(string text,bool error) {
        text=text??""; Color color=error?Look.Error:Look.Muted;
        if(notice.Text==text && notice.ForeColor==color) return;
        notice.Text=text; notice.ForeColor=color; tips.SetToolTip(notice,text);
        if(IsHistoryList && footer.Height!=(String.IsNullOrEmpty(text)?0:Look.S(32))) LayoutWindow();
    }
    public void SetSpeaking(bool active,string message) { speaking=active; if(!active) activeSpeechButton=null; if(message!=null) SetNotice(message,false); RefreshActions(); }
    public void MarkPlaybackStarted() { if(speaking && activeSpeechButton!=null && !IsHistoryList) SetNotice(UiLanguage.T("Reading aloud…","正在朗读…"),false); }
    public void RefreshActions() {
        bool answer=Current!=null && !String.IsNullOrEmpty(Current.Answer);
        save.Enabled=answer && Current.Status=="complete"; copy.Enabled=answer;
        readAnswer.Enabled=answer && Current.Status=="complete";
        readOriginal.Enabled=Current!=null && !String.IsNullOrEmpty(Current.Term);
        stop.Visible=(speaking && activeSpeechButton==null) || (Current!=null && Current.Status=="pending");
        UpdateActionIcon(save,Current!=null && Current.Saved?"saved":"save",UiLanguage.T(Current!=null && Current.Saved?"Remove favorite":"Save result",Current!=null && Current.Saved?"取消收藏":"收藏结果"),false);
        bool originalActive=speaking && activeSpeechButton==readOriginal,answerActive=speaking && activeSpeechButton==readAnswer;
        UpdateActionIcon(readOriginal,originalActive?"stop":"listen",originalActive?UiLanguage.T("Stop reading original","停止朗读原文"):UiLanguage.T("Read the original text or its selected words","朗读原文或其中选中的词"),originalActive);
        UpdateActionIcon(readAnswer,answerActive?"stop":"listen",answerActive?UiLanguage.T("Stop reading answer","停止朗读回答"):UiLanguage.T("Read the answer or its selected words","朗读回答或其中选中的词"),answerActive);
        RefreshWindowHistoryActions();
    }
    void LayoutWindow() {
        if(laying || ClientSize.Width<80) return; laying=true;
        try {
            footer.Height=IsHistoryList?(String.IsNullOrEmpty(notice.Text)?0:Look.S(32)):Look.S(64);
            int w=ClientSize.Width-Padding.Horizontal,p=Look.S(10),button=Look.S(28),gap=Look.S(5);
            close.SetBounds(w-p-button,Look.S(5),button,button); settingsButton.SetBounds(close.Left-gap-button,Look.S(5),button,button); pin.SetBounds(settingsButton.Left-gap-button,Look.S(5),button,button);
            title.SetBounds(p,Look.S(11),Math.Max(40,(transient?close.Left:pin.Left)-p-gap),Look.S(22));
            LayoutWindowHistoryHeader(p,button,gap);
            int x=p; foreach(var action in new[]{save,copy,readAnswer,stop}) { action.SetBounds(x,Look.S(3),button,button); x+=button+gap; }
            notice.SetBounds(p,Look.S(IsHistoryList?3:35),Math.Max(30,w-p*2),Look.S(24));
            timeline.SetBounds(0,0,viewport.ClientSize.Width+SystemInformation.VerticalScrollBarWidth,viewport.ClientSize.Height);
            scrollbar.SetBounds(Math.Max(0,viewport.ClientSize.Width-Look.S(8)),0,Look.S(8),viewport.ClientSize.Height); scrollbar.BringToFront();
            if(Width>0 && Height>0 && (Region==null || shapedWindowSize!=Size || shapedWindowRadius!=Look.S(12))) { Look.Shape(this,12); shapedWindowSize=Size; shapedWindowRadius=Look.S(12); }
        } finally { laying=false; }
        LayoutContent();
        LayoutWindowHistory();
    }
    void LayoutContent() {
        if(laying || viewport.ClientSize.Width<80) return; laying=true;
        try {
            int x=Look.S(16),width=Math.Max(40,viewport.ClientSize.Width-x*2),scroll=-timeline.AutoScrollPosition.Y;
            timeline.SuspendLayout();
            int originalWidth=Math.Max(40,width-Look.S(36));
            Original.SetBounds(x,Look.S(8)-scroll,originalWidth,Original.ContentHeight(originalWidth));
            readOriginal.SetBounds(x+width-Look.S(28),Original.Top,Look.S(28),Look.S(28));
            Answer.SetBounds(x,Math.Max(Original.Bottom,readOriginal.Bottom)+Look.S(14),width,Answer.ContentHeight(width));
            timeline.AutoScrollMinSize=new Size(0,Answer.Bottom+scroll+Look.S(16));
            timeline.ResumeLayout();
        } finally { laying=false; }
        SyncScroll();
    }
    void SyncScroll() {
        if(syncing || IsDisposed) return; syncing=true;
        try { scrollbar.Viewport=timeline.ClientSize.Height; scrollbar.Maximum=Math.Max(0,timeline.AutoScrollMinSize.Height-timeline.ClientSize.Height); scrollbar.Value=-timeline.AutoScrollPosition.Y; scrollbar.Visible=!IsHistoryList && scrollbar.Maximum>0; }
        finally { syncing=false; }
    }
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam);
    void Drag(object sender,MouseEventArgs e) { if(e.Button==MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero); } }
    protected override bool ProcessCmdKey(ref Message message,Keys keys) {
        if(keys==Keys.Escape) { Close(); return true; }
        return base.ProcessCmdKey(ref message,keys);
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);
        float width=Math.Max(1,Look.S(2)),inset=width/2;
        if(ClientSize.Width<=width || ClientSize.Height<=width) return;
        var state=e.Graphics.Save();
        try {
            e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using(var path=Look.Round(new RectangleF(inset,inset,ClientSize.Width-width,ClientSize.Height-width),Math.Max(1,Look.S(12)-inset)))
            using(var pen=new Pen(Color.Black,width)) e.Graphics.DrawPath(pen,path);
        } finally { e.Graphics.Restore(state); }
    }
    protected override void WndProc(ref Message message) {
        base.WndProc(ref message);
        if(message.Msg!=0x84) return;
        long packed=message.LParam.ToInt64(); var point=PointToClient(new Point((short)(packed&0xffff),(short)((packed>>16)&0xffff)));
        int edge=Look.S(6),corner=Look.S(14); bool left=point.X<edge,right=point.X>=ClientSize.Width-edge,top=point.Y<edge,bottom=point.Y>=ClientSize.Height-edge;
        if((left || point.X<corner) && top || left && point.Y<corner) message.Result=new IntPtr(13);
        else if((right || point.X>=ClientSize.Width-corner) && top || right && point.Y<corner) message.Result=new IntPtr(14);
        else if((left || point.X<corner) && bottom || left && point.Y>=ClientSize.Height-corner) message.Result=new IntPtr(16);
        else if((right || point.X>=ClientSize.Width-corner) && bottom || right && point.Y>=ClientSize.Height-corner) message.Result=new IntPtr(17);
        else if(left) message.Result=new IntPtr(10); else if(right) message.Result=new IntPtr(11); else if(top) message.Result=new IntPtr(12); else if(bottom) message.Result=new IntPtr(15);
    }
    protected override void Dispose(bool disposing) { if(disposing) tips.Dispose(); base.Dispose(disposing); }
}
}
