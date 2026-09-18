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
        private readonly WidgetDragHandler dragHandler;
        private readonly WeatherService weatherService = new();
        private readonly System.Windows.Forms.Timer timer;
        private CancellationTokenSource? refreshCts;
        private WeatherSnapshot? snapshot;
        private bool locked;
        private int refreshMinutes;
        private SystemWidgetStyle style;
        private string locationName = "Karlsruhe";
        private string languageCode = "de";
        private bool showForecast;
        private string? statusKey;

        private const int WidgetWidth = 330;
        private const int WS_EX_LAYERED = 0x00080000;

        /// <summary>
        /// Creates the weather widget with its location, display preferences, and position callback.
        /// </summary>
        /// <param name="locked">True to prevent the widget from being moved.</param>
        /// <param name="refreshMinutes">The requested refresh interval in minutes.</param>
        /// <param name="style">The visual style used to render the widget.</param>
        /// <param name="locationName">The city or location query used for weather lookup.</param>
        /// <param name="languageCode">The language code used for localized text.</param>
        /// <param name="showForecast">True to include the weather forecast.</param>
        /// <param name="location">The widget position in screen coordinates.</param>
        /// <param name="locationChanged">The callback that receives the widget&apos;s final position after a drag.</param>
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

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;

            dragHandler = new WidgetDragHandler(this, () => this.locked, RenderLayeredWindow, locationChanged);

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

        /// <summary>
        /// Processes native window messages while preventing mouse interaction from activating the widget.
        /// </summary>
        /// <param name="m">The native window message to inspect and process.</param>
        protected override void WndProc(ref Message m)
        {
            if (DesktopWidgetNative.HandleMouseActivation(ref m)) return;
            base.WndProc(ref m);
        }

        /// <summary>
        /// Loads weather data and starts periodic refreshes when the widget becomes visible.
        /// </summary>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (lifetimeEnded || IsDisposed || Disposing) return;
            shown = true;
            UpdateRefreshScheduling();
            await RefreshWeatherAsync();

        }

        /// <summary>
        /// Applies weather location, style, language, forecast, and refresh preferences.
        /// </summary>
        /// <param name="isLocked">The widget&apos;s updated position-lock preference.</param>
        /// <param name="newRefreshMinutes">The updated refresh interval in minutes.</param>
        /// <param name="newStyle">The updated widget style.</param>
        /// <param name="newLocationName">The updated weather location query.</param>
        /// <param name="newLanguageCode">The updated widget language code.</param>
        /// <param name="newShowForecast">True to include the forecast after applying the new preferences.</param>
        public void Apply(
            bool isLocked,
            int newRefreshMinutes,
            SystemWidgetStyle newStyle,
            string newLocationName,
            string newLanguageCode,
            bool newShowForecast)
        {
            if (lifetimeEnded || IsDisposed || Disposing) return;
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

            UpdateRefreshScheduling();
            if (IsHandleCreated && !IsDisposed) RenderLayeredWindow();

            if (mustRefresh && Visible && IsHandleCreated)
                _ = RefreshWeatherAsync();
        }

        /// <summary>
        /// Refreshes weather data asynchronously while preventing overlap and honoring cancellation.
        /// </summary>
        /// <returns>A task representing completion of the asynchronous operation.</returns>
        private async Task RefreshWeatherAsync()
        {
            if (!shown || lifetimeEnded || activitySuspended || IsDisposed || Disposing)
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
                if (lifetimeEnded || IsDisposed || cts.IsCancellationRequested || refreshCts != cts)
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
                if (lifetimeEnded || IsDisposed || cts.IsCancellationRequested || refreshCts != cts) return;
                statusKey = ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                    ? "WeatherLocationNotFound"
                    : "WeatherUnavailable";
                if (IsHandleCreated && !IsDisposed) RenderLayeredWindow();
            }
            catch (Exception ex)
            {
                if (lifetimeEnded || IsDisposed || cts.IsCancellationRequested || refreshCts != cts) return;
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

        /// <summary>
        /// Draws current conditions and the optional forecast while widget rendering is active.
        /// </summary>
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

                (Color panelColor, Color borderColor, Color titleColor, Color textColor, Color mutedColor, Color accentColor) = WidgetDrawing.GetPalette(style);

                if (style == SystemWidgetStyle.Glow)
                {
                    using Pen outerGlow = new(Color.FromArgb(48, accentColor), 5f);
                    using GraphicsPath glowPath = WidgetDrawing.RoundedRectangle(new RectangleF(3f, 3f, Width - 6f, Height - 6f), 14f);
                    g.DrawPath(outerGlow, glowPath);
                }

                using SolidBrush panel = new(panelColor);
                using Pen border = new(borderColor, 1f);
                using GraphicsPath path = WidgetDrawing.RoundedRectangle(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), 14f);
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
                    WidgetDrawing.DrawGlowText(g, widgetTitle, titleFont, 16, 13, accentColor);
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

            LayeredWidgetBitmap.Update(Handle, Location, bitmap);
        }

        /// <summary>
        /// Draws one weather label/value pair at the requested location.
        /// </summary>
        /// <param name="g">The drawing surface used for the operation.</param>
        /// <param name="font">The font used to draw the text.</param>
        /// <param name="textBrush">The brush used for primary text.</param>
        /// <param name="mutedBrush">The brush used for secondary text.</param>
        /// <param name="label">The display label for the value or test check.</param>
        /// <param name="value">The formatted value to display.</param>
        /// <param name="x">The horizontal coordinate.</param>
        /// <param name="y">The vertical coordinate.</param>
        private static void DrawInfo(Graphics g, Font font, Brush textBrush, Brush mutedBrush, string label, string value, int x, int y)
        {
            g.DrawString(label, font, mutedBrush, x, y);
            SizeF labelSize = g.MeasureString(label, font);
            g.DrawString(value, font, textBrush, x + Math.Max(64f, labelSize.Width + 7f), y);
        }

        /// <summary>
        /// Maps a weather condition code to the glyph used by the widget.
        /// </summary>
        /// <param name="code">The weather condition code returned by the forecast provider.</param>
        /// <returns>The display glyph associated with the weather condition code.</returns>
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

        /// <summary>
        /// Maps a weather condition code to its localized description resource key.
        /// </summary>
        /// <param name="code">The weather condition code returned by the forecast provider.</param>
        /// <returns>The resource key for the condition&apos;s localized description.</returns>
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

        /// <summary>
        /// Formats an abbreviated forecast day name in the selected language.
        /// </summary>
        /// <param name="date">The date to display using the selected language.</param>
        /// <param name="languageCode">The language code used for localized text.</param>
        /// <returns>The abbreviated day name in the requested language.</returns>
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

        private bool activitySuspended;
        private bool shown;
        private bool lifetimeEnded;

        private void UpdateRefreshScheduling()
        {
            if (lifetimeEnded || IsDisposed || Disposing) return;
            timer.Enabled = shown && !activitySuspended;
        }
        /// <summary>
        /// Cancels weather fetching and stops periodic refreshes until automatic suspension ends.
        /// </summary>
        /// <param name="suspended">True to pause background activity; false to resume it.</param>
        internal void SetActivitySuspended(bool suspended)
        {
            if (activitySuspended == suspended || lifetimeEnded || IsDisposed || Disposing) return;
            activitySuspended = suspended;
            UpdateRefreshScheduling();
            if (suspended) refreshCts?.Cancel(); else _ = RefreshWeatherAsync();
        }
        /// <summary>
        /// Releases the resources owned by this weather widget form.
        /// </summary>
        /// <param name="disposing">True when managed resources should be released during explicit disposal.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && !lifetimeEnded)
            {
                lifetimeEnded = true;
                shown = false;
                dragHandler.Dispose();
                refreshCts?.Cancel();
                refreshCts?.Dispose();
                refreshCts = null;
                timer.Stop();
                timer.Dispose();
                weatherService.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
