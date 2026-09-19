using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WallpaperControl
{
    /// <summary>
    /// Detects Windows Show Desktop by observing the shell's actual Z-order state.
    /// A foreground hook provides a fast path, while a lightweight timer covers cases
    /// where the desktop already owns the foreground and Win+D emits no useful change.
    /// </summary>
    internal sealed class DesktopShowMonitor : IDisposable
    {
        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int WS_DISABLED = 0x08000000;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_NOOWNERZORDER = 0x0200;
        private const uint SWP_NOSENDCHANGING = 0x0400;
        private const int NormalCheckInterval = 250;
        private const int ShowDesktopCheckInterval = 100;
        private const int RepairInterval = 50;
        private const int RepairAttempts = 5;

        private readonly Action repairDesktopBand;
        private readonly WinEventDelegate winEventDelegate;
        private readonly System.Windows.Forms.Timer stateTimer;
        private readonly System.Windows.Forms.Timer repairTimer;
        private readonly DesktopStateWindow stateWindow;
        private IntPtr hook;
        private int attemptsRemaining;
        private bool showDesktop;
        private bool repairRequiresShowDesktop;
        private bool disposed;

        public DesktopShowMonitor(Action repairDesktopBand)
        {
            this.repairDesktopBand = repairDesktopBand ?? throw new ArgumentNullException(nameof(repairDesktopBand));
            winEventDelegate = OnWinEvent;

            stateWindow = new DesktopStateWindow();
            SetWindowPos(stateWindow.Handle, new IntPtr(1), 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_NOSENDCHANGING);

            stateTimer = new System.Windows.Forms.Timer { Interval = NormalCheckInterval };
            stateTimer.Tick += StateTimer_Tick;
            stateTimer.Start();

            repairTimer = new System.Windows.Forms.Timer { Interval = RepairInterval };
            repairTimer.Tick += RepairTimer_Tick;

            hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
                winEventDelegate, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

            if (hook == IntPtr.Zero)
            {
                AppLogger.Warning("Show Desktop foreground monitoring could not be started; Z-order state monitoring remains active.",
                    new InvalidOperationException("SetWinEventHook returned zero."));
            }
        }

        private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject,
            int idChild, uint idEventThread, uint eventTime)
        {
            if (disposed || hwnd == IntPtr.Zero)
                return;

            IntPtr shellWindow = GetShellWindow();
            if (shellWindow != IntPtr.Zero && hwnd == shellWindow)
            {
                // Fast path: Explorer has taken the foreground. Repair immediately instead of
                // waiting for the periodic Z-order state check. The state monitor below remains
                // the fallback for Win+D when the desktop already had the foreground.
                BeginRepairBurst(requireShowDesktop: false);
                CheckDesktopState();
            }
        }

        private void StateTimer_Tick(object? sender, EventArgs e)
        {
            CheckDesktopState();
        }

        /// <summary>
        /// Determines Show Desktop from the real top-level Z-order, not merely foreground focus.
        /// The hidden state window is kept at the bottom during normal operation. When Explorer
        /// performs Show Desktop, Progman moves behind it, which makes the state observable even
        /// when Progman was already the foreground window before Win+D.
        /// </summary>
        private void CheckDesktopState()
        {
            if (disposed || stateWindow.Handle == IntPtr.Zero)
                return;

            IntPtr shellWindow = GetShellWindow();
            if (shellWindow == IntPtr.Zero || !IsWindowVisible(shellWindow))
                return;

            bool currentShowDesktop = IsWindowAfter(shellWindow, stateWindow.Handle);
            if (currentShowDesktop == showDesktop)
                return;

            showDesktop = currentShowDesktop;
            stateTimer.Interval = showDesktop ? ShowDesktopCheckInterval : NormalCheckInterval;

            if (showDesktop)
                BeginRepairBurst(requireShowDesktop: true);
        }

        private static bool IsWindowAfter(IntPtr startWindow, IntPtr targetWindow)
        {
            IntPtr current = startWindow;
            while ((current = GetWindow(current, 2)) != IntPtr.Zero) // GW_HWNDNEXT
            {
                if (current == targetWindow)
                    return true;
            }

            return false;
        }

        private void BeginRepairBurst(bool requireShowDesktop)
        {
            repairRequiresShowDesktop = requireShowDesktop;
            attemptsRemaining = RepairAttempts;
            repairDesktopBand();
            attemptsRemaining--;

            if (attemptsRemaining > 0 && !repairTimer.Enabled)
                repairTimer.Start();
        }

        private void RepairTimer_Tick(object? sender, EventArgs e)
        {
            if (disposed || attemptsRemaining <= 0 || (repairRequiresShowDesktop && !showDesktop))
            {
                repairTimer.Stop();
                return;
            }

            repairDesktopBand();
            attemptsRemaining--;

            if (attemptsRemaining <= 0)
                repairTimer.Stop();
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            stateTimer.Stop();
            stateTimer.Dispose();
            repairTimer.Stop();
            repairTimer.Dispose();

            if (hook != IntPtr.Zero)
            {
                UnhookWinEvent(hook);
                hook = IntPtr.Zero;
            }

            stateWindow.Dispose();
        }

        private sealed class DesktopStateWindow : NativeWindow, IDisposable
        {
            public DesktopStateWindow()
            {
                CreateHandle(new CreateParams
                {
                    Caption = "WallpaperControl.DesktopState",
                    Style = WS_POPUP | WS_DISABLED,
                    ExStyle = WS_EX_TOOLWINDOW,
                    X = -32000,
                    Y = -32000,
                    Width = 1,
                    Height = 1
                });
            }

            public void Dispose()
            {
                if (Handle != IntPtr.Zero)
                    DestroyHandle();
            }
        }

        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint idEventThread, uint eventTime);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y,
            int cx, int cy, uint uFlags);
    }
}
