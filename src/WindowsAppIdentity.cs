using System;
using System.Runtime.InteropServices;

namespace TermLens {
// Establish identity before creating any HWND. A launcher must not determine
// the taskbar group used by the independently running reading application.
public static class WindowsAppIdentity {
    public const string ReleaseId="TermLens.Desktop.Release";
    public const string PersonalId="TermLens.Desktop.Personal";
    public static string Previous { get; private set; }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
    static extern int SetCurrentProcessExplicitAppUserModelID(string id);
    [DllImport("shell32.dll",ExactSpelling=true)]
    static extern int GetCurrentProcessExplicitAppUserModelID(out IntPtr id);
    public static string Current() {
        IntPtr value=IntPtr.Zero;
        try { return GetCurrentProcessExplicitAppUserModelID(out value)<0?"":Marshal.PtrToStringUni(value)??""; }
        finally { if(value!=IntPtr.Zero) Marshal.FreeCoTaskMem(value); }
    }
    public static void Configure(bool release,bool test) {
        Previous=Current();
        string id=test?"TermLens.Desktop.Tests":release?ReleaseId:PersonalId;
        Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(id));
        if(Current()!=id) throw new InvalidOperationException("Windows did not apply the TermLens application identity.");
    }
}
}
