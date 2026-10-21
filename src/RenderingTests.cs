using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TermLens {
public static partial class Tests {
    [DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr hwnd,IntPtr rectangle,IntPtr region,uint flags);
    [DllImport("dwmapi.dll")] static extern int DwmFlush();
    static Bitmap VisiblePixels(Control control) {
        var bounds=control.RectangleToScreen(control.ClientRectangle);
        // The native gutter is intentionally outside the clipping host; sample
        // only pixels actually presented by the reading viewport.
        if(control.Parent!=null && control.Parent.Name=="ReadingViewport") bounds=Rectangle.Intersect(bounds,control.Parent.RectangleToScreen(control.Parent.ClientRectangle));
        var bmp=new Bitmap(bounds.Width,bounds.Height);
        using(var graphics=Graphics.FromImage(bmp)) graphics.CopyFromScreen(bounds.Location,Point.Empty,bounds.Size);
        return bmp;
    }
    static int DifferentPixels(Bitmap a,Bitmap b) {
        int count=0;
        for(int y=2;y<a.Height-2;y+=2) for(int x=2;x<a.Width-2;x+=2) {
            var ca=a.GetPixel(x,y); var cb=b.GetPixel(x,y);
            if(Math.Abs(ca.R-cb.R)+Math.Abs(ca.G-cb.G)+Math.Abs(ca.B-cb.B)>30) count++;
        }
        return count;
    }
    static Task<int> CompareRepaint(WorkspaceWindow window,string folder,string label) {
        // Capture a presented frame without first pumping deferred WM_PAINT.
        // Wheel messages may arrive continuously on a high-refresh-rate screen.
        DwmFlush();
        using(var before=VisiblePixels(window.Timeline)) {
            RedrawWindow(window.Handle,IntPtr.Zero,IntPtr.Zero,0x1|0x4|0x80|0x100);
            DwmFlush();
            using(var after=VisiblePixels(window.Timeline)) {
                int different=DifferentPixels(before,after);
                results.Add("INFO: "+label+": "+different+" sampled pixels differed from a forced full repaint.");
                if(different>25) { before.Save(Path.Combine(folder,label+"-before.png")); after.Save(Path.Combine(folder,label+"-correct.png")); }
                return Task.FromResult(different);
            }
        }
    }
    public static int Rendering(string root) {
        string folder=TestRoot(root,"rendering"); int exit=0;
        var session=new WorkSession();
        for(int i=0;i<3;i++) session.Entries.Add(new WorkEntry { Text="Rendering sample "+i,Answer=String.Concat(Enumerable.Repeat("## A right angle\nA **right angle** is a corner of a square. 中文说明。 $\\frac{1}{2}$\n- Simple *example* with `x_axis`\n",15)),Finished=true });
        var window=new WorkspaceWindow(session,new Settings { ReadingFontSize=14,ReadingWidth=532,ReadingHeight=886 });
        window.Location=new Point(200,80); window.TopMost=true;
        window.Shown+=async delegate {
            try {
                window.Composer.Focus(); await Task.Delay(180);
                foreach(bool dark in new[]{true,false}) {
                    Theme.TestDark=dark; Theme.Refresh(); Theme.Apply(window); await Task.Delay(100);
                    int worst=0;
                    for(int i=0;i<6;i++) {
                        window.Timeline.Wheel(i<3?-120:120,window.Cards[0].Answer.Font);
                        worst=Math.Max(worst,await CompareRepaint(window,folder,(dark?"dark":"light")+"-scroll-"+i));
                    }
                    Check(worst<=25,"Scroll pixels agree with a fresh repaint in "+(dark?"dark":"light")+" mode");
                    foreach(int command in new[]{1,3,2,0}) {
                        SendMessage(window.Timeline.Handle,0x115,new IntPtr(command),IntPtr.Zero);
                        Check(await CompareRepaint(window,folder,(dark?"dark":"light")+"-scrollbar-"+command)<=25,"Native scrollbar line/page movement leaves no stale strip");
                    }
                    window.Timeline.AutoScrollPosition=new Point(0,200);
                    for(int i=0;i<40;i++) window.Timeline.Wheel(i<20?-30:30,window.Cards[0].Answer.Font);
                    Check(await CompareRepaint(window,folder,(dark?"dark":"light")+"-rapid-wheel")<=25,"Back-to-back small wheel events leave no stale pixels without waiting for idle paint");
                    for(int i=0;i<3;i++) {
                        session.Entries[0].Answer=String.Concat(Enumerable.Repeat("Updated explanation line. 中文说明。\n",8+i*4));
                        window.UpdateEntry(session.Entries[0]);
                        Check(await CompareRepaint(window,folder,(dark?"dark":"light")+"-stream-"+i)<=25,"Changed text and card height do not leave stale pixels");
                    }
                }
                var times=new List<double>();
                window.Timeline.AutoScrollPosition=new Point(0,250);
                for(int i=0;i<120;i++) {
                    var watch=Stopwatch.StartNew(); window.Timeline.Wheel(i%2==0?-60:60,window.Cards[0].Answer.Font); window.Timeline.Update();
                    times.Add(watch.Elapsed.TotalMilliseconds);
                }
                times.Sort(); results.Add("INFO: Scroll CPU/paint processing avg="+times.Average().ToString("F2")+" ms; p95="+times[113].ToString("F2")+" ms; not measured display FPS.");
                Shot(window,Path.Combine(folder,"main-repaint.png"));
            } catch(Exception e) { exit=1; results.Add(e.ToString()); }
            finally { File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); window.Close(); }
        };
        Application.Run(window); return exit;
    }
}
}
