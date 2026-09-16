using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Settings dialog ClockPreview members. See README.md in this directory for the code map.
    internal sealed partial class SettingsForm
    {
        /// <summary>
        /// Renders a clock-style sample within the settings dialog.
        /// </summary>
        private sealed class ClockStyleCard : Control
        {
            [Browsable(false)]
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public ClockWidgetStyle Style { get; set; }
            [Browsable(false)]
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public string Caption { get; set; } = "";
            [Browsable(false)]
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public bool Selected { get; set; }
            [Browsable(false)]
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public bool ShowSeconds { get; set; }

            /// <summary>Enables flicker-free rendering and the style-selection cursor.</summary>
            public ClockStyleCard()
            {
                Cursor = Cursors.Hand;
                DoubleBuffered = true;
            }

            /// <summary>Paints the clock sample with its current style and selection appearance.</summary>
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Color bg = Color.FromArgb(28, 31, 36);
                Color border = Selected ? Color.FromArgb(55, 145, 255) : Color.FromArgb(75, 80, 88);
                using SolidBrush b = new(bg);
                using Pen p = new(border, Selected ? 3f : 1f);
                Rectangle r = new(1, 1, Width - 3, Height - 3);
                g.FillRectangle(b, r);
                g.DrawRectangle(p, r);
                DrawMiniClock(g, new Rectangle(6, 5, Width - 12, 58), Style, ShowSeconds);
                using Font f = new("Segoe UI", 8.5f, Selected ? FontStyle.Bold : FontStyle.Regular);
                using SolidBrush tb = new(Color.FromArgb(235, 235, 235));
                using StringFormat sf = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
                g.DrawString(Caption, f, tb, new RectangleF(5, 65, Width - 10, 22), sf);
            }
        }

        /// <summary>
        /// Renders a clock-style sample within the settings dialog.
        /// </summary>
        private sealed class ClockSettingsPreview : Control
        {
            [Browsable(false)]
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public ClockWidgetStyle Style { get; set; } = ClockWidgetStyle.Chrome;
            [Browsable(false)]
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public bool ShowSeconds { get; set; }
            [Browsable(false)]
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public string LanguageCode { get; set; } = "de";

            /// <summary>Enables flicker-free rendering of the enlarged clock sample.</summary>
            public ClockSettingsPreview() { DoubleBuffered = true; }

            /// <summary>Paints the clock sample with its current style and selection appearance.</summary>
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using System.Drawing.Drawing2D.LinearGradientBrush bg = new(ClientRectangle, Color.FromArgb(12, 24, 34), Color.FromArgb(25, 20, 18), 0f);
                g.FillRectangle(bg, ClientRectangle);
                using Pen border = new(Color.FromArgb(80, 95, 110));
                g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
                DrawMiniClock(g, new Rectangle(80, 12, Width - 160, Height - 24), Style, ShowSeconds, true);
            }
        }

        /// <summary>
        /// Draws the clock style sample used by the selection cards and enlarged preview.
        /// </summary>
        private static void DrawMiniClock(Graphics g, Rectangle bounds, ClockWidgetStyle style, bool seconds, bool large = false)
        {
            string time = DateTime.Now.ToString(seconds ? "HH:mm:ss" : "HH:mm");
            string font = style == ClockWidgetStyle.Classic ? "Georgia" : "Segoe UI";
            FontStyle fs = style == ClockWidgetStyle.Clean ? FontStyle.Bold : FontStyle.Regular;
            float timeSize = large ? 54f : 23f;
            Color c = style == ClockWidgetStyle.Glow ? Color.FromArgb(205, 245, 255) : Color.FromArgb(238, 240, 243);
            using Font tf = new(font, timeSize, fs, GraphicsUnit.Pixel);
            using StringFormat sf = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            Rectangle timeRect = new(bounds.X, bounds.Y, bounds.Width, (int)(bounds.Height * .58));
            if (style == ClockWidgetStyle.Glow)
            {
                using SolidBrush glow = new(Color.FromArgb(80, 90, 210, 255));
                Rectangle gr = timeRect; gr.Offset(1, 1);
                g.DrawString(time, tf, glow, gr, sf);
            }
            else if (style == ClockWidgetStyle.Chrome)
            {
                using SolidBrush shadow = new(Color.FromArgb(170, 0, 0, 0));
                Rectangle sr = timeRect; sr.Offset(2, 3);
                g.DrawString(time, tf, shadow, sr, sf);
            }
            using SolidBrush tb = new(c);
            g.DrawString(time, tf, tb, timeRect, sf);

            int y = bounds.Y + (int)(bounds.Height * .62);
            if (style != ClockWidgetStyle.Clean)
            {
                using Pen lp = new(style == ClockWidgetStyle.Glow ? Color.FromArgb(160, 225, 255) : Color.FromArgb(210, 215, 220), large ? 2f : 1f);
                int gap = large ? 14 : 6;
                g.DrawLine(lp, bounds.X + bounds.Width / 8, y, bounds.X + bounds.Width / 2 - gap, y);
                g.DrawLine(lp, bounds.X + bounds.Width / 2 + gap, y, bounds.Right - bounds.Width / 8, y);
                if (style != ClockWidgetStyle.Classic)
                {
                    Point[] d = { new(bounds.X + bounds.Width / 2, y - 5), new(bounds.X + bounds.Width / 2 + 5, y), new(bounds.X + bounds.Width / 2, y + 5), new(bounds.X + bounds.Width / 2 - 5, y) };
                    using SolidBrush db = new(c); g.FillPolygon(db, d);
                }
            }
            using Font df = new(style == ClockWidgetStyle.Classic ? "Georgia" : "Segoe UI", large ? 18f : 8f, FontStyle.Regular, GraphicsUnit.Pixel);
            Rectangle dateRect = new(bounds.X, y + (large ? 8 : 3), bounds.Width, large ? 28 : 14);
            using SolidBrush dateBrush = new(c);
            g.DrawString("13. September 2026", df, dateBrush, dateRect, sf);
        }
    }
}
