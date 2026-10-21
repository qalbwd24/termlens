using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace TermLens {
// Scope is an explicit executable allowlist, never a process-name kill or broadcast.
public static class InstanceCoordinator {
    public const string Home=@"D:\Termlens";
    public const string CoordinationName=@"Local\TermLens.Managed.Start.v2";
    static readonly Dictionary<string,string> Versions=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase) {
        {"release",@"release\Termlens\TermLens.exe"},
        {"v2",@"Termlens_v2\TermLens.exe"}, {"ds",@"termlens_ds\TermLens.exe"},
        {"original",@"TermLens\TermLens.exe"}, {"copy",@"term_lens\TermLens.exe"}, {"frozen",@"term_lens\versions\1.4.0-winforms-nobg\TermLens.exe"},
        {"nobg",@"term_lens\versions\1.3.5-20260930-1722-no-background\TermLens-1.3.5.exe"}
    };
    delegate bool WindowVisitor(IntPtr window,IntPtr state);
    [DllImport("user32.dll")] static extern bool EnumWindows(WindowVisitor visitor,IntPtr state);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr window,StringBuilder text,int count);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    static string Full(string path) { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar); }
    static bool EqualPath(string a,string b) { return String.Equals(Full(a),Full(b),StringComparison.OrdinalIgnoreCase); }
    public static string VersionPath(string version) {
        if(!String.Equals(version,"v2",StringComparison.OrdinalIgnoreCase) && !String.Equals(version,"release",StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("This legacy entry is disabled. Choose v2 or release.");
        string relative; if(!Versions.TryGetValue(version,out relative)) throw new InvalidOperationException("Unknown version: "+version);
        return Path.Combine(Home,relative);
    }
    public static bool IsReleaseDirectory(string directory) { return File.Exists(Path.Combine(directory,"release-channel.txt")); }
    static bool PortableRelease(string path) { return String.Equals(Path.GetFileName(path),"TermLens.exe",StringComparison.OrdinalIgnoreCase) && IsReleaseDirectory(Path.GetDirectoryName(path)); }
    static bool Known(string path) { return Versions.Values.Any(v=>EqualPath(path,Path.Combine(Home,v))) || PortableRelease(path); }
    static bool Launchable(string path) { return EqualPath(path,VersionPath("v2")) || PortableRelease(path); }
    static void CheckIdentity(string path) {
        if(!Known(path) || !File.Exists(path)) throw new InvalidOperationException("Not an installed managed TermLens executable: "+path);
        // A junction must not redirect an approved path to an unrelated program.
        for(string p=Path.GetDirectoryName(path); p!=null; p=Path.GetDirectoryName(p)) {
            if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0) throw new InvalidOperationException("Reparse-point paths are not supported: "+p);
            if(EqualPath(p,Home)) break;
        }
        if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0) throw new InvalidOperationException("Executable is a reparse point.");
        var info=FileVersionInfo.GetVersionInfo(path);
        if(info.ProductName!="TermLens" || info.CompanyName!="TermLens contributors") throw new InvalidOperationException("TermLens executable identity did not match: "+path);
    }
    static bool TestProcess(int pid) {
        try { using(var marker=Mutex.OpenExisting("Local\\TermLens.Test."+pid)) return true; }
        catch(WaitHandleCannotBeOpenedException) { return false; }
    }
    sealed class Running : IDisposable {
        public Process Process; public string Path;
        public void Dispose() { Process.Dispose(); }
    }
    static List<Running> RunningVersions(string onlyPath=null) {
        var result=new List<Running>();
        foreach(var process in Versions.Values.Select(v=>Path.GetFileNameWithoutExtension(v)).Distinct(StringComparer.OrdinalIgnoreCase).SelectMany(name=>Process.GetProcessesByName(name))) {
            bool keep=false;
            try {
                if(process.Id==Process.GetCurrentProcess().Id || process.SessionId!=Process.GetCurrentProcess().SessionId || TestProcess(process.Id)) continue;
                string path=process.MainModule.FileName;
                if(onlyPath!=null && !EqualPath(path,onlyPath)) continue;
                if(!Known(path)) continue;
                CheckIdentity(path); result.Add(new Running { Process=process,Path=path }); keep=true;
            } catch(InvalidOperationException) { if(!process.HasExited) throw; }
            catch(System.ComponentModel.Win32Exception) { if(onlyPath==null) throw new InvalidOperationException("Cannot inspect TermLens PID "+process.Id+". Close it normally before switching versions."); }
            finally { if(!keep) process.Dispose(); }
        }
        return result;
    }
    static IntPtr MainWindowOf(int pid) {
        IntPtr found=IntPtr.Zero, fallback=IntPtr.Zero;
        EnumWindows(delegate(IntPtr window,IntPtr unused) {
            uint owner; GetWindowThreadProcessId(window,out owner); if(owner!=(uint)pid) return true;
            var title=new StringBuilder(256); GetWindowText(window,title,title.Capacity);
            string text=title.ToString();
            if(text=="TermLens · CS & Math" || text=="TermLens · WPF beta" || text=="TermLens v2 · CS & Math" || text=="Termlens release") { found=window; return false; }
            // The original WPF shell is titled TermLens. A WinForms workspace
            // uses that title too, so only use it after searching every window
            // of this exact PID for the real hidden settings/message window.
            if(text=="TermLens") fallback=window;
            return true;
        },IntPtr.Zero);
        return found!=IntPtr.Zero?found:fallback;
    }
    static IntPtr WaitWindow(Process process) {
        var watch=Stopwatch.StartNew();
        while(watch.ElapsedMilliseconds<12000) {
            if(process.HasExited) throw new InvalidOperationException("TermLens exited before its main window was ready.");
            IntPtr handle=MainWindowOf(process.Id); if(handle!=IntPtr.Zero) return handle;
            Thread.Sleep(100);
        }
        throw new InvalidOperationException("TermLens PID "+process.Id+" did not become ready. No second instance was started.");
    }
    static void Stop(Running running) {
        if(running.Process.HasExited) return;
        IntPtr window=WaitWindow(running.Process);
        if(!Native.PostMessage(window,MainWindow.ExitMessage,IntPtr.Zero,IntPtr.Zero) || !running.Process.WaitForExit(15000))
            throw new InvalidOperationException("TermLens PID "+running.Process.Id+" did not exit normally. Save your work and exit it from the tray before retrying. Nothing was force-closed.");
    }
    static string DirectoryMutex(string directory) {
        using(var hash=SHA256.Create()) return "Local\\TermLens.Directory."+BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Full(directory).ToUpperInvariant()))).Replace("-","");
    }
    static void CopyTree(string source,string target) {
        if((File.GetAttributes(source)&FileAttributes.ReparsePoint)!=0) throw new IOException("Data migration does not follow junctions: "+source);
        Directory.CreateDirectory(target);
        foreach(string file in Directory.GetFiles(source)) {
            if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0) throw new IOException("Data migration does not follow linked files.");
            File.Copy(file,Path.Combine(target,Path.GetFileName(file)),false);
        }
        foreach(string dir in Directory.GetDirectories(source)) CopyTree(dir,Path.Combine(target,Path.GetFileName(dir)));
    }
    static void ImportFirstData(string baseDir) {
        string target=Path.Combine(baseDir,"data"); if(Directory.Exists(target)) return;
        // A portable release is a clean profile even on this developer machine.
        if(IsReleaseDirectory(baseDir)) { Directory.CreateDirectory(target); return; }
        string source=Path.Combine(Home,@"termlens_ds\data");
        if(!Directory.Exists(source)) { Directory.CreateDirectory(target); return; }
        var sourceProcesses=RunningVersions(Path.Combine(Home,@"termlens_ds\TermLens.exe"));
        try { if(sourceProcesses.Count>0) throw new IOException("The ds source is still running. Exit it normally before the first v2 data import; no instance was stopped automatically."); }
        finally { foreach(var item in sourceProcesses) item.Dispose(); }
        for(string p=source; p!=null; p=Path.GetDirectoryName(p)) {
            if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0) throw new IOException("Data migration does not follow junctions: "+p);
            if(EqualPath(p,Home)) break;
        }
        // Source must be stopped; coordination protects cooperating v2/release starts.
        string staging=Path.Combine(baseDir,"data-import-"+Guid.NewGuid().ToString("N"));
        try {
            CopyTree(source,staging); // Includes SQLite sidecars, audio and opaque DPAPI files.
            string settingsPath=Path.Combine(staging,"settings.json");
            if(File.Exists(settingsPath)) {
                var map=Files.Json().Deserialize<Dictionary<string,object>>(File.ReadAllText(settingsPath,Files.Utf8));
                object oldHeight; int height=map.TryGetValue("WorkspaceHeight",out oldHeight)?Convert.ToInt32(oldHeight):0;
                if(height>Look.S(560)) {
                    File.Copy(settingsPath,Path.Combine(staging,"settings-before-v2-compact.json"),false);
                    map["WorkspaceHeight"]=Look.S(560); map["ReadingHeight"]=560;
                    File.WriteAllText(settingsPath,Files.Json().Serialize(map),Files.Utf8);
                }
            }
            File.WriteAllText(Path.Combine(staging,"v2-import.txt"),"Independent copy of ds data after normal shutdown. UTC: "+DateTime.UtcNow.ToString("o"),new UTF8Encoding(false));
            Directory.Move(staging,target); // Never merges into or overwrites existing v2 data.
        } catch(Exception e) { throw new IOException("First-run data import was not completed. Source data is unchanged; partial copy is preserved at "+staging+". "+e.Message,e); }
    }
    static void ProbeHotkey(string baseDir) {
        const int probeId=0x544C;
        var config=Files.Load(Path.Combine(baseDir,"data"));
        uint key=IsReleaseDirectory(baseDir)?(uint)config.TriggerKey:(uint)Keys.F13;
        uint modifiers=IsReleaseDirectory(baseDir)?(uint)(config.TriggerModifiers|0x4000):0x4000;
        if(!Native.RegisterHotKey(IntPtr.Zero,probeId,modifiers,key))
            throw new InvalidOperationException("The configured shortcut is already in use. Exit its owner normally, or start this copy with --no-hotkey. Other instances were not changed.");
        Native.UnregisterHotKey(IntPtr.Zero,probeId);
    }
    static int ExitOwnPath(string current) {
        // No global coordination lock: another version cannot block this control command.
        var running=RunningVersions(current);
        try { foreach(var item in running) Stop(item); return 0; }
        finally { foreach(var item in running) item.Dispose(); }
    }
    static void ReportFailure(string baseDir,string[] args,Exception error) {
        try { File.AppendAllText(Path.Combine(baseDir,"launcher.log"),DateTime.UtcNow.ToString("o")+" START_FAILED "+error.GetType().Name+": "+error.Message+Environment.NewLine,new UTF8Encoding(false)); } catch { }
        if(!args.Contains("--quiet")) MessageBox.Show(Program.DisplayName+" could not start or complete the command. Other versions were not closed.\n\n"+error.Message,Program.DisplayName,MessageBoxButtons.OK,MessageBoxIcon.Error);
    }
#if !SHARE
    // Filesystem-only policy check. It never enumerates/signals processes,
    // creates an instance mutex or calls RegisterHotKey.
    public static int PolicyTest(string baseDir) {
        string folder=Path.Combine(baseDir,"tests","instance-policy-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));
        Directory.CreateDirectory(folder); var results=new List<string>();
        Action<bool,string> check=delegate(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); results.Add("PASS: "+message); };
        try {
            foreach(string legacy in new[]{"ds","original","copy","frozen","nobg"}) {
                bool rejected=false; try { VersionPath(legacy); } catch(InvalidOperationException) { rejected=true; }
                check(rejected,"Legacy launch is disabled: "+legacy);
            }
            string fixture=Path.Combine(folder,"portable-release"); Directory.CreateDirectory(fixture);
            File.WriteAllText(Path.Combine(fixture,"release-channel.txt"),"release",new UTF8Encoding(false));
            check(IsReleaseDirectory(fixture) && Launchable(Path.Combine(fixture,"TermLens.exe")),"Marked portable release does not depend on its installation path");
            check(!Launchable(Path.Combine(folder,"unmarked","TermLens.exe")),"Unmarked arbitrary executable is not launchable");
            ImportFirstData(fixture);
            check(Directory.Exists(Path.Combine(fixture,"data")) && Directory.GetFileSystemEntries(Path.Combine(fixture,"data")).Length==0,"Release creates an empty independent profile without ds import");
            check(DirectoryMutex(fixture)!=DirectoryMutex(Path.GetDirectoryName(VersionPath("v2"))),"Release and v2 instance mutex identities differ by directory");
            check(!EqualPath(Path.Combine(fixture,"TermLens.exe"),VersionPath("v2")),"Exact-path control cannot match the v2 executable");
            results.Add("No running instance, global hotkey, user startup entry or real data directory was touched.");
            File.WriteAllLines(Path.Combine(folder,"results.txt"),results,new UTF8Encoding(false)); return 0;
        } catch(Exception e) { results.Add("FAIL: "+e.Message); File.WriteAllLines(Path.Combine(folder,"results.txt"),results,new UTF8Encoding(false)); return 1; }
    }
#endif
    public static int Run(string baseDir,string[] args) {
        if(args.Contains("--exit")) {
            try { return ExitOwnPath(Application.ExecutablePath); }
            catch(Exception e) { ReportFailure(baseDir,args,e); return 1; }
        }
        bool held=false; Mutex directory=null;
        using(var coordination=new Mutex(false,CoordinationName)) {
            try {
                try { held=coordination.WaitOne(45000); } catch(AbandonedMutexException) { held=true; }
                if(!held) throw new InvalidOperationException("Another startup is still running. Please retry after it finishes.");
                string current=Application.ExecutablePath, target=current;
                int versionIndex=Array.IndexOf(args,"--launch-version");
                if(versionIndex>=0) { if(versionIndex+1>=args.Length) throw new InvalidOperationException("--launch-version needs a version name."); target=VersionPath(args[versionIndex+1]); }
                CheckIdentity(target);
                if(!Launchable(target)) throw new InvalidOperationException("This entry is disabled. Only v2 and a marked release can be started.");
                var same=RunningVersions(target);
                try {
                    if(same.Count>1) throw new InvalidOperationException("Multiple instances of this exact executable already exist. Exit them normally before retrying.");
                    if(same.Count==1) {
                        IntPtr window=WaitWindow(same[0].Process);
                        if(!args.Contains("--tray") && !Native.PostMessage(window,MainWindow.ShowMessage,args.Contains("--settings")?new IntPtr(1):IntPtr.Zero,IntPtr.Zero)) throw new InvalidOperationException("Could not activate this copy's existing instance.");
                        return 0;
                    }
                } finally { foreach(var item in same) item.Dispose(); }
                if(!EqualPath(current,target)) {
                    string switches=args.Contains("--tray")?"--tray":"";
                    if(args.Contains("--settings")) switches+=" --settings";
                    if(args.Contains("--no-hotkey")) switches+=" --no-hotkey";
                    if(args.Contains("--quiet")) switches+=" --quiet";
                    // The child needs the same coordination mutex. Never wait while owning it.
                    coordination.ReleaseMutex(); held=false;
                    using(var child=Process.Start(new ProcessStartInfo(target,switches) { WorkingDirectory=Path.GetDirectoryName(target),UseShellExecute=false })) WaitWindow(child);
                    return 0;
                }
                // Release reports a conflict in its shortcut settings so it can be
                // changed without terminating or changing another running copy.
                if(!Program.IsRelease && !args.Contains("--no-hotkey")) ProbeHotkey(baseDir);
                bool first; directory=new Mutex(true,DirectoryMutex(baseDir),out first);
                if(!first) throw new InvalidOperationException("This directory already has an instance starting. Retry once its window is ready.");
                ImportFirstData(baseDir);
                using(var windowToRun=new MainWindow(Path.Combine(baseDir,"data"),args.Contains("--tray"))) {
                    windowToRun.PrepareHotkey();
                    windowToRun.Shown+=delegate { if(held) { coordination.ReleaseMutex(); held=false; } };
                    Application.Run(windowToRun);
                }
                return 0;
            } catch(Exception e) { ReportFailure(baseDir,args,e); return 1; }
            finally { if(held) coordination.ReleaseMutex(); if(directory!=null) directory.Dispose(); }
        }
    }
}
}
