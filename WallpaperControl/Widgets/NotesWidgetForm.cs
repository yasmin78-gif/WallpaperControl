using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;

namespace WallpaperControl
{
    internal readonly record struct NoteHit(Guid? Id, bool Checkbox, bool Add, bool CompletedGroup = false);

    /// <summary>Bounded layered notes viewport; calendar scrolling and normal widget dragging remain shared.</summary>
    internal sealed class NotesWidgetForm : Form
    {
        internal const int LogicalWidth = 360;
        private readonly CalendarViewport viewport = new(LogicalWidth);
        private readonly NotesStore store;
        private readonly Func<DateTime> clock;
        private readonly Action<NoteEntry?> edit;
        private readonly WidgetDragHandler drag;
        private readonly System.Windows.Forms.Timer timer = new() { Interval = 30000 };
        private readonly List<(NoteEntry Entry, RectangleF Bounds)> rows = new();
        private WidgetSettings settings;
        private bool suspended, rendering, shown, thumbDragging, widgetDragging;
        private float thumbGrabOffset;
        private NoteHit? pressed;
        private bool completedExpanded;
        private RectangleF completedBounds;
        internal static RectangleF AddBounds => new(LogicalWidth - 40, 12, 26, 28);
        internal int OpenCount => store.Entries.Count(e => !e.IsCompletedOn(clock()));

        internal NotesWidgetForm(NotesStore store, WidgetSettings settings, Action<Point> moved,
            Action<NoteEntry?> edit, Func<DateTime>? clock = null)
        {
            this.store = store; this.settings = settings.Clone(); this.edit = edit; this.clock = clock ?? (() => DateTime.Now);
            Text = Localization.Get("NotesTitle", settings.ClockLanguageCode);
            AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; ClientSize = new Size(LogicalWidth, 120);
            Location = WidgetSettings.EnsureVisible(settings.NotesLocation, Size);
            drag = new WidgetDragHandler(this, () => this.settings.NotesLocked, Render, moved);
            store.Changed += RefreshData; timer.Tick += TimerTick;
        }

        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x00080000 | 0x00000080 | 0x08000000; return cp; }
        }
        internal void Apply(WidgetSettings value) { settings = value.Clone(); Text = Localization.Get("NotesTitle", settings.ClockLanguageCode); Render(); }
        private void TimerTick(object? sender, EventArgs e) => RefreshData();
        internal void RefreshData() { if (!suspended) Render(); }
        internal void SetActivitySuspended(bool value)
        {
            suspended = value; timer.Enabled = shown && !value && !IsDisposed;
            if (value) { pressed = null; thumbDragging = false; Capture = false; }
            else Render();
        }
        protected override void OnShown(EventArgs e) { base.OnShown(e); shown = true; timer.Enabled = !suspended; Render(); }
        protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); Render(); }
        protected override void WndProc(ref Message m)
        {
            if (DesktopWidgetNative.HandleMouseActivation(ref m)) return;
            if (m.Msg == 0x020A)
            {
                int packed = unchecked((int)m.LParam.ToInt64());
                Point point = PointToClient(new Point((short)(packed & 0xffff), (short)((packed >> 16) & 0xffff)));
                ScrollWheel(point, (short)((m.WParam.ToInt64() >> 16) & 0xffff), SystemInformation.MouseWheelScrollLines);
                m.Result = IntPtr.Zero; return;
            }
            base.WndProc(ref m);
            if (m.Msg is 0x007E or 0x001A && shown) Render();
        }

        private void Render()
        {
            if (suspended || rendering || !IsHandleCreated || IsDisposed || Disposing) return;
            rendering = true;
            try
            {
                Rectangle area = Screen.FromControl(this).WorkingArea;
                using Bitmap bitmap = RenderBitmap(area.Height, DeviceDpi);
                ClientSize = bitmap.Size;
                if (!widgetDragging)
                    Location = new Point(Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width)),
                        Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height)));
                LayeredWidgetBitmap.Update(Handle, Location, bitmap);
            }
            finally { rendering = false; }
        }

        internal Bitmap RenderBitmap(int heightLimit = 2000, int dpi = 96)
        {
            DateTime now = clock();
            IReadOnlyList<NoteGroup> groups = NoteGrouping.Build(store.Entries, now);
            NoteEntry[] done = store.Entries.Where(e => e.IsCompletedOn(now) && e.CompletedAt?.Date == now.Date)
                .OrderBy(e => e.CompletedAt).ThenBy(e => e.Id).ToArray();
            using Font font = new("Segoe UI", 13, FontStyle.Regular, GraphicsUnit.Pixel);
            using Bitmap measure = new(1, 1);
            measure.SetResolution(96, 96);
            using Graphics measuring = Graphics.FromImage(measure);
            string language = settings.ClockLanguageCode;
            CultureInfo culture = CultureInfo.GetCultureInfo(language);
            // Reserve scrollbar width consistently so measuring and drawing never disagree.
            const float textWidth = LogicalWidth - 74;
            var titles = store.Entries.ToDictionary(e => e.Id, e => FitTitle(measuring, DisplayTitle(e, now, culture), font, textWidth, 38));
            float RowHeight(NoteEntry e) => (titles[e.Id].Contains('\n') ? 44 : 28) + (e.IsCompletedOn(now) ? 18 : 0);
            float contentHeight = groups.Sum(group => 26 + group.Entries.Sum(RowHeight))
                + (done.Length > 0 ? 26 + (completedExpanded ? done.Sum(RowHeight) : 0) : 0);
            viewport.Update(Math.Max(40, contentHeight), Math.Clamp(settings.NotesMaximumHeight, 300, 1000), heightLimit, dpi);
            Bitmap? bitmap = new((int)Math.Ceiling(LogicalWidth * viewport.Scale), viewport.PhysicalHeight, PixelFormat.Format32bppPArgb);
            try
            {
                bitmap.SetResolution(96, 96);
                using Graphics g = Graphics.FromImage(bitmap);
                g.Clear(Color.Transparent); g.ScaleTransform(viewport.Scale, viewport.Scale);
                g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                var palette = WidgetDrawing.GetPalette(settings.NotesStyle);
                using GraphicsPath panel = WidgetDrawing.RoundedRectangle(new RectangleF(1, 1, LogicalWidth - 2, viewport.LogicalHeight - 2), 12);
                using SolidBrush fill = new(palette.panel); using Pen border = new(palette.border);
                g.FillPath(fill, panel);
                if (settings.NotesStyle == SystemWidgetStyle.Glow) { using Pen glow = new(Color.FromArgb(45, palette.accent), 3); g.DrawPath(glow, panel); }
                g.DrawPath(border, panel);
                using Font heading = new("Segoe UI", 13, FontStyle.Bold, GraphicsUnit.Pixel);
                using SolidBrush text = new(palette.text); using SolidBrush muted = new(palette.muted); using SolidBrush accent = new(palette.accent);
                using StringFormat line = new() { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center };
                using StringFormat paragraph = new() { FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.LineLimit };
                g.DrawString(Localization.Get("NotesTitle", language), heading, text, new RectangleF(14, 12, 260, 28), line);
                g.DrawString(OpenCount.ToString(culture), font, muted, new RectangleF(276, 12, 40, 28), line);
                g.DrawString("+", heading, accent, AddBounds, line);
                rows.Clear();
                completedBounds = RectangleF.Empty;
                GraphicsState state = g.Save();
                g.SetClip(viewport.ContentBounds);
                float y = CalendarViewport.HeaderHeight - viewport.ScrollOffset;
                foreach (NoteGroup group in groups)
                {
                    string label = group.Key.Length == 0 ? group.Date!.Value.ToString("D", culture) : Localization.Get(group.Key, language);
                    if (y + 26 >= viewport.ContentBounds.Top && y < viewport.ContentBounds.Bottom)
                        g.DrawString(label, heading, muted, new RectangleF(16, y, viewport.ContentBounds.Width - 4, 24), line);
                    y += 26;
                    foreach (NoteEntry entry in group.Entries)
                    {
                        RectangleF bounds = new(16, y, viewport.ContentBounds.Width - 4, RowHeight(entry));
                        if (bounds.Bottom >= viewport.ContentBounds.Top && bounds.Top < viewport.ContentBounds.Bottom)
                        {
                            rows.Add((entry, bounds));
                            DrawEntry(g, entry, titles[entry.Id], bounds, now, font, paragraph, culture, language, palette.muted, palette.title, palette.text);
                        }
                        y += RowHeight(entry);
                    }
                }
                if (done.Length > 0)
                {
                    completedBounds = new RectangleF(16, y, viewport.ContentBounds.Width - 4, 26);
                    string label = (completedExpanded ? "▾ " : "▸ ") + string.Format(culture, Localization.Get("NotesDoneGroup", language), done.Length);
                    g.DrawString(label, heading, muted, completedBounds, line);
                    y += 26;
                    if (completedExpanded)
                        foreach (NoteEntry entry in done)
                        {
                            RectangleF bounds = new(16, y, viewport.ContentBounds.Width - 4, RowHeight(entry));
                            if (bounds.Bottom >= viewport.ContentBounds.Top && bounds.Top < viewport.ContentBounds.Bottom)
                            {
                                rows.Add((entry, bounds));
                                DrawEntry(g, entry, titles[entry.Id], bounds, now, font, paragraph, culture, language, palette.muted, palette.title, palette.text);
                            }
                            y += RowHeight(entry);
                        }
                }
                if (groups.Count == 0 && done.Length == 0) g.DrawString(Localization.Get("NotesEmpty", language), font, muted, viewport.ContentBounds, paragraph);
                g.Restore(state);
                if (viewport.CanScroll)
                {
                    g.FillRectangle(muted, viewport.Track); g.FillRectangle(accent, viewport.Thumb);
                }
                Bitmap result = bitmap;
                bitmap = null; // Ownership passes to the caller.
                return result;
            }
            finally { bitmap?.Dispose(); }
        }

        private static string DisplayTitle(NoteEntry entry, DateTime now, CultureInfo culture)
        {
            string prefix = entry.RepeatsDaily ? "↻ " : entry.IsOverdue(now) ? entry.DueDate?.ToString("d", culture) + "  " : "";
            if (entry.DueTime is TimeOnly time) prefix += time.ToString("t", culture) + "  ";
            return prefix + entry.Title;
        }

        private static void DrawEntry(Graphics graphics, NoteEntry entry, string title, RectangleF bounds, DateTime now,
            Font font, StringFormat paragraph, CultureInfo culture, string language, Color muted, Color titleColor, Color textColor)
        {
            bool overdue = entry.IsOverdue(now);
            using (Pen box = new(overdue ? Color.FromArgb(225, 222, 172, 112) : muted))
                graphics.DrawRectangle(box, 18, bounds.Y + 5, 14, 14);
            bool completed = entry.IsCompletedOn(now);
            if (completed)
            {
                using Pen check = new(muted, 1.5f);
                graphics.DrawLines(check, new[] { new PointF(20, bounds.Y + 12), new PointF(24, bounds.Y + 16), new PointF(30, bounds.Y + 8) });
            }
            Color color = completed ? muted : overdue ? Color.FromArgb(245, 232, 193, 145) : entry.IsDueToday(now) ? titleColor : textColor;
            using SolidBrush ink = new(color);
            graphics.DrawString(title, font, ink, new RectangleF(42, bounds.Y + 3, LogicalWidth - 74, title.Contains('\n') ? 38 : 22), paragraph);
            if (completed && entry.CompletedAt is DateTime timestamp)
                graphics.DrawString(string.Format(culture, Localization.Get("NotesDoneAt", language), timestamp.ToString("t", culture)),
                    font, ink, new RectangleF(42, bounds.Bottom - 20, LogicalWidth - 74, 20), paragraph);
        }

        /// <summary>Explicitly ellipsizes at a grapheme boundary; GDI+ LineLimit alone can silently drop later lines.</summary>
        internal static string FitTitle(Graphics graphics, string text, Font font, float width, float height)
        {
            using StringFormat format = new() { FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.LineLimit };
            text = text.ReplaceLineEndings(" ");
            bool Fits(string value) => graphics.MeasureString(value, font, new SizeF(10000, 10000), format).Width <= width;
            int PrefixLength(string value, string ending)
            {
                int[] boundaries = StringInfo.ParseCombiningCharacters(value);
                int low = 0, high = boundaries.Length;
                while (low < high)
                {
                    int middle = (low + high + 1) / 2;
                    int end = middle == boundaries.Length ? value.Length : boundaries[middle];
                    if (Fits(value[..end] + ending)) low = middle; else high = middle - 1;
                }
                return low == boundaries.Length ? value.Length : boundaries[low];
            }
            if (Fits(text)) return text;
            if (height < font.GetHeight(graphics) * 2)
                return text[..PrefixLength(text, "…")].TrimEnd() + "…";
            int firstLength = PrefixLength(text, "");
            if (firstLength == 0) return "…";
            int wordBreak = text.LastIndexOf(' ', firstLength - 1, firstLength);
            if (wordBreak > 0) firstLength = wordBreak;
            string first = text[..firstLength].TrimEnd();
            string remainder = text[firstLength..].TrimStart();
            string second = Fits(remainder) ? remainder : remainder[..PrefixLength(remainder, "…")].TrimEnd() + "…";
            return first + "\n" + second;
        }

        internal NoteHit? HitTest(Point point)
        {
            PointF logical = new(point.X / viewport.Scale, point.Y / viewport.Scale);
            if (AddBounds.Contains(logical)) return new NoteHit(null, false, true);
            if (!viewport.ContentBounds.Contains(logical)) return null;
            if (completedBounds.Contains(logical)) return new NoteHit(null, false, false, true);
            foreach (var row in rows)
                if (row.Bounds.Contains(logical)) return new NoteHit(row.Entry.Id, logical.X < 38, false);
            return null;
        }
        internal bool ScrollWheel(Point point, int delta, int lines)
        {
            if (suspended || IsDisposed || widgetDragging || thumbDragging) return false;
            PointF logical = new(point.X / viewport.Scale, point.Y / viewport.Scale);
            if (logical.X < 12 || logical.X >= LogicalWidth - 12 || logical.Y < CalendarViewport.HeaderHeight || logical.Y >= viewport.ContentBounds.Bottom || !viewport.Wheel(delta, lines)) return false;
            Render(); return true;
        }
        protected override void OnMouseWheel(MouseEventArgs e) => ScrollWheel(e.Location, e.Delta, SystemInformation.MouseWheelScrollLines);
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (suspended || e.Button != MouseButtons.Left) return;
            PointF logical = new(e.X / viewport.Scale, e.Y / viewport.Scale);
            RectangleF track = viewport.Track; track.Inflate(4, 0);
            if (viewport.CanScroll && track.Contains(logical))
            {
                if (viewport.Thumb.Contains(logical)) { thumbDragging = true; thumbGrabOffset = logical.Y - viewport.Thumb.Y; Capture = true; }
                else { viewport.SetOffset(viewport.ScrollOffset + (logical.Y < viewport.Thumb.Top ? -1 : 1) * viewport.ViewportHeight); Render(); }
                return;
            }
            pressed = HitTest(e.Location);
            if (pressed != null) { Capture = true; return; }
            widgetDragging = !settings.NotesLocked; base.OnMouseDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (thumbDragging) { viewport.DragThumb(e.Y / viewport.Scale, thumbGrabOffset); Render(); return; }
            if (pressed != null) return;
            base.OnMouseMove(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (thumbDragging) { thumbDragging = false; Capture = false; return; }
            if (pressed is NoteHit hit)
            {
                pressed = null; Capture = false;
                if (!suspended && HitTest(e.Location) == hit)
                {
                    if (hit.CompletedGroup) { completedExpanded = !completedExpanded; Render(); }
                    else if (hit.Add) edit(null);
                    else if (hit.Id is Guid id)
                    {
                        if (hit.Checkbox)
                        {
                            NoteEntry? current = store.Entries.FirstOrDefault(n => n.Id == id);
                            if (current != null && !store.Complete(id, !current.IsCompletedOn(clock()))) MessageBox.Show(Localization.Get("NotesSaveError", settings.ClockLanguageCode), Text);
                        }
                        else if (store.Entries.FirstOrDefault(n => n.Id == id) is NoteEntry entry) edit(entry);
                    }
                }
                return;
            }
            base.OnMouseUp(e); widgetDragging = false;
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            if (!Capture)
            {
                pressed = null; thumbDragging = false;
                if (widgetDragging) { widgetDragging = false; base.OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0)); }
            }
            base.OnMouseCaptureChanged(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { shown = false; store.Changed -= RefreshData; timer.Stop(); timer.Dispose(); drag.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
