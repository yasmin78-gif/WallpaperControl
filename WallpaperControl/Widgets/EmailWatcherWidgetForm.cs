using System.Drawing.Drawing2D;

namespace WallpaperControl;

internal sealed class EmailWatcherWidgetForm : Form
{
    private EmailWatcherSettings settings;
    private string language;
    private bool dark = NotesDialogStyle.ResolveDarkMode();
    private readonly WidgetDragHandler drag;
    private readonly CalendarViewport viewport = new(390);
    private readonly Action dismiss, open;
    private IReadOnlyList<EmailNotice> notices = [];
    private bool thumbDragging;
    private float thumbGrab;
    internal EmailWatcherWidgetForm(EmailWatcherSettings value, string language, Action<Point> moved, Action dismiss, Action open)
    {
        settings = value.Clone(); this.language = language; this.dismiss = dismiss; this.open = open;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None; ClientSize = new(390, 120);
        drag = new(this, () => settings.Locked, Render, moved);
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams { get { var value = base.CreateParams; value.ExStyle |= 0x80000 | 0x80 | 0x08000000; return value; } }
    internal void Apply(EmailWatcherSettings value, string lang) { settings = value.Clone(); language = lang; Render(); }
    internal void ApplyTheme(bool value) { dark = value; Render(); }
    internal void SetNotices(IReadOnlyList<EmailNotice> value) { notices = value; Render(); }
    protected override void OnShown(EventArgs e) { base.OnShown(e); Render(); }
    protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); Render(); }
    protected override void WndProc(ref Message m)
    {
        if (DesktopWidgetNative.HandleMouseActivation(ref m)) return;
        if (m.Msg == 0x020A) { viewport.Wheel((short)(m.WParam.ToInt64() >> 16), SystemInformation.MouseWheelScrollLines); Render(); m.Result = IntPtr.Zero; return; }
        base.WndProc(ref m);
    }
    private void Render()
    {
        if (IsDisposed || !IsHandleCreated) return;
        using var image = RenderBitmap(Screen.FromControl(this).WorkingArea.Height, DeviceDpi); ClientSize = image.Size; LayeredWidgetBitmap.Update(Handle, Location, image);
    }
    internal Bitmap RenderBitmap(int workHeight = 2000, int dpi = 96)
    {
        int rowHeight = settings.ShowPreview ? 122 : 96;
        viewport.Update(Math.Max(42, notices.Count * rowHeight), settings.MaximumHeight, workHeight, dpi);
        var image = new Bitmap((int)Math.Ceiling(390 * viewport.Scale), viewport.PhysicalHeight, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(image); g.Clear(Color.Transparent); g.ScaleTransform(viewport.Scale, viewport.Scale); g.SmoothingMode = SmoothingMode.AntiAlias;
        var palette = WidgetDrawing.GetPalette(settings.Style);
        using var path = WidgetDrawing.RoundedRectangle(new(1, 1, 388, viewport.LogicalHeight - 2), 12);
        using var fill = new SolidBrush(dark ? palette.panel : Color.FromArgb(235, AppTheme.WindowBackground(false)));
        using var text = new SolidBrush(dark ? palette.text : AppTheme.TextPrimary(false)); using var muted = new SolidBrush(dark ? palette.muted : AppTheme.TextSecondary(false));
        using var accent = new SolidBrush(dark ? palette.accent : Color.FromArgb(29, 105, 184)); using var border = new Pen(accent.Color, settings.Style == SystemWidgetStyle.Glow ? 2 : 1);
        using var font = new Font("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel); using var bold = new Font("Segoe UI", 13, FontStyle.Bold, GraphicsUnit.Pixel);
        using var single = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        using var wrapped = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.LineLimit };
        g.FillPath(fill, path); g.DrawPath(border, path);
        WidgetHeader.Draw(g, Localization.Get("EmailTitle", language), language, settings.Style, 390, 96, text.Color, accent.Color);
        g.DrawString("×", bold, accent, 358, 14);
        var saved = g.Save(); g.SetClip(viewport.ContentBounds); float y = 54 - viewport.ScrollOffset;
        foreach (var notice in notices.OrderByDescending(item => item.Received))
        {
            float width = viewport.ContentBounds.Width;
            if (y + rowHeight > 54 && y < viewport.ContentBounds.Bottom) {
                g.DrawString(notice.SenderName.Length == 0 ? notice.Address : notice.SenderName, bold, text, new RectangleF(14, y, width - 4, 20), single);
                g.DrawString(notice.Address, font, muted, new RectangleF(14, y + 21, width - 4, 18), single);
                g.DrawString(notice.Subject, font, text, new RectangleF(14, y + 42, width - 4, 32), wrapped);
                g.DrawString(Localization.Get("EmailNew", language) + " · " + notice.Received.ToLocalTime().ToString("g", System.Globalization.CultureInfo.GetCultureInfo(language)), font, accent, new RectangleF(14, y + 76, width - 4, 18), single);
                if (settings.ShowPreview) g.DrawString(notice.Preview, font, muted, new RectangleF(14, y + 97, width - 4, 20), single);
                using var line = new Pen(Color.FromArgb(60, accent.Color)); g.DrawLine(line, 14, y + rowHeight - 3, viewport.ContentBounds.Right, y + rowHeight - 3);
            }
            y += rowHeight;
        }
        g.Restore(saved); WidgetScrollbar.Draw(g, viewport, dark);
        if (settings.Account.HasInboxTarget) g.DrawString(Localization.Get("EmailOpenInbox", language), font, accent, new RectangleF(14, viewport.LogicalHeight - 23, 260, 20), single);
        return image;
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) { base.OnMouseDown(e); return; }
        var point = new PointF(e.X / viewport.Scale, e.Y / viewport.Scale);
        if (point.Y < 42 && point.X > 345) { dismiss(); return; }
        if (point.Y >= viewport.LogicalHeight - 25) { if (settings.Account.HasInboxTarget) open(); return; }
        if (viewport.CanScroll && viewport.Track.Contains(point)) {
            if (viewport.Thumb.Contains(point)) { thumbDragging = true; thumbGrab = point.Y - viewport.Thumb.Top; Capture = true; }
            else { viewport.SetOffset(viewport.ScrollOffset + (point.Y < viewport.Thumb.Top ? -1 : 1) * viewport.ViewportHeight); Render(); }
            return;
        }
        base.OnMouseDown(e);
    }
    protected override void OnMouseMove(MouseEventArgs e) { if (thumbDragging) { viewport.DragThumb(e.Y / viewport.Scale, thumbGrab); Render(); } else base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { if (thumbDragging) { thumbDragging = false; Capture = false; } else base.OnMouseUp(e); }
    protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) thumbDragging = false; base.OnMouseCaptureChanged(e); }
    protected override void Dispose(bool disposing) { if (disposing) { drag.Dispose(); notices=[]; } base.Dispose(disposing); }
}
