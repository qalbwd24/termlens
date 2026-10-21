using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace TermLens {
// Guards the settings shell against high-DPI sizing regressions.
//
// Two classes of defect were found by hand in this tree:
//   1. layout overflow  - a control escapes its parent's client area. At 168 DPI
//      three page labels used AutoSize without a MaximumSize, so they grew to
//      their unwrapped text width (up to 762 px inside a 534 px page).
//   2. text clipping    - a single-line control is narrower than the text it has
//      to show, so the value is cut off (the voice list was the worst case).
//
// Each page is selected and given a moment to lay out before it is measured:
// measuring a never-shown page reports the TableLayoutPanel columns at their
// unresolved width and produces meaningless results.
public static partial class Tests {
    // Overflow is enforced at zero. The cause was an unstyled single column:
    // TableLayoutPanel columns default to AutoSize behaviour, and Microsoft's
    // AutoSize documentation states that the "control always shrinks to fit in
    // the cell" rule does not apply "when the cell is part of an AutoSize row or
    // column". So the widest child set the column width and every row left the
    // page (762 px inside a 534 px page).
    //
    // Three earlier fixes failed, and the reason is worth keeping:
    //   - MaximumSize on an AutoSize label: no effect;
    //   - AutoSize=false + Dock=Fill with an AutoSize row: threw
    //     "parameter is not valid" when the page was clicked in the running app;
    //   - ColumnStyle(Percent,100) alone: turned into a layout loop because an
    //     AutoSize row asks the label for its height while the label needs the
    //     column width in order to wrap. The page went from 750 ms to 80 s.
    // What works is breaking that circle: a percentage column, a fixed-height
    // hint row, and AutoSize=false on the hint label.
    //
    // The same class of loop also lived in Flow(): AutoSize + GrowAndShrink with
    // WrapContents asks for the width and derives the width from the children.
    // With that fixed too, all eight pages open in about 1.7 s in total.
    const int KnownOverflowControls=0;
    const int KnownClippedControls=0;
    public static int Layout(string root) {
        string folder=TestRoot(root,"layout"); int exit=0;
        var window=new MainWindow(folder);
        window.Shown+=delegate {
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var tabs=(SettingsPages)typeof(MainWindow).GetField("tabs",flags).GetValue(window);
            int page=0, overflow=0, clipped=0, controlsSeen=0, pagesSeen=0;
            var overflowDetail=new List<string>(); var clippedDetail=new List<string>();
            bool finished=false;
            // Write the record and close exactly once, from either exit path.
            Action finish=delegate {
                if(finished) return;
                finished=true;
                File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8);
                typeof(MainWindow).GetField("exiting",flags).SetValue(window,true); window.Close();
            };
            Action complete=delegate {
                results.Add("INFO: scanned "+pagesSeen+" pages, "+controlsSeen+" controls");
                foreach(var d in overflowDetail) results.Add("INFO overflow: "+d);
                foreach(var d in clippedDetail) results.Add("INFO clipped: "+d);
                if(overflow==KnownOverflowControls) results.Add("PASS: Settings layout overflow unchanged ("+overflow+" known)");
                else { results.Add("FAIL: settings layout overflow changed: expected "+KnownOverflowControls+", found "+overflow); exit=1; }
                if(clipped==KnownClippedControls) results.Add("PASS: Settings text clipping unchanged ("+clipped+" known)");
                else { results.Add("FAIL: settings text clipping changed: expected "+KnownClippedControls+", found "+clipped); exit=1; }
                // The reading window is the view the user actually works in, and
                // it was never scanned before. Check it at its default size and
                // again after shrinking it to the enforced minimum, so a layout
                // that only works while there is room cannot pass.
                try {
                    using(var workspace=new WorkspaceWindow(new WorkSession(),Files.Load(folder))) {
                        workspace.Show();
                        System.Windows.Forms.Application.DoEvents();
                        int wo=0,wc=0,wseen=0;
                        var wDetail=new List<string>();
                        ScanPage(workspace,ref wo,ref wc,ref wseen,wDetail,new List<string>());
                        results.Add("INFO: workspace "+workspace.Width+"x"+workspace.Height+" controls="+wseen+" overflow="+wo+" clipped="+wc);
                        foreach(var d in wDetail) results.Add("INFO workspace: "+d);
                        workspace.Size=workspace.MinimumSize;
                        System.Windows.Forms.Application.DoEvents();
                        int mo=0,mc=0,mseen=0;
                        var mDetail=new List<string>();
                        ScanPage(workspace,ref mo,ref mc,ref mseen,mDetail,new List<string>());
                        results.Add("INFO: workspace minimum "+workspace.MinimumSize.Width+"x"+workspace.MinimumSize.Height+" overflow="+mo+" clipped="+mc);
                        foreach(var d in mDetail) results.Add("INFO workspace small: "+d);
                        Check(wo==0 && mo==0,"Reading window keeps its controls inside the window at both sizes ("+wo+" and "+mo+")");
                    }
                } catch(Exception e) { results.Add("FAIL: reading window scan threw "+e.GetType().Name+": "+e.Message); exit=1; }
                // The result popup places its controls by explicit coordinates
                // rather than docking, so it is the most likely place for a
                // high-DPI overlap. Give it a real answer before measuring, since
                // its height follows the content.
                try {
                    using(var card=new ResultPopup()) {
                        card.BeginLookup("recursion",new Rectangle(150,100,120,20));
                        card.SetAnswer("Meaning: A function calls itself to solve a smaller part.\n\nExample: Count down from three to zero, then stop.");
                        card.Complete(false);
                        card.Show();
                        System.Windows.Forms.Application.DoEvents();
                        int po=0,pc=0,pseen=0;
                        var pDetail=new List<string>();
                        ScanPage(card,ref po,ref pc,ref pseen,pDetail,new List<string>());
                        results.Add("INFO: popup "+card.Width+"x"+card.Height+" controls="+pseen+" overflow="+po+" clipped="+pc);
                        foreach(var d in pDetail) results.Add("INFO popup: "+d);
                        Check(po==0,"Result popup keeps its controls inside the window ("+po+")");
                    }
                } catch(Exception e) { results.Add("FAIL: result popup scan threw "+e.GetType().Name+": "+e.Message); exit=1; }
                finish();
            };
            try {
                Check(tabs!=null,"Settings shell exposes its pages");
                Check(tabs.TabPages.Count>=8,"All settings pages are registered ("+tabs.TabPages.Count+")");
                var timer=new Timer { Interval=260 };
                bool inTick=false;
                timer.Tick+=delegate {
                    // DoEvents pumps messages, which can re-enter this handler
                    // before 'page' advances; without the guard the same page is
                    // scanned recursively until the stack blows.
                    if(inTick) return;
                    inTick=true;
                    try {
                        if(page<tabs.TabPages.Count) {
                            var current=tabs.TabPages[page];
                            tabs.SelectedIndex=page;
                            current.PerformLayout();
                            System.Windows.Forms.Application.DoEvents();
                            ScanPage(current,ref overflow,ref clipped,ref controlsSeen,overflowDetail,clippedDetail);
                            pagesSeen++; page++;
                            return;
                        }
                        timer.Stop(); timer.Dispose();
                        complete();
                    } catch(Exception e) { results.Add("FAIL: layout scan threw "+e.GetType().Name+": "+e.Message); exit=1; finish(); }
                    finally { inTick=false; }
                };
                timer.Start();
            } catch(Exception e) {
                results.Add("FAIL: layout test could not start: "+e.GetType().Name+": "+e.Message); exit=1; finish();
            }
        };
        Application.Run(window); return exit;
    }
    static void ScanPage(Control page,ref int overflow,ref int clipped,ref int controlsSeen,List<string> overflowDetail,List<string> clippedDetail) {        var stack=new Stack<Control>(); stack.Push(page);
        while(stack.Count>0) {
            var c=stack.Pop();
            foreach(Control child in c.Controls) stack.Push(child);
            // Editors scroll or ellipsize their own content; only the fixed-size
            // labels, buttons and check boxes are judged here.
            if(c is NumericUpDown || c is TextBoxBase || c is ComboBox) continue;
            if(c.Width<=0 || c.Height<=0) continue;
            controlsSeen++;
            bool clippedNativeGutter=c.Parent!=null && c.Left==0 && c.Right-c.Parent.ClientSize.Width==SystemInformation.VerticalScrollBarWidth && ((c is TimelinePanel && c.Parent.Name=="ReadingViewport") || (c is ComposerBox && c.Parent.Name=="ComposerViewport"));
            if(!clippedNativeGutter && c.Parent!=null && c.Parent.ClientSize.Width>0 && c.Right>c.Parent.ClientSize.Width+2) {
                overflow++;
                if(overflowDetail.Count<8) overflowDetail.Add(page.Text+"/"+c.GetType().Name+" right="+c.Right+" > parent="+c.Parent.ClientSize.Width
                    +" self="+c.Width+"x"+c.Height+" dock="+c.Dock+" autoSize="+autoSizeOf(c)+" parentType="+c.Parent.GetType().Name);
            }
            string text=null;
            var icon=c as SoftButton;
            if(icon!=null && !String.IsNullOrEmpty(icon.IconName)) { if(String.IsNullOrEmpty(icon.AccessibleName)) clipped++; continue; }
            if(c is SoftButton || c is Button || c is Label || c is CheckBox) text=c.Text;
            if(String.IsNullOrEmpty(text) || text.IndexOf('\n')>=0) continue; // multi-line text wraps
            int need=TextRenderer.MeasureText(text,c.Font).Width;
            if(need>c.Width) {
                clipped++;
                int parentH=c.Parent!=null?c.Parent.ClientSize.Height:0;
                if(clippedDetail.Count<8) clippedDetail.Add(page.Text+"/"+c.GetType().Name+" \""+(text.Length>32?text.Substring(0,32)+"…":text)+"\" w="+c.Width+" need="+need
                    +" font="+c.Font.Name+"/"+c.Font.SizeInPoints.ToString("F1")+"pt h="+c.Height+" parentH="+parentH+" oneLine="+c.Font.Height);
            }
        }
    }
    // Reports whether a control opts into AutoSize, whatever its concrete type
    // exposes, so an overflow line says whether the sizing rule applies.
    static string autoSizeOf(Control c) {
        try {
            var p=c.GetType().GetProperty("AutoSize");
            if(p==null) return "n/a";
            object v=p.GetValue(c,null);
            return v==null?"n/a":v.ToString();
        } catch { return "?"; }
    }
}
}
