extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using Microsoft.Win32;
using System.Reflection;

internal static class VideoWallpaperTests
{
    private sealed class Session : App.IVideoSession
    {
        internal bool Attached = true, Disposed, Playing;
        internal int Plays, Pauses;
        internal long Value;
        internal Task Initialization = Task.CompletedTask;
        public Task InitializeAsync(CancellationToken cancellation) => Initialization.WaitAsync(cancellation);
        public bool IsAttached(App.VideoShellGeneration shell) => Attached && !Disposed;
        public long Position => Value;
        public void Play() { Playing = true; Plays++; }
        public void Pause() { Playing = false; Pauses++; }
        public void Seek(long position) => Value = position;
        public event Action<Exception>? Failed;
        internal void Error() => Failed?.Invoke(new InvalidDataException());
        public void Dispose() { Disposed = true; Playing = false; Failed = null; }
    }
    private sealed class Desktop : App.IVideoDesktop
    {
        public int MonitorCount { get; set; } = 1;
        internal bool Exists = true, RejectCreate;
        internal Exception? ValidationError;
        internal Task Validation = Task.CompletedTask;
        internal Task Initialization = Task.CompletedTask;
        internal int Resolves, Creates, Validations;
        internal App.VideoShellGeneration? Shell = new(1, 2, 3, 4, 5);
        internal readonly List<Session> Sessions = new();
        internal Session Last => Sessions[^1];
        public bool FileExists(string path) => Exists;
        public async Task ValidateAsync(string path, CancellationToken cancellation)
        { Validations++; await Validation.WaitAsync(cancellation); if (ValidationError != null) throw ValidationError; }
        public App.VideoShellGeneration? ResolveShell() { Resolves++; return Shell; }
        public App.IVideoSession Create(string path, App.VideoShellGeneration shell)
        {
            Creates++; if (RejectCreate) throw new InvalidOperationException();
            var item = new Session { Initialization = Initialization }; Sessions.Add(item); return item;
        }
    }
    internal static void Run(Action<bool, string> check)
    {
        Task.Run(() => Engine(check)).GetAwaiter().GetResult();
        Settings(check);
        using Task work = new(() => Ui(check));
        Thread thread = new(() => work.RunSynchronously(TaskScheduler.Default));
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); work.GetAwaiter().GetResult();
    }
    private static async Task Engine(Action<bool, string> check)
    {
        var time = DateTime.UtcNow;
        var desktop = new Desktop();
        List<string> messages = new();
        using var controller = new App.VideoWallpaperController(desktop, messages.Add, () => time);
        await controller.StartAsync(@"C:\test.mp4");
        check(controller.State == App.VideoWallpaperState.Playing && desktop.Last.Playing, "Video: valid MP4 starts");
        desktop.Last.Value = 123456789;
        controller.SetPause(App.VideoPauseReason.Fullscreen, true);
        check(controller.State == App.VideoWallpaperState.Paused && !desktop.Last.Playing, "Video: production fullscreen reason pauses");
        check(desktop.Last.Value == 123456789, "Video: pause preserves position");
        controller.SetPause(App.VideoPauseReason.Manual, true);
        controller.SetPause(App.VideoPauseReason.Fullscreen, false);
        check(!desktop.Last.Playing, "Video: fullscreen exit cannot clear manual pause");
        controller.SetPause(App.VideoPauseReason.Manual, false);
        check(desktop.Last.Playing && desktop.Last.Value == 123456789, "Video: resume retains position");
        controller.SetPause(App.VideoPauseReason.Fullscreen, true);
        controller.SetPause(App.VideoPauseReason.Manual, true);
        controller.SetPause(App.VideoPauseReason.Manual, false);
        check(!desktop.Last.Playing, "Video: manual resume cannot clear fullscreen pause");
        int resolves = desktop.Resolves;
        await controller.TickAsync();
        check(desktop.Resolves > resolves && desktop.Creates == 1, "Video: valid shell re-resolved without repair");
        var first = desktop.Last;
        first.Attached = false;
        await controller.TickAsync();
        check(first.Disposed && controller.State == App.VideoWallpaperState.Recovering, "Video: actual renderer loss creates incident and releases surface");
        time = time.AddSeconds(1); await controller.TickAsync();
        check(controller.RecoveryAttempts == 1 && desktop.Last.Value == 123456789, "Video: recovery recreates renderer and restores position");
        check(!desktop.Last.Playing && controller.State == App.VideoWallpaperState.Paused, "Video: recovery respects fullscreen pause");
        controller.SetPause(App.VideoPauseReason.Fullscreen, false);
        check(desktop.Last.Playing, "Video: fullscreen exit after repair resumes");

        desktop.Shell = new(10, 20, 30, 40, 50); desktop.RejectCreate = true;
        await controller.TickAsync();
        check(controller.State == App.VideoWallpaperState.Recovering && controller.RecoveryAttempts == 0, "Video: new Explorer generation opens fresh incident");
        time = time.AddSeconds(1); await controller.TickAsync();
        check(controller.RecoveryAttempts == 1, "Video: first incident attempt");
        time = time.AddSeconds(2); await controller.TickAsync();
        check(controller.RecoveryAttempts == 1, "Video: backoff prevents early second attempt");
        time = time.AddSeconds(1); await controller.TickAsync();
        check(controller.RecoveryAttempts == 2, "Video: second attempt after backoff");
        time = time.AddSeconds(5); await controller.TickAsync();
        check(controller.RecoveryAttempts == 2, "Video: increasing backoff enforced");
        time = time.AddSeconds(1); await controller.TickAsync();
        check(controller.State == App.VideoWallpaperState.Failed && controller.RecoveryAttempts == 3, "Video: incident exhausted at three attempts");
        int creates = desktop.Creates;
        for (int i = 0; i < 100; i++) { time = time.AddSeconds(60); await controller.TickAsync(); }
        check(desktop.Creates == creates, "Video: exhausted incident never loops on idle polling");
        desktop.Shell = null; await controller.TickAsync();
        check(desktop.Creates == creates, "Video: temporarily absent shell does not reset exhausted budget");
        desktop.Shell = new(10, 20, 30, 40, 50); await controller.TickAsync();
        check(desktop.Creates == creates, "Video: same shell returning does not reset exhausted budget");
        desktop.Shell = new(11, 21, 31, 41, 51); desktop.RejectCreate = false;
        await controller.TickAsync(); time = time.AddSeconds(1); await controller.TickAsync();
        check(controller.State == App.VideoWallpaperState.Playing && controller.RecoveryAttempts == 1, "Video: genuine later shell generation allows new bounded incident");
        check(desktop.Last.Value == 123456789, "Video: later incident retains last known position");
        desktop.Last.Error();
        check(controller.State == App.VideoWallpaperState.Failed && desktop.Last.Disposed, "Video: MF error releases renderer and reports useful failure");
        check(messages.Any(m => m.Contains("hr=0x")), "Video: HRESULT diagnostics recorded");

        foreach (string failure in new[] { "missing", "corrupt", "unsupported", "monitor", "MF init" })
        {
            var boundary = new Desktop();
            if (failure == "missing") boundary.Exists = false;
            if (failure == "corrupt") boundary.ValidationError = new InvalidDataException();
            if (failure == "unsupported") boundary.ValidationError = new NotSupportedException();
            if (failure == "monitor") boundary.MonitorCount = 2;
            if (failure == "MF init") boundary.Initialization = Task.FromException(new System.Runtime.InteropServices.COMException("", unchecked((int)0x80004005)));
            using var service = new App.VideoWallpaperController(boundary, _ => { });
            await service.StartAsync(@"C:\test.mp4");
            check(service.State == App.VideoWallpaperState.Failed, $"Video: {failure} fails safely");
            check(boundary.Sessions.All(s => s.Disposed), $"Video: {failure} leaves no input-blocking surface");
            int attempts = boundary.Creates;
            await service.TickAsync(); await service.TickAsync();
            check(boundary.Creates == attempts, $"Video: {failure} does not retry indefinitely");
        }
        var removed = new Desktop();
        using (var service = new App.VideoWallpaperController(removed, _ => { }))
        {
            await service.StartAsync(@"C:\test.mp4"); removed.Exists = false; await service.TickAsync();
            check(service.ErrorKey == "VideoErrorFile" && removed.Last.Disposed, "Video: removed file stops safely");
        }
        var changedMonitor = new Desktop();
        using (var service = new App.VideoWallpaperController(changedMonitor, _ => { }))
        {
            await service.StartAsync(@"C:\test.mp4"); changedMonitor.MonitorCount = 2; await service.TickAsync();
            check(service.ErrorKey == "VideoErrorMonitor" && changedMonitor.Last.Disposed, "Video: hot-plug second monitor stops safely");
        }
        var slowValidation = new TaskCompletionSource();
        var slow = new Desktop { Validation = slowValidation.Task };
        var shutdown = new App.VideoWallpaperController(slow, _ => { });
        Task start = shutdown.StartAsync(@"C:\test.mp4"); shutdown.Dispose(); slowValidation.SetResult(); await start;
        check(slow.Creates == 0 && shutdown.State == App.VideoWallpaperState.Stopped, "Video: shutdown during validation discards late result");
        var slowInit = new TaskCompletionSource();
        var initializing = new Desktop { Initialization = slowInit.Task };
        shutdown = new(initializing, _ => { }); start = shutdown.StartAsync(@"C:\test.mp4");
        shutdown.Dispose(); slowInit.SetResult(); await start;
        check(initializing.Last.Disposed && !initializing.Last.Playing, "Video: shutdown during native initialization cleans candidate");
        var recovering = new Desktop(); shutdown = new(recovering, _ => { }, () => time);
        await shutdown.StartAsync(@"C:\test.mp4"); recovering.Last.Attached = false; await shutdown.TickAsync();
        recovering.Initialization = new TaskCompletionSource().Task;
        time = time.AddSeconds(1); Task repair = shutdown.TickAsync(); shutdown.Dispose(); await repair;
        check(recovering.Sessions.All(s => s.Disposed), "Video: shutdown during recovery releases all sessions");
        int afterExit = recovering.Creates; time = time.AddDays(1); await shutdown.TickAsync();
        check(recovering.Creates == afterExit, "Video: no recovery work after shutdown");
        var gate = new App.WallpaperModeOwnership();
        int old = gate.Generation;
        check(gate.AllowsImages, "Mode: existing installations default to images");
        gate.Switch(App.WallpaperOperatingMode.VideoWallpaper);
        check(!gate.AllowsImages && !gate.IsCurrentImage(old), "Mode: video excludes image scheduler and old callbacks");
        gate.Switch(App.WallpaperOperatingMode.ImageSlideshow);
        check(gate.AllowsImages && !gate.IsCurrentImage(old), "Mode: rapid round-trip cannot reuse old image lease");
        gate.Close(); check(!gate.AllowsImages, "Mode: disposed owner rejects all callbacks");
    }
    private static void Settings(Action<bool, string> check)
    {
        string key = @"Software\WallpaperControl.VideoTests-" + Guid.NewGuid().ToString("N");
        try
        {
            var settings = new App.AppSettingsStore(key);
            check(settings.LoadWallpaperOperatingMode() == App.WallpaperOperatingMode.ImageSlideshow, "Video settings: legacy default is images");
            check(settings.LoadVideoWallpaperPath() == "", "Video settings: no automatic video selection");
            settings.SaveWallpaperOperatingMode(App.WallpaperOperatingMode.VideoWallpaper);
            settings.SaveVideoWallpaperPath(@"C:\Videos\ü 日本語.mp4");
            var restored = new App.AppSettingsStore(key);
            check(restored.LoadWallpaperOperatingMode() == App.WallpaperOperatingMode.VideoWallpaper, "Video settings: mode persists");
            check(restored.LoadVideoWallpaperPath() == @"C:\Videos\ü 日本語.mp4", "Video settings: Unicode path persists");
            settings.SaveImageActiveBeforeVideo(true);
            check(restored.LoadImageActiveBeforeVideo(), "Video settings: image resume ownership survives application restart");
            settings.SaveWallpaperOperatingMode(App.WallpaperOperatingMode.ImageSlideshow);
            check(restored.LoadWallpaperOperatingMode() == App.WallpaperOperatingMode.ImageSlideshow, "Video settings: return to image mode persists");
            using var registry = Registry.CurrentUser.CreateSubKey(key); registry.SetValue("WallpaperOperatingMode", "unknown");
            check(restored.LoadWallpaperOperatingMode() == App.WallpaperOperatingMode.ImageSlideshow, "Video settings: corrupt mode defaults safely");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(key, false); }
    }
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, Members)!.GetValue(obj)!;
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Members)!.SetValue(obj, value);
    private static object? Invoke(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Members)!.Invoke(obj, args);
    private static void Pump(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(1); }
        if (!task.IsCompleted) throw new TimeoutException("Video UI test");
        task.GetAwaiter().GetResult();
    }
    private static void Ui(Action<bool, string> check)
    {
        App.Localization.RefreshAvailableLanguages();
        string original = App.Localization.CurrentLanguage;
        string widgetKey = @"Software\WallpaperControl.VideoUiTests-" + Guid.NewGuid().ToString("N");
        var resources = new System.Resources.ResourceManager("WallpaperControl.Strings", typeof(App.MainForm).Assembly);
        try
        {
            foreach (string language in new[] { "de", "en", "fr", "es", "ja" })
            {
                typeof(App.Localization).GetMethod("ApplyLanguage", Members)!.Invoke(null, new object[] { language, false });
                var ownResources = resources.GetResourceSet(language == "de" ? System.Globalization.CultureInfo.InvariantCulture : new(language), true, false);
                foreach (string key in new[] { "VideoStart", "VideoChange", "ImageStart", "VideoSelectedFile", "VideoNoSelection", "VideoSelectedImagesActive", "ImagesSelectedVideoActive", "WallpaperModeHeading", "ModeImageSlideshow", "ModeVideoWallpaper", "VideoApply", "VideoBrowse", "VideoActive", "VideoInitializing", "VideoRecovering", "VideoManualPaused", "VideoFullscreenPaused", "VideoPause", "VideoResume", "VideoErrorFile", "VideoErrorPlayback", "VideoErrorRecovery", "VideoErrorMonitor" })
                    check(!string.IsNullOrWhiteSpace(ownResources?.GetString(key)) && App.Localization.Get(key) != key, $"Video localization {language}: {key}");
            }
            using var widgets = new App.WidgetManager(() => { }, registryPath: widgetKey,
                notesStore: new App.NotesStore(Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"), "notes.json")));
            using var main = new App.MainForm(widgets, () => App.DesktopSlideshowState.Enabled | App.DesktopSlideshowState.Slideshow, () => null);
            main.Opacity = 0; main.Show();
            var desktop = new Desktop();
            var controller = new App.VideoWallpaperController(desktop, _ => { });
            Set(main, "videoWallpaper", controller);
            Field<TextBox>(main, "videoPathText").Text = @"C:\test.mp4";
            Field<ComboBox>(main, "wallpaperModeCombo").SelectedIndex = 1;
            Set(main, "customSlideshowEngineActive", true);
            using var cancelled = new CancellationTokenSource();
            Set(main, "customWallpaperCancellation", cancelled);
            DateTime configuredDeadline = Field<DateTime>(main, "customSlideshowNextChange");
            Field<ComboBox>(main, "wallpaperModeCombo").SelectedIndex = 0;
            Field<ComboBox>(main, "wallpaperModeCombo").SelectedIndex = 1;
            check(Field<Panel>(main, "videoCard").Visible && !Field<Panel>(main, "slideshowCard").Visible, "Video configuration immediately replaces image controls");
            check(Field<App.WallpaperModeOwnership>(main, "wallpaperOwnership").AllowsImages && Field<bool>(main, "customSlideshowEngineActive") && !cancelled.IsCancellationRequested && configuredDeadline == Field<DateTime>(main, "customSlideshowNextChange"), "Video selection keeps image runtime, transition and deadline");
            check(Field<Label>(main, "videoFileLabel").Text == "test.mp4", "Video primary selection shows filename only");
            check(Field<Button>(main, "videoApplyButton").Text == App.Localization.Get("VideoStart"), "Video start action localized before playback");
            Pump((Task)Invoke(main, "ApplyWallpaperModeAsync", App.WallpaperOperatingMode.VideoWallpaper)!);
            check(cancelled.IsCancellationRequested, "Video UI: entering cancels active image transition");
            main.GetType().GetField("customWallpaperCancellation", Members)!.SetValue(main, null);
            check(!Field<bool>(main, "customSlideshowEngineActive"), "Video UI: entering stops image engine");
            check(!Field<App.WallpaperModeOwnership>(main, "wallpaperOwnership").AllowsImages, "Video UI: image timer callbacks excluded");
            check(ReferenceEquals(Field<App.WidgetManager>(main, "widgetManager"), widgets), "Video UI: widget subsystem retained");
            check(!Field<Button>(main, "nextWallpaperButton").Enabled && !Field<Panel>(main, "slideshowCard").Enabled, "Video UI: image actions disabled");
            Field<ComboBox>(main, "wallpaperModeCombo").SelectedIndex = 0;
            check(Field<Panel>(main, "slideshowCard").Visible && !Field<Panel>(main, "videoCard").Visible, "Image selection immediately shows image configuration");
            check(!Field<App.WallpaperModeOwnership>(main, "wallpaperOwnership").AllowsImages && !desktop.Last.Disposed && desktop.Last.Playing, "Image selection keeps video runtime alive");
            check(controller.HasSession, "Programmatic image selection does not change the active video");
            Field<ComboBox>(main, "wallpaperModeCombo").SelectedIndex = 1;
            check(Field<Button>(main, "videoApplyButton").Text == App.Localization.Get("VideoRestart"), "Running video offers localized restart action");
            bool imageWrite = false;
            main.ApplyExplicitWallpaper(() => imageWrite = true, true);
            check(!imageWrite, "Video UI: statistics selection cannot write native wallpaper");
            DateTime deadline = Field<DateTime>(main, "customSlideshowNextChange");
            Invoke(main, "ProcessPreciseCustomSlideshowTick");
            check(Field<DateTime>(main, "customSlideshowNextChange") == deadline, "Video UI: precise scheduler cannot fire while video owns desktop");
            Pump((Task)Invoke(main, "AdvanceWallpaperAsync", App.DesktopSlideshowDirection.Forward)!);
            check(desktop.Creates == 1 && controller.State == App.VideoWallpaperState.Playing, "Video UI: manual Next cannot change video ownership");
            Pump((Task)Invoke(main, "ToggleSlideshowPauseAsync", false)!);
            check(controller.PauseReasons == App.VideoPauseReason.Manual, "Video UI: existing pause action targets video independently");
            Pump((Task)Invoke(main, "ApplyWallpaperModeAsync", App.WallpaperOperatingMode.VideoWallpaper)!);
            check(controller.State == App.VideoWallpaperState.Paused && controller.PauseReasons == App.VideoPauseReason.Manual,
                "Video UI: reapplying video preserves its manual pause policy");
            Pump((Task)Invoke(main, "ApplyWallpaperModeAsync", App.WallpaperOperatingMode.ImageSlideshow)!);
            check(desktop.Last.Disposed && Field<bool>(main, "customSlideshowEngineActive"), "Video UI: leaving disposes video and restores image engine");
            check(!Field<bool>(main, "slideshowPaused"), "Video UI: image manual pause semantics restored");
            check(Field<DateTime>(main, "customSlideshowNextChange") > DateTime.Now, "Video UI: leaving plans future deadline without catch-up");
            Set(main, "slideshowPaused", true);
            Pump((Task)Invoke(main, "ApplyWallpaperModeAsync", App.WallpaperOperatingMode.VideoWallpaper)!);
            check(controller.State == App.VideoWallpaperState.Paused, "Video UI: image manual pause carried into initial video policy");
            Pump((Task)Invoke(main, "ToggleSlideshowPauseAsync", false)!);
            Pump((Task)Invoke(main, "ApplyWallpaperModeAsync", App.WallpaperOperatingMode.ImageSlideshow)!);
            check(Field<bool>(main, "slideshowPaused"), "Video UI: changing video pause does not alter saved image manual pause");
            Set(main, "slideshowPaused", false);
            var validation = new TaskCompletionSource();
            desktop.Validation = validation.Task;
            int beforeRapid = desktop.Creates;
            Task enter = (Task)Invoke(main, "ApplyWallpaperModeAsync", App.WallpaperOperatingMode.VideoWallpaper)!;
            Task leave = (Task)Invoke(main, "ApplyWallpaperModeAsync", App.WallpaperOperatingMode.ImageSlideshow)!;
            Pump(Task.WhenAll(enter, leave)); validation.TrySetResult();
            check(desktop.Creates == beforeRapid && Field<App.WallpaperModeOwnership>(main, "wallpaperOwnership").AllowsImages,
                "Video UI: rapid switch during validation leaves images and rejects late video start");
            desktop.Validation = Task.CompletedTask;
            var transition = new TaskCompletionSource<bool>();
            var pending = Field<HashSet<Task<bool>>>(main, "imageTransitionTasks");
            pending.Add(transition.Task);
            enter = (Task)Invoke(main, "ApplyWallpaperModeAsync", App.WallpaperOperatingMode.VideoWallpaper)!;
            check(!enter.IsCompleted && !Field<App.WallpaperModeOwnership>(main, "wallpaperOwnership").AllowsImages && desktop.Creates == beforeRapid,
                "Video UI: ownership blocked before awaiting cancelled image transition");
            leave = (Task)Invoke(main, "ApplyWallpaperModeAsync", App.WallpaperOperatingMode.ImageSlideshow)!;
            transition.SetResult(false); Pump(Task.WhenAll(enter, leave)); pending.Clear();
            check(desktop.Creates == beforeRapid && Field<bool>(main, "customSlideshowEngineActive"),
                "Video UI: stale image completion during rapid mode changes cannot start video");
            Set(main, "videoMonitorCount", (Func<int>)(() => 2));
            Pump((Task)Invoke(main, "ApplyWallpaperModeAsync", App.WallpaperOperatingMode.VideoWallpaper)!);
            check(Field<App.WallpaperModeOwnership>(main, "wallpaperOwnership").AllowsImages && Field<bool>(main, "customSlideshowEngineActive") && desktop.Creates == beforeRapid,
                "Video UI: multiple monitors rejected before image ownership is disturbed");
            check(!Field<Button>(main, "videoApplyButton").Enabled && Field<Label>(main, "videoStatusLabel").Text == App.Localization.Get("VideoErrorMonitor"),
                "Video UI: unsupported monitor configuration explained and Apply disabled");
            Set(main, "videoMonitorCount", (Func<int>)(() => 1));
            Pump((Task)Invoke(main, "ApplyWallpaperModeAsync", App.WallpaperOperatingMode.ImageSlideshow)!);
            desktop.ValidationError = new InvalidDataException();
            bool imageWasActive = Field<bool>(main, "customSlideshowEngineActive");
            Pump((Task)Invoke(main, "ApplyWallpaperModeAsync", App.WallpaperOperatingMode.VideoWallpaper)!);
            check(Field<App.WallpaperModeOwnership>(main, "wallpaperOwnership").AllowsImages &&
                Field<bool>(main, "customSlideshowEngineActive") == imageWasActive &&
                main.GetType().GetField("savedImageMode", Members)!.GetValue(main) == null,
                "Phase 2 UI: invalid video selection preserves image mode and scheduler");
            desktop.ValidationError = null;
            desktop.Initialization = Task.FromException(new System.Runtime.InteropServices.COMException());
            Pump((Task)Invoke(main, "ApplyWallpaperModeAsync", App.WallpaperOperatingMode.VideoWallpaper)!);
            check(Field<App.WallpaperModeOwnership>(main, "wallpaperOwnership").AllowsImages &&
                Field<bool>(main, "customSlideshowEngineActive") == imageWasActive &&
                main.GetType().GetField("savedImageMode", Members)!.GetValue(main) == null,
                "Phase 2 UI: failed MF initialization rolls image ownership and snapshot back");
            desktop.Initialization = Task.CompletedTask;
            Pump((Task)Invoke(main, "ApplyWallpaperModeAsync", App.WallpaperOperatingMode.VideoWallpaper)!);
            var stableVideo = desktop.Last;
            controller.SetPause(App.VideoPauseReason.Manual, true);
            stableVideo.Value = 7654321;
            desktop.Initialization = Task.FromException(new System.Runtime.InteropServices.COMException());
            Field<TextBox>(main, "videoPathText").Text = @"C:\rejected.mp4";
            Pump((Task)Invoke(main, "ApplyWallpaperModeAsync", App.WallpaperOperatingMode.VideoWallpaper)!);
            check(!stableVideo.Disposed && !stableVideo.Playing && stableVideo.Value == 7654321 &&
                controller.PauseReasons == App.VideoPauseReason.Manual && !Field<App.WallpaperModeOwnership>(main, "wallpaperOwnership").AllowsImages,
                "Phase 2 UI: failed video-to-video Apply retains stable paused video ownership");
            check(Field<Button>(main, "videoPauseButton").Enabled &&
                Field<Label>(main, "videoStatusLabel").Text == App.Localization.Get("VideoErrorPlayback"),
                "Phase 2 UI: rejected switch shows error while retained video pause control remains usable");
            desktop.Initialization = Task.CompletedTask;
            Field<TextBox>(main, "videoPathText").Text = @"C:\test.mp4";
            Pump((Task)Invoke(main, "ApplyWallpaperModeAsync", App.WallpaperOperatingMode.ImageSlideshow)!);
            check(stableVideo.Disposed && Field<bool>(main, "customSlideshowEngineActive") == imageWasActive,
                "Phase 2 UI: returning to images after rejected switch frees the retained player");
            Field<ComboBox>(main, "wallpaperModeCombo").SelectedIndex = 1;
            Field<TextBox>(main, "videoPathText").Text = @"C:	est.mp4";
            Field<Button>(main, "videoApplyButton").PerformClick();
            Application.DoEvents();
            check(!Field<App.WallpaperModeOwnership>(main, "wallpaperOwnership").AllowsImages, "Video Start button explicitly enters video runtime");
            Field<ComboBox>(main, "wallpaperModeCombo").SelectedIndex = 0;
            typeof(ComboBox).GetMethod("OnSelectionChangeCommitted", Members)!.Invoke(Field<ComboBox>(main, "wallpaperModeCombo"), new object[] { EventArgs.Empty });
            Application.DoEvents();
            check(Field<App.WallpaperModeOwnership>(main, "wallpaperOwnership").AllowsImages && desktop.Last.Disposed, "Slideshow Start button explicitly stops video and restores images");
            foreach (bool dark in new[] { false, true })
            {
                Set(main, "darkMode", dark); Invoke(main, "ApplyWallpaperPageTheme");
                check(Field<Panel>(main, "videoCard").BackColor == App.AppTheme.PanelBackground(dark), $"Video UI: theme dark={dark}");
            }
            foreach (float scale in new[] { 1f, 1.5f, 2f })
            {
                Invoke(main, "ArrangeWallpaperPage", scale);
                var card = Field<Panel>(main, "videoCard");
                check(card.Controls.Cast<Control>().All(c => c.Left >= 0 && c.Right <= card.ClientSize.Width && c.Bottom <= card.ClientSize.Height), $"Video UI: controls fit at DPI scale={scale}");
            }
            Field<ComboBox>(main, "wallpaperModeCombo").SelectedIndex = 1;
            foreach (string language in new[] { "de", "en", "fr", "es", "ja" })
            {
                typeof(App.Localization).GetMethod("ApplyLanguage", Members)!.Invoke(null, new object[] { language, false });
                Invoke(main, "LocalizeVideoWallpaperUi");
                foreach (float scale in new[] { 1f, 1.5f, 2f })
                {
                    Invoke(main, "ArrangeWallpaperPage", scale);
                    var card = Field<Panel>(main, "videoCard");
                    check(card.Controls.Cast<Control>().All(c => card.ClientRectangle.Contains(c.Bounds)), $"Video UI {language}/{scale}: localized card fits");
                    string? output = Environment.GetEnvironmentVariable("WALLPAPER_VIDEO_TEST_OUTPUT");
                    if (output != null && scale == 1 && language is "de" or "ja")
                    {
                        Directory.CreateDirectory(output);
                        using Bitmap bitmap = new(main.Width, main.Height);
                        main.DrawToBitmap(bitmap, new(Point.Empty, bitmap.Size));
                        bitmap.Save(Path.Combine(output, $"video-{language}.png"));
                    }
                }
            }
        }
        finally
        {
            resources.ReleaseAllResources();
            Registry.CurrentUser.DeleteSubKeyTree(widgetKey, false);
            typeof(App.Localization).GetMethod("ApplyLanguage", Members)!.Invoke(null, new object[] { original, false });
        }
    }
}
