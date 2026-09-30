using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace WallpaperControl;

/// <summary>Layered, bounded package list using the existing calendar viewport and desktop drag handler.</summary>
internal sealed class PackageTrackingWidgetForm : Form
{
    internal const int LogicalWidth = 390;
    private readonly CalendarViewport viewport = new(LogicalWidth);
    private readonly PackageTrackingService service;
    private readonly Action<Guid> details;
    private readonly Action? add;
    private bool addPressed;
    private readonly WidgetDragHandler drag;
    private readonly CancellationTokenSource lifetime = new();
    private readonly List<(Guid Id, RectangleF Bounds)> rows = new();
    private WidgetSettings settings;
    private bool suspended, rendering, thumbDragging, widgetDragging;
    private float thumbGrabOffset;
    private Guid? pressed;
    private string? error;
    private bool dark;
    internal static RectangleF RefreshBounds => new(LogicalWidth - 44, 12, 30, 28);
    internal static RectangleF AddBounds => new(LogicalWidth - 80, 12, 30, 28);
    internal RectangleF UpdatedBounds { get; private set; }
    internal PackageTrackingWidgetForm(PackageTrackingService service, WidgetSettings settings, Action<Point> moved, Action<Guid> details, Action? add = null)
    {
        this.service = service; this.settings = settings.Clone(); this.details = details; this.add = add; dark = NotesDialogStyle.ResolveDarkMode();
        Text = Localization.Get("PackageTitle", settings.ClockLanguageCode);
        AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual; ClientSize = new(LogicalWidth, 120); Location = WidgetSettings.EnsureVisible(settings.PackageLocation, Size);
        drag = new WidgetDragHandler(this, () => this.settings.PackageLocked, Render, moved);
        service.Changed += RefreshData;
    }
    private void RefreshData()
    {
        if (!service.Busy && service.RefreshFailures.Count == 0) error = null;
        Render();
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x00080000 | 0x00000080 | 0x08000000; return cp; } }
    internal void Apply(WidgetSettings value) { settings = value.Clone(); Text = Localization.Get("PackageTitle", settings.ClockLanguageCode); Render(); }
    internal void ApplyTheme(bool value) { dark = value; Render(); }
    internal void SetActivitySuspended(bool value)
    {
        suspended = value;
        if (value) { pressed = null; addPressed = false; thumbDragging = false; Capture = false; }
        else Render(); // Only cached data, never a resume request.
    }
    protected override void OnShown(EventArgs e) { base.OnShown(e); Render(); }
    protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); Render(); }
    protected override void WndProc(ref Message m)
    {
        if (DesktopWidgetNative.HandleMouseActivation(ref m)) return;
        if (m.Msg == 0x020A)
        {
            int packed = unchecked((int)m.LParam.ToInt64());
            ScrollWheel(PointToClient(new Point((short)(packed & 0xffff), (short)((packed >> 16) & 0xffff))),
                (short)((m.WParam.ToInt64() >> 16) & 0xffff), SystemInformation.MouseWheelScrollLines);
            m.Result = IntPtr.Zero; return;
        }
        base.WndProc(ref m);
        if (m.Msg is 0x007E or 0x001A) Render();
    }
    private void Render()
    {
        if (suspended || rendering || !IsHandleCreated || IsDisposed || Disposing) return;
        rendering = true;
        try
        {
            var area = Screen.FromControl(this).WorkingArea;
            using var bitmap = RenderBitmap(area.Height, DeviceDpi);
            ClientSize = bitmap.Size;
            if (!widgetDragging) Location = new(Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width)), Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height)));
            LayeredWidgetBitmap.Update(Handle, Location, bitmap);
        }
        finally { rendering = false; }
    }
    internal Bitmap RenderBitmap(int heightLimit = 2000, int dpi = 96)
    {
        var shipments = service.Shipments;
        string lang = settings.ClockLanguageCode;
        string EventSummary(TrackedShipment s) => AmazonLogistics.IsLocal(s) ? Localization.Get("PackageAmazonOnly", lang) : string.Join(" · ", new[] { s.LastRelevantEvent?.Location,
            PackagePresentation.Date(s.LastRelevantEvent?.OccurredAt, lang) }.Where(v => !string.IsNullOrWhiteSpace(v)));
        float Height(TrackedShipment s) => 80 + (EventSummary(s).Length > 0 ? 24 : 0)
            + (s.EstimatedDelivery != null ? 24 : 0) + (service.RefreshFailures.ContainsKey(s.Id) ? 24 : 0);
        viewport.Update(Math.Max(40, shipments.Sum(Height)), settings.PackageMaximumHeight, heightLimit, dpi);
        Bitmap bitmap = new((int)Math.Ceiling(LogicalWidth * viewport.Scale), viewport.PhysicalHeight, PixelFormat.Format32bppPArgb);
        try
        {
            bitmap.SetResolution(96, 96);
            using var g = Graphics.FromImage(bitmap); g.Clear(Color.Transparent); g.ScaleTransform(viewport.Scale, viewport.Scale);
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            var palette = WidgetDrawing.GetPalette(settings.PackageStyle);
            using var panel = WidgetDrawing.RoundedRectangle(new RectangleF(1, 1, LogicalWidth - 2, viewport.LogicalHeight - 2), 12);
            using var fill = new SolidBrush(dark ? palette.panel : Color.FromArgb(235, AppTheme.WindowBackground(false)));
            using var border = new Pen(dark ? palette.border : settings.PackageStyle == SystemWidgetStyle.Minimal ? Color.Gray : Color.FromArgb(29, 105, 184));
            using var text = new SolidBrush(dark ? palette.text : AppTheme.TextPrimary(false));
            using var muted = new SolidBrush(dark ? palette.muted : AppTheme.TextSecondary(false));
            using var accent = new SolidBrush(dark ? palette.accent : Color.FromArgb(29, 105, 184));
            using var font = new Font("Segoe UI", 13, FontStyle.Regular, GraphicsUnit.Pixel);
            using var heading = new Font("Segoe UI", 13, FontStyle.Bold, GraphicsUnit.Pixel);
            using var line = new StringFormat { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center };
            g.FillPath(fill, panel); g.DrawPath(border, panel);
            if (settings.PackageStyle == SystemWidgetStyle.Glow)
            {
                using var glow = new Pen(Color.FromArgb(45, dark ? palette.accent : Color.FromArgb(29, 105, 184)), 3);
                g.DrawPath(glow, panel);
            }
            using var titleFont = new Font("Segoe UI Semibold", 11f, FontStyle.Bold);
            using var titleBrush = new SolidBrush(dark ? palette.title : AppTheme.TextPrimary(false));
            string widgetTitle = Localization.Get("PackageTitle", lang).ToUpperInvariant();
            var headerState = g.Save();
            g.SetClip(new RectangleF(12, 10, AddBounds.Left - 16, 30));
            if (settings.PackageStyle == SystemWidgetStyle.Glow)
                WidgetDrawing.DrawGlowText(g, widgetTitle, titleFont, 16, 13, accent.Color);
            else
                g.DrawString(widgetTitle, titleFont, titleBrush, 16, 13);
            g.Restore(headerState);
            if (settings.PackageStyle != SystemWidgetStyle.Minimal)
                g.FillRectangle(accent, 16, 40, LogicalWidth - 32, settings.PackageStyle == SystemWidgetStyle.Glow ? 2 : 1);
            g.DrawString(service.Busy ? "…" : "↻", heading, accent, RefreshBounds, line);
            WidgetDrawing.DrawAddIcon(g, AddBounds, service.Busy ? muted.Color : accent.Color);
            rows.Clear(); var state = g.Save(); g.SetClip(viewport.ContentBounds);
            using var separator = new Pen(Color.FromArgb(dark ? 70 : 55, muted.Color), 1);
            float y = CalendarViewport.HeaderHeight - viewport.ScrollOffset;
            int index = 0;
            foreach (var s in shipments)
            {
                var bounds = new RectangleF(14, y, viewport.ContentBounds.Width - 4, Height(s));
                if (bounds.Bottom >= viewport.ContentBounds.Top && bounds.Top < viewport.ContentBounds.Bottom)
                {
                    rows.Add((s.Id, bounds));
                    void Draw(string value, float offset, Brush brush, Font? f = null) => g.DrawString(value.ReplaceLineEndings(" "), f ?? font, brush, new RectangleF(bounds.X, y + offset, bounds.Width, 23), line);
                    Draw(PackagePresentation.Name(s, lang), 0, text, heading);
                    Draw(PackagePresentation.CarrierNumber(s, true), 24, muted);
                    Draw("● " + PackagePresentation.ShipmentStatus(s, lang), 48, text);
                    float offset = 72;
                    string summary = EventSummary(s);
                    if (summary.Length > 0) { Draw(summary, offset, muted); offset += 24; }
                    if (s.EstimatedDelivery != null) { Draw(PackagePresentation.Format("PackageEta", PackagePresentation.Date(s.EstimatedDelivery, lang), lang), offset, muted); offset += 24; }
                    if (service.RefreshFailures.ContainsKey(s.Id)) Draw(Localization.Get("PackageRefreshShort", lang), offset, muted);
                    if (index < shipments.Count - 1)
                        g.DrawLine(separator, bounds.Left, bounds.Bottom - 4, bounds.Right, bounds.Bottom - 4);
                }
                y += Height(s);
                index++;
            }
            if (shipments.Count == 0) g.DrawString(Localization.Get("PackageEmpty", lang), font, muted, viewport.ContentBounds, line);
            g.Restore(state);
            // The existing viewport reserves 25 logical pixels below the list. At the scroll
            // limit the list end meets that footer; before then the timestamp travels with it.
            UpdatedBounds = new RectangleF(14, Math.Max(viewport.ContentBounds.Bottom, y), LogicalWidth - 28, 22);
            var remote = shipments.Where(s => s.TrackingMode == "provider").ToArray();
            DateTimeOffset? updated = remote.Length > 0 && remote.All(s => s.LastSuccessfulRefresh != null)
                ? remote.Min(s => s.LastSuccessfulRefresh) : null;
            if (updated != null && error == null)
                g.DrawString(PackagePresentation.Format("PackageUpdated", PackagePresentation.Date(updated, lang), lang), font, muted, UpdatedBounds, line);
            if (viewport.CanScroll) { g.FillRectangle(muted, viewport.Track); g.FillRectangle(accent, viewport.Thumb); }
            if (error != null) g.DrawString(error, font, muted, new RectangleF(14, viewport.LogicalHeight - 24, LogicalWidth - 28, 22), line);
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }
    internal Guid? HitTest(Point point)
    {
        PointF logical = new(point.X / viewport.Scale, point.Y / viewport.Scale);
        if (RefreshBounds.Contains(logical)) return Guid.Empty;
        if (!viewport.ContentBounds.Contains(logical)) return null;
        return rows.Where(r => r.Bounds.Contains(logical)).Select(r => (Guid?)r.Id).FirstOrDefault();
    }
    internal bool HitTestAdd(Point point) => AddBounds.Contains(new PointF(point.X / viewport.Scale, point.Y / viewport.Scale));
    internal bool ScrollWheel(Point point, int delta, int lines)
    {
        if (suspended || widgetDragging || thumbDragging || !viewport.ContentBounds.Contains(new PointF(point.X / viewport.Scale, point.Y / viewport.Scale))) return false;
        if (!viewport.Wheel(delta, lines)) return false; Render(); return true;
    }
    protected override void OnMouseWheel(MouseEventArgs e) => ScrollWheel(e.Location, e.Delta, SystemInformation.MouseWheelScrollLines);
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (suspended || e.Button != MouseButtons.Left) return;
        if (HitTestAdd(e.Location)) { addPressed = true; Capture = true; return; }
        PointF logical = new(e.X / viewport.Scale, e.Y / viewport.Scale);
        var track = viewport.Track; track.Inflate(4, 0);
        if (viewport.CanScroll && track.Contains(logical))
        {
            if (viewport.Thumb.Contains(logical)) { thumbDragging = true; thumbGrabOffset = logical.Y - viewport.Thumb.Y; Capture = true; }
            else { viewport.SetOffset(viewport.ScrollOffset + (logical.Y < viewport.Thumb.Top ? -1 : 1) * viewport.ViewportHeight); Render(); }
            return;
        }
        pressed = HitTest(e.Location); if (pressed != null) { Capture = true; return; }
        widgetDragging = !settings.PackageLocked; base.OnMouseDown(e);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    { if (thumbDragging) { viewport.DragThumb(e.Y / viewport.Scale, thumbGrabOffset); Render(); } else if (pressed == null && !addPressed) base.OnMouseMove(e); }
    protected override async void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (addPressed)
        {
            addPressed = false; Capture = false;
            if (!suspended && !service.Busy && HitTestAdd(e.Location)) add?.Invoke();
            return;
        }
        if (thumbDragging) { thumbDragging = false; Capture = false; return; }
        if (pressed is Guid id)
        {
            pressed = null; Capture = false;
            if (suspended || HitTest(e.Location) != id) return;
            if (id != Guid.Empty) { details(id); return; }
            if (service.Busy) return; // The existing busy icon already indicates the in-flight automatic/manual cycle.
            try { var result = await service.RefreshAsync(lifetime.Token); error = result.Outcome == PackageOperation.Success ? null : PackagePresentation.Error(result, settings.ClockLanguageCode); Render(); }
            catch (OperationCanceledException) { }
            return;
        }
        base.OnMouseUp(e); widgetDragging = false;
    }
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        if (!Capture) { pressed = null; addPressed = false; thumbDragging = false; if (widgetDragging) { widgetDragging = false; base.OnMouseUp(new(MouseButtons.Left, 1, 0, 0, 0)); } }
        base.OnMouseCaptureChanged(e);
    }
    protected override void Dispose(bool disposing)
    { if (disposing && !IsDisposed) { lifetime.Cancel(); lifetime.Dispose(); service.Changed -= RefreshData; drag.Dispose(); } base.Dispose(disposing); }
}
