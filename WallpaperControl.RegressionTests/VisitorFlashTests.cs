extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using System.Reflection;
using Microsoft.Win32;

internal static class VisitorFlashTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Fields)!.GetValue(target)!;
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Fields)!.SetValue(target, value);
    private static Task Invoke(object target, string name, params object[] args) => (Task)target.GetType().GetMethod(name, Fields)!.Invoke(target, args)!;
    internal static void Run(Action<bool, string> check)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            using Form loop = new() { Location = new(-20000, -20000), ShowInTaskbar = false };
            loop.Shown += async (_, _) =>
            {
                try { await Checks(check, cancel: false); await Checks(check, cancel: true); }
                catch (Exception ex) { failure = ex; }
                finally { App.PersistentDesktopTransitionManager.Shutdown(); loop.Close(); }
            };
            Application.Run(loop);
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static async Task Checks(Action<bool, string> check, bool cancel)
    {
        string root = Path.Combine(AppContext.BaseDirectory, "test-data", "visitor-flash-" + Guid.NewGuid().ToString("N"));
        string media = Path.Combine(root, "media"), cache = Path.Combine(root, "cache"); Directory.CreateDirectory(media);
        string blue = Path.Combine(media, "a.png"), green = Path.Combine(media, "b.png");
        void Picture(string path, Color color) { using Bitmap image = new(16, 12); using (Graphics g = Graphics.FromImage(image)) g.Clear(color); image.Save(path); }
        Picture(blue, Color.Blue); Picture(green, Color.Lime);
        string registry = @"Software\WallpaperControl.VisitorFlash-" + Guid.NewGuid().ToString("N");
        var events = new List<string>(); string? native = null;
        using FakeHost host = new(events);
        try
        {
            using App.WidgetManager widgets = new(() => { }, registryPath: registry);
            using App.MainForm form = new(widgets, () => 0, () => blue);
            Set(form, "pauseOnFullscreen", false);
            var handoff = new App.VisitorWallpaperHandoff(cache,
                path => App.PersistentDesktopTransitionManager.ApplyNativeSelection(() => { events.Add("native-direct"); native = path; }),
                publishCovered: path => App.PersistentDesktopTransitionManager.ApplyCoveredNativeSelection(() =>
                { if (host.IsDisposed) throw new Exception("Cover was disposed before native update."); events.Add("native-covered"); native = path; }));
            form.VisitorHandoffForTests = handoff;
            var service = Field<App.VisitorModeService>(form, "visitorMode");
            await service.ConfigureAsync(s => s with { Source = App.VisitorSourceKind.Folder, Path = media });
            await service.SetActiveAsync(true);
            Field<System.Windows.Forms.Timer>(form, "visitorTimer").Stop();
            check(App.PersistentDesktopTransitionManager.TryStartSession(handoff.StaticPath, () => { }, () => host),
                "Visitor flash: isolated ready-renderer fixture starts without native desktop changes");
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            typeof(App.PersistentDesktopTransitionManager).GetField("compositionReady", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, ready.Task);
            string before = handoff.StaticPath; events.Clear();
            Task advance = Invoke(form, "AdvanceWallpaperAsync", App.DesktopSlideshowDirection.Forward);
            check(!advance.IsCompleted && native == before && handoff.StaticPath == before && host.Transitions == 0 && !host.IsDisposed,
                "Visitor flash: old safe wallpaper and renderer remain unchanged until composition is ready " + cancel);
            if (cancel)
            {
                Set(form, "visitorClosing", true);
                await Task.WhenAll(advance, Invoke(form, "StopVisitorAndCloseAsync"));
                check(!events.Contains("native-covered") && handoff.StaticPath == before && native == before,
                    "Visitor flash: closing during renderer preparation cannot publish an unshown target");
                check(events.IndexOf("native-direct") >= 0 && events.IndexOf("native-direct") < events.IndexOf("dispose"),
                    "Visitor flash: cancellation hands the last safe image to Windows before disposing the renderer");
                ready.TrySetCanceled();
                return;
            }
            ready.SetResult(); await advance;
            check(events.SequenceEqual(new[] { "native-covered", "animate" }) && host.Initializations == 1 && host.Transitions == 1 && !host.IsDisposed,
                "Visitor flash: covered native checkpoint precedes animation without renderer shutdown or recreation");
            using (Bitmap image = new(native!))
                check(image.GetPixel(0, 0).G == 255 && native == handoff.StaticPath && host.CurrentWallpaperPath == native,
                    "Visitor flash: native crash-safe pixels and committed rendered pixels both match the new image");
            events.Clear();
            await Invoke(form, "AdvanceWallpaperAsync", App.DesktopSlideshowDirection.Forward);
            check(events.SequenceEqual(new[] { "native-covered", "animate" }) && host.Initializations == 1 && host.Transitions == 2,
                "Visitor flash: repeated Next reuses the same rendered session");
            check(host.Paths.All(path => Path.GetDirectoryName(path) == cache),
                "Visitor flash: renderer receives only durable safe copies, never normal wallpaper paths");
            events.Clear(); Set(form, "visitorClosing", true); await Invoke(form, "StopVisitorAndCloseAsync");
            check(events.IndexOf("native-direct") >= 0 && events.IndexOf("native-direct") < events.IndexOf("dispose") && service.Active,
                "Visitor flash: exit still performs native handoff before retiring the retained renderer");
        }
        finally { App.PersistentDesktopTransitionManager.Shutdown(); Registry.CurrentUser.DeleteSubKeyTree(registry, false); }
    }

    private sealed class FakeHost(List<string> events) : App.IPersistentWallpaperHost
    {
        internal int Initializations, Transitions;
        internal readonly List<string> Paths = new();
        public bool IsDisposed { get; private set; }
        public string? CurrentWallpaperPath { get; private set; }
        public bool Initialize(string path) { Initializations++; CurrentWallpaperPath = path; Paths.Add(path); return true; }
        public bool EnsureDesktopPlacement() => !IsDisposed;
        public void SetActivitySuspended(bool suspended) { }
        public void SetWallpaperPosition(App.DesktopWallpaperPosition position) { }
        public Task TransitionToAsync(string path, App.WallpaperTransitionKind kind, int milliseconds,
            App.WallpaperTransitionDirection direction, App.WallpaperZoomMode zoom, CancellationToken cancellation)
        { cancellation.ThrowIfCancellationRequested(); Transitions++; Paths.Add(path); events.Add("animate"); return Task.CompletedTask; }
        public void CommitCurrentPath(string path) => CurrentWallpaperPath = path;
        public void Dispose() { if (!IsDisposed) { events.Add("dispose"); IsDisposed = true; } }
    }
}
