using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WallpaperControl
{
    internal sealed class NextWidgetForm : Form
    {
        private readonly Action next;
        private readonly Action<Point> locationChanged;
        private readonly ToolTip toolTip;
        private bool locked;
        private bool hover;
        private bool dragging;
        private bool moved;
        private Point dragMouseStart;
        private Point dragFormStart;
        private const int WS_EX_LAYERED = 0x00080000;
        private const byte AC_SRC_OVER = 0x00;
        private const byte AC_SRC_ALPHA = 0x01;
        private const int ULW_ALPHA = 0x00000002;

        public NextWidgetForm(
            bool locked,
            string languageCode,
            Point location,
            Action next,
            Action<Point> locationChanged)
        {
            this.locked = locked;
            this.next = next;
            this.locationChanged = locationChanged;

            toolTip = new ToolTip
            {
                AutoPopDelay = 5000,
                InitialDelay = 500,
                ReshowDelay = 100,
                ShowAlways = true
            };

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(52, 52);
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            Location = WidgetSettings.EnsureVisible(location, Size);
            UpdateToolTip(languageCode);

            MouseEnter += (_, _) =>
            {
                hover = true;
                RenderLayeredWindow();
            };

            MouseLeave += (_, _) =>
            {
                hover = false;
                RenderLayeredWindow();
            };

            MouseDown += BeginPointer;
            MouseMove += ContinuePointer;
            MouseUp += EndPointer;
        }

        protected override bool ShowWithoutActivation => true;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            RenderLayeredWindow();
        }

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
                cp.ExStyle |=
                    WS_EX_TOOLWINDOW |
                    WS_EX_NOACTIVATE |
                    WS_EX_LAYERED;
                return cp;
            }
        }

        public void Apply(bool isLocked, string languageCode)
        {
            locked = isLocked;
            UpdateToolTip(languageCode);
        }

        private void UpdateToolTip(string languageCode)
        {
            toolTip.SetToolTip(
                this,
                Localization.Get(
                    "NextWidgetToolTip",
                    languageCode));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                toolTip.Dispose();
            }

            base.Dispose(disposing);
        }

        private void RenderLayeredWindow()
        {
            if (!IsHandleCreated || IsDisposed)
            {
                return;
            }

            using Bitmap bitmap =
                new Bitmap(
                    ClientSize.Width,
                    ClientSize.Height,
                    PixelFormat.Format32bppPArgb);

            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.CompositingMode = CompositingMode.SourceOver;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.TextRenderingHint =
                    System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                // Same geometry and opacity as the Rainmeter button.
                RectangleF rect = new RectangleF(0.5f, 0.5f, 51.0f, 51.0f);
                using GraphicsPath path = RoundedRectangle(rect, 12.0f);

                Color fillColor = hover
                    ? Color.FromArgb(210, 55, 55, 55)
                    : Color.FromArgb(150, 20, 20, 20);

                Color borderColor = hover
                    ? Color.FromArgb(100, 255, 255, 255)
                    : Color.FromArgb(45, 255, 255, 255);

                using SolidBrush fill = new SolidBrush(fillColor);
                using Pen border = new Pen(borderColor, 1.0f);

                graphics.FillPath(fill, path);
                graphics.DrawPath(border, path);

                using Font font =
                    new Font(
                        "Segoe UI Symbol",
                        22,
                        FontStyle.Regular,
                        GraphicsUnit.Point);

                using SolidBrush text =
                    new SolidBrush(
                        Color.FromArgb(
                            230,
                            235,
                            235,
                            235));

                using StringFormat format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                    FormatFlags = StringFormatFlags.NoWrap
                };

                // Rainmeter's CenterCenter sits visually a touch higher than
                // GDI+ DrawString's mathematical centre.
                RectangleF textBounds =
                    new RectangleF(
                        0,
                        -1,
                        ClientSize.Width,
                        ClientSize.Height);

                graphics.DrawString(
                    "❯",
                    font,
                    text,
                    textBounds,
                    format);
            }

            UpdateLayeredBitmap(bitmap);
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

                NativePoint source = new NativePoint(0, 0);
                NativePoint destination =
                    new NativePoint(Left, Top);
                NativeSize size =
                    new NativeSize(bitmap.Width, bitmap.Height);

                BlendFunction blend = new BlendFunction
                {
                    BlendOp = AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = AC_SRC_ALPHA
                };

                UpdateLayeredWindow(
                    Handle,
                    screenDc,
                    ref destination,
                    ref size,
                    memoryDc,
                    ref source,
                    0,
                    ref blend,
                    ULW_ALPHA);
            }
            finally
            {
                if (oldBitmap != IntPtr.Zero)
                {
                    SelectObject(memoryDc, oldBitmap);
                }

                if (bitmapHandle != IntPtr.Zero)
                {
                    DeleteObject(bitmapHandle);
                }

                if (memoryDc != IntPtr.Zero)
                {
                    DeleteDC(memoryDc);
                }

                if (screenDc != IntPtr.Zero)
                {
                    ReleaseDC(IntPtr.Zero, screenDc);
                }
            }
        }

        private static GraphicsPath RoundedRectangle(
            RectangleF rectangle,
            float radius)
        {
            float diameter = radius * 2.0f;
            GraphicsPath path = new GraphicsPath();

            path.AddArc(
                rectangle.Left,
                rectangle.Top,
                diameter,
                diameter,
                180,
                90);
            path.AddArc(
                rectangle.Right - diameter,
                rectangle.Top,
                diameter,
                diameter,
                270,
                90);
            path.AddArc(
                rectangle.Right - diameter,
                rectangle.Bottom - diameter,
                diameter,
                diameter,
                0,
                90);
            path.AddArc(
                rectangle.Left,
                rectangle.Bottom - diameter,
                diameter,
                diameter,
                90,
                90);
            path.CloseFigure();

            return path;
        }

        private void BeginPointer(
            object? sender,
            MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            dragging = !locked;
            moved = false;
            dragMouseStart = Cursor.Position;
            dragFormStart = Location;
        }

        private void ContinuePointer(
            object? sender,
            MouseEventArgs e)
        {
            if (!dragging)
            {
                return;
            }

            Point now = Cursor.Position;
            int dx = now.X - dragMouseStart.X;
            int dy = now.Y - dragMouseStart.Y;

            if (Math.Abs(dx) + Math.Abs(dy) > 4)
            {
                moved = true;
            }

            if (moved)
            {
                Location =
                    new Point(
                        dragFormStart.X + dx,
                        dragFormStart.Y + dy);
            }
        }

        private void EndPointer(
            object? sender,
            MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            if (dragging && moved)
            {
                locationChanged(Location);
            }
            else
            {
                next();
            }

            dragging = false;
            DesktopWidgetNative.KeepOnDesktop(this);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;

            public NativePoint(int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSize
        {
            public int Width;
            public int Height;

            public NativeSize(int width, int height)
            {
                Width = width;
                Height = height;
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BlendFunction
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hDc);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteDC(IntPtr hDc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(
            IntPtr hDc,
            IntPtr hObject);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UpdateLayeredWindow(
            IntPtr hWnd,
            IntPtr hDcDestination,
            ref NativePoint destination,
            ref NativeSize size,
            IntPtr hDcSource,
            ref NativePoint source,
            int colorKey,
            ref BlendFunction blend,
            int flags);
    }
}
