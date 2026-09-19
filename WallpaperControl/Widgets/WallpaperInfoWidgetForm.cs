using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;

namespace WallpaperControl
{
    /// <summary>Event-driven layered desktop bar using the shared widget rendering and drag infrastructure.</summary>
    internal sealed class WallpaperInfoWidgetForm : Form
    {
        private readonly WidgetDragHandler dragHandler;
        private WidgetSettings settings;
        private WallpaperInfoSnapshot data;
        private bool suspended;
        private bool rendering;

        internal WallpaperInfoWidgetForm(WidgetSettings settings, Action<Point> locationChanged)
        {
            this.settings = settings.Clone();
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(420, settings.WallpaperInfoShowAdvanced ? 58 : 34);
            Location = WidgetSettings.EnsureVisible(settings.WallpaperInfoLocation, Size);
            dragHandler = new WidgetDragHandler(this, () => this.settings.WallpaperInfoLocked, RenderLayeredWindow, locationChanged);
        }

        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080 | 0x08000000 | 0x00080000;
                return cp;
            }
        }

        internal void Apply(WidgetSettings value)
        {
            settings = value.Clone();
            RenderLayeredWindow();
        }

        internal void SetData(WallpaperInfoSnapshot value)
        {
            if (data == value) return;
            data = value;
            RenderLayeredWindow();
        }

        internal void SetActivitySuspended(bool value)
        {
            if (suspended == value) return;
            suspended = value;
            if (!value) RenderLayeredWindow();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            RenderLayeredWindow();
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            RenderLayeredWindow();
        }

        protected override void WndProc(ref Message m)
        {
            if (DesktopWidgetNative.HandleMouseActivation(ref m)) return;
            base.WndProc(ref m);
            if (m.Msg is 0x007E or 0x001A) RenderLayeredWindow();
        }

        private void RenderLayeredWindow()
        {
            if (suspended || rendering || !IsHandleCreated || IsDisposed || Disposing) return;
            rendering = true;
            try
            {
                Rectangle area = Screen.FromControl(this).WorkingArea;
                using Bitmap bitmap = RenderBitmap(DeviceDpi, area.Width);
                ClientSize = bitmap.Size;
                if (!Capture)
                    Location = new Point(Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width)),
                        Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height)));
                LayeredWidgetBitmap.Update(Handle, Location, bitmap);
            }
            finally { rendering = false; }
        }

        /// <summary>Measures in logical pixels, then renders directly into a bounded DPI-scaled bitmap.</summary>
        internal Bitmap RenderBitmap(int dpi = 96, int widthLimit = 1920)
        {
            float scale = Math.Clamp(dpi / 96f, 1f, 8f);
            string language = settings.ClockLanguageCode;
            CultureInfo culture = CultureInfo.GetCultureInfo(language is "de" or "en" or "fr" or "es" or "ja" ? language : "de");
            string name = WallpaperInfoSnapshot.DisplayName(data.Path, settings.WallpaperInfoHiddenSuffix, settings.WallpaperInfoShowExtension);
            if (name.Length == 0) name = Localization.Get("CurrentWallpaperEmpty", language);
            string statistics = string.Format(culture, "{0}: {1:N0}   │   {2}: {3:N0}",
                Localization.Get("WallpaperInfoViews", language), data.Views,
                Localization.Get("WallpaperInfoCount", language), data.WallpaperCount);
            string advanced = settings.WallpaperInfoShowAdvanced
                ? string.Format(culture, "{0}: {1:N2}   │   {2}: {3:N0}   │   {4}: {5:N0}×",
                    Localization.Get("WallpaperInfoAverage", language), data.Summary.AverageViews,
                    Localization.Get("WallpaperInfoTotal", language), data.Summary.TotalViews,
                    Localization.Get("WallpaperInfoRecord", language), data.Summary.RecordViews)
                : string.Empty;
            using Font font = new("Segoe UI", Math.Clamp(settings.WallpaperInfoFontSize, 10, 24), FontStyle.Regular, GraphicsUnit.Pixel);
            using StringFormat format = new()
            {
                FormatFlags = StringFormatFlags.NoWrap,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter
            };
            float nameWidth, statsWidth, advancedWidth;
            float rowHeight;
            using (Bitmap measure = new(1, 1))
            {
                measure.SetResolution(96, 96);
                using Graphics g = Graphics.FromImage(measure);
                rowHeight = Math.Max(24, (float)Math.Ceiling(font.GetHeight(g)) + 6);
                nameWidth = g.MeasureString(name, font, 100000, format).Width;
                statsWidth = g.MeasureString(statistics, font, 100000, format).Width;
                advancedWidth = advanced.Length == 0 ? 0 : g.MeasureString(advanced, font, 100000, format).Width;
            }
            // Reserve statistics first; only the name is normally shortened. The second row can
            // increase width only when it cannot fit, and shares the same absolute width ceiling.
            float maximum = Math.Max(1, Math.Min(900 * Math.Max(1, font.Size / 13f), widthLimit / scale));
            float width = Math.Min(maximum, Math.Max(420, Math.Max(Math.Min(nameWidth, 420) + statsWidth + 54, advancedWidth + 28)));
            float height = 10 + rowHeight * (settings.WallpaperInfoShowAdvanced ? 2 : 1);
            Bitmap bitmap = new(Math.Max(1, (int)Math.Ceiling(width * scale)), (int)Math.Ceiling(height * scale), PixelFormat.Format32bppPArgb);
            bitmap.SetResolution(96, 96);
            try
            {
                using Graphics g = Graphics.FromImage(bitmap);
                g.Clear(Color.Transparent);
                g.ScaleTransform(scale, scale);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                var palette = WidgetDrawing.GetPalette(settings.WallpaperInfoStyle);
                using GraphicsPath panel = WidgetDrawing.RoundedRectangle(new RectangleF(1, 1, width - 2, height - 2), 8);
                using SolidBrush fill = new(palette.panel);
                using Pen border = new(palette.border);
                g.FillPath(fill, panel);
                if (settings.WallpaperInfoStyle == SystemWidgetStyle.Glow)
                {
                    using Pen glow = new(Color.FromArgb(48, palette.accent), 3);
                    g.DrawPath(glow, panel);
                }
                g.DrawPath(border, panel);
                using SolidBrush text = new(palette.text);
                using SolidBrush title = new(palette.title);
                using SolidBrush muted = new(palette.muted);
                float statsX = Math.Max(14, width - 14 - statsWidth);
                g.DrawString(name, font, title, new RectangleF(14, 5, Math.Max(0, statsX - 40), rowHeight), format);
                if (statsX > 40)
                {
                    using Pen separator = new(palette.muted);
                    g.DrawLine(separator, statsX - 13, 10, statsX - 13, rowHeight);
                }
                g.DrawString(statistics, font, text, new RectangleF(statsX, 5, Math.Max(0, width - 14 - statsX), rowHeight), format);
                if (settings.WallpaperInfoShowAdvanced)
                    g.DrawString(advanced, font, muted, new RectangleF(14, 5 + rowHeight, Math.Max(0, width - 28), rowHeight), format);
                return bitmap;
            }
            catch { bitmap.Dispose(); throw; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) dragHandler?.Dispose();
            base.Dispose(disposing);
        }
    }
}
