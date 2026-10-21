using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TermLens {
public static partial class Tests {
    const string MarkdownSample="# Perpendicular\n\nTwo lines are **perpendicular** (垂直的) when they meet at an angle of **90°**.\n\n## A tiny example\n- A *corner* of a square\n- Use `x_axis` and `y_axis`\n  - The lines meet here\n\n> Think of the corner of a page.\n\n```python\npath = r\"D:\\study\\file.py\"\nif a < b and x * y > 0:\n    print(\"中文 {example}\")\n```\n\n| Term | Easy meaning |\n| --- | --- |\n| **angle** | How far a line turns |\n| `90°` | A square corner |\n\nSee [the explanation](https://example.com/study).\n\nMath stays literal: $x_i * y_i$ and \\(a_b\\).";
    const string MathSample=@"## Math in simple English
An inline fraction $\frac{1}{2}$ means half. A root $\sqrt{x}$ asks which number times itself gives x.

$$
\frac{-b \pm \sqrt{b^2 - 4ac}}{2a}
$$

\[
\sum_{i=1}^{n} i = \frac{n(n+1)}{2}
\]

An integral: $\int_0^1 x^2 \, dx = \frac{1}{3}$

A matrix: $\begin{pmatrix}1 & 2 \\ 3 & 4\end{pmatrix}$

**Readable text after the formulas.**";
    static void StyleAt(StudyText text,string word,Func<Font,bool> expected,string label) {
        int at=text.Text.IndexOf(word,StringComparison.Ordinal); Check(at>=0,label+" is visible"); text.Select(at,word.Length);
        Check(text.SelectionFont!=null && expected(text.SelectionFont),label+" has the native text style"); text.Select(0,0);
    }
    public static int MarkdownUi(string root) {
        string folder=TestRoot(root,"markdown"); int exit=0;
        var session=new WorkSession(); var entry=new WorkEntry { Text="Keep source literally: **word** and x_axis",Answer=MarkdownSample,Finished=true }; session.Entries.Add(entry);
        Files.Save(folder,new Settings { ReadingFontSize=12,ReadingWidth=680,ReadingHeight=0,SpeechProvider="microsoft",SpeechPreload=false });
        var app=new MainWindow(folder,true); WorkspaceWindow window=null;
        app.Shown+=async delegate {
            try {
                await Task.Delay(120);
                window=(WorkspaceWindow)typeof(MainWindow).GetMethod("EnsureWorkspace",Private).Invoke(app,new object[]{(Rectangle?)new Rectangle(100,65,100,20),session}); window.Location=new Point(100,65);
                await Task.Delay(120); var answer=window.Cards[0].Answer;
                Check(window.Cards[0].Original.Text==entry.Text,"Original selected text is never interpreted as Markdown");
                Check(!answer.Text.Contains("**") && !answer.Text.Contains("```") && !answer.Text.Contains("##"),"DeepSeek heading, emphasis and fence markers disappear from the rendered answer");
                Check(answer.Text.Contains("D:\\study\\file.py") && answer.Text.Contains("x * y") && answer.Text.Contains("中文 {example}"),"Code keeps paths, operators, braces and Chinese characters exactly");
                Check(answer.Text.Contains("x_axis") && MathRender.RenderedCount>=2 && answer.Rtf.Contains("\\pict"),"Identifiers stay literal and TeX expressions become native formula pictures");
                Check(answer.Text.Contains("How far a line turns") && answer.Text.Contains("A square corner"),"Native table rows include every cell");
                Check(answer.Text.Contains("the explanation") && !answer.Text.Contains("https://example.com"),"Markdown links render their readable labels");
                StyleAt(answer,"Perpendicular",f=>f.Bold && f.SizeInPoints>12,"Heading");
                StyleAt(answer,"perpendicular",f=>f.Bold,"Bold term");
                StyleAt(answer,"corner",f=>f.Italic,"Italic example");
                StyleAt(answer,"x_axis",f=>f.Name=="Consolas","Inline code");
                StyleAt(answer,"path =",f=>f.Name=="Consolas","Fenced code");
                foreach(bool dark in new[]{true,false}) {
                    Theme.TestDark=dark; Theme.Refresh(); Theme.Apply(window);
                    StyleAt(answer,"perpendicular",f=>f.Bold,"Bold survives "+(dark?"dark":"light")+" theme");
                    answer.Select(0,0); window.Timeline.AutoScrollPosition=Point.Empty;
                    Shot(window,Path.Combine(folder,dark?"markdown-dark.png":"markdown-light.png"));
                }
                window.ApplyReading(18,640,0); window.ApplyReading(18,640,0);
                StyleAt(answer,"perpendicular",f=>f.Bold && f.SizeInPoints>=18,"Zoom preserves emphasis and remembered font size");
                StyleAt(answer,"x_axis",f=>f.Name=="Consolas" && f.SizeInPoints>=18,"Zoom preserves code font");
                Check(answer.ContentHeight(answer.Width)>=answer.GetPositionFromCharIndex(answer.TextLength-1).Y+answer.Font.Height,"Formatted answer height includes its final line");
                string spoken=""; typeof(WorkspaceWindow).GetField("SpeechRequested",Private).SetValue(window,(Action<string>)(value=>spoken=value));
                Field<SoftButton>(window.Cards[0],"listen").PerformClick();
                Check(spoken==Markdown.PlainText(entry.Answer) && !spoken.Contains("**") && !spoken.Contains("```"),"Whole-answer speech matches the Markdown-free preload text");
                Field<SoftButton>(window.Cards[0],"copy").PerformClick(); Check(Clipboard.GetText()==MarkdownSample,"Copy retains the original Markdown answer");
                window.Timeline.AutoScrollPosition=Point.Empty;
                Foreground(window);
                answer.Focus(); int selected=answer.Text.IndexOf("perpendicular",StringComparison.Ordinal); answer.Select(selected,13);
                results.Add("INFO: before Quote focused="+answer.Focused+" foreground="+Native.GetForegroundWindow()+" owner="+window.Handle+" selected="+answer.SelectedText);
                typeof(Control).GetMethod("OnMouseUp",Private).Invoke(answer,new object[]{new MouseEventArgs(MouseButtons.Left,1,20,10,0)});
                if(!window.Quote.Visible || window.SelectedReading(out selectedBounds)!="perpendicular") results.Add("Quote diagnostic: "+window.Quote.Tag+" visible="+window.Quote.Visible+" foreground="+Native.GetForegroundWindow()+" quote="+window.Quote.Handle+" selection="+answer.SelectedText+" focused="+answer.Focused+" anchor="+answer.SelectionAnchor()+" window="+window.Bounds+" timeline="+window.Timeline.AutoScrollPosition);
                Check(window.Quote.Visible && window.SelectedReading(out selectedBounds)=="perpendicular","Rendered words still offer Quote and the F13 reading selection");
                window.Quote.Hide(); window.Cards[0].RefreshContent(); Check(answer.SelectedText=="perpendicular","Refreshing an unchanged answer retains the selected word");
                using(var mini=new QuickDictionary(12)) {
                    mini.Owner=window; mini.Begin("perpendicular",new Rectangle(180,120,130,20));
                    mini.UpdateAnswer(MarkdownSample,true);
                    Check(mini.Body.Text.Contains("How far a line turns") && !mini.Body.Text.Contains("**"),"Temporary dictionary uses the same formatted Markdown renderer");
                    Shot(mini,Path.Combine(folder,"markdown-dictionary.png")); mini.Close();
                }
                entry.Answer="A **perpend"; window.UpdateEntry(entry); entry.Answer="A **perpendicular** line."; window.UpdateEntry(entry);
                Check(answer.Text=="A perpendicular line.","A streamed emphasis marker is resolved when its closing marker arrives");
                StyleAt(answer,"perpendicular",f=>f.Bold,"Streamed emphasis");
                entry.Answer="```python\nx_axis = 3\n```"; window.UpdateEntry(entry);
                Check(answer.Text=="x_axis = 3","A completed streamed code fence becomes just code");
                answer.SetMarkdown("**中文** 后面的 English `x_axis` normal");
                StyleAt(answer,"normal",f=>!f.Bold && f.Name=="Microsoft YaHei UI","Font returns to regular English after Chinese and code");
                answer.SetMarkdown(@"`{\rtf1\b injected}` **safe**");
                Check(answer.Text.Contains(@"{\rtf1\b injected}"),"Literal RTF-looking code cannot inject native formatting");
                Check(Markdown.PlainText("foo_bar x * y \\*literal\\*")=="foo_bar x * y *literal*","Ordinary underscores, multiplication and escaped punctuation remain literal");
                foreach(string formula in new[]{@"\frac{1}{2}",@"\sqrt{x}",@"\sum_{i=1}^{n} i = \frac{n(n+1)}{2}",@"\int_0^1 x^2 \, dx = \frac{1}{3}",@"\begin{pmatrix}1 & 2 \\ 3 & 4\end{pmatrix}"}) Check(MathRender.Picture(formula,14,540,true)!=null,"TeX renders "+formula);
                foreach(bool dark in new[]{true,false}) {
                    Theme.TestDark=dark; Theme.Refresh(); entry.Answer=MathSample; window.UpdateEntry(entry); window.ApplyReading(14,620,0); Theme.Apply(window);
                    window.Timeline.AutoScrollPosition=Point.Empty;
                    Check(answer.Rtf.Contains("\\pict") && !answer.Text.Contains("\\frac") && !answer.Text.Contains("\\sum"),"Native formula images survive "+(dark?"dark":"light")+" mode");
                    Shot(window,Path.Combine(folder,dark?"math-dark.png":"math-light.png"));
                }
                int renderCount=MathRender.RenderedCount; window.Cards[0].RefreshContent(); window.Timeline.ScrollBy(15);
                Check(MathRender.RenderedCount==renderCount,"Unchanged content and scrolling use the formula cache");
                answer.SetMarkdown(@"Unfinished $\frac{1}{ and unsupported $\unrecognizedterm{x}$");
                Check(answer.Text.Contains(@"\frac{1}{") && answer.Text.Contains(@"\unrecognizedterm{x}"),"Incomplete and unsupported formulas remain readable source instead of crashing");
            } catch(Exception e) { exit=1; results.Add(e.ToString()); }
            finally { File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); typeof(MainWindow).GetField("exiting",Private).SetValue(app,true); app.Close(); }
        };
        Application.Run(app); return exit;
    }
    static Rectangle selectedBounds;
}
}

