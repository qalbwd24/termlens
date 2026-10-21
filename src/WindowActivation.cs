using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TermLens {
// Called only in response to an explicit request to open a TermLens window.
public static class WindowActivation {
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint current,uint foreground,bool attach);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr handle);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr handle,int command);
    public static bool Show(Form form) {
        if(form.IsDisposed) return false;
        if(form.WindowState==FormWindowState.Minimized) ShowWindow(form.Handle,9); // SW_RESTORE
        if(!form.Visible) form.Show();
        form.BringToFront(); form.Activate(); Native.SetForegroundWindow(form.Handle);
        if(Native.GetForegroundWindow()==form.Handle) return true;
        uint ignored,current=GetCurrentThreadId(),foreground=Native.GetWindowThreadProcessId(Native.GetForegroundWindow(),out ignored);
        bool attached=foreground!=0 && foreground!=current && AttachThreadInput(current,foreground,true);
        try { BringWindowToTop(form.Handle); form.Activate(); Native.SetForegroundWindow(form.Handle); }
        finally { if(attached) AttachThreadInput(current,foreground,false); }
        return Native.GetForegroundWindow()==form.Handle;
    }
}
// A local message filter; it does not install a system-wide mouse hook.
public sealed class ReadingSelection : IMessageFilter,IDisposable {
    readonly WorkspaceWindow owner;
    public ReadingSelection(WorkspaceWindow owner) { this.owner=owner; Application.AddMessageFilter(this); }
    public bool PreFilterMessage(ref Message m) {
        if(m.Msg!=0x201 && m.Msg!=0x204 && m.Msg!=0x207) return false;
        var target=Control.FromHandle(m.HWnd);
        if(target==null || target.FindForm()!=owner) return false;
        var reading=target as StudyText;
        if(reading!=null) owner.ClearReadingSelection(reading);
        // A button click must retain the selection until the action has read it.
        else if(!(target is ButtonBase)) owner.ClearReadingSelection();
        return false;
    }
    public void Dispose() { Application.RemoveMessageFilter(this); }
}
}
