using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace TermLens {
public static partial class Tests {
    static async Task ReleaseSelectAndCapture(MainWindow app,ReleaseLookupWindow window,string term) {
        Foreground(window); window.Answer.Focus();
        int at=window.Answer.Text.IndexOf(term,StringComparison.Ordinal);
        Check(at>=0,"Scenario fixture contains the next selected word");
        window.Answer.Select(at,term.Length); await Task.Delay(60);
        await WorkCall(app,"CaptureRelease",window.Handle);
    }
    static async Task ReleaseScenarioSnapshotWorkflow(MainWindow app,ReleaseRequestRig rig,HistoryStore history,Rectangle anchor) {
        var settings=Field<Settings>(app,"settings");
        var technical=new PromptPreset { Id="snapshot-tech",Name="Technical",Text="TECH_PRIMARY_UNIQUE",SecondaryText="TECH_SECONDARY_UNIQUE",TemplateId="technical" };
        var everyday=new PromptPreset { Id="snapshot-chat",Name="Chat",Text="CHAT_PRIMARY_UNIQUE",SecondaryText="CHAT_SECONDARY_UNIQUE",TemplateId="everyday" };
        settings.PromptPresets=new List<PromptPreset> { technical,everyday };
        settings.PromptLibraryVersion=PromptLibrary.CurrentVersion;
        settings.ActivePromptPresetId=technical.Id; settings.Prompt=technical.Text;
        int start=rig.Bodies.Count;
        await app.RunReleaseLookup("technical-primary",null,anchor);
        var main=Field<ReleaseLookupWindow>(app,"releaseWindow");
        var original=main.Current;
        var frozen=ReleasePromptSnapshot.FromSaved(original.Context);
        Check(frozen!=null && frozen.Id==technical.Id && frozen.Text==technical.Text && frozen.SecondaryText==technical.SecondaryText,"Primary history freezes both prompts and the selected scene identity");
        Check(ReleaseMessage(rig.Bodies[start],0).StartsWith(technical.Text) && !ReleaseMessage(rig.Bodies[start],0).Contains(technical.SecondaryText),"Primary request sends only its selected primary prompt");
        Check(!ReleaseMessage(rig.Bodies[start],1).Contains("promptProfile") && !ReleaseMessage(rig.Bodies[start],1).Contains(technical.SecondaryText),"Scene snapshot metadata is absent from primary user content");

        settings.ActivePromptPresetId=everyday.Id; settings.Prompt=everyday.Text;
        await ReleaseSelectAndCapture(app,main,"simple");
        var child=Field<ReleaseLookupWindow>(app,"releaseSecondaryWindow");
        string firstChildContext=child.Current.Context;
        Check(ReleaseMessage(rig.Bodies[start+1],0).StartsWith(technical.SecondaryText) && !ReleaseMessage(rig.Bodies[start+1],0).Contains(everyday.Text),"Switching active scenes leaves a visible answer's secondary prompt frozen");
        Check(ReleasePromptSnapshot.FromSaved(firstChildContext).Id==technical.Id && !ReleaseMessage(rig.Bodies[start+1],1).Contains("promptProfile") && !ReleaseMessage(rig.Bodies[start+1],1).Contains(technical.Text),"Child persists its inherited profile without adding profile data to model context");
        var storedChild=history.Queries("simple",false,100).First(x=>x.Id==child.Current.Id);
        Check(storedChild.Context==firstChildContext,"Stored secondary history retains the original profile snapshot");
        await ReleaseSelectAndCapture(app,child,"explanation");
        child=Field<ReleaseLookupWindow>(app,"releaseSecondaryWindow");
        Check(ReleaseMessage(rig.Bodies[start+2],0).StartsWith(technical.SecondaryText) && ReleasePromptSnapshot.FromSaved(child.Current.Context).Id==technical.Id,"Repeated secondary selections propagate the original scene snapshot");
        var chain=Files.Json().Deserialize<Dictionary<string,object>>(child.Current.Context);
        var bounded=(Dictionary<string,object>)chain["lookupContext"];
        Check(bounded.Count==6 && Convert.ToString(bounded["rootOriginal"])=="technical-primary" && Convert.ToString(bounded["parentQuery"])=="simple","Scene propagation retains only the existing bounded root and immediate parent");

        app.OpenReleaseLookup(storedChild);
        main=Field<ReleaseLookupWindow>(app,"releaseWindow");
        await ReleaseSelectAndCapture(app,main,"simple");
        child=Field<ReleaseLookupWindow>(app,"releaseSecondaryWindow");
        Check(ReleaseMessage(rig.Bodies[start+3],0).StartsWith(technical.SecondaryText) && ReleasePromptSnapshot.FromSaved(child.Current.Context).Id==technical.Id,"Restored history continues with its saved secondary scene after settings change");

        int chat=rig.Bodies.Count;
        await app.RunReleaseLookup("chat-first",null,anchor);
        Check(ReleaseMessage(rig.Bodies[chat],0).StartsWith(everyday.Text) && ReleaseMessage(rig.Bodies[chat],1)=="chat-first","A newly selected scene starts with no weak topic references from a different scene");
        await app.RunReleaseLookup("chat-second",null,anchor);
        Check(ReleaseMessage(rig.Bodies[chat+1],1).Contains("chat-first"),"Consecutive primary lookups in the same scene retain recent completed selections");
        everyday.SecondaryText="CHAT_SECONDARY_EDITED";
        await app.RunReleaseLookup("chat-edited",null,anchor);
        Check(ReleaseMessage(rig.Bodies[chat+2],1)=="chat-edited","Editing a scene's prompt content clears stale weak topic references");

        var legacy=new Lookup { Id="legacy-fixture",Term="legacy-original",Answer=rig.Answer,Context="{}",Prompt="LEGACY_COMPOSITE_MUST_NOT_BE_PARSED",Kind="explain",Status="complete" };
        app.OpenReleaseLookup(legacy); main=Field<ReleaseLookupWindow>(app,"releaseWindow");
        int legacyRequest=rig.Bodies.Count;
        await ReleaseSelectAndCapture(app,main,"simple");
        child=Field<ReleaseLookupWindow>(app,"releaseSecondaryWindow");
        var fallback=Files.Json().Deserialize<Dictionary<string,object>>(child.Current.Context);
        var fallbackProfile=(Dictionary<string,object>)fallback["promptProfile"];
        Check(ReleaseMessage(rig.Bodies[legacyRequest],0).StartsWith(everyday.SecondaryText) && !rig.Bodies[legacyRequest].Contains(legacy.Prompt) && Convert.ToBoolean(fallbackProfile["legacyFallback"]),"Legacy history safely falls back to the current scene and explicitly records fallback without parsing old prompts");
        Check(ReleasePromptSnapshot.FromSaved("{broken")==null && ReleasePromptSnapshot.FromSaved("{\"promptProfile\":{\"version\":99}}")==null,"Malformed or future snapshot formats remain safe legacy fallbacks");
        var active=ReleasePromptSnapshot.Active(settings); active.SecondaryText="mutated detached snapshot";
        Check(everyday.SecondaryText=="CHAT_SECONDARY_EDITED","Active profile snapshots are detached from mutable settings");
    }
}
}
