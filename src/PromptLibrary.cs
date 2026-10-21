using System;
using System.Collections.Generic;

namespace TermLens {
public sealed class PromptPreset {
    public string Id,Name,Text,SecondaryText,TemplateId;
}
// The existing Prompt remains the effective request text. Library migration and
// validation preserve it exactly, including edits made through older code paths.
public static class PromptLibrary {
    public const int MaximumPresets=100;
    public const int CurrentVersion=1;
    public static PromptPreset New(string name,string text) {
        return new PromptPreset { Id=Guid.NewGuid().ToString("N"),Name=ValidName(name),Text=ValidText(text),SecondaryText=Settings.ReleaseSecondaryDefaultPrompt };
    }
    public static PromptPreset CreateTechnical() { return CreateScenario("编程与技术阅读","technical",ReleasePrompt.TechnicalContext); }
    public static PromptPreset CreateEveryday() { return CreateScenario("聊天与日常阅读","everyday",ReleasePrompt.EverydayContext); }
    static PromptPreset CreateScenario(string name,string template,string context) {
        var preset=New(name,context+Environment.NewLine+Settings.ReleaseDefaultPrompt);
        preset.SecondaryText=context+Environment.NewLine+Settings.ReleaseSecondaryDefaultPrompt;
        preset.TemplateId=template; return preset;
    }
    public static PromptPreset Clone(PromptPreset preset) {
        if(preset==null) throw new ArgumentNullException("preset");
        return new PromptPreset { Id=preset.Id,Name=preset.Name,Text=preset.Text,SecondaryText=preset.SecondaryText,TemplateId=preset.TemplateId };
    }
    public static void Ensure(Settings settings) {
        if(settings==null) throw new ArgumentNullException("settings");
        string current=ValidText(settings.Prompt);
        var presets=settings.PromptPresets;
        if(presets==null || presets.Count==0) {
            if(settings.PromptLibraryVersion==0 && String.Equals(current,Settings.ReleaseDefaultPrompt,StringComparison.Ordinal)) {
                var technical=CreateTechnical(); var everyday=CreateEveryday();
                settings.PromptPresets=new List<PromptPreset> { technical,everyday };
                settings.ActivePromptPresetId=technical.Id; settings.Prompt=technical.Text;
                settings.PromptLibraryVersion=CurrentVersion; return;
            }
            var first=New("当前英语学习",current);
            settings.PromptPresets=new List<PromptPreset> { first };
            settings.ActivePromptPresetId=first.Id;
            presets=settings.PromptPresets;
        }
        if(presets.Count>MaximumPresets) throw new ArgumentException("提示词存档最多保存 100 套，请删除不需要的存档。","PromptPresets");
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids=new HashSet<string>(StringComparer.Ordinal);
        var normalizedNames=new List<string>();
        PromptPreset active=null;
        string selected=settings.ActivePromptPresetId;
        bool chooseFirst=String.IsNullOrWhiteSpace(selected);
        foreach(var preset in presets) {
            if(preset==null) throw new ArgumentException("提示词存档不能包含空记录。","PromptPresets");
            string name=ValidName(preset.Name);
            if(!names.Add(name)) throw new ArgumentException("提示词存档名称重复："+name+"。请使用不同的名称。","PromptPresets");
            if(String.IsNullOrWhiteSpace(preset.Id) || !ids.Add(preset.Id)) throw new ArgumentException("提示词存档编号为空或重复，请重新创建该存档。","PromptPresets");
            normalizedNames.Add(name);
            if((chooseFirst && active==null) || String.Equals(selected,preset.Id,StringComparison.Ordinal)) active=preset;
        }
        if(active==null) throw new ArgumentException("当前选中的提示词存档不存在，请选择仍在列表中的存档。","ActivePromptPresetId");
        foreach(var preset in presets) if(!Object.ReferenceEquals(preset,active)) ValidText(preset.Text);
        // Null is an absent field in legacy JSON. An explicitly empty editor is invalid.
        foreach(var preset in presets) if(preset.SecondaryText!=null) ValidText(preset.SecondaryText);
        bool migrate=settings.PromptLibraryVersion<CurrentVersion;
        bool selectTechnical=migrate && IsOldDefault(current) && String.Equals(active.Text,current,StringComparison.Ordinal);
        // Apply normalization only after every record has passed validation.
        for(int i=0;i<presets.Count;i++) presets[i].Name=normalizedNames[i];
        foreach(var preset in presets) if(preset.SecondaryText==null) preset.SecondaryText=Settings.ReleaseSecondaryDefaultPrompt;
        settings.ActivePromptPresetId=active.Id;
        if(!String.Equals(active.Text,current,StringComparison.Ordinal)) active.Text=current;
        if(migrate) {
            var technical=AddScenario(presets,CreateTechnical(),names);
            AddScenario(presets,CreateEveryday(),names);
            if(selectTechnical && technical!=null) { settings.ActivePromptPresetId=technical.Id; settings.Prompt=technical.Text; }
            // Mark even a full library: migration must never re-add a later-deleted preset.
            settings.PromptLibraryVersion=CurrentVersion;
        }
    }
    static bool IsOldDefault(string text) {
        string normalized=(text??"").Replace("\r\n","\n");
        return String.Equals(normalized,Settings.ReleaseLegacyDefaultPrompt.Replace("\r\n","\n"),StringComparison.Ordinal) || String.Equals(normalized,Settings.ReleasePreviousDefaultPrompt.Replace("\r\n","\n"),StringComparison.Ordinal);
    }
    static PromptPreset AddScenario(List<PromptPreset> presets,PromptPreset candidate,HashSet<string> names) {
        foreach(var preset in presets) if(String.Equals(preset.TemplateId,candidate.TemplateId,StringComparison.Ordinal)) return preset;
        if(presets.Count>=MaximumPresets) return null;
        string basis=candidate.Name; int number=2;
        while(!names.Add(candidate.Name)) candidate.Name=basis+" ("+(number++)+")";
        presets.Add(candidate); return candidate;
    }
    static string ValidName(string name) {
        if(String.IsNullOrWhiteSpace(name)) throw new ArgumentException("请填写提示词存档名称。","name");
        return name.Trim();
    }
    static string ValidText(string text) {
        if(String.IsNullOrWhiteSpace(text)) throw new ArgumentException("提示词正文不能为空。","text");
        return text;
    }
}
}
