using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TermLens {
// The bottom layer of the desktop renderer.  A window owns one BackdropSurface
// and all of its normal controls live inside that surface.  Custom wallpaper
// images and window opacity were removed, so this surface only paints the flat
// theme canvas colour; it is kept because every window's control tree already
// hangs off it.
public sealed class BackdropSurface : Panel {
    public BackdropSurface() {
        Dock=DockStyle.Fill; TabStop=false; Enabled=true;
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
        BackColor=Look.Canvas;
        Resize+=delegate { Invalidate(); };
    }
    public void Apply(Settings value,bool quick) { BackColor=Look.Canvas; Invalidate(); }
    protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Look.Canvas); }
}

// Installs the backdrop without changing the public behavior of the existing
// WinForms windows.  The host is deliberately small so the Core/SQLite/LLM
// layers do not depend on the renderer and can later be reused by a WPF shell.
public static class AppearanceHost {
    static BackdropSurface Find(Form form) { return form.Controls.OfType<BackdropSurface>().FirstOrDefault(); }
    public static void Install(Form form,Settings value,bool quick) {
        if(form==null || form.IsDisposed) return;
        var existing=Find(form);
        if(existing==null) {
            existing=new BackdropSurface();
            // Move existing top-level content under the surface.  Dock and
            // anchoring remain relative to the same client rectangle.
            var children=form.Controls.Cast<Control>().ToArray();
            form.Controls.Add(existing); existing.SendToBack();
            foreach(var child in children) { if(child==existing) continue; form.Controls.Remove(child); existing.Controls.Add(child); }
        }
        form.BackgroundImage=null; form.BackgroundImageLayout=ImageLayout.None;
        existing.Apply(value,quick); existing.SendToBack();
        ApplyTransparentContainers(existing);
    }
    public static void ApplyTransparentContainers(Form form) { if(form==null || form.IsDisposed) return; var surface=Find(form); if(surface!=null) ApplyTransparentContainers(surface); }
    static void ApplyTransparentContainers(Control root) {
        foreach(Control child in root.Controls) {
            if(SettingsPages.UsesOpaqueSurface(child)) { SettingsPages.ApplyOpaqueSurface(child); continue; }
            if((child is Panel || child is TableLayoutPanel || child is FlowLayoutPanel || child is SplitContainer || child is TabControl || child is TabPage || child is SettingsPages) && !(child is FieldFrame)) child.BackColor=Color.Transparent;
            ApplyTransparentContainers(child);
        }
    }
}
}
