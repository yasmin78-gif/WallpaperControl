namespace WallpaperControl;

internal static class WidgetScrollbar
{
    internal static void Draw(Graphics graphics, CalendarViewport viewport, bool dark = true)
    {
        if (!viewport.CanScroll) return;
        using var track = new SolidBrush(dark ? Color.FromArgb(48, 61, 73) : Color.FromArgb(218, 225, 232));
        using var thumb = new SolidBrush(dark ? Color.FromArgb(225, 237, 247) : Color.FromArgb(28, 98, 157));
        using var border = new Pen(dark ? Color.White : Color.FromArgb(15, 62, 102));
        using var grip = new Pen(dark ? Color.FromArgb(48, 61, 73) : Color.White);
        graphics.FillRectangle(track, viewport.Track);
        graphics.FillRectangle(thumb, viewport.Thumb);
        var bounds = viewport.Thumb;
        graphics.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width, bounds.Height);
        float center = bounds.Top + bounds.Height / 2;
        graphics.DrawLine(grip, bounds.Left + 2, center - 2, bounds.Right - 2, center - 2);
        graphics.DrawLine(grip, bounds.Left + 2, center + 2, bounds.Right - 2, center + 2);
    }
}
