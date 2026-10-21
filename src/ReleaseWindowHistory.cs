using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TermLens {
public sealed partial class ReleaseLookupWindow {
    enum HistoryView { Query,List,Detail }
    HistoryView historyView;
    Lookup originalLookup;
    LookupContext originalContext;
    int originalScroll;
    string originalNotice="";
    bool originalNoticeError;
    readonly SoftButton historyButton=new SoftButton("History",false);
    readonly ReleaseHistoryList windowHistoryList=new ReleaseHistoryList();
    readonly Dictionary<Lookup,Lookup> historyDetails=new Dictionary<Lookup,Lookup>();
    public Func<List<Lookup>> HistoryProvider { get; set; }
    public Func<Lookup,Lookup> HistoryItemProvider { get; set; }
    public event Action HistoryViewChanged;
    public int DisplayRevision { get; private set; }
    public Lookup OriginalLookup { get { return originalLookup; } }
    public bool IsBrowsingHistory { get { return historyView!=HistoryView.Query; } }
    public bool IsHistoryList { get { return historyView==HistoryView.List; } }
    // Lookup contains only immutable strings and value fields. Copying fields
    // isolates later mutations without serializing large answers and contexts.
    static Lookup HistorySnapshot(Lookup item) {
        return item==null?null:new Lookup { Id=item.Id,Term=item.Term,Answer=item.Answer,Created=item.Created,Model=item.Model,Prompt=item.Prompt,Context=item.Context,Saved=item.Saved,Count=item.Count,Kind=item.Kind,Source=item.Source,SourceText=item.SourceText,Status=item.Status };
    }
    List<Lookup> HistoryRows(List<Lookup> records) {
        historyDetails.Clear(); var rows=new List<Lookup>(records.Count);
        foreach(var item in records) {
            if(item==null) continue;
            var row=new Lookup { Id=item.Id,Term=item.Term,Created=item.Created,Kind=item.Kind,Status=item.Status,Saved=item.Saved };
            historyDetails.Add(row,item); rows.Add(row);
        }
        return rows;
    }
    void InitWindowHistory() {
        historyButton.Name="ReleaseHistoryButton"; historyButton.Quiet=true;
        windowHistoryList.Name="ReleaseWindowHistoryList"; windowHistoryList.Visible=false;
        header.Controls.Add(historyButton); viewport.Controls.Add(windowHistoryList);
        historyButton.Click+=delegate { ToggleHistory(); };
        windowHistoryList.ItemActivated+=ShowHistoryDetail;
    }
    void ApplyWindowHistoryTheme() {
        historyButton.Visible=!transient;
        windowHistoryList.BackColor=Look.Canvas; windowHistoryList.ForeColor=Look.Ink; windowHistoryList.Invalidate();
        RefreshWindowHistoryActions();
    }
    void ResetWindowHistory(Lookup item,LookupContext context) {
        activeSpeechButton=null; speaking=false;
        DisplayRevision++; historyDetails.Clear(); windowHistoryList.SetItems(new List<Lookup>());
        originalLookup=item; originalContext=context; originalScroll=0; originalNoticeError=false;
        originalNotice=item!=null && item.Status=="pending"?UiLanguage.T("Explaining…","正在解释…"):"";
        historyView=HistoryView.Query; windowHistoryList.Visible=false; timeline.Visible=true;
        RefreshWindowHistoryActions();
    }
    void FinishOriginalNotice(bool success,string message) { originalNotice=message??""; originalNoticeError=!success; }
    void RefreshWindowHistoryActions() {
        historyButton.Visible=!transient;
        if(historyView==HistoryView.Detail) {
            UpdateActionIcon(historyButton,"back",UiLanguage.T("Back to history list","返回历史列表"),IsBrowsingHistory);
        } else UpdateActionIcon(historyButton,"history",UiLanguage.T(IsHistoryList?"Return to current lookup":"Lookup history",IsHistoryList?"返回当前查询":"查询历史"),IsBrowsingHistory);
        save.Visible=copy.Visible=readAnswer.Visible=!IsHistoryList;
        if(IsHistoryList) stop.Visible=false;
    }
    void LayoutWindowHistoryHeader(int padding,int button,int gap) {
        if(transient) return;
        historyButton.SetBounds(pin.Left-gap-button,Look.S(5),button,button);
        title.Width=Math.Max(20,historyButton.Left-padding-gap);
    }
    void LayoutWindowHistory() {
        windowHistoryList.Bounds=viewport.ClientRectangle;
        windowHistoryList.Visible=IsHistoryList; timeline.Visible=!IsHistoryList;
        if(IsHistoryList) { scrollbar.Visible=false; windowHistoryList.BringToFront(); }
    }
    void NotifyHistoryChange() {
        DisplayRevision++;
        speaking=false; activeSpeechButton=null; selection=null; Original.Select(0,0); Answer.Select(0,0);
        if(HistoryViewChanged!=null) HistoryViewChanged();
    }
    public void ToggleHistory() {
        if(transient) return;
        if(IsHistoryList) { RestoreOriginalLookup(); return; }
        if(historyView==HistoryView.Query) {
            originalScroll=Math.Max(0,-timeline.AutoScrollPosition.Y);
            originalNotice=notice.Text; originalNoticeError=notice.ForeColor==Look.Error;
        }
        List<Lookup> records;
        try { records=HistoryProvider==null?new List<Lookup>():HistoryProvider()??new List<Lookup>(); }
        catch(Exception) { SetNotice(UiLanguage.T("History could not be loaded.","暂时无法读取历史。"),true); return; }
        historyView=HistoryView.List; Current=null; ParentContext=null;
        NotifyHistoryChange(); windowHistoryList.SetItems(HistoryRows(records));
        SetNotice("",false); RefreshActions(); LayoutWindow(); windowHistoryList.Focus();
    }
    public void ShowHistoryDetail(Lookup item) {
        if(transient || item==null) return;
        if(historyView==HistoryView.Query) { originalScroll=Math.Max(0,-timeline.AutoScrollPosition.Y); originalNotice=notice.Text; originalNoticeError=notice.ForeColor==Look.Error; }
        Lookup snapshot;
        try {
            Lookup full; if(!historyDetails.TryGetValue(item,out full)) full=item;
            snapshot=HistorySnapshot(HistoryItemProvider==null?full:HistoryItemProvider(HistorySnapshot(item))??full);
        }
        catch(Exception) { SetNotice(UiLanguage.T("History could not be opened.","暂时无法打开此记录。"),true); return; }
        historyView=HistoryView.Detail; Current=snapshot; ParentContext=LookupContext.FromSaved(snapshot.Context);
        NotifyHistoryChange(); Original.SetContent(snapshot.Term??""); Answer.SetMarkdown(snapshot.Answer??"");
        SetNotice("",false); LayoutWindow(); timeline.AutoScrollPosition=Point.Empty; RefreshActions();
    }
    public void RestoreOriginalLookup() {
        if(transient || !IsBrowsingHistory) return;
        if(originalLookup!=null && HistoryItemProvider!=null) try {
            var refreshed=HistoryItemProvider(HistorySnapshot(originalLookup));
            if(refreshed!=null) originalLookup.Saved=refreshed.Saved;
        } catch(Exception) { /* Preserve the live query if history is temporarily unavailable. */ }
        historyView=HistoryView.Query; Current=originalLookup; ParentContext=originalContext;
        NotifyHistoryChange(); Original.SetContent(Current==null?"":Current.Term??""); Answer.SetMarkdown(Current==null?"":Current.Answer??"");
        SetNotice(originalNotice,originalNoticeError); LayoutWindow(); timeline.AutoScrollPosition=new Point(0,originalScroll); RefreshActions();
    }
}
// Paint only the selected text. No mode prefix, timestamps, multiline rows or
// native ListBox scrollbar; the existing slim scrollbar shares wheel/key state.
sealed class ReleaseHistoryList : Control {
    readonly SlimScrollBar scroll=new SlimScrollBar();
    List<Lookup> items=new List<Lookup>();
    int selected=-1,offset;
    internal static readonly TextFormatFlags RowTextFlags=TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding;
    public event Action<Lookup> ItemActivated;
    int RowHeight { get { return Look.S(36); } }
    public ReleaseHistoryList() {
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.Selectable,true);
        TabStop=true; Font=new Font("Microsoft YaHei UI",10);
        Controls.Add(scroll); scroll.ValueChanged+=delegate { offset=scroll.Value; Invalidate(); };
    }
    public void SetItems(List<Lookup> records) { items=records; selected=items.Count>0?0:-1; offset=0; UpdateScroll(); Invalidate(); }
    void UpdateScroll() {
        scroll.SetBounds(Math.Max(0,Width-Look.S(8)),0,Look.S(8),Height);
        scroll.Viewport=Height; scroll.Maximum=Math.Max(0,items.Count*RowHeight-Height); scroll.Value=Math.Min(offset,scroll.Maximum); offset=scroll.Value; scroll.Visible=scroll.Maximum>0;
    }
    protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateScroll(); }
    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.Clear(Look.Canvas);
        if(items.Count==0) { TextRenderer.DrawText(e.Graphics,UiLanguage.T("No lookup history yet","暂无查询历史"),Font,ClientRectangle,Look.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter); return; }
        int first=offset/RowHeight,last=Math.Min(items.Count-1,(offset+Height)/RowHeight);
        for(int index=first;index<=last;index++) {
            var row=new Rectangle(Look.S(8),index*RowHeight-offset,Math.Max(1,Width-Look.S(20)),RowHeight);
            if(index==selected) using(var fill=new SolidBrush(Look.Field)) e.Graphics.FillRectangle(fill,row);
            string text=(items[index].Term??"").Replace('\r',' ').Replace('\n',' ');
            row.Inflate(-Look.S(8),0); TextRenderer.DrawText(e.Graphics,text,Font,row,Look.Ink,RowTextFlags);
        }
    }
    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); scroll.Value=Math.Max(0,Math.Min(scroll.Maximum,offset-e.Delta/120*RowHeight*3)); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); int index=(e.Y+offset)/RowHeight; if(index>=0 && index<items.Count) { selected=index; Invalidate(); } }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); int index=(e.Y+offset)/RowHeight; if(e.Button==MouseButtons.Left && index==selected && index>=0 && index<items.Count) ActivateItem(); }
    void ActivateItem() { if(selected>=0 && selected<items.Count && ItemActivated!=null) ItemActivated(items[selected]); }
    protected override bool IsInputKey(Keys keyData) { Keys key=keyData&Keys.KeyCode; return key==Keys.Enter || key==Keys.Up || key==Keys.Down || key==Keys.Home || key==Keys.End || key==Keys.PageUp || key==Keys.PageDown || base.IsInputKey(keyData); }
    protected override void OnKeyDown(KeyEventArgs e) {
        base.OnKeyDown(e); int next=selected;
        if(e.KeyCode==Keys.Enter) { ActivateItem(); e.Handled=e.SuppressKeyPress=true; return; }
        if(e.KeyCode==Keys.Up) next--; else if(e.KeyCode==Keys.Down) next++; else if(e.KeyCode==Keys.Home) next=0; else if(e.KeyCode==Keys.End) next=items.Count-1;
        else if(e.KeyCode==Keys.PageUp) next-=Math.Max(1,Height/RowHeight); else if(e.KeyCode==Keys.PageDown) next+=Math.Max(1,Height/RowHeight); else return;
        selected=Math.Max(0,Math.Min(items.Count-1,next));
        if(selected*RowHeight<offset) scroll.Value=selected*RowHeight; else if((selected+1)*RowHeight>offset+Height) scroll.Value=Math.Min(scroll.Maximum,(selected+1)*RowHeight-Height);
        Invalidate(); e.Handled=e.SuppressKeyPress=true;
    }
}
}
