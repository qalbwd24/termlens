using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace TermLens {
public sealed partial class ReleaseLookupWindow {
    protected override bool ShowWithoutActivation { get { return Program.IsTest; } }
}
public static partial class Tests {
    static void CheckReadingOutline(string path) {
        using(var bitmap=new Bitmap(path)) {
            int inset=Math.Max(1,Look.S(2)/2);
            foreach(var point in new[]{new Point(bitmap.Width/2,inset),new Point(bitmap.Width/2,bitmap.Height-1-inset),new Point(inset,bitmap.Height/2),new Point(bitmap.Width-1-inset,bitmap.Height/2)}) {
                var color=bitmap.GetPixel(point.X,point.Y);
                Check(color.R<8 && color.G<8 && color.B<8,"Native screenshot has a black reading-window outline at "+point);
            }
        }
    }
    static void CheckRenderedDivider(ReleaseLookupWindow window,string path) {
        var origin=window.PointToClient(window.Answer.PointToScreen(Point.Empty));
        using(var bitmap=new Bitmap(path)) {
            int longest=0,lineRows=0,color=Look.Muted.ToArgb();
            int right=Math.Min(bitmap.Width,origin.X+window.Answer.ClientSize.Width),bottom=Math.Min(bitmap.Height,origin.Y+window.Answer.ClientSize.Height);
            for(int y=Math.Max(0,origin.Y);y<bottom;y++) {
                int run=0,rowLongest=0;
                for(int x=Math.Max(0,origin.X);x<right;x++) {
                    run=bitmap.GetPixel(x,y).ToArgb()==color?run+1:0;
                    rowLongest=Math.Max(rowLongest,run);
                }
                longest=Math.Max(longest,rowLongest);
                if(rowLongest>=window.Answer.ClientSize.Width-Look.S(30)) lineRows++;
            }
            Check(longest>=window.Answer.ClientSize.Width-Look.S(30) && lineRows>=1 && lineRows<=Look.S(3),"Native screenshot contains a full-width, thin visible divider ("+longest+" px, "+lineRows+" rows, answer "+window.Answer.ClientSize.Width+" px)");
        }
    }
    public static int ReadingFormat(string root) {
        string folder=TestRoot(root,"reading-format");
        try {
            const string answer="A **class** describes the data and actions its objects can have.\n\n---\n\nHere, a class describes one kind of object. This small piece of code shows a class with one action:\n\n```python\nclass Light:\n    def turn_on(self):\n        self.is_on = True\n```\n\n`Light` is the class. Each light object can use `turn_on` to change its own `is_on` value.";
            using(var window=new ReleaseLookupWindow(new Settings { ReadingTopMost=false,ReadingFontSize=11,ReadingWidth=460,ReadingHeight=650 })) {
                window.ShowInTaskbar=false; window.StartPosition=FormStartPosition.Manual;
                window.Location=new Point(-8000,-8000);
                window.ShowLookup(new Lookup { Id="render-only",Term="class",Answer=answer,Status="complete" },null);
                window.Show(); window.Location=new Point(-8000,-8000); Application.DoEvents();
                Check(Markdown.Parse(answer).Lines.Count(x=>x.Rule)==1,"Two-part output has exactly one native Markdown divider");
                Check(window.Answer.Rtf.Contains("\\pict"),"Divider reaches native RichEdit as a picture");
                Check(!Markdown.PlainText(answer).Contains("---"),"Divider adds no spoken characters to plain text");
                Check(window.Answer.Text.Contains("class Light:") && !window.Answer.Text.Contains("```"),"Code fence renders as selectable code without fence markers");
                int codeAt=window.Answer.Text.IndexOf("class Light:",StringComparison.Ordinal);
                window.Answer.Select(codeAt,"class Light:".Length);
                Check(window.Answer.SelectionFont!=null && window.Answer.SelectionFont.Name=="Consolas","Rendered code uses a monospace font");
                Check(window.Answer.SelectedText=="class Light:","Code remains selectable for a contextual secondary lookup");
                window.Answer.Select(0,0); Application.DoEvents();
                SettingsShot(window,Path.Combine(folder,"primary-with-code.png"));
                for(int theme=0;theme<2;theme++) {
                    Theme.TestDark=theme==0; Theme.Refresh(); window.ApplySettings(new Settings { ReadingTopMost=false,ReadingFontSize=11 });
                    foreach(int logicalWidth in new[]{460,360}) {
                        window.Width=Look.S(logicalWidth);
                        window.ShowLookup(new Lookup { Id="render-only",Term="class",Answer=answer,Status="complete" },null);
                        Application.DoEvents();
                        string shot=Path.Combine(folder,"divider-"+(theme==0?"dark":"light")+"-"+logicalWidth+".png");
                        SettingsShot(window,shot);
                        CheckRenderedDivider(window,shot);
                        CheckReadingOutline(shot);
                    }
                }
                window.Close();
            }
            File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); return 0;
        } catch(Exception error) { results.Add("FAIL: "+error); File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); return 1; }
        finally { Theme.TestDark=null; Theme.Refresh(); }
    }
}
}
