using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TermLens {
public static partial class Tests {
    static readonly List<string> results=new List<string>();
    static void Check(bool condition,string label) { if(!condition) throw new Exception("FAIL: "+label); results.Add("PASS: "+label); }
    static void Skip(string label,string reason) { results.Add("SKIPPED: "+label+" - "+reason); }
    static string TestRoot(string root,string kind) { string path=Path.Combine(root,"tests",kind+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")); Directory.CreateDirectory(path); return path; }
    public static int LivePrompt(string root) {
        string folder=TestRoot(root,"prompt-live");
        try {
            var settings=Files.Load(Path.Combine(root,"data")); settings.Prompt=Settings.DefaultPrompt;
            string secret=Files.LoadKey(Path.Combine(root,"data"));
            foreach(string term in new[]{"recursion","derivative"}) {
                string response=Requests.Complete(settings,secret,term,new List<string>(),delegate(string partial){},CancellationToken.None,null).GetAwaiter().GetResult();
                File.WriteAllText(Path.Combine(folder,term+".txt"),response,Files.Utf8);
                Check(response.Contains("Meaning:") && response.Contains("Example:"),term+" uses two clear points");
                Check(Regex.Matches(response,@"\b[A-Za-z]+(?:'[A-Za-z]+)?\b").Count<=90,term+" stays within 90 English words");
            }
            results.Add("Two live API requests used the configured provider and model. No lookup history was sent or added.");
            File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); return 0;
        } catch(Exception e) { results.Add("Live test failed: "+e.GetType().Name+" (check connection settings)"); File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); return 1; }
    }
    sealed class Handler : HttpMessageHandler {
        public string Body;
        public string Reply;
        public HttpStatusCode Code=HttpStatusCode.OK;
        public Handler(string reply) { Reply=reply; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) {
            Check(request.RequestUri.AbsolutePath=="/chat/completions","API path");
            Check(request.Headers.Authorization.Scheme=="Bearer","Bearer authentication");
            Body=await request.Content.ReadAsStringAsync();
            return new HttpResponseMessage(Code) { Content=new StringContent(Reply,Encoding.UTF8,"text/event-stream") };
        }
    }
    static async Task Network() {
        string sse="data: {\"choices\":[{\"delta\":{\"content\":\"Recursion 递归\"},\"finish_reason\":null}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\" means solving a problem using smaller versions of itself.\"},\"finish_reason\":null}]}\n\ndata: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n";
        var h=new Handler(sse); string progress="";
        var settings=new Settings { CarefulAnswers=false };
        string reply=await Requests.Complete(settings,"test-key","recursion",new List<string>{"stack"},s=>progress=s,CancellationToken.None,h);
        Check(reply.Contains("递归") && reply==progress,"UTF-8 streaming and incremental display");
        var body=Files.Json().Deserialize<Dictionary<string,object>>(h.Body);
        Check(h.Body.Contains("stack") && h.Body.Contains("recursion") && h.Body.Contains("disabled"),"Current term, recent history, non-thinking mode");
        Check(!h.Body.Contains("test-key"),"API key absent from JSON body");
        Check(Requests.SafeError("bad test-key","test-key")=="bad [hidden]","Secret redaction");
        Check(!new Settings().CarefulAnswers && !Requests.Body(new Settings(),"word",new List<string>()).ContainsKey("reasoning_effort"),"Thinking is off by default");
        var carefulBody=Requests.Body(new Settings { CarefulAnswers=true },"word",new List<string>()); Check(carefulBody.ContainsKey("reasoning_effort") && (string)carefulBody["reasoning_effort"]=="low","Careful mode enables a brief reasoning pass when explicitly selected");
        try { await Requests.Complete(settings,"test-key","word",new List<string>(),s=>{},CancellationToken.None,new Handler(sse.Replace("data: [DONE]\n",""))); throw new Exception("truncated accepted"); }
        catch(Exception e) { Check(e.Message.Contains("before the answer completed"),"Interrupted stream not accepted as success"); }
        try { await Requests.Complete(settings,"test-key","word",new List<string>(),s=>{},CancellationToken.None,new Handler(sse.Replace("\"stop\"","\"length\""))); throw new Exception("length accepted"); }
        catch(Exception e) { Check(e.Message.Contains("output limit"),"Output truncation not saved as success"); }
        try { await Requests.Complete(settings,"test-key","word",new List<string>(),s=>{},CancellationToken.None,new Handler(""){Code=HttpStatusCode.Unauthorized}); throw new Exception("401 accepted"); }
        catch(Exception e) { Check(e.Message.Contains("401") && !e.Message.Contains("test-key"),"Authentication failure produces useful safe error"); }
    }
    public static int Run(string root) {
        string folder=TestRoot(root,"core");
        try {
            var settings=new Settings(); Files.Save(folder,settings); var loaded=Files.Load(folder);
            Check(loaded.Prompt.Contains("Chinese university") && loaded.Model=="deepseek-flash","Settings round trip");
            Files.SaveKey(folder,"unit-test-secret-中文"); Check(Files.LoadKey(folder)=="unit-test-secret-中文","Windows DPAPI round trip");
            Check(!File.ReadAllText(Path.Combine(folder,"api-key.dpapi")).Contains("unit-test-secret"),"Secret not stored as plaintext");
            Check(!File.ReadAllText(Path.Combine(folder,"settings.json")).Contains("unit-test-secret"),"Settings do not contain secret");
            Check(settings.Endpoint().AbsoluteUri=="https://api.deepseek.com/chat/completions","DeepSeek endpoint assembly");
            settings.BaseUrl="https://api.deepseek.com/v1/chat/completions"; Check(settings.Endpoint().AbsolutePath=="/v1/chat/completions","Full endpoint not duplicated");
            settings.BaseUrl="http://example.com"; bool rejected=false; try{settings.Validate();}catch{rejected=true;} Check(rejected,"Non-local insecure HTTP rejected"); settings.BaseUrl="https://api.deepseek.com";
            using(var db=new HistoryStore(Path.Combine(folder,"history.sqlite3"))) {
                db.Add("stack","栈","test","prompt","[]");
                db.Add("  Stack  ","A stack","test","prompt","[]");
                db.Add("recursion","递归","test","prompt","[]");
                db.Add("a very long selected sentence\nthat should not be added to the context","answer","test","prompt","[]");
                db.Add("x'; DROP TABLE terms; --","safe","test","prompt","[]");
                var matches=db.List("stack",false,50); Check(matches.Count==2 && matches[0].Count==2,"Lookup frequency merges case and surrounding space");
                Check(db.List("",true,50).Count==0,"Words are not saved automatically"); db.SaveWord("stack",true);
                Check(db.List("",true,50).Count==2,"Manual save persists across word variants");
                var recent=db.Recent(settings,"recursion"); Check(!recent.Contains("recursion") && !recent.Any(x=>x.Contains("\n")),"Context excludes current term and long passages");
                Check(recent.Count(x=>x.Trim().ToLowerInvariant()=="stack")==1,"History context contains distinct terms");
                settings.IncludeHistory=false; Check(db.Recent(settings,"term").Count==0,"History context can be disabled"); settings.IncludeHistory=true;
                Check(db.List("DROP TABLE",false,50).Count==1,"SQL bound parameters protect unusual terms");
                db.Run("UPDATE terms SET last_seen='2000-01-01T00:00:00.0000000Z' WHERE term_key=?","stack");
                Check(!db.Recent(settings,"word").Any(x=>x.Trim().ToLowerInvariant()=="stack"),"Expired context excluded");
                db.SaveWord("stack",false); Check(db.List("",true,50).Count==0,"Unsave preserves history");
            }
            using(var db=new HistoryStore(Path.Combine(folder,"history.sqlite3"))) Check(db.List("stack",false,50).Count==2,"Database survives reopening");
            Check(Requests.UserText("current",new List<string>())=="current","History off sends only selected text");
            var work=new Rectangle(0,0,1920,1080); var selected=new Rectangle(350,400,500,65); var buttonSize=new Size(120,40);
            var below=Look.Below(buttonSize,selected,work,12,false);
            Check(below.HasValue && below.Value.Y==selected.Bottom+12 && below.Value.X==selected.Left,"Button stays below the full selection");
            selected=new Rectangle(350,1020,500,45); var edge=Look.Below(buttonSize,selected,work,12,false);
            Check(edge.HasValue && !new Rectangle(edge.Value,buttonSize).IntersectsWith(selected),"Screen-edge placement does not cover selected text");
            Check(!Look.Below(buttonSize,work,work,12,false).HasValue,"No safe space suppresses button instead of obscuring selection");
            var reverse=Selection.ApproximateBounds(new Point(500,500),new Point(100,100));
            Check(reverse.Bottom>500,"Backward multiline drag anchors below its lower end");
            Check(Settings.DefaultPrompt.Contains("angle of 90") && Settings.DefaultPrompt.Contains("For a sentence") && Settings.DefaultPrompt.Contains("For code"),"Dictionary prompt adapts to words, sentences and code");
            Network().GetAwaiter().GetResult();
            results.Add("No real API requests were made."); File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); return 0;
        } catch(Exception e) { results.Add(e.ToString()); File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); return 1; }
    }
    public static int Ui(string root) {
        string folder=TestRoot(root,"ui"); int exit=0; var window=new MainWindow(folder);
        window.Shown+=async delegate {
            try {
                await Task.Delay(600);
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                var input=(TextBox)typeof(MainWindow).GetField("input",flags).GetValue(window);
                input.Text="中文输入测试：递归 recursion，线性代数 linear algebra。";
                Check((input.ImeMode==ImeMode.Off || input.ImeMode==ImeMode.Close) && input.Text.Contains("递归"),"Main composer starts in English IME mode while accepting Unicode text");
                var tabs=(SettingsPages)typeof(MainWindow).GetField("tabs",flags).GetValue(window);
                for(int i=0;i<tabs.TabPages.Count;i++) { tabs.SelectedIndex=i; await Task.Delay(150); using(var bmp=new Bitmap(window.Width,window.Height)) { window.DrawToBitmap(bmp,new Rectangle(0,0,bmp.Width,bmp.Height)); bmp.Save(Path.Combine(folder,"tab-"+i+".png")); } }
                var voice=(ComboBox)typeof(MainWindow).GetField("speechVoice",flags).GetValue(window);
                Check(voice.SelectedIndex>=0 && voice.Text=="en-US-AvaMultilingualNeural","Voice settings show the configured default voice");
                var opacity=(NumericUpDown)typeof(MainWindow).GetField("scrollSpeed",flags).GetValue(window);
                Check(opacity.Minimum==10 && opacity.Maximum==150,"Scroll speed uses a 10-150 slider");
                tabs.SelectedIndex=5; window.TopMost=true; window.Reveal(); window.Refresh(); await Task.Delay(250);
                using(var bmp=new Bitmap(window.Width,window.Height)) { using(var g=Graphics.FromImage(bmp)) g.CopyFromScreen(window.Location,Point.Empty,window.Size); bmp.Save(Path.Combine(folder,"voice-screen.png")); }
                results.Add("Rendered all native UI tabs without a live API key.");
            } catch(Exception e) { results.Add(e.ToString()); exit=1; }
            finally {
                File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8);
                typeof(MainWindow).GetField("exiting",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(window,true); window.Close();
            }
        };
        Application.Run(window); return exit;
    }
    public static int Hotkeys(string root) {
        string folder=TestRoot(root,"hotkey"); int exit=0;
        Native.Point oldCursor; GetCursorPos(out oldCursor); Native.Point? testCursor=null;
        var window=new MainWindow(folder);
        window.Shown+=async delegate {
            try {
                await Task.Delay(250);
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                var registered=(List<int>)typeof(MainWindow).GetField("registered",flags).GetValue(window);
                Check(registered.Contains(4),"F13 registered globally without Ctrl or Alt");
                Check(registered.Contains(1) && registered.Contains(2) && registered.Contains(3),"Existing three shortcuts remain registered");
                ((SettingsPages)typeof(MainWindow).GetField("tabs",flags).GetValue(window)).SelectedIndex=0;
                var input=(TextBox)typeof(MainWindow).GetField("input",flags).GetValue(window);
                input.Text="recursion"; ShowWindow(window.Handle,9); window.TopMost=true; window.Reveal(); input.Focus(); input.SelectAll();
                await Task.Delay(150);
                if(Native.GetForegroundWindow()!=window.Handle) {
                    var point=input.PointToScreen(new Point(20,20)); var nativePoint=new Native.Point { X=point.X,Y=point.Y };
                    results.Add("Focus diagnostic: window="+window.Bounds+" input="+input.Visible+" point="+point+" hit="+Native.GetAncestor(Native.WindowFromPoint(nativePoint),2)+" expected="+window.Handle+" work="+Screen.FromControl(window).WorkingArea);
                    Check(Native.GetAncestor(Native.WindowFromPoint(nativePoint),2)==window.Handle,"Only activate the visible test window");
                    SetCursorPos(point.X,point.Y); mouse_event(2,0,0,0,UIntPtr.Zero); await Task.Delay(60); mouse_event(4,0,0,0,UIntPtr.Zero); testCursor=nativePoint;
                    input.Focus(); input.SelectAll(); await Task.Delay(100);
                }
                Check(Native.GetForegroundWindow()==window.Handle && !Native.ModifiersDown(),"Controlled input is focused with no modifiers held before F13 test");
                var popup=(ResultPopup)typeof(MainWindow).GetField("popup",flags).GetValue(window);
                Native.keybd_event((byte)Keys.F13,0,0,UIntPtr.Zero);
                for(int i=0;i<40 && !popup.Visible;i++) await Task.Delay(50);
                Check(popup.Visible && !window.Visible,"Real F13 key event opens the selected-text reading window");
                Native.keybd_event((byte)Keys.F13,0,0,UIntPtr.Zero); await Task.Delay(100);
                Native.keybd_event((byte)Keys.F13,0,2,UIntPtr.Zero);
                Native.Point cursorNow; GetCursorPos(out cursorNow); if(testCursor.HasValue && cursorNow.X==testCursor.Value.X && cursorNow.Y==testCursor.Value.Y) SetCursorPos(oldCursor.X,oldCursor.Y);
                string log=File.ReadAllText(Path.Combine(folder,"ui-events.log"),Files.Utf8);
                Check(log.Split(new[] {"capture requested"},StringSplitOptions.None).Length-1==1,"Holding F13 does not trigger repeated lookups");
                Check(log.Contains("capture ready"),"F13 reaches the selection capture path");
                popup.Hide(); window.Reveal(); input.Focus(); input.Select(0,0); await Task.Delay(100);
                Native.keybd_event((byte)Keys.F13,0,0,UIntPtr.Zero); Native.keybd_event((byte)Keys.F13,0,2,UIntPtr.Zero);
                var entry=(InputPopup)typeof(MainWindow).GetField("quickInput",flags).GetValue(window);
                for(int i=0;i<40 && !entry.Visible;i++) await Task.Delay(50);
                Check(entry.Visible && !popup.Visible,"F13 with no selection opens a small input box without a request");
                var editor=(TextBox)typeof(InputPopup).GetField("editor",flags).GetValue(entry);
                Check(editor.ContainsFocus && editor.ImeMode==ImeMode.On,"Quick input receives focus and supports Chinese input");
                editor.Text="derivative 导数";
                entry.Refresh(); await Task.Delay(200);
                using(var bmp=new Bitmap(entry.Width,entry.Height)) { using(var g=Graphics.FromImage(bmp)) g.CopyFromScreen(entry.Location,Point.Empty,entry.Size); bmp.Save(Path.Combine(folder,"quick-input.png")); }
                SendMessage(editor.Handle,0x100,new IntPtr((int)Keys.Enter),IntPtr.Zero); await Task.Delay(100);
                var submittedPopup=(ResultPopup)typeof(MainWindow).GetField("latestCard",flags).GetValue(window);
                var states=(Dictionary<ResultPopup,StudyCard>)typeof(MainWindow).GetField("studyCards",flags).GetValue(window);
                Check(!entry.Visible && submittedPopup.Visible && submittedPopup.OriginalText=="derivative 导数" && states[submittedPopup].Teacher,"Enter in quick input opens a teacher card");
                results.Add("Isolated test settings have no API key; no network requests or user history access.");
            } catch(Exception e) { results.Add(e.ToString()); exit=1; }
            finally {
                Native.keybd_event((byte)Keys.F13,0,2,UIntPtr.Zero);
                File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8);
                typeof(MainWindow).GetField("exiting",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(window,true); window.Close();
            }
        };
        Application.Run(window); return exit;
    }
    public static int Popup(string root) {
        string folder=TestRoot(root,"popup"); int exit=0;
        var window=new MainWindow(folder,true);
        window.Shown+=async delegate {
            try {
                await Task.Delay(400);
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                var input=(TextBox)typeof(MainWindow).GetField("input",flags).GetValue(window); input.Text="recursion";
                var area=Screen.PrimaryScreen.WorkingArea; var anchor=new Rectangle(area.Left+Look.S(80),area.Top+Look.S(70),Look.S(190),Look.S(22));
                var liveBubble=(SelectionBubble)typeof(MainWindow).GetField("bubble",flags).GetValue(window);
                if(liveBubble==null) { Skip("floating selection button","the selection bubble is not created; the floating button is disabled by design (Settings.FloatingButton)"); return; }
                liveBubble.Present(window.Handle,anchor);
                typeof(Control).GetMethod("OnMouseUp",flags).Invoke(liveBubble,new object[]{new MouseEventArgs(MouseButtons.Left,1,10,10,0)});
                var popup=(ResultPopup)typeof(MainWindow).GetField("popup",flags).GetValue(window);
                for(int i=0;i<30 && !popup.Visible;i++) await Task.Delay(50);
                Check(!liveBubble.Visible && popup.Visible,"Floating button click opens the reading window after tray startup");
                Check(!window.Visible && popup.Visible,"Selection lookup opens only compact popup, not the main window");
                Check(popup.Width==Look.S(480) && area.Contains(popup.Bounds),"Initial reading window is compact and on screen");
                popup.BeginLookup("recursion",anchor);
                popup.PointerDown(new Point(popup.Right+20,popup.Bottom+20));
                Check(popup.Visible,"Clicking the source while loading keeps the reading window visible");
                popup.SetAnswer("Meaning: A function (a small set of computer instructions) calls itself to solve a smaller part of a problem.\n\nExample: To count files in a folder, count its files, then do the same inside each smaller folder.");
                popup.Complete(false); await Task.Delay(150);
                Check(popup.ContentHeight>0 && popup.ReadingHeight>=popup.ContentHeight,"Normal answer fits using actual rich-text height without scrolling");
                popup.PointerDown(new Point(popup.Right+20,popup.Bottom+20),0);
                Check(popup.Visible,"An older queued click cannot hide a newly opened reading window");
                popup.PointerDown(new Point(popup.Right+20,popup.Bottom+20));
                Check(popup.Visible,"Outside clicks do not dismiss completed readings");
                popup.ShowExisting();
                Check(popup.Visible && popup.AnswerText.Contains("Meaning:"),"Latest answer reopens without clearing content or making a request");
                Check(!popup.Bounds.IntersectsWith(anchor),"Result popup avoids selected text when there is space");
                using(var bmp=new Bitmap(popup.Width,popup.Height)) { popup.DrawToBitmap(bmp,popup.ClientRectangle); bmp.Save(Path.Combine(folder,"lookup-card.png")); }
                popup.Activate(); popup.Refresh(); await Task.Delay(200);
                Check(popup.Visible,"Popup stays visible while reading without requiring focus");
                using(var bmp=new Bitmap(popup.Width,popup.Height)) { using(var g=Graphics.FromImage(bmp)) g.CopyFromScreen(popup.Location,Point.Empty,popup.Size); bmp.Save(Path.Combine(folder,"lookup-card-screen.png")); }
                popup.SetAnswer("**Meaning:** A short definition.\n\n**Example:** One tiny example.");
                Check(!popup.AnswerText.Contains("**") && popup.AnswerText.Contains("Meaning:"),"Basic bold Markdown renders as readable text");
                int shortHeight=popup.Height;
                string longAnswer="Meaning: A function (a small computer job) follows a list of steps.\n\n";
                for(int i=0;i<14;i++) longAnswer+="Example "+(i+1)+": Read one number, add it to the total (the number so far), and then read the next number.\n\n";
                popup.BeginLookup("How a function uses a list of steps to solve a problem",anchor);
                for(int n=100;n<longAnswer.Length;n+=180) { popup.SetAnswer(longAnswer.Substring(0,n)); await Task.Delay(90); }
                popup.SetAnswer(longAnswer); popup.Complete(false); await Task.Delay(120);
                Check(popup.Height>shortHeight && area.Contains(popup.Bounds),"Long streamed answers expand the card within the screen");
                Check(popup.ReadingHeight>=popup.ContentHeight || popup.Height>=area.Height-Look.S(33),"Scrolling is needed only after available screen height is used");
                using(var bmp=new Bitmap(popup.Width,popup.Height)) { using(var g=Graphics.FromImage(bmp)) g.CopyFromScreen(popup.Location,Point.Empty,popup.Size); bmp.Save(Path.Combine(folder,"long-answer-screen.png")); }
                popup.BeginLookup("recursion",anchor);
                popup.SetAnswer("Meaning: A function (a small computer job) calls itself to solve a smaller part of a problem.\n\nExample: Count down: say 3, repeat with 2, then 1, and stop at 0."); popup.Complete(false);
                await Task.Delay(100);
                Check(popup.Height<shortHeight+Look.S(140),"A new short answer returns to a small window");
                var reading=(RichTextBox)typeof(ResultPopup).GetField("answer",flags).GetValue(popup);
                var copyButton=(Button)typeof(ResultPopup).GetField("copy",flags).GetValue(popup);
                typeof(ResultPopup).GetField("manualSize",flags).SetValue(popup,true);
                popup.Size=new Size(Look.S(500),Look.S(400));
                popup.SetAnswer("Meaning: A small test.\n\nExample: One step.");
                Check(popup.Width==Look.S(500) && popup.Height==Look.S(400),"Manual size is preserved when answer content updates");
                popup.FitToAnswer();
                Check(popup.Width==Look.S(480) && popup.Height<Look.S(430),"Fit returns from a manual size to content-based sizing");
                popup.Size=popup.MinimumSize;
                Check(reading.Bottom<copyButton.Top,"Reading text and footer stay separate at minimum size");
                popup.SetAnswer("Meaning: A function (a small computer job) calls itself to solve a smaller part of a problem.\n\nExample: Count down: say 3, repeat with 2, then 1, and stop at 0."); popup.FitToAnswer();
                popup.Refresh(); await Task.Delay(200);
                using(var bmp=new Bitmap(popup.Width,popup.Height)) { using(var g=Graphics.FromImage(bmp)) g.CopyFromScreen(popup.Location,Point.Empty,popup.Size); bmp.Save(Path.Combine(folder,"redesigned-card-screen.png")); }
                string original="When a function calls itself, it works on a smaller part of the same problem each time. It must also have a stopping rule, so that it can finish instead of calling itself forever.\nFor example, counting down from three reaches zero and then stops.";
                popup.BeginLookup(original,anchor);
                Check(popup.OriginalText.Replace("\r\n","\n")==original,"Original text beyond 220 characters is retained with line breaks");
                Check(popup.OriginalReadingHeight>Look.S(60) && popup.OriginalReadingHeight>=popup.OriginalContentHeight,"Full sentence grows beyond two lines before the answer arrives");
                popup.SetAnswer("Meaning: A function (a small computer job) repeats its work on a smaller part, then stops.\n\nExample: Say 3, then 2, then 1, and stop at 0."); popup.Complete(false);
                popup.Refresh(); await Task.Delay(200);
                using(var bmp=new Bitmap(popup.Width,popup.Height)) { using(var g=Graphics.FromImage(bmp)) g.CopyFromScreen(popup.Location,Point.Empty,popup.Size); bmp.Save(Path.Combine(folder,"full-original-screen.png")); }
                string passage=String.Join("\n",Enumerable.Repeat(original,25));
                popup.BeginLookup(passage,anchor); popup.SetAnswer("Meaning: A long selection.\n\nExample: Read all of it by scrolling the original text."); popup.Complete(false);
                Check(popup.OriginalText.Replace("\r\n","\n")==passage && popup.OriginalContentHeight>popup.OriginalReadingHeight,"Large original passages remain complete in a scrollable source area");
                Check(area.Contains(popup.Bounds) && reading.Bottom<copyButton.Top,"Large source text keeps the answer and actions within the screen");
                popup.BeginLookup("recursion",anchor); popup.SetAnswer("Meaning: Repeat on a smaller part.\n\nExample: Count down to zero."); popup.Complete(false);
                Check(popup.Height<Look.S(500),"Returning from a long original to a word restores a compact window");
                popup.Hide();
                using(var bubble=new SelectionBubble()) {
                    bubble.Present(window.Handle,anchor); await Task.Delay(100);
                    Check(bubble.Top==anchor.Bottom+Look.S(8) && bubble.Left==anchor.Left && !bubble.Bounds.IntersectsWith(anchor),"Styled button is fixed precisely below selection with a visible gap");
                    using(var bmp=new Bitmap(bubble.Width,bubble.Height)) { bubble.DrawToBitmap(bmp,bubble.ClientRectangle); bmp.Save(Path.Combine(folder,"selection-button.png")); }
                    Theme.TestDark=!Theme.Dark; Theme.Refresh(); bubble.Refresh();
                    using(var bmp=new Bitmap(bubble.Width,bubble.Height)) { bubble.DrawToBitmap(bmp,bubble.ClientRectangle); bmp.Save(Path.Combine(folder,"selection-button-alternate.png")); }
                    Theme.TestDark=null; Theme.Refresh();
                    bubble.Hide();
                }
                results.Add("Preview uses sample text only. No live API calls or user data.");
            } catch(Exception e) { results.Add(e.ToString()); exit=1; }
            finally { File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); typeof(MainWindow).GetField("exiting",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(window,true); window.Close(); }
        };
        Application.Run(window); return exit;
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string className,string title);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string className,string title);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd,int msg,IntPtr wp,IntPtr lp);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hwnd,ref Native.Point point);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Native.Point point);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd,int command);
    sealed class FixtureForm : Form {
        public TextBox Box;
        protected override void WndProc(ref Message m) {
            if(m.Msg==0x8001) { Box.Select(0,0); return; }
            if(m.Msg==0x8002) { Box.Focus(); Box.SelectAll(); return; }
            base.WndProc(ref m);
        }
    }
    public static void Fixture(bool topmost=true) {
        var form=new FixtureForm { Text="TermLens selection test fixture",Size=new Size(560,230),StartPosition=FormStartPosition.CenterScreen,TopMost=topmost };
        var box=new TextBox { Multiline=true,Dock=DockStyle.Fill,Text="recursion 递归 — fixture selection",Font=new Font("Microsoft YaHei UI",13),ImeMode=ImeMode.On };
        form.Box=box;
        form.Controls.Add(box); form.Shown+=delegate { box.Focus(); box.SelectAll(); };
        var timer=new System.Windows.Forms.Timer { Interval=120000 }; timer.Tick+=delegate { timer.Stop(); form.Close(); }; timer.Start(); Application.Run(form); timer.Dispose();
    }
    public static int Capture(string root) {
        string folder=TestRoot(root,"capture"); int exit=0;
        var driver=new Form { Text="TermLens capture test",ShowInTaskbar=false,Opacity=0,Size=new Size(1,1) };
        driver.Shown+=async delegate {
            Process fixture=null; IDataObject original=null; Native.Point oldCursor; GetCursorPos(out oldCursor); Native.Point lastTestCursor=oldCursor;
            string sentinel="TermLens clipboard restoration test "+Guid.NewGuid().ToString("N");
            try {
                original=Clipboard.GetDataObject();
                fixture=Process.Start(new ProcessStartInfo(Application.ExecutablePath,"--selection-fixture") { UseShellExecute=false });
                IntPtr source=IntPtr.Zero;
                for(int i=0;i<30;i++) { await Task.Delay(100); fixture.Refresh(); source=fixture.MainWindowHandle; if(source!=IntPtr.Zero) break; }
                Check(source!=IntPtr.Zero,"External native selection fixture launched"); ShowWindow(source,9); Native.SetForegroundWindow(source); await Task.Delay(250);
                if(Native.GetForegroundWindow()!=source) {
                    Native.Point activatePoint=new Native.Point { X=20,Y=20 }; ClientToScreen(source,ref activatePoint);
                    Check(Native.GetAncestor(Native.WindowFromPoint(activatePoint),2)==source,"Only click inside the visible test fixture");
                    SetCursorPos(activatePoint.X,activatePoint.Y); mouse_event(2,0,0,0,UIntPtr.Zero); await Task.Delay(60); mouse_event(4,0,0,0,UIntPtr.Zero);
                    lastTestCursor=activatePoint; await Task.Delay(100); SendMessage(source,0x8002,IntPtr.Zero,IntPtr.Zero); await Task.Delay(100);
                }
                string accessible=await Selection.AccessibleSelection(source);
                results.Add("FOCUS: fixture="+source+" fixturePid="+fixture.Id+" foreground="+Native.GetForegroundWindow()+" foregroundPid="+Native.ProcessOf(Native.GetForegroundWindow())+" sourcePid="+Native.ProcessOf(source));
                Check(Native.GetForegroundWindow()==source,"External fixture is foreground before capture");
                if(accessible!=null) Check(accessible.Contains("递归"),"Read selected Unicode text via Windows UI Automation");
                else results.Add("INFO: This edit control exposes no usable UI Automation text selection; exercising the copy fallback.");
                using(var bubble=new SelectionBubble()) {
                    bubble.Present(source,new Point(450,350)); await Task.Delay(100);
                    Check(Native.GetForegroundWindow()==source,"Floating button does not steal source focus");
                    bubble.Hide();
                }
                Clipboard.SetText(sentinel); var result=await Selection.CopySelection(source);
                Check(result.Text.Contains("recursion") && result.Text.Contains("递归"),"Ctrl+C fallback captures exact Unicode selection");
                Check(Clipboard.GetText()==sentinel,"Fallback restores previous clipboard text");
                // Clearing selection must not return stale clipboard text.
                SendMessage(source,0x8001,IntPtr.Zero,IntPtr.Zero);
                bool staleRejected=false;
                try { await Selection.CopySelection(source); } catch(Exception e) { staleRejected=e.Message.Contains("No selected text"); }
                Check(staleRejected && Clipboard.GetText()==sentinel,"Empty selection never sends stale clipboard text");
                bool emptyDetected=false; try { await Selection.Capture(source); } catch(NoSelectionException) { emptyDetected=true; }
                Check(emptyDetected,"Normal capture reports empty external selection for the F13 input window");
                using(var bubble=new SelectionBubble())
                using(var hook=new MouseSelection(driver,bubble,delegate { return true; })) {
                    Check(hook.Installed,"Global mouse hook installed");
                    Task<CaptureResult> clicked=null;
                    SelectionAction chosen=SelectionAction.Explain;
                    bubble.ActionRequested+=delegate(IntPtr target,SelectionAction action) { chosen=action; clicked=Selection.Capture(target); };
                    Native.Point start=new Native.Point { X=5,Y=18 }; ClientToScreen(source,ref start);
                    SetCursorPos(start.X,start.Y); mouse_event(2,0,0,0,UIntPtr.Zero); await Task.Delay(70);
                    SetCursorPos(start.X+250,start.Y); await Task.Delay(70); mouse_event(4,0,0,0,UIntPtr.Zero);
                    lastTestCursor=new Native.Point { X=start.X+250,Y=start.Y };
                    var appearance=Stopwatch.StartNew();
                    for(int i=0;i<50 && !bubble.Visible;i++) await Task.Delay(20);
                    results.Add("Toolbar appearance after mouse release: "+appearance.ElapsedMilliseconds+" ms (native test fixture).");
                    Check(bubble.Visible,"Dragging selection creates floating Explain button");
                    Check(!bubble.Bounds.IntersectsWith(bubble.SelectionBounds),"Real drag button does not cover selected text");
                    int bx=bubble.Left+bubble.ActionBounds(0).Left+bubble.ActionBounds(0).Width/2, by=bubble.Top+bubble.Height/2;
                    SetCursorPos(bx,by); mouse_event(2,0,0,0,UIntPtr.Zero); await Task.Delay(70); mouse_event(4,0,0,0,UIntPtr.Zero);
                    lastTestCursor=new Native.Point { X=bx,Y=by };
                    for(int i=0;i<20 && clicked==null;i++) await Task.Delay(50);
                    Check(clicked!=null,"Clicking floating button invokes capture");
                    CaptureResult captured=await clicked;
                    Check(captured.Text.StartsWith("recursion"),"Full drag → floating button → selected text flow succeeds");
                    for(int action=1;action<3;action++) {
                        clicked=null; bubble.Present(source,bubble.SelectionBounds); await Task.Delay(60);
                        var hit=bubble.ActionBounds(action); bx=bubble.Left+hit.Left+hit.Width/2; by=bubble.Top+hit.Top+hit.Height/2;
                        Check(Native.GetAncestor(Native.WindowFromPoint(new Native.Point { X=bx,Y=by }),2)==bubble.Handle,"Only click the visible toolbar action");
                        SetCursorPos(bx,by); mouse_event(2,0,0,0,UIntPtr.Zero); await Task.Delay(45); mouse_event(4,0,0,0,UIntPtr.Zero);
                        lastTestCursor=new Native.Point { X=bx,Y=by };
                        for(int i=0;i<20 && clicked==null;i++) await Task.Delay(25);
                        Check(clicked!=null && chosen==(SelectionAction)action,"Physical click selects "+(SelectionAction)action+" action");
                        var other=await clicked;
                        Check(other.Text==captured.Text && Native.GetForegroundWindow()==source,"Action preserves exact selection and source focus: "+(SelectionAction)action);
                    }
                }
                results.Add("Real Chrome/Edge/VS Code and physical Chinese IME composition remain user acceptance tests.");
            } catch(Exception e) { results.Add(e.ToString()); exit=1; }
            finally {
                try { if(Clipboard.ContainsText() && (Clipboard.GetText()==sentinel || Clipboard.GetText().StartsWith("recursion 递归"))) { if(original!=null) Clipboard.SetDataObject(original,true); else Clipboard.Clear(); } } catch { }
                if(fixture!=null && !fixture.HasExited) fixture.CloseMainWindow();
                Native.Point cursorNow; GetCursorPos(out cursorNow); if(cursorNow.X==lastTestCursor.X && cursorNow.Y==lastTestCursor.Y) SetCursorPos(oldCursor.X,oldCursor.Y);
                File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); driver.Close();
            }
        };
        Application.Run(driver); return exit;
    }
}
}
