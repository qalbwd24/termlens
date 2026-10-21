using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace TermLens {
public sealed class StudyImage {
    public string Id,Name;
    public int Width,Height;
}
public static class ImageInput {
    public const int MaximumCount=4;
    public static void ValidateModel(Settings settings) {
        Uri endpoint; if(Uri.TryCreate(settings.BaseUrl,UriKind.Absolute,out endpoint) && endpoint.Host.Equals("api.deepseek.com",StringComparison.OrdinalIgnoreCase) && settings.Model!="deepseek-flash" && settings.Model!="deepseek-v4-flash-vision-exp") throw new Exception(UiLanguage.T("For DeepSeek images, select deepseek-flash in Settings. The draft has been kept.","使用 DeepSeek 图片查询时，请在设置选择 deepseek-flash。草稿已保留。"));
    }
    public static string PathFor(string root,StudyImage image) {
        Guid id;
        if(image==null || !Guid.TryParseExact(image.Id,"N",out id)) throw new Exception(UiLanguage.T("Invalid saved image.","图片记录无效。"));
        return Path.Combine(root,"images",image.Id+".png");
    }
    public static StudyImage StoreFile(string root,string path) {
        var info=new FileInfo(path); if(info.Length>32*1024*1024) throw new Exception(UiLanguage.T("Choose an image smaller than 32 MB.","请选择小于 32 MB 的图片。"));
        using(var source=Image.FromFile(path)) return Store(root,source,Path.GetFileName(path));
    }
    public static StudyImage Store(string root,Image source,string name) {
        if(source.Width<=0 || source.Height<=0 || (long)source.Width*source.Height>40000000) throw new Exception(UiLanguage.T("This image is too large. Crop it first.","图片过大，请先裁剪。"));
        int edge=2048; byte[] bytes=null; int width=0,height=0;
        do {
            double scale=Math.Min(1,edge/(double)Math.Max(source.Width,source.Height)); width=Math.Max(1,(int)Math.Round(source.Width*scale)); height=Math.Max(1,(int)Math.Round(source.Height*scale));
            using(var bitmap=new Bitmap(width,height,PixelFormat.Format24bppRgb)) {
                using(var g=Graphics.FromImage(bitmap)) { g.Clear(Color.White); g.InterpolationMode=InterpolationMode.HighQualityBicubic; g.DrawImage(source,0,0,width,height); }
                using(var stream=new MemoryStream()) { bitmap.Save(stream,ImageFormat.Png); bytes=stream.ToArray(); }
            }
            edge=(int)(edge*.75);
        } while(bytes.Length>1500000 && edge>=384);
        if(bytes.Length>1500000) throw new Exception(UiLanguage.T("Crop this image to the part you want to read.","请把图片裁剪到需要查询的部分。"));
        var image=new StudyImage { Id=Guid.NewGuid().ToString("N"),Name=name??"Image",Width=width,Height=height };
        Directory.CreateDirectory(Path.Combine(root,"images")); File.WriteAllBytes(PathFor(root,image),bytes); return image;
    }
    public static List<string> DataUrls(string root,IList<StudyImage> images) {
        if(images==null || images.Count==0) return new List<string>();
        if(images.Count>MaximumCount) throw new Exception(UiLanguage.T("Attach up to 4 images per question.","每次最多添加 4 张图片。"));
        return images.Select(image=> {
            string path=PathFor(root,image);
            if(!File.Exists(path)) throw new Exception(UiLanguage.T("A saved image is missing. Attach it again.","已保存的图片丢失，请重新添加。"));
            var info=new FileInfo(path); if(info.Length>1500000) throw new Exception(UiLanguage.T("A saved image exceeds the size limit.","已保存的图片超过大小限制。"));
            return "data:image/png;base64,"+Convert.ToBase64String(File.ReadAllBytes(path));
        }).ToList();
    }
    public static Image Load(string root,StudyImage image) {
        using(var input=Image.FromFile(PathFor(root,image))) return new Bitmap(input);
    }
}
public sealed class ImageTile : Control {
    readonly StudyImage attachment;
    readonly string root;
    readonly Image thumbnail;
    readonly bool removable;
    public event Action Removed;
    public ImageTile(string root,StudyImage image,bool remove) {
        this.root=root; attachment=image; removable=remove;
        Size=new Size(Look.S(80),Look.S(62)); Margin=new Padding(0,0,Look.S(6),0); Cursor=Cursors.Hand;
        AccessibleName=(remove?UiLanguage.T("Attached image: ","已添加图片："):UiLanguage.T("Query image: ","查询图片："))+image.Name;
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
        try { using(var full=ImageInput.Load(root,image)) { float scale=Math.Min(Look.S(80)/(float)full.Width,Look.S(62)/(float)full.Height); thumbnail=new Bitmap(full,new Size(Math.Max(1,(int)(full.Width*scale)),Math.Max(1,(int)(full.Height*scale)))); } } catch { }
    }
    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.Clear(Look.Surface); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(var path=Look.Round(new RectangleF(0,0,Width-1,Height-1),Look.S(6))) {
            using(var fill=new SolidBrush(Look.Field)) e.Graphics.FillPath(fill,path);
            if(thumbnail!=null) {
                float scale=Math.Min((Width-Look.S(8))/(float)thumbnail.Width,(Height-Look.S(8))/(float)thumbnail.Height);
                var bounds=new RectangleF((Width-thumbnail.Width*scale)/2,(Height-thumbnail.Height*scale)/2,thumbnail.Width*scale,thumbnail.Height*scale);
                e.Graphics.DrawImage(thumbnail,bounds);
            }
            using(var pen=new Pen(Look.Border)) e.Graphics.DrawPath(pen,path);
        }
        if(removable) {
            var target=new Rectangle(Width-Look.S(24),0,Look.S(24),Look.S(24));
            using(var fill=new SolidBrush(Look.Field)) e.Graphics.FillRectangle(fill,target);
            TextRenderer.DrawText(e.Graphics,"×",Font,target,Look.Ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        }
    }
    protected override void OnMouseUp(MouseEventArgs e) {
        base.OnMouseUp(e); if(e.Button!=MouseButtons.Left) return;
        if(removable && e.X>=Width-Look.S(24) && e.Y<Look.S(24)) { if(Removed!=null) Removed(); return; }
        try {
            using(var viewer=new Form { Text=UiLanguage.T("Image preview","图片预览"),StartPosition=FormStartPosition.CenterParent,ShowInTaskbar=false,Size=new Size(Look.S(700),Look.S(500)) })
            using(var full=ImageInput.Load(root,attachment)) {
                var picture=new PictureBox { Image=full,Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.Zoom }; viewer.Controls.Add(picture); Theme.Apply(viewer); viewer.ShowDialog(FindForm());
            }
        } catch(Exception ex) { MessageBox.Show(FindForm(),ex.Message,"TermLens"); }
    }
    protected override void Dispose(bool disposing) { if(disposing && thumbnail!=null) thumbnail.Dispose(); base.Dispose(disposing); }
}
}
