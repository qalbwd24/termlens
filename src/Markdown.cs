using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TermLens {
[Flags] public enum MarkdownStyle { Normal=0, Bold=1, Italic=2, Code=4, Strike=8, Link=16, Math=32, DisplayMath=64 }
public sealed class MarkdownRun {
    public string Text;
    public string Formula;
    public MarkdownStyle Style;
    public MarkdownRun(string text,MarkdownStyle style) { Text=text; Style=style; }
}
public sealed class MarkdownLine {
    public List<MarkdownRun> Runs=new List<MarkdownRun>();
    public List<List<MarkdownRun>> Cells;
    public int Heading,Indent;
    public bool Code,Quote,Rule,TableHeader;
    public string Prefix="";
    public string Text { get { return Cells!=null?String.Join("\t",Cells.Select(c=>String.Concat(c.Select(r=>r.Text)))):Prefix+String.Concat(Runs.Select(r=>r.Text)); } }
}
public sealed class MarkdownDocument {
    public readonly List<MarkdownLine> Lines=new List<MarkdownLine>();
    public string Text { get { return String.Join("\n",Lines.Select(l=>l.Text)); } }
    public string Rtf(float size,int width) {
        var b=new StringBuilder(@"{\rtf1\ansi\deff0\uc1{\fonttbl{\f0 Microsoft YaHei UI;}{\f1 Consolas;}}{\colortbl;");
        foreach(var c in new[]{Look.Ink,Look.Field,Look.Border,Look.Accent,Look.Muted}) b.Append("\\red").Append(c.R).Append("\\green").Append(c.G).Append("\\blue").Append(c.B).Append(';');
        b.Append(@"}\viewkind4\deflang1033 ");
        int available=Math.Max(2400,(int)(width*1440/(Look.Scale*96))-160);
        for(int n=0;n<Lines.Count;n++) {
            var line=Lines[n];
            if(line.Cells!=null) {
                int count=line.Cells.Count,cellWidth=available/Math.Max(1,count);
                b.Append(@"\trowd\trgaph80\trleft0");
                for(int i=0;i<count;i++) {
                    foreach(var side in new[]{"t","b","l","r"}) b.Append("\\clbrdr").Append(side).Append(@"\brdrs\brdrw6\brdrcf3");
                    if(line.TableHeader) b.Append(@"\clcbpat2");
                    b.Append("\\cellx").Append(cellWidth*(i+1));
                }
                b.Append(' ');
                foreach(var cell in line.Cells) {
                    b.Append(@"\pard\plain\intbl\sa70\sb70\f0\cf1\fs").Append((int)Math.Round(size*2)).Append(' ');
                    Runs(b,cell,size,line.TableHeader,false,width/count); b.Append(@"\cell ");
                }
                b.Append(@"\row ");
                if(n==Lines.Count-1 || Lines[n+1].Cells==null) b.Append(@"\pard ");
                continue;
            }
            float points=line.Heading>0?size+Math.Max(1,5-line.Heading):size;
            b.Append(@"\pard\plain\f0\cf1\sa70\sb0\fs").Append((int)Math.Round(points*2));
            if(line.Text.Length==0 && !line.Code) b.Append(@"\fs2\sl80\slmult0\sa0");
            if(line.Heading>0) b.Append(@"\sb100\sa90");
            if(line.Prefix.Length>0) { int indent=300+line.Indent*260; b.Append("\\li").Append(indent).Append(@"\fi-260\tx").Append(indent); }
            else if(line.Quote) b.Append(@"\li260\ri120\cf5");
            else if(line.Code) b.Append(@"\li140\ri140\sa0\f1\highlight2");
            if(line.Rule) {
                // RichEdit does not draw borders on empty paragraphs. A one-pixel
                // DIB stretched in RTF gives an exact-width rule without adding
                // spoken/selectable characters or allocating native graphics.
                b.Append(@"\sa100 ").Append(RulePicture(available));
                if(n<Lines.Count-1) b.Append(@"\par ");
                continue;
            }
            b.Append(' '); Escape(b,line.Prefix);
            if(line.Runs.Count==1 && (line.Runs[0].Style&MarkdownStyle.DisplayMath)!=0) b.Append(@"\qc ");
            Runs(b,line.Runs,points,line.Heading>0,line.Quote,width);
            if(n<Lines.Count-1) b.Append(@"\par ");
        }
        return b.Append('}').ToString();
    }
    static string RulePicture(int widthTwips) {
        var color=Look.Muted;
        return @"{\pict\dibitmap0\picw1\pich1\picwgoal"+widthTwips+@"\pichgoal15 "+
            "28000000010000000100000001001800000000000400000000000000000000000000000000000000"+
            color.B.ToString("x2",CultureInfo.InvariantCulture)+color.G.ToString("x2",CultureInfo.InvariantCulture)+color.R.ToString("x2",CultureInfo.InvariantCulture)+"00}";
    }
    static void Runs(StringBuilder b,IEnumerable<MarkdownRun> runs,float size,bool bold,bool quote,int width) {
        foreach(var run in runs) {
            if((run.Style&MarkdownStyle.Math)!=0) {
                var picture=MathRender.Picture(run.Formula,size,width,(run.Style&MarkdownStyle.DisplayMath)!=0);
                if(picture!=null) { b.Append(picture); continue; }
            }
            bool code=(run.Style&MarkdownStyle.Code)!=0;
            b.Append(@"{\f").Append(code?1:0).Append("\\fs").Append((int)Math.Round(size*2));
            b.Append("\\cf").Append((run.Style&MarkdownStyle.Link)!=0?4:quote?5:1);
            if(code) b.Append(@"\highlight2");
            if(bold || (run.Style&MarkdownStyle.Bold)!=0) b.Append(@"\b");
            if((run.Style&MarkdownStyle.Italic)!=0) b.Append(@"\i");
            if((run.Style&MarkdownStyle.Strike)!=0) b.Append(@"\strike");
            if((run.Style&MarkdownStyle.Link)!=0) b.Append(@"\ul");
            b.Append(' '); Escape(b,run.Text); b.Append('}');
        }
    }
    static void Escape(StringBuilder b,string text) {
        foreach(char c in text) {
            if(c=='\\' || c=='{' || c=='}') b.Append('\\').Append(c);
            else if(c=='\t') b.Append(@"\tab ");
            else if(c=='\n') b.Append(@"\line ");
            else if(c>=32 && c<=126) b.Append(c);
            else if(c>=32) b.Append("\\u").Append((short)c).Append('?');
        }
    }
}
// A native-text Markdown renderer: no HTML execution or remote image loading.
public static class Markdown {
    static readonly Regex fence=new Regex(@"^ {0,3}(`{3,}|~{3,})(.*)$");
    static readonly Regex heading=new Regex(@"^ {0,3}(#{1,6})\s+(.*?)(?:\s+#+)?\s*$");
    static readonly Regex list=new Regex(@"^(\s*)([-+*]|\d{1,9}[.)])\s+(.+)$");
    public static string PlainText(string text) { return Parse(text).Text; }
    public static MarkdownDocument Parse(string text) {
        var document=new MarkdownDocument(); string[] source=(text??"").Replace("\r\n","\n").Replace('\r','\n').Split('\n');
        string delimiter=null;
        for(int i=0;i<source.Length;i++) {
            string value=source[i]; var fm=fence.Match(value);
            if(delimiter!=null) {
                if(fm.Success && fm.Groups[1].Value[0]==delimiter[0] && fm.Groups[1].Length>=delimiter.Length && String.IsNullOrWhiteSpace(fm.Groups[2].Value)) { delimiter=null; continue; }
                var code=new MarkdownLine { Code=true }; code.Runs.Add(new MarkdownRun(value,MarkdownStyle.Code)); document.Lines.Add(code); continue;
            }
            if(fm.Success) { delimiter=fm.Groups[1].Value; continue; }
            string trimmed=value.Trim();
            if(trimmed.StartsWith("$$") || trimmed.StartsWith(@"\[")) {
                string opening=trimmed.StartsWith("$$")?"$$":@"\[", closing=opening=="$$"?"$$":@"\]";
                string body=trimmed.Substring(2); int end=body.IndexOf(closing,StringComparison.Ordinal),last=i;
                while(end<0 && last+1<source.Length) { body+="\n"+source[++last]; end=body.IndexOf(closing,StringComparison.Ordinal); }
                if(end>=0) {
                    var mathLine=new MarkdownLine(); mathLine.Runs.Add(new MarkdownRun(opening+body.Substring(0,end)+closing,MarkdownStyle.Math|MarkdownStyle.DisplayMath) { Formula=body.Substring(0,end) }); document.Lines.Add(mathLine);
                    string after=body.Substring(end+closing.Length); if(after.Trim().Length>0) { var tail=new MarkdownLine(); tail.Runs=Inline(after,MarkdownStyle.Normal,0); document.Lines.Add(tail); }
                    i=last; continue;
                }
            }
            if(i+1<source.Length && IsTableRule(source[i+1]) && value.Contains("|") && TableCells(value).Count<=8) {
                int columns=TableCells(value).Count;
                AddTable(document,value,true,columns); i++;
                while(i+1<source.Length && source[i+1].Contains("|") && !String.IsNullOrWhiteSpace(source[i+1])) AddTable(document,source[++i],false,columns);
                continue;
            }
            var line=new MarkdownLine(); var hm=heading.Match(value);
            if(hm.Success) { line.Heading=hm.Groups[1].Length; value=hm.Groups[2].Value; }
            else if(i+1<source.Length && value.Trim().Length>0 && Regex.IsMatch(source[i+1],@"^ {0,3}(=+|-+)\s*$")) { line.Heading=source[i+1].Trim()[0]=='='?1:2; i++; }
            if(Regex.IsMatch(value,@"^ {0,3}((\*\s*){3,}|(-\s*){3,}|(_\s*){3,})$")) { line.Rule=true; document.Lines.Add(line); continue; }
            if(Regex.IsMatch(value,@"^ {0,3}>")) { line.Quote=true; value=Regex.Replace(value,@"^ {0,3}(>\s?)+",""); }
            var lm=list.Match(value);
            if(lm.Success) {
                line.Indent=Math.Min(6,lm.Groups[1].Value.Replace("\t","  ").Length/2);
                string marker=lm.Groups[2].Value; line.Prefix=(marker.Length==1?"•":marker)+"\t"; value=lm.Groups[3].Value;
                var check=Regex.Match(value,@"^\[([ xX])\]\s+");
                if(check.Success) { line.Prefix=check.Groups[1].Value==" "?"☐\t":"☑\t"; value=value.Substring(check.Length); }
            }
            line.Runs=Inline(value,MarkdownStyle.Normal,0); document.Lines.Add(line);
        }
        return document;
    }
    static bool IsTableRule(string line) {
        var cells=TableCells(line); return cells.Count>=2 && cells.All(c=>Regex.IsMatch(c.Trim(),@"^:?-{3,}:?$"));
    }
    static List<string> TableCells(string line) {
        line=line.Trim(); if(line.StartsWith("|")) line=line.Substring(1); if(line.EndsWith("|") && !line.EndsWith(@"\|")) line=line.Substring(0,line.Length-1);
        var cells=new List<string>(); var b=new StringBuilder(); bool code=false;
        for(int i=0;i<line.Length;i++) {
            char c=line[i];
            if(c=='\\' && i+1<line.Length) { b.Append(c).Append(line[++i]); continue; }
            if(c=='`') code=!code;
            if(c=='|' && !code) { cells.Add(b.ToString().Trim()); b.Clear(); } else b.Append(c);
        }
        cells.Add(b.ToString().Trim()); return cells;
    }
    static void AddTable(MarkdownDocument doc,string text,bool header,int columns) {
        var cells=TableCells(text); while(cells.Count<columns) cells.Add("");
        if(cells.Count>columns) { cells[columns-1]=String.Join(" | ",cells.Skip(columns-1)); cells=cells.Take(columns).ToList(); }
        var line=new MarkdownLine { TableHeader=header,Cells=cells.Select(c=>Inline(c,MarkdownStyle.Normal,0)).ToList() }; doc.Lines.Add(line);
    }
    static int Closing(string text,string token,int start,bool emphasis) {
        for(int i=start;i<=text.Length-token.Length;i++) {
            if(text[i]=='\\') { i++; continue; }
            if(String.CompareOrdinal(text,i,token,0,token.Length)!=0) continue;
            if(emphasis && (i==start || Char.IsWhiteSpace(text[i-1]))) continue;
            if(emphasis && token[0]=='_' && i+token.Length<text.Length && Char.IsLetterOrDigit(text[i+token.Length])) continue;
            return i;
        }
        return -1;
    }
    static List<MarkdownRun> Inline(string text,MarkdownStyle style,int depth) {
        var runs=new List<MarkdownRun>(); var plain=new StringBuilder();
        Action flush=delegate { if(plain.Length>0) { runs.Add(new MarkdownRun(plain.ToString(),style)); plain.Clear(); } };
        if(depth>12) { runs.Add(new MarkdownRun(text,style)); return runs; }
        for(int i=0;i<text.Length;) {
            char c=text[i];
            if(c=='\\' && i+1<text.Length && (text[i+1]=='(' || text[i+1]=='[')) {
                string close=text[i+1]=='('?@"\)":@"\]"; int end=text.IndexOf(close,i+2,StringComparison.Ordinal);
                if(end>=0) { flush(); runs.Add(new MarkdownRun(text.Substring(i,end+2-i),style|MarkdownStyle.Math|(text[i+1]=='['?MarkdownStyle.DisplayMath:MarkdownStyle.Normal)) { Formula=text.Substring(i+2,end-i-2) }); i=end+2; continue; }
            }
            if(c=='\\' && i+1<text.Length && @"\`*_{}[]()>#+-.!|~".Contains(text[i+1])) { plain.Append(text[i+1]); i+=2; continue; }
            if(c=='`') {
                int length=1; while(i+length<text.Length && text[i+length]=='`') length++;
                string token=new string('`',length); int end=text.IndexOf(token,i+length,StringComparison.Ordinal);
                if(end>=0) {
                    flush(); string code=text.Substring(i+length,end-i-length);
                    if(code.Length>1 && code[0]==' ' && code[code.Length-1]==' ' && code.Trim().Length>0) code=code.Substring(1,code.Length-2);
                    runs.Add(new MarkdownRun(code,style|MarkdownStyle.Code)); i=end+length; continue;
                }
            }
            if(c=='$') {
                int length=i+1<text.Length && text[i+1]=='$'?2:1; int end=text.IndexOf(new string('$',length),i+length,StringComparison.Ordinal);
                if(end>=0 && (length==2 || (i+1<text.Length && !Char.IsWhiteSpace(text[i+1]) && !Char.IsWhiteSpace(text[end-1])))) { flush(); runs.Add(new MarkdownRun(text.Substring(i,end+length-i),style|MarkdownStyle.Math|(length==2?MarkdownStyle.DisplayMath:MarkdownStyle.Normal)) { Formula=text.Substring(i+length,end-i-length) }); i=end+length; continue; }
            }
            int linkStart=c=='!' && i+1<text.Length && text[i+1]=='['?i+1:i;
            if(text[linkStart]=='[') {
                int bracket=Closing(text,"]",linkStart+1,false);
                if(bracket>=0 && bracket+1<text.Length && text[bracket+1]=='(') {
                    int nest=1,end=bracket+2;
                    for(;end<text.Length;end++) { if(text[end]=='\\') { end++; continue; } if(text[end]=='(') nest++; if(text[end]==')' && --nest==0) break; }
                    if(end<text.Length) {
                        flush(); string label=text.Substring(linkStart+1,bracket-linkStart-1);
                        if(linkStart!=i) runs.Add(new MarkdownRun("[Image: "+label+"]",style));
                        else runs.AddRange(Inline(label,style|MarkdownStyle.Link,depth+1));
                        i=end+1; continue;
                    }
                }
            }
            string marker=null; MarkdownStyle extra=MarkdownStyle.Normal;
            foreach(var pair in new[]{"***","___","**","__","~~","*","_"}) {
                if(i+pair.Length>=text.Length || String.CompareOrdinal(text,i,pair,0,pair.Length)!=0 || Char.IsWhiteSpace(text[i+pair.Length])) continue;
                if(pair[0]=='_' && i>0 && Char.IsLetterOrDigit(text[i-1])) continue;
                marker=pair; extra=pair.Length==3?MarkdownStyle.Bold|MarkdownStyle.Italic:pair=="~~"?MarkdownStyle.Strike:pair.Length==2?MarkdownStyle.Bold:MarkdownStyle.Italic; break;
            }
            if(marker!=null) {
                int end=Closing(text,marker,i+marker.Length,true);
                if(end>=0) { flush(); runs.AddRange(Inline(text.Substring(i+marker.Length,end-i-marker.Length),style|extra,depth+1)); i=end+marker.Length; continue; }
            }
            plain.Append(c); i++;
        }
        flush(); return runs;
    }
}
}
