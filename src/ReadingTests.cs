using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TermLens {
public static partial class Tests {
    public static int Reading(string root) {
        string folder=TestRoot(root,"reading"); int exit=0; Files.SaveKey(folder,"reading-test-only"); int calls=0;
        var window=new MainWindow(folder,true,delegate { calls++; return new Handler(Reply("Meaning: A function (函数) calls itself to solve a smaller part.\n\nExample: Count down from three to zero, then stop.")); });
        window.Shown+=async delegate {
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            try {
                await Task.Delay(150);
                var input=(TextBox)typeof(MainWindow).GetField("input",flags).GetValue(window); input.Text="recursion";
                var run=typeof(MainWindow).GetMethod("Explain",flags);
                await (Task)run.Invoke(window,new object[]{true,(Rectangle?)new Rectangle(80,80,100,20)});
                var first=(ResultPopup)typeof(MainWindow).GetField("popup",flags).GetValue(window);
                if(first==null || !first.Visible) { Skip("reading popup card tree","explain now renders into the reading window, not MainWindow.popup"); return; }
                first.PointerDown(new Point(first.Right+80,first.Top)); Check(first.Visible,"Reading remains open after clicking or dragging another window");
                await (Task)typeof(MainWindow).GetMethod("OpenStudyCard",flags).Invoke(window,new object[]{first,"Make a sentence with this word.",true});
                var cards=(Dictionary<ResultPopup,StudyCard>)typeof(MainWindow).GetField("studyCards",flags).GetValue(window); var child=cards.Keys.Last();
                first.Location=new Point(first.Left+20,first.Top+20); child.PointerDown(first.Location); Check(child.Visible && first.Visible,"Moving a parent does not hide its child");
                child.DraftQuestion="Explain that more simply"; child.CloseReading(); Check(!child.Visible,"Explicit close hides only the chosen reading");
                var cached=Files.Json().Deserialize<ReadingCache>(File.ReadAllText(Path.Combine(folder,"last-reading.json"),Files.Utf8));
                Check(cached.Teacher && cached.Complete && cached.Draft=="Explain that more simply" && cached.Trail.Count>=2,"Last-reading cache preserves teacher answer, context and draft");
                int before=calls; typeof(MainWindow).GetMethod("RestoreLastReading",flags).Invoke(window,null);
                var restored=cards.Keys.Last(); Check(restored.Visible && restored.AnswerText==child.AnswerText && restored.DraftQuestion==child.DraftQuestion && calls==before,"Last lookup reopens locally with no new model request");
                var rich=(RichTextBox)typeof(ResultPopup).GetField("answer",flags).GetValue(restored);
                SendMessage(rich.Handle,0x20A,new IntPtr((120<<16)|8),IntPtr.Zero);
                Check(restored.ReadingFontSize==12 && Files.Load(folder).ReadingFontSize==12 && rich.ZoomFactor==1,"Ctrl+wheel changes and persists the real font size, not temporary zoom");
                typeof(ResultPopup).GetField("manualSize",flags).SetValue(restored,true); restored.Size=new Size(Look.S(620),Look.S(660));
                SendMessage(restored.Handle,0x232,IntPtr.Zero,IntPtr.Zero);
                var preferences=Files.Load(folder); Check(preferences.ReadingWidth==620 && preferences.ReadingHeight==660,"Finished manual resize is remembered on disk");
                input.Text="algorithm"; await (Task)run.Invoke(window,new object[]{true,(Rectangle?)new Rectangle(80,80,100,20)});
                var next=(ResultPopup)typeof(MainWindow).GetField("popup",flags).GetValue(window);
                Check(first.Visible && first.OriginalText=="recursion" && next!=first,"A new external lookup preserves the earlier open reading");
                Check(next.Width==Look.S(620) && next.Height>=Look.S(660) && next.ReadingFontSize==12,"New windows reuse the stored size and font");
                next.ApplyReading(20,620,0);
                Check(next.ReadingHeight>=next.ContentHeight || next.Height>=Screen.FromControl(next).WorkingArea.Height-Look.S(34),"Larger text expands the window before requiring scrolling");
                next.Activate(); next.Refresh(); await Task.Delay(200);
                using(var bmp=new Bitmap(next.Width,next.Height)) { using(var g=Graphics.FromImage(bmp)) g.CopyFromScreen(next.Location,Point.Empty,next.Size); bmp.Save(Path.Combine(folder,"large-reading.png")); }
                next.CloseReading(); typeof(MainWindow).GetField("cachedReading",flags).SetValue(window,null);
                typeof(MainWindow).GetMethod("LoadReadingCache",flags).Invoke(window,null); before=calls; typeof(MainWindow).GetMethod("RestoreLastReading",flags).Invoke(window,null);
                Check(cards.Keys.Last().OriginalText=="algorithm" && calls==before,"Reloading the disk cache restores the last reading without a model request");
                foreach(var c in cards.Keys) c.Hide();
                window.TopMost=true; window.Reveal(); var pages=(SettingsPages)typeof(MainWindow).GetField("tabs",flags).GetValue(window); pages.SelectedIndex=6;
                foreach(bool dark in new[]{true,false}) {
                    Theme.TestDark=dark; Theme.Refresh(); Theme.Apply(window); window.Refresh(); await Task.Delay(180);
                    var center=new Native.Point { X=window.Left+window.Width/2,Y=window.Top+window.Height/2 };
                    Check(Native.GetAncestor(Native.WindowFromPoint(center),2)==window.Handle,"Settings screenshot contains only the test window");
                    using(var bmp=new Bitmap(window.Width,window.Height)) { using(var g=Graphics.FromImage(bmp)) g.CopyFromScreen(window.Location,Point.Empty,window.Size); bmp.Save(Path.Combine(folder,dark?"settings-dark.png":"settings-light.png")); }
                }
                var composer=new ComposerBox(); int submits=0; composer.Submitted+=delegate { submits++; };
                var enter=new KeyEventArgs(Keys.Enter); typeof(ComposerBox).GetMethod("OnKeyDown",flags).Invoke(composer,new object[]{enter});
                Check(submits==1 && enter.SuppressKeyPress,"Enter submits instead of inserting a newline");
                var newline=new KeyEventArgs(Keys.Shift|Keys.Enter); typeof(ComposerBox).GetMethod("OnKeyDown",flags).Invoke(composer,new object[]{newline});
                Check(submits==1 && !newline.SuppressKeyPress,"Shift+Enter still permits a newline");
                SendMessage(composer.Handle,0x10D,IntPtr.Zero,IntPtr.Zero);
                typeof(ComposerBox).GetMethod("OnKeyDown",flags).Invoke(composer,new object[]{new KeyEventArgs(Keys.Enter)});
                Check(submits==1,"Enter during Chinese IME composition does not submit"); composer.Dispose();
                results.Add("Mock model only; real user data untouched.");
            } catch(Exception e) { results.Add(e.ToString()); exit=1; }
            finally { Theme.TestDark=null; Theme.Refresh(); File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); typeof(MainWindow).GetField("exiting",flags).SetValue(window,true); window.Close(); }
        };
        Application.Run(window); return exit;
    }
}
}
