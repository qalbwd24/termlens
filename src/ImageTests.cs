using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TermLens {
public static partial class Tests {
    static Bitmap ImageFixture() {
        var image=new Bitmap(620,150);
        using(var g=Graphics.FromImage(image)) using(var font=new Font("Arial",28,FontStyle.Bold)) { g.Clear(Color.White); g.DrawString("RIGHT ANGLE\n90 DEGREES",font,Brushes.Black,12,10); }
        return image;
    }
    public static int Images(string root,bool live) {
        string folder=TestRoot(root,live?"image-live":"images"); int exit=0;
        if(live) {
            try {
                var use=Files.Load(Path.Combine(root,"data")); use.CarefulAnswers=false; use.MaxTokens=128; use.Prompt="Read the two lines in the attached test image. Return those exact words and nothing else.";
                ImageInput.ValidateModel(use);
                using(var image=ImageFixture()) {
                    var attachment=ImageInput.Store(folder,image,"Synthetic test");
                    string reply=Requests.Complete(use,Files.LoadKey(Path.Combine(root,"data")),"Read both lines.",new List<string>(),delegate{},CancellationToken.None,null,null,ImageInput.DataUrls(folder,new List<StudyImage>{attachment})).GetAwaiter().GetResult();
                    Check(reply.ToUpperInvariant().Contains("RIGHT ANGLE") && reply.Contains("90"),"Configured live model reads text from a synthetic screenshot");
                    File.WriteAllText(Path.Combine(folder,"answer.txt"),reply,Files.Utf8);
                }
            } catch(Exception e) { exit=1; results.Add("Live image test failed: "+e.GetType().Name+"; "+e.Message); }
            File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); return exit;
        }
        Files.Save(folder,new Settings { ReadingWidth=560,ReadingFontSize=12,SpeechPreload=false }); Files.SaveKey(folder,"isolated-image-test");
        var handlers=new List<Handler>(); var app=new MainWindow(folder,true,delegate { var handler=new Handler(Reply("**Right angle** · 直角\nAn angle of $90^\\circ$, like a corner of a square.")); handlers.Add(handler); return handler; });
        IDataObject clipboard=null;
        app.Shown+=async delegate {
            try {
                await Task.Delay(100); clipboard=Clipboard.GetDataObject();
                var window=(WorkspaceWindow)typeof(MainWindow).GetMethod("EnsureWorkspace",Private).Invoke(app,new object[]{(Rectangle?)new Rectangle(120,90,130,20),null});
                using(var image=ImageFixture()) {
                    Clipboard.SetImage(image); window.Composer.Focus(); SendMessage(window.Composer.Handle,0x302,IntPtr.Zero,IntPtr.Zero);
                    Check(window.Session.DraftImages.Count==1 && window.Composer.TextLength==0,"Native Paste attaches a screenshot to an empty input instead of inserting binary text");
                    Check(handlers.Count==0,"Pasting an image never uploads or sends a request");
                    var attachment=window.Session.DraftImages[0];
                    Check(File.Exists(ImageInput.PathFor(folder,attachment)),"Image is saved in the local profile");
                    Check(!Files.Json().Serialize(window.Session).Contains("base64"),"Session caches references without duplicating image bytes");
                    Theme.TestDark=true; Theme.Refresh(); Theme.Apply(window); Shot(window,Path.Combine(folder,"image-draft-dark.png"));
                    Theme.TestDark=false; Theme.Refresh(); Theme.Apply(window); Shot(window,Path.Combine(folder,"image-draft-light.png"));
                    window.Send();
                    for(int i=0;i<70 && (window.Session.Entries.Count==0 || !window.Session.Entries[0].Finished);i++) await Task.Delay(30);
                    Check(window.Session.Entries.Count==1 && window.Session.Entries[0].Finished,"Image-only Enter dispatch finishes in Explain mode");
                    var entry=window.Session.Entries[0];
                    Check(entry.ImageOnly && entry.Images.Count==1 && window.Session.DraftImages.Count==0,"Accepted image moves into its record and clears only the submitted draft");
                    var json=Files.Json().Deserialize<Dictionary<string,object>>(handlers[0].Body); var messages=(ArrayList)json["messages"]; var parts=(ArrayList)((Dictionary<string,object>)messages[messages.Count-1])["content"];
                    var part=(Dictionary<string,object>)parts[1]; var url=(Dictionary<string,object>)part["image_url"];
                    Check((string)part["type"]=="image_url" && ((string)url["url"]).StartsWith("data:image/png;base64,"),"API sends a real multimodal image_url block alongside the text instruction");
                    Check(handlers[0].Body.Contains("Read the image") && handlers[0].Body.Contains("skip sentence correction"),"Image-only request has a clear reading instruction instead of pretending the label is a learner question");
                    Shot(window,Path.Combine(folder,"image-answer-light.png"));
                    window.Composer.Text="What does this angle mean?"; window.AttachImage(image,"Second screenshot"); window.SetMode(WorkMode.Ask); window.Send();
                    for(int i=0;i<70 && (window.Session.Entries.Count<2 || !window.Session.Entries[1].Finished);i++) await Task.Delay(30);
                    Check(window.Session.Entries.Count==2 && !window.Session.Entries[1].ImageOnly && handlers[1].Body.Contains("What does this angle mean?"),"Ask sends the typed question with its image");
                    await WorkCall(app,"SendWorkspace",window,WorkMode.Polish,"",new List<WorkQuote>(),false,entry.Images);
                    Check(window.Session.Entries.Last().Mode==WorkMode.Polish && handlers.Last().Body.Contains("improve it"),"Polish action retains the original attached image");
                    window.AttachImage(image,"Keep draft"); window.Composer.Text="my unfinished question";
                    Field<TextBox>(app,"key").Text=""; int count=window.Session.Entries.Count; window.Send();
                    Check(window.Session.Entries.Count==count && window.Session.DraftImages.Count==1 && window.Composer.Text=="my unfinished question","Validation failure keeps both the image and typed draft");
                    Field<TextBox>(app,"key").Text="isolated-image-test";
                    Field<TextBox>(app,"model").Text="deepseek-v4-pro"; int requests=handlers.Count; window.Send();
                    Check(handlers.Count==requests && window.Session.DraftImages.Count==1 && window.Session.Entries.Count==count,"Incompatible DeepSeek model is rejected before upload and keeps the draft");
                    Field<TextBox>(app,"model").Text="deepseek-flash";
                    var tile=Field<FlowLayoutPanel>(window,"imageRow").Controls[0]; typeof(Control).GetMethod("OnMouseUp",Private).Invoke(tile,new object[]{new MouseEventArgs(MouseButtons.Left,1,tile.Width-2,2,0)});
                    Check(window.Session.DraftImages.Count==0 && window.Composer.Text=="my unfinished question","Remove icon clears only its image and keeps the text");
                    for(int i=0;i<5;i++) window.AttachImage(image,"Limit test");
                    Check(window.Session.DraftImages.Count==4,"A fifth image is refused without losing the first four");
                    window.SaveNow(); var restored=(WorkSession)typeof(MainWindow).GetMethod("ReadSession",Private).Invoke(app,null);
                    Check(restored.DraftImages.Count==4 && restored.Entries[0].Images[0].Id==attachment.Id,"Closing/restoring a session retains image drafts and sent-image references");
                    var frame=Field<FieldFrame>(window,"composerFrame"); var strip=Field<FlowLayoutPanel>(window,"imageRow"); Check(strip.Bottom<frame.Top && strip.Controls.Cast<Control>().All(c=>c.Right<=strip.Width),"Four thumbnails fit above the input without overlap");
                    window.Session.DraftImages.Clear(); window.RestoreDraftImages(null); Clipboard.SetText("ordinary text"); Check(!window.PasteClipboardImage(),"Plain-text clipboard falls through to ordinary paste");
                    bool refused=false; try { ImageInput.PathFor(folder,new StudyImage { Id="../../outside" }); } catch { refused=true; } Check(refused,"Saved image IDs cannot address a file outside the profile");
                    using(var large=new Bitmap(3600,800)) { var reduced=ImageInput.Store(folder,large,"Large screenshot"); Check(reduced.Width<=2048 && reduced.Height>0,"Large images are resized before upload"); }
                }
            } catch(Exception e) { exit=1; results.Add(e.ToString()); }
            finally { if(clipboard!=null) try { Clipboard.SetDataObject(clipboard,true); } catch { } File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); typeof(MainWindow).GetField("exiting",Private).SetValue(app,true); app.Close(); }
        };
        Application.Run(app); return exit;
    }
}
}
