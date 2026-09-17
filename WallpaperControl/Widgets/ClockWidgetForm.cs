using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Windows.Forms;

namespace WallpaperControl
{
    internal sealed class ClockWidgetForm : Form
    {
        private readonly System.Windows.Forms.Timer timer;
        private readonly Action<Point> locationChanged;
        private int clockSize;
        private bool locked;
        private bool showSeconds;
        private ClockWidgetStyle style;
        private string languageCode;
        private bool dragging;
        private Point dragMouseStart;
        private Point dragFormStart;

        /// <summary>
        /// Creates the desktop clock with its style, size, language, and position callback.
        /// </summary>
        /// <param name="clockSize">The requested clock size in logical pixels.</param>
        /// <param name="locked">True to prevent the widget from being moved.</param>
        /// <param name="showSeconds">True to display seconds in the clock.</param>
        /// <param name="style">The visual style used to render the widget.</param>
        /// <param name="languageCode">The language code used for localized text.</param>
        /// <param name="location">The widget position in screen coordinates.</param>
        /// <param name="locationChanged">The callback that receives the widget&apos;s final position after a drag.</param>
        public ClockWidgetForm(
            int clockSize,
            bool locked,
            bool showSeconds,
            ClockWidgetStyle style,
            string languageCode,
            Point location,
            Action<Point> locationChanged)
        {
            this.clockSize = Math.Clamp(clockSize, 70, 240);
            this.locked = locked;
            this.showSeconds = showSeconds;
            this.style = style;
            this.languageCode = languageCode;
            this.locationChanged = locationChanged;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;

            // Deliberately use an almost-black transparency key instead of a
            // saturated key colour. This keeps anti-aliased edge pixels neutral.
            BackColor = Color.FromArgb(1, 2, 3);
            TransparencyKey = Color.FromArgb(1, 2, 3);
            DoubleBuffered = true;

            SetSize();
            Location = WidgetSettings.EnsureVisible(location, Size);

            MouseDown += BeginDrag;
            MouseMove += ContinueDrag;
            MouseUp += EndDrag;

            timer = new System.Windows.Forms.Timer
            {
                Interval = 1000
            };
            timer.Tick += (_, _) => Invalidate();
            if (!activitySuspended) timer.Start();
        }

        protected override bool ShowWithoutActivation => true;

        /// <summary>
        /// Processes native window messages while preventing mouse interaction from activating the widget.
        /// </summary>
        /// <param name="m">The native window message to inspect and process.</param>
        protected override void WndProc(ref Message m)
        {
            if (DesktopWidgetNative.HandleMouseActivation(ref m)) return;
            base.WndProc(ref m);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TOOLWINDOW = 0x00000080;
                const int WS_EX_NOACTIVATE = 0x08000000;

                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        /// <summary>
        /// Applies the clock&apos;s display and locking preferences and refreshes its size and appearance.
        /// </summary>
        /// <param name="size">The clock size in pixels.</param>
        /// <param name="isLocked">The widget&apos;s updated position-lock preference.</param>
        /// <param name="secondsVisible">True to display the seconds component.</param>
        /// <param name="currentStyle">The selected clock style.</param>
        /// <param name="currentLanguageCode">The language code used by the clock.</param>
        public void Apply(int size, bool isLocked, bool secondsVisible, ClockWidgetStyle currentStyle, string currentLanguageCode)
        {
            clockSize = Math.Clamp(size, 70, 240);
            locked = isLocked;
            showSeconds = secondsVisible;
            style = currentStyle;
            languageCode = currentLanguageCode;
            SetSize();
            Location = WidgetSettings.EnsureVisible(Location, Size);
            Invalidate();
        }

        /// <summary>
        /// Draws the current time and date using the selected clock style.
        /// </summary>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            float scale = clockSize / 150f;
            float centerX = ClientSize.Width / 2f;

            CultureInfo culture;
            try
            {
                culture = CultureInfo.GetCultureInfo(languageCode);
            }
            catch (CultureNotFoundException)
            {
                culture = Localization.CurrentCulture;
            }

            DateTime now = DateTime.Now;
            string time = now.ToString(showSeconds ? "HH:mm:ss" : "HH:mm", culture);
            string date = FormatDate(now, culture);

            switch (style)
            {
                case ClockWidgetStyle.Minimal:
                    DrawSimpleStyle(g, time, date, scale, centerX, "Segoe UI Light", FontStyle.Regular, Color.FromArgb(235, 235, 238, 242), true, false, false);
                    break;
                case ClockWidgetStyle.Clean:
                    DrawSimpleStyle(g, time, date, scale, centerX, "Segoe UI", FontStyle.Bold, Color.White, false, false, false);
                    break;
                case ClockWidgetStyle.Glow:
                    DrawSimpleStyle(g, time, date, scale, centerX, "Segoe UI Light", FontStyle.Regular, Color.FromArgb(255, 225, 248, 255), true, true, false);
                    break;
                case ClockWidgetStyle.Classic:
                    DrawSimpleStyle(g, time, date, scale, centerX, "Georgia", FontStyle.Regular, Color.FromArgb(245, 242, 239, 232), true, false, true);
                    break;
                default:
                    DrawTime(g, time, scale, centerX);
                    DrawDivider(g, scale, centerX);
                    DrawDate(g, date, scale, centerX);
                    break;
            }
        }

        /// <summary>
        /// Formats the clock&apos;s date using the supplied culture and language-specific layout.
        /// </summary>
        /// <param name="value">The date to format using the clock language.</param>
        /// <param name="culture">The culture used to format the date.</param>
        /// <returns>The date formatted with the clock&apos;s language-specific layout.</returns>
        private static string FormatDate(DateTime value, CultureInfo culture)
        {
            string language = culture.TwoLetterISOLanguageName;
            string format = language switch
            {
                "en" => "MMMM d, yyyy",
                "fr" => "d MMMM yyyy",
                "es" => "d 'de' MMMM 'de' yyyy",
                "ja" => "yyyy年M月d日",
                _ => "d. MMMM yyyy"
            };

            return value.ToString(format, culture);
        }


        /// <summary>
        /// Draws one of the lightweight clock styles with its requested font and decorations.
        /// </summary>
        /// <param name="g">The drawing surface used for the operation.</param>
        /// <param name="time">The already formatted time shown by the clock.</param>
        /// <param name="date">The formatted date text to draw.</param>
        /// <param name="scale">The size multiplier applied to the clock&apos;s layout and effects.</param>
        /// <param name="centerX">The horizontal center of the text layout.</param>
        /// <param name="fontName">The font family used for the clock text.</param>
        /// <param name="fontStyle">The font emphasis used for the clock text.</param>
        /// <param name="color">The foreground drawing color.</param>
        /// <param name="divider">Whether to draw a divider below the clock time.</param>
        /// <param name="glow">Whether to draw a glow around the text.</param>
        /// <param name="classic">Whether to use the classic clock presentation.</param>
        private void DrawSimpleStyle(Graphics g, string time, string date, float scale, float centerX, string fontName, FontStyle fontStyle, Color color, bool divider, bool glow, bool classic)
        {
            float timeY = -54f * scale;
            float timeHeight = 190f * scale;
            float timePixels = clockSize * g.DpiY / 72f;
            using FontFamily timeFamily = new(fontName);
            using StringFormat centered = CreateCenteredFormat();
            using GraphicsPath timePath = new();
            timePath.AddString(time, timeFamily, (int)fontStyle, timePixels, new RectangleF(0, timeY, ClientSize.Width, timeHeight), centered);

            if (glow)
            {
                for (int width = 12; width >= 4; width -= 4)
                {
                    using Pen glowPen = new(Color.FromArgb(35, 80, 210, 255), Math.Max(2f, width * scale));
                    glowPen.LineJoin = LineJoin.Round;
                    g.DrawPath(glowPen, timePath);
                }
            }
            else if (!classic)
            {
                using Matrix shadowMatrix = new();
                shadowMatrix.Translate(3f * scale, 5f * scale);
                using GraphicsPath shadowPath = (GraphicsPath)timePath.Clone();
                shadowPath.Transform(shadowMatrix);
                using SolidBrush shadowBrush = new(Color.FromArgb(150, 0, 0, 0));
                g.FillPath(shadowBrush, shadowPath);
            }

            using SolidBrush timeBrush = new(color);
            g.FillPath(timeBrush, timePath);

            float dateHeight = 36f * scale;
            float dateY = ClientSize.Height - dateHeight - 14f * scale;
            float dividerY = dateY - 16f * scale;

            if (divider)
            {
                float lineWidth = (classic ? 480f : 630f) * scale;
                float gap = (classic ? 26f : 18f) * scale;
                using Pen linePen = new(glow ? Color.FromArgb(230, 155, 230, 255) : color, Math.Max(1f, 2f * scale));
                g.DrawLine(linePen, centerX - lineWidth / 2f, dividerY, centerX - gap, dividerY);
                g.DrawLine(linePen, centerX + gap, dividerY, centerX + lineWidth / 2f, dividerY);

                if (classic)
                {
                    using Font ornamentFont = new("Georgia", Math.Max(8f, 15f * scale), FontStyle.Regular, GraphicsUnit.Pixel);
                    using StringFormat ornamentFormat = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    using SolidBrush ornamentBrush = new(color);
                    g.DrawString("◇", ornamentFont, ornamentBrush, new RectangleF(centerX - 22f * scale, dividerY - 15f * scale, 44f * scale, 30f * scale), ornamentFormat);
                }
                else
                {
                    PointF[] diamond = { new(centerX, dividerY - 9f * scale), new(centerX + 9f * scale, dividerY), new(centerX, dividerY + 9f * scale), new(centerX - 9f * scale, dividerY) };
                    using SolidBrush diamondBrush = new(glow ? Color.FromArgb(245, 190, 240, 255) : color);
                    g.FillPolygon(diamondBrush, diamond);
                }
            }

            float datePixels = (classic ? 25f : 27f) * scale * g.DpiY / 72f;
            using FontFamily dateFamily = new(classic ? "Georgia" : "Segoe UI");
            using GraphicsPath datePath = CreateTextPath(date, dateFamily, datePixels, new RectangleF(0, dateY, ClientSize.Width, dateHeight + 10f * scale), centered);
            if (glow)
            {
                using Pen dateGlow = new(Color.FromArgb(55, 80, 210, 255), Math.Max(2f, 4f * scale));
                g.DrawPath(dateGlow, datePath);
            }
            using SolidBrush dateBrush = new(glow ? Color.FromArgb(245, 185, 235, 255) : color);
            g.FillPath(dateBrush, datePath);
        }

        /// <summary>
        /// Draws the main time text with the clock&apos;s layered visual treatment.
        /// </summary>
        /// <param name="g">The drawing surface used for the operation.</param>
        /// <param name="text">The text to display, format, or parse.</param>
        /// <param name="scale">The size multiplier applied to the clock&apos;s layout and effects.</param>
        /// <param name="centerX">The horizontal center of the text layout.</param>
        private void DrawTime(
            Graphics g,
            string text,
            float scale,
            float centerX)
        {
            float y = -62f * scale;
            float height = 205f * scale;
            float fontPixels = clockSize * g.DpiY / 72f;

            using FontFamily family = new("Segoe UI Light");
            using StringFormat format = CreateCenteredFormat();
            using GraphicsPath path = CreateTextPath(
                text,
                family,
                fontPixels,
                new RectangleF(0, y, ClientSize.Width, height),
                format);

            // Rainmeter V6: soft depth shadow (+5,+8).
            using (Matrix shadowMatrix = new())
            {
                shadowMatrix.Translate(5f * scale, 8f * scale);
                using GraphicsPath shadowPath = (GraphicsPath)path.Clone();
                shadowPath.Transform(shadowMatrix);
                using SolidBrush shadowBrush =
                    new(Color.FromArgb(210, 0, 0, 0));
                g.FillPath(shadowBrush, shadowPath);
            }

            // Dark metal edge, corresponding to the second Rainmeter text layer.
            using (Pen edgePen = new(
                       Color.FromArgb(235, 35, 37, 42),
                       Math.Max(1f, 2.2f * scale)))
            {
                edgePen.LineJoin = LineJoin.Round;
                g.DrawPath(edgePen, path);
            }

            RectangleF bounds = path.GetBounds();
            if (bounds.Width > 0 && bounds.Height > 0)
            {
                using LinearGradientBrush chrome =
                    new(bounds, Color.White, Color.Gray, LinearGradientMode.Vertical);

                chrome.InterpolationColors = new ColorBlend
                {
                    Colors = new[]
                    {
                        Color.FromArgb(255, 78, 81, 88),
                        Color.FromArgb(255, 250, 250, 252),
                        Color.FromArgb(255, 172, 176, 184),
                        Color.FromArgb(255, 242, 243, 246),
                        Color.FromArgb(255, 76, 79, 86),
                        Color.FromArgb(255, 228, 230, 235),
                        Color.FromArgb(255, 130, 134, 143),
                        Color.FromArgb(255, 248, 248, 250),
                        Color.FromArgb(255, 88, 91, 98)
                    },
                    Positions = new[]
                    {
                        0.00f, 0.12f, 0.28f, 0.42f, 0.55f,
                        0.68f, 0.82f, 0.93f, 1.00f
                    }
                };

                g.FillPath(chrome, path);
            }

            // Fine light edge from the Rainmeter highlight layer (-1,-1).
            using (Matrix highlightMatrix = new())
            {
                highlightMatrix.Translate(-1f * scale, -1f * scale);
                using GraphicsPath highlightPath = (GraphicsPath)path.Clone();
                highlightPath.Transform(highlightMatrix);
                using Pen highlightPen = new(
                    Color.FromArgb(90, 255, 255, 255),
                    Math.Max(0.8f, 1f * scale));
                highlightPen.LineJoin = LineJoin.Round;
                g.DrawPath(highlightPen, highlightPath);
            }
        }

        /// <summary>
        /// Draws the clock&apos;s scaled horizontal divider.
        /// </summary>
        /// <param name="g">The drawing surface used for the operation.</param>
        /// <param name="scale">The size multiplier applied to the clock&apos;s layout and effects.</param>
        /// <param name="centerX">The horizontal center of the text layout.</param>
        private void DrawDivider(
            Graphics g,
            float scale,
            float centerX)
        {
            float dateY =
                ClientSize.Height - (36f + 14f) * scale;
            float dividerY = dateY - 16f * scale;
            float lineWidth = 630f * scale;
            float gap = 18f * scale;
            float left = centerX - lineWidth / 2f;
            float right = centerX + lineWidth / 2f;

            using Pen shadow = new(
                Color.FromArgb(155, 0, 0, 0),
                Math.Max(1f, 3f * scale));
            g.DrawLine(
                shadow,
                left,
                dividerY + 2f * scale,
                centerX - gap,
                dividerY + 2f * scale);
            g.DrawLine(
                shadow,
                centerX + gap,
                dividerY + 2f * scale,
                right,
                dividerY + 2f * scale);

            using Pen metal = new(
                Color.FromArgb(255, 210, 213, 220),
                Math.Max(1f, 2f * scale));
            g.DrawLine(metal, left, dividerY, centerX - gap, dividerY);
            g.DrawLine(metal, centerX + gap, dividerY, right, dividerY);

            using Pen highlight = new(
                Color.FromArgb(210, 255, 255, 255),
                Math.Max(0.8f, 1f * scale));
            g.DrawLine(
                highlight,
                left,
                dividerY - 1f * scale,
                centerX - gap,
                dividerY - 1f * scale);
            g.DrawLine(
                highlight,
                centerX + gap,
                dividerY - 1f * scale,
                right,
                dividerY - 1f * scale);

            PointF[] shadowDiamond =
            {
                new(centerX + 2f * scale, dividerY - 11f * scale),
                new(centerX + 13f * scale, dividerY + 1f * scale),
                new(centerX + 2f * scale, dividerY + 13f * scale),
                new(centerX - 9f * scale, dividerY + 1f * scale)
            };
            using SolidBrush diamondShadow =
                new(Color.FromArgb(180, 0, 0, 0));
            g.FillPolygon(diamondShadow, shadowDiamond);

            PointF[] diamond =
            {
                new(centerX, dividerY - 12f * scale),
                new(centerX + 12f * scale, dividerY),
                new(centerX, dividerY + 12f * scale),
                new(centerX - 12f * scale, dividerY)
            };

            RectangleF diamondBounds = new(
                centerX - 12f * scale,
                dividerY - 12f * scale,
                24f * scale,
                24f * scale);

            using LinearGradientBrush diamondChrome =
                new(diamondBounds, Color.White, Color.Gray, 45f);
            diamondChrome.InterpolationColors = new ColorBlend
            {
                Colors = new[]
                {
                    Color.FromArgb(255, 70, 73, 80),
                    Color.FromArgb(255, 245, 246, 249),
                    Color.FromArgb(255, 115, 119, 127),
                    Color.FromArgb(255, 232, 234, 239)
                },
                Positions = new[] { 0.0f, 0.32f, 0.62f, 1.0f }
            };

            using Pen diamondBorder = new(
                Color.FromArgb(255, 240, 242, 246),
                Math.Max(1f, scale));
            g.FillPolygon(diamondChrome, diamond);
            g.DrawPolygon(diamondBorder, diamond);
        }

        /// <summary>
        /// Draws the localized date below the clock&apos;s time display.
        /// </summary>
        /// <param name="g">The drawing surface used for the operation.</param>
        /// <param name="text">The text to display, format, or parse.</param>
        /// <param name="scale">The size multiplier applied to the clock&apos;s layout and effects.</param>
        /// <param name="centerX">The horizontal center of the text layout.</param>
        private void DrawDate(
            Graphics g,
            string text,
            float scale,
            float centerX)
        {
            float dateSize = 27f * scale;
            float dateHeight = 36f * scale;
            float dateY = ClientSize.Height - dateHeight - 14f * scale;
            float fontPixels = dateSize * g.DpiY / 72f;

            using FontFamily family = new("Segoe UI");
            using StringFormat format = CreateCenteredFormat();
            using GraphicsPath path = CreateTextPath(
                text,
                family,
                fontPixels,
                new RectangleF(0, dateY, ClientSize.Width, dateHeight + 10f * scale),
                format);

            using (Matrix shadowMatrix = new())
            {
                shadowMatrix.Translate(3f * scale, 4f * scale);
                using GraphicsPath shadowPath = (GraphicsPath)path.Clone();
                shadowPath.Transform(shadowMatrix);
                using SolidBrush shadow = new(Color.FromArgb(200, 0, 0, 0));
                g.FillPath(shadow, shadowPath);
            }

            using Pen edge = new(
                Color.FromArgb(225, 70, 73, 80),
                Math.Max(0.9f, 1.4f * scale));
            edge.LineJoin = LineJoin.Round;
            g.DrawPath(edge, path);

            RectangleF bounds = path.GetBounds();
            if (bounds.Width > 0 && bounds.Height > 0)
            {
                using LinearGradientBrush chrome =
                    new(bounds, Color.White, Color.Gray, LinearGradientMode.Vertical);
                chrome.InterpolationColors = new ColorBlend
                {
                    Colors = new[]
                    {
                        Color.FromArgb(255, 105, 108, 116),
                        Color.FromArgb(255, 248, 248, 250),
                        Color.FromArgb(255, 160, 164, 173),
                        Color.FromArgb(255, 240, 241, 244),
                        Color.FromArgb(255, 100, 103, 111),
                        Color.FromArgb(255, 225, 227, 232)
                    },
                    Positions = new[]
                    {
                        0.00f, 0.20f, 0.48f, 0.66f, 0.83f, 1.00f
                    }
                };
                g.FillPath(chrome, path);
            }
        }

        /// <summary>
        /// Creates the centered text layout used by the clock renderer.
        /// </summary>
        /// <returns>A new centered text format that the caller must dispose.</returns>
        private static StringFormat CreateCenteredFormat()
        {
            return new StringFormat(StringFormat.GenericTypographic)
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Near,
                FormatFlags = StringFormatFlags.NoWrap
            };
        }

        /// <summary>
        /// Builds a vector path for rendering the requested text with the clock effects.
        /// </summary>
        /// <param name="text">The text to display, format, or parse.</param>
        /// <param name="family">The font family used to construct the glyph outlines.</param>
        /// <param name="emSize">The font size in the units used by the drawing operation.</param>
        /// <param name="layout">The text layout rectangle.</param>
        /// <param name="format">The text alignment and layout options.</param>
        /// <returns>A new glyph-outline path that the caller must dispose.</returns>
        private static GraphicsPath CreateTextPath(
            string text,
            FontFamily family,
            float emSize,
            RectangleF layout,
            StringFormat format)
        {
            GraphicsPath path = new();
            path.AddString(
                text,
                family,
                (int)FontStyle.Regular,
                emSize,
                layout,
                format);
            return path;
        }

        /// <summary>
        /// Applies the current clock size to the widget window.
        /// </summary>
        private void SetSize()
        {
            ClientSize = new Size(
                (int)Math.Round(700 * clockSize / 150d),
                (int)Math.Round(260 * clockSize / 150d));
        }

        /// <summary>
        /// Starts an unlocked clock drag and stores its cursor and window origins.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private void BeginDrag(object? sender, MouseEventArgs e)
        {
            if (locked || e.Button != MouseButtons.Left)
            {
                return;
            }

            dragging = true;
            dragMouseStart = Cursor.Position;
            dragFormStart = Location;
        }

        /// <summary>
        /// Moves the clock relative to the stored drag origins.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private void ContinueDrag(object? sender, MouseEventArgs e)
        {
            if (!dragging)
            {
                return;
            }

            Point now = Cursor.Position;
            Location = new Point(
                dragFormStart.X + now.X - dragMouseStart.X,
                dragFormStart.Y + now.Y - dragMouseStart.Y);
        }

        /// <summary>
        /// Ends a clock drag and reports the final window position.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private void EndDrag(object? sender, MouseEventArgs e)
        {
            if (!dragging)
            {
                return;
            }

            dragging = false;
            locationChanged(Location);
            DesktopWidgetNative.KeepOnDesktop(this);
        }

        private bool activitySuspended;
        /// <summary>
        /// Stops periodic clock repaints during automatic suspension and refreshes when resumed.
        /// </summary>
        /// <param name="suspended">True to pause background activity; false to resume it.</param>
        internal void SetActivitySuspended(bool suspended)
        {
            if (activitySuspended == suspended || IsDisposed) return;
            activitySuspended = suspended;
            if (suspended) timer.Stop(); else timer.Start();
            if (!suspended) Invalidate();
        }
        /// <summary>
        /// Releases the resources owned by this clock widget form.
        /// </summary>
        /// <param name="disposing">True when managed resources should be released during explicit disposal.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
