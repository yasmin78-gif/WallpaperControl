extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using Video = WallpaperApp::WallpaperControl.Video;

internal static class MpvControllerIntegrationTests
{
    private sealed class Session(List<string> order, string name) : App.IVideoSession
    {
        internal Task Initialization = Task.CompletedTask, Preparation = Task.CompletedTask, Presentation = Task.CompletedTask, Retirement = Task.CompletedTask;
        internal bool Published, Dead, Playing, Audible;
        internal Action<Exception>? Error;
        internal int Disposals;
        public event Action<Exception>? Failed { add => Error += value; remove => Error -= value; }
        public Task InitializeAsync(CancellationToken token) => Initialization.WaitAsync(token);
        public Task PrepareAsync(CancellationToken token) => Preparation.WaitAsync(token);
        public async Task PresentAsync(CancellationToken token) { await Presentation.WaitAsync(token); Published = true; order.Add(name + ":commit"); }
        public bool IsAttached(App.VideoShellGeneration shell) => !Dead;
        public long Position => 12000000;
        public void Play() { if (Published) Playing = true; }
        public void Pause() => Playing = false;
        public void Seek(long position) { }
        public void SetAudio(bool enabled, int volume) { Audible = Published && enabled; order.Add(name + ":audio:" + Audible); }
        public async ValueTask DisposeAsync()
        { Disposals++; Audible = false; order.Add(name + ":retire"); await Retirement; Dead = true; order.Add(name + ":released"); }
        public void Dispose() => throw new InvalidOperationException("Async session must not be synchronously disposed");
    }
    private sealed class Desktop(List<string> order) : App.IVideoDesktop
    {
        internal readonly List<Session> Sessions = new();
        internal int Peak;
        internal Action<Session> Configure = _ => { };
        internal bool NativeRecovery;
        public bool RecoverNativeFailures => NativeRecovery;
        public int MonitorCount => 1;
        public bool FileExists(string file) => true;
        public Task ValidateAsync(string file, CancellationToken token) => Task.CompletedTask;
        public App.VideoShellGeneration? ResolveShell() => new(1, 2, 3, 4, 5);
        public App.IVideoSession Create(string file, App.VideoShellGeneration shell)
        {
            var session = new Session(order, Path.GetFileNameWithoutExtension(file));
            Configure(session);
            Sessions.Add(session); Peak = Math.Max(Peak, Sessions.Count(s => !s.Dead)); return session;
        }
    }
    internal static void Run(Action<bool, string> check) => Task.Run(() => RunAsync(check)).GetAwaiter().GetResult();
    private static async Task RunAsync(Action<bool, string> check)
    {
        foreach (int volume in new[] { -1, 0, 1, 50, 100, 101 })
            check(Math.Abs(Video.MpvVideoSession.MapVolume(volume) - 100 * Math.Cbrt(Math.Clamp(volume, 0, 100) / 100d)) < 1e-8,
                $"mpv integration: linear gain mapping {volume}");
        var order = new List<string>(); var desktop = new Desktop(order);
        using var controller = new App.VideoWallpaperController(desktop, _ => { });
        controller.SetAudio(true, 50);
        check(await controller.StartAsync(@"C:\A.mp4"), "mpv integration: publishes initial async session");
        var a = desktop.Sessions[0]; var retired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        a.Retirement = retired.Task;
        Task<bool> bRequest = controller.StartAsync(@"C:\B.mp4");
        var b = desktop.Sessions[1];
        check(b.Published && !b.Audible && !bRequest.IsCompleted && !a.Dead, "mpv integration: committed B stays muted while A retires asynchronously");
        controller.SetAudio(true, 1); controller.SetPause(App.VideoPauseReason.Manual, true);
        Task<bool> cRequest = controller.StartAsync(@"C:\C.mp4");
        check(desktop.Sessions.Count == 2 && !b.Dead && !b.Audible, "mpv integration: postcommit new request cannot create third client");
        retired.SetResult(); await bRequest; await cRequest;
        check(order.IndexOf("A:released") < order.IndexOf("B:audio:True"), "mpv integration: old native retirement precedes B audio activation");
        check(desktop.Peak == 2 && a.Disposals == 1 && b.Disposals == 1, "mpv integration: async rapid requests keep max two and retire exactly once");
        check(controller.PauseReasons == App.VideoPauseReason.Manual && !desktop.Sessions[^1].Playing, "mpv integration: policy received during retirement survives newer commit");
        var c = desktop.Sessions[^1]; var lateError = c.Error;
        await controller.StopAsync(); lateError?.Invoke(new InvalidOperationException());
        check(desktop.Sessions.All(s => s.Dead && s.Disposals == 1) && controller.State == App.VideoWallpaperState.Stopped,
            "mpv integration: joined stop releases every session and ignores late native error");
        check(desktop.Sessions.All(s => s.Error == null), "mpv integration: no event subscriptions after stop");
        foreach (string phase in new[] { "initialize", "prepare", "present" })
        {
            var events = new List<string>(); var phases = new Desktop(events);
            using var engine = new App.VideoWallpaperController(phases, _ => { });
            await engine.StartAsync(@"C:\stable.mp4");
            var wait = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            phases.Configure = s =>
            {
                if (phase == "initialize") s.Initialization = wait.Task;
                if (phase == "prepare") s.Preparation = wait.Task;
                if (phase == "present") s.Presentation = wait.Task;
                s.Retirement = release.Task;
            };
            Task<bool> cancelled = engine.StartAsync(@"C:\cancelled.mp4");
            var candidate = phases.Sessions[1]; var stale = candidate.Error;
            engine.CancelPendingStart();
            phases.Configure = _ => { };
            Task<bool> newest = engine.StartAsync(@"C:\newest.mp4");
            check(phases.Sessions.Count == 2 && !newest.IsCompleted && !candidate.Published,
                $"mpv integration: {phase} cancellation waits native retirement before new candidate");
            release.SetResult(); await cancelled; await newest; wait.TrySetResult(); stale?.Invoke(new Exception());
            check(engine.ActivePath == @"C:\newest.mp4" && candidate.Dead && candidate.Disposals == 1 && phases.Peak == 2,
                $"mpv integration: {phase} stale work/error cannot publish or create third session");
            await engine.StopAsync();
            check(phases.Sessions.All(s => s.Dead && s.Error == null), $"mpv integration: {phase} cleanup joins all async ownership");
        }
        foreach (string failure in new[] { "load timeout", "prepare timeout", "native shutdown during preparation" })
        {
            var failures = new Desktop(new());
            using var engine = new App.VideoWallpaperController(failures, _ => { });
            await engine.StartAsync(@"C:\stable.mp4");
            var stable = failures.Sessions[0];
            var preparation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            failures.Configure = s =>
            {
                if (failure == "load timeout") s.Initialization = Task.FromException(new TimeoutException());
                else if (failure == "prepare timeout") s.Preparation = Task.FromException(new TimeoutException());
                else s.Preparation = preparation.Task;
            };
            Task<bool> request = engine.StartAsync(@"C:\failed.mp4");
            var candidate = failures.Sessions[1];
            if (failure == "native shutdown during preparation")
            { candidate.Error?.Invoke(new IOException("native shutdown")); preparation.SetResult(); }
            check(!await request && engine.HasSession && engine.ActivePath == @"C:\stable.mp4" && !stable.Dead,
                $"mpv integration: {failure} retains stable published player");
            check(candidate.Dead && candidate.Disposals == 1 && candidate.Error == null && failures.Peak == 2,
                $"mpv integration: {failure} releases candidate and event ownership exactly once");
            await engine.StopAsync();
        }
        {
            var time = DateTime.UtcNow; var recovery = new Desktop(new()) { NativeRecovery = true };
            using var engine = new App.VideoWallpaperController(recovery, _ => { }, () => time);
            engine.SetPause(App.VideoPauseReason.Manual | App.VideoPauseReason.Fullscreen, true);
            await engine.StartAsync(@"C:\recovery.mp4"); recovery.Sessions[0].Error?.Invoke(new IOException("native shutdown"));
            check(engine.State == App.VideoWallpaperState.Recovering && recovery.Sessions[0].Dead,
                "mpv integration: published native shutdown starts one bounded recovery incident");
            recovery.Configure = s => s.Initialization = Task.FromException(new IOException("output unavailable"));
            foreach (int seconds in new[] { 1, 3, 6 }) { time = time.AddSeconds(seconds); await engine.TickAsync(); }
            check(engine.State == App.VideoWallpaperState.Failed && engine.RecoveryAttempts == 3 &&
                engine.PauseReasons == (App.VideoPauseReason.Manual | App.VideoPauseReason.Fullscreen),
                "mpv integration: repeated output failures exhaust three attempts without resetting independent pause reasons");
            int created = recovery.Sessions.Count;
            for (int i = 0; i < 10; i++) { time = time.AddMinutes(1); await engine.TickAsync(); }
            check(recovery.Sessions.Count == created && recovery.Sessions.All(s=>s.Dead),
                "mpv integration: exhausted native incident never loops or retains players");
            await engine.StopAsync();
        }
    }
}
