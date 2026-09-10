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
        private CancellationTokenRegistration cancellationRegistration;
        private int durationMilliseconds;
        private double progress = 1.0;
        private WallpaperTransitionKind transitionKind =
            WallpaperTransitionKind.DesktopWipe;
        private WallpaperTransitionDirection transitionDirection =
            WallpaperTransitionDirection.Left;
        private WallpaperZoomMode zoomMode =
            WallpaperZoomMode.In;

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

        public Task TransitionToAsync(
            string nextWallpaperPath,
            WallpaperTransitionKind kind,
            int milliseconds,
            WallpaperTransitionDirection direction,
            WallpaperZoomMode requestedZoomMode,
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(nextWallpaperPath))
            {
                throw new FileNotFoundException(
                    "Wallpaper file not found.",
                    nextWallpaperPath);
            }

            ReplaceBitmap(
                ref nextFrame,
                LoadFrame(nextWallpaperPath));

            transitionKind = kind;
            transitionDirection =
                ResolveDirection(direction);
            zoomMode = requestedZoomMode;

            durationMilliseconds = Math.Max(1, milliseconds);
            progress = 0.0;

            cancellationRegistration.Dispose();

            TaskCompletionSource<bool> source =
                new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

            completionSource = source;

            if (cancellationToken.CanBeCanceled)
            {
                cancellationRegistration =
                    cancellationToken.Register(() =>
                    {
                        if (!IsDisposed && IsHandleCreated)
                        {
                            BeginInvoke(new Action(() =>
                            {
                                if (!ReferenceEquals(
                                        completionSource,
                                        source))
                                {
                                    return;
                                }

                                StopAnimation();
                                completionSource = null;
                                cancellationRegistration.Dispose();
                                source.TrySetCanceled(
                                    cancellationToken);
                            }));
                        }
                    });
            }

            stopwatch.Restart();
            animationTimer.Start();
            Invalidate();

            return source.Task;
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

                TaskCompletionSource<bool>? source =
                    completionSource;

                completionSource = null;
                cancellationRegistration.Dispose();
                source?.TrySetResult(true);
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

            if (nextFrame == null)
            {
                e.Graphics.DrawImageUnscaled(
                    currentFrame,
                    0,
                    0);
                return;
            }

            switch (transitionKind)
            {
                case WallpaperTransitionKind.DesktopSlide:
                    DrawSlideTransition(e.Graphics, transitionDirection);
                    break;

                case WallpaperTransitionKind.DesktopFade:
                    DrawFadeTransition(e.Graphics);
                    break;

                case WallpaperTransitionKind.DesktopZoomFade:
                    if (zoomMode == WallpaperZoomMode.Out)
                    {
                        DrawZoomOutFadeTransition(e.Graphics);
                    }
                    else
                    {
                        DrawZoomFadeTransition(e.Graphics);
                    }
                    break;

                case WallpaperTransitionKind.DesktopSplit:
                    DrawSplitTransition(e.Graphics);
                    break;

                case WallpaperTransitionKind.DesktopCurtain:
                    DrawCurtainTransition(e.Graphics);
                    break;

                case WallpaperTransitionKind.DesktopZoomOutFade:
                    DrawZoomOutFadeTransition(e.Graphics);
                    break;

                case WallpaperTransitionKind.DesktopWipe:
                default:
                    DrawWipeTransition(e.Graphics, transitionDirection);
                    break;
            }
        }

        private WallpaperTransitionDirection ResolveDirection(
            WallpaperTransitionDirection direction)
        {
            if (direction != WallpaperTransitionDirection.Random)
            {
                return direction;
            }

            return Random.Shared.Next(4) switch
            {
                0 => WallpaperTransitionDirection.Left,
                1 => WallpaperTransitionDirection.Right,
                2 => WallpaperTransitionDirection.Up,
                _ => WallpaperTransitionDirection.Down
            };
        }

        private void DrawWipeTransition(
            Graphics graphics,
            WallpaperTransitionDirection direction)
        {
            if (currentFrame == null)
            {
                return;
            }

            graphics.DrawImageUnscaled(currentFrame, 0, 0);

            if (nextFrame == null)
            {
                return;
            }

            int width = ClientSize.Width;
            int height = ClientSize.Height;
            Rectangle reveal;

            switch (direction)
            {
                case WallpaperTransitionDirection.Right:
                {
                    int visible = Math.Min(width, (int)Math.Round(width * progress));
                    reveal = new Rectangle(0, 0, visible, height);
                    break;
                }

                case WallpaperTransitionDirection.Up:
                {
                    int visible = Math.Min(height, (int)Math.Round(height * progress));
                    reveal = new Rectangle(0, height - visible, width, visible);
                    break;
                }

                case WallpaperTransitionDirection.Down:
                {
                    int visible = Math.Min(height, (int)Math.Round(height * progress));
                    reveal = new Rectangle(0, 0, width, visible);
                    break;
                }

                case WallpaperTransitionDirection.Left:
                default:
                {
                    int visible = Math.Min(width, (int)Math.Round(width * progress));
                    reveal = new Rectangle(width - visible, 0, visible, height);
                    break;
                }
            }

            if (reveal.Width <= 0 || reveal.Height <= 0)
            {
                return;
            }

            graphics.DrawImage(nextFrame, reveal, reveal, GraphicsUnit.Pixel);
        }

        private void DrawSlideTransition(
            Graphics graphics,
            WallpaperTransitionDirection direction)
        {
            if (currentFrame == null || nextFrame == null)
            {
                return;
            }

            int width = ClientSize.Width;
            int height = ClientSize.Height;
            int currentX = 0;
            int currentY = 0;
            int nextX = 0;
            int nextY = 0;

            switch (direction)
            {
                case WallpaperTransitionDirection.Right:
                {
                    int offset = (int)Math.Round(width * progress);
                    currentX = offset;
                    nextX = -width + offset;
                    break;
                }

                case WallpaperTransitionDirection.Up:
                {
                    int offset = (int)Math.Round(height * progress);
                    currentY = -offset;
                    nextY = height - offset;
                    break;
                }

                case WallpaperTransitionDirection.Down:
                {
                    int offset = (int)Math.Round(height * progress);
                    currentY = offset;
                    nextY = -height + offset;
                    break;
                }

                case WallpaperTransitionDirection.Left:
                default:
                {
                    int offset = (int)Math.Round(width * progress);
                    currentX = -offset;
                    nextX = width - offset;
                    break;
                }
            }

            graphics.DrawImageUnscaled(currentFrame, currentX, currentY);
            graphics.DrawImageUnscaled(nextFrame, nextX, nextY);
        }

        private void DrawFadeTransition(Graphics graphics)
        {
            if (currentFrame == null)
            {
                return;
            }

            graphics.DrawImageUnscaled(
                currentFrame,
                0,
                0);

            if (nextFrame == null)
            {
                return;
            }

            float alpha =
                (float)Math.Clamp(
                    progress,
                    0.0,
                    1.0);

            using System.Drawing.Imaging.ImageAttributes attributes =
                new System.Drawing.Imaging.ImageAttributes();

            System.Drawing.Imaging.ColorMatrix matrix =
                new System.Drawing.Imaging.ColorMatrix
                {
                    Matrix00 = 1.0f,
                    Matrix11 = 1.0f,
                    Matrix22 = 1.0f,
                    Matrix33 = alpha,
                    Matrix44 = 1.0f
                };

            attributes.SetColorMatrix(
                matrix,
                System.Drawing.Imaging.ColorMatrixFlag.Default,
                System.Drawing.Imaging.ColorAdjustType.Bitmap);

            Rectangle destination =
                new Rectangle(
                    0,
                    0,
                    ClientSize.Width,
                    ClientSize.Height);

            graphics.DrawImage(
                nextFrame,
                destination,
                0,
                0,
                nextFrame.Width,
                nextFrame.Height,
                GraphicsUnit.Pixel,
                attributes);
        }

        private void DrawZoomFadeTransition(Graphics graphics)
        {
            if (currentFrame == null)
            {
                return;
            }

            graphics.DrawImageUnscaled(
                currentFrame,
                0,
                0);

            if (nextFrame == null)
            {
                return;
            }

            float alpha =
                (float)Math.Clamp(
                    progress,
                    0.0,
                    1.0);

            double startScale = 1.15;
            double scale =
                startScale -
                ((startScale - 1.0) * progress);

            int width =
                (int)Math.Round(
                    ClientSize.Width * scale);

            int height =
                (int)Math.Round(
                    ClientSize.Height * scale);

            int x =
                (ClientSize.Width - width) / 2;

            int y =
                (ClientSize.Height - height) / 2;

            using System.Drawing.Imaging.ImageAttributes attributes =
                new System.Drawing.Imaging.ImageAttributes();

            System.Drawing.Imaging.ColorMatrix matrix =
                new System.Drawing.Imaging.ColorMatrix
                {
                    Matrix00 = 1.0f,
                    Matrix11 = 1.0f,
                    Matrix22 = 1.0f,
                    Matrix33 = alpha,
                    Matrix44 = 1.0f
                };

            attributes.SetColorMatrix(
                matrix,
                System.Drawing.Imaging.ColorMatrixFlag.Default,
                System.Drawing.Imaging.ColorAdjustType.Bitmap);

            Rectangle destination =
                new Rectangle(
                    x,
                    y,
                    width,
                    height);

            graphics.DrawImage(
                nextFrame,
                destination,
                0,
                0,
                nextFrame.Width,
                nextFrame.Height,
                GraphicsUnit.Pixel,
                attributes);
        }

        private void DrawSplitTransition(Graphics graphics)
        {
            if (currentFrame == null)
            {
                return;
            }

            graphics.DrawImageUnscaled(currentFrame, 0, 0);

            if (nextFrame == null)
            {
                return;
            }

            int halfWidth =
                (int)Math.Round((ClientSize.Width / 2.0) * progress);

            if (halfWidth <= 0)
            {
                return;
            }

            int center = ClientSize.Width / 2;

            Rectangle left =
                new Rectangle(center - halfWidth, 0, halfWidth, ClientSize.Height);
            Rectangle right =
                new Rectangle(center, 0, halfWidth, ClientSize.Height);

            graphics.DrawImage(nextFrame, left, left, GraphicsUnit.Pixel);
            graphics.DrawImage(nextFrame, right, right, GraphicsUnit.Pixel);
        }

        private void DrawCurtainTransition(Graphics graphics)
        {
            if (currentFrame == null || nextFrame == null)
            {
                return;
            }

            // Das neue Bild liegt bereits vollständig dahinter.
            graphics.DrawImageUnscaled(nextFrame, 0, 0);

            int halfWidth =
                ClientSize.Width / 2;

            int offset =
                (int)Math.Round(
                    halfWidth * progress);

            Rectangle leftSource =
                new Rectangle(
                    0,
                    0,
                    halfWidth,
                    ClientSize.Height);

            Rectangle rightSource =
                new Rectangle(
                    halfWidth,
                    0,
                    ClientSize.Width - halfWidth,
                    ClientSize.Height);

            Rectangle leftDestination =
                new Rectangle(
                    -offset,
                    0,
                    halfWidth,
                    ClientSize.Height);

            Rectangle rightDestination =
                new Rectangle(
                    halfWidth + offset,
                    0,
                    ClientSize.Width - halfWidth,
                    ClientSize.Height);

            // Anders als beim Split werden die beiden Hälften des alten
            // Wallpapers tatsächlich nach außen geschoben. Dadurch bleibt
            // ihr Bildinhalt sichtbar in Bewegung, statt nur abgeschnitten
            // zu werden.
            graphics.DrawImage(
                currentFrame,
                leftDestination,
                leftSource,
                GraphicsUnit.Pixel);

            graphics.DrawImage(
                currentFrame,
                rightDestination,
                rightSource,
                GraphicsUnit.Pixel);
        }

        private void DrawZoomOutFadeTransition(Graphics graphics)
        {
            if (currentFrame == null)
            {
                return;
            }

            graphics.DrawImageUnscaled(currentFrame, 0, 0);

            if (nextFrame == null)
            {
                return;
            }

            float alpha = (float)Math.Clamp(progress, 0.0, 1.0);
            double startScale = 0.85;
            double scale = startScale + ((1.0 - startScale) * progress);

            int width = (int)Math.Round(ClientSize.Width * scale);
            int height = (int)Math.Round(ClientSize.Height * scale);
            int x = (ClientSize.Width - width) / 2;
            int y = (ClientSize.Height - height) / 2;

            using System.Drawing.Imaging.ImageAttributes attributes =
                new System.Drawing.Imaging.ImageAttributes();

            System.Drawing.Imaging.ColorMatrix matrix =
                new System.Drawing.Imaging.ColorMatrix
                {
                    Matrix00 = 1.0f,
                    Matrix11 = 1.0f,
                    Matrix22 = 1.0f,
                    Matrix33 = alpha,
                    Matrix44 = 1.0f
                };

            attributes.SetColorMatrix(
                matrix,
                System.Drawing.Imaging.ColorMatrixFlag.Default,
                System.Drawing.Imaging.ColorAdjustType.Bitmap);

            Rectangle destination = new Rectangle(x, y, width, height);

            graphics.DrawImage(
                nextFrame,
                destination,
                0,
                0,
                nextFrame.Width,
                nextFrame.Height,
                GraphicsUnit.Pixel,
                attributes);
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
                StopAnimation();
                animationTimer.Dispose();
                cancellationRegistration.Dispose();

                TaskCompletionSource<bool>? pending =
                    completionSource;

                completionSource = null;
                pending?.TrySetException(
                    new ObjectDisposedException(
                        nameof(PersistentDesktopWallpaperHost)));

                currentFrame?.Dispose();
                currentFrame = null;

                nextFrame?.Dispose();
                nextFrame = null;
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
