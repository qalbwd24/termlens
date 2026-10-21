using System;
using System.Drawing;
using System.Windows.Forms;

namespace TermLens {
// The row keeps a known width and computes height from actual wrapping. This
// avoids AutoSize/GrowAndShrink's circular width negotiation inside a table.
public sealed class SettingsActionRow : FlowLayoutPanel {
    bool sizing;
    public SettingsActionRow() {
        Dock=DockStyle.Fill; AutoSize=false; Height=Look.S(42); WrapContents=true;
        Padding=new Padding(0,Look.S(3),0,Look.S(3));
    }
    protected override void OnLayout(LayoutEventArgs e) {
        base.OnLayout(e);
        if(sizing || ClientSize.Width<=0) return;
        int bottom=Padding.Top;
        foreach(Control child in Controls) if(child.Visible) bottom=Math.Max(bottom,child.Bottom+child.Margin.Bottom);
        int wanted=Math.Max(Look.S(38),bottom+Padding.Bottom);
        if(Height==wanted) return;
        sizing=true;
        try { Height=wanted; if(Parent!=null) Parent.PerformLayout(this,"Height"); }
        finally { sizing=false; }
    }
    public override Size GetPreferredSize(Size proposedSize) { return new Size(proposedSize.Width,Height); }
}
}
