using System;
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
    internal sealed class WeatherWidgetForm : Form
    {
        private readonly Action<Point> locationChanged;
        private readonly WeatherService weatherService = new();
        private readonly System.Windows.Forms.Timer timer;
        private CancellationTokenSource? refreshCts;
        private WeatherSnapshot? snapshot;
        private bool locked;
        private bool dragging;
        private Point dragMouseStart;
        private Point dragFormStart;
        private int refreshMinutes;
        private SystemWidgetStyle style;
        private string locationName = "Karlsruhe";
        private string languageCode = "de";
        private bool showForecast;
        private string? statusKey;

        private const int WidgetWidth = 330;
        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int MA_NOACTIVATE = 3;
        private const int WS_EX_LAYERED = 0x00080000;
        private const byte AC_SRC_OVER = 0x00;
        private const byte AC_SRC_ALPHA = 0x01;
        private const int ULW_ALPHA = 0x00000002;

        public WeatherWidgetForm(
            bool locked,
            int refreshMinutes,
            SystemWidgetStyle style,
            string locationName,
            string languageCode,
            bool showForecast,
            Point location,
            Action<Point> locationChanged)
        {
            this.locationChanged = locationChanged;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;

            MouseDown += BeginDrag;
            MouseMove += ContinueDrag;
            MouseUp += EndDrag;

            timer = new System.Windows.Forms.Timer();
            timer.Tick += async (_, _) => await RefreshWeatherAsync();

            Apply(locked, refreshMinutes, style, locationName, languageCode, showForecast);
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

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            await RefreshWeatherAsync();
            if (!activitySuspended) timer.Start();
        }

        public void Apply(
            bool isLocked,
            int newRefreshMinutes,
            SystemWidgetStyle newStyle,
            string newLocationName,
            string newLanguageCode,
            bool newShowForecast)
        {
            string normalizedLocation = string.IsNullOrWhiteSpace(newLocationName) ? "Karlsruhe" : newLocationName.Trim();
            bool mustRefresh = !string.Equals(locationName, normalizedLocation, StringComparison.OrdinalIgnoreCase) ||
                               !string.Equals(languageCode, newLanguageCode, StringComparison.OrdinalIgnoreCase);

            locked = isLocked;
            refreshMinutes = Math.Clamp(newRefreshMinutes, 15, 120);
            style = newStyle;
            locationName = normalizedLocation;
            languageCode = string.IsNullOrWhiteSpace(newLanguageCode) ? Localization.CurrentLanguage : newLanguageCode;
            showForecast = newShowForecast;
            timer.Interval = refreshMinutes * 60 * 1000;

            Size newSize = new(WidgetWidth, showForecast ? 278 : 190);
            if (Size != newSize)
                ClientSize = newSize;

            if (Visible && !timer.Enabled && !activitySuspended) timer.Start();
            if (IsHandleCreated && !IsDisposed) RenderLayeredWindow();

            if (mustRefresh && Visible && IsHandleCreated)
                _ = RefreshWeatherAsync();
        }

        private async Task RefreshWeatherAsync()
        {
            if (activitySuspended || IsDisposed)
                return;

            CancellationTokenSource cts = new();
            CancellationTokenSource? previous = refreshCts;
            refreshCts = cts;
            previous?.Cancel();
            previous?.Dispose();

            try
            {
                statusKey = snapshot == null ? "WeatherLoading" : null;
                RenderLayeredWindow();

                WeatherSnapshot newSnapshot = await weatherService.FetchAsync(locationName, languageCode, cts.Token);
                if (IsDisposed || cts.IsCancellationRequested || refreshCts != cts)
                    return;

                snapshot = newSnapshot;
                statusKey = null;
                if (IsHandleCreated) RenderLayeredWindow();
            }
            catch (OperationCanceledException)
            {
            }
            catch (InvalidOperationException ex)
            {
                if (cts.IsCancellationRequested || refreshCts != cts) return;
                statusKey = ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                    ? "WeatherLocationNotFound"
                    : "WeatherUnavailable";
                if (IsHandleCreated && !IsDisposed) RenderLayeredWindow();
            }
            catch (Exception ex)
            {
                if (cts.IsCancellationRequested || refreshCts != cts) return;
                AppLogger.Warning("Weather widget could not load weather data.", ex);
                statusKey = "WeatherUnavailable";
                if (IsHandleCreated && !IsDisposed) RenderLayeredWindow();
            }
            finally
            {
                if (refreshCts == cts)
                {
                    refreshCts = null;
                    cts.Dispose();
                }
            }
        }

        private void RenderLayeredWindow()
        {
            if (activitySuspended || !IsHandleCreated || IsDisposed) return;

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
                using Font cityFont = new("Segoe UI Semibold", 10.5f, FontStyle.Bold);
                using Font tempFont = new("Segoe UI Semibold", 27f, FontStyle.Bold);
                using Font conditionFont = new("Segoe UI", 10f, FontStyle.Regular);
                using Font rowFont = new("Segoe UI", 8.8f, FontStyle.Regular);
                using Font forecastDayFont = new("Segoe UI Semibold", 8.5f, FontStyle.Bold);
                using Font forecastTempFont = new("Segoe UI", 8.5f, FontStyle.Regular);
                using Font iconFont = new("Segoe UI Symbol", 28f, FontStyle.Regular);
                using Font smallIconFont = new("Segoe UI Symbol", 15f, FontStyle.Regular);
                using SolidBrush titleBrush = new(titleColor);
                using SolidBrush textBrush = new(textColor);
                using SolidBrush mutedBrush = new(mutedColor);
                using SolidBrush accentBrush = new(accentColor);

                string widgetTitle = Localization.Get("WeatherWidgetTitle", languageCode).ToUpperInvariant();
                if (style == SystemWidgetStyle.Glow)
                    DrawGlowText(g, widgetTitle, titleFont, 16, 13, accentColor);
                else
                    g.DrawString(widgetTitle, titleFont, titleBrush, 16, 13);

                if (style != SystemWidgetStyle.Minimal)
                    g.FillRectangle(accentBrush, 16, 40, Width - 32, style == SystemWidgetStyle.Glow ? 2 : 1);

                if (snapshot == null)
                {
                    string status = Localization.Get(statusKey ?? "WeatherLoading", languageCode);
                    g.DrawString(locationName, cityFont, textBrush, 16, 60);
                    g.DrawString(status, conditionFont, mutedBrush, 16, 92);
                }
                else
                {
                string displayLocation = snapshot.LocationName;
                if (!string.IsNullOrWhiteSpace(snapshot.Country))
                    displayLocation += $", {snapshot.Country}";

                g.DrawString(displayLocation, cityFont, textBrush, 16, 55);
                g.DrawString(GetWeatherGlyph(snapshot.WeatherCode), iconFont, accentBrush, 18, 82);
                g.DrawString($"{snapshot.TemperatureC:0}°", tempFont, textBrush, 67, 76);
                g.DrawString(Localization.Get(GetWeatherDescriptionKey(snapshot.WeatherCode), languageCode), conditionFont, mutedBrush, 148, 90);

                int y = 132;
                DrawInfo(g, rowFont, textBrush, mutedBrush, Localization.Get("WeatherFeelsLike", languageCode), $"{snapshot.ApparentTemperatureC:0} °C", 16, y);
                DrawInfo(g, rowFont, textBrush, mutedBrush, Localization.Get("WeatherHumidity", languageCode), $"{snapshot.RelativeHumidityPercent:0}%", 170, y);
                y += 24;
                DrawInfo(g, rowFont, textBrush, mutedBrush, Localization.Get("WeatherRain", languageCode), $"{snapshot.PrecipitationMm:0.0} mm", 16, y);
                DrawInfo(g, rowFont, textBrush, mutedBrush, Localization.Get("WeatherWind", languageCode), $"{snapshot.WindSpeedKmh:0} km/h", 170, y);

                if (showForecast)
                {
                    int lineY = 181;
                    if (style != SystemWidgetStyle.Minimal)
                    {
                        using Pen separator = new(Color.FromArgb(90, accentColor), 1f);
                        g.DrawLine(separator, 16, lineY, Width - 16, lineY);
                    }

                    WeatherDaySnapshot[] forecast = snapshot.Days.Skip(1).Take(3).ToArray();
                    for (int i = 0; i < forecast.Length; i++)
                    {
                        WeatherDaySnapshot day = forecast[i];
                        int x = 18 + i * 101;
                        string dayName = GetShortDayName(day.Date, languageCode).ToUpperInvariant();
                        g.DrawString(dayName, forecastDayFont, textBrush, x, 194);
                        g.DrawString(GetWeatherGlyph(day.WeatherCode), smallIconFont, accentBrush, x, 216);
                        g.DrawString($"{day.MaxTemperatureC:0}° / {day.MinTemperatureC:0}°", forecastTempFont, mutedBrush, x + 28, 220);
                    }
                }

                }

                if (statusKey != null)
                {
                    string status = Localization.Get(statusKey, languageCode);
                    using Font statusFont = new("Segoe UI", 7.5f, FontStyle.Regular);
                    SizeF size = g.MeasureString(status, statusFont);
                    g.DrawString(status, statusFont, mutedBrush, Width - size.Width - 16, Height - 20);
                }
            }

            UpdateLayeredBitmap(bitmap);
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

        private static void DrawInfo(Graphics g, Font font, Brush textBrush, Brush mutedBrush, string label, string value, int x, int y)
        {
            g.DrawString(label, font, mutedBrush, x, y);
            SizeF labelSize = g.MeasureString(label, font);
            g.DrawString(value, font, textBrush, x + Math.Max(64f, labelSize.Width + 7f), y);
        }

        private static string GetWeatherGlyph(int code)
        {
            if (code == 0) return "☀";
            if (code is 1 or 2) return "◐";
            if (code == 3) return "☁";
            if (code is 45 or 48) return "≋";
            if (code is >= 51 and <= 67) return "☂";
            if (code is >= 71 and <= 77) return "❄";
            if (code is >= 80 and <= 82) return "☂";
            if (code is >= 85 and <= 86) return "❄";
            if (code is >= 95 and <= 99) return "⚡";
            return "☁";
        }

        private static string GetWeatherDescriptionKey(int code)
        {
            if (code == 0) return "WeatherClear";
            if (code is 1 or 2) return "WeatherPartlyCloudy";
            if (code == 3) return "WeatherCloudy";
            if (code is 45 or 48) return "WeatherFog";
            if (code is >= 51 and <= 67) return "WeatherRainy";
            if (code is >= 71 and <= 77) return "WeatherSnowy";
            if (code is >= 80 and <= 82) return "WeatherShowers";
            if (code is >= 85 and <= 86) return "WeatherSnowShowers";
            if (code is >= 95 and <= 99) return "WeatherThunderstorm";
            return "WeatherCloudy";
        }

        private static string GetShortDayName(DateTime date, string languageCode)
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
                return CultureInfo.GetCultureInfo(cultureName).DateTimeFormat.GetAbbreviatedDayName(date.DayOfWeek).TrimEnd('.');
            }
            catch
            {
                return date.ToString("ddd", CultureInfo.InvariantCulture);
            }
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
                BlendFunction blend = new()
                {
                    BlendOp = AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = AC_SRC_ALPHA
                };

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

        private bool activitySuspended;
        internal void SetActivitySuspended(bool suspended)
        {
            if (activitySuspended == suspended || IsDisposed) return;
            activitySuspended = suspended;
            if (suspended) timer.Stop(); else timer.Start();
            if (suspended) refreshCts?.Cancel(); else _ = RefreshWeatherAsync();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                refreshCts?.Cancel();
                refreshCts?.Dispose();
                timer.Dispose();
                weatherService.Dispose();
            }
            base.Dispose(disposing);
        }

        private static GraphicsPath RoundedRectangle(RectangleF rect, float radius)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
                return new GraphicsPath();

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
        private struct NativePoint
        {
            public int X;
            public int Y;
            public NativePoint(int x, int y) { X = x; Y = y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSize
        {
            public int Width;
            public int Height;
            public NativeSize(int width, int height) { Width = width; Height = height; }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BlendFunction
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr hdcDst, ref NativePoint pptDst, ref NativeSize psize, IntPtr hdcSrc, ref NativePoint pptSrc, int crKey, ref BlendFunction pblend, int dwFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}
