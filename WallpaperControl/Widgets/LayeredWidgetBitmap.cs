using System.Drawing;
using System.Runtime.InteropServices;

namespace WallpaperControl
{
    internal static class LayeredWidgetBitmap
    {
        private const byte AC_SRC_OVER = 0x00;
        private const byte AC_SRC_ALPHA = 0x01;
        private const int ULW_ALPHA = 0x00000002;

        /// <summary>Uploads a transparent bitmap and releases every temporary native drawing resource.</summary>
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
                if (oldBitmap != IntPtr.Zero) SelectObject(memoryDc, oldBitmap);
                if (bitmapHandle != IntPtr.Zero) DeleteObject(bitmapHandle);
                if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
                if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
            }
        }


        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
            public NativePoint(int x, int y) { X = x; Y = y; }
        }


        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSize
        {
            public int Width;
            public int Height;
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


        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr hdcDst, ref NativePoint pptDst, ref NativeSize psize, IntPtr hdcSrc, ref NativePoint pptSrc, int crKey, ref BlendFunction pblend, int dwFlags);


        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);


        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);


        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hDC);


        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);


        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);


        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

    }
}
