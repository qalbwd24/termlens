using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Speech.Synthesis;
using System.Windows.Forms;

namespace TermLens {
public static partial class Tests {
    public static int Startup(string root) {
        string folder=TestRoot(root,"startup");
        try {
            string path=Path.Combine(folder,"TermLens.lnk");
            var option=new StartupOption(Application.ExecutablePath,path);
            Check(!option.Enabled,"Fresh startup option is unchecked");
            option.SetEnabled(true); Check(option.Enabled && File.Exists(path),"Enable creates a working startup shortcut");
            dynamic shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")); dynamic link=shell.CreateShortcut(path);
            Check((string)link.TargetPath==Application.ExecutablePath && (string)link.Arguments=="--tray","Startup targets this executable in tray mode");
            Marshal.FinalReleaseComObject(link); Marshal.FinalReleaseComObject(shell);
            option.SetEnabled(true); Check(option.Enabled,"Repeated enable is idempotent");
            option.SetEnabled(false); Check(!option.Enabled && !File.Exists(path),"Uncheck removes startup");
            option.SetEnabled(false); Check(!option.Enabled,"Repeated disable is safe");
            shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")); link=shell.CreateShortcut(path);
            link.TargetPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"notepad.exe"); link.Save();
            Marshal.FinalReleaseComObject(link); Marshal.FinalReleaseComObject(shell);
            bool rejected=false; try { option.SetEnabled(false); } catch { rejected=true; }
            Check(rejected && File.Exists(path),"Unrelated shortcut is preserved");
            using(var voice=new SpeechSynthesizer()) Check(voice.GetInstalledVoices().Count>0,"Offline Windows speech is available on this test machine");
            using(var window=new MainWindow(folder,true)) {
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                using(var menu=new ContextMenuStrip()) {
                    var menuOption=new StartupOption(Application.ExecutablePath,Path.Combine(folder,"menu-startup.lnk"));
                    typeof(MainWindow).GetMethod("AddStartupMenu",flags).Invoke(window,new object[]{menu,menuOption});
                    var item=(ToolStripMenuItem)menu.Items[0];
                    Check(item.Text=="Start at sign-in" && !item.Checked,"Tray checkbox starts unchecked for a clean setup");
                    item.PerformClick(); Check(item.Checked && menuOption.Enabled,"Checking the tray item persists startup");
                    item.PerformClick(); Check(!item.Checked && !menuOption.Enabled,"Unchecking the tray item disables startup");
                }
            }
            results.Add("Startup mutations isolated to test shortcuts; no user startup entry changed.");
            File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); return 0;
        } catch(Exception e) { results.Add(e.ToString()); File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); return 1; }
    }
}
}
