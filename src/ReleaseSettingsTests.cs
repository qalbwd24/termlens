using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TermLens {
public static partial class Tests {
    public static int ReleaseSettings(string root) {
        string folder=TestRoot(root,"release-settings"); int exit=0;
        Files.Save(folder,new Settings { SpeechPreload=false });
        var window=new MainWindow(folder);
        window.Shown+=async delegate {
            try {
                await Task.Delay(120);
                var pages=Field<List<Panel>>(window,"releasePages");
                Check(pages.Count==5,"Release has four settings groups and one library page");
                string migration=Path.Combine(folder,"prompt-migration"); Directory.CreateDirectory(migration);
                Files.Save(migration,new Settings { Prompt=Settings.ReleasePreviousDefaultPrompt.Replace("\r\n","\n") });
                string beforeMigration=File.ReadAllText(Path.Combine(migration,"settings.json"),Files.Utf8);
                var migrated=Files.Load(migration);
                Check(migrated.PromptPresets.Any(p=>p.Text==Settings.ReleasePreviousDefaultPrompt.Replace("\r\n","\n")) && migrated.PromptPresets.Single(p=>p.Id==migrated.ActivePromptPresetId).TemplateId=="technical" && File.ReadAllText(Path.Combine(migration,"settings.json"),Files.Utf8)==beforeMigration,"Previous default is archived verbatim, the new technical scene is selected, and load does not rewrite user files");
                Files.Save(migration,new Settings { Prompt="My own English learning preference." });
                Check(Files.Load(migration).Prompt=="My own English learning preference.","Custom prompt survives migration unchanged");
                Check(!SettingsDescendants(window).Contains(Field<TextBox>(window,"input")),"No lookup composer is mounted in release settings");
                Check(Field<List<int>>(window,"registered").Count==0,"UI tests do not register a global shortcut");
                var candidate=window.ReadReleaseSettings();
                Check(candidate.Prompt==PromptLibrary.CreateTechnical().Text.Replace("\r\n","\n") && candidate.PromptPresets.Count==2 && !candidate.IncludeHistory && !candidate.QuoteAllWindow,"Clean profile starts with two reading scenes and the technical primary prompt without unrelated conversation history");
                CheckReleaseEdgeSettings(window,folder);
                var store=Field<HistoryStore>(window,"history");
                var first=new Lookup { Id="fixture-saved",Kind="quick",Source="release-context",Term="perpendicular",Answer="Two lines meet at 90 degrees. 垂直的。",Created="2026-01-01T00:00:00Z",Context="{\"source\":\"A right angle.\"}" };
                store.RecordQuery(first.Id,first.Kind,first.Source,first.Term,first.Answer,"The sides form a right angle.",first.Context,"mock","prompt",first.Created,"complete","");
                store.SetFavorite(first,true);
                store.Run("BEGIN IMMEDIATE");
                try {
                    for(int i=0;i<520;i++) store.RecordQuery("ordinary-"+i,"explain","fixture","word "+i,"An explanation.","","{}","mock","prompt",new DateTime(2026,2,1,0,0,0,DateTimeKind.Utc).AddSeconds(i).ToString("o"),"complete","");
                    store.Run("COMMIT");
                } catch { store.Run("ROLLBACK"); throw; }
                Check(store.Queries("",true,1).Single().Id==first.Id,"Saved filter runs before the row limit and includes older favorites");
                store.RecordSelection(new Lookup { Id="archive-only",Kind="explain",Source="release-selection",Term="选词 archive only",Created="2000-01-01T00:00:00Z",Answer="Do not export this unused content",Context="{}" });
                var all=Files.Json().Deserialize<Dictionary<string,object>>(window.ReleaseHistoryJson(false));
                var saved=Files.Json().Deserialize<Dictionary<string,object>>(window.ReleaseHistoryJson(true));
                Check(((ArrayList)all["records"]).Count==521 && ((ArrayList)saved["records"]).Count==1,"JSON export includes all records beyond the visible 500-row page");
                Check((string)all["format"]=="termlens-history" && Convert.ToInt32(all["version"])==2,"Export has a portable versioned JSON envelope with long-term selections");
                var allEvents=(ArrayList)all["selection_events"]; var savedEvents=(ArrayList)saved["selection_events"];
                Check(allEvents.Count==2 && savedEvents.Count==1 && allEvents.Cast<Dictionary<string,object>>().Any(e=>(string)e["id"]=="archive-only"),"All-history export retains archive-only selections while favorites export respects its scope");
                Check(allEvents.Cast<Dictionary<string,object>>().All(e=>e.Count==8 && !e.ContainsKey("answer") && !e.ContainsKey("context") && !e.ContainsKey("prompt")),"Permanent selection export contains only lightweight event metadata");
                Check(!all.ContainsKey("apiKey") && !all.ContainsKey("settings"),"Export does not contain connection settings or API credentials");
                window.TopMost=true; Foreground(window); window.Size=window.MinimumSize;
                for(int theme=0;theme<2;theme++) {
                    Theme.TestDark=theme==0; Theme.Refresh(); Theme.Apply(window);
                    for(int i=0;i<pages.Count;i++) {
                        typeof(MainWindow).GetMethod("SelectReleasePage",Private).Invoke(window,new object[]{i});
                        await Task.Delay(70); CheckSettingsActions(pages[i],"Release page "+i);
                        if(i==1) {
                            var editor=Field<TextBox>(window,"prompt"); CheckSettingsEditor(editor,3,"Release prompt");
                            string savedPrompt=editor.Text;
                            editor.Text=String.Join("\r\n",Enumerable.Range(0,100).Select(n=>"Prompt line "+n)); editor.Focus(); editor.Select(0,0); editor.ScrollToCaret();
                            Native.PostMessage(editor.Handle,0x20A,new IntPtr(unchecked((int)0xfc400000)),IntPtr.Zero); await Task.Delay(120);
                            Check(SendMessage(editor.Handle,0xCE,IntPtr.Zero,IntPtr.Zero).ToInt32()>0,"Prompt editor mouse wheel scrolls through a long editable prompt");
                            editor.Select(editor.TextLength,0); editor.ScrollToCaret();
                            Check(SendMessage(editor.Handle,0xCE,IntPtr.Zero,IntPtr.Zero).ToInt32()>30,"Prompt editor can reach its final lines");
                            var host=(ReleasePromptScrollHost)editor.Parent.Parent; host.SyncScroll();
                            Check(host.Scrollbar.Value>0,"Prompt slim scrollbar follows the native editor position");
                            string beforeScroll=editor.Text; int selection=editor.SelectionStart;
                            host.Scrollbar.Value=0;
                            Check(SendMessage(editor.Handle,0xCE,IntPtr.Zero,IntPtr.Zero).ToInt32()==0 && editor.Text==beforeScroll && editor.SelectionStart==selection,"Dragging the slim scrollbar returns to the opening without changing text or selection");
                            editor.Text=savedPrompt; editor.Select(0,0); editor.ScrollToCaret();
                        }
                        if(i==3) Check(!pages[i].VerticalScroll.Visible,"Shortcut capture and startup settings fit the minimum window without scrolling");
                        if(i==2) {
                            var provider=Field<ComboBox>(window,"releaseSpeechProvider"); var voices=Field<ComboBox>(window,"releaseInstalledVoice");
                            provider.SelectedIndex=2;
                            Field<CheckBox>(window,"releasePreload").Checked=true; Field<CheckBox>(window,"releaseSaveAudio").Checked=true;
                            var onlineSettings=window.ReadReleaseSettings();
                            Check(onlineSettings.SpeechProvider=="microsoft" && onlineSettings.SpeechVoice.StartsWith("en-") && onlineSettings.SpeechPreload && onlineSettings.SpeechSaveWithFavorites,"Microsoft voice, preloading and saving favorite audio are selectable and persisted");
                            if(voices.Items.Count>1) {
                                provider.SelectedIndex=1; voices.SelectedIndex=1; await Task.Delay(60);
                                var voiceSettings=window.ReadReleaseSettings();
                                Check(voiceSettings.SpeechProvider=="windows" && voiceSettings.SpeechVoice==Convert.ToString(voices.SelectedItem),"Windows voice setting retains an actual installed voice name");
                                Check(!pages[i].VerticalScroll.Visible,"Windows voice controls fit at the minimum settings size without an extra page scrollbar");
                                CheckSettingsActions(pages[i],"Windows voice settings");
                                SettingsShot(window,Path.Combine(folder,"windows-voice-"+theme+".png"));
                                provider.SelectedIndex=0;
                            }
                        }
                        if(i==4) {
                            CheckSettingsEditor(Field<RichTextBox>(window,"historyAnswer"),2,"Release history preview");
                            var list=Field<ListBox>(window,"list"); var scroll=Field<SlimScrollBar>(window,"releaseListScroll");
                            list.Focus(); Native.PostMessage(list.Handle,0x100,new IntPtr((int)Keys.End),IntPtr.Zero); await Task.Delay(160);
                            Check(list.SelectedIndex==list.Items.Count-1 && scroll.Value==list.TopIndex,"History End key reaches the last record and synchronizes the slim scrollbar");
                            scroll.Value=0; Check(list.TopIndex==0,"Dragging the history scrollbar back to its start updates the native list");
                            Native.PostMessage(list.Handle,0x20A,new IntPtr(unchecked((int)0xff880000)),IntPtr.Zero); await Task.Delay(160);
                            Check(list.TopIndex>0 && scroll.Value==list.TopIndex,"Native mouse-wheel scrolling remains available with the custom history scrollbar");
                            scroll.Value=0; list.SelectedIndex=0;
                        }
                        SettingsShot(window,Path.Combine(folder,"page-"+i+"-"+(theme==0?"dark":"light")+".png"));
                    }
                }
                window.DeleteReleaseHistoryItem(first);
                Check(!store.SelectionEvents().Any(e=>e.Id==first.Id) && store.SelectionEvents().Any(e=>e.Id=="archive-only"),"Explicit deletion removes its selection event while unrelated long-term selections remain");
                Check(!MainWindow.CanFavoriteReleaseLookup(new Lookup { Status="failed",Answer="Partial answer" }),"Failed or incomplete records cannot be saved as favorites");
                Check(store.Queries("perpendicular",false,500).Count==0 && store.Favorites().Count==0,"Deleting a record removes its saved item and query");
                File.WriteAllText(Path.Combine(folder,"export-sample.json"),window.ReleaseHistoryJson(false),Files.Utf8);
            } catch(Exception e) { results.Add("FAIL: "+e); exit=1; try { SettingsShot(window,Path.Combine(folder,"failure.png")); } catch { } }
            finally {
                Theme.TestDark=null; Theme.Refresh();
                File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8);
                typeof(MainWindow).GetField("exiting",Private).SetValue(window,true); window.Close();
            }
        };
        Application.Run(window); window.Dispose(); return exit;
    }
    static void CheckReleaseEdgeSettings(MainWindow window,string folder) {
        var provider=Field<ComboBox>(window,"releaseSpeechProvider");
        var voices=Field<ComboBox>(window,"releaseMicrosoftVoice");
        var settingsField=typeof(MainWindow).GetField("settings",Private);
        var original=Field<Settings>(window,"settings");
        try {
            Check(provider.Items.Count==4 && Convert.ToString(provider.Items[0])=="百度在线朗读" && Convert.ToString(provider.Items[1])=="Windows 系统语音" && Convert.ToString(provider.Items[2])=="微软在线（实验）" && Convert.ToString(provider.Items[3])=="Edge TTS（在线）","Edge is appended without changing the existing provider indices");
            provider.SelectedIndex=3;
            Check(window.ReadReleaseSettings().SpeechVoice=="en-US-AndrewMultilingualNeural","First Edge selection uses the Andrew multilingual voice");
            Check(voices.Items.Contains("en-US-EmmaMultilingualNeural") && voices.Items.Contains("en-US-AriaNeural") && voices.Items.Contains("en-US-GuyNeural") && voices.Items.Contains("en-US-JennyNeural") && voices.Items.Contains("en-GB-SoniaNeural"),"Edge offers multilingual and existing English voices");
            string fixtureRoot=Path.Combine(folder,"edge-roundtrip"); Directory.CreateDirectory(fixtureRoot);
            var fixture=Files.Json().Deserialize<Settings>(Files.Json().Serialize(original));
            fixture.SpeechProvider="edge"; fixture.SpeechVoice="en-US-EmmaMultilingualNeural"; fixture.SpeechRate=-15;
            fixture.SpeechProxy="http://localhost:19876"; fixture.UseSystemProxy=false;
            fixture.SpeechPreload=true; fixture.SpeechSaveWithFavorites=true;
            Files.Save(fixtureRoot,fixture);
            settingsField.SetValue(window,Files.Load(fixtureRoot)); window.ApplyReleaseSettings();
            Check(provider.SelectedIndex==3 && Convert.ToString(voices.SelectedItem)==fixture.SpeechVoice,"Saved Edge provider and voice are restored to the UI");
            Field<NumericUpDown>(window,"releaseRate").Value=20;
            voices.SelectedItem="en-US-GuyNeural";
            var edited=window.ReadReleaseSettings(); Files.Save(fixtureRoot,edited);
            settingsField.SetValue(window,Files.Load(fixtureRoot)); window.ApplyReleaseSettings();
            var roundtrip=window.ReadReleaseSettings();
            Check(roundtrip.SpeechProvider=="edge" && roundtrip.SpeechVoice=="en-US-GuyNeural" && roundtrip.SpeechRate==20 && roundtrip.SpeechProxy==fixture.SpeechProxy && !roundtrip.UseSystemProxy && roundtrip.SpeechPreload && roundtrip.SpeechSaveWithFavorites,"Edge UI edits survive save/load with voice, rate, proxy, preloading and favorite-audio options intact");
            fixture.SpeechVoice=""; settingsField.SetValue(window,fixture); window.ApplyReleaseSettings();
            Check(window.ReadReleaseSettings().SpeechVoice=="en-US-AndrewMultilingualNeural","An Edge profile with no voice receives the Andrew default");
            fixture.SpeechVoice="en-US-FutureFixtureNeural"; settingsField.SetValue(window,fixture); window.ApplyReleaseSettings();
            Check(window.ReadReleaseSettings().SpeechVoice==fixture.SpeechVoice,"An existing custom Edge voice survives UI load and save");
        } finally {
            settingsField.SetValue(window,original); window.ApplyReleaseSettings();
        }
    }
}
}
