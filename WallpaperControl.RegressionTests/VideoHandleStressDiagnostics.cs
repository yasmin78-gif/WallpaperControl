extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using Video = WallpaperApp::WallpaperControl.Video;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.ComponentModel;
using Microsoft.Win32;

// Explicit, opt-in interactive diagnostics. No production instrumentation,
// foreign-handle closing, GC forcing, overlay changes or COM refcount probes.
internal static class VideoHandleStressDiagnostics
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static object? Field(object value, string name) => value.GetType().GetField(name, Fields)!.GetValue(value);
    private static void Set(object value, string name, object? item) => value.GetType().GetField(name, Fields)!.SetValue(value, item);
    private static T Read<T>(object value, string name) => (T)Field(value, name)!;
    private static void Pump(int milliseconds)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(2); }
    }
    private static void Wait(Func<bool> ready)
    {
        var timer = Stopwatch.StartNew();
        while (!ready()) { if (timer.Elapsed.TotalSeconds > 35) throw new TimeoutException("Handle stress operation"); Pump(5); }
    }
    private static void Finish(Task work) { Wait(() => work.IsCompleted); work.GetAwaiter().GetResult(); }
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static int PaintHandlers(Control control)
    {
        object key = typeof(Control).GetFields(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(f => f.FieldType == typeof(object) && f.Name.Contains("paint", StringComparison.OrdinalIgnoreCase)).GetValue(null)!;
        var events = (EventHandlerList)typeof(Component).GetProperty("Events", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
        return events[key]?.GetInvocationList().Length ?? 0;
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Simple(nint self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate int CreateItem(nint self, [MarshalAs(UnmanagedType.LPWStr)] string file, int synchronous, nuint data, out nint item);
    private static T Slot<T>(nint self, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(self), slot * IntPtr.Size));
    // IMFPMediaPlayer ABI, including IUnknown, from the Microsoft SDK mfplay.h.
    private const int StopSlot = 5, CreateItemSlot = 14, ClearItemSlot = 17;

    private sealed class Session : App.IVideoSession
    {
        internal readonly Video.MediaFoundationVideoSession Inner;
        internal readonly Video.MfPlayer Player;
        internal readonly nint Hwnd;
        private readonly Desktop desktop;
        private readonly Task gate;
        private readonly bool fail, stale;
        internal string Phase = "created";
        internal int Subscribers, Disposals;
        internal Session(Desktop desktop, Video.MediaFoundationVideoSession inner, Task gate, bool fail, bool stale)
        {
            this.desktop = desktop; Inner = inner; this.gate = gate; this.fail = fail; this.stale = stale;
            Player = Read<Video.MfPlayer>(inner, "player"); Hwnd = Read<nint>(inner, "rendererHandle");
        }
        public event Action<Exception>? Failed
        {
            add { Inner.Failed += value; Subscribers++; }
            remove { Inner.Failed -= value; Subscribers--; }
        }
        public async Task InitializeAsync(CancellationToken token) { await Inner.InitializeAsync(token); Phase = "initialized"; }
        public async Task PrepareAsync(CancellationToken token)
        {
            Phase = "preparing";
            await Inner.PrepareAsync(token);
            Phase = "frame-ready";
            desktop.OnPrepared?.Invoke();
            if (stale) await gate; else await gate.WaitAsync(token);
            if (fail) throw new InvalidDataException("Diagnostic failure after real first-frame preparation");
        }
        public void Present() { Inner.Present(); Phase = "published"; }
        public bool IsAttached(App.VideoShellGeneration shell) => Inner.IsAttached(shell);
        public long Position => Inner.Position;
        public void Play() => Inner.Play();
        public void Pause() => Inner.Pause();
        public void Seek(long position) => Inner.Seek(position);
        internal int ComPointers
        {
            get
            {
                int count = Read<nint>(Player, "player") != 0 ? 1 : 0;
                var renderer = Field(Player, "renderer");
                if (renderer != null) { if (Read<nint>(renderer, "sink") != 0) count++; if (Read<nint>(renderer, "display") != 0) count++; }
                return count;
            }
        }
        internal int Callbacks => Field(Read<Video.MfCallback>(Player, "callback"), "Event") is Delegate d ? d.GetInvocationList().Length : 0;
        internal int ErrorHandlers => Field(Inner, "Failed") is Delegate d ? d.GetInvocationList().Length : 0;
        internal int RendererPaintHandlers => PaintHandlers(Read<Video.VideoRenderForm>(Inner, "render"));
        // Diagnostic-only source reuse. Production still creates hidden players.
        internal void ChangeMediaSource(string path, bool green)
        {
            var callback = Read<Video.MfCallback>(Player, "callback");
            var previous = Read<Action<nint>>(callback, "Event");
            int stopped = 0, cleared = 0, set = 0; Exception? failure = null;
            callback.Event = header =>
            {
                previous(header);
                int hr = Marshal.ReadInt32(header, 4), type = Marshal.ReadInt32(header);
                if (hr < 0) failure = new COMException("Source reuse MF event", hr);
                if (type == 2) stopped++;
                if (type == 8) cleared++;
                if (type == 6) set++;
            };
            try
            {
                nint native = Read<nint>(Player, "player");
                Marshal.ThrowExceptionForHR(Slot<Simple>(native, StopSlot)(native));
                Wait(() => stopped > 0 || failure != null);
                if (failure != null) throw failure;
                // Rebinding a shutdown custom EVR produced no new frames in
                // the first diagnostic attempt. Keep MFPlay itself, but replace
                // the per-item EVR after clearing the old item completely.
                Marshal.ThrowExceptionForHR(Slot<Simple>(native, ClearItemSlot)(native));
                Wait(() => cleared > 0 || failure != null);
                if (failure != null) throw failure;
                var oldRenderer = Field(Player, "renderer") as IDisposable;
                Set(Player, "renderer", null); oldRenderer?.Dispose();
                desktop.ReusedPlayerEvrRecreations++;
                nint item = 0;
                try { Marshal.ThrowExceptionForHR(Slot<CreateItem>(native, CreateItemSlot)(native, path, 0, 0, out item)); }
                finally { if (item != 0) Marshal.Release(item); }
                Wait(() => set > 0 || failure != null);
                if (failure != null) throw failure;
                Pump(10); Inner.Play();
                Wait(() =>
                {
                    if (failure != null) return true;
                    if (!Player.TryCaptureFrame(out var frame) || frame == null) return false;
                    return green ? frame.Pixels[1] > 200 && frame.Pixels[0] < 40 : frame.Pixels[0] > 200 && frame.Pixels[1] < 40;
                });
                if (failure != null) throw failure;
                Require(Player.Muted, "Source reuse lost mute");
                Require(Read<nint>(Player, "player") == native, "Source reuse recreated MFPlay");
            }
            finally { if (Read<nint>(Player, "player") != 0) callback.Event = previous; }
        }
        public void Dispose()
        {
            Disposals++;
            Require(Disposals == 1, "Native session disposed more than once");
            Inner.Dispose(); Phase = "disposed";
            Require(Subscribers == 0 && ErrorHandlers == 0 && RendererPaintHandlers == 0 && Callbacks == 0 && ComPointers == 0 &&
                !Video.VideoNative.IsWindow(Hwnd) && !Player.HasPresentedFrame && !Read<bool>(Player, "platformStarted"),
                "Disposed native session retains an owned resource");
            desktop.Disposed++; desktop.Live.Remove(this);
        }
    }
    private sealed class Desktop(Control dispatcher) : App.IVideoDesktop
    {
        private readonly Video.MediaFoundationVideoDesktop native = new(dispatcher);
        internal readonly HashSet<Session> Live = new();
        internal int Created, Disposed;
        internal int ReusedPlayerEvrRecreations;
        internal Task Gate = Task.CompletedTask;
        internal bool FailAfterPrepare, StalePreparation;
        internal Action? OnPrepared;
        internal Session? Last;
        public int MonitorCount => native.MonitorCount;
        public bool FileExists(string path) => native.FileExists(path);
        public Task ValidateAsync(string path, CancellationToken token) => native.ValidateAsync(path, token);
        public App.VideoShellGeneration? ResolveShell() => native.ResolveShell();
        public App.IVideoSession Create(string path, App.VideoShellGeneration shell)
        {
            Last = new(this, new(dispatcher, path, shell), Gate, FailAfterPrepare, StalePreparation);
            Created++; Live.Add(Last); return Last;
        }
    }
    private sealed class Recorder : IDisposable
    {
        private readonly StreamWriter writer;
        private readonly Process process = Process.GetCurrentProcess();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private TimeSpan lastCpu, lastAt;
        private readonly List<WeakReference<CancellationTokenSource>> sources = new();
        private readonly Desktop desktop;
        private readonly App.VideoWallpaperController controller;
        private readonly App.MainForm main;
        private int samples;
        internal Recorder(string output, Desktop desktop, App.VideoWallpaperController controller, App.MainForm main)
        { writer = new(output, false); this.desktop = desktop; this.controller = controller; this.main = main; }
        internal void TrackSource()
        {
            if (Field(controller, "cancellation") is CancellationTokenSource source) sources.Add(new(source));
        }
        internal void Sample(string stage, int switches, int expectedPlayers, bool verify = true)
        {
            var at = clock.Elapsed;
            process.Refresh(); var cpu = process.TotalProcessorTime;
            long working = process.WorkingSet64, bytes = process.PrivateMemorySize64;
            int processHandles = process.HandleCount;
            var duration = Stopwatch.StartNew(); var handles = NativeHandleSnapshot.Capture(); duration.Stop();
            var players = desktop.Live.Where(s => Read<nint>(s.Player, "player") != 0).ToArray();
            int windows = desktop.Live.Count(s => Video.VideoNative.IsWindow(s.Hwnd));
            int com = desktop.Live.Sum(s => s.ComPointers), callbacks = desktop.Live.Sum(s => s.Callbacks), events = desktop.Live.Sum(s => s.ErrorHandlers);
            int paints = desktop.Live.Sum(s => s.RendererPaintHandlers);
            int evr = desktop.Live.Count(s => Field(s.Player, "renderer") != null);
            int liveSources = sources.Select(w => w.TryGetTarget(out var source) ? source : null).Where(s => s != null)
                .Distinct().Count(s => !Read<bool>(s!, "_disposed"));
            int currentSources = Field(controller, "cancellation") == null ? 0 : 1;
            int published = Field(controller, "session") == null ? 0 : 1, preparing = Field(controller, "initializingSession") == null ? 0 : 1;
            int changed = Field(controller, "Changed") is Delegate d ? d.GetInvocationList().Length : 0;
            if (verify)
            {
                Require(players.Length == expectedPlayers && windows == expectedPlayers && evr == expectedPlayers && com == expectedPlayers * 3 &&
                    callbacks == expectedPlayers && events == expectedPlayers && paints == expectedPlayers && liveSources == currentSources && desktop.Created - desktop.Disposed == expectedPlayers,
                    $"Owned resource mismatch at {stage}: players={players.Length}, windows={windows}, evr={evr}, com={com}, callbacks={callbacks}, events={events}, cts={liveSources}/{currentSources}");
                Require(!Read<bool>(controller, "ticking") && controller.RecoveryAttempts == 0, "Unexpected recovery operation during stress");
            }
            var record = new
            {
                sample = ++samples, pid = Environment.ProcessId, utc = DateTimeOffset.UtcNow, seconds = at.TotalSeconds, stage, switches,
                processHandles, totalHandles = handles.Total, handleTypes = handles.Types, nvidiaIpc = handles.NvidiaIpc,
                nvidiaNames = handles.NvidiaNames, nvidiaUniqueObjects = handles.NvidiaUniqueObjects,
                nvidiaObjectIdentitiesAvailable = handles.NvidiaObjectIdentitiesAvailable,
                nvidiaRedactedObjectIdentities = handles.NvidiaRedactedObjectIdentities,
                cpuSeconds = cpu.TotalSeconds, cpuPercentMachine = samples == 1 ? (double?)null :
                    (cpu - lastCpu).TotalSeconds / (at - lastAt).TotalSeconds / Environment.ProcessorCount * 100,
                cpuWindowSeconds = (at - lastAt).TotalSeconds, workingSetMiB = working / 1048576.0, privateMiB = bytes / 1048576.0,
                activeMfPlayers = players.Length, rendererHwnds = windows, activeEvr = evr, ownedComPointers = com,
                nativeCallbacks = callbacks, sessionErrorSubscriptions = events, controllerChangedSubscriptions = changed,
                rendererPaintSubscriptions = paints,
                gcCollections = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) },
                currentCancellationTokenSources = currentSources, trackedUndisposedCancellationTokenSources = liveSources,
                publishedPlayers = published, preparingPlayers = preparing, sessionsCreated = desktop.Created, sessionsDisposed = desktop.Disposed,
                reusedPlayerEvrRecreations = desktop.ReusedPlayerEvrRecreations,
                recoveryAttempts = controller.RecoveryAttempts, recoveryInFlight = Read<bool>(controller, "ticking"),
                mode = Read<App.WallpaperModeOwnership>(main, "wallpaperOwnership").Mode.ToString(), state = controller.State.ToString(),
                diagnosticHandleSnapshotMs = duration.Elapsed.TotalMilliseconds
            };
            writer.WriteLine(JsonSerializer.Serialize(record)); writer.Flush(); lastCpu = cpu; lastAt = at;
            Console.WriteLine($"RESOURCE {stage} switches={switches} handles={handles.Total} nvidia={handles.NvidiaIpc} players={players.Length} hwnd={windows} evr={evr} com={com} cts={liveSources} privateMiB={bytes / 1048576.0:F2}");
        }
        public void Dispose() { writer.Dispose(); process.Dispose(); }
    }
    internal static void Run(string fixtures, string output, int switches, string scenario)
    {
        Require(switches > 0 && new[] { "recreate", "rapid", "cancelled", "stale", "failed", "reuse", "com-audit" }.Contains(scenario), "Invalid stress arguments");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        using Task work = new(() => Core(fixtures, output, switches, scenario));
        var thread = new Thread(() =>
        {
            using var loop = new Video.VideoRenderForm { Location = new(-20000, -20000), Size = new(100, 100) };
            loop.Shown += (_, _) => { try { work.RunSynchronously(TaskScheduler.Default); } finally { loop.Close(); } };
            Application.Run(loop);
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); work.GetAwaiter().GetResult();
    }
    private static void Core(string fixtures, string output, int switches, string scenario)
    {
        if (scenario == "com-audit") { ComAudit(fixtures, output, switches); return; }
        Require(Screen.AllScreens.Length == 1, "Stress diagnostics require one monitor");
        string a = Path.Combine(fixtures, "loop-colors.mp4"), b = Path.Combine(fixtures, "switch-colors.mp4");
        string registry = @"Software\WallpaperControl.HandleDiagnostics-" + Guid.NewGuid().ToString("N");
        try
        {
            using var widgets = new App.WidgetManager(() => { }, registryPath: registry,
                notesStore: new App.NotesStore(Path.Combine(Path.GetDirectoryName(output)!, "notes-" + Guid.NewGuid().ToString("N") + ".json")));
            using var main = new App.MainForm(widgets, () => (App.DesktopSlideshowState)0, () => null);
            _ = main.Handle;
            var desktop = new Desktop(main);
            using var controller = new App.VideoWallpaperController(desktop, message => Console.WriteLine("LIFECYCLE " + message));
            Set(main, "videoWallpaper", controller);
            controller.Changed += main.GetType().GetMethod("VideoWallpaperChanged", Fields)!.CreateDelegate<Action>(main);
            using var record = new Recorder(output, desktop, controller, main);
            Task Apply(string path, App.WallpaperOperatingMode mode = App.WallpaperOperatingMode.VideoWallpaper)
            {
                Read<TextBox>(main, "videoPathText").Text = path;
                var task = (Task)main.GetType().GetMethod("ApplyWallpaperModeAsync", Fields)!.Invoke(main, new object[] { mode })!;
                record.TrackSource(); return task;
            }
            for (int i = 0; i < 8; i++) Finish(Apply(i % 2 == 0 ? b : a));
            Require(controller.HasSession, "Warmup failed"); Pump(200);
            record.Sample("baseline", 0, 1);
            int current = 0;
            desktop.OnPrepared = () => { if (current == 1 || current % 25 == 0) record.Sample("prepared-before-swap", current, 2); };
            if (scenario is "recreate" or "reuse")
            {
                for (current = 1; current <= switches; current++)
                {
                    if (scenario == "reuse") desktop.Last!.ChangeMediaSource(current % 2 == 1 ? b : a, current % 2 == 1);
                    else Finish(Apply(current % 2 == 1 ? b : a));
                    Require(controller.State == App.VideoWallpaperState.Playing, "Switch failed");
                    if (current == 1 || current % 5 == 0 || current == switches) record.Sample("after-switch", current, 1);
                }
            }
            else
            {
                var requests = new List<Task<bool>>(); var gates = new List<TaskCompletionSource>();
                for (current = 1; current <= switches; current++)
                {
                    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    desktop.Gate = scenario == "failed" ? Task.CompletedTask : gate.Task;
                    desktop.FailAfterPrepare = scenario == "failed"; desktop.StalePreparation = scenario == "stale";
                    var request = controller.StartAsync(current % 2 == 1 ? b : a); record.TrackSource(); requests.Add(request); gates.Add(gate);
                    Wait(() => desktop.Last?.Phase == "frame-ready" || request.IsCompleted);
                    if (scenario is "cancelled" or "stale") { controller.CancelPendingStart(); record.TrackSource(); }
                    if (scenario == "cancelled") { gate.SetResult(); Finish(request); Require(!request.Result, "Cancelled preparation published"); }
                    if (scenario == "failed") { Finish(request); Require(!request.Result, "Failed preparation published"); }
                    if (current == 1 || current % 5 == 0 || current == switches)
                        record.Sample("after-" + scenario, current, scenario == "rapid" ? 2 : 1);
                }
                desktop.Gate = Task.CompletedTask; desktop.FailAfterPrepare = desktop.StalePreparation = false;
                var newest = controller.StartAsync(b); record.TrackSource(); Finish(newest); Require(newest.Result, "Latest request failed");
                foreach (var gate in gates) gate.TrySetResult();
                foreach (var request in requests) { Finish(request); Require(!request.Result, "Obsolete preparation published"); }
                record.Sample("rapid-or-cancellation-settled", switches, 1);
            }
            desktop.OnPrepared = null;
            var idle = Stopwatch.StartNew(); record.Sample("final-immediate", switches, 1);
            while (idle.Elapsed.TotalSeconds < 30) Pump(50);
            record.Sample("final-idle-30s", switches, 1);
            while (idle.Elapsed.TotalSeconds < 120) Pump(50);
            record.Sample("final-idle-120s", switches, 1);
            Finish(Apply(a, App.WallpaperOperatingMode.ImageSlideshow)); record.Sample("image-immediate", switches, 0);
            Pump(30000); record.Sample("image-idle-30s", switches, 0);
            Finish(Apply(a)); record.Sample("video-restarted-for-stop", switches, 1);
            controller.Stop(); record.Sample("video-stop-immediate", switches, 0);
            Pump(30000); record.Sample("video-stop-idle-30s", switches, 0);
            controller.Dispose(); record.Sample("video-controller-disposed", switches, 0);
            Require(Field(controller, "Changed") == null && Field(controller, "cancellation") == null &&
                Field(controller, "session") == null && Field(controller, "initializingSession") == null, "Controller shutdown retains ownership");
            Console.WriteLine($"STRESS PASSED scenario={scenario} switches={switches} pid={Environment.ProcessId} created={desktop.Created} disposed={desktop.Disposed}");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(registry, false); }
    }
    private static void ComAudit(string fixtures, string output, int cycles)
    {
        using var writer = new StreamWriter(output);
        using var dispatcher = new Video.VideoRenderForm { Location = new(-20000, -20000), Size = new(100, 100) };
        _ = dispatcher.Handle;
        var shell = Video.VideoNative.Resolve() ?? throw new InvalidOperationException("Desktop shell unavailable");
        for (int cycle = 1; cycle <= cycles; cycle++)
        {
            using var session = new Video.MediaFoundationVideoSession(dispatcher,
                Path.Combine(fixtures, cycle % 2 == 0 ? "loop-colors.mp4" : "switch-colors.mp4"), shell);
            Finish(session.InitializeAsync(default)); Finish(session.PrepareAsync(default));
            var player = Read<Video.MfPlayer>(session, "player"); var renderer = Field(player, "renderer")!;
            nint playerProbe = Read<nint>(player, "player"), sinkProbe = Read<nint>(renderer, "sink"), displayProbe = Read<nint>(renderer, "display");
            // Separate from growth measurements. Hold exactly one additional
            // reference to each of our own interfaces through Shutdown, then
            // release only those references. Never inspect a freed pointer.
            Marshal.AddRef(playerProbe); Marshal.AddRef(sinkProbe); Marshal.AddRef(displayProbe);
            try
            {
                session.Dispose(); Pump(500); // Drain shutdown callbacks.
                int displayFinal = Marshal.Release(displayProbe); displayProbe = 0;
                int sinkFinal = Marshal.Release(sinkProbe); sinkProbe = 0;
                int playerFinal = Marshal.Release(playerProbe); playerProbe = 0;
                Require(displayFinal == 0 && sinkFinal == 0 && playerFinal == 0, $"COM audit retained native objects: player={playerFinal}; sink={sinkFinal}; display={displayFinal}");
                var identities = NativeHandleSnapshot.Capture();
                writer.WriteLine(JsonSerializer.Serialize(new { cycle, pid = Environment.ProcessId, utc = DateTimeOffset.UtcNow,
                    additionalOwnedReferences = 3, playerFinal, sinkFinal, displayFinal,
                    ownedPlayerPointer = Read<nint>(player, "player").ToInt64(), rendererDetached = Field(player, "renderer") == null,
                    rendererHwndAlive = Video.VideoNative.IsWindow(Read<nint>(session, "rendererHandle")),
                    nvidiaObjectIdentitiesAvailable = identities.NvidiaObjectIdentitiesAvailable,
                    nvidiaRedactedObjectIdentities = identities.NvidiaRedactedObjectIdentities,
                    nvidiaIpc = identities.NvidiaIpc, nvidiaNames = identities.NvidiaNames }));
                writer.Flush();
                Console.WriteLine($"COM AUDIT cycle={cycle} finalPlayerRelease={playerFinal} finalSinkRelease={sinkFinal} displayRelease={displayFinal}");
            }
            finally
            {
                if (displayProbe != 0) Marshal.Release(displayProbe);
                if (sinkProbe != 0) Marshal.Release(sinkProbe);
                if (playerProbe != 0) Marshal.Release(playerProbe);
            }
        }
        Console.WriteLine($"COM AUDIT PASSED cycles={cycles}; final native MF player/sink releases=0");
    }
}
