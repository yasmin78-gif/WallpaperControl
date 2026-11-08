using System.Drawing.Drawing2D;

namespace WallpaperControl;

// Standard CheckBox keyboard/accessibility semantics with a compact switch presentation.
internal sealed class VisitorToggleSwitch : CheckBox
{
    internal VisitorToggleSwitch()
    {
        AutoSize = false; Size = new Size(76, 34);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        AccessibleRole = AccessibleRole.CheckButton;
        Cursor = Cursors.Hand;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int height = Math.Max(8, Height - 8), width = Math.Min(Width - 8, height * 2);
        Rectangle bounds = new(4, 4, width, height);
        using GraphicsPath path = new();
        path.AddArc(bounds.Left, bounds.Top, height, height, 90, 180);
        path.AddArc(bounds.Right - height, bounds.Top, height, height, 270, 180); path.CloseFigure();
        using SolidBrush track = new(!Enabled ? Color.DimGray : Checked ? Color.SeaGreen : Color.SlateGray);
        e.Graphics.FillPath(track, path);
        using SolidBrush thumb = new(Color.White);
        int diameter = height - 6;
        e.Graphics.FillEllipse(thumb, Checked ? bounds.Right - diameter - 3 : bounds.Left + 3, bounds.Top + 3, diameter, diameter);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
    }
    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
}
