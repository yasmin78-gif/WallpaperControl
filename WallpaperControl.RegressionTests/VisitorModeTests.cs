extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;

internal static class VisitorModeTests
{
    private const System.Reflection.BindingFlags Members = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Members)!.GetValue(target)!;
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Members)!.SetValue(target, value);
    internal static void Ui(Action<bool, string> check)
    {
        Exception? error = null;
        Thread thread = new(() =>
        {
            string key = @"Software\WallpaperControl.RegressionTests-VisitorUi-" + Guid.NewGuid();
            try
            {
                using App.WidgetManager widgets = new(() => { }, registryPath: key);
                using App.MainForm form = new(widgets, () => 0, () => null);
                var service = Field<App.VisitorModeService>(form, "visitorMode");
                var toggle = Field<CheckBox>(form, "visitorSwitch");
                var content = Field<Panel>(form, "wallpaperContent");
                var source = Field<ComboBox>(form, "visitorSource");
                check(source.Items.Count == 3 && !toggle.Checked, "Visitor UI: three source types and compatible off default");
                toggle.Checked = true;
                check(service.Active && toggle.Checked && !content.Enabled && service.Error != null, "Visitor UI: main switch activates safe mode for missing source");
                service.SetActiveAsync(false).GetAwaiter().GetResult();
                check(!toggle.Checked && content.Enabled, "Visitor UI: service synchronizes main switch and restores controls");
                var ownership = Field<App.WallpaperModeOwnership>(form, "wallpaperOwnership");
                var desktop = new FakeDesktop();
                var controller = new App.VideoWallpaperController(desktop, _ => { });
                controller.StartAsync("normal.mp4").GetAwaiter().GetResult();
                desktop.Session!.CurrentPosition = 87654321;
                controller.SetPause(App.VideoPauseReason.Manual, true);
                Set(form, "videoWallpaper", controller);
                ownership.Switch(App.WallpaperOperatingMode.VideoWallpaper);
                service.SetActiveAsync(true).GetAwaiter().GetResult();
                check(service.Settings.Resume?.VideoPosition == 87654321 && service.Settings.Resume.VideoPaused,
                    "Visitor UI: normal video position and manual pause captured");
                service.SetActiveAsync(false).GetAwaiter().GetResult();
                check(!service.Active && controller.PlaybackPosition == 87654321 && controller.PauseReasons.HasFlag(App.VideoPauseReason.Manual)
                    && ownership.Mode == App.WallpaperOperatingMode.VideoWallpaper && desktop.Session!.SoughtBeforePresent,
                    "Visitor UI: normal video seeks before presentation and restores manual pause");
            }
            catch (Exception ex) { error = ex; }
            finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(key, false); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error != null) throw error;
    }

    private sealed class FakeDesktop : App.IVideoDesktop
    {
        internal FakeSession? Session;
        public int MonitorCount => 1;
        public bool FileExists(string path) => true;
        public Task ValidateAsync(string path, CancellationToken cancellation) => Task.CompletedTask;
        public App.VideoShellGeneration? ResolveShell() => new(1, 2, 3, 4, 5);
        public App.IVideoSession Create(string path, App.VideoShellGeneration shell) => Session = new();
    }
    private sealed class FakeSession : App.IVideoSession
    {
        internal long CurrentPosition;
        internal bool SoughtBeforePresent, Presented;
        public long Position => CurrentPosition;
        public Task InitializeAsync(CancellationToken cancellation) => Task.CompletedTask;
        public bool IsAttached(App.VideoShellGeneration shell) => true;
        public void Seek(long position) { CurrentPosition = position; SoughtBeforePresent = !Presented; }
        public void Present() => Presented = true;
        public void Play() { }
        public void Pause() { }
        public void Dispose() { }
        public event Action<Exception>? Failed { add { } remove { } }
    }
    internal static void Run(Action<bool, string> check) => RunAsync(check).GetAwaiter().GetResult();
    private static async Task RunAsync(Action<bool, string> check)
    {
        var snapshot = new App.VisitorResumeState { ImagePath = "normal.png", RemainingTicks = 123456, VideoPosition = 998877 };
        int captures = 0, activations = 0, restorations = 0;
        App.VisitorModeSettings? persisted = null;
        App.VisitorResumeState? restored = null;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new App.VisitorModeService(new(), value => { persisted = value; return true; },
            () => { captures++; return snapshot; }, async () => { activations++; start.TrySetResult(); await release.Task; },
            value => { restored = value; restorations++; return Task.CompletedTask; });
        int notifications = 0; service.Changed += () => notifications++;
        var enable = service.SetActiveAsync(true);
        await start.Task;
        check(service.Active && service.Busy && persisted?.Resume == snapshot, "Visitor: active snapshot persisted before async publication");
        var disable = service.SetActiveAsync(false);
        check(restorations == 0, "Visitor: restoration waits for activation");
        release.SetResult(); await Task.WhenAll(enable, disable);
        check(!service.Active && restored == snapshot && persisted?.Resume == null && !service.Busy, "Visitor: exact snapshot restored and cleared after disable");
        check(captures == 1 && activations == 1 && notifications >= 3, "Visitor: one owner and shared state notifications");
        await service.SetActiveAsync(false);
        check(restorations == 1, "Visitor: repeated set is idempotent");
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => service.ToggleAsync()));
        check(!service.Active && captures == 11 && restorations == 11, "Visitor: twenty rapid UI and hotkey toggles serialize without loss");
        var saved = new App.VisitorModeSettings { Active = true, Resume = snapshot };
        var startup = new App.VisitorModeService(saved, _ => true, () => throw new Exception("Must not capture visitor desktop"),
            () => Task.CompletedTask, value => { restored = value; return Task.CompletedTask; });
        await startup.SetActiveAsync(true, startup: true); await startup.SetActiveAsync(false);
        check(restored == snapshot, "Visitor: startup retains original resume snapshot");
        var failure = new App.VisitorModeService(new(), _ => true, () => snapshot,
            () => throw new IOException("Media unavailable"), _ => throw new IOException("Restore unavailable"));
        await failure.SetActiveAsync(true);
        check(failure.Active && failure.Error != null && failure.Settings.Resume == snapshot, "Visitor: activation failure retains safe ownership and snapshot");
        await failure.SetActiveAsync(false);
        check(failure.Active && failure.Settings.Resume == snapshot, "Visitor: restore failure cannot release visitor ownership");
        await service.ConfigureAsync(s => s with { IntervalMilliseconds = 1, Active = true, Resume = snapshot }, false);
        check(service.Settings.IntervalMilliseconds == 1000 && !service.Active && service.Settings.Resume == null, "Visitor: configuration cannot override active state or snapshot");
        service.Close(); await service.ToggleAsync();
        check(!service.Active, "Visitor: closed service rejects queued requests");

        string key = @"Software\WallpaperControl.RegressionTests-Visitor-" + Guid.NewGuid();
        try
        {
            var store = new App.VisitorModeStore(key);
            check(!store.Load().Active, "Visitor: compatible defaults for missing settings");
            check(store.Save(saved with { Source = App.VisitorSourceKind.Folder, Path = "visitor", WidgetX = -100 }), "Visitor: isolated persistence succeeds");
            var loaded = store.Load();
            check(loaded.Active && loaded.Resume?.RemainingTicks == snapshot.RemainingTicks && loaded.Resume?.VideoPosition == snapshot.VideoPosition
                && loaded.WidgetX == -100 && loaded.Source == App.VisitorSourceKind.Folder, "Visitor: active, source, negative position and resume round trip");
        }
        finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(key, false); }
        var ownership = new App.WallpaperModeOwnership(); int lease = ownership.Generation;
        ownership.Switch(App.WallpaperOperatingMode.Visitor);
        check(!ownership.AllowsImages && !ownership.IsCurrentImage(lease), "Visitor: stale scheduler and image requests rejected");
        ownership.Switch(App.WallpaperOperatingMode.ImageSlideshow);
        check(!ownership.IsCurrentImage(lease), "Visitor: stale work rejected even after returning to images");
    }
}
