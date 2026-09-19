using System.IO;

using System.Runtime.InteropServices;

namespace WallpaperControl
{
    internal sealed class PersistentDesktopWallpaperHost : Form, IPersistentWallpaperHost
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
        private string? nextWallpaperPath;
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
        private DesktopWallpaperPosition wallpaperPosition =
            DesktopWallpaperPosition.Fill;

        public string? CurrentWallpaperPath { get; private set; }

        /// <summary>
        /// Configures the persistent desktop window and its animation timer.
        /// </summary>
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

        /// <summary>
        /// Attaches the empty host to the desktop before loading a frame at its final dimensions.
        /// </summary>
        /// <param name="wallpaperPath">The wallpaper image path to load or record.</param>
        /// <returns>True when the host was initialized with the requested wallpaper; otherwise, false.</returns>
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
            // before allocating the render surface for the actual desktop size.
            PerformLayout();
            Update();

            ReplaceBitmap(
                ref currentFrame,
                LoadFrame(
                    wallpaperPath,
                    new Size(
                        finalBounds.Width,
                        finalBounds.Height),
                    wallpaperPosition));

            CurrentWallpaperPath =
                wallpaperPath;

            progress = 1.0;

            Invalidate(true);
            Update();

            return true;
        }

        /// <summary>
        /// Updates the image layout and refreshes rendered frames for the desktop surface.
        /// </summary>
        /// <param name="position">The Windows wallpaper scaling and placement mode.</param>
        public void SetWallpaperPosition(DesktopWallpaperPosition position)
        {
            if (wallpaperPosition == position) return;
            Bitmap? current = null;
            Bitmap? next = null;
            try
            {
                if (currentFrame != null && CurrentWallpaperPath != null)
                    current = LoadFrame(CurrentWallpaperPath, ClientSize, position);
                if (nextFrame != null && nextWallpaperPath != null)
                    next = LoadFrame(nextWallpaperPath, ClientSize, position);
                // Swap both frames atomically on the UI thread, retaining progress.
                if (current != null) { ReplaceBitmap(ref currentFrame, current); current = null; }
                if (next != null) { ReplaceBitmap(ref nextFrame, next); next = null; }
                wallpaperPosition = position;
                Invalidate(true);
                Update();
            }
            finally { current?.Dispose(); next?.Dispose(); }
        }

        /// <summary>
        /// Loads the next image and runs the selected animation with cancellation and suspension support.
        /// </summary>
        /// <param name="nextWallpaperPath">The image path to display next.</param>
        /// <param name="kind">The transition effect to animate.</param>
        /// <param name="milliseconds">The animation duration in milliseconds.</param>
        /// <param name="direction">The requested direction of the animated wallpaper transition.</param>
        /// <param name="requestedZoomMode">The zoom mode requested for this transition.</param>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        /// <returns>A task representing completion of the asynchronous operation.</returns>
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

            cancellationToken.ThrowIfCancellationRequested();

            if (completionSource != null || animationTimer.Enabled)
            {
                throw new InvalidOperationException(
                    "A wallpaper transition is already in progress.");
            }

            ReplaceBitmap(
                ref nextFrame,
                LoadFrame(nextWallpaperPath, ClientSize, wallpaperPosition));

            this.nextWallpaperPath = nextWallpaperPath;
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
                        try
                        {
                            if (IsDisposed || !IsHandleCreated)
                            {
                                source.TrySetCanceled(
                                    cancellationToken);
                                return;
                            }

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
                        catch (InvalidOperationException)
                        {
                            // ObjectDisposedException derives from
                            // InvalidOperationException, so this also
                            // covers a handle/control disposed while
                            // cancellation is being marshalled.
                            source.TrySetCanceled(
                                cancellationToken);
                        }
                    });
            }

            stopwatch.Restart();
            if (activitySuspended) stopwatch.Stop();
            if (!activitySuspended) animationTimer.Start();
            Invalidate(true);

            return source.Task;
        }

        private bool activitySuspended;
        /// <summary>
        /// Stops or resumes the animation clock and timer without discarding an in-progress transition.
        /// </summary>
        /// <param name="suspended">True to pause background activity; false to resume it.</param>
        public void SetActivitySuspended(bool suspended)
        {
            activitySuspended = suspended;
            if (suspended) { animationTimer.Stop(); stopwatch.Stop(); }
            else if (completionSource != null) { stopwatch.Start(); animationTimer.Start(); }
        }
        /// <summary>
        /// Advances animation progress and completes the transition when its duration has elapsed.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private void AnimationTimer_Tick(object? sender, EventArgs e)
        {
            progress = Math.Clamp(
                stopwatch.Elapsed.TotalMilliseconds / durationMilliseconds,
                0.0,
                1.0);

            Invalidate(true);

            if (progress >= 1.0)
            {
                StopAnimation();

                Bitmap? old = currentFrame;
                currentFrame = nextFrame;
                nextFrame = null;
                CurrentWallpaperPath = nextWallpaperPath;
                nextWallpaperPath = null;
                old?.Dispose();

                progress = 1.0;
                Invalidate(true);

                TaskCompletionSource<bool>? source =
                    completionSource;

                completionSource = null;
                cancellationRegistration.Dispose();
                source?.TrySetResult(true);
            }
        }

        /// <summary>
        /// Records the wallpaper path that the host now presents as current.
        /// </summary>
        /// <param name="wallpaperPath">The wallpaper image path to load or record.</param>
        public void CommitCurrentPath(string wallpaperPath)
        {
            CurrentWallpaperPath = wallpaperPath;
        }

        /// <summary>
        /// Stops timing and cleans up the active transition&apos;s completion and cancellation state.
        /// </summary>
        private void StopAnimation()
        {
            animationTimer.Stop();
            stopwatch.Stop();
        }

        /// <summary>
        /// Draws the current frame or the active transition on the desktop surface.
        /// </summary>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
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


                case WallpaperTransitionKind.DesktopWipe:
                default:
                    DrawWipeTransition(e.Graphics, transitionDirection);
                    break;
            }
        }

        /// <summary>
        /// Chooses a concrete movement direction when a random direction is requested.
        /// </summary>
        /// <param name="direction">The requested direction of the animated wallpaper transition.</param>
        /// <returns>A concrete movement direction for the animation.</returns>
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

        /// <summary>
        /// Reveals the next frame along the selected direction using the current animation progress.
        /// </summary>
        /// <param name="graphics">The drawing surface used for the operation.</param>
        /// <param name="direction">The requested direction of the animated wallpaper transition.</param>
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

        /// <summary>
        /// Draws the moving frames for the directional slide animation.
        /// </summary>
        /// <param name="graphics">The drawing surface used for the operation.</param>
        /// <param name="direction">The requested direction of the animated wallpaper transition.</param>
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

        /// <summary>
        /// Blends the current and next frames using the animation progress.
        /// </summary>
        /// <param name="graphics">The drawing surface used for the operation.</param>
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

        /// <summary>
        /// Draws the zoom-and-fade effect for the selected zoom mode.
        /// </summary>
        /// <param name="graphics">The drawing surface used for the operation.</param>
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

        /// <summary>
        /// Reveals the next frame by separating the visible regions of the current frame.
        /// </summary>
        /// <param name="graphics">The drawing surface used for the operation.</param>
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

        /// <summary>
        /// Slides both halves of the old frame outward while retaining their visible image content.
        /// </summary>
        /// <param name="graphics">The drawing surface used for the operation.</param>
        private void DrawCurtainTransition(Graphics graphics)
        {
            if (currentFrame == null || nextFrame == null)
            {
                return;
            }

            // The complete new image has already been drawn behind the old image.
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

            // Unlike the split effect, the curtain moves both halves of the old
            // wallpaper outward. Their image content therefore moves visibly
            // instead of being revealed only by clipping the old image.
            // The new wallpaper remains stationary underneath.
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

        /// <summary>
        /// Shrinks and fades the old frame over the already prepared next image.
        /// </summary>
        /// <param name="graphics">The drawing surface used for the operation.</param>
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

        /// <summary>
        /// Loads a wallpaper into a bitmap sized and positioned for the requested desktop layout.
        /// </summary>
        /// <param name="path">The image or folder path to process.</param>
        /// <returns>The rendered wallpaper bitmap, whose ownership passes to the caller.</returns>
        private Bitmap LoadFrame(string path)
        {
            Screen screen =
                Screen.PrimaryScreen
                ?? Screen.AllScreens[0];

            return LoadFrame(
                path,
                screen.Bounds.Size,
                wallpaperPosition);
        }

        /// <summary>
        /// Loads a wallpaper into a bitmap sized and positioned for the requested desktop layout.
        /// </summary>
        /// <param name="path">The image or folder path to process.</param>
        /// <param name="targetSize">The target surface dimensions in pixels.</param>
        /// <param name="position">The Windows wallpaper scaling and placement mode.</param>
        /// <returns>The rendered wallpaper bitmap, whose ownership passes to the caller.</returns>
        private static Bitmap LoadFrame(
            string path,
            Size targetSize,
            DesktopWallpaperPosition position)
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

            g.Clear(Color.Black);

            Rectangle targetRectangle = position switch
            {
                DesktopWallpaperPosition.Stretch =>
                    new Rectangle(0, 0, targetSize.Width, targetSize.Height),
                DesktopWallpaperPosition.Center =>
                    new Rectangle(
                        (targetSize.Width - source.Width) / 2,
                        (targetSize.Height - source.Height) / 2,
                        source.Width,
                        source.Height),
                DesktopWallpaperPosition.Fit =>
                    GetAspectRectangle(source.Size, targetSize, fill: false),
                DesktopWallpaperPosition.Fill =>
                    GetAspectRectangle(source.Size, targetSize, fill: true),
                DesktopWallpaperPosition.Span =>
                    GetAspectRectangle(source.Size, targetSize, fill: true),
                _ => Rectangle.Empty
            };

            if (position == DesktopWallpaperPosition.Tile)
            {
                using TextureBrush brush = new TextureBrush(source);
                g.FillRectangle(brush, new Rectangle(Point.Empty, targetSize));
            }
            else
            {
                g.DrawImage(source, targetRectangle);
            }

            return result;
        }


        /// <summary>
        /// Calculates an aspect-preserving fit or fill rectangle for the source image.
        /// </summary>
        /// <param name="sourceSize">The source image dimensions in pixels.</param>
        /// <param name="targetSize">The target surface dimensions in pixels.</param>
        /// <param name="fill">True to cover the target by cropping; false to fit the entire source image.</param>
        /// <returns>The destination rectangle that preserves the source aspect ratio.</returns>
        private static Rectangle GetAspectRectangle(
            Size sourceSize,
            Size targetSize,
            bool fill)
        {
            double scaleX = (double)targetSize.Width / sourceSize.Width;
            double scaleY = (double)targetSize.Height / sourceSize.Height;
            double scale = fill
                ? Math.Max(scaleX, scaleY)
                : Math.Min(scaleX, scaleY);

            int width = Math.Max(1, (int)Math.Round(sourceSize.Width * scale));
            int height = Math.Max(1, (int)Math.Round(sourceSize.Height * scale));

            return new Rectangle(
                (targetSize.Width - width) / 2,
                (targetSize.Height - height) / 2,
                width,
                height);
        }

        /// <summary>
        /// Replaces an owned frame and disposes the previous bitmap.
        /// </summary>
        /// <param name="target">The owned bitmap reference to replace after releasing its previous image.</param>
        /// <param name="replacement">The new bitmap whose ownership replaces the current frame.</param>
        private static void ReplaceBitmap(
            ref Bitmap? target,
            Bitmap replacement)
        {
            Bitmap? old = target;
            target = replacement;
            old?.Dispose();
        }

        /// <summary>
        /// Finds the appropriate Explorer desktop surface and attaches the wallpaper host to it.
        /// </summary>
        /// <returns>True when the host could attach to the expected desktop surface.</returns>
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

            Invalidate(true);
            Update();
            return positioned;
        }

        /// <summary>
        /// Restores the host&apos;s required placement relative to Explorer&apos;s desktop windows.
        /// </summary>
        /// <returns>True when the host was successfully positioned behind the desktop icons.</returns>
        public bool EnsureDesktopPlacement()
        {
            if (!PersistentDesktopTransitionManager.SupportsConfiguration(Screen.AllScreens.Length, wallpaperPosition))
                return false;
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
                Invalidate(true);
                Update();
            }

            return positioned;
        }

        /// <summary>
        /// Releases the resources owned by this persistent desktop wallpaper host.
        /// </summary>
        /// <param name="disposing">True when managed resources should be released during explicit disposal.</param>
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

        /// <summary>
        /// Finds a top-level native window by class name and caption.
        /// </summary>
        /// <param name="lpClassName">The native window class to match, or null to match any class.</param>
        /// <param name="lpWindowName">The window caption to match, or null to match any caption.</param>
        /// <returns>The matching window handle, or zero when no window matches.</returns>
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        /// <summary>
        /// Finds a child native window after the specified sibling.
        /// </summary>
        /// <param name="hWndParent">The parent window whose children are searched.</param>
        /// <param name="hWndChildAfter">The sibling after which to continue the child-window search.</param>
        /// <param name="lpszClass">The child-window class to match, or null to match any class.</param>
        /// <param name="lpszWindow">The child-window caption to match, or null to match any caption.</param>
        /// <returns>The matching child window handle, or zero when no window matches.</returns>
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindWindowEx(
            IntPtr hWndParent,
            IntPtr hWndChildAfter,
            string? lpszClass,
            string? lpszWindow);

        /// <summary>
        /// Retrieves a native window&apos;s parent or owner.
        /// </summary>
        /// <param name="hWnd">The native window handle used by the operation.</param>
        /// <returns>The parent or owner handle, or zero when none exists.</returns>
        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        /// <summary>
        /// Changes a native window&apos;s parent to the requested desktop surface.
        /// </summary>
        /// <param name="hWndChild">The child window whose parent is changed.</param>
        /// <param name="hWndNewParent">The new parent window handle, or zero to detach from a parent.</param>
        /// <returns>The previous parent handle; zero may also indicate failure.</returns>
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        /// <summary>
        /// Updates the color-key or alpha attributes of a layered native window.
        /// </summary>
        /// <param name="hwnd">The native window handle used by the operation.</param>
        /// <param name="crKey">The native color key used by the layered-window operation.</param>
        /// <param name="bAlpha">The constant opacity value from zero to 255.</param>
        /// <param name="dwFlags">The option flags defined by the invoked native API.</param>
        /// <returns>True when the attributes were applied; otherwise, false.</returns>
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetLayeredWindowAttributes(
            IntPtr hwnd,
            uint crKey,
            byte bAlpha,
            uint dwFlags);

        /// <summary>
        /// Changes native window bounds, visibility, or Z-order according to the supplied flags.
        /// </summary>
        /// <param name="hWnd">The native window handle used by the operation.</param>
        /// <param name="hWndInsertAfter">The window or special Z-order handle used for placement.</param>
        /// <param name="X">The requested native window X coordinate.</param>
        /// <param name="Y">The requested native window Y coordinate.</param>
        /// <param name="cx">The requested native window width in pixels.</param>
        /// <param name="cy">The requested native window height in pixels.</param>
        /// <param name="uFlags">The native placement flags controlling which bounds and Z-order changes are applied.</param>
        /// <returns>True when the requested operation succeeds; otherwise, false.</returns>
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

        /// <summary>
        /// Sends a native window message with a bounded wait.
        /// </summary>
        /// <param name="hWnd">The native window handle used by the operation.</param>
        /// <param name="Msg">The native window message identifier.</param>
        /// <param name="wParam">The message-specific first parameter.</param>
        /// <param name="lParam">The message-specific second parameter.</param>
        /// <param name="fuFlags">The native message-send behavior flags.</param>
        /// <param name="uTimeout">The maximum native message wait in milliseconds.</param>
        /// <param name="lpdwResult">Receives the result produced by the native message handler.</param>
        /// <returns>A nonzero value on success, or zero on failure or timeout.</returns>
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd,
            uint Msg,
            IntPtr wParam,
            IntPtr lParam,
            uint fuFlags,
            uint uTimeout,
            out IntPtr lpdwResult);

        /// <summary>
        /// Reads a pointer-sized window attribute in a 64-bit process.
        /// </summary>
        /// <param name="hWnd">The native window handle used by the operation.</param>
        /// <param name="nIndex">The index of the native window attribute to read or write.</param>
        /// <returns>The value stored at the requested native window attribute index.</returns>
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        /// <summary>
        /// Reads a window attribute through the 32-bit Windows API.
        /// </summary>
        /// <param name="hWnd">The native window handle used by the operation.</param>
        /// <param name="nIndex">The index of the native window attribute to read or write.</param>
        /// <returns>The value stored at the requested attribute index.</returns>
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

        /// <summary>
        /// Reads a native window attribute using the API appropriate for the process architecture.
        /// </summary>
        /// <param name="hWnd">The native window handle used by the operation.</param>
        /// <param name="nIndex">The index of the native window attribute to read or write.</param>
        /// <returns>The pointer-sized value stored at the requested attribute index.</returns>
        private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
            IntPtr.Size == 8
                ? GetWindowLongPtr64(hWnd, nIndex)
                : GetWindowLong32(hWnd, nIndex);

        /// <summary>
        /// Writes a pointer-sized window attribute in a 64-bit process.
        /// </summary>
        /// <param name="hWnd">The native window handle used by the operation.</param>
        /// <param name="nIndex">The index of the native window attribute to read or write.</param>
        /// <param name="dwNewLong">The pointer-sized value to store at the native attribute index.</param>
        /// <returns>The previous attribute value; zero may also indicate failure.</returns>
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr64(
            IntPtr hWnd,
            int nIndex,
            IntPtr dwNewLong);

        /// <summary>
        /// Writes a native window attribute through the 32-bit Windows API.
        /// </summary>
        /// <param name="hWnd">The native window handle used by the operation.</param>
        /// <param name="nIndex">The index of the native window attribute to read or write.</param>
        /// <param name="dwNewLong">The pointer-sized value to store at the native attribute index.</param>
        /// <returns>The previous attribute value; zero may also indicate failure.</returns>
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern IntPtr SetWindowLong32(
            IntPtr hWnd,
            int nIndex,
            IntPtr dwNewLong);

        /// <summary>
        /// Writes a native window attribute using the API appropriate for the process architecture.
        /// </summary>
        /// <param name="hWnd">The native window handle used by the operation.</param>
        /// <param name="nIndex">The index of the native window attribute to read or write.</param>
        /// <param name="value">The native window attribute value to write.</param>
        /// <returns>The previous pointer-sized attribute value; zero may also indicate failure.</returns>
        private static IntPtr SetWindowLongPtr(
            IntPtr hWnd,
            int nIndex,
            IntPtr value) =>
            IntPtr.Size == 8
                ? SetWindowLongPtr64(hWnd, nIndex, value)
                : SetWindowLong32(hWnd, nIndex, value);
    }
}
