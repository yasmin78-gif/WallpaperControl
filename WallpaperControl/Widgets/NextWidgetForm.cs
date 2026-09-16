using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace WallpaperControl
{
    internal sealed class NextWidgetForm : Form
    {
        private readonly Action next;
        private readonly Action<Point> locationChanged;
        private readonly ToolTip toolTip;
        private bool locked;
        private SystemWidgetStyle style;
        private bool hover;
        private bool dragging;
        private bool moved;
        private Point dragMouseStart;
        private Point dragFormStart;
        private const int WS_EX_LAYERED = 0x00080000;

        public NextWidgetForm(
            bool locked,
            SystemWidgetStyle style,
            string languageCode,
            Point location,
            Action next,
            Action<Point> locationChanged)
        {
            this.locked = locked;
            this.style = Enum.IsDefined(style) ? style : SystemWidgetStyle.Minimal;
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

        /// <summary>Applies preview or saved preferences and redraws the selected style immediately.</summary>
        public void Apply(bool isLocked, SystemWidgetStyle newStyle, string languageCode)
        {
            locked = isLocked;
            style = Enum.IsDefined(newStyle) ? newStyle : SystemWidgetStyle.Minimal;
            UpdateToolTip(languageCode);
            RenderLayeredWindow();
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

        /// <summary>Renders the current style and hover state without altering click or drag handling.</summary>
        private void RenderLayeredWindow()
        {
            if (!IsHandleCreated || IsDisposed) return;

            using Bitmap bitmap = new(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
                NextWidgetRenderer.Draw(graphics, ClientSize, style, hover);

            LayeredWidgetBitmap.Update(Handle, Location, bitmap);
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
    }
}
