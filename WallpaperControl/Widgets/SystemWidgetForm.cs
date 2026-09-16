using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WallpaperControl
{
    internal sealed class SystemWidgetForm : Form
    {
        private readonly WidgetDragHandler dragHandler;
        private readonly SystemMonitorService monitor = new();
        private readonly System.Windows.Forms.Timer timer;
        private SystemMonitorSnapshot snapshot = new();
        private bool locked;
        private SystemWidgetStyle style;
        private bool showCpu;
        private bool showRam;
        private bool showGpu;
        private bool showVram;
        private bool showNetwork;
        private bool showDrives;
        private bool refreshInProgress;
        private bool disposingWidget;

        private const int WidgetWidth = 330;
        private const int WS_EX_LAYERED = 0x00080000;

        public SystemWidgetForm(
            bool locked,
            int refreshSeconds,
            SystemWidgetStyle style,
            bool showCpu,
            bool showRam,
            bool showGpu,
            bool showVram,
            bool showNetwork,
            bool showDrives,
            Point location,
            Action<Point> locationChanged)
        {

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;

            dragHandler = new WidgetDragHandler(this, () => this.locked, RenderLayeredWindow, locationChanged);

            timer = new System.Windows.Forms.Timer();
            timer.Tick += (_, _) => RefreshSnapshot();

            Apply(locked, refreshSeconds, style, showCpu, showRam, showGpu, showVram, showNetwork, showDrives);
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
            RefreshSnapshot();
            if (!activitySuspended) timer.Start();
        }

        public void Apply(
            bool isLocked,
            int refreshSeconds,
            SystemWidgetStyle newStyle,
            bool cpu,
            bool ram,
            bool gpu,
            bool vram,
            bool network,
            bool drives)
        {
            locked = isLocked;
            style = newStyle;
            showCpu = cpu;
            showRam = ram;
            showGpu = gpu;
            showVram = vram;
            showNetwork = network;
            showDrives = drives;
            timer.Interval = Math.Clamp(refreshSeconds, 1, 5) * 1000;

            Size newSize = new(WidgetWidth, CalculateHeight());
            if (Size != newSize)
            {
                ClientSize = newSize;
            }

            if (Visible && !timer.Enabled && !activitySuspended) timer.Start();
            if (IsHandleCreated && !IsDisposed) RenderLayeredWindow();
        }

        private int CalculateHeight()
        {
            int height = 58;
            if (showCpu) height += 38;
            if (showRam) height += 38;
            if (showGpu) height += 38;
            if (showVram) height += 38;
            if (showNetwork) height += 52;
            if (showDrives) height += 35;
            return Math.Max(108, height + 12);
        }

        private async void RefreshSnapshot()
        {
            if (activitySuspended || refreshInProgress || disposingWidget || IsDisposed)
                return;

            refreshInProgress = true;
            try
            {
                // LibreHardwareMonitor can take long enough to stall the WinForms UI thread.
                // Sample it on a worker thread so wallpaper transition timers stay smooth.
                SystemMonitorSnapshot nextSnapshot = await Task.Run(monitor.Sample);

                if (activitySuspended || disposingWidget || IsDisposed || !IsHandleCreated)
                    return;

                snapshot = nextSnapshot;
                RenderLayeredWindow();
            }
            catch (ObjectDisposedException) when (disposingWidget || IsDisposed)
            {
                // The widget was closed while a background sample was finishing.
            }
            catch (Exception ex)
            {
                AppLogger.Warning("System widget could not read hardware data.", ex);
            }
            finally
            {
                refreshInProgress = false;
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

                (Color panelColor, Color borderColor, Color titleColor, Color textColor, Color mutedColor, Color accentColor, Color barBackColor) = GetPalette();

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
                using Font rowFont = new("Segoe UI", 9.5f, FontStyle.Regular);
                using Font smallFont = new("Segoe UI", 8.5f, FontStyle.Regular);
                using SolidBrush titleBrush = new(titleColor);
                using SolidBrush textBrush = new(textColor);
                using SolidBrush mutedBrush = new(mutedColor);
                using SolidBrush accentBrush = new(accentColor);
                using SolidBrush barBackBrush = new(barBackColor);

                if (style == SystemWidgetStyle.Glow)
                {
                    WidgetDrawing.DrawGlowText(g, "SYSTEM", titleFont, 16, 13, accentColor);
                }
                else
                {
                    g.DrawString("SYSTEM", titleFont, titleBrush, 16, 13);
                }

                if (style != SystemWidgetStyle.Minimal)
                {
                    g.FillRectangle(accentBrush, 16, 40, Width - 32, style == SystemWidgetStyle.Glow ? 2 : 1);
                }

                int y = 55;
                if (showCpu)
                {
                    DrawPerformanceRow(g, rowFont, smallFont, textBrush, mutedBrush, accentBrush, barBackBrush,
                        "CPU", snapshot.CpuLoad, FormatPercent(snapshot.CpuLoad), FormatTemperature(snapshot.CpuTemperature), ref y);
                }
                if (showRam)
                {
                    DrawPerformanceRow(g, rowFont, smallFont, textBrush, mutedBrush, accentBrush, barBackBrush,
                        "RAM", snapshot.MemoryLoad, FormatPercent(snapshot.MemoryLoad), FormatMemory(snapshot.MemoryUsedGb, snapshot.MemoryTotalGb), ref y);
                }
                if (showGpu)
                {
                    DrawPerformanceRow(g, rowFont, smallFont, textBrush, mutedBrush, accentBrush, barBackBrush,
                        "GPU", snapshot.GpuLoad, FormatPercent(snapshot.GpuLoad), FormatTemperature(snapshot.GpuTemperature), ref y);
                }
                if (showVram)
                {
                    float? vramPercent = snapshot.VramUsedGb.HasValue && snapshot.VramTotalGb > 0
                        ? snapshot.VramUsedGb.Value / snapshot.VramTotalGb.Value * 100f
                        : null;
                    DrawPerformanceRow(g, rowFont, smallFont, textBrush, mutedBrush, accentBrush, barBackBrush,
                        "VRAM", vramPercent, FormatMemoryPercent(snapshot.VramUsedGb, snapshot.VramTotalGb), FormatMemory(snapshot.VramUsedGb, snapshot.VramTotalGb), ref y);
                }

                if (showNetwork)
                {
                    y += 2;
                    DrawTextPair(g, rowFont, textBrush, mutedBrush, "NET ↓", FormatRate(snapshot.DownloadBytesPerSecond), ref y);
                    DrawTextPair(g, rowFont, textBrush, mutedBrush, "NET ↑", FormatRate(snapshot.UploadBytesPerSecond), ref y);
                    y += 3;
                }

                if (showDrives)
                {
                    DriveSnapshot? systemDrive = snapshot.Drives.FirstOrDefault(d => d.Name.StartsWith("C:", StringComparison.OrdinalIgnoreCase)) ?? snapshot.Drives.FirstOrDefault();
                    if (systemDrive != null)
                    {
                        double used = systemDrive.TotalBytes > 0 ? (systemDrive.TotalBytes - systemDrive.FreeBytes) * 100d / systemDrive.TotalBytes : 0;
                        string driveText = $"{BytesToGb(systemDrive.FreeBytes):0} GB frei / {BytesToGb(systemDrive.TotalBytes):0} GB";
                        DrawPerformanceRow(g, rowFont, smallFont, textBrush, mutedBrush, accentBrush, barBackBrush,
                            systemDrive.Name.TrimEnd('\\'), (float)used, $"{used:0}%", driveText, ref y);
                    }
                }
            }

            LayeredWidgetBitmap.Update(Handle, Location, bitmap);
        }

        private (Color panel, Color border, Color title, Color text, Color muted, Color accent, Color barBack) GetPalette()
        {
            return style switch
            {
                SystemWidgetStyle.Minimal => (
                    Color.FromArgb(112, 12, 18, 24),
                    Color.FromArgb(48, 255, 255, 255),
                    Color.FromArgb(245, 241, 246),
                    Color.FromArgb(238, 235, 241, 246),
                    Color.FromArgb(185, 174, 188, 199),
                    Color.FromArgb(185, 174, 188, 199),
                    Color.FromArgb(70, 255, 255, 255)),
                SystemWidgetStyle.Clean => (
                    Color.FromArgb(178, 17, 24, 31),
                    Color.FromArgb(105, 92, 184, 224),
                    Color.FromArgb(245, 241, 246),
                    Color.FromArgb(238, 235, 241, 246),
                    Color.FromArgb(200, 174, 188, 199),
                    Color.FromArgb(210, 92, 184, 224),
                    Color.FromArgb(80, 92, 184, 224)),
                _ => (
                    Color.FromArgb(166, 10, 18, 25),
                    Color.FromArgb(175, 82, 201, 255),
                    Color.FromArgb(255, 228, 249, 255),
                    Color.FromArgb(245, 239, 248, 252),
                    Color.FromArgb(205, 177, 211, 226),
                    Color.FromArgb(235, 88, 211, 255),
                    Color.FromArgb(88, 88, 211, 255))
            };
        }

        private void DrawPerformanceRow(
            Graphics g,
            Font rowFont,
            Font smallFont,
            Brush textBrush,
            Brush mutedBrush,
            Brush accentBrush,
            Brush barBackBrush,
            string label,
            float? percent,
            string value,
            string extra,
            ref int y)
        {
            g.DrawString(label, rowFont, textBrush, 16, y);
            g.DrawString(value, rowFont, textBrush, 88, y);
            g.DrawString(extra, smallFont, mutedBrush, 170, y + 1);

            int barX = 88;
            int barY = y + 24;
            int barWidth = Width - barX - 18;
            int barHeight = style == SystemWidgetStyle.Minimal ? 2 : 4;
            using GraphicsPath backPath = WidgetDrawing.RoundedRectangle(new RectangleF(barX, barY, barWidth, barHeight), barHeight / 2f);
            g.FillPath(barBackBrush, backPath);

            if (percent.HasValue)
            {
                float clamped = Math.Clamp(percent.Value, 0f, 100f);
                float fillWidth = barWidth * clamped / 100f;
                if (fillWidth >= 1f)
                {
                    using GraphicsPath fillPath = WidgetDrawing.RoundedRectangle(new RectangleF(barX, barY, fillWidth, barHeight), Math.Min(barHeight / 2f, fillWidth / 2f));
                    g.FillPath(accentBrush, fillPath);
                }
            }

            y += 38;
        }

        private static void DrawTextPair(Graphics g, Font font, Brush textBrush, Brush mutedBrush, string label, string value, ref int y)
        {
            g.DrawString(label, font, textBrush, 16, y);
            g.DrawString(value, font, mutedBrush, 88, y);
            y += 23;
        }

        private static string FormatPercent(float? value) => value.HasValue ? $"{value.Value:0}%" : "--%";
        private static string FormatTemperature(float? value) => value.HasValue ? $"{value.Value:0} °C" : "-- °C";
        private static string FormatMemory(float? used, float? total) => used.HasValue && total.HasValue ? $"{used.Value:0.0}/{total.Value:0.0} GB" : "--";
        private static string FormatMemoryPercent(float? used, float? total) => used.HasValue && total > 0 ? $"{used.Value / total.Value * 100f:0}%" : "--%";
        private static double BytesToGb(long bytes) => bytes / 1024d / 1024d / 1024d;

        private static string FormatRate(double bytesPerSecond)
        {
            if (bytesPerSecond >= 1024d * 1024d) return $"{bytesPerSecond / 1024d / 1024d:0.0} MB/s";
            if (bytesPerSecond >= 1024d) return $"{bytesPerSecond / 1024d:0.0} KB/s";
            return $"{bytesPerSecond:0} B/s";
        }

        private bool activitySuspended;
        internal void SetActivitySuspended(bool suspended)
        {
            if (activitySuspended == suspended || IsDisposed) return;
            activitySuspended = suspended;
            if (suspended) timer.Stop(); else timer.Start();
            if (!suspended) RefreshSnapshot();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                dragHandler.Dispose();
                disposingWidget = true;
                timer.Stop();
                timer.Dispose();
                monitor.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
