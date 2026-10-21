using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TermLens {
public static partial class Tests {
    static void SettingsShot(Form window,string path) {
        window.Refresh(); Application.DoEvents();
        using(var bitmap=new Bitmap(window.Width,window.Height)) {
            using(var graphics=Graphics.FromImage(bitmap)) {
                IntPtr dc=graphics.GetHdc(); try { PrintWindow(window.Handle,dc,2); } finally { graphics.ReleaseHdc(dc); }
            }
            if(bitmap.GetPixel(bitmap.Width/2,bitmap.Height/2).ToArgb()==Color.Black.ToArgb()) window.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
            Check(bitmap.GetPixel(bitmap.Width/2,bitmap.Height/2).ToArgb()!=Color.Black.ToArgb(),"Settings screenshot is rendered, not a black PrintWindow frame");
            bitmap.Save(path);
        }
    }
    static IEnumerable<Control> SettingsDescendants(Control root) {
        foreach(Control child in root.Controls) { yield return child; foreach(var nested in SettingsDescendants(child)) yield return nested; }
    }
    static void CheckSettingsActions(Control page,string scope) {
        int checkedActions=0;
        foreach(var action in SettingsDescendants(page).OfType<Button>().Where(b=>b.Visible)) {
            var parent=action.Parent; if(parent==null || parent.ClientSize.Width<=0 || parent.ClientSize.Height<=0) continue;
            var scroll=parent as ScrollableControl;
            // A scrollable page can intentionally extend beyond its viewport.
            // A fixed-height FlowLayoutPanel must actually contain every row;
            // scrolling a distant ancestor cannot expose content it clips.
            bool canScroll=scroll!=null && scroll.AutoScroll && !(parent is FlowLayoutPanel);
            Rectangle accessible=canScroll?scroll.DisplayRectangle:parent.ClientRectangle;
            bool inside=action.Right<=accessible.Right+2 && action.Bottom<=accessible.Bottom+2;
            Check(inside,scope+": action "+(action.AccessibleName??action.Text)+" fits its "+parent.GetType().Name+" ("+action.Bounds+" within "+accessible+")");
            checkedActions++;
        }
        results.Add("INFO: "+scope+" checked "+checkedActions+" visible action bounds");
    }
    static void CheckSettingsEditor(TextBoxBase editor,int lines,string scope) {
        // This checks usable editing/reading area, not the entire scrollable text.
        Check(editor.Visible,scope+" editor is visible on its selected page");
        Check(editor.ClientSize.Width>=Math.Max(100,editor.Font.Height*6) && editor.ClientSize.Height>=editor.Font.Height*lines,
            scope+" has readable text space ("+editor.ClientSize.Width+"x"+editor.ClientSize.Height+", font height "+editor.Font.Height+")");
    }
    public static int V2Settings(string root) {
        string folder=TestRoot(root,"v2-settings"); int exit=0;
        Files.Save(folder,new Settings { UiLanguage="en",SpeechPreload=false });
        var window=new MainWindow(folder);
        window.Shown+=async delegate {
            try {
                await Task.Delay(100);
                var pages=Field<SettingsPages>(window,"tabs");
                window.TopMost=true; Foreground(window);
                Check(pages.TabPages.Count==8,"Grouped settings navigation preserves all eight pages");
                Check(Field<List<SoftButton>>(pages,"links").Count==8,"Grouped navigation keeps eight clickable page links");
                var minimum=window.MinimumSize;
                Check(minimum.Width>0 && minimum.Height>0,"Settings window declares a minimum size");
                window.Size=new Size(Math.Max(1,minimum.Width-160),Math.Max(1,minimum.Height-120));
                await Task.Delay(80);
                Check(window.Width>=minimum.Width && window.Height>=minimum.Height,"Attempting to shrink settings below its minimum is clamped");
                results.Add("INFO: requested below minimum "+minimum+"; actual "+window.Size+"; DPI scale "+Look.Scale);
                Field<TextBox>(window,"input").Text="perpendicular";
                Field<RichTextBox>(window,"output").Text="Two lines are perpendicular when they meet at a right angle.\n\nExample: the two edges of a square.";
                var language=Field<ComboBox>(window,"languageChoice");
                Theme.TestDark=true; Theme.Refresh();
                foreach(int locale in new[]{0,1}) {
                    language.SelectedIndex=locale;
                    string code=locale==0?"en":"zh";
                    for(int i=0;i<pages.TabPages.Count;i++) {
                        pages.ClickPage(i); await Task.Delay(70);
                        Check(pages.SelectedIndex==i && pages.TabPages[i].Visible,code+": navigation reaches page "+i);
                        var page=pages.TabPages[i];
                        page.AutoScrollPosition=Point.Empty; Application.DoEvents();
                        string scope=code+" page "+i+" "+page.Text;
                        Check(page.ClientSize.Width>=160 && page.ClientSize.Height>=100,scope+" retains a usable page viewport");
                        CheckSettingsActions(page,scope);
                        if(i==0) { CheckSettingsEditor(Field<TextBox>(window,"input"),2,scope+" source"); CheckSettingsEditor(Field<RichTextBox>(window,"output"),2,scope+" answer"); }
                        if(i==1) CheckSettingsEditor(Field<TextBox>(window,"prompt"),3,scope+" prompt");
                        if(i==3) CheckSettingsEditor(Field<RichTextBox>(window,"historyAnswer"),2,scope+" history detail");
                        if(i==4) CheckSettingsEditor(Field<TextBox>(window,"teacherPrompt"),3,scope+" teacher prompt");
                        if(i==7) CheckSettingsEditor(Field<TextBox>(window,"refinePrompt"),3,scope+" polish prompt");
                        SettingsShot(window,Path.Combine(folder,"settings-"+code+"-"+i.ToString("00")+"-minimum-dark.png"));
                    }
                }
                // Dark screenshots for these pages already exist in both languages.
                // Explicitly revisit the reading and history surfaces in light mode.
                Theme.TestDark=false; Theme.Refresh();
                foreach(int locale in new[]{0,1}) {
                    language.SelectedIndex=locale;
                    foreach(int pageIndex in new[]{0,3}) {
                        pages.ClickPage(pageIndex); await Task.Delay(70);
                        CheckSettingsActions(pages.TabPages[pageIndex],"light "+locale+" page "+pageIndex);
                        SettingsShot(window,Path.Combine(folder,"settings-"+(locale==0?"en":"zh")+"-"+pageIndex.ToString("00")+"-minimum-light.png"));
                    }
                }
                results.Add("INFO: 16 minimum-size dark screenshots plus 4 light screenshots; visual review is still required.");
                foreach(bool dark in new[]{true,false}) {
                    Theme.TestDark=dark; Theme.Refresh(); language.SelectedIndex=0;
                    foreach(int pageIndex in new[]{6,3,4,6,0,2,6}) {
                        pages.ClickPage(pageIndex); pages.Focus(); await Task.Delay(50); DwmFlush();
                        Check(SettingsDescendants(pages).Where(c=>c.Visible).All(c=>c.BackColor.A==255),"Visible settings controls stay opaque after page and theme changes");
                        using(var before=VisiblePixels(pages)) {
                            RedrawWindow(window.Handle,IntPtr.Zero,IntPtr.Zero,0x1|0x4|0x80|0x100); DwmFlush();
                            using(var after=VisiblePixels(pages)) {
                                int differences=DifferentPixels(before,after);
                                results.Add("INFO: settings switch "+pageIndex+" dark="+dark+" repaint differences="+differences);
                                if(differences>25) { before.Save(Path.Combine(folder,"switch-"+pageIndex+"-"+dark+"-before.png")); after.Save(Path.Combine(folder,"switch-"+pageIndex+"-"+dark+"-repaint.png")); }
                                Check(differences<=25,"Settings page switches leave no stale pixels: "+pageIndex+" dark="+dark);
                            }
                        }
                    }
                    foreach(int extra in new[]{80,0}) {
                        window.Size=new Size(minimum.Width+Look.S(extra),minimum.Height+Look.S(extra/2)); await Task.Delay(80); DwmFlush();
                        using(var before=VisiblePixels(pages)) {
                            RedrawWindow(window.Handle,IntPtr.Zero,IntPtr.Zero,0x1|0x4|0x80|0x100); DwmFlush();
                            using(var after=VisiblePixels(pages)) Check(DifferentPixels(before,after)<=25,"Resizing settings leaves no stale text in "+(dark?"dark":"light")+" mode");
                        }
                    }
                    using(var screen=VisiblePixels(pages)) screen.Save(Path.Combine(folder,"settings-presented-"+(dark?"dark":"light")+".png"));
                }
            } catch(Exception e) { results.Add("FAIL: "+e); exit=1; try { SettingsShot(window,Path.Combine(folder,"settings-failure.png")); } catch { } }
            finally {
                Theme.TestDark=null; Theme.Refresh();
                File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8);
                typeof(MainWindow).GetField("exiting",Private).SetValue(window,true); window.Close();
            }
        };
        Application.Run(window); window.Dispose(); return exit;
    }
}
}
