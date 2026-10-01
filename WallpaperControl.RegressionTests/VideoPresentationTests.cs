extern alias WallpaperApp;
using Video = WallpaperApp::WallpaperControl.Video;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class VideoPresentationTests
{
    [DllImport("user32.dll")] private static extern bool GetLayeredWindowAttributes(nint hwnd, out uint color, out byte alpha, out uint flags);
    internal static void Run(Action<bool, string> check)
    {
        var gate = new Video.MfLoopFrameGate();
        check(gate.CanRepaint(false), "Video loop: ordinary pause/resume repaint remains available");
        gate.BeginLoop();
        check(gate.Holding && !gate.CanRepaint(false) && !gate.NeedsFrameCheck, "Video loop: EOS holds last surface before seek");
        check(!gate.CanRepaint(true), "Video loop: stale pre-seek sample cannot release held surface");
        gate.PositionSet();
        check(gate.NeedsFrameCheck && !gate.CanRepaint(false), "Video loop: seek acknowledgement without frame keeps last surface");
        check(!gate.CanRepaint(false), "Video loop: delayed first frame has no timer/Play-ack fallback");
        check(gate.CanRepaint(true) && !gate.Holding, "Video loop: actual composited frame releases hold, including legitimate black content");
        gate.BeginLoop();
        check(!gate.CanRepaint(true), "Video loop: every loop requires its own seek and new frame");
        gate.PositionSet();
        check(gate.CanRepaint(true), "Video loop: next valid frame releases next loop");
        foreach (uint original in new uint[] { 0, 1, 0xff, 0x100, 0x1ff })
            check(Video.MfVideoRenderer.PreserveLastFrame(original) == (original | 0x100),
                $"Video presentation: retain frame without changing other EVR preferences {original:X}");
        var wide = Video.MfVideoRenderer.FillSource(new(1920, 1080), new(16, 9), new(1000, 1000));
        check(Math.Abs(wide.Left - 0.21875f) < 0.00001 && Math.Abs(wide.Right - 0.78125f) < 0.00001 && wide.Top == 0 && wide.Bottom == 1,
            "Video presentation: wide source retains centered horizontal Fill crop");
        var tall = Video.MfVideoRenderer.FillSource(new(1080, 1920), Size.Empty, new(1920, 1080));
        check(tall.Left == 0 && tall.Right == 1 && Math.Abs(tall.Top - 0.341796875f) < 0.00001 && Math.Abs(tall.Bottom - 0.658203125f) < 0.00001,
            "Video presentation: tall source retains centered vertical Fill crop");
        var anamorphic = Video.MfVideoRenderer.FillSource(new(720, 576), new(16, 9), new(1920, 1080));
        check(anamorphic.Left == 0 && anamorphic.Top == 0 && anamorphic.Right == 1 && anamorphic.Bottom == 1,
            "Video presentation: Fill uses display aspect ratio");
        bool rejected = false;
        try { Video.MfVideoRenderer.FillSource(Size.Empty, Size.Empty, new(100, 100)); }
        catch (InvalidOperationException) { rejected = true; }
        check(rejected, "Video presentation: unavailable dimensions remain rejected");
        using Task task = new(() => Window(check));
        Thread thread = new(() => task.RunSynchronously(TaskScheduler.Default));
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); task.GetAwaiter().GetResult();
    }
    private static void Window(Action<bool, string> check)
    {
        using var render = new Video.VideoRenderForm();
        using var bitmap = new Bitmap(16, 16);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Magenta);
        typeof(Video.VideoRenderForm).GetMethod("OnPaintBackground", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(render, new object[] { new PaintEventArgs(graphics, new(0, 0, 16, 16)) });
        check(bitmap.GetPixel(8, 8).ToArgb() == Color.Magenta.ToArgb(), "Video presentation: managed background paint cannot erase retained pixels");
        object[] args = { Message.Create(render.Handle, 0x14, 0, 0) };
        var wndProc = typeof(Video.VideoRenderForm).GetMethod("WndProc", BindingFlags.Instance | BindingFlags.NonPublic)!;
        wndProc.Invoke(render, args);
        check(((Message)args[0]).Result == 1, "Video presentation: native WM_ERASEBKGND suppressed");
        args[0] = Message.Create(render.Handle, 0x84, 0, 0); wndProc.Invoke(render, args);
        check(((Message)args[0]).Result == -1 && !render.Enabled && !render.ShowInTaskbar && !render.TopMost,
            "Video presentation: renderer remains disabled and click-through");
        args[0] = Message.Create(render.Handle, 0x21, 0, 0); wndProc.Invoke(render, args);
        check(((Message)args[0]).Result == 3, "Video presentation: renderer still refuses mouse activation");
        using var desktop = new Form();
        Video.VideoNative.SetParent(render.Handle, desktop.Handle);
        int original = Video.VideoNative.GetStyle(render.Handle, -20);
        Video.VideoNative.ConfigureDesktopComposition(render.Handle, desktop.Handle);
        check(Video.VideoNative.GetStyle(render.Handle, -20) == original,
            "Video desktop: traditional shell retains original composition");
        desktop.Opacity = 0.99;
        Video.VideoNative.ConfigureDesktopComposition(render.Handle, desktop.Handle);
        check(Video.VideoNative.GetStyle(render.Handle, -20) == (original | 0x80000),
            "Video desktop: layered shell receives composited child while preserving other styles");
        check(GetLayeredWindowAttributes(render.Handle, out _, out var alpha, out var flags) && alpha == 255 && flags == 2,
            "Video desktop: composited renderer is fully opaque without a color key");
        Video.VideoNative.SetParent(render.Handle, 0);
    }
}
