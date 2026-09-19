using System.Drawing;
using System.Runtime.InteropServices;

namespace WallpaperControl
{
    internal static class LayeredWidgetBitmap
    {
        private const byte AC_SRC_OVER = 0x00;
        private const byte AC_SRC_ALPHA = 0x01;
        private const int ULW_ALPHA = 0x00000002;
        private static int releaseFailureLogged;

        /// <summary>
        /// Uploads a transparent bitmap and releases every temporary native drawing resource.
        /// </summary>
        /// <param name="handle">The native window handle used by the operation.</param>
        /// <param name="location">The widget position in screen coordinates.</param>
        /// <param name="bitmap">The bitmap to upload; the caller retains ownership.</param>
        internal static void Update(IntPtr handle, Point location, Bitmap bitmap)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memoryDc = CreateCompatibleDC(screenDc);
            IntPtr bitmapHandle = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;

            try
            {
                bitmapHandle = bitmap.GetHbitmap(Color.FromArgb(0));
                oldBitmap = SelectObject(memoryDc, bitmapHandle);

                NativePoint source = new(0, 0);
                NativePoint destination = new(location.X, location.Y);
                NativeSize size = new(bitmap.Width, bitmap.Height);
                BlendFunction blend = new()
                {
                    BlendOp = AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = AC_SRC_ALPHA
                };

                UpdateLayeredWindow(handle, screenDc, ref destination, ref size, memoryDc, ref source, 0, ref blend, ULW_ALPHA);
            }
            finally
            {
                // Restore the previous selection before deleting the bitmap; GDI still owns
                // a reference to any object selected into a device context.
                if (oldBitmap != IntPtr.Zero) SelectObject(memoryDc, oldBitmap);
                if (bitmapHandle != IntPtr.Zero) DeleteObject(bitmapHandle);
                if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
                if (screenDc != IntPtr.Zero && ReleaseDC(IntPtr.Zero, screenDc) == 0 &&
                    System.Threading.Interlocked.Exchange(ref releaseFailureLogged, 1) == 0)
                    AppLogger.Warning("Could not release the widget screen device context.",
                        new InvalidOperationException("ReleaseDC failed."));
            }
        }


        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
            /// <summary>
            /// Initializes the coordinates passed to the native layered-window API.
            /// </summary>
            /// <param name="x">The horizontal coordinate.</param>
            /// <param name="y">The vertical coordinate.</param>
            public NativePoint(int x, int y) { X = x; Y = y; }
        }


        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSize
        {
            public int Width;
            public int Height;
            /// <summary>
            /// Initializes the dimensions passed to the native layered-window API.
            /// </summary>
            /// <param name="width">The width in pixels.</param>
            /// <param name="height">The height in pixels.</param>
            public NativeSize(int width, int height) { Width = width; Height = height; }
        }


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BlendFunction
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }


        /// <summary>
        /// Updates a layered window&apos;s position, dimensions, and per-pixel-alpha surface.
        /// </summary>
        /// <param name="hWnd">The native window handle used by the operation.</param>
        /// <param name="hdcDst">The destination device context used for the layered-window update.</param>
        /// <param name="pptDst">The destination window position in screen coordinates.</param>
        /// <param name="psize">The width and height of the layered-window surface.</param>
        /// <param name="hdcSrc">The source memory device context containing the bitmap.</param>
        /// <param name="pptSrc">The source bitmap origin in its memory device context.</param>
        /// <param name="crKey">The native color key used by the layered-window operation.</param>
        /// <param name="pblend">The alpha-blending parameters used for the layered surface.</param>
        /// <param name="dwFlags">The option flags defined by the invoked native API.</param>
        /// <returns>True when the layered window was updated; otherwise, false.</returns>
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr hdcDst, ref NativePoint pptDst, ref NativeSize psize, IntPtr hdcSrc, ref NativePoint pptSrc, int crKey, ref BlendFunction pblend, int dwFlags);


        /// <summary>
        /// Acquires a device context for the specified window or the screen.
        /// </summary>
        /// <param name="hWnd">The native window handle used by the operation.</param>
        /// <returns>The device-context handle, or zero on failure; release it with ReleaseDC.</returns>
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);


        /// <summary>
        /// Releases a device context previously obtained through GetDC.
        /// </summary>
        /// <param name="hWnd">The native window handle used by the operation.</param>
        /// <param name="hDC">The native device-context handle.</param>
        /// <returns>A nonzero value if the device context was released; otherwise, zero.</returns>
        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);


        /// <summary>
        /// Creates a memory device context compatible with the supplied context.
        /// </summary>
        /// <param name="hDC">The native device-context handle.</param>
        /// <returns>The memory device-context handle, or zero on failure; release it with DeleteDC.</returns>
        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hDC);


        /// <summary>
        /// Releases a memory device context created by the application.
        /// </summary>
        /// <param name="hdc">The native device-context handle.</param>
        /// <returns>True when the device context was deleted; otherwise, false.</returns>
        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);


        /// <summary>
        /// Selects a drawing object into a device context and returns the previous selection.
        /// </summary>
        /// <param name="hdc">The native device-context handle.</param>
        /// <param name="hgdiobj">The drawing-object handle to select into the device context.</param>
        /// <returns>The previously selected object; restore it before deleting the replacement object.</returns>
        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);


        /// <summary>
        /// Releases an application-owned GDI drawing object.
        /// </summary>
        /// <param name="hObject">The owned GDI object handle to delete.</param>
        /// <returns>True when the object was deleted; otherwise, false.</returns>
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

    }
}
