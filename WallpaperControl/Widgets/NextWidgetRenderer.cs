using System.Drawing;
using System.Drawing.Drawing2D;

namespace WallpaperControl
{
    /// <summary>Draws the next-wallpaper button independently of its click and drag behavior.</summary>
    internal static class NextWidgetRenderer
    {
        /// <summary>Draws Minimal, Clean, or Glow, retaining the original geometry and Minimal colors.</summary>
        internal static void Draw(Graphics graphics, Size size, SystemWidgetStyle style, bool hover)
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            RectangleF rect = new(0.5f, 0.5f, size.Width - 1f, size.Height - 1f);
            using GraphicsPath path = WidgetDrawing.RoundedRectangle(rect, 12f);
            var palette = WidgetDrawing.GetPalette(style);
            bool minimal = style == SystemWidgetStyle.Minimal;
            Color fillColor = minimal
                ? (hover ? Color.FromArgb(210, 55, 55, 55) : Color.FromArgb(150, 20, 20, 20))
                : (hover ? Color.FromArgb(Math.Min(255, palette.panel.A + 45), palette.panel) : palette.panel);
            Color borderColor = minimal
                ? (hover ? Color.FromArgb(100, 255, 255, 255) : Color.FromArgb(45, 255, 255, 255))
                : (hover ? Color.FromArgb(230, palette.accent) : palette.border);

            using SolidBrush fill = new(fillColor);
            using Pen border = new(borderColor, 1f);
            graphics.FillPath(fill, path);
            graphics.DrawPath(border, path);

            using Font font = new("Segoe UI Symbol", 22, FontStyle.Regular, GraphicsUnit.Point);
            using StringFormat format = new()
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap
            };
            // Keep the original optical alignment of the arrow.
            RectangleF textBounds = new(0, -1, size.Width, size.Height);
            if (style == SystemWidgetStyle.Glow)
            {
                using SolidBrush glow = new(Color.FromArgb(80, palette.accent));
                foreach (Point offset in new[] { new Point(-2, 0), new Point(2, 0), new Point(0, -2), new Point(0, 2) })
                {
                    RectangleF glowBounds = textBounds;
                    glowBounds.Offset(offset);
                    graphics.DrawString("❯", font, glow, glowBounds, format);
                }
            }
            using SolidBrush text = new(minimal ? Color.FromArgb(230, 235, 235, 235) : palette.title);
            graphics.DrawString("❯", font, text, textBounds, format);
        }
    }
}
