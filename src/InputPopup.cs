using System;
using System.Drawing;
using System.Windows.Forms;

namespace TermLens {
public sealed class InputPopup : Form {
    public event Action<string> Submitted;
    public event Action<string> RefineRequested;
    public event Action LastLookupRequested;
    public event Action SettingsRequested;
    readonly SoftButton lastLookup=new SoftButton("Last lookup",false);
    readonly SoftButton settingsButton=new SoftButton("Settings",false);
    readonly TextBox editor=new ComposerBox { MaxLength=4000,Font=new Font("Microsoft YaHei UI",11) };
    readonly SoftButton explain=new SoftButton("Ask teacher",true);
    readonly SoftButton refine=new SoftButton("Refine wording",false);
    public InputPopup() {
        AutoScaleMode=AutoScaleMode.None; FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false; TopMost=true; StartPosition=FormStartPosition.Manual;
        Size=new Size(Look.S(440),Look.S(330)); Text="TermLens input";
        var title=new Label { Text="TERMLENS  ·  Write, learn, improve",Font=new Font("Segoe UI",9),Bounds=new Rectangle(Look.S(24),Look.S(20),Look.S(350),Look.S(20)) };
        var close=new SoftButton("×",false) { Bounds=new Rectangle(Width-Look.S(52),Look.S(12),Look.S(28),Look.S(28)) }; close.Click+=delegate { Hide(); };
        var frame=new FieldFrame(editor,true) { Dock=DockStyle.None }; frame.SetBounds(Look.S(24),Look.S(54),Width-Look.S(48),Look.S(130));
        var hint=new Label { Text="Enter: Ask teacher · Shift+Enter: new line",Font=new Font("Segoe UI",8),Bounds=new Rectangle(Look.S(24),Look.S(194),Look.S(392),Look.S(22)) };
        refine.SetBounds(Look.S(24),Look.S(220),Look.S(190),Look.S(36)); refine.Enabled=false;
        explain.SetBounds(Look.S(226),Look.S(220),Look.S(190),Look.S(36)); explain.Enabled=false;
        editor.TextChanged+=delegate { explain.Enabled=refine.Enabled=!String.IsNullOrWhiteSpace(editor.Text); }; explain.Click+=delegate { Submit(); };
        refine.Click+=delegate { if(!String.IsNullOrWhiteSpace(editor.Text) && RefineRequested!=null) RefineRequested(editor.Text.Trim()); };
        ((ComposerBox)editor).Submitted+=Submit;
        lastLookup.SetBounds(Look.S(24),Look.S(276),Width-Look.S(144),Look.S(30)); lastLookup.Enabled=false;
        settingsButton.SetBounds(Width-Look.S(112),Look.S(276),Look.S(88),Look.S(30));
        settingsButton.Click+=delegate { if(SettingsRequested!=null) SettingsRequested(); };
        lastLookup.Click+=delegate { if(LastLookupRequested!=null) LastLookupRequested(); };
        Controls.AddRange(new Control[]{title,frame,hint,explain,refine,close,lastLookup,settingsButton}); Theme.Apply(this); Look.Shape(this,14);
        VisibleChanged+=delegate { if(Visible) Theme.Apply(this); };
    }
    public void SetLastLookup(string text) { lastLookup.Enabled=!String.IsNullOrWhiteSpace(text); lastLookup.Text=text==null?"Last lookup · none yet":"Last lookup · "+text.Replace("\r"," ").Replace("\n"," ").Substring(0,Math.Min(text.Replace("\r"," ").Replace("\n"," ").Length,32)); }
    void Submit() { if(!String.IsNullOrWhiteSpace(editor.Text) && Submitted!=null) Submitted(editor.Text.Trim()); }
    public void ClearDraft() { editor.Clear(); }
    public void Present() {
        var work=Screen.FromPoint(Cursor.Position).WorkingArea;
        Location=new Point(work.Left+(work.Width-Width)/2,work.Top+(work.Height-Height)/3);
        Show(); Activate(); Native.SetForegroundWindow(Handle); editor.Focus();
    }
    protected override bool ProcessCmdKey(ref Message msg,Keys keys) {
        if(keys==Keys.Escape) { Hide(); return true; }
        if((keys==Keys.Enter || keys==(Keys.Control|Keys.Enter)) && !editor.ContainsFocus) { Submit(); return true; }
        return base.ProcessCmdKey(ref msg,keys);
    }
    protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using(var path=Look.Round(new RectangleF(0,0,Width-1,Height-1),Look.S(14))) using(var pen=new Pen(Look.Border)) e.Graphics.DrawPath(pen,path); }
}
public sealed partial class MainWindow {
    readonly InputPopup quickInput=new InputPopup();
    void ConnectInput() {
        quickInput.LastLookupRequested+=RestoreLastReading; LoadReadingCache();
        quickInput.SettingsRequested+=delegate { Reveal(); tabs.SelectedIndex=4; };
        quickInput.RefineRequested+=async delegate(string text) { await RunStudyCard(null,text,false,true); };
        quickInput.Submitted+=async delegate(string text) {
            await OpenStudyCard(null,text,true);
        };
    }
    void ShowQuickInput() { if(bubble!=null) bubble.Hide(); quickInput.Present(); }
}
}
