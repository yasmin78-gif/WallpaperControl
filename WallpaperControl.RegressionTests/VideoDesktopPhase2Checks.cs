extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using Video = WallpaperApp::WallpaperControl.Video;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class VideoDesktopPhase2Checks
{
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    private sealed class Session(Video.MediaFoundationVideoSession inner, Task gate) : App.IVideoSession
    {
        internal readonly nint Hwnd = (nint)typeof(Video.MediaFoundationVideoSession).GetField("rendererHandle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(inner)!;
        internal readonly Video.MfPlayer Player = (Video.MfPlayer)typeof(Video.MediaFoundationVideoSession).GetField("player", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(inner)!;
        internal int Disposals, Subscribers;
        internal bool InitiallyHidden => !IsWindowVisible(Hwnd);
        public event Action<Exception>? Failed
        {
            add { inner.Failed += value; Subscribers++; }
            remove { inner.Failed -= value; Subscribers--; }
        }
        public Task InitializeAsync(CancellationToken token) => inner.InitializeAsync(token);
        public async Task PrepareAsync(CancellationToken token) { await gate.WaitAsync(token); await inner.PrepareAsync(token); }
        public void Present() => inner.Present();
        public bool IsAttached(App.VideoShellGeneration shell) => inner.IsAttached(shell);
        public long Position => inner.Position;
        public void Play() => inner.Play();
        public void Pause() => inner.Pause();
        public void Seek(long position) => inner.Seek(position);
        public void Dispose() { Disposals++; inner.Dispose(); }
    }
    private sealed class Desktop(Control dispatcher) : App.IVideoDesktop
    {
        private readonly Video.MediaFoundationVideoDesktop native = new(dispatcher);
        internal Task Gate = Task.CompletedTask;
        internal readonly List<Session> Sessions = new();
        public int MonitorCount => native.MonitorCount;
        public bool FileExists(string path) => native.FileExists(path);
        public Task ValidateAsync(string path, CancellationToken token) => native.ValidateAsync(path, token);
        public App.VideoShellGeneration? ResolveShell() => native.ResolveShell();
        public App.IVideoSession Create(string path, App.VideoShellGeneration shell)
        {
            Console.WriteLine($"Native candidate thread={Environment.CurrentManagedThreadId}; apartment={Thread.CurrentThread.GetApartmentState()}; context={SynchronizationContext.Current?.GetType().Name ?? "none"}");
            var session = new Session(new(dispatcher, path, shell), Gate);
            Sessions.Add(session); return session;
        }
    }
    private static void Pump(int milliseconds)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(2); }
    }
    private static void Wait(Func<bool> complete)
    {
        var clock = Stopwatch.StartNew();
        while (!complete()) { if (clock.Elapsed.TotalSeconds > 50) throw new TimeoutException("Desktop Phase 2 check"); Pump(5); }
    }
    private static bool Start(Task<bool> task) { Wait(() => task.IsCompleted); return task.GetAwaiter().GetResult(); }
    private static Color Pixel(Point point)
    {
        DwmFlush();
        using var image = new Bitmap(1, 1);
        using (var graphics = Graphics.FromImage(image)) graphics.CopyFromScreen(point, Point.Empty, image.Size);
        return image.GetPixel(0, 0);
    }
    private static bool Blue(Color pixel) => pixel.B > 200 && pixel.R < 40 && pixel.G < 40;
    private static bool Green(Color pixel) => pixel.G > 200 && pixel.R < 40 && pixel.B < 40;
    internal static void Run(string fixtures, Action<bool, string> check)
    {
        string first = Path.Combine(fixtures, "loop-colors.mp4"), second = Path.Combine(fixtures, "switch-colors.mp4");
        using var dispatcher = new Video.VideoRenderForm { Location = new(-20000, -20000), Size = new(100, 100) };
        dispatcher.Show();
        var desktop = new Desktop(dispatcher);
        using var controller = new App.VideoWallpaperController(desktop, message => Console.WriteLine("Native Phase 2 controller: " + message));
        check(Start(controller.StartAsync(first)), "Native Phase 2 desktop: controller publishes a real first-frame-ready session");
        controller.SetPause(App.VideoPauseReason.Manual, true); controller.SetPause(App.VideoPauseReason.Fullscreen, true); Pump(50);
        Point? exposed = null;
        var bounds = Screen.AllScreens[0].Bounds;
        for (int y = bounds.Top + 40; y < bounds.Bottom - 60 && exposed == null; y += 120)
            for (int x = bounds.Left + 40; x < bounds.Right - 40; x += 120)
                if (Blue(Pixel(new(x, y)))) { exposed = new(x, y); break; }
        check(exposed != null, "Native Phase 2 desktop: published first frame is actually visible");
        if (exposed == null) throw new InvalidOperationException("Uncover part of desktop before running native checks");
        var stable = desktop.Sessions[0];
        foreach (string invalid in new[] { "corrupt.mp4", "unsupported.mp4", "missing-phase2.mp4" })
            check(!Start(controller.StartAsync(Path.Combine(fixtures, invalid))) && stable.Disposals == 0 && Blue(Pixel(exposed.Value)),
                $"Native Phase 2 desktop: {invalid} preserves old visible paused frame");
        string locked = Path.Combine(fixtures, "locked-desktop-" + Guid.NewGuid().ToString("N") + ".mp4");
        File.Copy(first, locked);
        using (File.Open(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            check(!Start(controller.StartAsync(locked)) && stable.Disposals == 0 && Blue(Pixel(exposed.Value)),
                "Native Phase 2 desktop: unreadable file preserves the actual old surface");
        check(!Start(controller.StartAsync(locked, () => { File.Delete(locked); return Task.CompletedTask; })) &&
            stable.Disposals == 0 && Blue(Pixel(exposed.Value)), "Native Phase 2 desktop: disappearing file during native startup preserves old player");
        check(Start(controller.StartAsync(second)) && controller.PauseReasons == (App.VideoPauseReason.Manual | App.VideoPauseReason.Fullscreen),
            "Native Phase 2 desktop: real Video A to B preserves both pause reasons");
        Pump(50);
        check(Green(Pixel(exposed.Value)) && !Video.VideoNative.IsWindow(stable.Hwnd) && !stable.Player.HasPresentedFrame,
            "Native Phase 2 desktop: new valid green frame replaces old blue frame and releases its HWND/EVR");
        var previous = desktop.Sessions[^1];
        check(Start(controller.StartAsync(second)), "Native Phase 2 desktop: reapply of the same MP4 succeeds");
        Pump(50);
        check(Green(Pixel(exposed.Value)) && previous.Disposals == 1 && !Video.VideoNative.IsWindow(previous.Hwnd),
            "Native Phase 2 desktop: reapply releases previous native renderer");
        controller.SetPause(App.VideoPauseReason.Manual, false);
        long position = desktop.Sessions[^1].Position; Pump(150);
        check(Math.Abs(desktop.Sessions[^1].Position - position) < 500000 && Green(Pixel(exposed.Value)),
            "Native Phase 2 desktop: clearing Manual alone leaves Fullscreen pause active");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        desktop.Gate = gate.Task;
        var starts = new List<Task<bool>>();
        for (int i = 0; i < 6; i++)
        {
            int count = desktop.Sessions.Count;
            starts.Add(controller.StartAsync(i % 2 == 0 ? first : second));
            Wait(() => desktop.Sessions.Count > count || starts[^1].IsCompleted);
            if (i == 0)
                check(desktop.Sessions[^1].InitiallyHidden && Green(Pixel(exposed.Value)),
                    "Native Phase 2 desktop: candidate stays hidden while waiting for first-frame preparation");
        }
        gate.SetResult(); Wait(() => starts.All(s => s.IsCompleted));
        foreach (var task in starts) task.GetAwaiter().GetResult();
        Pump(50);
        check(starts.Count(s => s.Result) == 1 && Green(Pixel(exposed.Value)) && desktop.Sessions.Count(s => s.Disposals == 0) == 1,
            "Native Phase 2 desktop: six rapid real-MF switches publish only the newest session");
        check(desktop.Sessions.Take(desktop.Sessions.Count - 1).All(s => s.Disposals == 1 && !Video.VideoNative.IsWindow(s.Hwnd) && !s.Player.HasPresentedFrame),
            "Native Phase 2 desktop: cancelled native candidates leave no renderer HWND or EVR sample");
        controller.SetPause(App.VideoPauseReason.Fullscreen, false);
        position = desktop.Sessions[^1].Position; Pump(150);
        check(desktop.Sessions[^1].Position > position && controller.PauseReasons == App.VideoPauseReason.None,
            "Native Phase 2 desktop: final pause reason removal resumes the latest player");
        controller.Stop();
        check(desktop.Sessions.All(s => s.Disposals == 1 && s.Subscribers == 0 && !Video.VideoNative.IsWindow(s.Hwnd) && !s.Player.HasPresentedFrame),
            "Native Phase 2 desktop: Stop releases every published and staged MF session");
    }
}
