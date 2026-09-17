using System.Drawing;
using System.Drawing.Drawing2D;

namespace WallpaperControl
{
    internal static class WidgetDrawing
    {
        /// <summary>
        /// Returns the shared weather/calendar colors for a widget style.
        /// </summary>
        /// <param name="style">The visual style used to render the widget.</param>
        /// <returns>The shared panel, border, title, text, muted, and accent colors for the style.</returns>
        internal static (Color panel, Color border, Color title, Color text, Color muted, Color accent) GetPalette(SystemWidgetStyle style)
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

        /// <summary>
        /// Draws the shared text glow without retaining GDI resources.
        /// </summary>
        /// <param name="g">The drawing surface used for the operation.</param>
        /// <param name="text">The text to display, format, or parse.</param>
        /// <param name="font">The font used to draw the text.</param>
        /// <param name="x">The horizontal coordinate.</param>
        /// <param name="y">The vertical coordinate.</param>
        /// <param name="color">The foreground drawing color.</param>
        internal static void DrawGlowText(Graphics g, string text, Font font, float x, float y, Color color)
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

        /// <summary>
        /// Builds a rounded path; callers retain their existing policy for empty bounds.
        /// </summary>
        /// <param name="rect">The bounds of the rounded shape.</param>
        /// <param name="radius">The requested corner radius in drawing units.</param>
        /// <param name="skipEmptyBounds">True to return an empty path for nonpositive bounds; false to retain the calendar behavior.</param>
        /// <returns>A new rounded path that the caller must dispose, possibly empty under the requested policy.</returns>
        internal static GraphicsPath RoundedRectangle(RectangleF rect, float radius, bool skipEmptyBounds = true)
        {
            if (skipEmptyBounds && (rect.Width <= 0 || rect.Height <= 0))
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

    }
}
