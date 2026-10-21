using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TermLens {
public sealed partial class MainWindow {
    ReleaseAudioService releaseAudio;
    readonly Dictionary<ReleaseLookupWindow,CancellationTokenSource> releaseAudioPreloads=new Dictionary<ReleaseLookupWindow,CancellationTokenSource>();
    CancellationTokenSource releasePlayback;
    ReleaseLookupWindow releasePlaybackWindow;
    ReleaseAudioService GetReleaseAudio() {
        // UI/request tests must never contact a real voice service.
        if(Program.IsTest) return null;
        if(releaseAudio==null) {
            releaseAudio=new ReleaseAudioService(root,history);
            releaseAudio.FavoriteAudioChanged+=delegate(Favorite favorite) {
                if(exiting || IsDisposed) return;
                foreach(var window in new[]{releaseWindow,releaseSecondaryWindow}) {
                    if(window==null || window.IsDisposed || window.Current==null || window.Current.Id!=favorite.QueryId || window.Current.Answer!=favorite.Explanation) continue;
                    window.SetNotice(favorite.AudioState=="ready"?"已收藏文字和音频":favorite.AudioState=="failed"?"文字已收藏，音频保存失败，可取消后重新收藏。":"文字已收藏，正在保存音频…",favorite.AudioState=="failed");
                }
            };
        }
        return releaseAudio;
    }
    Settings ReleaseAudioSettings() {
        return new Settings { SpeechProvider=settings.SpeechProvider,SpeechVoice=settings.SpeechVoice,SpeechRate=settings.SpeechRate,SpeechProxy=settings.SpeechProxy,SpeechPreload=settings.SpeechPreload,SpeechSaveWithFavorites=settings.SpeechSaveWithFavorites };
    }
    async void PreloadReleaseAudio(ReleaseLookupWindow window,string original,string answer,Lookup lookup) {
        if(exiting || !settings.SpeechPreload || window.IsDisposed) return;
        var service=GetReleaseAudio(); if(service==null) return;
        CancellationTokenSource token;
        if(!releaseAudioPreloads.TryGetValue(window,out token)) { token=new CancellationTokenSource(); releaseAudioPreloads[window]=token; }
        var elapsed=System.Diagnostics.Stopwatch.StartNew();
        try { await service.PreloadAsync(original,answer,ReleaseAudioSettings(),token.Token,lookup); UiTrace.Write("release audio preload phase="+(String.IsNullOrEmpty(answer)?"original":"answer")+" ms="+elapsed.ElapsedMilliseconds); }
        catch(OperationCanceledException) { }
        catch(Exception e) { UiTrace.Write("release audio preload failed type="+e.GetType().Name); }
    }
    void StopReleaseWindowPlayback(ReleaseLookupWindow window) {
        if(releasePlaybackWindow!=window) return;
        if(releasePlayback!=null) releasePlayback.Cancel();
        if(releaseAudio!=null) releaseAudio.StopPlayback();
        if(!window.IsDisposed) window.SetSpeaking(false,null);
    }
    void CancelReleaseAudio(bool secondary) {
        foreach(var pair in releaseAudioPreloads.ToArray()) {
            if(secondary && !pair.Key.IsTransient) continue;
            pair.Value.Cancel(); pair.Value.Dispose(); releaseAudioPreloads.Remove(pair.Key);
        }
        if(!secondary || releasePlaybackWindow!=null && releasePlaybackWindow.IsTransient) {
            if(releasePlayback!=null) releasePlayback.Cancel();
            if(releaseAudio!=null) releaseAudio.StopPlayback();
        }
    }
    async Task SpeakReleaseAudio(ReleaseLookupWindow window,string text) {
        var service=GetReleaseAudio(); if(service==null) return;
        if(releasePlayback!=null) releasePlayback.Cancel();
        service.StopPlayback();
        if(releasePlaybackWindow!=null && releasePlaybackWindow!=window && !releasePlaybackWindow.IsDisposed) releasePlaybackWindow.SetSpeaking(false,null);
        var token=new CancellationTokenSource(); releasePlayback=token; releasePlaybackWindow=window;
        var item=window.Current;
        int displayRevision=window.DisplayRevision;
        Func<bool> stillShown=()=>!window.IsDisposed && !token.IsCancellationRequested && releasePlayback==token && window.DisplayRevision==displayRevision && Object.ReferenceEquals(window.Current,item);
        var elapsed=System.Diagnostics.Stopwatch.StartNew();
        try {
            window.SetSpeaking(true,"正在准备音频…");
            var favorite=item==null?null:history.FindFavorite(item.Kind,item.Term);
            await service.PlayAsync(text,ReleaseAudioSettings(),token.Token,favorite,item,delegate {
                if(stillShown()) { window.MarkPlaybackStarted(); UiTrace.Write("release audio playback started ms="+elapsed.ElapsedMilliseconds); }
            });
            if(stillShown()) window.SetSpeaking(false,"朗读完成");
        } catch(OperationCanceledException) { }
        catch(Exception e) { UiTrace.Write("release audio playback failed type="+e.GetType().Name); if(stillShown()) window.SetSpeaking(false,"朗读失败："+e.Message); }
        finally {
            if(stillShown()) window.SetSpeaking(false,null);
            if(releasePlayback==token) { releasePlayback=null; releasePlaybackWindow=null; }
            token.Dispose();
        }
    }
    async void SaveReleaseFavoriteAudio(Favorite favorite) {
        if(favorite==null) return;
        if(!favorite.Saved) { if(releaseAudio!=null) releaseAudio.CancelFavorite(favorite.Id); return; }
        if(!settings.SpeechSaveWithFavorites) return;
        var service=GetReleaseAudio(); if(service==null) return;
        try { await service.PrepareFavoriteAsync(favorite,ReleaseAudioSettings()); }
        catch(OperationCanceledException) { }
        catch(Exception e) { UiTrace.Write("release favorite audio failed type="+e.GetType().Name); }
    }
    void DisposeReleaseAudio() { CancelReleaseAudio(false); if(releaseAudio!=null) { releaseAudio.Dispose(); releaseAudio=null; } }
}
}
