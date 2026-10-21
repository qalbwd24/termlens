using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace TermLens {
// Saved alongside context, never included in the model's quoted study data.
// Each child copies one fixed profile rather than a recursive request chain.
public static class ReleasePromptSnapshot {
    public static PromptPreset Active(Settings settings) {
        var copy=Files.Json().Deserialize<Settings>(Files.Json().Serialize(settings));
        PromptLibrary.Ensure(copy);
        foreach(var preset in copy.PromptPresets)
            if(preset.Id==copy.ActivePromptPresetId) return PromptLibrary.Clone(preset);
        throw new InvalidOperationException("The selected prompt archive is unavailable.");
    }
    public static PromptPreset FromSaved(string contextJson) {
        try {
            var saved=Files.Json().Deserialize<Dictionary<string,object>>(contextJson??""); object raw;
            if(saved==null || !saved.TryGetValue("promptProfile",out raw)) return null;
            var profile=raw as Dictionary<string,object>;
            if(profile==null || !profile.ContainsKey("version") || Convert.ToInt32(profile["version"])!=1) return null;
            Func<string,string> read=key=>profile.ContainsKey(key)?Convert.ToString(profile[key]):"";
            string primary=read("primaryText"),secondary=read("secondaryText");
            if(String.IsNullOrWhiteSpace(primary) || String.IsNullOrWhiteSpace(secondary)) return null;
            return new PromptPreset { Id=read("id"),Name=read("name"),Text=primary,SecondaryText=secondary,TemplateId=read("templateId") };
        } catch { return null; }
    }
    public static PromptPreset ForParent(Lookup parent,Settings settings) {
        // Legacy history has no reliable separate secondary prompt. Use today's
        // active scene, and never try to parse its old composite system prompt.
        return (parent==null?null:FromSaved(parent.Context))??Active(settings);
    }
    public static string Attach(string contextJson,PromptPreset preset,bool legacyFallback=false) {
        var snapshot=Files.Json().Deserialize<Dictionary<string,object>>(contextJson);
        snapshot["promptProfile"]=new { version=1,id=preset.Id,name=preset.Name,primaryText=preset.Text,secondaryText=preset.SecondaryText,templateId=preset.TemplateId,legacyFallback=legacyFallback };
        return Files.Json().Serialize(snapshot);
    }
    public static string Signature(PromptPreset preset) {
        // Names may be edited without changing a scene's interpretation.
        string input=Files.Json().Serialize(new { id=preset.Id,primary=preset.Text,secondary=preset.SecondaryText,template=preset.TemplateId });
        using(var hash=SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(input)));
    }
}
}
