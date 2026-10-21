using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TermLens {
public sealed partial class MainWindow {
    SpeechCache speechCache;
    string preloadGroup="";
    readonly ComboBox speechProvider=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Top,FlatStyle=FlatStyle.Flat };
    string speechServiceProvider="";
    bool refreshingSpeechLabels;
    readonly CheckBox speechPreload=new CheckBox { Text="Prepare audio",AutoSize=true };
    readonly ComboBox speechScope=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Top,FlatStyle=FlatStyle.Flat };
    readonly Label speechCacheInfo=new Label { AutoSize=true,Dock=DockStyle.Fill };
    Form speechWindow;
    Action<bool,string> speechFeedback;
    bool BaiduVoice { get { return speechProvider.SelectedIndex==0; } }
    string ActiveVoice { get { return BaiduVoice?"baidu-auto":Convert.ToString(speechVoice.SelectedItem); } }
    string ActiveVoiceProxy { get { return BaiduVoice?"":speechProxy.Text.Trim(); } }
    System.Collections.Generic.List<string> VoiceParts(string text) { return BaiduVoice?WebSpeech.Parts(text):SpeechParts(text); }
    SpeechCache GetSpeechCache(string proxyAddress) {
        string provider=BaiduVoice?"baidu-web":"microsoft";
        if(speechCache==null || speechServiceProxy!=proxyAddress || speechServiceProvider!=provider) {
            SpeechCache next; TranslatorSpeech microsoft=null;
            if(BaiduVoice) { var web=new WebSpeech(); next=new SpeechCache(web.Synthesize,web.Dispose); }
            else { microsoft=new TranslatorSpeech(root,proxyAddress); next=new SpeechCache(microsoft.Synthesize,microsoft.Dispose); }
            var previous=speechCache;
            speechService=microsoft; speechServiceProxy=proxyAddress; speechServiceProvider=provider; speechCache=next;
            if(previous!=null) previous.Dispose();
        }
        return speechCache;
    }
    string SelectedSpeechScope { get { return speechScope.SelectedIndex==0?"original":speechScope.SelectedIndex==2?"all":"polish"; } }
    void RefreshSpeechLabels() {
        refreshingSpeechLabels=true;
        int provider=speechProvider.SelectedIndex;
        speechProvider.Items.Clear(); speechProvider.Items.AddRange(new object[]{UiLanguage.T("Baidu web voice · no key","百度网页发音 · 无需密钥"),UiLanguage.T("Microsoft Translator / Windows offline","微软 Translator / Windows 离线")});
        speechProvider.SelectedIndex=provider<0?0:provider;
        int index=speechScope.SelectedIndex;
        speechScope.Items.Clear(); speechScope.Items.AddRange(new object[]{UiLanguage.T("Original only","只预加载原文"),UiLanguage.T("Original + Polish result","原文 + 润色结果"),UiLanguage.T("Original + every answer","原文 + 每次回答")});
        speechScope.SelectedIndex=index<0?2:index;
        refreshingSpeechLabels=false;
        speechVoice.Enabled=speechProxy.Enabled=!BaiduVoice;
        speechCacheInfo.Text=UiLanguage.T("Silently prepares original text and completed answers. Long text is prepared in parts.\nOne voice request at a time; up to 128 clips / 16 MiB in memory. Audio expires after 30 minutes and is cleared on exit.\nOnline preparation sends this text to the selected voice service even if you do not play it. Baidu web voice needs no key; availability depends on its website.","静默准备原文和已完成的回答；较长文本分段预加载。\n语音请求逐个执行，内存缓存最多 128 段 / 16 MiB，30 分钟后失效，退出时清空。\n启用在线预加载后，即使不播放，相应文本也会发送给所选语音服务。百度网页发音无需密钥，可用性取决于其网站。");
    }
    async void BeginSpeechPreload(string group,string text) {
        preloadGroup=group;
        if(speechCache!=null) speechCache.CancelPreloads();
        await QueueSpeechPreload(group,text);
    }
    async void PreloadSpeechAnswer(string group,WorkMode mode,string answer) {
        if(SelectedSpeechScope=="all" || (SelectedSpeechScope=="polish" && mode==WorkMode.Polish)) await QueueSpeechPreload(group,Markdown.PlainText(answer));
    }
    async Task QueueSpeechPreload(string group,string text) {
        if(exiting || group!=preloadGroup || !speechPreload.Checked || String.IsNullOrWhiteSpace(text) || (!BaiduVoice && !File.Exists(Path.Combine(root,"translator-signing.dpapi")))) return;
        try {
            string voice=ActiveVoice,proxyAddress=ActiveVoiceProxy;
            ValidateSpeech(voice,proxyAddress);
            var cache=GetSpeechCache(proxyAddress); int rate=(int)speechRate.Value;
            foreach(string part in VoiceParts(text.Trim())) {
                if(exiting || group!=preloadGroup || !speechPreload.Checked) return;
                await cache.PreloadAsync(part,voice,rate);
            }
        } catch(OperationCanceledException) { }
        catch(Exception e) { UiTrace.Write("voice preload unavailable: "+e.GetType().Name); }
    }
    void StopSpeechPreload(string group) {
        if(group==preloadGroup) { preloadGroup=""; if(speechCache!=null) speechCache.CancelPreloads(); }
    }
    void ClearSpeechCache() {
        StopSpeech(); preloadGroup=""; if(speechCache!=null) speechCache.Clear();
        SetStatus(UiLanguage.T("Audio cache cleared.","语音缓存已清空。"),false);
    }
    void DisposeSpeechCache() {
        if(speechCache!=null) { speechCache.Dispose(); speechCache=null; speechService=null; }
        else if(speechService!=null) speechService.Dispose();
    }
    async void PrepareFavoriteAudio(Favorite favorite) {
        if(favorite==null || !favorite.Saved || String.IsNullOrWhiteSpace(favorite.Term)) return;
        try {
            string voice=ActiveVoice, proxyAddress=ActiveVoiceProxy; ValidateSpeech(voice,proxyAddress);
            if(!BaiduVoice && !File.Exists(Path.Combine(root,"translator-signing.dpapi"))) { favorite.AudioState="unavailable"; history.StoreFavorite(favorite); return; }
            var cache=GetSpeechCache(proxyAddress); int rate=(int)speechRate.Value; string dir=Path.Combine(root,"favorites",favorite.Id); Directory.CreateDirectory(dir);
            favorite.Voice=voice; favorite.WordAudio.Clear(); favorite.ExplanationAudio.Clear(); favorite.AudioState="preparing"; history.StoreFavorite(favorite);
            foreach(var pair in new[]{new { Text=favorite.Term,Files=favorite.WordAudio },new { Text=Markdown.PlainText(favorite.Explanation),Files=favorite.ExplanationAudio }}) {
                int part=0; foreach(string fragment in VoiceParts(pair.Text)) { byte[] bytes=await cache.GetAsync(fragment,voice,rate,CancellationToken.None); if(bytes==null || bytes.Length==0) continue; string name=(pair.Files==favorite.WordAudio?"word-":"explanation-")+part+".mp3"; string path=Path.Combine(dir,name); File.WriteAllBytes(path,bytes); pair.Files.Add(Path.Combine("favorites",favorite.Id,name)); part++; }
            }
            favorite.AudioState="ready"; history.StoreFavorite(favorite);
        } catch(Exception e) { favorite.AudioState="failed"; favorite.AudioError=e.Message.Length>240?e.Message.Substring(0,240):e.Message; try { history.StoreFavorite(favorite); } catch { } }
    }
    Task SpeakWorkspace(WorkspaceWindow window,string text) {
        return Speak(null,text,delegate(bool speaking,string message) {
            if(!window.IsDisposed) { window.SetSpeaking(speaking); if(message!=null) window.SetNotice(message); }
        },window);
    }
}
}
