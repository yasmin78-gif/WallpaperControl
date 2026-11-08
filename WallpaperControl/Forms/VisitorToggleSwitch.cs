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
        DrawSwitch(e.Graphics, new Rectangle(4, 4, Width - 8, Height - 8), Checked, Enabled);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
    }
    internal static void DrawSwitch(Graphics graphics, Rectangle area, bool active, bool enabled)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int height = Math.Max(8, area.Height), width = Math.Min(area.Width, height * 2);
        Rectangle bounds = new(area.X, area.Y, width, height);
        using GraphicsPath path = new();
        path.AddArc(bounds.Left, bounds.Top, height, height, 90, 180);
        path.AddArc(bounds.Right - height, bounds.Top, height, height, 270, 180); path.CloseFigure();
        using SolidBrush track = new(!enabled ? Color.DimGray : active ? Color.SeaGreen : Color.SlateGray);
        graphics.FillPath(track, path);
        using SolidBrush thumb = new(Color.White);
        int diameter = height - 6;
        graphics.FillEllipse(thumb, active ? bounds.Right - diameter - 3 : bounds.Left + 3, bounds.Top + 3, diameter, diameter);
    }
    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
}
