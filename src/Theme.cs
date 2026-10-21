using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TermLens {
public static class Theme {
    static readonly Timer watch=new Timer { Interval=1500 };
    public static bool Dark { get; private set; }
    internal static bool? TestDark;
    static bool? preferredDark;
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr handle,int attribute,ref int value,int size);
    [DllImport("uxtheme.dll",CharSet=CharSet.Unicode)] static extern int SetWindowTheme(IntPtr handle,string app,string id);
    public static void Start() { Dark=ReadDark(); watch.Tick+=delegate { Refresh(); }; watch.Start(); }
    static bool ReadDark() {
        if(TestDark.HasValue) return TestDark.Value;
        if(preferredDark.HasValue) return preferredDark.Value;
        try { return Convert.ToInt32(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",1))==0; }
        catch { return false; }
    }
    public static void SetPreferred(bool dark) { preferredDark=dark; if(Dark!=dark) { Dark=dark; foreach(Form form in Application.OpenForms.Cast<Form>().ToArray()) Apply(form); } }
    public static void Refresh() {
        bool next=ReadDark(); if(Dark==next) return; Dark=next;
        foreach(Form form in Application.OpenForms.Cast<Form>().ToArray()) Apply(form);
    }
    public static void Apply(Control control) {
        var bubble=control as SelectionBubble;
        control.BackColor=Look.Surface;
        if(control is SoftButton || control is Label || control is CheckBox || control is RadioButton) control.BackColor=Color.Transparent;
        if(control is MainWindow || control is SettingsPages || control.Name=="SettingsShell" || control.Name=="SettingsNavigation" || (control.Parent!=null && control.Parent.Name=="SettingsShell")) control.BackColor=Look.Canvas;
        if(control is FieldFrame) control.BackColor=Color.Transparent;
        else if(control.Parent is FieldFrame || (control.Parent!=null && control.Parent.Name=="ComposerViewport")) control.BackColor=Look.Field;
        if(SettingsPages.UsesOpaqueSurface(control)) control.BackColor=SettingsPages.OpaqueBackColor(control);
        control.ForeColor=Look.Ink;
        var combo=control as ComboBox;
        if(combo!=null && combo.DrawMode!=DrawMode.OwnerDrawFixed) {
            combo.DrawMode=DrawMode.OwnerDrawFixed;
            combo.DrawItem+=delegate(object sender,DrawItemEventArgs e) {
                using(var fill=new SolidBrush(Look.Surface)) e.Graphics.FillRectangle(fill,e.Bounds);
                string label=e.Index>=0?Convert.ToString(combo.Items[e.Index]):combo.Text;
                TextRenderer.DrawText(e.Graphics,label,combo.Font,e.Bounds,Look.Ink,TextFormatFlags.Left|TextFormatFlags.VerticalCenter);
                e.DrawFocusRectangle();
            };
        }
        var rich=control as RichTextBox;
        var study=control as StudyText;
        if(study!=null && study.MarkdownEnabled) study.RefreshMarkdownTheme();
        else if(rich!=null) {
            int start=rich.SelectionStart,length=rich.SelectionLength;
            rich.SelectAll(); rich.SelectionColor=Look.Ink; rich.Select(start,length);
        }
        var tab=control as TabControl;
        if(tab!=null && tab.DrawMode!=TabDrawMode.OwnerDrawFixed) {
            tab.DrawMode=TabDrawMode.OwnerDrawFixed;
            tab.DrawItem+=delegate(object sender,DrawItemEventArgs e) {
                using(var fill=new SolidBrush(Look.Surface)) e.Graphics.FillRectangle(fill,e.Bounds);
                TextRenderer.DrawText(e.Graphics,tab.TabPages[e.Index].Text,tab.Font,e.Bounds,e.Index==tab.SelectedIndex?Look.Accent:Look.Ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            };
        }
        foreach(Control child in control.Controls) Apply(child);
        if(control.IsHandleCreated && (control is TextBoxBase || control is ComboBox || control is ListBox || control is NumericUpDown || control is ScrollableControl)) { try { SetWindowTheme(control.Handle,Dark?"DarkMode_Explorer":"Explorer",null); } catch { } }
        var popup=control as ResultPopup;
        if(popup!=null) popup.RefreshTheme();
        var form=control as Form;
        if(form!=null) { WindowAppearance.ApplyContainers(form); if(form.IsHandleCreated) try { int value=Dark?1:0; DwmSetWindowAttribute(form.Handle,20,ref value,sizeof(int)); } catch { } }
        control.Invalidate(true);
    }
}
}
