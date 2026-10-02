using System.Runtime.InteropServices;
using System.Text;

namespace WallpaperControl.Video;

internal sealed class MpvVideoDesktop(Control dispatcher, MpvRuntime runtime) : IVideoDesktop
{
    private readonly MediaFoundationVideoDesktop validation = new(dispatcher);
    private VideoShellGeneration? composedShell;
    public string BackendName => "libmpv/D3D11";
    public bool RecoverNativeFailures => true;
    public int MonitorCount => validation.MonitorCount;
    public bool FileExists(string path) => validation.FileExists(path);
    public Task ValidateAsync(string path, CancellationToken cancellation) => validation.ValidateAsync(path, cancellation);
    public VideoShellGeneration? ResolveShell()
    {
        var shell = VideoNative.Resolve();
        if (shell == null || shell == composedShell) return shell;
        if (!VideoNative.EnsureRaisedComposition(shell.Value)) return null;
        composedShell = VideoNative.Resolve();
        return composedShell;
    }
    public IVideoSession Create(string path, VideoShellGeneration shell) => new MpvVideoSession(dispatcher, runtime, path, shell);
}

// Owns only WallpaperControl's windows. All methods run on their creating UI thread.
internal sealed class MpvVideoHost : IDisposable
{
    private readonly VideoRenderForm render;
    private readonly VideoShellGeneration shell;
    private readonly Rectangle bounds;
    internal nint Handle { get; }
    internal nint Child { get; private set; }
    internal MpvVideoHost(VideoShellGeneration shell)
    {
        this.shell = shell; bounds = Screen.AllScreens[0].Bounds;
        var previous = VideoNative.SetThreadDpiAwarenessContext(VideoNative.GetWindowDpiAwarenessContext(shell.Host));
        render = new();
        try
        {
            if (VideoNative.Resolve() != shell) throw new InvalidOperationException("Shell lease changed");
            Handle = render.Handle;
            VideoNative.SetStyle(Handle, -16, (VideoNative.GetStyle(Handle, -16) & ~unchecked((int)0x80000000)) | 0x40000000);
            VideoNative.SetParent(Handle, shell.Host);
            VideoNative.ConfigureDesktopComposition(Handle, shell.DefView);
            Place(false);
        }
        catch { render.Dispose(); throw; }
        finally { if (previous != 0) VideoNative.SetThreadDpiAwarenessContext(previous); }
    }
    internal void FindChild()
    {
        VideoNative.EnumChildWindows(Handle, (hwnd, _) =>
        {
            var name = new StringBuilder(256); GetClassName(hwnd, name, name.Capacity);
            if (name.ToString() == "mpv" && VideoNative.GetParent(hwnd) == Handle) Child = hwnd;
            return true;
        }, 0);
        EnableWindow(Handle, false);
        if (Child != 0) EnableWindow(Child, false);
    }
    internal bool Attached(VideoShellGeneration current) => current == shell && !render.IsDisposed &&
        render.RendererHandle == Handle && VideoNative.IsWindow(Handle) && VideoNative.GetParent(Handle) == shell.Host && Child != 0 &&
        VideoNative.IsWindow(Child) && VideoNative.GetParent(Child) == Handle && Screen.AllScreens.Length == 1 && Screen.AllScreens[0].Bounds == bounds;
    private void Place(bool show)
    {
        var origin = bounds.Location;
        if (!VideoNative.ScreenToClient(shell.Host, ref origin) ||
            !VideoNative.SetWindowPos(Handle, shell.DefView, origin.X, origin.Y, bounds.Width, bounds.Height, 0x10 | 0x20 | (show ? 0x40u : 0)))
            throw new InvalidOperationException("Own video host placement failed");
    }
    internal void Present() { render.Show(); Place(true); }
    internal void Hide() { if (!render.IsDisposed) render.Hide(); }
    public void Dispose() => render.Dispose();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern bool EnableWindow(nint hwnd, bool enabled);
}
