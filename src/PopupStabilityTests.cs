using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TermLens {
public static partial class Tests {
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")] static extern int WindowStyle(IntPtr handle,int index);
    static async Task PopupStability(MainWindow app,WorkspaceWindow window,string folder) {
        Check(new Settings().ReadingTopMost,"New profiles default to a pinned reading window");
        window.SetPinned(true);
        window.PinButton.PerformClick();
        Check(!window.TopMost && !Files.Load(folder).ReadingTopMost && !Field<CheckBox>(app,"readingTopMost").Checked,"Unpin applies immediately and is remembered in settings");
        Check((WindowStyle(window.Handle,-20)&8)==0,"Unpin removes the native always-on-top window style");
        using(var reopened=new WorkspaceWindow(new WorkSession(),Files.Load(folder))) Check(!reopened.TopMost,"A new reading window remembers that pinning was turned off");
        var preference=Field<CheckBox>(app,"readingTopMost"); preference.Checked=true;
        Check(window.TopMost && window.PinButton.Primary && Files.Load(folder).ReadingTopMost,"The reading preference restores pinning and synchronizes the header button");
        Check((WindowStyle(window.Handle,-20)&8)!=0,"Pinning sets the native always-on-top style");
        using(var reopened=new WorkspaceWindow(new WorkSession(),Files.Load(folder))) Check(reopened.TopMost,"A new reading window remembers that pinning was turned on");
        var before=window.Size; var entry=window.Session.Entries[0]; bool finished=entry.Finished; string answer=entry.Answer;
        SendMessage(window.Handle,0x231,IntPtr.Zero,IntPtr.Zero);
        entry.Finished=true; entry.Answer=String.Concat(Enumerable.Repeat("A small example. ",80)); window.UpdateEntry(entry);
        Check(window.Size==before,"A new answer does not resize the main window during a native drag");
        SendMessage(window.Handle,0x232,IntPtr.Zero,IntPtr.Zero);
        entry.Answer=answer; entry.Finished=finished; window.UpdateEntry(entry);
        foreach(bool dark in new[]{true,false}) {
            Theme.TestDark=dark; Theme.Refresh(); Theme.Apply(window); window.ApplyReading(14,440,0);
            Check(window.PinButton.Right<Field<SoftButton>(window,"last").Left && Field<SoftButton>(window,"last").Right<Field<SoftButton>(window,"settingsButton").Left && Field<SoftButton>(window,"settingsButton").Right<window.CloseButton.Left,"Pin, Last, Settings and Close icons do not overlap at minimum width in "+(dark?"dark":"light"));
            Shot(window,Path.Combine(folder,dark?"pinned-dark.png":"pinned-light.png"));
        }
        Theme.TestDark=true; Theme.Refresh(); window.ApplyReading(12,560,0);
        using(var mini=new QuickDictionary(14)) {
            mini.Owner=window;
            var work=Screen.FromControl(window).WorkingArea;
            var anchor=new Rectangle(work.Left+120,work.Top+60,130,20);
            mini.Begin("perpendicular",anchor);
            await Task.Delay(100);
            Rectangle initial=mini.Bounds; int moves=0,resizes=0,regions=0,initialRenders=mini.Body.MarkdownRenderCount;
            mini.LocationChanged+=delegate { moves++; }; mini.SizeChanged+=delegate { resizes++; }; mini.RegionChanged+=delegate { regions++; };
            string text="";
            for(int i=0;i<120;i++) { text+="A right angle is like a corner of a square. "; mini.UpdateAnswer(text,false); await Task.Delay(3); }
            await Task.Delay(100);
            Check(mini.Bounds==initial && moves==0 && resizes==0 && regions==0,"120 streaming chunks keep dictionary position, size and rounded region steady");
            // Loading RTF emits more than one native TextChanged notification.
            // Count actual document renders to measure stream coalescing.
            Check(mini.Body.Text==text && mini.Body.MarkdownRenderCount-initialRenders<60,"Dictionary renders the latest text while coalescing rapid stream updates");
            mini.UpdateAnswer(text,true);
            Check(mini.Body.Text==text && moves==0 && resizes<=1 && regions<=1 && work.Contains(mini.Bounds),"Completion keeps all text, stays on-screen, and fits height at most once without moving");
            SendMessage(mini.Body.Handle,0xB6,IntPtr.Zero,new IntPtr(8));
            int position=mini.Body.GetPositionFromCharIndex(0).Y;
            mini.UpdateAnswer(text+" A final example.",false); await Task.Delay(100);
            Check(position<0 && mini.Body.GetPositionFromCharIndex(0).Y==position,"Streaming preserves the dictionary reader's vertical scroll position");
            Look.Shape(mini,10); Look.Shape(mini,10);
            Check(regions<=1,"Repeated layout with unchanged bounds does not recreate the rounded region");
            Shot(mini,Path.Combine(folder,"stable-dictionary.png"));
            mini.Close();
        }
        Foreground(window);
    }
}
}
