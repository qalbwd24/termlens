using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TermLens {
public sealed class StartupOption {
    readonly string executable, shortcutPath;
    const string Approved=@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";
    public StartupOption(string executable,string shortcutPath) { this.executable=Path.GetFullPath(executable); this.shortcutPath=Path.GetFullPath(shortcutPath); }
    public static StartupOption Current() {
        string folder=Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        if(Program.IsRelease) return new StartupOption(Application.ExecutablePath,Path.Combine(folder,"Termlens release.lnk"));
        var legacy=new StartupOption(Application.ExecutablePath,Path.Combine(folder,"TermLens.lnk"));
        // Preserve ownership of this copy's existing shortcut, including when
        // Windows has disabled it. Detection must not create or rewrite a link.
        if(legacy.ExistingOwnedShortcut()) return legacy;
        return new StartupOption(Application.ExecutablePath,Path.Combine(folder,"TermLens v2.lnk"));
    }
    bool ExistingOwnedShortcut() {
        if(!File.Exists(shortcutPath)) return false;
        dynamic shell=null,shortcut=null;
        try { shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")); shortcut=shell.CreateShortcut(shortcutPath); return Owns(shortcut); }
        catch { return false; }
        finally { if(shortcut!=null) Marshal.FinalReleaseComObject(shortcut); if(shell!=null) Marshal.FinalReleaseComObject(shell); }
    }
    bool Owns(dynamic shortcut) {
        string target=shortcut.TargetPath, arguments=shortcut.Arguments;
        if(String.Equals(target,executable,StringComparison.OrdinalIgnoreCase) && arguments=="--tray") return true;
        // Recognize the earlier local script without touching other startup entries.
        string legacy=Path.Combine(Path.GetDirectoryName(executable),"Start-TermLens.vbs");
        return String.Equals(Path.GetFileName(target),"wscript.exe",StringComparison.OrdinalIgnoreCase) && String.Equals(arguments.Trim('"'),legacy,StringComparison.OrdinalIgnoreCase);
    }
    bool BlockedByWindows() {
        if(!String.Equals(Path.GetDirectoryName(shortcutPath),Environment.GetFolderPath(Environment.SpecialFolder.Startup),StringComparison.OrdinalIgnoreCase)) return false;
        using(var key=Registry.CurrentUser.OpenSubKey(Approved)) {
            var value=key==null?null:key.GetValue(Path.GetFileName(shortcutPath)) as byte[];
            return value!=null && value.Length>0 && (value[0]==3 || value[0]==7);
        }
    }
    public bool Enabled {
        get {
            if(!File.Exists(shortcutPath)) return false;
            dynamic shell=null,shortcut=null;
            try { shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")); shortcut=shell.CreateShortcut(shortcutPath); return Owns(shortcut) && !BlockedByWindows(); }
            finally { if(shortcut!=null) Marshal.FinalReleaseComObject(shortcut); if(shell!=null) Marshal.FinalReleaseComObject(shell); }
        }
    }
    public void SetEnabled(bool enabled) {
        dynamic shell=null,shortcut=null;
        try {
            shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")); shortcut=shell.CreateShortcut(shortcutPath);
            if(File.Exists(shortcutPath) && !Owns(shortcut)) throw new Exception("Another TermLens installation owns the startup shortcut. Turn startup off in that copy first.");
            if(!enabled) { if(File.Exists(shortcutPath)) File.Delete(shortcutPath); return; }
            if(BlockedByWindows()) throw new Exception("Windows has disabled TermLens startup. Enable TermLens in Windows Settings > Apps > Startup first.");
            Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath));
            shortcut.TargetPath=executable; shortcut.Arguments="--tray"; shortcut.WorkingDirectory=Path.GetDirectoryName(executable);
            shortcut.IconLocation=executable+",0"; shortcut.Description="Start "+Program.DisplayName+" quietly without closing any other instance"; shortcut.WindowStyle=7; shortcut.Save();
        } finally { if(shortcut!=null) Marshal.FinalReleaseComObject(shortcut); if(shell!=null) Marshal.FinalReleaseComObject(shell); }
    }
}
public sealed partial class MainWindow {
    ToolStripMenuItem startAtSignIn;
    void AddStartupMenu(ContextMenuStrip menu,StartupOption option=null) {
        option=option??StartupOption.Current();
        startAtSignIn=new ToolStripMenuItem("Start at sign-in") { CheckOnClick=false };
        Action refresh=delegate { try { startAtSignIn.Checked=option.Enabled; startAtSignIn.ToolTipText="Start quietly in the tray when you sign in to Windows"; } catch { startAtSignIn.Checked=false; startAtSignIn.ToolTipText="Could not read startup settings"; } };
        startAtSignIn.Click+=delegate {
            try { option.SetEnabled(!option.Enabled); }
            catch(Exception e) { MessageBox.Show(e.Message,"TermLens startup",MessageBoxButtons.OK,MessageBoxIcon.Information); }
            refresh();
        };
        menu.Opening+=delegate { refresh(); }; refresh(); menu.Items.Add(startAtSignIn);
    }
}
}
