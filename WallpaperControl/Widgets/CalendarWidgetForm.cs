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
        private const int WidgetWidth = 340;
        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int MA_NOACTIVATE = 3;
        private const int WS_EX_LAYERED = 0x00080000;
        private const byte AC_SRC_OVER = 0x00;
        private const byte AC_SRC_ALPHA = 0x01;
        private const int ULW_ALPHA = 0x00000002;

        private readonly Action<Point> locationChanged;
        private readonly ICalendarProvider calendarProvider;
        private readonly System.Windows.Forms.Timer refreshTimer = new();
        private CancellationTokenSource? refreshCancellation = new();
        private bool locked;
        private bool dragging;
        private Point dragMouseStart;
        private Point dragFormStart;
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
            this.locationChanged = locationChanged;
            this.calendarProvider = calendarProvider ?? throw new ArgumentNullException(nameof(calendarProvider));

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;

            MouseDown += BeginDrag;
            MouseMove += ContinueDrag;
            MouseUp += EndDrag;
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
            if (m.Msg == WM_MOUSEACTIVATE)
            {
                m.Result = new IntPtr(MA_NOACTIVATE);
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            RenderLayeredWindow();
            RefreshCalendar();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
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
            if (IsHandleCreated && !refreshTimer.Enabled) refreshTimer.Start();

            // Final height depends on how many appointments occur on the selected
            // occupied days and is calculated by RenderLayeredWindow().
            if (ClientSize.Width != WidgetWidth)
                ClientSize = new Size(WidgetWidth, ClientSize.Height);

            if (IsHandleCreated && !IsDisposed)
                RenderLayeredWindow();
        }

        private void RenderLayeredWindow()
        {
            if (!IsHandleCreated || IsDisposed) return;

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

                (Color panelColor, Color borderColor, Color titleColor, Color textColor, Color mutedColor, Color accentColor) = GetPalette();

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
                    DrawGlowText(g, widgetTitle, titleFont, 16, 13, accentColor);
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

            UpdateLayeredBitmap(bitmap);
        }

        public async void RefreshCalendar()
        {
            if (IsDisposed) return;

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

        private (Color panel, Color border, Color title, Color text, Color muted, Color accent) GetPalette()
        {
            return style switch
            {
                SystemWidgetStyle.Minimal => (
                    Color.FromArgb(112, 12, 18, 24), Color.FromArgb(48, 255, 255, 255), Color.FromArgb(245, 241, 246),
                    Color.FromArgb(238, 235, 241, 246), Color.FromArgb(185, 174, 188, 199), Color.FromArgb(185, 174, 188, 199)),
                SystemWidgetStyle.Clean => (
                    Color.FromArgb(178, 17, 24, 31), Color.FromArgb(105, 92, 184, 224), Color.FromArgb(245, 241, 246),
                    Color.FromArgb(238, 235, 241, 246), Color.FromArgb(200, 174, 188, 199), Color.FromArgb(210, 92, 184, 224)),
                _ => (
                    Color.FromArgb(166, 10, 18, 25), Color.FromArgb(175, 82, 201, 255), Color.FromArgb(255, 228, 249, 255),
                    Color.FromArgb(245, 239, 248, 252), Color.FromArgb(205, 177, 211, 226), Color.FromArgb(235, 88, 211, 255))
            };
        }

        private static void DrawGlowText(Graphics g, string text, Font font, float x, float y, Color color)
        {
            using SolidBrush glow1 = new(Color.FromArgb(40, color));
            using SolidBrush glow2 = new(Color.FromArgb(70, color));
            using SolidBrush core = new(Color.FromArgb(255, color));
            g.DrawString(text, font, glow1, x - 2, y - 2);
            g.DrawString(text, font, glow1, x + 2, y + 2);
            g.DrawString(text, font, glow2, x - 1, y);
            g.DrawString(text, font, glow2, x + 1, y);
            g.DrawString(text, font, core, x, y);
        }

        private void UpdateLayeredBitmap(Bitmap bitmap)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memoryDc = CreateCompatibleDC(screenDc);
            IntPtr bitmapHandle = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;

            try
            {
                bitmapHandle = bitmap.GetHbitmap(Color.FromArgb(0));
                oldBitmap = SelectObject(memoryDc, bitmapHandle);
                NativePoint source = new(0, 0);
                NativePoint destination = new(Left, Top);
                NativeSize size = new(bitmap.Width, bitmap.Height);
                BlendFunction blend = new() { BlendOp = AC_SRC_OVER, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
                UpdateLayeredWindow(Handle, screenDc, ref destination, ref size, memoryDc, ref source, 0, ref blend, ULW_ALPHA);
            }
            finally
            {
                if (oldBitmap != IntPtr.Zero) SelectObject(memoryDc, oldBitmap);
                if (bitmapHandle != IntPtr.Zero) DeleteObject(bitmapHandle);
                if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
                if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        private void BeginDrag(object? sender, MouseEventArgs e)
        {
            if (locked || e.Button != MouseButtons.Left) return;
            dragging = true;
            dragMouseStart = Cursor.Position;
            dragFormStart = Location;
            Capture = true;
        }

        private void ContinueDrag(object? sender, MouseEventArgs e)
        {
            if (!dragging) return;
            Point now = Cursor.Position;
            Location = new Point(dragFormStart.X + now.X - dragMouseStart.X, dragFormStart.Y + now.Y - dragMouseStart.Y);
            RenderLayeredWindow();
        }

        private void EndDrag(object? sender, MouseEventArgs e)
        {
            if (!dragging || e.Button != MouseButtons.Left) return;
            dragging = false;
            Capture = false;
            locationChanged(Location);
        }

        private static GraphicsPath RoundedRectangle(RectangleF rect, float radius)
        {
            radius = Math.Max(0.5f, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2f));
            float d = radius * 2f;
            GraphicsPath path = new();
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }


        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X; public int Y; public NativePoint(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSize { public int Width; public int Height; public NativeSize(int width, int height) { Width = width; Height = height; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BlendFunction { public byte BlendOp; public byte BlendFlags; public byte SourceConstantAlpha; public byte AlphaFormat; }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr hdcDst, ref NativePoint pptDst, ref NativeSize psize, IntPtr hdcSrc, ref NativePoint pptSrc, int crKey, ref BlendFunction pblend, int dwFlags);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
    }
}
