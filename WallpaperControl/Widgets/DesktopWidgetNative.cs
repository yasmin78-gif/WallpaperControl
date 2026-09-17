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
        /// Keeps the widget as an independent top-level tool window, but places it in the Z-order band immediately above Progman. That is the useful desktop band on the current Windows 11 shell: normal application windows stay above it, while the widget stays above the wallpaper. Important: Progman is NOT used as owner. Owned windows are grouped with their owner and Windows can raise that whole group after mouse input, which is exactly what made the widgets jump in front of apps.
        /// </summary>
        /// <param name="form">The window whose native state or test controls are accessed.</param>
        /// <param name="location">The widget position in screen coordinates.</param>
        /// <returns>True when the widget was attached to the desktop band; otherwise, false.</returns>
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

        /// <summary>
        /// Restores an existing widget&apos;s placement in the desktop window band.
        /// </summary>
        /// <param name="form">The window whose native state or test controls are accessed.</param>
        /// <returns>True when the widget was successfully placed in the desktop band.</returns>
        public static bool KeepOnDesktop(Form form)
        {
            if (form.IsDisposed || !form.IsHandleCreated)
                return false;

            return PutInDesktopBand(form, form.Location, frameChanged: false);
        }

        /// <summary>
        /// Re-enables widget input when a modal settings preview has disabled existing windows.
        /// </summary>
        /// <param name="form">The window whose native state or test controls are accessed.</param>
        /// <returns>False for an unavailable window; otherwise, the native result indicating whether the window was previously disabled.</returns>
        public static bool EnableInteraction(Form form)
        {
            if (form.IsDisposed || !form.IsHandleCreated)
                return false;

            return EnableWindow(form.Handle, true);
        }

        /// <summary>
        /// Places a widget above the desktop shell surface and below ordinary application windows.
        /// </summary>
        /// <param name="form">The window whose native state or test controls are accessed.</param>
        /// <param name="location">The widget position in screen coordinates.</param>
        /// <param name="frameChanged">Whether native frame changes must be applied during placement.</param>
        /// <returns>True when the native window positioning operation succeeded.</returns>
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

        /// <summary>
        /// Finds a top-level native window by class name and caption.
        /// </summary>
        /// <param name="lpClassName">The native window class to match, or null to match any class.</param>
        /// <param name="lpWindowName">The window caption to match, or null to match any caption.</param>
        /// <returns>The matching window handle, or zero when no window matches.</returns>
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        /// <summary>
        /// Retrieves a native window related to the supplied window by the requested relationship.
        /// </summary>
        /// <param name="hWnd">The native window handle used by the operation.</param>
        /// <param name="uCmd">The native relationship used to locate a related window.</param>
        /// <returns>The related window handle, or zero when no such window exists.</returns>
        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        /// <summary>
        /// Enables or disables input for a native window.
        /// </summary>
        /// <param name="hWnd">The native window handle used by the operation.</param>
        /// <param name="bEnable">Whether native window input should be enabled.</param>
        /// <returns>A nonzero value if the window was previously disabled; otherwise, zero.</returns>
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnableWindow(IntPtr hWnd, bool bEnable);

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
        /// Reads a pointer-sized window attribute in a 64-bit process.
        /// </summary>
        /// <param name="hWnd">The native window handle used by the operation.</param>
        /// <param name="nIndex">The index of the native window attribute to read or write.</param>
        /// <returns>The value stored at the requested native window attribute index.</returns>
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        /// <summary>
        /// Reads a window attribute through the 32-bit Windows API.
        /// </summary>
        /// <param name="hWnd">The native window handle used by the operation.</param>
        /// <param name="nIndex">The index of the native window attribute to read or write.</param>
        /// <returns>The value stored at the requested attribute index.</returns>
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

        /// <summary>
        /// Reads a native window attribute using the API appropriate for the process architecture.
        /// </summary>
        /// <param name="hWnd">The native window handle used by the operation.</param>
        /// <param name="nIndex">The index of the native window attribute to read or write.</param>
        /// <returns>The pointer-sized value stored at the requested attribute index.</returns>
        private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
            IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);

        /// <summary>
        /// Writes a pointer-sized window attribute in a 64-bit process.
        /// </summary>
        /// <param name="hWnd">The native window handle used by the operation.</param>
        /// <param name="nIndex">The index of the native window attribute to read or write.</param>
        /// <param name="dwNewLong">The pointer-sized value to store at the native attribute index.</param>
        /// <returns>The previous attribute value; zero may also indicate failure.</returns>
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        /// <summary>
        /// Writes a native window attribute through the 32-bit Windows API.
        /// </summary>
        /// <param name="hWnd">The native window handle used by the operation.</param>
        /// <param name="nIndex">The index of the native window attribute to read or write.</param>
        /// <param name="dwNewLong">The pointer-sized value to store at the native attribute index.</param>
        /// <returns>The previous attribute value; zero may also indicate failure.</returns>
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        /// <summary>
        /// Writes a native window attribute using the API appropriate for the process architecture.
        /// </summary>
        /// <param name="hWnd">The native window handle used by the operation.</param>
        /// <param name="nIndex">The index of the native window attribute to read or write.</param>
        /// <param name="newValue">The pointer-sized native attribute value to store.</param>
        /// <returns>The previous pointer-sized attribute value; zero may also indicate failure.</returns>
        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr newValue) =>
            IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, newValue) : SetWindowLong32(hWnd, nIndex, newValue);
        /// <summary>
        /// Handles mouse activation without taking focus away from the foreground application.
        /// </summary>
        /// <param name="message">The native window message to inspect and process.</param>
        /// <returns>True when the message was handled without activating the widget.</returns>
        internal static bool HandleMouseActivation(ref Message message)
        {
            if (message.Msg != 0x0021) return false;
            message.Result = new IntPtr(3);
            return true;
        }
    }
}
