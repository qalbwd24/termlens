using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace TermLens {
// Drives the settings shell the way a user does - by clicking the left-hand
// navigation button - inside its own process.
//
// This exists because an earlier smoke check synthesised Ctrl+Tab with
// keybd_event and reported success, but no page ever changed: this shell has no
// keyboard navigation, and the key went to whichever desktop window had focus.
// Clicks sent through PerformClick() cannot leave the process, so the check is
// both valid and unable to disturb the user's own typing.
//
// The value of the check is the layout work that runs when a page is shown: at
// one point a page threw "parameter is not valid" from WinForms layout and only
// showed up here, because --layout-test scanned pages in isolation.
public static partial class Tests {
    public static int Pages(string root) {
        string folder=TestRoot(root,"pages"); int exit=0;
        var window=new MainWindow(folder);
        window.Shown+=delegate {
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var tabs=(SettingsPages)typeof(MainWindow).GetField("tabs",flags).GetValue(window);
            var slow=new List<string>();
            try {
                Check(tabs!=null,"Settings shell exposes its pages");
                int count=tabs.TabPages.Count;
                Check(count>=8,"All settings pages are registered ("+count+")");
                for(int i=0;i<count;i++) {
                    string title=tabs.TabPages[i].Text;
                    var watch=Stopwatch.StartNew();
                    // Click the navigation button; the click handler sets the page.
                    tabs.ClickPage(i);
                    System.Windows.Forms.Application.DoEvents();
                    watch.Stop();
                    Check(tabs.SelectedIndex==i,"Clicking page "+i+" ("+title+") selects it");
                    Check(tabs.TabPages[i].Visible,"Page "+i+" ("+title+") is shown");
                    // A page that takes far longer than its neighbours usually means
                    // layout is thrashing rather than simply drawing.
                    if(watch.ElapsedMilliseconds>1200) slow.Add(title+" "+watch.ElapsedMilliseconds+" ms");
                }
                Check(slow.Count==0,"Every settings page opens promptly"+(slow.Count>0?" (slow: "+String.Join(", ",slow.ToArray())+")":""));
                results.Add("INFO: clicked "+count+" navigation buttons without an exception");
            } catch(Exception e) { results.Add("FAIL: settings page click threw "+e.GetType().Name+": "+e.Message); exit=1; }
            finally {
                File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8);
                typeof(MainWindow).GetField("exiting",flags).SetValue(window,true); window.Close();
            }
        };
        Application.Run(window); return exit;
    }
}
}
