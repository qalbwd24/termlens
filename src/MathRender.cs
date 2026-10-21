using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfMath.Parsers;
using WpfMath.Rendering;
using XamlMath;

namespace TermLens {
internal sealed class FormulaPictureCache {
    sealed class Item { internal string Picture; internal LinkedListNode<string> Node; }
    readonly Dictionary<string,Item> items=new Dictionary<string,Item>();
    readonly LinkedList<string> recent=new LinkedList<string>();
    readonly int capacity;
    internal FormulaPictureCache(int capacity) { if(capacity<1) throw new ArgumentOutOfRangeException("capacity"); this.capacity=capacity; }
    internal int Count { get { return items.Count; } }
    internal bool TryGet(string key,out string picture) {
        Item item;
        if(!items.TryGetValue(key,out item)) { picture=null; return false; }
        recent.Remove(item.Node); recent.AddLast(item.Node); picture=item.Picture; return true;
    }
    internal void Put(string key,string picture) {
        Item item;
        if(items.TryGetValue(key,out item)) { item.Picture=picture; recent.Remove(item.Node); recent.AddLast(item.Node); return; }
        if(items.Count>=capacity) { items.Remove(recent.First.Value); recent.RemoveFirst(); }
        items.Add(key,new Item { Picture=picture,Node=recent.AddLast(key) });
    }
}
// Formula pictures are cached by source, size, width and palette. Scrolling
// never reruns TeX parsing or bitmap rendering. Invalid/incomplete TeX stays text.
public static class MathRender {
    [DllImport("gdiplus.dll")] static extern uint GdipEmfToWmfBits(IntPtr emf,uint size,byte[] data,int mapMode,int flags);
    [DllImport("gdi32.dll")] static extern bool DeleteEnhMetaFile(IntPtr handle);
    static readonly FormulaPictureCache cache=new FormulaPictureCache(192);
    public static int RenderedCount { get; private set; }
    public static string Picture(string source,float points,int width,bool display) {
        if(String.IsNullOrWhiteSpace(source) || source.Length>6000) return null;
        string key=source+"\0"+points.ToString(CultureInfo.InvariantCulture)+":"+width+":"+display+":"+Theme.Dark+":"+Look.Scale.ToString(CultureInfo.InvariantCulture);
        string result; if(cache.TryGet(key,out result)) return result;
        result=null;
        try {
            double size=points*96/72.0;
            var ink=Look.Ink; var bg=Look.Surface;
            var foreground=new SolidColorBrush(Color.FromRgb(ink.R,ink.G,ink.B));
            var background=new SolidColorBrush(Color.FromRgb(bg.R,bg.G,bg.B));
            var formula=WpfTeXFormulaParser.Instance.Parse(source.Trim());
            var environment=WpfTeXEnvironment.Create(display?TexStyle.Display:TexStyle.Text,size,"Arial",foreground,background);
            var bitmap=formula.RenderToBitmap(environment,size,2,2,96*Look.Scale);
            if(bitmap.PixelWidth<=8000 && bitmap.PixelHeight<=4000) {
                double factor=Math.Min(1,Math.Max(60,width/Look.Scale)/(bitmap.PixelWidth/(double)Look.Scale));
                int goalWidth=(int)Math.Round(bitmap.PixelWidth*1440/(96*Look.Scale)*factor);
                int goalHeight=(int)Math.Round(bitmap.PixelHeight*1440/(96*Look.Scale)*factor);
                var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using(var stream=new MemoryStream()) {
                    encoder.Save(stream); stream.Position=0;
                    byte[] bytes;
                    using(var sourceImage=new System.Drawing.Bitmap(stream))
                    using(var image=new System.Drawing.Bitmap(sourceImage.Width,sourceImage.Height,System.Drawing.Imaging.PixelFormat.Format24bppRgb))
                    using(var screen=System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) {
                        // WMF has no alpha blending; flatten before recording or
                        // dark glyphs become corrupted black/pink raster blocks.
                        using(var flatten=System.Drawing.Graphics.FromImage(image)) { flatten.Clear(bg); flatten.DrawImageUnscaled(sourceImage,0,0); }
                        IntPtr hdc=screen.GetHdc();
                        System.Drawing.Imaging.Metafile metafile;
                        try { metafile=new System.Drawing.Imaging.Metafile(hdc,new System.Drawing.Rectangle(0,0,image.Width,image.Height),System.Drawing.Imaging.MetafileFrameUnit.Pixel,System.Drawing.Imaging.EmfType.EmfOnly); }
                        finally { screen.ReleaseHdc(hdc); }
                        using(metafile) {
                            using(var g=System.Drawing.Graphics.FromImage(metafile)) { g.Clear(bg); g.DrawImage(image,0,0,image.Width,image.Height); }
                            IntPtr handle=metafile.GetHenhmetafile();
                            try { uint length=GdipEmfToWmfBits(handle,0,null,8,0); bytes=new byte[length]; GdipEmfToWmfBits(handle,length,bytes,8,0); }
                            finally { DeleteEnhMetaFile(handle); }
                        }
                    }
                    var b=new StringBuilder("{\\pict\\wmetafile8\\picw"+bitmap.PixelWidth+"\\pich"+bitmap.PixelHeight+"\\picwgoal"+goalWidth+"\\pichgoal"+goalHeight+" ");
                    foreach(byte value in bytes) b.Append(value.ToString("x2",CultureInfo.InvariantCulture));
                    result=b.Append('}').ToString(); RenderedCount++;
                }
            }
        } catch(Exception) { /* A broken formula must never break an answer or streaming. */ }
        cache.Put(key,result); return result;
    }
}
}

