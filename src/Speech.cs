using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Speech.Synthesis;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TermLens {
public sealed class TranslatorSpeech : IDisposable {
    const string Endpoint="https://dev.microsofttranslator.com/apps/endpoint?api-version=1.0";
    readonly HttpClient client;
    readonly byte[] signingKey;
    readonly string clientId=Guid.NewGuid().ToString("N");
    string region, bearer;
    DateTime expires;
    public TranslatorSpeech(string root,string proxy,HttpMessageHandler handler=null,byte[] testKey=null) {
        signingKey=testKey??ProtectedData.Unprotect(File.ReadAllBytes(Path.Combine(root,"translator-signing.dpapi")),null,DataProtectionScope.CurrentUser);
        if(handler==null) {
            // Voice traffic is direct, including authentication and synthesis.
            // Keep the legacy parameter for stored profiles and callers.
            var h=new HttpClientHandler { UseProxy=false,Proxy=null };
            handler=h;
        }
        client=new HttpClient(handler) { Timeout=TimeSpan.FromSeconds(60) };
    }
    string Sign() {
        string date=DateTime.UtcNow.ToString("ddd, dd MMM yyyy HH:mm:ss",CultureInfo.InvariantCulture).ToLowerInvariant()+"GMT";
        string id=Guid.NewGuid().ToString("N");
        string payload=("MSTranslatorAndroidApp"+Uri.EscapeDataString(Endpoint.Split(new[]{"://"},StringSplitOptions.None)[1])+date+id).ToLowerInvariant();
        using(var mac=new HMACSHA256(signingKey)) return "MSTranslatorAndroidApp::"+Convert.ToBase64String(mac.ComputeHash(Encoding.UTF8.GetBytes(payload)))+"::"+date+"::"+id;
    }
    async Task Authenticate(bool force,CancellationToken token) {
        if(!force && bearer!=null && DateTime.UtcNow<expires) return;
        using(var req=new HttpRequestMessage(HttpMethod.Post,Endpoint)) {
            req.Content=new StringContent("",Encoding.UTF8,"application/json");
            req.Headers.TryAddWithoutValidation("X-MT-Signature",Sign());
            req.Headers.TryAddWithoutValidation("X-ClientVersion","4.0.530a 5fe1dc6c");
            req.Headers.TryAddWithoutValidation("X-UserId",clientId);
            req.Headers.TryAddWithoutValidation("X-ClientTraceId",Guid.NewGuid().ToString());
            req.Headers.TryAddWithoutValidation("Accept-Language","zh-Hans");
            req.Headers.TryAddWithoutValidation("X-HomeGeographicRegion","zh-Hans-CN");
            req.Headers.TryAddWithoutValidation("User-Agent","okhttp/4.5.0");
            using(var res=await client.SendAsync(req,token).ConfigureAwait(false)) {
                if(!res.IsSuccessStatusCode) throw new Exception("Voice sign-in: HTTP "+(int)res.StatusCode+". Direct connection failed; try again later.");
                var data=Files.Json().Deserialize<Dictionary<string,object>>(await res.Content.ReadAsStringAsync().ConfigureAwait(false));
                if(!data.ContainsKey("r") || !data.ContainsKey("t") || !Regex.IsMatch(Convert.ToString(data["r"]),"^[a-zA-Z0-9]+$")) throw new Exception("Unexpected voice sign-in response.");
                region=Convert.ToString(data["r"]); bearer=Convert.ToString(data["t"]); expires=DateTime.UtcNow.AddMinutes(8);
                if(String.IsNullOrEmpty(bearer)) throw new Exception("Empty voice sign-in response.");
            }
        }
    }
    public static string Ssml(string text,string voice,int rate) {
        if(!Regex.IsMatch(voice??"","^[a-z]{2}-[A-Z]{2}-[a-zA-Z0-9:._-]+$")) throw new Exception("Choose a valid voice on the Voice tab.");
        text=Regex.Replace(text??"",@"[\x00-\x08\x0B\x0C\x0E-\x1F\uFFFE\uFFFF]","");
        return "<speak version=\"1.0\" xml:lang=\""+voice.Substring(0,5)+"\" xmlns=\"http://www.w3.org/2001/10/synthesis\"><voice name=\""+voice+"\"><prosody rate=\""+Math.Max(-50,Math.Min(50,rate)).ToString(CultureInfo.InvariantCulture)+"%\">"+SecurityElement.Escape(text)+"</prosody></voice></speak>";
    }
    public async Task<byte[]> Synthesize(string text,string voice,int rate,CancellationToken token) {
        string ssml=Ssml(text,voice,rate);
        for(int attempt=0;attempt<2;attempt++) {
            token.ThrowIfCancellationRequested(); await Authenticate(attempt>0,token).ConfigureAwait(false);
            using(var req=new HttpRequestMessage(HttpMethod.Post,"https://"+region+".tts.speech.microsoft.com/cognitiveservices/v1")) {
                req.Content=new StringContent(ssml,Encoding.UTF8,"application/ssml+xml");
                req.Content.Headers.ContentType.CharSet=null;
                req.Headers.TryAddWithoutValidation("Authorization",bearer);
                req.Headers.ExpectContinue=false;
                req.Headers.TryAddWithoutValidation("User-Agent","okhttp/4.5.0");
                req.Headers.TryAddWithoutValidation("X-Microsoft-OutputFormat","audio-24khz-48kbitrate-mono-mp3");
                using(var res=await client.SendAsync(req,token).ConfigureAwait(false)) {
                    if(attempt==0 && (res.StatusCode==HttpStatusCode.Unauthorized || res.StatusCode==HttpStatusCode.Forbidden)) continue;
                    if(!res.IsSuccessStatusCode) throw new Exception("Voice service: HTTP "+(int)res.StatusCode+". Try again or choose another voice.");
                    var type=res.Content.Headers.ContentType;
                    if(type!=null && !type.MediaType.StartsWith("audio/") && type.MediaType!="application/octet-stream") throw new Exception("Voice service returned no audio.");
                    byte[] audio=await res.Content.ReadAsByteArrayAsync().ConfigureAwait(false); token.ThrowIfCancellationRequested();
                    if(audio.Length<100) throw new Exception("Voice service returned empty audio.");
                    return audio;
                }
            }
        }
        throw new Exception("Voice sign-in was refused.");
    }
    public void Dispose() { client.Dispose(); Array.Clear(signingKey,0,signingKey.Length); }
}
public sealed class Mp3Player : IDisposable {
    [DllImport("winmm.dll",CharSet=CharSet.Unicode)] static extern int mciSendString(string command,StringBuilder result,int length,IntPtr callback);
    readonly string alias="termlens"+Guid.NewGuid().ToString("N");
    bool opened;
    int Command(string command,StringBuilder output=null) { return mciSendString(command,output,output==null?0:output.Capacity,IntPtr.Zero); }
    public async Task Play(string path,CancellationToken token) {
        token.ThrowIfCancellationRequested();
        if(Command("open \""+path+"\" type mpegvideo alias "+alias)!=0) throw new Exception("Windows could not open the audio file.");
        opened=true;
        try {
            if(Command("play "+alias+" from 0")!=0) throw new Exception("Windows could not play audio. Check your sound output.");
            while(true) {
                await Task.Delay(100,token);
                var mode=new StringBuilder(64);
                if(Command("status "+alias+" mode",mode)!=0) throw new Exception("Audio playback failed.");
                if(mode.ToString()=="stopped") break;
            }
        } finally { Dispose(); }
    }
    public void Dispose() { if(opened) { Command("stop "+alias); Command("close "+alias); opened=false; } }
}
public sealed partial class MainWindow {
    readonly ComboBox speechVoice=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Top,FlatStyle=FlatStyle.Flat };
    readonly NumericUpDown speechRate=new NumericUpDown { Minimum=-50,Maximum=50,Increment=5,Width=Look.S(90) };
    readonly TextBox speechProxy=new TextBox();
    CancellationTokenSource speechRequest;
    Mp3Player speechPlayer;
    TranslatorSpeech speechService;
    string speechServiceProxy;
    ResultPopup speechOwner;
    SpeechSynthesizer localSpeech;
    void BuildVoice() {
        var page=Page("Voice / 朗读"); var panel=new TableLayoutPanel { Dock=DockStyle.Top,AutoSize=true,ColumnCount=2 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        speechVoice.Items.AddRange(new object[]{"en-US-AvaMultilingualNeural","en-US-AndrewMultilingualNeural","en-US-JennyMultilingualNeural","en-US-AriaNeural","en-GB-SoniaNeural","en-GB-RyanNeural","zh-CN-XiaoxiaoNeural"});
        AddRow(panel,0,"Voice service",speechProvider); AddRow(panel,1,"Microsoft voice",speechVoice); AddRow(panel,2,"Speed (%)",speechRate); AddRow(panel,3,"Voice proxy",speechProxy);
        AddRow(panel,4,"",new Label { AutoSize=true,Dock=DockStyle.Fill,Text="Read original text, answers, or your draft. A selection reads only those words.\nBaidu web voice connects directly without a key. Microsoft uses the voice and proxy above, or Windows offline voices when no online setup is present.\n0% is normal speed. Stop audio cancels preparation or playback." });
        var actions=Flow(); actions.Controls.Add(Button("Save settings / 保存",delegate { SaveSettings(true); }));
        actions.Controls.Add(Button("Test voice",async delegate { await Speak(null,"An algorithm is a set of steps. 算法就是一组步骤。"); }));
        actions.Controls.Add(Button("Stop audio",StopSpeech)); AddRow(panel,5,"",actions); page.Controls.Add(panel);
        speechProvider.SelectedIndexChanged+=delegate {
            if(refreshingSpeechLabels) return;
            StopSpeech(); if(speechCache!=null) { speechCache.Dispose(); speechCache=null; speechService=null; }
            speechVoice.Enabled=speechProxy.Enabled=!BaiduVoice;
        };
        speechScope.SelectedIndexChanged+=delegate { if(!refreshingSpeechLabels && speechCache!=null) speechCache.CancelPreloads(); };
        speechPreload.CheckedChanged+=delegate { if(!speechPreload.Checked && speechCache!=null) speechCache.CancelPreloads(); };
        RefreshSpeechLabels();
        AddRow(panel,6,"",speechPreload); AddRow(panel,7,"Prepare",speechScope); AddRow(panel,8,"",speechCacheInfo);
        AddRow(panel,9,"",Button("Clear audio cache",ClearSpeechCache));
    }
    static void ValidateSpeech(string voice,string proxy) {
        if(voice=="baidu-auto") return;
        TranslatorSpeech.Ssml("",voice,0);
        Uri address;
        if(proxy.Length>0 && (!Uri.TryCreate(proxy,UriKind.Absolute,out address) || (address.Scheme!="http" && address.Scheme!="https") || !String.IsNullOrEmpty(address.UserInfo))) throw new Exception("Voice proxy must be an HTTP or HTTPS address without a password.");
    }
    public static List<string> SpeechParts(string text) {
        var parts=new List<string>();
        while(text.Length>2500) {
            int n=2500; while(n>1500 && !Char.IsWhiteSpace(text[n-1]) && ".!?。！？".IndexOf(text[n-1])<0) n--;
            if(n==1500) n=2500;
            if(Char.IsHighSurrogate(text[n-1])) n--;
            parts.Add(text.Substring(0,n)); text=text.Substring(n);
        }
        if(text.Length>0) parts.Add(text); return parts;
    }
    void StopSpeech() {
        if(speechRequest!=null) speechRequest.Cancel();
        if(localSpeech!=null) localSpeech.SpeakAsyncCancelAll();
        if(speechPlayer!=null) speechPlayer.Dispose();
        if(speechOwner!=null && !speechOwner.IsDisposed) speechOwner.SetSpeaking(false,"Audio stopped");
        if(speechFeedback!=null) speechFeedback(false,UiLanguage.T("Audio stopped","朗读已停止"));
        speechFeedback=null; speechWindow=null; speechOwner=null;
    }
    async Task SpeakOffline(string text,int rate,CancellationToken token) {
        using(var voice=new SpeechSynthesizer()) {
            localSpeech=voice;
            try {
                var installedVoices=voice.GetInstalledVoices();
                var preferred=Program.IsRelease?settings.SpeechVoice:null;
                var choice=System.Linq.Enumerable.FirstOrDefault(installedVoices,v=>v.Enabled && v.VoiceInfo.Name==preferred)
                    ??System.Linq.Enumerable.FirstOrDefault(installedVoices,v=>v.Enabled && v.VoiceInfo.Culture.TwoLetterISOLanguageName=="en");
                if(choice!=null) voice.SelectVoice(choice.VoiceInfo.Name);
                voice.Rate=Math.Max(-5,Math.Min(5,rate/10)); voice.SetOutputToDefaultAudioDevice();
                var finished=new TaskCompletionSource<bool>();
                voice.SpeakCompleted+=delegate(object sender,SpeakCompletedEventArgs e) { if(e.Cancelled) finished.TrySetCanceled(); else if(e.Error!=null) finished.TrySetException(e.Error); else finished.TrySetResult(true); };
                token.ThrowIfCancellationRequested(); voice.SpeakAsync(text);
                using(token.Register(delegate { voice.SpeakAsyncCancelAll(); })) { await finished.Task; }
            } finally { if(localSpeech==voice) localSpeech=null; }
        }
    }
    async Task Speak(ResultPopup card,string text,Action<bool,string> feedback=null,Form owner=null) {
        StopSpeech(); text=(text??"").Trim(); if(text.Length==0) return;
        var active=new CancellationTokenSource(); speechRequest=active; speechOwner=card; speechFeedback=feedback; speechWindow=owner;
        Action<bool,string> report=delegate(bool speaking,string message) {
            if(speechRequest!=active || active.IsCancellationRequested) return;
            if(card!=null && !card.IsDisposed) card.SetSpeaking(speaking,message); else if(feedback!=null) feedback(speaking,message); else if(message!=null) SetStatus(message,false);
        };
        try {
            if(Program.IsRelease && settings.SpeechProvider=="windows") {
                report(true,"正在朗读…");
                await SpeakOffline(text,settings.SpeechRate,active.Token);
                report(false,"朗读完成"); return;
            }
            string voice=ActiveVoice, proxyAddress=ActiveVoiceProxy;
            ValidateSpeech(voice,proxyAddress);
            if(!BaiduVoice && !File.Exists(Path.Combine(root,"translator-signing.dpapi"))) {
                report(true,UiLanguage.T("Reading with a Windows voice…","正在使用 Windows 语音朗读…"));
                await SpeakOffline(text,(int)speechRate.Value,active.Token);
                report(false,UiLanguage.T("Finished reading","朗读完成"));
                return;
            }
            var audioCache=GetSpeechCache(proxyAddress); int rate=(int)speechRate.Value;
            foreach(string part in VoiceParts(text)) {
                active.Token.ThrowIfCancellationRequested();
                report(true,audioCache.Contains(part,voice,rate)?UiLanguage.T("Playing cached audio…","正在播放缓存音频…"):UiLanguage.T("Preparing audio…","正在准备音频…"));
                byte[] audio=await audioCache.GetAsync(part,voice,rate,active.Token);
                active.Token.ThrowIfCancellationRequested();
                string path=Path.Combine(Path.GetTempPath(),"termlens-audio-"+Guid.NewGuid().ToString("N")+".mp3");
                try {
                    File.WriteAllBytes(path,audio);
                    using(var player=new Mp3Player()) {
                        speechPlayer=player;
                        report(true,UiLanguage.T("Reading aloud…","正在朗读…"));
                        await player.Play(path,active.Token);
                    }
                } finally { try { File.Delete(path); } catch { } }
            }
            report(false,UiLanguage.T("Finished reading","朗读完成"));
        } catch(Exception e) {
            if(!active.IsCancellationRequested && !exiting) {
                // HTTP exceptions can contain private service URLs. Show a bounded, user-safe message.
                string message=e is HttpRequestException?"Voice connection failed. Check the Voice proxy setting.":e is TaskCanceledException?"Voice service timed out. Try again.":e is FileNotFoundException?"Voice setup file is missing. Restore translator-signing.dpapi.":e.Message;
                report(false,message);
            }
        } finally {
            if(speechRequest==active) {
                if(card!=null && !card.IsDisposed) card.SetSpeaking(false,null);
                if(feedback!=null) feedback(false,null);
                speechRequest=null; speechPlayer=null; speechOwner=null; speechFeedback=null; speechWindow=null;
            }
            active.Dispose();
        }
    }
}
}
