extern alias WallpaperApp;
using Video = WallpaperApp::WallpaperControl.Video;
using System.Runtime.InteropServices;

// Explicit interactive test: briefly attaches its own renderer to the real
// desktop and removes only that renderer. No registry/wallpaper settings writes.
internal static class VideoDesktopSmokeTests
{
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    internal static void Run(string fixtures, Action<bool, string> check)
    {
        using Task task = new(() => Desktop(Path.Combine(fixtures, "loop-colors.mp4"), check));
        var thread = new Thread(() => task.RunSynchronously(TaskScheduler.Default));
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); task.GetAwaiter().GetResult();
    }
    private static void Pump(int milliseconds)
    {
        var end = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < end) { Application.DoEvents(); Thread.Sleep(2); }
    }
    private static Color Pixel(Point point)
    {
        DwmFlush();
        using var bitmap = new Bitmap(1, 1);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(point, Point.Empty, bitmap.Size);
        return bitmap.GetPixel(0, 0);
    }
    private static bool Blue(Color pixel) => pixel.B > 200 && pixel.R < 40 && pixel.G < 40;
    private static bool Red(Color pixel) => pixel.R > 200 && pixel.B < 40 && pixel.G < 40;
    private static void Desktop(string file, Action<bool, string> check)
    {
        if (Screen.AllScreens.Length != 1) throw new InvalidOperationException("Desktop video check requires one monitor");
        var shell = Video.VideoNative.Resolve() ?? throw new InvalidOperationException("Desktop shell unavailable");
        using var dispatcher = new Video.VideoRenderForm { Location = new(-20000, -20000), Size = new(100, 100) };
        dispatcher.Show();
        Point? exposed = null;
        // Two independent sessions cover persisted startup and an Apply/restart.
        for (int run = 0; run < 2; run++)
        {
            using var session = new Video.MediaFoundationVideoSession(dispatcher, file, shell);
            Exception? failure = null;
            session.Failed += error => failure = error;
            var ready = session.InitializeAsync(default);
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!ready.IsCompleted && DateTime.UtcNow < deadline) Pump(10);
            ready.GetAwaiter().GetResult();
            session.Play(); Pump(300);
            check(session.IsAttached(shell) && failure == null, $"Desktop video {run}: production session attaches and initializes");
            if (exposed == null)
            {
                var bounds = Screen.AllScreens[0].Bounds;
                for (int y = bounds.Top + 40; y < bounds.Bottom - 60 && exposed == null; y += 120)
                    for (int x = bounds.Left + 40; x < bounds.Right - 40; x += 120)
                        if (Blue(Pixel(new(x, y)))) { exposed = new(x, y); break; }
            }
            check(exposed != null && Blue(Pixel(exposed.Value)), $"Desktop video {run}: first frame actually visible behind desktop icons");
            if (exposed == null) throw new InvalidOperationException("No exposed blue video pixel; uncover part of desktop and rerun");
            Pump(100); session.Pause(); Pump(150);
            long paused = session.Position;
            Pump(150);
            check(Math.Abs(session.Position - paused) < 500000 && Blue(Pixel(exposed.Value)),
                $"Desktop video {run}: pause retains visible frame and clock");
            session.Play();
            bool redSeen = false, blueAfterRed = false, nonBlack = true;
            deadline = DateTime.UtcNow.AddSeconds(7);
            while (DateTime.UtcNow < deadline)
            {
                Pump(20);
                Color pixel = Pixel(exposed.Value);
                nonBlack &= Blue(pixel) || Red(pixel);
                if (Red(pixel)) redSeen = true;
                if (redSeen && Blue(pixel)) blueAfterRed = true;
            }
            check(redSeen && blueAfterRed && failure == null, $"Desktop video {run}: resume and automatic loops visibly present new frames");
            check(nonBlack, $"Desktop video {run}: visible desktop surface never turns black across loops");
        }
    }
}
