using System;
using System.Windows.Forms;

namespace TermLens {
public sealed partial class MainWindow {
    Timer releaseRetentionTimer;
    DateTime releaseRetentionDue=DateTime.MinValue;
    bool releaseRetentionBusy;
    void InitReleaseRetention() {
        releaseRetentionTimer=new Timer { Interval=60000 };
        releaseRetentionTimer.Tick+=delegate { MaintainReleaseHistory(); };
        Shown+=delegate { MaintainReleaseHistory(); releaseRetentionTimer.Start(); };
        Disposed+=delegate { releaseRetentionTimer.Dispose(); };
    }
    void MaintainReleaseHistory() {
        if(exiting || releaseRetentionBusy || DateTime.UtcNow<releaseRetentionDue) return;
        // The database and favorite flags belong to the UI thread. Keep the
        // ownership check and cleanup together so saving a favorite cannot race
        // a stale list of protected IDs. Defer maintenance during live work.
        if(releaseRequest!=null || releaseSecondaryRequest!=null || releasePlayback!=null) return;
        releaseRetentionBusy=true;
        try {
            var result=history.ApplyReleaseRetention(root,DateTime.UtcNow);
            releaseRetentionDue=DateTime.UtcNow.AddHours(1);
            UiTrace.Write("release retention queries="+result.ExpiredQueryIds.Count+" audioDirectories="+result.DeletedAudioDirectories.Count+" warnings="+result.Warnings.Count);
        } catch(Exception error) {
            releaseRetentionDue=DateTime.UtcNow.AddMinutes(5);
            UiTrace.Write("release retention failed type="+error.GetType().Name);
        } finally { releaseRetentionBusy=false; }
    }
}
}
