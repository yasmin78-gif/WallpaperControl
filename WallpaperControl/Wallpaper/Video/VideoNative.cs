using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WallpaperControl.Video;

internal static class VideoNative
{
    internal delegate bool EnumProc(nint hwnd, nint data);
    [DllImport("user32.dll")] internal static extern nint GetShellWindow();
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback, nint data);
    [DllImport("user32.dll")] internal static extern bool EnumChildWindows(nint parent, EnumProc callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder name, int size);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] internal static extern nint GetParent(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] internal static extern int GetStyle(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)] internal static extern int SetStyle(nint hwnd, int index, int value);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetParent(nint hwnd, nint parent);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetLayeredWindowAttributes(nint hwnd, uint color, byte alpha, uint flags);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern bool ScreenToClient(nint hwnd, ref Point point);
    [DllImport("user32.dll")] internal static extern nint GetWindowDpiAwarenessContext(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SendMessageTimeout(nint hwnd, uint message, nint wParam, nint lParam, uint flags, uint timeout, out nint result);
    private static string Class(nint hwnd) { var text = new StringBuilder(256); GetClassName(hwnd, text, text.Capacity); return text.ToString(); }

    internal static bool EnsureRaisedComposition(VideoShellGeneration shell)
    {
        // The same Explorer request used by the existing image host. A newly
        // restarted shell can have valid layered DefView HWNDs while its desktop
        // background still occludes sibling renderers. Request composition once
        // per shell generation, never in response to mere occlusion/Win+D.
        if (Resolve() != shell) return false;
        return SendMessageTimeout(shell.Shell, 0x052C, 0xD, 1, 2, 1000, out _) != 0;
    }

    internal static void ConfigureDesktopComposition(nint renderer, nint defView)
    {
        // Explorer's raised desktop uses a layered DefView. Ordinary children
        // behind it can decode/present successfully while DWM never displays
        // their pixels. Match that composition path at full opacity, as the
        // image wallpaper host does; leave traditional desktops unchanged.
        const int layered = 0x80000;
        if ((GetStyle(defView, -20) & layered) == 0) return;
        SetStyle(renderer, -20, GetStyle(renderer, -20) | layered);
        if ((GetStyle(renderer, -20) & layered) == 0 || !SetLayeredWindowAttributes(renderer, 0, 255, 2))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Video desktop composition failed");
    }

    internal static VideoShellGeneration? Resolve()
    {
        // GetShellWindow identifies the active shell; class names only refine its
        // own process hierarchy. Start time defeats PID/HWND reuse after restart.
        nint shell = GetShellWindow();
        if (shell == 0 || !IsWindow(shell)) return null;
        GetWindowThreadProcessId(shell, out uint pid);
        long start;
        try { using var process = Process.GetProcessById((int)pid); start = process.StartTime.ToUniversalTime().Ticks; }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (System.ComponentModel.Win32Exception) { return null; }
        VideoShellGeneration? result = null;
        EnumWindows((host, _) =>
        {
            GetWindowThreadProcessId(host, out uint owner);
            if (owner != pid || Class(host) is not ("Progman" or "WorkerW")) return true;
            EnumChildWindows(host, (child, _) =>
            {
                if (GetParent(child) != host || Class(child) != "SHELLDLL_DefView") return true;
                GetWindowThreadProcessId(child, out uint childOwner);
                bool icons = false;
                EnumChildWindows(child, (icon, _) => { icons |= GetParent(icon) == child && Class(icon) == "SysListView32"; return true; }, 0);
                if (childOwner == pid && icons) result = new(shell, host, child, pid, start);
                return true;
            }, 0);
            return result == null;
        }, 0);
        return result;
    }
}

internal sealed class VideoRenderForm : Form
{
    internal nint RendererHandle { get; private set; }
    internal VideoRenderForm()
    {
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
        Enabled = false; BackColor = Color.Black; StartPosition = FormStartPosition.Manual;
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    { get { var value = base.CreateParams; value.ExStyle |= 0x08000080; return value; } }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); RendererHandle = Handle; }
    // EVR owns the pixels. Erasing its window between Stop/seek and the next
    // sample would defeat the presenter's retained surface, including WM_PAINT
    // while paused or temporarily exposed by Show Desktop.
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x14) { message.Result = 1; return; } // WM_ERASEBKGND
        if (message.Msg == 0x84) { message.Result = -1; return; }
        if (message.Msg == 0x21) { message.Result = 3; return; }
        base.WndProc(ref message);
    }
}
