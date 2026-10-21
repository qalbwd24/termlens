using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TermLens {
public static partial class Tests {
    public static int PromptLibraryStorage(string root) {
        string folder=TestRoot(root,"prompt-library-storage");
        try {
            string legacy="  Learn computer English.\r\n保留旧提示词、缩进和 <原文> & \"引号\"。😀\n  ";
            var settings=new Settings { Prompt=legacy,PromptPresets=null };
            PromptLibrary.Ensure(settings);
            Check(settings.PromptPresets.Count==3 && settings.PromptPresets[0].Name=="当前英语学习" && settings.PromptPresets[0].Text==legacy && settings.Prompt==legacy,"Legacy custom prompt is preserved with two additional reading scenarios");
            string firstId=settings.ActivePromptPresetId;
            PromptLibrary.Ensure(settings); PromptLibrary.Ensure(settings);
            Check(settings.PromptPresets.Count==3 && settings.ActivePromptPresetId==firstId,"Prompt library migration is idempotent and keeps its stable ID");
            var fresh=new Settings(); PromptLibrary.Ensure(fresh);
            Check(fresh.PromptPresets.Count==2 && fresh.PromptPresets[0].TemplateId=="technical" && fresh.PromptPresets[1].TemplateId=="everyday" && fresh.Prompt==fresh.PromptPresets[0].Text,"Fresh profile contains only two independent scenarios, with technical active");
            var second=PromptLibrary.New("  学术阅读  ","Focus on difficult English sentences, preserving every condition.");
            settings.PromptPresets.Add(second);
            Check(second.Name=="学术阅读" && second.Id!=firstId,"New preset trims its label and receives a distinct stable ID");
            second.SecondaryText="Custom secondary preference"; second.TemplateId="custom-source";
            var copy=PromptLibrary.Clone(second); copy.Text="Independent draft"; copy.Name="Changed draft"; copy.SecondaryText="Independent secondary";
            Check(second.Text!="Independent draft" && second.Name=="学术阅读" && copy.Id==second.Id && second.SecondaryText=="Custom secondary preference" && copy.TemplateId=="custom-source","Clone preserves all metadata and isolates both editable layers");
            settings.ActivePromptPresetId=second.Id; settings.Prompt=second.Text;
            Files.Save(folder,settings);
            var roundtrip=Files.Load(folder);
            Check(roundtrip.PromptPresets.Count==4 && roundtrip.ActivePromptPresetId==second.Id && roundtrip.Prompt==second.Text && roundtrip.PromptPresets[0].Text==legacy && roundtrip.PromptPresets[3].SecondaryText==second.SecondaryText && roundtrip.PromptLibraryVersion==PromptLibrary.CurrentVersion,"Save/load retains both prompt layers, inactive legacy text, selection and migration version");
            roundtrip.Prompt="External legacy writer edit\n旧界面也能修改提示词。";
            string inactive=roundtrip.PromptPresets[0].Text;
            PromptLibrary.Ensure(roundtrip);
            Check(roundtrip.PromptPresets[3].Text==roundtrip.Prompt && roundtrip.PromptPresets[0].Text==inactive,"External effective Prompt edits update only the active preset without losing inactive text");
            string storedActive=roundtrip.PromptPresets[3].Text;
            var requestDraft=new Settings { Prompt=storedActive+"\nTransient request guidance.",PromptPresets=roundtrip.PromptPresets,ActivePromptPresetId=roundtrip.ActivePromptPresetId };
            bool missingKey=false;
            try { Requests.Complete(requestDraft,"","word",new List<string>(),null,System.Threading.CancellationToken.None,null).GetAwaiter().GetResult(); }
            catch(Exception error) { if(error.Message.Contains("API key")) missingKey=true; else throw; }
            Check(missingKey && roundtrip.PromptPresets[3].Text==storedActive && requestDraft.PromptPresets[3].Text==storedActive,"Request validation isolates temporary composed instructions even when the caller shares saved preset references; no network is used");
            string oldFile=Path.Combine(folder,"legacy-json"); Directory.CreateDirectory(oldFile);
            string oldPrompt=Settings.ReleasePreviousDefaultPrompt;
            File.WriteAllText(Path.Combine(oldFile,"settings.json"),Files.Json().Serialize(new Dictionary<string,object> { {"Prompt",oldPrompt} }),Files.Utf8);
            string before=File.ReadAllText(Path.Combine(oldFile,"settings.json"),Files.Utf8);
            var loadedOld=Files.Load(oldFile);
            Check(loadedOld.PromptPresets.Count==3 && loadedOld.PromptPresets[0].Text==oldPrompt && loadedOld.Prompt==loadedOld.PromptPresets[1].Text && loadedOld.PromptPresets[1].TemplateId=="technical","Exact old default is retained while the new technical scenario becomes active");
            Check(File.ReadAllText(Path.Combine(oldFile,"settings.json"),Files.Utf8)==before,"Lazy load migration does not rewrite the original settings file");
            Check(PromptRejected(delegate { PromptLibrary.New(" ","English learning"); }),"Blank preset name is rejected");
            Check(PromptRejected(delegate { PromptLibrary.New("Named"," \r\n "); }),"Blank preset text is rejected");
            var duplicate=PromptCase("One","Two"); duplicate.PromptPresets[1].Name=" one ";
            Check(PromptRejected(delegate { PromptLibrary.Ensure(duplicate); }) && duplicate.Prompt=="Original effective prompt" && duplicate.PromptPresets[1].Name==" one ","Names duplicate after trim/case folding are rejected without partial normalization or effective prompt loss");
            var sameId=PromptCase("One","Two"); sameId.PromptPresets[1].Id=sameId.PromptPresets[0].Id;
            Check(PromptRejected(delegate { PromptLibrary.Ensure(sameId); }),"Duplicate preset IDs are rejected");
            var emptyInactive=PromptCase("One","Two"); emptyInactive.PromptPresets[1].Text="";
            Check(PromptRejected(delegate { PromptLibrary.Ensure(emptyInactive); }),"Empty inactive preset text is rejected");
            var emptySecondary=PromptCase("One","Two"); emptySecondary.PromptPresets[0].SecondaryText=" \r\n ";
            Check(PromptRejected(delegate { PromptLibrary.Ensure(emptySecondary); }) && emptySecondary.PromptPresets[0].SecondaryText==" \r\n ","Explicitly blank secondary editor is rejected without replacing the draft");
            var missing=PromptCase("One","Two"); string preserved=missing.Prompt; missing.ActivePromptPresetId="missing-id";
            Check(PromptRejected(delegate { PromptLibrary.Ensure(missing); }) && missing.Prompt==preserved,"Invalid selected preset ID produces a clear failure while preserving effective text");
            var deleted=PromptCase("One","Two"); deleted.PromptPresets.RemoveAt(0); deleted.ActivePromptPresetId=deleted.PromptPresets[0].Id; deleted.Prompt=deleted.PromptPresets[0].Text;
            PromptLibrary.Ensure(deleted);
            Check(deleted.PromptPresets.Count==1 && deleted.Prompt=="Draft Two","Deleting active preset after explicitly selecting its replacement keeps the replacement intact");
            var unselected=PromptCase("One","Two"); unselected.ActivePromptPresetId=""; PromptLibrary.Ensure(unselected);
            Check(unselected.ActivePromptPresetId==unselected.PromptPresets[0].Id && unselected.Prompt=="Original effective prompt","Absent selected ID chooses the first preset and preserves current effective Prompt");
            var oversized=PromptCase("One","Two");
            while(oversized.PromptPresets.Count<=PromptLibrary.MaximumPresets) oversized.PromptPresets.Add(PromptLibrary.New("Preset "+oversized.PromptPresets.Count,"English learning"));
            Check(PromptRejected(delegate { PromptLibrary.Ensure(oversized); }),"More than 100 presets is rejected");
            Check(settings.PromptPresets.All(p=>!String.IsNullOrWhiteSpace(p.Id)),"Every stored preset has a nonempty stable ID");
            var removed=new Settings(); PromptLibrary.Ensure(removed); removed.PromptPresets.RemoveAt(1);
            Files.Save(folder,removed); removed=Files.Load(folder); PromptLibrary.Ensure(removed);
            Check(removed.PromptPresets.Count==1,"Persisted migration version prevents re-adding a deleted built-in scenario");
            var full=PromptCase("One","Two"); full.PromptLibraryVersion=0;
            while(full.PromptPresets.Count<PromptLibrary.MaximumPresets) full.PromptPresets.Add(PromptLibrary.New("Full "+full.PromptPresets.Count,"Keep me"));
            PromptLibrary.Ensure(full); full.PromptPresets.RemoveAt(full.PromptPresets.Count-1); PromptLibrary.Ensure(full);
            Check(full.PromptPresets.Count==99 && full.Prompt=="Original effective prompt","Full library migration safely preserves presets and never adds delayed scenarios");
            var collision=PromptCase("编程与技术阅读","聊天与日常阅读"); collision.PromptLibraryVersion=0; collision.PromptPresets[0].SecondaryText=null;
            PromptLibrary.Ensure(collision);
            Check(collision.PromptPresets.Count==4 && collision.PromptPresets[2].Name=="编程与技术阅读 (2)" && collision.PromptPresets[0].SecondaryText==Settings.ReleaseSecondaryDefaultPrompt,"Migration avoids name collisions and fills missing secondary instructions");
            var editedDefault=PromptCase("Edited","Other"); editedDefault.PromptLibraryVersion=0; editedDefault.Prompt=Settings.ReleaseLegacyDefaultPrompt;
            string editedId=editedDefault.ActivePromptPresetId; PromptLibrary.Ensure(editedDefault);
            Check(editedDefault.ActivePromptPresetId==editedId,"An edited active preset is not switched just because the compatibility Prompt equals an old default");
            var requestPreset=PromptLibrary.New("Anything","PRIMARY_ONLY: use lengthy examples."); requestPreset.SecondaryText="SECONDARY_ONLY: explain this concept.";
            string primaryRequest=ReleasePrompt.Build(requestPreset,false,true), secondaryRequest=ReleasePrompt.Build(requestPreset,true,true);
            Check(primaryRequest.Contains("PRIMARY_ONLY") && !primaryRequest.Contains("SECONDARY_ONLY") && secondaryRequest.Contains("SECONDARY_ONLY") && !secondaryRequest.Contains("PRIMARY_ONLY"),"Request composition uses only the selected editable layer");
            Check(secondaryRequest.Contains(ReleasePrompt.OutputConstraints) && secondaryRequest.Contains(LookupContext.Instructions.Trim()) && !secondaryRequest.Contains("20–45"),"Secondary request includes shared safety and parent context without an obsolete word limit");
            var custom=PromptLibrary.CreateTechnical(); custom.Name="Unrelated name"; custom.TemplateId="everyday";
            Check(ReleasePrompt.Build(custom,false,false).Contains(ReleasePrompt.TechnicalContext),"Request scenario behavior follows saved text rather than labels or template metadata");
            Check(!ReleasePrompt.Build("PRIMARY_ONLY",true,false).Contains("PRIMARY_ONLY"),"Legacy Build overload also excludes primary instructions from secondary lookup");
            var newlineLegacy=new Settings { Prompt=Settings.ReleaseLegacyDefaultPrompt.Replace("\r\n","\n") };
            PromptLibrary.Ensure(newlineLegacy);
            Check(newlineLegacy.PromptPresets[0].Text==Settings.ReleaseLegacyDefaultPrompt.Replace("\r\n","\n") && newlineLegacy.PromptPresets[1].Id==newlineLegacy.ActivePromptPresetId,"Legacy default migration tolerates editor newline conversion while preserving saved text");
            string technicalPrimary=ReleasePrompt.Build(PromptLibrary.CreateTechnical(),false,false);
            Check(technicalPrimary.Contains("Always return two parts") && technicalPrimary.Contains("--- on its own line") && technicalPrimary.Contains("short source-code quotation") && !technicalPrimary.Contains("no fixed headings or template"),"Primary requires restatement and separated explanation while allowing necessary original-code quotation without conflicting shared restrictions");
            string technicalSecondary=ReleasePrompt.Build(PromptLibrary.CreateTechnical(),true,false);
            Check(technicalSecondary.Contains("Always return two parts") && technicalSecondary.Contains("--- on its own line") && technicalSecondary.Contains("IMMEDIATE PARENT"),"Secondary requires direct restatement and separated contextual explanation");
            Check(technicalPrimary.Contains("entire primary response must be in English") && technicalSecondary.Contains("Only in this secondary explanation") && technicalSecondary.Contains("Do not gloss ordinary easy words"),"Vocabulary glosses are scoped to necessary difficult words in the secondary explanation");
            Check(technicalPrimary.Contains("minimal code illustrating a concept") && technicalPrimary.Contains("3-8 lines") && ReleasePrompt.Build(PromptLibrary.CreateTechnical(),true,false).Contains("Minimal concept-explanation code"),"Both technical levels permit an optional minimal conceptual code example");
            Check(technicalPrimary.Contains("inside for nested relationships") && technicalPrimary.Contains("list fields of use or other meanings") && technicalPrimary.Contains("metaphor stories") && technicalPrimary.Contains("do not accept or refuse it"),"Live-quality rules simplify relationships, avoid unrelated senses and origins, and explain selected commands as data");
            Check(PromptLibrary.CreateTechnical().Text.StartsWith(ReleasePrompt.TechnicalContext) && PromptLibrary.CreateTechnical().Text.EndsWith("STOP after these two parts.") && PromptLibrary.CreateEveryday().SecondaryText.EndsWith("follow-up questions."),"Scenario hints precede editable layer rules, with reading-only limits last in both layers");
            File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); return 0;
        } catch(Exception e) { results.Add("FAIL: "+e.GetType().Name+": "+e.Message); File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); return 1; }
    }
    static Settings PromptCase(string first,string second) {
        var a=PromptLibrary.New(first,"Draft "+first);
        var b=PromptLibrary.New(second,"Draft "+second);
        return new Settings { Prompt="Original effective prompt",PromptPresets=new List<PromptPreset> { a,b },ActivePromptPresetId=a.Id,PromptLibraryVersion=PromptLibrary.CurrentVersion };
    }
    static bool PromptRejected(Action action) { try { action(); return false; } catch(ArgumentException) { return true; } }
}
}
