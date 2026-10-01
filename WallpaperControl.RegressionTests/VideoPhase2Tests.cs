extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using System.Runtime.InteropServices;

internal static class VideoPhase2Tests
{
    private sealed class Session : App.IVideoSession
    {
        internal Task Initialization = Task.CompletedTask, Preparation = Task.CompletedTask;
        internal bool IgnoreCancellation, Playing, Presented, Attached = true, PlayFails, PresentFails;
        internal int Disposals, Subscribers, Plays;
        internal long Value;
        private Action<Exception>? error;
        public event Action<Exception>? Failed
        {
            add { error += value; Subscribers++; }
            remove { error -= value; Subscribers--; }
        }
        internal Action<Exception>? CapturedError => error;
        internal void Error() => error?.Invoke(new COMException("decoder error", unchecked((int)0xC00D36B4)));
        public Task InitializeAsync(CancellationToken token) => IgnoreCancellation ? Initialization : Initialization.WaitAsync(token);
        public Task PrepareAsync(CancellationToken token) => IgnoreCancellation ? Preparation : Preparation.WaitAsync(token);
        public void Present() { if (PresentFails) throw new COMException(); Presented = true; }
        public bool IsAttached(App.VideoShellGeneration shell) => Attached && Disposals == 0;
        public long Position => Value;
        public void Seek(long position) => Value = position;
        public void Play() { if (PlayFails) throw new COMException(); Playing = true; Plays++; }
        public void Pause() => Playing = false;
        public void Dispose() { Disposals++; Playing = Presented = false; }
    }
    private sealed class Desktop : App.IVideoDesktop
    {
        public int MonitorCount => 1;
        internal bool Exists = true;
        internal Func<string, CancellationToken, Task> Validate = (_, _) => Task.CompletedTask;
        internal Func<Session> Next = () => new();
        internal readonly List<Session> Sessions = new();
        internal int MaximumLive;
        internal App.VideoShellGeneration Shell = new(1, 2, 3, 4, 5);
        public bool FileExists(string path) => Exists;
        public Task ValidateAsync(string path, CancellationToken token) => Validate(path, token);
        public App.VideoShellGeneration? ResolveShell() => Shell;
        public App.IVideoSession Create(string path, App.VideoShellGeneration shell)
        {
            var value = Next(); Sessions.Add(value);
            MaximumLive = Math.Max(MaximumLive, Sessions.Count(s => s.Disposals == 0));
            return value;
        }
    }
    private static TaskCompletionSource Pending() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal static void Run(Action<bool, string> check) => Task.Run(() => RunAsync(check)).GetAwaiter().GetResult();
    private static async Task RunAsync(Action<bool, string> check)
    {
        foreach (string failure in new[] { "corrupt", "codec", "missing", "locked", "MF initialization", "MF first frame", "MF Play", "MF Present" })
        {
            var desktop = new Desktop();
            var messages = new List<string>();
            using var controller = new App.VideoWallpaperController(desktop, messages.Add);
            await controller.StartAsync(@"C:\A.mp4");
            var old = desktop.Sessions[0]; old.Value = 123456;
            if (failure == "corrupt") desktop.Validate = (_, _) => Task.FromException(new InvalidDataException());
            if (failure == "codec") desktop.Validate = (_, _) => Task.FromException(new NotSupportedException());
            if (failure == "locked") desktop.Validate = (_, _) => Task.FromException(new IOException());
            if (failure == "missing") desktop.Exists = false;
            if (failure == "MF initialization") desktop.Next = () => new() { Initialization = Task.FromException(new COMException()) };
            if (failure == "MF first frame") desktop.Next = () => new() { Preparation = Task.FromException(new TimeoutException()) };
            if (failure == "MF Play") desktop.Next = () => new() { PlayFails = true };
            if (failure == "MF Present") desktop.Next = () => new() { PresentFails = true };
            bool result = await controller.StartAsync(@"C:\B.mp4");
            check(!result && old.Disposals == 0 && old.Playing && old.Presented && old.Value == 123456 && controller.ActivePath == @"C:\A.mp4",
                $"Phase 2: {failure} retains the old stable video and position");
            check(controller.State == App.VideoWallpaperState.Playing && controller.ErrorKey.Length > 0 &&
                desktop.Sessions.Skip(1).All(s => s.Disposals == 1 && s.Subscribers == 0),
                $"Phase 2: {failure} releases candidate and detaches error handlers exactly once");
            int count = desktop.Sessions.Count;
            for (int i = 0; i < 20; i++) await controller.TickAsync();
            check(desktop.Sessions.Count == count && messages.Any(m => m.Contains("media change rejected") && m.Contains("hr=0x")),
                $"Phase 2: {failure} logs failure without automatic switch retry");
        }
        foreach (var reasons in new[] { App.VideoPauseReason.None, App.VideoPauseReason.Manual, App.VideoPauseReason.Fullscreen,
            App.VideoPauseReason.Manual | App.VideoPauseReason.Fullscreen })
        {
            var desktop = new Desktop();
            using var controller = new App.VideoWallpaperController(desktop, _ => { });
            controller.SetPause(reasons, true);
            await controller.StartAsync(@"C:\A.mp4");
            foreach (string file in new[] { @"C:\B.mp4", @"C:\B.mp4", @"C:\A.mp4" })
            {
                var old = desktop.Sessions[^1];
                await controller.StartAsync(file);
                check(controller.PauseReasons == reasons && desktop.Sessions[^1].Playing == (reasons == App.VideoPauseReason.None) &&
                    old.Disposals == 1 && old.Subscribers == 0, $"Phase 2: switch/reapply preserves pause reasons={reasons}; file={file}");
            }
            controller.SetPause(App.VideoPauseReason.Manual, false);
            check(desktop.Sessions[^1].Playing == ((reasons & App.VideoPauseReason.Fullscreen) == 0),
                $"Phase 2: removing Manual leaves independent Fullscreen reason={reasons}");
        }
        {
            var desktop = new Desktop();
            using var controller = new App.VideoWallpaperController(desktop, _ => { });
            await controller.StartAsync(@"C:\A.mp4"); var old = desktop.Sessions[0];
            var frame = Pending();
            desktop.Next = () => new() { Preparation = frame.Task };
            Task<bool> next = controller.StartAsync(@"C:\B.mp4");
            check(old.Playing && old.Disposals == 0 && !desktop.Sessions[^1].Presented, "Phase 2: old player remains visible while candidate has no valid frame");
            controller.SetPause(App.VideoPauseReason.Fullscreen, true); controller.SetPause(App.VideoPauseReason.Manual, true);
            controller.SetPause(App.VideoPauseReason.Fullscreen, false);
            frame.SetResult(); await next;
            check(controller.PauseReasons == App.VideoPauseReason.Manual && !desktop.Sessions[^1].Playing && desktop.Sessions[^1].Plays == 0,
                "Phase 2: pause changes during first-frame preparation apply to the new session");
        }
        {
            var desktop = new Desktop();
            using var controller = new App.VideoWallpaperController(desktop, _ => { });
            await controller.StartAsync(@"C:\A.mp4");
            var old = desktop.Sessions[0];
            var starts = new List<Task<bool>>(); var gates = new List<TaskCompletionSource>();
            for (int i = 0; i < 100; i++)
            {
                var gate = Pending(); gates.Add(gate);
                desktop.Next = () => new() { Preparation = gate.Task };
                starts.Add(controller.StartAsync($@"C:\rapid-{i}.mp4"));
            }
            check(old.Disposals == 0 && desktop.MaximumLive == 2 && desktop.Sessions.Skip(1).Take(99).All(s => s.Disposals == 1 && s.Subscribers == 0),
                "Phase 2: 100 rapid requests retain at most one old player and one candidate");
            gates[^1].SetResult(); await Task.WhenAll(starts);
            check(controller.ActivePath == @"C:\rapid-99.mp4" && starts.Count(s => s.Result) == 1 &&
                desktop.Sessions.Count(s => s.Disposals == 0) == 1 && old.Disposals == 1,
                "Phase 2: only the latest rapid request publishes a player");
            controller.Stop();
            check(desktop.Sessions.All(s => s.Disposals == 1 && s.Subscribers == 0), "Phase 2: rapid switch cleanup leaves no session or subscribed event");
        }
        {
            var desktop = new Desktop(); var gate = Pending();
            using var controller = new App.VideoWallpaperController(desktop, _ => { });
            desktop.Next = () => new() { Initialization = gate.Task, IgnoreCancellation = true };
            Task<bool> obsolete = controller.StartAsync(@"C:\obsolete.mp4");
            var stale = desktop.Sessions[0]; var queuedError = stale.CapturedError;
            desktop.Next = () => new();
            await controller.StartAsync(@"C:\new.mp4");
            gate.SetException(new COMException("late old decoder error")); await obsolete;
            queuedError?.Invoke(new COMException());
            check(controller.State == App.VideoWallpaperState.Playing && controller.ActivePath == @"C:\new.mp4" && controller.ErrorKey == "",
                "Phase 2: late initialization failure and queued stale MF event cannot overwrite newer state");
            check(stale.Disposals == 1 && !stale.Presented && stale.Subscribers == 0, "Phase 2: noncooperative obsolete initializer is released exactly once");
        }
        {
            var desktop = new Desktop(); var gate = Pending();
            using var controller = new App.VideoWallpaperController(desktop, _ => { });
            await controller.StartAsync(@"C:\A.mp4"); var old = desktop.Sessions[0];
            desktop.Next = () => new() { Preparation = gate.Task };
            Task<bool> pending = controller.StartAsync(@"C:\B.mp4");
            old.Error(); gate.SetResult(); await pending;
            check(controller.ActivePath == @"C:\B.mp4" && controller.State == App.VideoWallpaperState.Playing && old.Disposals == 1,
                "Phase 2: failing old decoder cannot destroy an in-flight newer video");
        }
        {
            var time = DateTime.UtcNow; var desktop = new Desktop(); var gate = Pending();
            using var controller = new App.VideoWallpaperController(desktop, _ => { }, () => time);
            await controller.StartAsync(@"C:\A.mp4"); desktop.Sessions[0].Attached = false; await controller.TickAsync();
            desktop.Next = () => new() { Preparation = gate.Task, IgnoreCancellation = true };
            time = time.AddSeconds(1); Task recovery = controller.TickAsync(); var obsolete = desktop.Sessions[^1];
            desktop.Next = () => new(); await controller.StartAsync(@"C:\B.mp4");
            gate.SetResult(); await recovery;
            check(controller.ActivePath == @"C:\B.mp4" && controller.State == App.VideoWallpaperState.Playing && obsolete.Disposals == 1,
                "Phase 2: a late recovery cannot republish over a newer Apply");
        }
        {
            var desktop = new Desktop(); var gate = Pending();
            var controller = new App.VideoWallpaperController(desktop, _ => { });
            desktop.Next = () => new() { Preparation = gate.Task };
            Task<bool> pending = controller.StartAsync(@"C:\A.mp4");
            controller.Dispose(); controller.SetPause(App.VideoPauseReason.Manual, true); await pending;
            check(desktop.Sessions.All(s => s.Disposals == 1 && !s.Presented) && controller.State == App.VideoWallpaperState.Stopped,
                "Phase 2: shutdown during first-frame preroll cannot leave a hidden decoder or publish late");
        }
    }
}
