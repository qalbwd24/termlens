using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace TermLens {
public static partial class Tests {
    public static int ReadingFonts(string root) {
        string folder=TestRoot(root,"font-regression");
        try {
            using(var card=new EntryCard(new WorkEntry { Text="perpendicular",Answer="Two lines meet at an angle of 90 degrees.",Finished=true },14)) {
                card.Width=553; card.CreateControl();
                for(int i=0;i<8;i++) card.ApplyFont(14);
                Check(card.Original.Font.GetHeight(96)>0 && card.Answer.Font.GetHeight(96)>0,"Repeated application of the same 14-point font keeps both fonts usable");
            }
            foreach(float size in new[]{11f,14f,28f}) {
                var session=new WorkSession();
                session.Entries.Add(new WorkEntry { Text="A right angle",Answer=String.Concat(Enumerable.Repeat("Two lines meet like a corner of a square. 中文说明。\n",25)),Finished=true });
                using(var window=new WorkspaceWindow(session,new Settings { ReadingFontSize=size,ReadingWidth=553,ReadingHeight=613 })) {
                    window.Show();
                    for(int i=0;i<12;i++) {
                        window.ApplyReading(size,553+i%2*20,613);
                        foreach(var card in window.Cards) {
                            foreach(var text in new[]{card.Original,card.Answer}) {
                                window.Timeline.Wheel(-120,text.Font);
                                SendMessage(text.Handle,0x20A,new IntPtr(120<<16),IntPtr.Zero);
                                text.SelectionAnchor(); text.ContentHeight(text.Width);
                            }
                        }
                        window.Refresh();
                    }
                    Check(window.Cards[0].Answer.Font.GetHeight(96)>0,"Restored session, repeated settings, resizing and native wheel remain usable at "+size+" points");
                    foreach(float changed in new[]{9f,18f,28f,28f,14f,14f}) window.ApplyReading(changed,553,613);
                    Check(window.Cards[0].Original.Font.GetHeight(144)>0,"Zoom including repeated minimum/maximum values keeps valid fonts from "+size+" points");
                    window.Close();
                }
            }
            File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); return 0;
        } catch(Exception e) { results.Add(e.ToString()); File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); return 1; }
    }
}
}
