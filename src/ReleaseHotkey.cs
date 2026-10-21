using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TermLens {
// Independently implemented message-only-window design, as used by NHotkey:
// https://github.com/thomaslevesque/NHotkey/blob/master/src/NHotkey.WindowsForms/HotkeyManager.cs
internal sealed class ReleaseHotkeyMessageWindow : NativeWindow, IDisposable {
    internal event Action<int> Pressed;
    internal ReleaseHotkeyMessageWindow() { CreateHandle(new CreateParams { Caption="Termlens hotkey messages",Parent=new IntPtr(-3) }); }
    protected override void WndProc(ref Message message) {
        if(message.Msg==0x312) { if(Pressed!=null) Pressed(message.WParam.ToInt32()); return; }
        base.WndProc(ref message);
    }
    public void Dispose() { if(Handle!=IntPtr.Zero) DestroyHandle(); }
}
public sealed partial class MainWindow {
    int releaseRegisteredId, releaseRegisteredKey, releaseRegisteredModifiers;
    bool releaseHotkeySuspended;
    string releaseHotkeyStartupError;
    ReleaseHotkeyMessageWindow releaseHotkeyWindow;
    ReleaseMouseShortcut releaseMouseShortcut;
    internal Task RouteReleaseShortcut(IntPtr source) {
        if(exiting || releaseHotkeySuspended) return Task.FromResult(0);
        return CaptureRelease(source);
    }
    internal void ApplyReleaseHotkey(Settings candidate) {
        candidate.Validate();
        if(Program.NoHotkey || releaseHotkeySuspended || (Program.IsTest && !Program.AllowTestHotkey)) return;
        bool mouse=candidate.TriggerKey==(int)Keys.XButton1 || candidate.TriggerKey==(int)Keys.XButton2;
        if(releaseRegisteredId!=0 && releaseRegisteredKey==candidate.TriggerKey && releaseRegisteredModifiers==candidate.TriggerModifiers) return;
        if(mouse) {
            ReleaseMouseShortcut replacement=null;
            replacement=new ReleaseMouseShortcut(candidate.TriggerKey,candidate.TriggerModifiers,delegate(IntPtr source) {
                if(exiting || releaseHotkeySuspended || !IsHandleCreated || releaseMouseShortcut!=replacement) return;
                BeginInvoke((Action)(async delegate {
                    if(releaseMouseShortcut!=replacement || exiting || releaseHotkeySuspended) return;
                    UiTrace.Write("release mouse shortcut received key="+candidate.TriggerKey+" sourcePid="+Native.ProcessOf(source));
                    await RouteReleaseShortcut(source);
                }));
            });
            if(releaseMouseShortcut!=null) releaseMouseShortcut.Dispose();
            else if(releaseRegisteredId!=0 && releaseHotkeyWindow!=null) { Native.UnregisterHotKey(releaseHotkeyWindow.Handle,releaseRegisteredId); registered.Remove(releaseRegisteredId); }
            releaseMouseShortcut=replacement; releaseRegisteredId=42;
            releaseRegisteredKey=candidate.TriggerKey; releaseRegisteredModifiers=candidate.TriggerModifiers;
            UiTrace.Write("release mouse shortcut registered key="+candidate.TriggerKey+" modifiers="+candidate.TriggerModifiers);
            return;
        }
        if(releaseHotkeyWindow==null) {
            releaseHotkeyWindow=new ReleaseHotkeyMessageWindow();
            releaseHotkeyWindow.Pressed+=delegate(int id) {
                var source=Native.GetForegroundWindow();
                UiTrace.Write("release hotkey received id="+id+" sourcePid="+Native.ProcessOf(source));
                if(exiting || releaseHotkeySuspended || id!=releaseRegisteredId || !IsHandleCreated) return;
                BeginInvoke((Action)(async delegate { await RouteReleaseShortcut(source); }));
            };
        }
        // Register the replacement before releasing the working shortcut. A conflict
        // therefore cannot silently disable the user's previous shortcut.
        int next=releaseRegisteredId==40?41:40;
        if(!Native.RegisterHotKey(releaseHotkeyWindow.Handle,next,(uint)(candidate.TriggerModifiers|0x4000),(uint)candidate.TriggerKey))
            throw new InvalidOperationException("这个快捷键已被占用或被 Windows 保留，请选择其他组合。"+(releaseRegisteredId!=0?"原快捷键保持不变。":"当前尚未启用查词快捷键。"));
        if(releaseMouseShortcut!=null) { releaseMouseShortcut.Dispose(); releaseMouseShortcut=null; }
        else if(releaseRegisteredId!=0) { Native.UnregisterHotKey(releaseHotkeyWindow.Handle,releaseRegisteredId); registered.Remove(releaseRegisteredId); }
        releaseRegisteredId=next; releaseRegisteredKey=candidate.TriggerKey; releaseRegisteredModifiers=candidate.TriggerModifiers;
        registered.Add(next);
        UiTrace.Write("release hotkey registered key="+candidate.TriggerKey+" modifiers="+candidate.TriggerModifiers+" messageWindow="+releaseHotkeyWindow.Handle);
    }
    internal void SuspendReleaseHotkey() {
        releaseHotkeySuspended=true;
        if(releaseMouseShortcut!=null) { releaseMouseShortcut.Dispose(); releaseMouseShortcut=null; releaseRegisteredId=0; }
        else if(releaseRegisteredId!=0 && releaseHotkeyWindow!=null) {
            Native.UnregisterHotKey(releaseHotkeyWindow.Handle,releaseRegisteredId); registered.Remove(releaseRegisteredId); releaseRegisteredId=0;
        }
        UiTrace.Write("release hotkey paused for capture");
    }
    internal void ResumeReleaseHotkey() {
        releaseHotkeySuspended=false;
        if(exiting || IsDisposed || Disposing) return;
        try { ApplyReleaseHotkey(settings); }
        catch(Exception e) { UiTrace.Write("release hotkey resume failed type="+e.GetType().Name); SetStatus(e.Message,true); }
    }
    void DisposeReleaseHotkey() {
        SuspendReleaseHotkey();
        if(releaseHotkeyWindow!=null) { releaseHotkeyWindow.Dispose(); releaseHotkeyWindow=null; }
    }
}
}
