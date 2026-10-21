using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace TermLens {
public static class ReleaseAudioButtonTests {
    static T Field<T>(object target,string name) { return (T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target); }
    public static int Run(string root) {
        string folder=Path.Combine(root,"tests","release-audio-buttons-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")); Directory.CreateDirectory(folder);
        var report=new List<string>(); int exit=0;
        Action<bool,string> check=(ok,label)=> { if(!ok) throw new Exception(label); report.Add("PASS: "+label); };
        var window=new ReleaseLookupWindow(new Settings { ReadingWidth=440,ReadingHeight=370,ReadingTopMost=false });
        window.Shown+=delegate {
            try {
                var lookup=new Lookup { Id="audio-buttons",Term="first second",Answer="clear answer",Status="complete",Context="{}" }; window.ShowLookup(lookup,null);
                var original=Field<SoftButton>(window,"readOriginal"); var answer=Field<SoftButton>(window,"readAnswer");
                int requests=0,cancels=0; string last=null; bool preparingVisible=false,preparingNotice=false;
                window.SpeechRequested+=text=> { requests++; last=text; preparingVisible=original.IconName=="stop" || answer.IconName=="stop"; preparingNotice=Field<Label>(window,"notice").Text==UiLanguage.T("Preparing audio…","正在准备音频…"); };
                window.CancelSpeechRequested+=()=> { cancels++; window.SetSpeaking(false,null); };
                check(original.IconName=="listen" && answer.IconName=="listen","Both audio controls start with listen icons");
                lookup.Status="pending"; window.RefreshActions();
                check(original.Enabled && !answer.Enabled,"Original remains playable while incomplete answers wait for their full text");
                lookup.Status="complete"; window.RefreshActions();
                window.Original.Select(6,6); original.PerformClick();
                check(preparingVisible && original.IconName=="stop" && original.Primary && answer.IconName=="listen" && !answer.Primary,"Original button switches to the only active stop icon before speech preparation starts");
                check(requests==1 && last=="second","Original playback preserves selected-words behavior");
                check(preparingNotice,"Preparing status is visible before entering the audio callback");
                window.MarkPlaybackStarted();
                check(Field<Label>(window,"notice").Text==UiLanguage.T("Reading aloud…","正在朗读…") && original.IconName=="stop","Actual playback callback changes preparation text without changing the active stop control");
                int invalidations=0; InvalidateEventHandler counted=(sender,eventArgs)=>invalidations++;
                var historyButton=Field<SoftButton>(window,"historyButton"); var favoriteButton=Field<SoftButton>(window,"save");
                original.Invalidated+=counted; answer.Invalidated+=counted; historyButton.Invalidated+=counted; favoriteButton.Invalidated+=counted;
                for(int i=0;i<25;i++) window.RefreshActions();
                original.Invalidated-=counted; answer.Invalidated-=counted; historyButton.Invalidated-=counted; favoriteButton.Invalidated-=counted;
                check(invalidations==0,"Unchanged action state does not invalidate audio, history or favorite icons repeatedly");
                original.PerformClick(); check(cancels==1 && requests==1 && original.IconName=="listen" && !original.Primary,"Second click cancels original playback without requesting synthesis again");
                string canceledNotice=Field<Label>(window,"notice").Text; window.MarkPlaybackStarted();
                check(Field<Label>(window,"notice").Text==canceledNotice,"A late start notification cannot restore playback status after cancellation");
                window.Answer.Select(0,5); answer.PerformClick();
                check(last=="clear" && answer.IconName=="stop" && original.IconName=="listen","Answer playback uses selected answer words and its own stop icon");
                window.SetSpeaking(true,"Preparing"); check(answer.IconName=="stop","Asynchronous preparation state retains the requested answer role");
                window.SetSpeaking(false,"Finished"); check(answer.IconName=="listen" && !answer.Primary,"Playback completion restores listen state");
                answer.PerformClick(); window.SetSpeaking(false,"Failed"); check(answer.IconName=="listen" && original.IconName=="listen","Playback failure restores both buttons");
                original.PerformClick(); int before=cancels; answer.PerformClick();
                check(cancels==before+1 && answer.IconName=="stop" && answer.Primary && original.IconName=="listen" && !original.Primary,"Switching from original to answer cancels old playback and leaves only answer active");
                answer.PerformClick(); check(answer.IconName=="listen" && original.IconName=="listen","Second answer click cancels and resets the controls");
                window.Original.Select(0,0); original.PerformClick(); check(last==lookup.Term,"No selection reads the complete original text");
                window.ShowLookup(new Lookup { Id="new",Term="new selection",Answer="new explanation",Status="complete",Context="{}" },null);
                check(original.IconName=="listen" && answer.IconName=="listen" && !original.Primary && !answer.Primary,"Showing a new lookup resets both playback buttons");
                window.HistoryProvider=()=>new List<Lookup>{lookup}; answer.PerformClick(); window.ToggleHistory();
                check(window.IsHistoryList && original.IconName=="listen" && answer.IconName=="listen","Entering history resets preparing or active audio state");
                window.ShowHistoryDetail(lookup); answer.PerformClick(); window.ToggleHistory(); window.RestoreOriginalLookup();
                check(original.IconName=="listen" && answer.IconName=="listen","Returning from historical playback never carries its stop state into the original query");
                report.Add("INFO: Only UI callbacks were exercised; no network, audio synthesis, playback or global hotkeys.");
            } catch(Exception error) { report.Add("FAIL: "+error); exit=1; }
            finally { File.WriteAllLines(Path.Combine(folder,"results.txt"),report,Files.Utf8); window.Close(); }
        };
        Application.Run(window); window.Dispose(); return exit;
    }
}
}
