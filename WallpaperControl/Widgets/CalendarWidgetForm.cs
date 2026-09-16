using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WallpaperControl
{
    internal sealed class CalendarWidgetForm : Form
    {
        private readonly WidgetDragHandler dragHandler;
        private const int WidgetWidth = 340;
        private const int WS_EX_LAYERED = 0x00080000;
        private readonly ICalendarProvider calendarProvider;
        private readonly System.Windows.Forms.Timer refreshTimer = new();
        private CancellationTokenSource? refreshCancellation = new();
        private bool locked;
        private SystemWidgetStyle style;
        private int maxEntries;
        private bool showLocation;
        private int refreshMinutes;
        private string languageCode = "de";

        public CalendarWidgetForm(
            bool locked,
            SystemWidgetStyle style,
            int maxEntries,
            bool showLocation,
            int refreshMinutes,
            string languageCode,
            ICalendarProvider calendarProvider,
            Point location,
            Action<Point> locationChanged)
        {
            this.calendarProvider = calendarProvider ?? throw new ArgumentNullException(nameof(calendarProvider));

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;

            dragHandler = new WidgetDragHandler(this, () => this.locked, RenderLayeredWindow, locationChanged);
            refreshTimer.Tick += (_, _) => RefreshCalendar();

            Apply(locked, style, maxEntries, showLocation, refreshMinutes, languageCode);
            Location = WidgetSettings.EnsureVisible(location, Size);
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TOOLWINDOW = 0x00000080;
                const int WS_EX_NOACTIVATE = 0x08000000;
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (DesktopWidgetNative.HandleMouseActivation(ref m)) return;
            base.WndProc(ref m);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            RenderLayeredWindow();
            RefreshCalendar();
        }

        private bool activitySuspended;
        internal void SetActivitySuspended(bool suspended)
        {
            if (activitySuspended == suspended || IsDisposed) return;
            activitySuspended = suspended;
            if (suspended) refreshTimer.Stop(); else refreshTimer.Start();
            if (suspended) refreshCancellation?.Cancel(); else { var previous = refreshCancellation; refreshCancellation = new CancellationTokenSource(); previous?.Dispose(); RefreshCalendar(); }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                dragHandler.Dispose();
                refreshTimer.Stop();
                refreshTimer.Dispose();

                CancellationTokenSource? cancellation = Interlocked.Exchange(ref refreshCancellation, null);
                if (cancellation != null)
                {
                    try { cancellation.Cancel(); } catch (ObjectDisposedException) { }
                    cancellation.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        public void Apply(
            bool isLocked,
            SystemWidgetStyle newStyle,
            int newMaxEntries,
            bool newShowLocation,
            int newRefreshMinutes,
            string newLanguageCode)
        {
            locked = isLocked;
            style = newStyle;
            maxEntries = newMaxEntries switch { <= 3 => 3, >= 9 => 9, _ => 5 };
            showLocation = newShowLocation;
            refreshMinutes = Math.Clamp(newRefreshMinutes, 15, 120);
            languageCode = string.IsNullOrWhiteSpace(newLanguageCode) ? Localization.CurrentLanguage : newLanguageCode;
            refreshTimer.Interval = refreshMinutes * 60 * 1000;
            if (IsHandleCreated && !refreshTimer.Enabled && !activitySuspended) refreshTimer.Start();

            // Final height depends on how many appointments occur on the selected
            // occupied days and is calculated by RenderLayeredWindow().
            if (ClientSize.Width != WidgetWidth)
                ClientSize = new Size(WidgetWidth, ClientSize.Height);

            if (IsHandleCreated && !IsDisposed)
                RenderLayeredWindow();
        }

        private void RenderLayeredWindow()
        {
            if (activitySuspended || !IsHandleCreated || IsDisposed) return;

            IReadOnlyList<CalendarEvent> entries = calendarProvider.GetUpcoming(DateTime.Now, maxEntries, languageCode);
            int occupiedDays = entries.Select(e => e.Start.Date).Distinct().Count();
            int locationLineCount = showLocation
                ? entries.Count(e => !string.IsNullOrWhiteSpace(e.Location))
                : 0;
            int contentHeight = entries.Count == 0
                ? 120
                : 79 + occupiedDays * 32 + entries.Count * 21 + locationLineCount * 13;
            int desiredHeight = Math.Max(120, contentHeight);
            if (ClientSize.Width != WidgetWidth || ClientSize.Height != desiredHeight)
                ClientSize = new Size(WidgetWidth, desiredHeight);

            using Bitmap bitmap = new(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.CompositingMode = CompositingMode.SourceOver;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                (Color panelColor, Color borderColor, Color titleColor, Color textColor, Color mutedColor, Color accentColor) = WidgetDrawing.GetPalette(style);

                if (style == SystemWidgetStyle.Glow)
                {
                    using Pen outerGlow = new(Color.FromArgb(48, accentColor), 5f);
                    using GraphicsPath glowPath = RoundedRectangle(new RectangleF(3f, 3f, Width - 6f, Height - 6f), 14f);
                    g.DrawPath(outerGlow, glowPath);
                }

                using SolidBrush panel = new(panelColor);
                using Pen border = new(borderColor, 1f);
                using GraphicsPath path = RoundedRectangle(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), 14f);
                g.FillPath(panel, path);
                g.DrawPath(border, path);

                using Font titleFont = new("Segoe UI Semibold", 11f, FontStyle.Bold);
                using Font dayFont = new("Segoe UI Semibold", 8.5f, FontStyle.Bold);
                using Font timeFont = new("Segoe UI Semibold", 9.2f, FontStyle.Bold);
                using Font subjectFont = new("Segoe UI", 9.2f, FontStyle.Regular);
                using Font locationFont = new("Segoe UI", 7.8f, FontStyle.Regular);
                using SolidBrush titleBrush = new(titleColor);
                using SolidBrush textBrush = new(textColor);
                using SolidBrush mutedBrush = new(mutedColor);
                using SolidBrush accentBrush = new(accentColor);

                string widgetTitle = Localization.Get("CalendarWidgetTitle", languageCode).ToUpperInvariant();
                if (style == SystemWidgetStyle.Glow)
                    WidgetDrawing.DrawGlowText(g, widgetTitle, titleFont, 16, 13, accentColor);
                else
                    g.DrawString(widgetTitle, titleFont, titleBrush, 16, 13);

                if (style != SystemWidgetStyle.Minimal)
                    g.FillRectangle(accentBrush, 16, 40, Width - 32, style == SystemWidgetStyle.Glow ? 2 : 1);

                int y = 54;
                DateTime? currentDay = null;
                for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
                {
                    CalendarEvent entry = entries[entryIndex];
                    if (currentDay?.Date != entry.Start.Date)
                    {
                        currentDay = entry.Start.Date;
                        string day = FormatDayHeading(entry.Start.Date);
                        g.DrawString(day.ToUpperInvariant(), dayFont, accentBrush, 16, y);
                        y += 20;
                    }

                    string time = entry.IsAllDay ? Localization.Get("CalendarAllDay", languageCode) : entry.Start.ToString("HH:mm", CultureInfo.InvariantCulture);
                    bool isHoliday = entry.SourceName.StartsWith("Holiday:", StringComparison.Ordinal);
                    if (isHoliday)
                    {
                        RectangleF holidayRect = new(12f, y - 2f, Width - 24f, 20f);
                        using GraphicsPath holidayPath = RoundedRectangle(holidayRect, 4f);
                        using SolidBrush holidayBrush = new(Color.FromArgb(style == SystemWidgetStyle.Minimal ? 145 : 190, 190, 74, 72));
                        g.FillPath(holidayBrush, holidayPath);

                        if (style == SystemWidgetStyle.Glow)
                        {
                            using Pen holidayGlow = new(Color.FromArgb(55, 220, 92, 88), 2f);
                            g.DrawPath(holidayGlow, holidayPath);
                        }

                        using SolidBrush holidayTextBrush = new(Color.White);
                        // The colored holiday bar already communicates that this is a
                        // special all-day entry, so omit the redundant "All day" label.
                        g.DrawString(entry.Title, subjectFont, holidayTextBrush, 16, y - 1);
                    }
                    else
                    {
                        g.DrawString(time, timeFont, mutedBrush, 16, y);
                        g.DrawString(entry.Title, subjectFont, textBrush, 72, y - 1);
                    }
                    y += 21;

                    if (showLocation && !string.IsNullOrWhiteSpace(entry.Location))
                    {
                        g.DrawString(entry.Location, locationFont, mutedBrush, 72, y - 2);
                        y += 13;
                    }

                    bool nextEntryIsSameDay = entryIndex + 1 < entries.Count
                        && entries[entryIndex + 1].Start.Date == entry.Start.Date;
                    if (!nextEntryIsSameDay)
                        y += 12;
                }

                if (entries.Count == 0)
                {
                    string emptyText = Localization.Get("CalendarNoUpcoming", languageCode);
                    g.DrawString(emptyText, subjectFont, mutedBrush, 16, 65);
                }

                string status = Localization.Get(calendarProvider.StatusResourceKey, languageCode);
                using Font statusFont = new("Segoe UI", 7.2f, FontStyle.Italic);
                SizeF statusSize = g.MeasureString(status, statusFont);
                g.DrawString(status, statusFont, mutedBrush, Width - statusSize.Width - 16, Height - 20);
            }

            LayeredWidgetBitmap.Update(Handle, Location, bitmap);
        }

        public async void RefreshCalendar()
        {
            if (activitySuspended || IsDisposed) return;

            CancellationTokenSource? cancellation = Volatile.Read(ref refreshCancellation);
            if (cancellation == null) return;

            CancellationToken token;
            try { token = cancellation.Token; }
            catch (ObjectDisposedException) { return; }

            try
            {
                await calendarProvider.RefreshAsync(token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException) when (IsDisposed || Volatile.Read(ref refreshCancellation) == null)
            {
                return;
            }

            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired)
            {
                try { BeginInvoke((Action)RenderLayeredWindow); } catch { }
            }
            else
            {
                RenderLayeredWindow();
            }
        }

        private string FormatDayHeading(DateTime date)
        {
            if (date.Date == DateTime.Today)
                return $"{Localization.Get("CalendarToday", languageCode)} · {FormatDate(date)}";
            if (date.Date == DateTime.Today.AddDays(1))
                return $"{Localization.Get("CalendarTomorrow", languageCode)} · {FormatDate(date)}";
            return FormatDate(date);
        }

        private string FormatDate(DateTime date)
        {
            try
            {
                string cultureName = languageCode switch
                {
                    "de" => "de-DE",
                    "fr" => "fr-FR",
                    "es" => "es-ES",
                    "ja" => "ja-JP",
                    _ => "en-US"
                };
                CultureInfo culture = CultureInfo.GetCultureInfo(cultureName);
                return date.ToString(languageCode == "ja" ? "M月d日" : "dd. MMMM", culture);
            }
            catch
            {
                return date.ToString("dd. MMMM", CultureInfo.InvariantCulture);
            }
        }

        private static GraphicsPath RoundedRectangle(RectangleF rect, float radius)
        {
            return WidgetDrawing.RoundedRectangle(rect, radius, skipEmptyBounds: false);
        }
    }
}
