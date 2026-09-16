using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace WallpaperControl
{
    internal static class FullscreenActivityDetector
    {
        internal static bool IsFullscreenActive()
        {
            IntPtr window = GetForegroundWindow();
            if (window == IntPtr.Zero || window == GetShellWindow() || window == GetDesktopWindow() || IsIconic(window)) return false;
            GetWindowThreadProcessId(window, out uint processId);
            if (processId == Environment.ProcessId) return false;
            var name = new StringBuilder(256);
            GetClassName(window, name, name.Capacity);
            if (name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
            // A maximized, captioned window is not a fullscreen application.
            if (IsZoomed(window) && (GetWindowLong(window, -16) & 0x00C00000) != 0) return false;
            if (!GetWindowRect(window, out var bounds)) return false;
            var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(MonitorFromWindow(window, 2), ref monitor)) return false;
            return CoversMonitor(bounds.Rectangle, monitor.Monitor.Rectangle);
        }

        internal static bool CoversMonitor(Rectangle window, Rectangle monitor) =>
            monitor.Width > 0 && monitor.Height > 0 &&
            window.Left <= monitor.Left + 2 && window.Top <= monitor.Top + 2 &&
            window.Right >= monitor.Right - 2 && window.Bottom >= monitor.Bottom - 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left, Top, Right, Bottom;
            public Rectangle Rectangle => Rectangle.FromLTRB(Left, Top, Right, Bottom);
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr window);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int count);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rectangle);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    }

    internal sealed class FullscreenPausePolicy
    {
        internal bool IsPaused { get; private set; }
        internal bool AllowsSlideshow(bool manuallyPaused) => !IsPaused && !manuallyPaused;
        private DateTime? clearSince;

        internal bool Update(bool enabled, bool fullscreen, DateTime now)
        {
            bool previous = IsPaused;
            if (!enabled) { IsPaused = false; clearSince = null; }
            else if (fullscreen) { IsPaused = true; clearSince = null; }
            else if (IsPaused)
            {
                clearSince ??= now;
                if (now - clearSince.Value >= TimeSpan.FromSeconds(2)) IsPaused = false;
            }
            return previous != IsPaused;
        }
    }
}
