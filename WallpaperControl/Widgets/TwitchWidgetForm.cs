using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;

namespace WallpaperControl;

/// <summary>Layered Twitch list using the existing widget palette, DPI viewport and drag behavior.</summary>
internal sealed class TwitchWidgetForm : Form
{
    private const int LogicalWidth = 390, RowHeight = TwitchRowPresentation.RowHeight;
    private readonly CalendarViewport viewport = new(LogicalWidth);
    private readonly TwitchService service;
    private readonly WidgetDragHandler drag;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Action<Uri> open;
    private readonly List<(string Id, Uri Url, RectangleF Bounds)> rows = new();
    private readonly TwitchAvatarCache avatars;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly System.Windows.Forms.Timer displayTimer = new() { Interval = 1000 };
    private WidgetSettings settings;
    private bool dark, suspended, powerSuspended, rendering, thumbDragging, widgetDragging;
    private float thumbGrabOffset;
    private Uri? pressed;
    private bool refreshPressed;
    internal static RectangleF RefreshBounds => new(LogicalWidth - 44, 12, 30, 28);
    internal TwitchWidgetForm(TwitchService service, WidgetSettings settings, Action<Point> moved, Action<Uri>? open = null,
        TwitchAvatarCache? avatars = null, Func<DateTimeOffset>? utcNow = null)
    {
        this.service = service; this.settings = settings.Clone(); this.open = open ?? (url => TwitchLinks.Open(url)); dark = NotesDialogStyle.ResolveDarkMode();
        this.avatars = avatars ?? new(service.GetProfileImagesAsync); this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        this.avatars.Changed += AvatarChanged; displayTimer.Tick += DisplayTick;
        Text = Localization.Get("TwitchTitle", settings.ClockLanguageCode); AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
        ClientSize = new(LogicalWidth, 120); Location = WidgetSettings.EnsureVisible(settings.TwitchLocation, Size);
        drag = new WidgetDragHandler(this, () => this.settings.TwitchLocked, Render, moved); service.Changed += RefreshData;
    }
    private void RefreshData()
    {
        if (IsDisposed || Disposing) return;
        if (IsHandleCreated && InvokeRequired) { BeginInvoke(RefreshData); return; }
        Render();
        UpdateDisplayTimer();
        if (!service.Connected) avatars.Clear();
        else if (service.Busy) avatars.CancelPending();
        else QueueAvatars();
    }
    private void AvatarChanged()
    {
        if (IsDisposed || Disposing || !IsHandleCreated) return;
        if (InvokeRequired) { BeginInvoke(AvatarChanged); return; }
        Render();
    }
    private async void QueueAvatars()
    {
        if (suspended || powerSuspended || service.Busy || !service.Connected || IsDisposed || Disposing || !IsHandleCreated || !Visible) return;
        try { await avatars.EnsureAsync(rows.Select(r => r.Id).ToArray(), lifetime.Token); }
        catch (OperationCanceledException) { }
    }
    private void UpdateDisplayTimer()
    { if (!suspended && !powerSuspended && IsHandleCreated && Visible && service.Streams.Count > 0) displayTimer.Start(); else displayTimer.Stop(); }
    private void DisplayTick(object? sender, EventArgs e) => RefreshDisplayTick();
    internal void RefreshDisplayTick() { if (!suspended && !powerSuspended && Visible && service.Streams.Count > 0) Render(); } // Cached data only, no network.
    internal void SetPowerSuspended(bool value)
    { powerSuspended = value; if (value) avatars.CancelPending(); else { Render(); QueueAvatars(); } UpdateDisplayTimer(); }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x00080000 | 0x00000080 | 0x08000000; return cp; } }
    internal void Apply(WidgetSettings value) { settings = value.Clone(); Text = Localization.Get("TwitchTitle", value.ClockLanguageCode); Render(); QueueAvatars(); }
    internal void ApplyTheme(bool value) { dark = value; Render(); }
    internal void SetActivitySuspended(bool value)
    {
        suspended = value;
        if (value) { pressed = null; refreshPressed = false; thumbDragging = false; Capture = false; avatars.CancelPending(); }
        else { Render(); QueueAvatars(); }
        UpdateDisplayTimer();
    }
    protected override void OnShown(EventArgs e) { base.OnShown(e); RefreshData(); }
    protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); Render(); QueueAvatars(); }
    protected override void WndProc(ref Message m)
    {
        if (DesktopWidgetNative.HandleMouseActivation(ref m)) return;
        if (m.Msg == 0x020A)
        {
            int packed = unchecked((int)m.LParam.ToInt64());
            ScrollWheel(PointToClient(new((short)(packed & 0xffff), (short)((packed >> 16) & 0xffff))), (short)((m.WParam.ToInt64() >> 16) & 0xffff), SystemInformation.MouseWheelScrollLines);
            m.Result = IntPtr.Zero; return;
        }
        base.WndProc(ref m); if (m.Msg is 0x007E or 0x001A) { Render(); QueueAvatars(); }
    }
    private void Render()
    {
        if (suspended || powerSuspended || rendering || !IsHandleCreated || IsDisposed || Disposing) return;
        rendering = true;
        try
        {
            var area = Screen.FromControl(this).WorkingArea;
            using var bitmap = RenderBitmap(area.Height, DeviceDpi); ClientSize = bitmap.Size;
            if (!widgetDragging) Location = new(Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width)), Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height)));
            LayeredWidgetBitmap.Update(Handle, Location, bitmap);
        }
        finally { rendering = false; }
    }
    internal Bitmap RenderBitmap(int heightLimit = 2000, int dpi = 96)
    {
        string lang = settings.ClockLanguageCode; var culture = CultureInfo.GetCultureInfo(lang);
        var now = utcNow();
        var streams = service.Streams;
        viewport.Update(Math.Max(55, streams.Count * RowHeight), settings.TwitchMaximumHeight, heightLimit, dpi);
        Bitmap bitmap = new((int)Math.Ceiling(LogicalWidth * viewport.Scale), viewport.PhysicalHeight, PixelFormat.Format32bppPArgb);
        try
        {
            bitmap.SetResolution(96, 96);
            using var g = Graphics.FromImage(bitmap); g.Clear(Color.Transparent); g.ScaleTransform(viewport.Scale, viewport.Scale);
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            var p = WidgetDrawing.GetPalette(settings.TwitchStyle);
            using var panel = WidgetDrawing.RoundedRectangle(new(1, 1, LogicalWidth - 2, viewport.LogicalHeight - 2), 12);
            using var fill = new SolidBrush(dark ? p.panel : Color.FromArgb(235, AppTheme.WindowBackground(false)));
            using var text = new SolidBrush(dark ? p.text : AppTheme.TextPrimary(false));
            using var muted = new SolidBrush(dark ? p.muted : AppTheme.TextSecondary(false));
            using var accent = new SolidBrush(dark ? p.accent : Color.FromArgb(29, 105, 184));
            using var border = new Pen(dark ? p.border : Color.Gray);
            using var font = new Font("Segoe UI", 13, FontStyle.Regular, GraphicsUnit.Pixel);
            using var heading = new Font("Segoe UI", 13, FontStyle.Bold, GraphicsUnit.Pixel);
            using var live = new SolidBrush(Color.FromArgb(230, 55, 65));
            using var fallbackFill = new SolidBrush(Color.FromArgb(65, muted.Color));
            using var titleFont = new Font("Segoe UI Semibold", 11, FontStyle.Bold);
            using var line = new StringFormat { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center };
            using var right = new StringFormat { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter, Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
            g.FillPath(fill, panel); g.DrawPath(border, panel);
            if (settings.TwitchStyle == SystemWidgetStyle.Glow)
            { using var glow = new Pen(Color.FromArgb(45, accent.Color), 3); g.DrawPath(glow, panel); WidgetDrawing.DrawGlowText(g, "TWITCH", titleFont, 16, 13, accent.Color); }
            else g.DrawString("TWITCH", titleFont, text, 16, 13);
            if (settings.TwitchStyle != SystemWidgetStyle.Minimal) g.FillRectangle(accent, 16, 40, LogicalWidth - 32, settings.TwitchStyle == SystemWidgetStyle.Glow ? 2 : 1);
            g.DrawString(service.Busy ? "…" : "↻", heading, accent, RefreshBounds, line);
            rows.Clear(); var state = g.Save(); g.SetClip(viewport.ContentBounds);
            float y = CalendarViewport.HeaderHeight - viewport.ScrollOffset;
            using var separator = new Pen(Color.FromArgb(60, muted.Color));
            foreach (var s in streams)
            {
                var bounds = new RectangleF(14, y, viewport.ContentBounds.Width - 4, RowHeight);
                if (bounds.Bottom >= viewport.ContentBounds.Top && bounds.Top < viewport.ContentBounds.Bottom)
                {
                    if (TwitchLinks.Channel(s.Login) is Uri url) rows.Add((s.UserId, url, bounds));
                    var avatar = new RectangleF(bounds.X, y + 5, TwitchRowPresentation.AvatarSize, TwitchRowPresentation.AvatarSize);
                    // Partial scroll rows retain their text; draw avatars only when the entire circle fits the viewport.
                    if (viewport.ContentBounds.Contains(avatar))
                    {
                        using var circle = new GraphicsPath(); circle.AddEllipse(avatar);
                        var avatarState = g.Save(); g.SetClip(circle, CombineMode.Intersect);
                        if (!avatars.Draw(g, s.UserId, avatar))
                        {
                            g.FillEllipse(fallbackFill, avatar);
                            g.FillEllipse(muted, avatar.X + 16, avatar.Y + 9, 14, 14);
                            g.FillEllipse(muted, avatar.X + 9, avatar.Y + 27, 28, 20);
                        }
                        g.Restore(avatarState);
                    }
                    float textX = avatar.Right + 10, rightX = bounds.Right - 82;
                    g.DrawString(s.DisplayName.ReplaceLineEndings(" "), heading, text, new RectangleF(textX, y + 2, rightX - textX - 8, 23), line);
                    g.DrawString(s.Game.ReplaceLineEndings(" "), font, muted, new RectangleF(textX, y + 27, rightX - textX - 8, 23), line);
                    g.DrawString(TwitchRowPresentation.Viewers(s.Viewers, lang), font, text, new RectangleF(rightX, y + 2, 67, 23), right);
                    g.FillEllipse(live, bounds.Right - 8, y + 10, 6, 6);
                    g.DrawString(TwitchRowPresentation.Duration(s.StartedAt, now), font, muted, new RectangleF(rightX, y + 27, 82, 23), right);
                    g.DrawLine(separator, bounds.Left, bounds.Bottom - 3, bounds.Right, bounds.Bottom - 3);
                }
                y += RowHeight;
            }
            if (streams.Count == 0)
            {
                using var wrap = new StringFormat { Trimming = StringTrimming.EllipsisCharacter };
                string key = service.EmptyStateKey;
                g.DrawString(Localization.Get(key, lang), font, muted, viewport.ContentBounds, wrap);
            }
            g.Restore(state);
            string footer = service.Error != TwitchError.None ? Localization.Get("TwitchError" + service.Error, lang)
                : service.LastUpdated is DateTimeOffset updated ? string.Format(culture, Localization.Get("TwitchUpdated", lang), updated.ToLocalTime().ToString("g", culture)) : "";
            g.DrawString(footer, font, muted, new RectangleF(14, viewport.ContentBounds.Bottom, LogicalWidth - 28, 23), line);
            WidgetScrollbar.Draw(g, viewport, dark);
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }
    internal Uri? HitTest(Point point)
    { PointF logical = new(point.X / viewport.Scale, point.Y / viewport.Scale); return viewport.ContentBounds.Contains(logical) ? rows.FirstOrDefault(r => r.Bounds.Contains(logical)).Url : null; }
    internal bool ScrollWheel(Point point, int delta, int lines)
    {
        if (suspended || widgetDragging || thumbDragging || !viewport.ContentBounds.Contains(new PointF(point.X / viewport.Scale, point.Y / viewport.Scale)) || !viewport.Wheel(delta, lines)) return false;
        Render(); QueueAvatars(); return true;
    }
    protected override void OnMouseWheel(MouseEventArgs e) => ScrollWheel(e.Location, e.Delta, SystemInformation.MouseWheelScrollLines);
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (suspended || e.Button != MouseButtons.Left) return;
        PointF logical = new(e.X / viewport.Scale, e.Y / viewport.Scale);
        if (RefreshBounds.Contains(logical)) { refreshPressed = true; Capture = true; return; }
        var track = viewport.Track; track.Inflate(4, 0);
        if (viewport.CanScroll && track.Contains(logical))
        {
            if (viewport.Thumb.Contains(logical)) { thumbDragging = true; thumbGrabOffset = logical.Y - viewport.Thumb.Y; Capture = true; }
            else { viewport.SetOffset(viewport.ScrollOffset + (logical.Y < viewport.Thumb.Top ? -1 : 1) * viewport.ViewportHeight); Render(); QueueAvatars(); }
            return;
        }
        pressed = HitTest(e.Location); if (pressed != null) { Capture = true; return; }
        widgetDragging = !settings.TwitchLocked; base.OnMouseDown(e);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    { if (thumbDragging) { viewport.DragThumb(e.Y / viewport.Scale, thumbGrabOffset); Render(); QueueAvatars(); } else if (pressed == null && !refreshPressed) base.OnMouseMove(e); }
    protected override async void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (thumbDragging) { thumbDragging = false; Capture = false; return; }
        if (refreshPressed)
        { refreshPressed = false; Capture = false; if (!suspended && RefreshBounds.Contains(new PointF(e.X / viewport.Scale, e.Y / viewport.Scale))) await service.RefreshAsync(lifetime.Token); return; }
        if (pressed is Uri url)
        { pressed = null; Capture = false; if (!suspended && HitTest(e.Location) == url) open(url); return; }
        base.OnMouseUp(e); widgetDragging = false;
    }
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        if (!Capture) { pressed = null; refreshPressed = false; thumbDragging = false; if (widgetDragging) { widgetDragging = false; base.OnMouseUp(new(MouseButtons.Left, 1, 0, 0, 0)); } }
        base.OnMouseCaptureChanged(e);
    }
    protected override void Dispose(bool disposing)
    { if (disposing && !IsDisposed) { displayTimer.Stop(); displayTimer.Tick -= DisplayTick; displayTimer.Dispose(); avatars.Changed -= AvatarChanged; avatars.Dispose(); lifetime.Cancel(); lifetime.Dispose(); service.Changed -= RefreshData; drag.Dispose(); } base.Dispose(disposing); }
}
