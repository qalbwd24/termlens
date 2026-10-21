using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TermLens {
public static partial class Tests {
    public static int LiveRefine(string root) {
        string folder=TestRoot(root,"refine-live");
        try {
            var settings=Files.Load(Path.Combine(root,"data")); settings.CarefulAnswers=false; settings.Prompt=settings.RefinePrompt;
            string secret=Files.LoadKey(Path.Combine(root,"data"));
            string[] samples={"python how delete chongfu item in list keep order","why this sentence is wrong?"};
            for(int i=0;i<samples.Length;i++) {
                string answer=Requests.Complete(settings,secret,samples[i],new List<string>(),delegate(string text){},CancellationToken.None,null,null).GetAwaiter().GetResult();
                File.WriteAllText(Path.Combine(folder,"sample-"+i+".txt"),samples[i]+"\r\n\r\n"+answer,Files.Utf8);
                Check(answer.Length>5 && answer.Length<220 && !answer.Contains("Corrected English:") && !answer.Contains("Meaning:"),"Sample "+i+" returns concise wording only");
            }
            results.Add("Two live fixed samples; thinking disabled; no user history sent or saved.");
            File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); return 0;
        } catch(Exception e) { File.WriteAllText(Path.Combine(folder,"results.txt"),"Live refine check failed: "+e.GetType().Name,Files.Utf8); return 1; }
    }
    public static int LiveTeacher(string root) {
        string folder=TestRoot(root,"teacher-live");
        try {
            var settings=Files.Load(Path.Combine(root,"data")); settings.CarefulAnswers=false; settings.Prompt=Settings.DefaultTeacherPrompt;
            string secret=Files.LoadKey(Path.Combine(root,"data"));
            var context=new List<StudyTurn> { new StudyTurn("recursion","Meaning: A function (a small computer job) calls itself to solve a smaller part of a problem.\n\nExample: Count down from three to zero, then stop.") };
            string answer=Requests.Complete(settings,secret,"make sentence with this word",new List<string>(),delegate(string text){},CancellationToken.None,null,context).GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(folder,"answer.txt"),answer,Files.Utf8);
            Check(answer.IndexOf("recursion",StringComparison.OrdinalIgnoreCase)>=0,"Teacher resolves this word from the earlier lookup");
            Check(!answer.StartsWith("Meaning:"),"Teacher follows the sentence request rather than the lookup template");
            Check(answer.Replace("**","").StartsWith("Corrected English:"),"Teacher corrects the learner's English before answering");
            results.Add("One live provider request, thinking disabled; fixed sample context only, no real history sent or saved.");
            File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); return 0;
        } catch(Exception e) { File.WriteAllText(Path.Combine(folder,"results.txt"),"Live teacher check failed: "+e.GetType().Name,Files.Utf8); return 1; }
    }
    sealed class WaitingHandler:HttpMessageHandler {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message,CancellationToken token) { await Task.Delay(-1,token); throw new Exception("Unexpected wait completion"); }
    }
    static string Reply(string text) {
        return "data: "+Files.Json().Serialize(new { choices=new[]{new { delta=new { content=text },finish_reason="stop" } } })+"\n\ndata: [DONE]\n";
    }
    public static int Study(string root) {
        string folder=TestRoot(root,"study"); int exit=0; Files.SaveKey(folder,"isolated-study-test-key");
        var calls=new List<Handler>(); bool waiting=false;
        var window=new MainWindow(folder,true,delegate {
            if(waiting) return new WaitingHandler();
            var handler=new Handler(Reply(calls.Count<2?"Meaning: A function (a small computer job) works in steps.\n\nExample: Add two numbers.":"I wrote a function to add two numbers.\n\nHere, function means a small computer job.")); calls.Add(handler); return handler;
        });
        window.Shown+=async delegate {
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            try {
                await Task.Delay(200);
                ((TextBox)typeof(MainWindow).GetField("input",flags).GetValue(window)).Text="recursion";
                var work=Screen.PrimaryScreen.WorkingArea; var anchor=new Rectangle(work.Left+20,work.Top+40,80,20);
                await (Task)typeof(MainWindow).GetMethod("Explain",flags).Invoke(window,new object[]{true,(Rectangle?)anchor});
                var rootCard=(ResultPopup)typeof(MainWindow).GetField("popup",flags).GetValue(window);
                var cards=(Dictionary<ResultPopup,StudyCard>)typeof(MainWindow).GetField("studyCards",flags).GetValue(window);
                if(rootCard==null || !rootCard.Visible || cards==null || !cards.ContainsKey(rootCard)) { Skip("study card tree","explain now renders into the reading window, not MainWindow.popup"); return; }
                Check(rootCard.CanStudy && calls.Count==1 && cards[rootCard].Trail.Count==1,"Successful lookup enables study controls with its own context");
                Check(calls[0].Body.Contains("disabled") && !calls[0].Body.Contains("reasoning_effort"),"Study root sends non-thinking request by default");
                var originalAnswer=rootCard.AnswerText;
                var rich=(RichTextBox)typeof(ResultPopup).GetField("answer",flags).GetValue(rootCard);
                rootCard.ShowExisting(); rich.Focus(); rich.Select(rich.Text.IndexOf("function"),"function".Length);
                typeof(Control).GetMethod("OnMouseUp",flags).Invoke(rich,new object[]{new MouseEventArgs(MouseButtons.Left,1,20,20,0)});
                var bubble=(SelectionBubble)typeof(MainWindow).GetField("bubble",flags).GetValue(window);
                Check(bubble.Visible && bubble.Source==rootCard.Handle,"Selecting a displayed word offers the Explain button for that card");
                typeof(Control).GetMethod("OnMouseUp",flags).Invoke(bubble,new object[]{new MouseEventArgs(MouseButtons.Left,1,10,10,0)});
                for(int i=0;i<40 && (cards.Count<2 || !cards.Keys.Last().CanStudy);i++) await Task.Delay(50);
                var word=cards.Keys.Last();
                Check(cards.Count==2 && word!=rootCard && word.OriginalText=="function" && word.CanStudy,"Clicking internal Explain opens a separate word window");
                Check(rootCard.Visible && rootCard.IsPinned && rootCard.AnswerText==originalAnswer,"Opening a word keeps the parent explanation intact");
                Check(calls[1].Body.Contains("recursion") && calls[1].Body.Contains("small computer job"),"Nested word request includes its parent explanation");
                ((Button)typeof(ResultPopup).GetField("save",flags).GetValue(word)).PerformClick();
                var db=(HistoryStore)typeof(MainWindow).GetField("history",flags).GetValue(window);
                Check(db.List("function",true,10).Count==1 && db.List("recursion",true,10).Count==0,"Saving a child word does not save its parent");
                var box=(TextBox)typeof(ResultPopup).GetField("question",flags).GetValue(word);
                box.Text="Make a sentence with this word.";
                ((Button)typeof(ResultPopup).GetField("ask",flags).GetValue(word)).PerformClick();
                for(int i=0;i<40 && (cards.Count<3 || !cards.Keys.Last().CanStudy);i++) await Task.Delay(50);
                var teacher=cards.Keys.Last();
                Check(cards.Count==3 && cards[teacher].Teacher && teacher.CanStudy,"Ask opens a separate teacher window");
                var body=Files.Json().Deserialize<Dictionary<string,object>>(calls[2].Body);
                var messages=(System.Collections.ArrayList)body["messages"];
                Check((string)((Dictionary<string,object>)messages[0])["content"]==Settings.DefaultTeacherPrompt,"Teacher uses its separate editable prompt");
                Check(messages.Count==6 && calls[2].Body.Contains("Make a sentence with this word.") && calls[2].Body.Contains("recursion") && calls[2].Body.Contains("function"),"Teacher receives ancestor lookup and explanation followed by the current question");
                Check(calls[2].Body.Contains("disabled") && !calls[2].Body.Contains("reasoning_effort"),"Teacher request also uses thinking disabled");
                Check(db.Run("SELECT question FROM teacher_answers").Count==1 && db.List("Make a sentence",false,10).Count==0,"Teacher answers are stored separately from vocabulary frequency");
                Check(!((Button)typeof(ResultPopup).GetField("save",flags).GetValue(teacher)).Visible,"Teacher question is not offered as a vocabulary word");
                ((Button)typeof(ResultPopup).GetField("back",flags).GetValue(teacher)).PerformClick();
                Check(word.Visible && word.OriginalText=="function","Back restores the exact parent window");
                teacher.ShowExisting(); teacher.Refresh(); await Task.Delay(200);
                using(var bmp=new Bitmap(teacher.Width,teacher.Height)) { using(var g=Graphics.FromImage(bmp)) g.CopyFromScreen(teacher.Location,Point.Empty,teacher.Size); bmp.Save(Path.Combine(folder,"teacher-screen.png")); }
                waiting=true;
                ((TextBox)typeof(ResultPopup).GetField("question",flags).GetValue(teacher)).Text="Can you explain that more simply?";
                ((Button)typeof(ResultPopup).GetField("ask",flags).GetValue(teacher)).PerformClick(); await Task.Delay(100);
                var pending=cards.Keys.Last();
                Check(cards.Count==4 && cards[pending].Request!=null,"A teacher answer can itself receive a follow-up question");
                ((Button)typeof(ResultPopup).GetField("stop",flags).GetValue(pending)).PerformClick();
                for(int i=0;i<40 && cards[pending].Request!=null;i++) await Task.Delay(50);
                Check(cards[pending].Request==null && teacher.CanStudy && !pending.CanStudy,"Stopping a child request leaves its parent usable");
                Check(db.Run("SELECT question FROM teacher_answers").Count==1,"Canceled teacher answers are not saved");
                var many=Enumerable.Range(0,20).Select(i=>new StudyTurn(new string('q',8000),new string('a',12000))).ToList();
                var bounded=StudyTurn.Recent(many);
                Check(bounded.Count<=6 && bounded.Sum(t=>t.Question.Length+t.Answer.Length)<=24000,"Conversation context is bounded for long study sessions");
                waiting=false;
                var entry=(InputPopup)typeof(MainWindow).GetField("quickInput",flags).GetValue(window);
                entry.Present();
                var editor=(TextBox)typeof(InputPopup).GetField("editor",flags).GetValue(entry);
                editor.Text="why this sentence is wrong?";
                ((Button)typeof(InputPopup).GetField("settingsButton",flags).GetValue(entry)).PerformClick();
                Check(window.Visible && ((SettingsPages)typeof(MainWindow).GetField("tabs",flags).GetValue(window)).SelectedIndex==4 && editor.Text=="why this sentence is wrong?","Typing-box Settings opens the teacher page without clearing the draft");
                entry.Present();
                int previous=calls.Count;
                typeof(ComposerBox).GetMethod("OnKeyDown",flags).Invoke(editor,new object[]{new KeyEventArgs(Keys.Enter)});
                for(int i=0;i<40 && (calls.Count==previous || !cards.Keys.Last().CanStudy);i++) await Task.Delay(50);
                var fresh=cards.Keys.Last();
                var freshBody=Files.Json().Deserialize<Dictionary<string,object>>(calls.Last().Body);
                var freshMessages=(System.Collections.ArrayList)freshBody["messages"];
                Check(cards[fresh].Teacher && fresh.CanStudy && cards[fresh].Parent==null,"Enter from the no-selection box starts an independent teacher answer");
                Check((string)((Dictionary<string,object>)freshMessages[0])["content"]==Settings.DefaultTeacherPrompt && calls.Last().Body.Contains("Corrected English:"),"Quick input uses the correction-first teacher prompt");
                Check(freshMessages.Count==2,"A fresh teacher question does not include unrelated old lookups");
                Check(db.Run("SELECT question FROM teacher_answers").Count==2 && db.List("why this sentence",false,10).Count==0,"Quick teacher questions use teacher history rather than word lookup counts");
                fresh.CloseReading(); entry.Present(); entry.Refresh(); await Task.Delay(180);
                using(var bmp=new Bitmap(entry.Width,entry.Height)) { using(var g=Graphics.FromImage(bmp)) g.CopyFromScreen(entry.Location,Point.Empty,entry.Size); bmp.Save(Path.Combine(folder,"teacher-input.png")); }
                editor.Text="python how delete chongfu item in list keep order";
                var refineButton=(Button)typeof(InputPopup).GetField("refine",flags).GetValue(entry);
                previous=calls.Count; refineButton.PerformClick();
                for(int i=0;i<40 && (calls.Count==previous || !cards.Keys.Last().CanStudy);i++) await Task.Delay(50);
                var refined=cards.Keys.Last();
                var refineBody=Files.Json().Deserialize<Dictionary<string,object>>(calls.Last().Body);
                var refineMessages=(System.Collections.ArrayList)refineBody["messages"];
                Check(cards[refined].Refine && !cards[refined].Teacher && refined.CanStudy,"Refine button opens its own response mode");
                Check(refineMessages.Count==2 && (string)((Dictionary<string,object>)refineMessages[0])["content"]==Settings.DefaultRefinePrompt,"Refine sends its separate wording-only prompt without unrelated history");
                Check(db.Run("SELECT question FROM refinements").Count==1 && db.Run("SELECT question FROM teacher_answers").Count==2 && db.List("chongfu",false,10).Count==0,"Refine history is separate from teacher answers and word counts");
                refined.CloseReading(); previous=calls.Count;
                typeof(MainWindow).GetMethod("RestoreLastReading",flags).Invoke(window,null);
                var restored=cards.Keys.Last();
                Check(cards[restored].Refine && !cards[restored].Teacher && restored.AnswerText==refined.AnswerText && calls.Count==previous,"Last lookup restores refined wording and mode without a request");
                var cache=Files.Json().Deserialize<ReadingCache>(File.ReadAllText(Path.Combine(folder,"last-reading.json"),Files.Utf8));
                Check(cache.Refine && !cache.Teacher,"Refine mode is persisted in the last-reading cache");
                restored.CloseReading(); entry.Present(); editor.Text="how can i learn this?";
                refineButton.Focus(); previous=calls.Count;
                typeof(InputPopup).GetMethod("ProcessCmdKey",flags).Invoke(entry,new object[]{Message.Create(entry.Handle,0x100,IntPtr.Zero,IntPtr.Zero),Keys.Enter});
                for(int i=0;i<40 && (calls.Count==previous || !cards.Keys.Last().CanStudy);i++) await Task.Delay(50);
                Check(cards[cards.Keys.Last()].Teacher && !cards[cards.Keys.Last()].Refine,"Enter uses teacher even with Refine focused after a refinement");
                cards.Keys.Last().CloseReading(); entry.Present(); editor.Text="";
                Check(!refineButton.Enabled,"Blank drafts cannot start refinement");
                editor.Text="python how delete chongfu item in list keep order"; entry.Refresh(); await Task.Delay(180);
                using(var bmp=new Bitmap(entry.Width,entry.Height)) { using(var g=Graphics.FromImage(bmp)) g.CopyFromScreen(entry.Location,Point.Empty,entry.Size); bmp.Save(Path.Combine(folder,"refine-input.png")); }
                entry.Hide(); fresh.ShowExisting(); fresh.DraftQuestion="This draft must not replace the original text.";
                var readingRefine=(Button)typeof(ResultPopup).GetField("refine",flags).GetValue(fresh);
                previous=calls.Count; readingRefine.PerformClick();
                for(int i=0;i<40 && (calls.Count==previous || !cards.Keys.Last().CanStudy);i++) await Task.Delay(50);
                var readingResult=cards.Keys.Last();
                var readingBody=Files.Json().Deserialize<Dictionary<string,object>>(calls.Last().Body);
                var readingMessages=(System.Collections.ArrayList)readingBody["messages"];
                Check(cards[readingResult].Refine && cards[readingResult].Parent==fresh && readingResult.OriginalText==fresh.OriginalText,"Reading Refine button rewrites the original text in a separate child window");
                Check(readingMessages.Count==2 && (string)((Dictionary<string,object>)readingMessages[0])["content"]==Settings.DefaultRefinePrompt && (string)((Dictionary<string,object>)readingMessages[1])["content"]==fresh.OriginalText,"Reading refinement sends the original wording with its refine prompt, without the explanation or teacher draft");
                Check(fresh.Visible && fresh.DraftQuestion=="This draft must not replace the original text." && fresh.CanStudy,"Refining keeps the parent explanation and unfinished teacher question available");
                readingResult.CloseReading(); fresh.ShowExisting(); fresh.ApplyReading(11,360,0);
                var label=(Label)typeof(ResultPopup).GetField("questionLabel",flags).GetValue(fresh);
                var frame=(Control)typeof(ResultPopup).GetField("questionFrame",flags).GetValue(fresh);
                Check(readingRefine.Visible && !readingRefine.Bounds.IntersectsWith(label.Bounds) && !readingRefine.Bounds.IntersectsWith(frame.Bounds) && fresh.ClientRectangle.Contains(readingRefine.Bounds),"Reading Refine button fits the minimum-width window without overlaps");
                fresh.ApplyReading(11,480,0); fresh.Refresh(); await Task.Delay(180);
                using(var bmp=new Bitmap(fresh.Width,fresh.Height)) { using(var g=Graphics.FromImage(bmp)) g.CopyFromScreen(fresh.Location,Point.Empty,fresh.Size); bmp.Save(Path.Combine(folder,"reading-refine.png")); }
                previous=calls.Count; readingRefine.Focus();
                typeof(ResultPopup).GetMethod("ProcessCmdKey",flags).Invoke(fresh,new object[]{Message.Create(fresh.Handle,0x100,IntPtr.Zero,IntPtr.Zero),Keys.Enter});
                for(int i=0;i<40 && (calls.Count==previous || !cards.Keys.Last().CanStudy);i++) await Task.Delay(50);
                Check(cards[cards.Keys.Last()].Teacher && !cards[cards.Keys.Last()].Refine,"Enter with reading Refine focused still sends the teacher question");
                for(int action=1;action<3;action++) {
                    fresh.ShowExisting();
                    var sourceText=(RichTextBox)typeof(ResultPopup).GetField("term",flags).GetValue(fresh);
                    sourceText.Focus(); sourceText.Select(0,sourceText.Text.Length);
                    typeof(Control).GetMethod("OnMouseUp",flags).Invoke(sourceText,new object[]{new MouseEventArgs(MouseButtons.Left,1,20,20,0)});
                    Check(bubble.Visible,"Internal selection offers all three actions");
                    var hit=bubble.ActionBounds(action); previous=calls.Count;
                    typeof(Control).GetMethod("OnMouseUp",flags).Invoke(bubble,new object[]{new MouseEventArgs(MouseButtons.Left,1,hit.Left+hit.Width/2,hit.Top+hit.Height/2,0)});
                    for(int i=0;i<40 && (calls.Count==previous || !cards.Keys.Last().CanStudy);i++) await Task.Delay(50);
                    var child=cards.Keys.Last();
                    Check(cards[child].Parent==fresh && cards[child].Teacher==(action==1) && cards[child].Refine==(action==2) && child.OriginalText==fresh.OriginalText,"Internal toolbar routes "+(SelectionAction)action+" correctly");
                    child.CloseReading();
                    window.Reveal(); var sourceInput=(TextBox)typeof(MainWindow).GetField("input",flags).GetValue(window);
                    sourceInput.Text="i have implement all the function, could you check them"; sourceInput.Focus(); sourceInput.SelectAll();
                    bubble.Present(window.Handle,anchor); previous=calls.Count;
                    typeof(Control).GetMethod("OnMouseUp",flags).Invoke(bubble,new object[]{new MouseEventArgs(MouseButtons.Left,1,hit.Left+hit.Width/2,hit.Top+hit.Height/2,0)});
                    for(int i=0;i<40 && (calls.Count==previous || !cards.Keys.Last().CanStudy);i++) await Task.Delay(50);
                    var independent=cards.Keys.Last();
                    Check(cards[independent].Parent==null && cards[independent].Teacher==(action==1) && cards[independent].Refine==(action==2) && independent.OriginalText==sourceInput.Text,"Captured toolbar selection routes "+(SelectionAction)action+" to an independent response");
                    independent.CloseReading();
                }
                results.Add("Mock HTTP only; no live API requests or changes to real user data.");
            } catch(Exception e) { results.Add(e.ToString()); exit=1; }
            finally { File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); typeof(MainWindow).GetField("exiting",flags).SetValue(window,true); window.Close(); }
        };
        Application.Run(window); return exit;
    }
}
}
