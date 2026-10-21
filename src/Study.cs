using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TermLens {
public sealed class StudyTurn {
    public string Question, Answer;
    public StudyTurn() { }
    public StudyTurn(string question,string answer) { Question=question; Answer=answer; }
    static string Clip(string text,int max) { text=text??""; return text.Length<=max?text:text.Substring(0,max)+"\n[Earlier text shortened]"; }
    public static List<StudyTurn> Recent(IList<StudyTurn> turns) {
        var result=new List<StudyTurn>(); int size=0;
        for(int i=turns.Count-1;i>=0 && result.Count<6;i--) {
            var t=new StudyTurn(Clip(turns[i].Question,6000),Clip(turns[i].Answer,10000));
            if(result.Count>0 && size+t.Question.Length+t.Answer.Length>24000) break;
            size+=t.Question.Length+t.Answer.Length; result.Insert(0,t);
        }
        return result;
    }
}
public sealed class StudyCard {
    public ResultPopup Window, Parent;
    public List<StudyTurn> Trail=new List<StudyTurn>();
    public Lookup Lookup;
    public CancellationTokenSource Request;
    public bool Teacher, Refine;
}
public sealed partial class MainWindow {
    readonly Dictionary<ResultPopup,StudyCard> studyCards=new Dictionary<ResultPopup,StudyCard>();
    ResultPopup latestCard;
    StudyCard ConnectCard(ResultPopup card,ResultPopup parent,bool teacher,bool refine=false) {
        var state=new StudyCard { Window=card,Parent=parent,Teacher=teacher,Refine=refine }; studyCards.Add(card,state);
        latestCard=card;
        card.ApplyReading(settings.ReadingFontSize,settings.ReadingWidth,settings.ReadingHeight);
        card.UserClosed+=delegate { RememberReading(state); };
        card.FontSizeChanged+=delegate(float value) { PersistReading(value,settings.ReadingWidth,settings.ReadingHeight,true); };
        card.PreferredSizeChanged+=delegate(Size size) { PersistReading(settings.ReadingFontSize,size.Width,size.Height,false); };
        card.SetStudyMode(teacher || refine,parent!=null); if(refine) card.SetRefineMode();
        card.SpeechRequested+=async delegate(string text) { await Speak(card,text); };
        card.SpeechStopRequested+=delegate { if(speechOwner==card) StopSpeech(); };
        card.Activated+=delegate { latestCard=card; };
        card.WordRequested+=async delegate(string word) { await OpenStudyCard(card,word,false); };
        card.QuestionRequested+=async delegate(string question) { await OpenStudyCard(card,question,true); };
        card.RefineRequested+=async delegate(string text) { await RunStudyCard(card,text,false,true); };
        card.SelectionOffered+=delegate(Rectangle bounds) { if(bubble!=null && settings.FloatingButton) bubble.Present(card.Handle,bounds); };
        card.ParentRequested+=delegate { if(parent!=null && !parent.IsDisposed) parent.ShowExisting(); };
        card.SaveRequested+=delegate {
            if(state.Lookup==null) return;
            try { history.SaveWord(state.Lookup.Term,!state.Lookup.Saved); state.Lookup.Saved=!state.Lookup.Saved; card.SetSaved(state.Lookup.Saved); if(current!=null && current.Id==state.Lookup.Id) UpdateSaved(); }
            catch(Exception e) { card.SetStatus(e.Message,true); }
        };
        card.SettingsRequested+=delegate { Reveal(); tabs.SelectedIndex=refine?7:6; };
        card.StopRequested+=delegate { if(state.Request!=null) state.Request.Cancel(); else if(card==popup && request!=null) request.Cancel(); };
        return state;
    }
    Task OpenStudyCard(ResultPopup parent,string text,bool teacher) { return RunStudyCard(parent,text,teacher,false); }
    async Task RunStudyCard(ResultPopup parent,string text,bool teacher,bool refine,Rectangle? selectionAnchor=null) {
        StudyCard source=null;
        if(exiting || (parent==null && !teacher && !refine)) return;
        if(parent!=null && (!studyCards.TryGetValue(parent,out source) || source.Trail.Count==0 || !parent.CanStudy)) return;
        text=(text??"").Trim(); if(text.Length==0) return;
        if(text.Length>(teacher || refine?4000:12000)) { StudyInputError(parent,"Please use a shorter question or selection."); return; }
        Settings use;
        try { use=ReadSettings(); if(teacher || refine) { string chosen=refine?use.RefinePrompt:use.TeacherPrompt; if(String.IsNullOrWhiteSpace(chosen)) throw new Exception("Enter the "+(refine?"Refine wording":"English teacher")+" prompt in Settings."); use.Prompt=chosen; } }
        catch(Exception e) { StudyInputError(parent,e.Message); return; }
        var context=source==null || refine?new List<StudyTurn>():StudyTurn.Recent(source.Trail);
        if(parent!=null) { parent.KeepOpen(); parent.SetBranchBusy(true); }
        if(bubble!=null) bubble.Hide();
        var card=new ResultPopup(); var state=ConnectCard(card,parent,teacher,refine); state.Trail=context;
        var selected=selectionAnchor??(parent==null?quickInput.Bounds:new Rectangle(parent.Right+Look.S(12),parent.Top,1,1));
        if(parent==null) { quickInput.Hide(); quickInput.ClearDraft(); Hide(); }
        card.BeginLookup(text,selected); if(parent!=null) card.PlaceBeside(parent.Bounds);
        string useKey=key.Text.Trim();
        if(useKey.Length==0) { card.Failed("Add your API key in Settings to begin."); if(parent!=null) parent.SetBranchBusy(false); return; }
        state.Request=new CancellationTokenSource(); var active=state.Request;
        try {
            card.SetStatus(use.CarefulAnswers?"Thinking…":"Answering…",false);
            string reply=await Requests.Complete(use,useKey,text,new List<string>(),card.SetAnswer,active.Token,requestHandlers==null?null:requestHandlers(),context);
            if(exiting) return;
            state.Trail=StudyTurn.Recent(context.Concat(new[]{new StudyTurn(text,reply)}).ToList());
            if(teacher || refine) {
                history.Run("INSERT INTO "+(refine?"refinements":"teacher_answers")+"(id,question,answer,context,prompt,model,created) VALUES(?,?,?,?,?,?,?)",Guid.NewGuid().ToString("N"),text,reply,Files.Json().Serialize(context),use.Prompt,use.Model,DateTime.UtcNow.ToString("o"));
            } else state.Lookup=history.Add(text,reply,use.Model,use.Prompt,Files.Json().Serialize(context));
            card.Complete(state.Lookup!=null && state.Lookup.Saved);
            RememberReading(state);
        } catch(Exception e) { if(!exiting) card.Failed(active.IsCancellationRequested?"Stopped. Partial text was not saved.":Requests.SafeError(e.Message,useKey)); }
        finally { active.Dispose(); state.Request=null; if(!exiting && parent!=null && !parent.IsDisposed) parent.SetBranchBusy(false); }
    }
    void StudyInputError(ResultPopup parent,string message) {
        if(parent!=null) parent.SetStatus(message,true);
        else { SetStatus(message,true); MessageBox.Show(quickInput,message,"Check teacher settings",MessageBoxButtons.OK,MessageBoxIcon.Information); }
    }
}
}
