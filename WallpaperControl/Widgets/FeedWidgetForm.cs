using System.Diagnostics;
namespace WallpaperControl;
internal sealed class FeedWidgetForm : Form
{
    private readonly FeedService service;
    private readonly Action<FeedWidgetSettings> changed;
    private readonly Action manage, add;
    private readonly WidgetDragHandler drag;
    private readonly CalendarViewport viewport = new(390);
    private readonly List<(RectangleF Bounds, string? Link)> rows = [];
    private readonly Dictionary<string, Bitmap> images = [];
    private readonly HashSet<string> visibleImages = [];
    private readonly HashSet<string> requestedImages = [];
    private readonly List<Task> imageTasks = [];
    private string[] previousEntries = [];
    private readonly HttpClient imageHttp = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly SemaphoreSlim imageGate = new(2);
    private readonly CancellationTokenSource lifetime = new();
    private FeedWidgetSettings settings;
    private string language;
    private bool suspended, powerSuspended, dark = NotesDialogStyle.ResolveDarkMode(), rendering, disposed;
    private readonly ContextMenuStrip menu = new();
    private bool thumbDragging;
    private float thumbGrabOffset;
    internal FeedWidgetForm(FeedService service, FeedWidgetSettings settings, string language, Action<Point> moved, Action<FeedWidgetSettings> changed, Action manage, Action add)
    {
        this.service = service; this.settings = settings.Clone(); this.language = language; this.changed = changed; this.manage = manage; this.add = add;
        AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; ClientSize = new(390, 120);
        drag = new(this, () => this.settings.Locked, Render, moved); service.Changed += Render;
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x00080000 | 0x00000080 | 0x08000000; return cp; } }
    internal void Apply(FeedWidgetSettings value, string lang) { settings = value.Clone(); language = lang; Render(); }
    internal void ApplyTheme(bool value) { dark = value; ContextMenuTheme.Apply(menu, dark); Render(); }
    internal void SetActivitySuspended(bool value) { suspended = value; if (value) StopThumbDrag(); else Render(); }
    internal void SetPowerSuspended(bool value) { powerSuspended = value; if (value) StopThumbDrag(); else Render(); }
    protected override void OnShown(EventArgs e) { base.OnShown(e); Render(); }
    protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); Render(); }
    protected override void WndProc(ref Message m)
    {
        if (DesktopWidgetNative.HandleMouseActivation(ref m)) return;
        if (m.Msg == 0x020A) { viewport.Wheel((short)((m.WParam.ToInt64() >> 16) & 0xffff), SystemInformation.MouseWheelScrollLines); Render(); m.Result = 0; return; }
        base.WndProc(ref m);
    }
    private void Render()
    {
        if (disposed || suspended || powerSuspended || rendering || !IsHandleCreated) return;
        rendering = true;
        try { using var bitmap = RenderBitmap(Screen.FromControl(this).WorkingArea.Height, DeviceDpi); ClientSize = bitmap.Size; LayeredWidgetBitmap.Update(Handle, Location, bitmap); }
        finally { rendering = false; }
    }
    internal Bitmap RenderBitmap(int workHeight = 2000, int dpi = 96)
    {
        var items = service.Items(settings.Selected, settings.Period).ToArray();
        var keys = items.Select(item => item.Source.Id + ":" + item.Entry.Id).ToArray();
        imageTasks.RemoveAll(task => task.IsCompleted);
        if (imageTasks.Count == 0) requestedImages.RemoveWhere(key => !images.ContainsKey(key) && !items.Any(item => item.Entry.ImageUrl == key));
        var wanted = items.Select(item => item.Entry.ImageUrl).Where(url => url != null).ToHashSet();
        foreach (var key in images.Keys.Where(key => !wanted.Contains(key)).ToArray()) { images[key].Dispose(); images.Remove(key); requestedImages.Remove(key); }
        float Height(int index) => settings.Display == FeedDisplayMode.Titles ? 50 : settings.Display == FeedDisplayMode.FocusFirst && index == 0 ? 180 : 112;
        float? anchorOffset = null;
        if (previousEntries.Length > 0 && !previousEntries.SequenceEqual(keys) && viewport.ScrollOffset > 0)
        {
            float before = 0; int index = 0;
            while (index + 1 < previousEntries.Length && before + Height(index) <= viewport.ScrollOffset) before += Height(index++);
            int successor = Array.IndexOf(keys, previousEntries[index]);
            if (successor >= 0) anchorOffset = Enumerable.Range(0, successor).Sum(Height) + viewport.ScrollOffset - before;
        }
        viewport.Update(Math.Max(60, items.Select((_, i) => Height(i)).Sum()), settings.MaximumHeight, workHeight, dpi);
        if (anchorOffset.HasValue) viewport.SetOffset(anchorOffset.Value);
        previousEntries = keys;
        visibleImages.Clear();
        float imageY = CalendarViewport.HeaderHeight - viewport.ScrollOffset;
        for (int index = 0; index < items.Length; index++)
        {
            if (settings.Display != FeedDisplayMode.Titles && items[index].Entry.ImageUrl is string url
                && imageY < viewport.ContentBounds.Bottom && imageY + Height(index) > viewport.ContentBounds.Top)
                visibleImages.Add(url);
            imageY += Height(index);
        }
        Bitmap bitmap = new((int)Math.Ceiling(390 * viewport.Scale), viewport.PhysicalHeight, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bitmap); g.Clear(Color.Transparent); g.ScaleTransform(viewport.Scale, viewport.Scale);
        var palette = WidgetDrawing.GetPalette(settings.Style);
        using var path = WidgetDrawing.RoundedRectangle(new(1, 1, 388, viewport.LogicalHeight - 2), 12);
        using var fill = new SolidBrush(dark ? palette.panel : Color.FromArgb(235, AppTheme.WindowBackground(false)));
        using var text = new SolidBrush(dark ? palette.text : AppTheme.TextPrimary(false));
        using var muted = new SolidBrush(dark ? palette.muted : AppTheme.TextSecondary(false));
        using var accent = new SolidBrush(dark ? palette.accent : Color.FromArgb(29,105,184));
        using var border = new Pen(accent.Color, settings.Style == SystemWidgetStyle.Glow ? 2 : 1);
        using var font = new Font("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel);
        using var bold = new Font("Segoe UI", 13, FontStyle.Bold, GraphicsUnit.Pixel);
        using var focusFont = new Font("Segoe UI", 17, FontStyle.Bold, GraphicsUnit.Pixel);
        using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter };
        using var single = new StringFormat { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
        g.FillPath(fill, path); g.DrawPath(border, path);
        string heading = settings.Sources.FirstOrDefault(s => s.Id == settings.Selected)?.Title ?? Localization.Get("FeedTitle", language);
        g.DrawString(heading, bold, accent, new RectangleF(14, 14, 285, 26), single); g.DrawString("↻", bold, accent, 312, 14); g.DrawString("...", bold, accent, 350, 14);
        var saved = g.Save(); g.SetClip(viewport.ContentBounds); rows.Clear(); float y = CalendarViewport.HeaderHeight - viewport.ScrollOffset;
        for (int i = 0; i < items.Length; i++)
        {
            var (source, entry) = items[i]; float height = Height(i); var row = new RectangleF(14, y, viewport.ContentBounds.Width - 4, height); rows.Add((row, entry.Link));
            if (row.IntersectsWith(viewport.ContentBounds))
            {
                bool hasImage = settings.Display != FeedDisplayMode.Titles && entry.ImageUrl != null;
                bool focused = settings.Display == FeedDisplayMode.FocusFirst && i == 0;
                float imageSize = focused ? 110 : 64;
                float width = row.Width - (hasImage ? imageSize + 12 : 0);
                g.DrawString(source.Title, font, accent, new RectangleF(14, y, width * .54f, 18), single);
                string date = entry.Published?.ToLocalTime().ToString("g", System.Globalization.CultureInfo.GetCultureInfo(language)) ?? "";
                g.DrawString(date, font, muted, new RectangleF(14 + width * .55f, y, width * .45f, 18), single);
                g.DrawString(entry.Title, focused ? focusFont : bold, text, new RectangleF(14, y + 21, width, settings.Display == FeedDisplayMode.Titles ? 24 : 37), format);
                if (settings.Display != FeedDisplayMode.Titles) g.DrawString(entry.Description, font, text, new RectangleF(14, y + 60, width, height - 66), format);
                if (hasImage && entry.ImageUrl != null)
                {
                    if (images.TryGetValue(entry.ImageUrl, out var image)) g.DrawImage(image, row.Right - imageSize - 4, y + 22, imageSize, imageSize);
                    else if (!disposed && requestedImages.Count < 64 && requestedImages.Add(entry.ImageUrl)) imageTasks.Add(LoadImageAsync(entry.ImageUrl));
                }
                g.DrawLine(border, 14, row.Bottom - 3, row.Right, row.Bottom - 3);
            }
            y += height;
        }
        if (items.Length == 0) g.DrawString(Localization.Get(service.Busy ? "FeedChecking" : settings.Period != FeedPeriod.All && settings.Sources.Count > 0 ? "FeedNoMatching" : "FeedEmpty", language), font, muted, viewport.ContentBounds, format);
        g.Restore(saved);
        WidgetScrollbar.Draw(g, viewport, dark);
        bool failed = settings.Sources.Where(s => settings.Selected == null || settings.Selected == s.Id).Any(s => service.Snapshots.TryGetValue(s.Id, out var state) && state.Error != null);
        var updated = service.Snapshots.Where(s => settings.Selected == null || settings.Selected == s.Key).Select(s => s.Value.Updated).Max();
        string footer = failed ? Localization.Get("FeedRefreshFailed", language) : updated == null ? "" : string.Format(Localization.Get("FeedUpdated", language), updated.Value.ToLocalTime().ToString("g"));
        g.DrawString(footer, font, muted, new RectangleF(14, viewport.LogicalHeight - 23, 360, 20), single);
        return bitmap;
    }
    private async Task LoadImageAsync(string url)
    {
        bool acquired = false;
        try
        {
            await imageGate.WaitAsync(lifetime.Token); acquired = true;
            using var response = await imageHttp.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, lifetime.Token); response.EnsureSuccessStatusCode();
            using var stream = new MemoryStream(await FeedService.ReadBoundedAsync(response, 2_000_000, lifetime.Token)); using var original = Image.FromStream(stream);
            if (original.Width > 8192 || original.Height > 8192 || (long)original.Width * original.Height > 16_000_000 || disposed) return;
            var thumbnail = new Bitmap(96, 96);
            try
            {
                using var graphics = Graphics.FromImage(thumbnail);
                float scale = Math.Min(96f / original.Width, 96f / original.Height);
                graphics.DrawImage(original, (96 - original.Width * scale) / 2, (96 - original.Height * scale) / 2, original.Width * scale, original.Height * scale);
                CacheImage(url, thumbnail);
            }
            catch { thumbnail.Dispose(); throw; }
            Render();
        }
        catch (Exception ex) { if (!disposed) AppLogger.Info($"Feed image unavailable: type={ex.GetType().Name}"); }
        finally { if (acquired) imageGate.Release(); }
    }
    internal void CacheImage(string url, Bitmap thumbnail)
    {
        if (images.Remove(url, out var replaced)) replaced.Dispose();
        if (images.Count >= 48)
        {
            // Dictionary slots are reused after removal. Never evict a visible
            // image merely because it occupies the first slot in the cache.
            string? victim = images.Keys.FirstOrDefault(key => !visibleImages.Contains(key));
            if (victim == null) { thumbnail.Dispose(); return; }
            images[victim].Dispose(); images.Remove(victim); requestedImages.Remove(victim);
        }
        images[url] = thumbnail;
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (suspended || powerSuspended) return;
        var point = new PointF(e.X / viewport.Scale, e.Y / viewport.Scale);
        if (e.Button == MouseButtons.Right || point.Y < 45 && point.X > 350) { ShowMenu(); return; }
        if (point.Y < 45 && point.X > 310) { _ = service.RefreshAsync(true); return; }
        if (point.Y >= 54)
        {
            var scrollHit = viewport.Track; scrollHit.Inflate(8, 0);
            if (e.Button == MouseButtons.Left && viewport.CanScroll && scrollHit.Contains(point))
            {
                if (viewport.Thumb.Contains(point) || point.Y >= viewport.Thumb.Top && point.Y <= viewport.Thumb.Bottom)
                { thumbDragging = true; thumbGrabOffset = point.Y - viewport.Thumb.Top; Capture = true; }
                else { viewport.SetOffset(viewport.ScrollOffset + (point.Y < viewport.Thumb.Top ? -1 : 1) * viewport.ViewportHeight); Render(); }
                return;
            }
            var row = rows.FirstOrDefault(r => r.Bounds.Contains(point));
            if (FeedParser.WebUri(row.Link) is { } uri) { try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); } catch (Exception ex) { AppLogger.Info($"Feed link failed: {ex.GetType().Name}"); } return; }
        }
        base.OnMouseDown(e);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (thumbDragging) { viewport.DragThumb(e.Y / viewport.Scale, thumbGrabOffset); Render(); }
        else base.OnMouseMove(e);
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (thumbDragging && e.Button == MouseButtons.Left) { StopThumbDrag(); return; }
        base.OnMouseUp(e);
    }
    private void StopThumbDrag() { thumbDragging = false; if (Capture) Capture = false; }
    protected override void OnMouseCaptureChanged(EventArgs e)
    { if (!Capture) thumbDragging = false; base.OnMouseCaptureChanged(e); }
    private void ShowMenu()
    {
        menu.Items.Clear(); string L(string key) => Localization.Get(key, language);
        var sources = new ToolStripMenuItem(L("FeedSelect")); menu.Items.Add(sources);
        void Select(Guid? id) { settings.Selected = id; previousEntries = []; viewport.SetOffset(0); changed(settings.Clone()); Render(); }
        var all = new ToolStripMenuItem(L("FeedAll")) { Checked = settings.Selected == null }; all.Click += (_, _) => Select(null); sources.DropDownItems.Add(all);
        foreach (var source in settings.Sources) { var choice = new ToolStripMenuItem(source.Title) { Checked = settings.Selected == source.Id }; choice.Click += (_, _) => Select(source.Id); sources.DropDownItems.Add(choice); }
        var modes = new ToolStripMenuItem(L("FeedDisplay")); menu.Items.Add(modes);
        foreach (var mode in Enum.GetValues<FeedDisplayMode>()) { var choice = new ToolStripMenuItem(L("FeedMode" + mode)) { Checked = settings.Display == mode }; choice.Click += (_, _) => { settings.Display = mode; previousEntries = []; viewport.SetOffset(0); changed(settings.Clone()); Render(); }; modes.DropDownItems.Add(choice); }
        var periods = new ToolStripMenuItem(L("FeedPeriodSelect")); menu.Items.Add(periods);
        foreach (var period in Enum.GetValues<FeedPeriod>())
        {
            var choice = new ToolStripMenuItem(L("FeedPeriod" + period)) { Checked = settings.Period == period };
            choice.Click += (_, _) => { settings.Period = period; previousEntries = []; viewport.SetOffset(0); changed(settings.Clone()); Render(); };
            periods.DropDownItems.Add(choice);
        }
        menu.Items.Add(L("FeedAdd"), null, (_, _) => add()); menu.Items.Add(L("FeedManage"), null, (_, _) => manage());
        menu.Items.Add(L("FeedRefresh"), null, async (_, _) => await service.RefreshAsync(true));
        ContextMenuTheme.Apply(menu, dark);
        menu.Show(Cursor.Position);
    }
    private async Task ReleaseImageResourcesAsync()
    {
        await Task.WhenAll(imageTasks).ConfigureAwait(false);
        imageGate.Dispose(); lifetime.Dispose();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed) { disposed = true; lifetime.Cancel(); service.Changed -= Render; drag.Dispose(); menu.Dispose(); imageHttp.Dispose(); foreach (var image in images.Values) image.Dispose(); images.Clear(); _ = ReleaseImageResourcesAsync(); }
        base.Dispose(disposing);
    }
}
