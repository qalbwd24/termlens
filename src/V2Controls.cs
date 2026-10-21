using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TermLens {
// Small vector paths avoid missing icon-font glyphs on clean Windows installs.
public static class V2Icons {
    public static void Set(SoftButton button,string icon,string accessibleLabel,ToolTip tips) {
        button.IconName=icon; button.Quiet=true;
        button.AccessibleName=accessibleLabel; button.AccessibleRole=AccessibleRole.PushButton;
        if(tips!=null) { tips.InitialDelay=1000; tips.ReshowDelay=1000; tips.AutoPopDelay=6000; tips.SetToolTip(button,accessibleLabel); }
    }
    public static void Draw(Graphics graphics,string name,Rectangle bounds,Color color) {
        Draw(graphics,name,bounds,color,19f,3);
    }
    internal static void Draw(Graphics graphics,string name,Rectangle bounds,Color color,float logicalSize,int inset) {
        float size=Math.Min(Look.S(logicalSize),Math.Min(bounds.Width-inset*2,bounds.Height-inset*2));
        if(size<4) return;
        var state=graphics.Save();
        try {
            graphics.TranslateTransform(bounds.Left+(bounds.Width-size)/2f,bounds.Top+(bounds.Height-size)/2f);
            graphics.ScaleTransform(size/24f,size/24f); graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var pen=new Pen(color,2f)) using(var brush=new SolidBrush(color)) {
                pen.StartCap=LineCap.Round; pen.EndCap=LineCap.Round; pen.LineJoin=LineJoin.Round;
                switch(name) {
                    case "close": graphics.DrawLine(pen,6,6,18,18); graphics.DrawLine(pen,18,6,6,18); break;
                    case "back": graphics.DrawLine(pen,5,12,20,12); graphics.DrawLines(pen,new[]{new PointF(11,5),new PointF(4,12),new PointF(11,19)}); break;
                    case "copy": graphics.DrawRectangle(pen,8,8,12,13); graphics.DrawLines(pen,new[]{new PointF(16,5),new PointF(16,3),new PointF(3,3),new PointF(3,16),new PointF(5,16)}); break;
                    case "quote":
                        graphics.DrawRectangle(pen,3,5,6,7); graphics.DrawArc(pen,2,7,7,11,0,85);
                        graphics.DrawRectangle(pen,14,5,6,7); graphics.DrawArc(pen,13,7,7,11,0,85); break;
                    case "save": case "saved":
                        var bookmark=new[]{new PointF(6,3),new PointF(18,3),new PointF(18,21),new PointF(12,17),new PointF(6,21)};
                        if(name=="saved") graphics.FillPolygon(brush,bookmark); else graphics.DrawPolygon(pen,bookmark); break;
                    case "listen":
                        graphics.DrawPolygon(pen,new[]{new PointF(3,9),new PointF(7,9),new PointF(12,5),new PointF(12,19),new PointF(7,15),new PointF(3,15)});
                        graphics.DrawArc(pen,9,7,9,10,-65,130); graphics.DrawArc(pen,9,3,14,18,-65,130); break;
                    case "stop": graphics.FillRectangle(brush,6,6,12,12); break;
                    case "retry": graphics.DrawArc(pen,4,4,16,16,-50,295); graphics.DrawLines(pen,new[]{new PointF(15,3),new PointF(20,6),new PointF(20,1)}); break;
                    case "history": graphics.DrawArc(pen,4,4,16,16,-150,325); graphics.DrawLines(pen,new[]{new PointF(2,3),new PointF(3,9),new PointF(8,8)}); graphics.DrawLines(pen,new[]{new PointF(12,7),new PointF(12,12),new PointF(16,14)}); break;
                    case "pin": graphics.DrawPolygon(pen,new[]{new PointF(8,3),new PointF(16,3),new PointF(15,10),new PointF(19,14),new PointF(5,14),new PointF(9,10)}); graphics.DrawLine(pen,12,14,12,22); break;
                    case "settings":
                        // Eight outlined teeth and a central bore distinguish this
                        // from the previous ring-and-rays (sun-like) symbol.
                        var gear=new PointF[32];
                        double[] toothAngles={-22.5,-11.0,11.0,22.5};
                        for(int i=0;i<gear.Length;i++) {
                            int phase=i%4;
                            double angle=(i/4*45+toothAngles[phase])*Math.PI/180.0;
                            float radius=phase==1 || phase==2?10f:7.6f;
                            gear[i]=new PointF(12+(float)Math.Cos(angle)*radius,12+(float)Math.Sin(angle)*radius);
                        }
                        graphics.DrawPolygon(pen,gear); graphics.DrawEllipse(pen,8.5f,8.5f,7,7); break;
                    case "ask":
                        using(var bubble=new GraphicsPath()) {
                            bubble.AddLine(7,3,17,3); bubble.AddArc(16,3,5,5,270,90);
                            bubble.AddLine(21,5.5f,21,14.5f); bubble.AddArc(16,12,5,5,0,90);
                            bubble.AddLine(18.5f,17,9,17); bubble.AddLine(9,17,4,21);
                            bubble.AddLine(4,21,4,16); bubble.AddArc(3,12,5,5,90,90);
                            bubble.AddLine(3,14.5f,3,5.5f); bubble.AddArc(3,3,5,5,180,90);
                            bubble.CloseFigure(); graphics.DrawPath(pen,bubble);
                        }
                        graphics.DrawLine(pen,8,8,16,8); graphics.DrawLine(pen,8,12,13,12); break;
                    case "image": graphics.DrawRectangle(pen,3,4,18,16); graphics.DrawEllipse(pen,6,7,3,3); graphics.DrawLines(pen,new[]{new PointF(4,18),new PointF(10,12),new PointF(14,16),new PointF(17,12),new PointF(21,16)}); break;
                    case "send": graphics.DrawPolygon(pen,new[]{new PointF(3,3),new PointF(22,12),new PointF(3,21),new PointF(7,12)}); graphics.DrawLine(pen,7,12,21,12); break;
                    case "polish": graphics.DrawLines(pen,new[]{new PointF(5,20),new PointF(5,16),new PointF(16,5),new PointF(20,9),new PointF(9,20),new PointF(5,20)}); graphics.DrawLine(pen,13,8,17,12); graphics.DrawLine(pen,18,2,18,4); graphics.DrawLine(pen,21,5,23,5); break;
                    case "explain": graphics.DrawEllipse(pen,3,3,14,14); graphics.DrawLine(pen,15,15,21,21); graphics.DrawLine(pen,10,7,10,11); graphics.FillEllipse(brush,9.1f,13,1.8f,1.8f); break;
                    case "fit": graphics.DrawLines(pen,new[]{new PointF(3,9),new PointF(3,3),new PointF(9,3)}); graphics.DrawLines(pen,new[]{new PointF(15,3),new PointF(21,3),new PointF(21,9)}); graphics.DrawLines(pen,new[]{new PointF(21,15),new PointF(21,21),new PointF(15,21)}); graphics.DrawLines(pen,new[]{new PointF(9,21),new PointF(3,21),new PointF(3,15)}); break;
                    default: graphics.DrawEllipse(pen,4,4,16,16); graphics.FillEllipse(brush,11,11,2,2); break;
                }
            }
        } finally { graphics.Restore(state); }
    }
}
// Maximum is the maximum scroll offset, NOT the content height.
public sealed class SlimScrollBar : Control {
    int maximum,position,viewport=1,dragY,dragValue;
    bool hovering,dragging;
    public event EventHandler ValueChanged;
    public int Maximum { get { return maximum; } set { maximum=Math.Max(0,value); Value=position; Invalidate(); } }
    public int Viewport { get { return viewport; } set { viewport=Math.Max(1,value); Invalidate(); } }
    public int Value { get { return position; } set { int next=Math.Max(0,Math.Min(maximum,value)); if(next==position) return; position=next; Invalidate(); if(ValueChanged!=null) ValueChanged(this,EventArgs.Empty); } }
    public SlimScrollBar() {
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
        Width=Look.S(10); TabStop=true; AccessibleName="Scroll reading area"; AccessibleRole=AccessibleRole.ScrollBar;
        Cursor=Cursors.Default;
    }
    Rectangle Thumb {
        get {
            int available=Math.Max(1,ClientSize.Height-4);
            int height=maximum==0?available:Math.Max(Look.S(24),(int)(available*(double)viewport/((double)maximum+viewport)));
            height=Math.Min(available,height);
            int y=2+(maximum==0?0:(int)((available-height)*(double)position/maximum));
            int width=Math.Max(2,Look.S(hovering||dragging?5:3));
            return new Rectangle(Math.Max(0,(ClientSize.Width-width)/2),y,width,height);
        }
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e); if(maximum<=0) return;
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        Rectangle thumb=Thumb;
        using(var b=new SolidBrush(hovering||dragging?Look.Muted:Look.Border))
        using(var path=Look.Round(thumb,thumb.Width/2f)) e.Graphics.FillPath(b,path);
        if(Focused) ControlPaint.DrawFocusRectangle(e.Graphics,ClientRectangle);
    }
    protected override void OnMouseEnter(EventArgs e) { hovering=true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovering=false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e); if(e.Button!=MouseButtons.Left || maximum<=0) return;
        Focus(); var thumb=Thumb;
        // The full control width is the hit target even when its line is narrow.
        if(e.Y>=thumb.Top && e.Y<=thumb.Bottom) { dragging=true; dragY=e.Y; dragValue=position; Capture=true; }
        else Value+=e.Y<thumb.Top?-viewport:viewport;
        Invalidate();
    }
    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e); if(!dragging) return;
        int travel=Math.Max(1,ClientSize.Height-4-Thumb.Height);
        Value=dragValue+(int)((e.Y-dragY)*(double)maximum/travel);
    }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if(e.Button==MouseButtons.Left) { dragging=false; Capture=false; Invalidate(); } }
    protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if(!Capture) { dragging=false; Invalidate(); } }
    protected override void OnMouseWheel(MouseEventArgs e) { Value-=e.Delta/120*Math.Max(Look.S(24),viewport/12); base.OnMouseWheel(e); }
    protected override bool IsInputKey(Keys keyData) { Keys key=keyData&Keys.KeyCode; return key==Keys.Up || key==Keys.Down || key==Keys.PageUp || key==Keys.PageDown || key==Keys.Home || key==Keys.End || base.IsInputKey(keyData); }
    protected override void OnKeyDown(KeyEventArgs e) {
        int step=Math.Max(Look.S(20),viewport/12);
        switch(e.KeyCode) { case Keys.Up:Value-=step;break; case Keys.Down:Value+=step;break; case Keys.PageUp:Value-=viewport;break; case Keys.PageDown:Value+=viewport;break; case Keys.Home:Value=0;break; case Keys.End:Value=maximum;break; default:base.OnKeyDown(e);return; }
        e.Handled=true;
    }
}
}
