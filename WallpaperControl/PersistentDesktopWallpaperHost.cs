using System.IO;

using System.Runtime.InteropServices;

namespace WallpaperControl
{
    internal sealed class PersistentDesktopWallpaperHost : Form
    {
        private const uint WM_SPAWN_WORKER = 0x052C;
        private const int WS_CHILD = 0x40000000;
        private const int WS_CLIPSIBLINGS = 0x04000000;
        private const int GWL_STYLE = -16;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int GWL_EXSTYLE = -20;
        private const uint LWA_ALPHA = 0x00000002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_FRAMECHANGED = 0x0020;

        private Bitmap? currentFrame;
        private Bitmap? nextFrame;
        private readonly System.Windows.Forms.Timer animationTimer;
        private readonly System.Diagnostics.Stopwatch stopwatch = new();
        private TaskCompletionSource<bool>? completionSource;
        private int durationMilliseconds;
        private double progress = 1.0;

        public string? CurrentWallpaperPath { get; private set; }

        public PersistentDesktopWallpaperHost()
        {
            Screen screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];

            StartPosition = FormStartPosition.Manual;
            Bounds = screen.Bounds;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = false;

            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer,
                true);

            animationTimer = new System.Windows.Forms.Timer { Interval = 15 };
            animationTimer.Tick += AnimationTimer_Tick;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |=
                    WS_EX_LAYERED |
                    WS_EX_TOOLWINDOW |
                    WS_EX_NOACTIVATE;
                return cp;
            }
        }

        public bool Initialize(string wallpaperPath)
        {
            if (!File.Exists(wallpaperPath))
            {
                return false;
            }

            // First create and place the EMPTY host in its final desktop
            // position. SetParent/SetWindowPos can change the effective client
            // geometry, so do not create the wallpaper bitmap before this.
            if (!TryAttachToRaisedDesktop())
            {
                return false;
            }

            Show();

            Screen screen =
                Screen.PrimaryScreen
                ?? Screen.AllScreens[0];

            Rectangle finalBounds =
                screen.Bounds;

            Bounds =
                new Rectangle(
                    0,
                    0,
                    finalBounds.Width,
                    finalBounds.Height);

            SetWindowPos(
                Handle,
                FindWindowEx(
                    FindWindow("Progman", null),
                    IntPtr.Zero,
                    "SHELLDLL_DefView",
                    null),
                0,
                0,
                finalBounds.Width,
                finalBounds.Height,
                SWP_NOACTIVATE |
                SWP_SHOWWINDOW |
                SWP_FRAMECHANGED);

            // Force WinForms to observe the final child-window dimensions
            // before allocating the 3440x1440 render surface.
            PerformLayout();
            Update();

            ReplaceBitmap(
                ref currentFrame,
                LoadFrame(
                    wallpaperPath,
                    new Size(
                        finalBounds.Width,
                        finalBounds.Height)));

            CurrentWallpaperPath =
                wallpaperPath;

            progress = 1.0;

            Invalidate();
            Update();

            return true;
        }

        public Task WipeToAsync(
            string nextWallpaperPath,
            int milliseconds,
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(nextWallpaperPath))
            {
                throw new FileNotFoundException(
                    "Wallpaper file not found.",
                    nextWallpaperPath);
            }

            ReplaceBitmap(ref nextFrame, LoadFrame(nextWallpaperPath));
            durationMilliseconds = Math.Max(1, milliseconds);
            progress = 0.0;

            completionSource =
                new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() =>
                {
                    if (!IsDisposed && IsHandleCreated)
                    {
                        BeginInvoke(new Action(() =>
                        {
                            StopAnimation();
                            completionSource?.TrySetCanceled(cancellationToken);
                        }));
                    }
                });
            }

            stopwatch.Restart();
            animationTimer.Start();
            Invalidate();

            return completionSource.Task;
        }

        private void AnimationTimer_Tick(object? sender, EventArgs e)
        {
            progress = Math.Clamp(
                stopwatch.Elapsed.TotalMilliseconds / durationMilliseconds,
                0.0,
                1.0);

            Invalidate();

            if (progress >= 1.0)
            {
                StopAnimation();

                Bitmap? old = currentFrame;
                currentFrame = nextFrame;
                nextFrame = null;
                old?.Dispose();

                progress = 1.0;
                Invalidate();

                completionSource?.TrySetResult(true);
            }
        }

        public void CommitCurrentPath(string wallpaperPath)
        {
            CurrentWallpaperPath = wallpaperPath;
        }

        private void StopAnimation()
        {
            animationTimer.Stop();
            stopwatch.Stop();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (currentFrame == null)
            {
                return;
            }

            e.Graphics.DrawImageUnscaled(currentFrame, 0, 0);

            if (nextFrame == null)
            {
                return;
            }

            int width = Math.Min(
                ClientSize.Width,
                (int)Math.Round(ClientSize.Width * progress));

            if (width <= 0)
            {
                return;
            }

            Rectangle reveal =
                new Rectangle(0, 0, width, ClientSize.Height);

            e.Graphics.DrawImage(
                nextFrame,
                reveal,
                reveal,
                GraphicsUnit.Pixel);
        }

        private Bitmap LoadFrame(string path)
        {
            Screen screen =
                Screen.PrimaryScreen
                ?? Screen.AllScreens[0];

            return LoadFrame(
                path,
                screen.Bounds.Size);
        }

        private static Bitmap LoadFrame(
            string path,
            Size targetSize)
        {
            using Image source =
                Image.FromFile(path);

            Bitmap result =
                new Bitmap(
                    targetSize.Width,
                    targetSize.Height,
                    System.Drawing.Imaging.PixelFormat.Format32bppPArgb);

            using Graphics g =
                Graphics.FromImage(result);

            g.InterpolationMode =
                System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

            g.DrawImage(
                source,
                new Rectangle(
                    0,
                    0,
                    targetSize.Width,
                    targetSize.Height));

            return result;
        }

        private static void ReplaceBitmap(
            ref Bitmap? target,
            Bitmap replacement)
        {
            Bitmap? old = target;
            target = replacement;
            old?.Dispose();
        }

        private bool TryAttachToRaisedDesktop()
        {
            IntPtr progman = FindWindow("Progman", null);
            if (progman == IntPtr.Zero)
            {
                return false;
            }

            SendMessageTimeout(
                progman,
                WM_SPAWN_WORKER,
                new IntPtr(0xD),
                new IntPtr(0x1),
                0,
                1000,
                out _);

            IntPtr shellView =
                FindWindowEx(
                    progman,
                    IntPtr.Zero,
                    "SHELLDLL_DefView",
                    null);

            if (shellView == IntPtr.Zero)
            {
                return false;
            }

            IntPtr hwnd = Handle;

            long style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
            style |= WS_CHILD | WS_CLIPSIBLINGS;
            SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(style));

            long exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            exStyle |=
                WS_EX_LAYERED |
                WS_EX_TOOLWINDOW |
                WS_EX_NOACTIVATE;
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(exStyle));

            Marshal.SetLastPInvokeError(0);
            IntPtr previousParent = SetParent(hwnd, progman);
            int parentError = Marshal.GetLastPInvokeError();

            if (previousParent == IntPtr.Zero && parentError != 0)
            {
                return false;
            }

            if (!SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA))
            {
                return false;
            }

            Screen screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            Rectangle bounds = screen.Bounds;

            bool positioned =
                SetWindowPos(
                    hwnd,
                    shellView,
                    0,
                    0,
                    bounds.Width,
                    bounds.Height,
                    SWP_NOACTIVATE |
                    SWP_SHOWWINDOW |
                    SWP_FRAMECHANGED);

            Invalidate();
            Update();
            return positioned;
        }

        public bool EnsureDesktopPlacement()
        {
            if (!IsHandleCreated || IsDisposed)
            {
                return false;
            }

            IntPtr progman = FindWindow("Progman", null);
            if (progman == IntPtr.Zero)
            {
                return false;
            }

            IntPtr shellView =
                FindWindowEx(
                    progman,
                    IntPtr.Zero,
                    "SHELLDLL_DefView",
                    null);

            if (shellView == IntPtr.Zero)
            {
                SendMessageTimeout(
                    progman,
                    WM_SPAWN_WORKER,
                    new IntPtr(0xD),
                    new IntPtr(0x1),
                    0,
                    1000,
                    out _);

                shellView =
                    FindWindowEx(
                        progman,
                        IntPtr.Zero,
                        "SHELLDLL_DefView",
                        null);
            }

            if (shellView == IntPtr.Zero)
            {
                return false;
            }

            if (GetParent(Handle) != progman)
            {
                Marshal.SetLastPInvokeError(0);

                IntPtr previousParent =
                    SetParent(
                        Handle,
                        progman);

                int parentError =
                    Marshal.GetLastPInvokeError();

                if (previousParent == IntPtr.Zero &&
                    parentError != 0)
                {
                    return false;
                }
            }

            Screen screen =
                Screen.PrimaryScreen
                ?? Screen.AllScreens[0];

            Rectangle bounds = screen.Bounds;

            bool positioned =
                SetWindowPos(
                    Handle,
                    shellView,
                    0,
                    0,
                    bounds.Width,
                    bounds.Height,
                    SWP_NOACTIVATE |
                    SWP_SHOWWINDOW);

            if (positioned)
            {
                Invalidate();
                Update();
            }

            return positioned;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                animationTimer.Dispose();
                currentFrame?.Dispose();
                nextFrame?.Dispose();
            }

            base.Dispose(disposing);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindWindowEx(
            IntPtr hWndParent,
            IntPtr hWndChildAfter,
            string? lpszClass,
            string? lpszWindow);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetLayeredWindowAttributes(
            IntPtr hwnd,
            uint crKey,
            byte bAlpha,
            uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int X,
            int Y,
            int cx,
            int cy,
            uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd,
            uint Msg,
            IntPtr wParam,
            IntPtr lParam,
            uint fuFlags,
            uint uTimeout,
            out IntPtr lpdwResult);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

        private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
            IntPtr.Size == 8
                ? GetWindowLongPtr64(hWnd, nIndex)
                : GetWindowLong32(hWnd, nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr64(
            IntPtr hWnd,
            int nIndex,
            IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern IntPtr SetWindowLong32(
            IntPtr hWnd,
            int nIndex,
            IntPtr dwNewLong);

        private static IntPtr SetWindowLongPtr(
            IntPtr hWnd,
            int nIndex,
            IntPtr value) =>
            IntPtr.Size == 8
                ? SetWindowLongPtr64(hWnd, nIndex, value)
                : SetWindowLong32(hWnd, nIndex, value);
    }
}
