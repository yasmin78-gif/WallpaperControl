using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WallpaperControl
{
    internal static class DesktopWidgetNative
    {
        private const int GWLP_HWNDPARENT = -8;
        private const int GWL_EXSTYLE = -20;
        private const uint GW_HWNDPREV = 3;

        private const long WS_EX_TOOLWINDOW = 0x00000080L;
        private const long WS_EX_NOACTIVATE = 0x08000000L;
        private const long WS_EX_TOPMOST = 0x00000008L;

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const uint SWP_NOOWNERZORDER = 0x0200;

        /// <summary>
        /// Keeps the widget as an independent top-level tool window, but places
        /// it in the Z-order band immediately above Progman. That is the useful
        /// desktop band on the current Windows 11 shell: normal application
        /// windows stay above it, while the widget stays above the wallpaper.
        ///
        /// Important: Progman is NOT used as owner. Owned windows are grouped
        /// with their owner and Windows can raise that whole group after mouse
        /// input, which is exactly what made the widgets jump in front of apps.
        /// </summary>
        public static bool AttachToDesktop(Form form, Point location)
        {
            if (form.IsDisposed || !form.IsHandleCreated)
                return false;

            IntPtr progman = FindWindow("Progman", null);
            if (progman == IntPtr.Zero)
                return false;

            IntPtr hwnd = form.Handle;

            long exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            exStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            exStyle &= ~WS_EX_TOPMOST;
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(exStyle));

            // Explicitly remove any owner left over from an earlier widget
            // implementation. For a top-level window GWLP_HWNDPARENT is owner.
            SetWindowLongPtr(hwnd, GWLP_HWNDPARENT, IntPtr.Zero);

            Point safe = WidgetSettings.EnsureVisible(location, form.Size);
            form.Location = safe;

            return PutInDesktopBand(form, safe, frameChanged: true);
        }

        public static bool KeepOnDesktop(Form form)
        {
            if (form.IsDisposed || !form.IsHandleCreated)
                return false;

            return PutInDesktopBand(form, form.Location, frameChanged: false);
        }

        public static bool EnableInteraction(Form form)
        {
            if (form.IsDisposed || !form.IsHandleCreated)
                return false;

            return EnableWindow(form.Handle, true);
        }

        private static bool PutInDesktopBand(Form form, Point location, bool frameChanged)
        {
            IntPtr progman = FindWindow("Progman", null);
            if (progman == IntPtr.Zero)
                return false;

            // Get the top-level window immediately above Progman. Inserting
            // our widget after that window places it directly above Progman,
            // and therefore below all ordinary application windows.
            IntPtr insertAfter = GetWindow(progman, GW_HWNDPREV);
            if (insertAfter == IntPtr.Zero)
                return false;

            uint flags = SWP_NOSIZE |
                         SWP_NOACTIVATE |
                         SWP_SHOWWINDOW |
                         SWP_NOOWNERZORDER;

            if (frameChanged)
                flags |= SWP_FRAMECHANGED;

            return SetWindowPos(
                form.Handle,
                insertAfter,
                location.X,
                location.Y,
                0,
                0,
                flags);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnableWindow(IntPtr hWnd, bool bEnable);

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

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

        private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
            IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr newValue) =>
            IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, newValue) : SetWindowLong32(hWnd, nIndex, newValue);
        /// <summary>Handles mouse activation without taking focus away from the foreground application.</summary>
        internal static bool HandleMouseActivation(ref Message message)
        {
            if (message.Msg != 0x0021) return false;
            message.Result = new IntPtr(3);
            return true;
        }
    }
}
