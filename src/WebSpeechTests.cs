using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace TermLens {
public static partial class Tests {
    public static int WebVoice(string root) {
        string folder=TestRoot(root,"web-speech");
        try {
            string mixed="perpendicular · 垂直的\n\n"+String.Concat(Enumerable.Repeat("Two lines meet at a right angle, like a corner of a square. ",20))+"这是一段用于检查分段的中文说明。";
            var parts=WebSpeech.Parts(mixed);
            Check(Regex.Replace(String.Concat(parts),@"\s+","")==Regex.Replace(mixed,@"\s+",""),"Baidu chunking keeps every character of a long bilingual explanation");
            Check(parts.All(p=>p.Length<=160) && parts.Count>5,"Baidu long speech uses short chunks rather than one oversized web request");
            Check(parts.Where(p=>Regex.IsMatch(p,@"[\u3400-\u9fff]")).All(p=>!Regex.IsMatch(p,"[A-Za-z]")),"Chinese glosses use separate chunks from English pronunciation");
            using(var web=new WebSpeech()) using(var cache=new SpeechCache(web.Synthesize,null)) {
                string text="The two edges of a page meet at a right angle.";
                var watch=Stopwatch.StartNew();
                byte[] clip=cache.PreloadAsync(text,"baidu-auto",-10).GetAwaiter().GetResult(); long cold=watch.ElapsedMilliseconds;
                watch.Restart(); var hit=cache.GetAsync(text,"baidu-auto",-10,CancellationToken.None).GetAwaiter().GetResult();
                Check(clip.Length>1000 && Object.ReferenceEquals(clip,hit),"Baidu web audio is synthesized without a key and reused from memory");
                results.Add("INFO: Baidu fixed sentence prepare="+cold+" ms; memory lookup="+watch.ElapsedMilliseconds+" ms.");
                string path=Path.Combine(folder,"baidu-sentence.mp3"); File.WriteAllBytes(path,hit); CheckPlayback(path);
                var zh=cache.PreloadAsync("垂直的","baidu-auto",-10).GetAwaiter().GetResult();
                Check(zh.Length>1000,"Chinese pronunciation also returns valid MP3 audio");
            }
            File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); return 0;
        } catch(Exception e) { results.Add("FAIL: "+e.GetType().Name+": "+e.Message); File.WriteAllLines(Path.Combine(folder,"results.txt"),results,Files.Utf8); return 1; }
    }
}
}
