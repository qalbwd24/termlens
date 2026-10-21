using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace TermLens {
public sealed class ReadingCache {
    public string Text, Answer, Draft;
    public bool Teacher, Refine, Complete;
    public Lookup Lookup;
    public List<StudyTurn> Trail;
}
public sealed partial class MainWindow {
    readonly NumericUpDown readingSize=new NumericUpDown { Minimum=9,Maximum=28,Width=Look.S(90) };
    readonly NumericUpDown readingWidth=new NumericUpDown { Minimum=360,Maximum=1400,Increment=20,Width=Look.S(100) };
    readonly NumericUpDown readingHeight=new NumericUpDown { Minimum=0,Maximum=2000,Increment=20,Width=Look.S(100) };
    readonly NumericUpDown scrollSpeed=new NumericUpDown { Minimum=10,Maximum=150,Increment=10,Width=Look.S(90) };
    readonly CheckBox readingTopMost=new CheckBox { AutoSize=true,Text="Keep reading on top" };
    bool applyingReading;
    ReadingCache cachedReading;
    void BuildReadingPreferences() {
        var page=Page("Reading / 阅读外观"); var panel=new TableLayoutPanel { Dock=DockStyle.Top,AutoSize=true,ColumnCount=2 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        AddRow(panel,0,"Text size · pt",readingSize); AddRow(panel,1,"Window width",readingWidth); AddRow(panel,2,"Minimum height",readingHeight);
        AddRow(panel,3,"",new Label { AutoSize=true,MaximumSize=new Size(Look.S(470),0),Text="Ctrl + mouse wheel adjusts text size. Your size is remembered.\nThe main window keeps records until you close it.\nF13 with no selection opens Explain. Last session restores your previous window.\n\nShift switches Explain / Ask / Polish. Shift+Enter inserts a new line.\nQuote adds a whole entry; select answer text and press F13 for the temporary dictionary.",Padding=new Padding(0,12,0,20) });
        var actions=Flow(); actions.Controls.Add(Button("Apply reading size",delegate { PersistReading((float)readingSize.Value,(int)readingWidth.Value,(int)readingHeight.Value,true); }));
        actions.Controls.Add(Button("Reset reading size",delegate { PersistReading(11,480,0,true); })); AddRow(panel,4,"",actions);
        AddRow(panel,5,"Scroll speed (%)",scrollSpeed);
        AddRow(panel,6,"",new Label { AutoSize=true,MaximumSize=new Size(Look.S(470),0),Text="40% gives a gentler scroll; 100% follows the Windows wheel distance.\nChanges are saved immediately." });
        AddRow(panel,7,"",readingTopMost);
        readingTopMost.CheckedChanged+=delegate { if(!applyingReading) PersistTopMost(readingTopMost.Checked); };
        scrollSpeed.ValueChanged+=delegate {
            if(applyingReading) return;
            settings.ScrollSpeed=(int)scrollSpeed.Value;
            if(workspace!=null && !workspace.IsDisposed) workspace.Timeline.SpeedPercent=settings.ScrollSpeed;
            try { var saved=Files.Load(root); saved.ScrollSpeed=settings.ScrollSpeed; Files.Save(root,saved); }
            catch(Exception e) { SetStatus(e.Message,true); }
        };
        page.Controls.Add(panel);
    }
    void SetReadingControls() {
        applyingReading=true;
        readingSize.Value=(decimal)Math.Max(9,Math.Min(28,settings.ReadingFontSize));
        readingWidth.Value=Math.Max(360,Math.Min(1400,settings.ReadingWidth));
        readingHeight.Value=Math.Max(0,Math.Min(2000,settings.ReadingHeight));
        scrollSpeed.Value=Math.Max(10,Math.Min(150,settings.ScrollSpeed));
        readingTopMost.Checked=settings.ReadingTopMost;
        applyingReading=false;
    }
    void PersistAppearance() {
        try { var saved=Files.Load(root); Files.Save(root,saved); settings=saved; ApplyAppearance(saved); SetStatus("Appearance saved.",false); } catch(Exception e) { SetStatus(e.Message,true); }
    }
    void ApplyAppearance(Settings value) {
        WindowAppearance.Apply(this,value,false);
        if(popup!=null && !popup.IsDisposed) popup.ApplyAppearance(value);
        if(workspace!=null && !workspace.IsDisposed) workspace.ApplyAppearance(value);
        if(dictionary!=null && !dictionary.IsDisposed) dictionary.ApplyAppearance(value);
    }
    void PersistTopMost(bool enabled) {
        settings.ReadingTopMost=enabled;
        applyingReading=true; readingTopMost.Checked=enabled; applyingReading=false;
        if(workspace!=null && !workspace.IsDisposed) workspace.SetPinned(enabled);
        if(Visible) TopMost=enabled;
        try { var saved=Files.Load(root); saved.ReadingTopMost=enabled; Files.Save(root,saved); }
        catch(Exception e) { SetStatus(e.Message,true); }
    }
    // Remember where the reading window was left, so it reopens in the same place.
    void PersistWorkspaceGeometry(int left,int top,int width,int height) {
        if(width<=0 || height<=0) return;
        settings.WorkspaceLeft=left; settings.WorkspaceTop=top;
        settings.WorkspaceWidth=width; settings.WorkspaceHeight=height;
        try {
            var saved=Files.Load(root);
            saved.WorkspaceLeft=left; saved.WorkspaceTop=top;
            saved.WorkspaceWidth=width; saved.WorkspaceHeight=height;
            Files.Save(root,saved);
        } catch { }
    }
    void PersistReading(float font,int width,int height,bool apply) {
        if(applyingReading) return;
        settings.ReadingFontSize=font; settings.ReadingWidth=width; settings.ReadingHeight=height; SetReadingControls();
        try {
            var saved=Files.Load(root); saved.ReadingFontSize=font; saved.ReadingWidth=width; saved.ReadingHeight=height; Files.Save(root,saved);
            if(apply) foreach(var card in studyCards.Keys) if(!card.IsDisposed) card.ApplyReading(font,width,height);
            if(apply && workspace!=null && !workspace.IsDisposed) workspace.ApplyReading(font,width,height);
            SetStatus("Reading size saved. Ctrl + mouse wheel changes text size.",false);
        } catch(Exception e) { SetStatus("Could not save reading size: "+e.Message,true); }
    }
    void LoadReadingCache() {
        try {
            string path=Path.Combine(root,"last-reading.json");
            if(File.Exists(path)) cachedReading=Files.Json().Deserialize<ReadingCache>(File.ReadAllText(path,Files.Utf8));
            if(cachedReading!=null && String.IsNullOrWhiteSpace(cachedReading.Text)) cachedReading=null;
        } catch { cachedReading=null; }
        if(cachedReading==null) {
            try {
                var words=history.List("",false,1); string date="";
                if(words.Count>0) { var last=words[0]; date=last.Created; cachedReading=new ReadingCache { Text=last.Term,Answer=last.Answer,Complete=true,Lookup=last,Trail=new List<StudyTurn>{new StudyTurn(last.Term,last.Answer)} }; }
                var teachers=history.Run("SELECT question,answer,context,created FROM teacher_answers ORDER BY created DESC LIMIT 1");
                if(teachers.Count>0 && String.CompareOrdinal(teachers[0][3],date)>0) {
                    var row=teachers[0]; var trail=Files.Json().Deserialize<List<StudyTurn>>(row[2]); trail.Add(new StudyTurn(row[0],row[1]));
                    cachedReading=new ReadingCache { Text=row[0],Answer=row[1],Complete=true,Teacher=true,Trail=StudyTurn.Recent(trail) };
                    date=row[3];
                }
                var refined=history.Run("SELECT question,answer,context,created FROM refinements ORDER BY created DESC LIMIT 1");
                if(refined.Count>0 && String.CompareOrdinal(refined[0][3],date)>0) {
                    var row=refined[0]; var trail=Files.Json().Deserialize<List<StudyTurn>>(row[2]); trail.Add(new StudyTurn(row[0],row[1]));
                    cachedReading=new ReadingCache { Text=row[0],Answer=row[1],Complete=true,Refine=true,Trail=StudyTurn.Recent(trail) };
                }
            } catch { }
        }
        UpdateLastReading();
    }
    void UpdateLastReading() { quickInput.SetLastLookup(cachedReading==null?null:cachedReading.Text); }
    void RememberReading(StudyCard state) {
        if(state==null || !state.Window.HasLookup) return;
        var card=state.Window;
        cachedReading=new ReadingCache { Text=card.OriginalText,Answer=card.RawAnswer,Draft=card.DraftQuestion,Teacher=state.Teacher,Refine=state.Refine,Complete=card.IsComplete,Lookup=state.Lookup,Trail=StudyTurn.Recent(state.Trail) };
        try { Files.Atomic(Path.Combine(root,"last-reading.json"),Files.Json().Serialize(cachedReading)); }
        catch { card.SetStatus("Last reading could not be saved to disk.",false); }
        UpdateLastReading();
    }
    void RestoreLastReading() {
        if(cachedReading==null) return;
        var saved=cachedReading;
        var card=new ResultPopup(); var state=ConnectCard(card,null,saved.Teacher,saved.Refine);
        state.Trail=saved.Trail??new List<StudyTurn>(); state.Lookup=saved.Lookup;
        if(state.Lookup!=null) { var rows=history.Run("SELECT saved FROM terms WHERE term_key=?",HistoryStore.Key(state.Lookup.Term)); if(rows.Count>0) state.Lookup.Saved=rows[0][0]=="1"; }
        card.BeginLookup(saved.Text,new Rectangle(Cursor.Position,new Size(1,1))); card.SetAnswer(saved.Answer??"");
        if(saved.Complete) card.Complete(saved.Lookup!=null && saved.Lookup.Saved); else card.Failed("Restored an unfinished answer. No new request was made.");
        card.DraftQuestion=saved.Draft; quickInput.Hide();
    }
}
}
