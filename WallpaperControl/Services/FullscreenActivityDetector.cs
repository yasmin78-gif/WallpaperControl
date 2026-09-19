using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace WallpaperControl
{
    internal static class FullscreenActivityDetector
    {
        /// <summary>
        /// Detects a foreground application covering a monitor, excluding desktop surfaces and ordinary maximized windows.
        /// </summary>
        /// <returns>True when an eligible foreground window covers a monitor; otherwise, false.</returns>
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

        /// <summary>
        /// Checks monitor coverage with a two-pixel tolerance for window-frame rounding.
        /// </summary>
        /// <param name="window">The foreground window bounds in screen coordinates.</param>
        /// <param name="monitor">The monitor bounds in screen coordinates.</param>
        /// <returns>True when the window covers the valid monitor bounds within the two-pixel tolerance.</returns>
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
        /// <summary>
        /// Retrieves the native window currently receiving foreground input.
        /// </summary>
        /// <returns>The foreground window handle, or zero when none is available.</returns>
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        /// <summary>
        /// Retrieves the Windows shell&apos;s desktop window.
        /// </summary>
        /// <returns>The shell window handle, or zero when the shell is unavailable.</returns>
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        /// <summary>
        /// Retrieves the native desktop window.
        /// </summary>
        /// <returns>The desktop window handle.</returns>
        [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
        /// <summary>
        /// Queries whether a native window is minimized.
        /// </summary>
        /// <param name="window">The native window handle.</param>
        /// <returns>True when the window is minimized; otherwise, false.</returns>
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        /// <summary>
        /// Queries whether a native window is maximized.
        /// </summary>
        /// <param name="window">The native window handle.</param>
        /// <returns>True when the window is maximized; otherwise, false.</returns>
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr window);
        /// <summary>
        /// Reads the requested 32-bit native window attribute.
        /// </summary>
        /// <param name="window">The native window handle.</param>
        /// <param name="index">The native window attribute index to read.</param>
        /// <returns>The value stored at the requested window attribute index.</returns>
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
        /// <summary>
        /// Reads the thread and process identifiers that own a native window.
        /// </summary>
        /// <param name="window">The native window handle.</param>
        /// <param name="processId">Receives the process identifier that owns the window.</param>
        /// <returns>The identifier of the thread that created the window.</returns>
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        /// <summary>
        /// Copies a native window&apos;s class name into the supplied buffer.
        /// </summary>
        /// <param name="window">The native window handle.</param>
        /// <param name="name">The buffer that receives the native window class name.</param>
        /// <param name="count">The maximum number of bytes or characters to process.</param>
        /// <returns>The number of characters copied, excluding the terminator, or zero on failure.</returns>
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int count);
        /// <summary>
        /// Reads the screen-space bounds of a native window.
        /// </summary>
        /// <param name="window">The native window handle.</param>
        /// <param name="rectangle">Receives the native window bounds in screen coordinates.</param>
        /// <returns>True when the window bounds were retrieved; otherwise, false.</returns>
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rectangle);
        /// <summary>
        /// Finds the monitor associated with a native window using the supplied fallback policy.
        /// </summary>
        /// <param name="window">The native window handle.</param>
        /// <param name="flags">The fallback policy when the window does not overlap a monitor.</param>
        /// <returns>The selected monitor handle, or zero if the policy permits no match.</returns>
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        /// <summary>
        /// Reads the monitor and working-area rectangles for a native monitor.
        /// </summary>
        /// <param name="monitor">The native monitor handle.</param>
        /// <param name="info">The monitor information structure, with its size initialized before the call.</param>
        /// <returns>True when monitor information was retrieved; otherwise, false.</returns>
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    }

    internal sealed class FullscreenPausePolicy
    {
        internal const int NormalPollingIntervalMilliseconds = 1000;
        internal const int PausedPollingIntervalMilliseconds = 250;
        internal const int ExitStabilityMilliseconds = 500;
        internal bool IsPaused { get; private set; }
        internal int PollingIntervalMilliseconds => IsPaused
            ? PausedPollingIntervalMilliseconds : NormalPollingIntervalMilliseconds;
        /// <summary>
        /// Checks whether neither manual pause nor automatic fullscreen suspension blocks slideshow changes.
        /// </summary>
        /// <param name="manuallyPaused">True when the user has explicitly paused the slideshow.</param>
        /// <returns>True when neither manual nor automatic pause blocks slideshow changes.</returns>
        internal bool AllowsSlideshow(bool manuallyPaused) => !IsPaused && !manuallyPaused;
        private DateTime? clearSince;

        /// <summary>
        /// Updates automatic suspension immediately on fullscreen entry and releases it after the clear-period delay.
        /// </summary>
        /// <param name="enabled">True when the user has enabled automatic fullscreen suspension.</param>
        /// <param name="fullscreen">True when the foreground application currently covers a monitor.</param>
        /// <param name="now">The current time used for the scheduling or pause decision.</param>
        /// <returns>True only when the automatic suspension state changed.</returns>
        internal bool Update(bool enabled, bool fullscreen, DateTime now)
        {
            bool previous = IsPaused;
            if (!enabled) { IsPaused = false; clearSince = null; }
            else if (fullscreen) { IsPaused = true; clearSince = null; }
            else if (IsPaused)
            {
                // Recheck at 250 ms while paused. A single transient negative sample
                // cannot resume activity; fullscreen returning resets this clear period.
                clearSince ??= now;
                if (now - clearSince.Value >= TimeSpan.FromMilliseconds(ExitStabilityMilliseconds))
                {
                    IsPaused = false;
                    clearSince = null;
                }
            }
            return previous != IsPaused;
        }
    }
}
