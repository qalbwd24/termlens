using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace TermLens {
// This override exists only in test builds; SHARE excludes *Tests.cs files.
public sealed partial class MainWindow {
    protected override bool ShowWithoutActivation { get { return Program.IsTest; } }
}
public static partial class Tests {
    public static int ReleasePromptLibraryUi(string root) {
        string folder=TestRoot(root,"release-prompt-library-ui"); int exit=0;
        const string originalPrompt="Explain technical English simply and preserve the meaning.";
        File.WriteAllText(Path.Combine(folder,"settings.json"),Files.Json().Serialize(new Settings { Prompt=originalPrompt,SpeechPreload=false,SpeechProvider="edge",SpeechVoice="en-US-EmmaMultilingualNeural",SpeechProxy="http://localhost:19888",SpeechRate=-5 }),Files.Utf8);
        string before=File.ReadAllText(Path.Combine(folder,"settings.json"),Files.Utf8);
        var window=new MainWindow(folder) { StartPosition=FormStartPosition.Manual,Location=new Point(30,30),ShowInTaskbar=false };
        window.Shown+=delegate { window.Location=new Point(30,30); };
            try {
                window.Location=new Point(30,30); window.Show();
                window.Location=new Point(30,30); Application.DoEvents();
                typeof(MainWindow).GetMethod("SelectReleasePage",Private).Invoke(window,new object[]{1});
                var editor=Field<TextBox>(window,"prompt");
                int initialCount=window.ReleasePromptDrafts.Count;
                Check(initialCount>=2 && initialCount<=3 && editor.Text==originalPrompt,"Legacy prompt is preserved with built-in reading scenarios");
                string first=window.SelectedReleasePromptPresetId;
                var technical=window.ReleasePromptDrafts.Single(p=>p.TemplateId=="technical");
                window.SelectReleasePromptPreset(technical.Id); editor.Text="Temporary technical rewrite.";
                window.SelectReleasePromptLevel(true); editor.Text="Temporary technical explanation."; window.RestoreReleasePromptLevel();
                Check(editor.Text.Replace("\r\n","\n")==PromptLibrary.CreateTechnical().SecondaryText.Replace("\r\n","\n") && window.ReleasePromptDrafts.Single(p=>p.Id==technical.Id).Text=="Temporary technical rewrite.","Technical restore follows template identity and affects only second level");
                window.SelectReleasePromptLevel(false); window.RestoreReleasePromptLevel();
                Check(editor.Text.Replace("\r\n","\n")==PromptLibrary.CreateTechnical().Text.Replace("\r\n","\n"),"Technical first-level restore uses its own scenario default");
                window.CreateReleasePromptPreset("技术场景副本",true); string technicalCopy=window.SelectedReleasePromptPresetId;
                Check(window.ReleasePromptDrafts.Single(p=>p.Id==technicalCopy).TemplateId=="technical" && window.ReleasePromptDrafts.Single(p=>p.Id==technicalCopy).SecondaryText==window.ReleasePromptDrafts.Single(p=>p.Id==technical.Id).SecondaryText,"Save As retains scenario template identity and both levels");
                window.DeleteReleasePromptPreset(technicalCopy,false);
                window.SelectReleasePromptPreset(first);
                window.SelectReleasePromptLevel(true); editor.Text="Explain selected technical terms with necessary background.";
                window.SelectReleasePromptLevel(false);
                Check(editor.Text==originalPrompt,"Switching levels preserves the first-level draft");
                editor.Text="My unsaved computer-English draft.";
                window.CreateReleasePromptPreset("学术阅读",false); string second=window.SelectedReleasePromptPresetId;
                Check(editor.Text.Replace("\r\n","\n")==Settings.ReleaseDefaultPrompt.Replace("\r\n","\n"),"New archive starts from the editable English-learning default");
                editor.Text="Explain the English abstract and technical vocabulary.";
                window.SelectReleasePromptLevel(true); editor.Text="Explain the selected phrase in this abstract.";
                window.SelectReleasePromptLevel(false);
                window.SelectReleasePromptPreset(first);
                Check(editor.Text=="My unsaved computer-English draft.","Switching away and back retains the first archive's unsaved edits");
                window.SelectReleasePromptLevel(true);
                Check(editor.Text=="Explain selected technical terms with necessary background.","Scenario switches retain separate second-level drafts");
                window.SelectReleasePromptLevel(false);
                window.SelectReleasePromptPreset(second);
                Check(editor.Text=="Explain the English abstract and technical vocabulary.","Both archives retain separate in-memory drafts");
                window.CreateReleasePromptPreset("学术阅读副本",true); string copied=window.SelectedReleasePromptPresetId;
                Check(editor.Text=="Explain the English abstract and technical vocabulary.","Save As copies the currently edited text rather than the previously saved text");
                window.SelectReleasePromptLevel(true);
                Check(editor.Text=="Explain the selected phrase in this abstract.","Save As also copies the current second-level draft");
                window.SelectReleasePromptLevel(false);
                window.RenameReleasePromptPreset("日常英语");
                bool duplicate=false;
                try { window.RenameReleasePromptPreset("学术阅读"); } catch(InvalidOperationException) { duplicate=true; }
                Check(duplicate && window.ReleasePromptDrafts.Single(p=>p.Id==copied).Name=="日常英语","Duplicate archive names are rejected without changing the current name");
                window.DeleteReleasePromptPreset(second,false);
                window.SelectReleasePromptPreset(first);
                var reset=SettingsDescendants(Field<System.Collections.Generic.List<Panel>>(window,"releasePages")[1]).OfType<Button>().Single(b=>b.Text=="恢复当前级别默认");
                typeof(Button).GetMethod("OnClick",Private).Invoke(reset,new object[]{EventArgs.Empty});
                Check(window.ReleasePromptDrafts.Count==initialCount+1 && window.ReleasePromptDrafts.Single(p=>p.Id==copied).Text=="Explain the English abstract and technical vocabulary.","Restoring the English default edits only the selected archive");
                window.SelectReleasePromptLevel(true); window.RestoreReleasePromptLevel();
                Check(editor.Text.Replace("\r\n","\n")==Settings.ReleaseSecondaryDefaultPrompt.Replace("\r\n","\n") && window.ReleasePromptDrafts.Single(p=>p.Id==first).Text.Replace("\r\n","\n")==Settings.ReleaseDefaultPrompt.Replace("\r\n","\n"),"Restore affects only the current level of a custom scenario");
                window.SelectReleasePromptLevel(false);
                window.SelectReleasePromptPreset(copied);
                Check(window.ReleasePromptLibraryDirty && File.ReadAllText(Path.Combine(folder,"settings.json"),Files.Utf8)==before,"Editing, renaming, deleting and switching stay in drafts until settings are saved");
                typeof(MainWindow).GetMethod("SelectReleasePage",Private).Invoke(window,new object[]{2});
                var pending=window.ReadReleaseSettings();
                Check(pending.PromptPresets.Count==initialCount+1 && pending.ActivePromptPresetId==copied && pending.Prompt=="Explain the English abstract and technical vocabulary.","Saving another settings page exports all drafts and the selected active archive");
                Check((bool)typeof(MainWindow).GetMethod("SaveSettings",Private).Invoke(window,new object[]{false}),"The normal global SaveSettings path persists the archive library");
                var saved=Files.Load(folder);
                Check(saved.PromptPresets.Count==initialCount+1 && saved.ActivePromptPresetId==copied && saved.Prompt==pending.Prompt && saved.SpeechProvider=="edge" && saved.SpeechVoice=="en-US-EmmaMultilingualNeural" && saved.SpeechProxy=="http://localhost:19888" && saved.SpeechRate==-5,"Atomic save/load retains archives, active prompt and unrelated audio settings");
                Check(saved.PromptPresets.Single(p=>p.Id==copied).SecondaryText=="Explain the selected phrase in this abstract.","Save/load retains second-level scenario content");
                Check(!window.ReleasePromptLibraryDirty,"Only a successful settings save marks the library clean");
                window.SelectReleasePromptLevel(true); editor.Text="";
                string diskBeforeSecondaryFailure=File.ReadAllText(Path.Combine(folder,"settings.json"),Files.Utf8);
                Check(!(bool)typeof(MainWindow).GetMethod("SaveSettings",Private).Invoke(window,new object[]{false}) && window.ReleasePromptLibraryDirty && File.ReadAllText(Path.Combine(folder,"settings.json"),Files.Utf8)==diskBeforeSecondaryFailure,"Empty second-level save preserves disk settings and unsaved drafts");
                editor.Text="Explain the selected phrase in this abstract."; window.SelectReleasePromptLevel(false);
                string beforeFailure=File.ReadAllText(Path.Combine(folder,"settings.json"),Files.Utf8),savedEditor=editor.Text;
                editor.Text="";
                Check(!(bool)typeof(MainWindow).GetMethod("SaveSettings",Private).Invoke(window,new object[]{false}),"An empty prompt fails through the actual global save path");
                Check(window.ReleasePromptLibraryDirty && File.ReadAllText(Path.Combine(folder,"settings.json"),Files.Utf8)==beforeFailure,"Failed save preserves the disk settings and leaves drafts marked unsaved");
                editor.Text=savedEditor;
                typeof(MainWindow).GetField("settings",Private).SetValue(window,saved); window.ApplyReleaseSettings();
                Check(window.SelectedReleasePromptPresetId==copied && editor.Text==saved.Prompt,"Reload restores the saved active archive and its text");
                window.DeleteReleasePromptPreset(copied,false);
                foreach(var extra in window.ReleasePromptDrafts.Where(p=>p.Id!=first).ToList()) window.DeleteReleasePromptPreset(extra.Id,false);
                bool lastRejected=false;
                try { window.DeleteReleasePromptPreset(first,false); } catch(InvalidOperationException) { lastRejected=true; }
                Check(lastRejected && window.ReleasePromptDrafts.Count==1 && window.SelectedReleasePromptPresetId==first,"Deleting the active archive selects a valid remaining archive and cannot remove the final archive");
                window.ApplyReleaseSettings();
                window.DeleteReleasePromptPreset(copied,false);
                typeof(MainWindow).GetMethod("SelectReleasePage",Private).Invoke(window,new object[]{1}); window.Size=window.MinimumSize;
                var scenarioStrip=Field<FlowLayoutPanel>(window,"releasePromptScenarios"); Application.DoEvents();
                results.Add("INFO: strip "+scenarioStrip.ClientSize+" horizontal "+scenarioStrip.HorizontalScroll.Visible+" cards "+String.Join(" / ",scenarioStrip.Controls.Cast<Control>().Select(c=>c.Bounds.ToString())));
                Check(scenarioStrip.Controls.Count==3 && !scenarioStrip.HorizontalScroll.Visible && scenarioStrip.Controls.Cast<Control>().All(c=>c.Right<=scenarioStrip.ClientSize.Width && c.Bottom<=scenarioStrip.ClientSize.Height),"Three reading scenarios are all directly visible at minimum window size");
                var work=Screen.FromControl(window).WorkingArea;
                Check(window.Size==new Size(Math.Min(Look.S(740),(int)Math.Round(work.Width*0.96)),Math.Min(Look.S(520),(int)Math.Round(work.Height*0.96))),"Shown fixture uses the real OnLoad minimum geometry: "+window.Size);
                Check(Field<System.Collections.Generic.List<int>>(window,"registered").Count==0,"Prompt archive UI fixture does not register a global hotkey");
                for(int theme=0;theme<2;theme++) {
                    Theme.TestDark=theme==0; Theme.Refresh(); Theme.Apply(window); PromptLibraryFixtureLayout(window); Application.DoEvents();
                    var page=Field<System.Collections.Generic.List<Panel>>(window,"releasePages")[1];
                    foreach(var row in SettingsDescendants(page).OfType<SettingsActionRow>()) CheckSettingsActions(row,"Prompt archive actions at real minimum size");
                    CheckSettingsActions(Field<SoftButton>(window,"releasePromptPrimary").Parent,"Large level buttons at minimum size");
                    CheckSettingsActions(reset.Parent,"Restore action at minimum size");
                    CheckSettingsEditor(editor,3,"Prompt archive editor at real minimum size");
                    Check(!page.VerticalScroll.Visible,"Archive selector, actions and editor fit the minimum window without page scrolling");
                    string priorText=editor.Text;
                    editor.Text=String.Join("\r\n",Enumerable.Range(0,100).Select(i=>"English-learning draft line "+i));
                    editor.Select(0,0); editor.ScrollToCaret();
                    Native.PostMessage(editor.Handle,0x20A,new IntPtr(unchecked((int)0xff880000)),IntPtr.Zero); Application.DoEvents();
                    Check(SendMessage(editor.Handle,0xCE,IntPtr.Zero,IntPtr.Zero).ToInt32()>0,"Prompt editor mouse wheel scrolls a long archive draft");
                    editor.Select(editor.TextLength,0); editor.ScrollToCaret();
                    var host=(ReleasePromptScrollHost)editor.Parent.Parent; host.SyncScroll();
                    Check(SendMessage(editor.Handle,0xCE,IntPtr.Zero,IntPtr.Zero).ToInt32()>30 && host.Scrollbar.Value>0,"Prompt editor and slim scrollbar reach the final draft lines");
                    string beforeScroll=editor.Text; int selection=editor.SelectionStart; host.Scrollbar.Value=0;
                    Check(SendMessage(editor.Handle,0xCE,IntPtr.Zero,IntPtr.Zero).ToInt32()==0 && editor.Text==beforeScroll && editor.SelectionStart==selection,"Archive slim scrollbar preserves draft text and selection when dragged to its start");
                    editor.Text=priorText; editor.Select(0,0); editor.ScrollToCaret(); Application.DoEvents();
                    window.SelectReleasePromptPreset(technical.Id); window.SelectReleasePromptLevel(theme==1);
                    window.SetStatus("阅读场景的两级提示词可分别编辑。",false); Application.DoEvents();
                    var primaryButton=Field<SoftButton>(window,"releasePromptPrimary"); var secondaryButton=Field<SoftButton>(window,"releasePromptSecondaryButton"); var purpose=Field<Label>(window,"releasePromptPurpose");
                    Check(primaryButton.Visible && secondaryButton.Visible && primaryButton.Height>=Look.S(32) && primaryButton.Width>=Look.S(200) && purpose.Visible,"Both levels have large direct-click buttons and a visible purpose");
                    primaryButton.PerformClick(); Check(!window.ReleasePromptSecondary && primaryButton.Primary && !secondaryButton.Primary,"One click selects and highlights the primary editor");
                    secondaryButton.PerformClick(); Check(window.ReleasePromptSecondary && !primaryButton.Primary && secondaryButton.Primary,"One click selects and highlights the secondary editor");
                    window.SelectReleasePromptLevel(theme==1);
                    var selectedCard=scenarioStrip.Controls.Cast<Button>().Single(b=>(string)b.Tag==technical.Id);
                    Check((bool)selectedCard.GetType().GetField("Selected").GetValue(selectedCard) && scenarioStrip.Controls.Cast<Button>().Count(b=>(bool)b.GetType().GetField("Selected").GetValue(b))==1,"Only the selected scenario card is highlighted");
                    Check(Field<Label>(window,"releasePromptLibraryState").Text.Contains("尚未应用") && Field<Label>(window,"releasePromptLibraryState").Text.Contains("日常英语"),"Draft selection is distinguished from the scene actually applied");
                    string libraryShot=Path.Combine(folder,theme==0?"prompt-library-dark.png":"prompt-library-light.png");
                    SettingsShot(window,libraryShot);
                    using(var bitmap=new Bitmap(libraryShot)) {
                        var colors=new System.Collections.Generic.HashSet<int>();
                        for(int x=0;x<bitmap.Width;x+=30) for(int y=0;y<bitmap.Height;y+=30) colors.Add(bitmap.GetPixel(x,y).ToArgb());
                        Check(colors.Count>8,"Prompt library bitmap contains rendered controls rather than a blank frame");
                    }
                }
                while(window.ReleasePromptDrafts.Count<10) window.CreateReleasePromptPreset("场景 "+(window.ReleasePromptDrafts.Count+1),false);
                string longName=new string('长',50)+"阅读场景"; window.RenameReleasePromptPreset(longName);
                var scenarioIds=window.ReleasePromptDrafts.Select(p=>p.Id).ToList();
                foreach(var id in scenarioIds) {
                    window.SelectReleasePromptPreset(id); window.SelectReleasePromptLevel(false); editor.Text="Primary draft "+id;
                    window.SelectReleasePromptLevel(true); editor.Text="Secondary draft "+id;
                }
                foreach(var id in scenarioIds) {
                    var card=scenarioStrip.Controls.Cast<Button>().Single(b=>(string)b.Tag==id); card.PerformClick();
                    window.SelectReleasePromptLevel(false); Check(editor.Text=="Primary draft "+id,"Direct scenario button retains its primary draft: "+card.Text);
                    window.SelectReleasePromptLevel(true); Check(editor.Text=="Secondary draft "+id,"Direct scenario button retains its secondary draft: "+card.Text);
                }
                var longCard=scenarioStrip.Controls.Cast<Button>().Single(b=>b.Text==longName);
                Check(Field<ToolTip>(window,"releasePromptTips").GetToolTip(longCard)==longName && longCard.Width<=scenarioStrip.ClientSize.Width/2,"Long names keep a compact ellipsis card with a complete tooltip");
                Check(File.ReadAllText(Path.Combine(folder,"settings.json"),Files.Utf8)==beforeFailure,"Scenario card clicks and ten-scenario edits do not implicitly save other settings");
                for(int theme=0;theme<2;theme++) {
                    Theme.TestDark=theme==0; Theme.Refresh(); Theme.Apply(window); PromptLibraryFixtureLayout(window); Application.DoEvents();
                    window.SelectReleasePromptPreset(scenarioIds[0]); scenarioStrip.AutoScrollPosition=Point.Empty; Application.DoEvents();
                    Native.PostMessage(scenarioStrip.Handle,0x20A,new IntPtr(unchecked((int)0xff880000)),IntPtr.Zero); Application.DoEvents();
                    Check(scenarioStrip.HorizontalScroll.Visible && !scenarioStrip.VerticalScroll.Visible && scenarioStrip.AutoScrollPosition.X<0,"Ten scenario cards scroll horizontally by mouse wheel without adding editor rows");
                    window.SelectReleasePromptPreset(scenarioIds[9]); Application.DoEvents();
                    Check(longCard.Left>=0 && longCard.Right<=scenarioStrip.ClientSize.Width && editor.Visible && editor.Height>=editor.Font.Height*3,"Last scenario scrolls into view while the editor remains usable");
                    window.SetStatus("点击场景即可选择；保存并应用后生效。",false);
                    SettingsShot(window,Path.Combine(folder,theme==0?"ten-scenarios-dark.png":"ten-scenarios-light.png"));
                }
                window.DeleteReleasePromptPreset(scenarioIds[9],false);
                Check(window.ReleasePromptDrafts.Count==9 && scenarioStrip.Controls.Count==9 && window.SelectedReleasePromptPresetId!=scenarioIds[9],"Deleting a selected card updates the strip and selects a surviving scenario");
            } catch(Exception error) { results.Add("FAIL: "+error); exit=1; }
            finally {
                Theme.TestDark=null; Theme.Refresh();
                File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8);
                typeof(MainWindow).GetField("exiting",Private).SetValue(window,true); window.Close();
            }
        window.Dispose(); return exit;
    }
    static void PromptLibraryFixtureLayout(Control control) {
        control.PerformLayout();
        foreach(Control child in control.Controls) PromptLibraryFixtureLayout(child);
    }
}
}
