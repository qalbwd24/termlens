using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TermLens {
public static partial class Tests {
    public static int ReleaseScroll(string root) {
        string folder=TestRoot(root,"release-scroll"); int exit=0;
        var settings=new Settings { ReadingTopMost=false,ReadingFontSize=12,ReadingWidth=360,ReadingHeight=300 };
        using(var primary=new ReleaseLookupWindow(settings)) {
            primary.Shown+=async delegate {
                try {
                    for(int mode=0;mode<2;mode++) {
                        var window=mode==0?primary:new ReleaseLookupWindow(settings,true) { Owner=primary };
                        try {
                            if(mode==1) window.ShowAt(new Rectangle(primary.Right-30,primary.Top+50,5,20));
                            var item=new Lookup { Term="selected word",Answer="Opening **meaning**.",Status="pending" };
                            window.ShowLookup(item,null);
                            var timeline=Field<TimelinePanel>(window,"timeline");
                            var bar=Field<SlimScrollBar>(window,"scrollbar");
                            for(int batch=0;batch<10;batch++) {
                                item.Answer+="\n\n- More simple words to explain the selected text. **Important meaning** stays readable.";
                                window.UpdateAnswer(); await Task.Delay(20);
                                Check(timeline.AutoScrollPosition.Y==0 && bar.Value==0,"Window "+mode+": streaming past the viewport keeps the opening visible, batch "+batch);
                            }
                            timeline.AutoScrollPosition=new Point(0,Look.S(100));
                            int scroll=-timeline.AutoScrollPosition.Y;
                            Check(scroll>0,"Window "+mode+": reader can scroll manually");
                            window.Answer.Focus(); window.Answer.Select(window.Answer.Text.IndexOf("Important",StringComparison.Ordinal),9);
                            string selected=window.Answer.SelectedText;
                            var position=window.Answer.PointToScreen(window.Answer.GetPositionFromCharIndex(window.Answer.SelectionStart));
                            item.Answer+=String.Concat(Enumerable.Repeat("\n\nMore content below the reader.",8));
                            window.UpdateAnswer(); await Task.Delay(40);
                            Check(-timeline.AutoScrollPosition.Y==scroll && bar.Value==scroll && window.Answer.SelectedText==selected && window.Answer.PointToScreen(window.Answer.GetPositionFromCharIndex(window.Answer.SelectionStart))==position,"Window "+mode+": streamed updates preserve manual position and selected text on screen");
                            timeline.AutoScrollPosition=new Point(0,timeline.AutoScrollMinSize.Height);
                            scroll=-timeline.AutoScrollPosition.Y;
                            item.Answer+=String.Concat(Enumerable.Repeat("\n\nAnother paragraph generated below.",8));
                            window.UpdateAnswer(); await Task.Delay(20);
                            Check(-timeline.AutoScrollPosition.Y==scroll && -timeline.AutoScrollPosition.Y+timeline.ClientSize.Height<timeline.AutoScrollMinSize.Height,"Window "+mode+": being at the previous bottom does not enable automatic following");
                            item.Status="complete"; window.UpdateAnswer(); window.Finish(true,"");
                            Check(-timeline.AutoScrollPosition.Y==scroll,"Window "+mode+": completion does not jump");
                            window.ShowLookup(new Lookup { Term="next",Answer="Next answer",Status="pending" },null);
                            Check(timeline.AutoScrollPosition.Y==0,"Window "+mode+": a new lookup resets to its opening");
                        } finally { if(mode==1) window.Dispose(); }
                    }
                    results.Add("INFO: No API, profile, global keyboard input or hotkey registration was used.");
                } catch(Exception e) { results.Add("FAIL: "+e); exit=1; }
                finally { File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); primary.Close(); }
            };
            Application.Run(primary);
        }
        return exit;
    }
}
}
