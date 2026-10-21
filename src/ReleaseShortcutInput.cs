using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TermLens {
// Captures keys only while this focused editor is explicitly recording. It does
// not install a global hook or apply the chosen global shortcut before Save.
// Behavioral reference (independent implementation; no source copied):
// https://raw.githubusercontent.com/ShareX/ShareX/develop/ShareX/Controls/HotkeySelectionControl.cs
public sealed class ReleaseShortcutInput : TextBox {
    sealed class CaptureMouseFilter : IMessageFilter {
        readonly ReleaseShortcutInput owner;
        internal CaptureMouseFilter(ReleaseShortcutInput owner) { this.owner=owner; }
        public bool PreFilterMessage(ref Message message) { return owner.CaptureMouseMessage(ref message); }
    }
    CaptureMouseFilter mouseFilter;
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    int pendingKey,pendingModifiers;
    public int KeyCode { get; private set; }
    public int Modifiers { get; private set; }
    public bool IsRecording { get; private set; }
    public string Hint { get; private set; }
    public event Action CaptureStarted,CaptureEnded;
    public event EventHandler HintChanged;
    public ReleaseShortcutInput() {
        ReadOnly=true; ShortcutsEnabled=false; TabStop=true; Cursor=Cursors.Hand;
        AccessibleName="查询快捷键，点击后按键盘组合键或鼠标侧键";
        KeyCode=(int)Keys.F13; Modifiers=0; UpdateDisplay();
    }
    public void SetShortcut(int key,int modifiers) {
        if(IsRecording) CancelCapture();
        KeyCode=key; Modifiers=modifiers; UpdateDisplay();
    }
    void SetHint(string text) { Hint=text; if(HintChanged!=null) HintChanged(this,EventArgs.Empty); }
    void UpdateDisplay() { Text=Format(KeyCode,Modifiers); SelectionStart=TextLength; SelectionLength=0; }
    public static string Format(int key,int modifiers) {
        string prefix=((modifiers&2)!=0?"Ctrl + ":"")+((modifiers&1)!=0?"Alt + ":"")+((modifiers&4)!=0?"Shift + ":"")+((modifiers&8)!=0?"Win + ":"");
        string label=((Keys)key).ToString();
        if(key>=(int)Keys.D0 && key<=(int)Keys.D9) label=((char)key).ToString();
        if(key==(int)Keys.Return) label="Enter";
        if(key==(int)Keys.XButton1) label="鼠标后退键";
        if(key==(int)Keys.XButton2) label="鼠标前进键";
        return prefix+label;
    }
    public void BeginCapture() {
        if(IsRecording || IsDisposed) return;
        try { if(CaptureStarted!=null) CaptureStarted(); }
        catch(Exception error) { SetHint("暂时无法录入："+error.Message); return; }
        pendingKey=0; pendingModifiers=0; IsRecording=true; Focus(); Text="请按下快捷键…"; SelectAll();
        mouseFilter=new CaptureMouseFilter(this); Application.AddMessageFilter(mouseFilter);
        SetHint("按键盘组合键或鼠标侧键，Esc 取消。");
    }
    public void CancelCapture() { if(IsRecording) FinishCapture("已取消录入，保留原设置。"); }
    void FinishCapture(string message) {
        if(!IsRecording) return;
        if(mouseFilter!=null) { Application.RemoveMessageFilter(mouseFilter); mouseFilter=null; }
        IsRecording=false; pendingKey=0; pendingModifiers=0; UpdateDisplay();
        try { if(CaptureEnded!=null) CaptureEnded(); SetHint(message); }
        catch(Exception error) { SetHint("快捷键恢复失败："+error.Message); }
    }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if(e.Button==MouseButtons.Left) BeginCapture(); }
    protected override void WndProc(ref Message message) {
        if(CaptureMouseMessage(ref message)) return;
        base.WndProc(ref message);
    }
    bool CaptureMouseMessage(ref Message message) {
        if(IsRecording && (message.Msg==0x20B || message.Msg==0x20C || message.Msg==0x20D)) {
            int button=(int)((message.WParam.ToInt64()>>16)&0xffff);
            int key=button==1?(int)Keys.XButton1:button==2?(int)Keys.XButton2:0;
            if(key!=0) {
                if(message.Msg==0x20C) CompleteKeyRelease(key);
                else {
                    int modifiers=(GetAsyncKeyState((int)Keys.Menu)<0?1:0)|(GetAsyncKeyState((int)Keys.ControlKey)<0?2:0)|(GetAsyncKeyState((int)Keys.ShiftKey)<0?4:0);
                    if(GetAsyncKeyState((int)Keys.LWin)<0 || GetAsyncKeyState((int)Keys.RWin)<0) modifiers|=8;
                    AcceptCandidate(key,modifiers);
                }
                message.Result=new IntPtr(1); return true;
            }
        }
        return false;
    }
    protected override void OnLeave(EventArgs e) { CancelCapture(); base.OnLeave(e); }
    protected override bool IsInputKey(Keys keyData) { return IsRecording || base.IsInputKey(keyData); }
    protected override bool ProcessCmdKey(ref Message message,Keys keyData) {
        if(!IsRecording) return base.ProcessCmdKey(ref message,keyData);
        int key=(int)(keyData&Keys.KeyCode);
        if(key==(int)Keys.Escape) { CancelCapture(); return true; }
        if(pendingKey!=0) return true;
        int modifiers=((keyData&Keys.Alt)!=0?1:0)|((keyData&Keys.Control)!=0?2:0)|((keyData&Keys.Shift)!=0?4:0);
        if(GetAsyncKeyState((int)Keys.LWin)<0 || GetAsyncKeyState((int)Keys.RWin)<0) modifiers|=8;
        AcceptCandidate(key,modifiers); return true;
    }
    // Public to permit deterministic key-validation tests without posting input.
    public bool AcceptCandidate(int key,int modifiers) {
        if(!IsRecording || pendingKey!=0) return false;
        string error;
        if(!TryValidate(key,modifiers,out error)) { SetHint(error); return false; }
        pendingKey=key; pendingModifiers=modifiers; Text=Format(key,modifiers);
        SetHint("请松开按键以完成录入。"); return true;
    }
    public static bool TryValidate(int key,int modifiers,out string error) {
        error="";
        if(modifiers<0 || modifiers>15) { error="此组合键暂不支持。"; return false; }
        if(key==(int)Keys.ShiftKey || key==(int)Keys.ControlKey || key==(int)Keys.Menu || key==(int)Keys.LWin || key==(int)Keys.RWin || key>=(int)Keys.LShiftKey && key<=(int)Keys.RMenu) {
            error="请再按一个键，不能只使用修饰键。"; return false;
        }
        if(key==(int)Keys.F12) { error="F12 为 Windows 调试器保留，请使用其他键。"; return false; }
        if(key==(int)Keys.PrintScreen && modifiers==0) { error="单独的截图键由系统管理，请使用组合键或其他键。"; return false; }
        bool function=key>=(int)Keys.F1 && key<=(int)Keys.F24;
        bool standard=key>=(int)Keys.A && key<=(int)Keys.Z || key>=(int)Keys.D0 && key<=(int)Keys.D9 || key>=(int)Keys.NumPad0 && key<=(int)Keys.Divide ||
            key>=(int)Keys.PageUp && key<=(int)Keys.Down || key==(int)Keys.Space || key==(int)Keys.Tab || key==(int)Keys.Return || key==(int)Keys.Back || key==(int)Keys.Insert || key==(int)Keys.Delete || key==(int)Keys.PrintScreen ||
            key>=(int)Keys.OemSemicolon && key<=(int)Keys.Oemtilde || key>=(int)Keys.OemOpenBrackets && key<=(int)Keys.OemQuotes || key==(int)Keys.OemBackslash;
        bool side=key==(int)Keys.XButton1 || key==(int)Keys.XButton2;
        if(!function && !standard && !side) { error="请使用功能键、常用组合键或鼠标侧键。"; return false; }
        if(modifiers==0 && !function && !side) { error="普通按键请同时按 Ctrl、Alt、Shift 或 Win，避免影响打字。"; return false; }
        return true;
    }
    public bool CompleteKeyRelease(int key) {
        if(!IsRecording || pendingKey==0 || pendingKey!=key) return false;
        KeyCode=pendingKey; Modifiers=pendingModifiers;
        FinishCapture("已录入 "+Format(KeyCode,Modifiers)+"；点击保存后生效。"); return true;
    }
    protected override void OnPreviewKeyDown(PreviewKeyDownEventArgs e) {
        if(IsRecording) e.IsInputKey=true;
        base.OnPreviewKeyDown(e);
    }
    protected override void OnKeyUp(KeyEventArgs e) {
        if(IsRecording) {
            if(pendingKey==0 && e.KeyCode==Keys.PrintScreen) {
                int modifiers=(e.Alt?1:0)|(e.Control?2:0)|(e.Shift?4:0);
                if(GetAsyncKeyState((int)Keys.LWin)<0 || GetAsyncKeyState((int)Keys.RWin)<0) modifiers|=8;
                AcceptCandidate((int)e.KeyCode,modifiers);
            }
            CompleteKeyRelease((int)e.KeyCode); e.Handled=true; return;
        }
        base.OnKeyUp(e);
    }
    protected override void OnKeyDown(KeyEventArgs e) {
        if(IsRecording) {
            if(e.KeyCode==Keys.Escape) CancelCapture();
            else if(pendingKey==0) {
                int modifiers=(e.Alt?1:0)|(e.Control?2:0)|(e.Shift?4:0);
                if(GetAsyncKeyState((int)Keys.LWin)<0 || GetAsyncKeyState((int)Keys.RWin)<0) modifiers|=8;
                AcceptCandidate((int)e.KeyCode,modifiers);
            }
            e.Handled=true; return;
        }
        base.OnKeyDown(e);
    }
    protected override void OnKeyPress(KeyPressEventArgs e) { if(IsRecording) { e.Handled=true; return; } base.OnKeyPress(e); }
    protected override void Dispose(bool disposing) { if(disposing) CancelCapture(); base.Dispose(disposing); }
}
}
