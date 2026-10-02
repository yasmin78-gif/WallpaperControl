extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using Video = WallpaperApp::WallpaperControl.Video;
using Microsoft.Win32;
using System.Reflection;

internal static class ProductionVideoIntegrationChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private static T Get<T>(object obj, string name) => (T)obj.GetType().GetField(name, Flags)!.GetValue(obj)!;
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Flags)!.SetValue(obj, value);
    private static Task Apply(App.MainForm form, App.WallpaperOperatingMode mode) => (Task)form.GetType().GetMethod("ApplyWallpaperModeAsync", Flags)!.Invoke(form, [mode])!;
    private static Task Engine(App.MainForm form, App.VideoEngine engine) => (Task)form.GetType().GetMethod("SelectVideoEngineAsync", Flags)!.Invoke(form, [engine])!;
    private static App.VideoWallpaperController Controller(App.MainForm form) => Get<App.VideoWallpaperController>(form, "videoWallpaper");
    private static void Pump(Task task)
    {
        var until = DateTime.UtcNow.AddSeconds(40);
        while (!task.IsCompleted && DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(2); }
        if (!task.IsCompleted) throw new TimeoutException("Production video integration");
        task.GetAwaiter().GetResult();
    }
    private static void CommitMode(App.MainForm form, int index)
    {
        var combo = Get<ComboBox>(form, "wallpaperModeCombo"); combo.SelectedIndex = index;
        typeof(ComboBox).GetMethod("OnSelectionChangeCommitted", Flags)!.Invoke(combo, [EventArgs.Empty]);
    }
    private static void ClickEngine(App.MainForm form, bool standard)
    {
        var radio = Get<RadioButton>(form, standard ? "videoStandardRadio" : "videoCompatibilityRadio");
        radio.Checked = true;
        typeof(RadioButton).GetMethod("OnClick", Flags)!.Invoke(radio, [EventArgs.Empty]);
    }
    private static void Sta(Action action)
    {
        Exception? error = null;
        Thread thread = new(() => { try { action(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error != null) throw new Exception("STA integration failure", error);
    }
    private sealed class Session : App.IVideoSession
    {
        internal bool Disposed, Presented, Playing;
        internal bool Sound;
        internal int Volume;
        internal long Value;
        internal Task Prepare = Task.CompletedTask;
        internal Task Retire = Task.CompletedTask;
        public event Action<Exception>? Failed;
        internal Action<Exception>? CapturedFailure => Failed;
        public Task InitializeAsync(CancellationToken token) => Task.CompletedTask;
        public Task PrepareAsync(CancellationToken token) => Prepare.WaitAsync(token);
        public void Present() { Presented = true; }
        public bool IsAttached(App.VideoShellGeneration shell) => !Disposed;
        public long Position => Value;
        public void Play() { Playing = true; }
        public void Pause() { Playing = false; }
        public void Seek(long position) { Value = position; }
        public void SetAudio(bool enabled, int volume) { Sound = enabled; Volume = volume; }
        public void Dispose() { Disposed = true; Presented = Playing = false; Failed = null; }
        public async ValueTask DisposeAsync() { await Retire; Dispose(); }
    }
    private sealed class Desktop : App.IVideoDesktop
    {
        internal readonly List<Session> Sessions = [];
        internal Task Prepare = Task.CompletedTask;
        internal bool Missing, Reject;
        internal Action? BeforeCreate;
        public string BackendName => "Integration fake";
        public int MonitorCount => 1;
        public bool FileExists(string path) => !Missing;
        public Task ValidateAsync(string path, CancellationToken token) => Reject ? Task.FromException(new InvalidDataException("invalid video")) : Task.CompletedTask;
        public App.VideoShellGeneration? ResolveShell() => new(1, 2, 3, 4, 5);
        public App.IVideoSession Create(string path, App.VideoShellGeneration shell)
        { BeforeCreate?.Invoke(); var s = new Session { Prepare = Prepare }; Sessions.Add(s); return s; }
        internal Session Last => Sessions[^1];
    }
    internal static void Run(Action<bool, string> check)
    {
        string key = @"Software\WallpaperControl.ProductionIntegration-" + Guid.NewGuid().ToString("N");
        try
        {
            var settings = new WallpaperControl.AppSettingsStore(key);
            check(settings.LoadVideoEngine() == WallpaperControl.VideoEngine.Standard, "New settings default Standard");
            settings.SaveVideoWallpaperPath("retained.mp4");
            check(settings.LoadVideoEngine() == WallpaperControl.VideoEngine.Standard, "Existing settings without engine default Standard");
            foreach (var engine in new[] { WallpaperControl.VideoEngine.Standard, WallpaperControl.VideoEngine.Compatibility })
            { settings.SaveVideoEngine(engine); check(new WallpaperControl.AppSettingsStore(key).LoadVideoEngine() == engine, "Stable enum preference restores " + engine); }
            using var registry = Registry.CurrentUser.OpenSubKey(key, true)!;
            registry.SetValue("VideoEngine", "unknown");
            check(settings.LoadVideoEngine() == WallpaperControl.VideoEngine.Standard && settings.LoadVideoWallpaperPath() == "retained.mp4", "Unknown preference defaults Standard without changing video path");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(key, false); }
        Sta(() => Ui(check));
    }
    private static void Ui(Action<bool, string> check)
    {
        App.Localization.RefreshAvailableLanguages();
        string original = App.Localization.CurrentLanguage;
        string key = @"Software\WallpaperControl.ProductionUi-" + Guid.NewGuid().ToString("N");
        using var widgets = new App.WidgetManager(() => { }, registryPath: key,
            notesStore: new App.NotesStore(Path.Combine(Path.GetTempPath(), "wc-production-" + Guid.NewGuid().ToString("N"), "notes.json")));
        using var form = new App.MainForm(widgets, () => App.DesktopSlideshowState.Enabled | App.DesktopSlideshowState.Slideshow, () => null);
        form.Opacity = 0; form.Show();
        Set(form, "customSlideshowEngineActive", true);
        var a = new Desktop(); var b = new Desktop(); int selections = 0;
        bool fallback = false;
        form.SelectVideoBackendForTests = engine =>
        {
            selections++;
            return Task.FromResult(new Video.VideoBackendSelection(engine == App.VideoEngine.Standard && !fallback ? a : b,
                fallback ? App.VideoEngine.Compatibility : engine, fallback, fallback ? "test runtime failure" : null));
        };
        try
        {
            check(Get<RadioButton>(form, "videoStandardRadio").Checked, "Default UI prefers Standard");
            Get<ComboBox>(form, "wallpaperModeCombo").SelectedIndex = 1;
            check(selections == 0 && Get<App.WallpaperModeOwnership>(form, "wallpaperOwnership").AllowsImages, "Programmatic mode synchronization does not activate");
            CommitMode(form, 1);
            check(selections == 0 && Get<App.WallpaperModeOwnership>(form, "wallpaperOwnership").AllowsImages && Get<Panel>(form, "videoCard").Visible,
                "Empty configured video opens UI and retains safe images");
            check(Get<Label>(form, "videoStatusLabel").Text == App.Localization.Get("VideoChooseFileSafe"), "Empty video has useful status and no error");
            ClickEngine(form, false);
            check(selections == 0 && Get<App.VideoEngine>(form, "preferredVideoEngine") == App.VideoEngine.Compatibility, "Engine choice in images only sets preference");
            ClickEngine(form, true);
            Get<TextBox>(form, "videoPathText").Text = @"C:\a.mp4";
            a.Missing = true; CommitMode(form, 1);
            check(!Controller(form).HasSession && Get<App.WallpaperModeOwnership>(form, "wallpaperOwnership").AllowsImages, "Missing video retains images");
            check(Get<Panel>(form, "videoCard").Visible && Get<Label>(form, "videoStatusLabel").Text == App.Localization.Get("VideoErrorFile"), "Missing path keeps replacement UI and existing error");
            a.Missing = false; CommitMode(form, 1);
            check(Controller(form).HasSession && a.Sessions.Count == 1 && Get<App.WallpaperModeOwnership>(form, "wallpaperOwnership").Mode == App.WallpaperOperatingMode.VideoWallpaper,
                "Genuine Video selection starts configured Standard immediately");
            check(a.Last.Value == 0, "Initial video starts at zero");
            int before = selections;
            Get<RadioButton>(form, "videoCompatibilityRadio").Checked = true;
            Get<ComboBox>(form, "wallpaperModeCombo").SelectedIndex = 0;
            Get<ComboBox>(form, "wallpaperModeCombo").SelectedIndex = 1;
            check(selections == before && !a.Last.Disposed, "Programmatic radio and mode changes never restart");
            var resources = new System.Resources.ResourceManager("WallpaperControl.Strings", typeof(App.MainForm).Assembly);
            foreach (string language in new[] { "de", "en", "fr", "es", "ja" })
            {
                typeof(App.Localization).GetMethod("ApplyLanguage", Flags)!.Invoke(null, [language, false]);
                check(App.Localization.CurrentLanguage == language, "UI actually switched to " + language);
                form.GetType().GetMethod("LocalizeVideoWallpaperUi", Flags)!.Invoke(form, null);
                var set = resources.GetResourceSet(language == "de" ? System.Globalization.CultureInfo.InvariantCulture : new(language), true, false)!;
                foreach (string resource in new[] { "VideoEngineHeading", "VideoEngineStandard", "VideoEngineCompatibility", "VideoEngineStandardDescription", "VideoEngineCompatibilityDescription", "VideoEngineFallback", "VideoEngineActive", "VideoRestart", "VideoChooseFileSafe" })
                    check(!string.IsNullOrWhiteSpace(set.GetString(resource)), "Own translation " + language + ": " + resource);
                check(Get<RadioButton>(form, "videoStandardRadio").Text == App.Localization.Get("VideoEngineStandard") && selections == before, "Localized controls preserve engine " + language);
            }
            Get<CheckBox>(form, "videoSoundCheck").Checked = true;
            Get<TrackBar>(form, "videoVolumeSlider").Value = 37;
            Controller(form).SetPause(App.VideoPauseReason.Manual, true);
            Controller(form).SetPause(App.VideoPauseReason.Fullscreen, true);
            Get<App.FullscreenPausePolicy>(form, "fullscreenPolicy").Update(true, true, DateTime.UtcNow);
            var old = a.Last; var stale = old.CapturedFailure;
            b.BeforeCreate = () => check(old.Disposed, "Old Standard fully retired before Compatibility creates session");
            ClickEngine(form, false);
            check(selections == before + 1 && b.Last.Value == 0 && Controller(form).ActivePath == @"C:\a.mp4", "Standard to Compatibility restarts same video once at zero");
            check(Controller(form).PauseReasons == (App.VideoPauseReason.Manual | App.VideoPauseReason.Fullscreen) && !b.Last.Playing, "Engine change preserves independent pause reasons");
            check(b.Last.Sound && b.Last.Volume == 37, "Engine change preserves sound and volume");
            stale?.Invoke(new Exception("late retired backend"));
            check(Controller(form).HasSession && Controller(form).State == App.VideoWallpaperState.Paused, "Retired backend event cannot overwrite current state");
            Controller(form).SetPause(App.VideoPauseReason.Manual, false);
            check(!b.Last.Playing, "Clearing manual alone leaves fullscreen pause");
            Get<App.FullscreenPausePolicy>(form, "fullscreenPolicy").Update(true, false, DateTime.UtcNow.AddSeconds(10));
            Get<App.FullscreenPausePolicy>(form, "fullscreenPolicy").Update(true, false, DateTime.UtcNow.AddSeconds(11));
            Controller(form).SetPause(App.VideoPauseReason.Fullscreen, false);
            var oldB = b.Last;
            a.BeforeCreate = () => check(oldB.Disposed, "Old Compatibility retired before Standard creates session");
            ClickEngine(form, true);
            check(Controller(form).HasSession && a.Last.Value == 0 && selections == before + 2, "Compatibility to Standard restarts exactly once");
            check(a.Last.Playing, "Cleared fullscreen and manual policy resumes replacement Standard");
            a.BeforeCreate = null; b.BeforeCreate = null;
            a.Reject = true; int selected = selections;
            Pump(Apply(form, App.WallpaperOperatingMode.VideoWallpaper));
            check(selections == selected && Get<Video.VideoBackendSelection?>(form, "currentVideoBackend")!.Value.Effective == App.VideoEngine.Standard,
                "Ordinary media error never selects another backend");
            check(Controller(form).HasSession, "Invalid replacement retains previously stable video");
            a.Reject = false;
            var current = a.Last; current.Value = 9876;
            CommitMode(form, 0);
            check(current.Disposed && Get<App.WallpaperModeOwnership>(form, "wallpaperOwnership").AllowsImages && Get<bool>(form, "customSlideshowEngineActive"), "Genuine Image selection automatically stops video and restores slideshow");
            CommitMode(form, 1);
            check(a.Last != current && a.Last.Value == 0 && Controller(form).HasSession, "Re-entering video starts from beginning without Start button");
            check(Get<ComboBox>(form, "wallpaperModeCombo").SelectedIndex == 1 && !Get<Panel>(form, "slideshowCard").Visible, "Normal selected and active modes agree");
            CommitMode(form, 0); Pump(Engine(form, App.VideoEngine.Compatibility));
            selected = selections; CommitMode(form, 1);
            check(selections == selected + 1 && Get<Video.VideoBackendSelection?>(form, "currentVideoBackend")!.Value.Effective == App.VideoEngine.Compatibility,
                "Preference changed in images is honored on next activation");
            CommitMode(form, 0); fallback = true; Pump(Engine(form, App.VideoEngine.Standard)); CommitMode(form, 1);
            check(Get<RadioButton>(form, "videoStandardRadio").Checked && Get<Label>(form, "videoEngineStatus").Text == App.Localization.Get("VideoEngineFallback"), "Fallback visibly distinguishes preferred Standard and effective Compatibility");
            check(Get<App.VideoEngine>(form, "preferredVideoEngine") == App.VideoEngine.Standard, "Transient fallback does not overwrite preference");
            fallback = false; ClickEngine(form, true);
            check(Get<Video.VideoBackendSelection?>(form, "currentVideoBackend")!.Value.Effective == App.VideoEngine.Standard, "Selecting Standard again retries recovered runtime");
            // A slow candidate must be retired before a newer mode request publishes.
            var prepare = new TaskCompletionSource(); a.Prepare = prepare.Task;
            var pending = Apply(form, App.WallpaperOperatingMode.VideoWallpaper);
            var obsolete = a.Last;
            var images = Apply(form, App.WallpaperOperatingMode.ImageSlideshow);
            a.Prepare = Task.CompletedTask;
            var latest = Apply(form, App.WallpaperOperatingMode.VideoWallpaper);
            Pump(Task.WhenAll(pending, images, latest));
            check(obsolete.Disposed && Controller(form).HasSession && Get<App.WallpaperModeOwnership>(form, "wallpaperOwnership").Mode == App.WallpaperOperatingMode.VideoWallpaper,
                "Rapid Video Image Video cancels stale candidate and publishes latest");
            check(a.Sessions.Concat(b.Sessions).Count(s => !s.Disposed && s.Presented) == 1, "Exactly one fake session remains published after rapid requests");
            Pump(Apply(form, App.WallpaperOperatingMode.ImageSlideshow));
            check(a.Sessions.Concat(b.Sessions).All(s => s.Disposed), "Returning to images retires every session");
            // Exercise the same startup entry point used by MainForm_Shown.
            var startup = form.GetType().GetMethod("RestoreStartupVideoAsync", Flags)!;
            selected = selections;
            Pump((Task)startup.Invoke(form, [App.WallpaperOperatingMode.ImageSlideshow, @"C:\a.mp4"])!);
            check(selections == selected && !Controller(form).HasSession, "Image startup never activates a hidden video");
            Pump((Task)startup.Invoke(form, [App.WallpaperOperatingMode.VideoWallpaper, @"C:\a.mp4"])!);
            int restored = a.Sessions.Count;
            Pump((Task)startup.Invoke(form, [App.WallpaperOperatingMode.VideoWallpaper, @"C:\a.mp4"])!);
            check(Controller(form).HasSession && a.Sessions.Count == restored, "Saved Video startup entry restores exactly once");
            b.Reject = true; current = a.Last;
            Pump(Engine(form, App.VideoEngine.Compatibility));
            check(!current.Disposed && Controller(form).HasSession, "Failed engine media validation retains stable Standard");
            b.Reject = false;
            Pump(Engine(form, App.VideoEngine.Standard));
            var retire = new TaskCompletionSource(); current = a.Last; current.Retire = retire.Task;
            int bCount = b.Sessions.Count;
            Task switching = Engine(form, App.VideoEngine.Compatibility);
            check(!switching.IsCompleted && b.Sessions.Count == bCount, "New engine waits for acknowledged asynchronous retirement");
            Controller(form).SetPause(App.VideoPauseReason.Manual, true);
            retire.SetResult(); Pump(switching);
            check(current.Disposed && b.Sessions.Count == bCount + 1, "Only acknowledged retirement permits replacement creation");
            check((Controller(form).PauseReasons & App.VideoPauseReason.Manual) != 0 && !b.Last.Playing,
                "Manual policy changes during retirement carry into replacement");
            Pump(Apply(form, App.WallpaperOperatingMode.ImageSlideshow));
            // The startup guard belongs to each form; persisted Compatibility
            // follows the same entry point as Standard, without a CLI override.
            string startupKey = key + "-startup";
            using (var startupWidgets = new App.WidgetManager(() => { }, registryPath: startupKey,
                notesStore: new App.NotesStore(Path.Combine(Path.GetTempPath(), "wc-startup-" + Guid.NewGuid().ToString("N"), "notes.json"))))
            using (var startupForm = new App.MainForm(startupWidgets, () => App.DesktopSlideshowState.Enabled, () => null))
            {
                startupForm.Opacity = 0; startupForm.Show();
                Set(startupForm, "preferredVideoEngine", App.VideoEngine.Compatibility);
                int startupCalls = 0;
                var startupDesktop = new Desktop();
                startupForm.SelectVideoBackendForTests = preference =>
                {
                    check(preference == App.VideoEngine.Compatibility, "Compatibility startup passes saved preference to backend factory");
                    startupCalls++;
                    return Task.FromResult(new Video.VideoBackendSelection(startupDesktop, preference, false, null));
                };
                var entry = startupForm.GetType().GetMethod("RestoreStartupVideoAsync", Flags)!;
                Pump((Task)entry.Invoke(startupForm, [App.WallpaperOperatingMode.VideoWallpaper, @"C:\saved.mp4"])!);
                Pump((Task)entry.Invoke(startupForm, [App.WallpaperOperatingMode.VideoWallpaper, @"C:\saved.mp4"])!);
                check(startupCalls == 1 && startupDesktop.Sessions.Count == 1 && Controller(startupForm).ActivePath == @"C:\saved.mp4",
                    "Saved Compatibility video starts exactly once at startup");
                Pump(Apply(startupForm, App.WallpaperOperatingMode.ImageSlideshow));
            }
            Registry.CurrentUser.DeleteSubKeyTree(startupKey, false);
            Get<ComboBox>(form, "wallpaperModeCombo").SelectedIndex = 1;
            // Render the actual controls for a focused layout review.
            string screenshots = Path.Combine(Path.GetTempPath(), "wc-production-ui");
            Directory.CreateDirectory(screenshots);
            foreach (string language in new[] { "de", "fr", "ja" })
            {
                typeof(App.Localization).GetMethod("ApplyLanguage", Flags)!.Invoke(null, [language, false]);
                check(App.Localization.CurrentLanguage == language, "UI actually switched to " + language);
                form.GetType().GetMethod("LocalizeVideoWallpaperUi", Flags)!.Invoke(form, null);
                using var image = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                image.Save(Path.Combine(screenshots, language + ".png"));
                check(Get<Label>(form, "videoCompatibilityDescription").Bottom <= Get<Panel>(form, "videoCard").Height,
                    "Measured engine descriptions stay inside card " + language);
            }
        }
        finally
        {
            typeof(App.Localization).GetMethod("ApplyLanguage", Flags)!.Invoke(null, [original, false]);
            Registry.CurrentUser.DeleteSubKeyTree(key, false);
        }
    }
    internal static void Native(string fixtures, Action<bool, string> check)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            using var dispatcher = new Form { ShowInTaskbar = false, Opacity = 0 };
            dispatcher.Shown += async (_, _) =>
            {
                dispatcher.Hide();
                try { await NativeAsync(dispatcher, fixtures, check); }
                catch (Exception ex) { error = ex; }
                finally { dispatcher.Close(); }
            };
            Application.Run(dispatcher);
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error != null) throw new Exception("Native integration failure", error);
    }
    private static async Task NativeAsync(Control dispatcher, string fixtures, Action<bool, string> check)
    {
            var missing = await (Video.VideoBackendFactory.SelectAsync(dispatcher, App.VideoEngine.Standard,
                ["--video-backend=libmpv", "--mpv-runtime=" + Path.Combine(fixtures, "absent-runtime", "libmpv-2.dll")]));
            check(missing.Fallback && missing.Effective == App.VideoEngine.Compatibility && missing.Reason!.Contains("Exception"), "Missing native package produces explicit Compatibility result with reason");
            string corruptRuntime = Path.Combine(Path.GetTempPath(), "wc-corrupt-runtime-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(corruptRuntime);
            try
            {
                File.WriteAllText(Path.Combine(corruptRuntime, "libmpv-2.dll"), "corrupt runtime");
                var corrupt = await Video.VideoBackendFactory.SelectAsync(dispatcher, App.VideoEngine.Standard,
                    ["--video-backend=libmpv", "--mpv-runtime=" + Path.Combine(corruptRuntime, "libmpv-2.dll")]);
                check(corrupt.Fallback && corrupt.Reason!.Contains("hash mismatch"), "Corrupt runtime falls back with precise hash reason");
            }
            finally { Directory.Delete(corruptRuntime, true); }
            var forced = await Video.VideoBackendFactory.SelectAsync(dispatcher, App.VideoEngine.Standard, ["--video-backend=mfplay"]);
            check(forced.Effective == App.VideoEngine.Compatibility && !forced.Fallback, "Explicit development override has deterministic precedence");
            bool rejected = false;
            try { await Video.VideoBackendFactory.SelectAsync(dispatcher, App.VideoEngine.Standard, ["--mpv-runtime=C:\\arbitrary.dll"]); }
            catch (ArgumentException) { rejected = true; }
            check(rejected, "Runtime path alone cannot accidentally override normal settings");
            var standard = await (Video.VideoBackendFactory.SelectAsync(dispatcher, App.VideoEngine.Standard, []));
            check(standard.Effective == App.VideoEngine.Standard && !standard.Fallback, "Normal no-flag selection loads packaged Standard");
            check(Video.MpvClient.LiveClients == 0 && Video.MpvVideoSession.LiveSessions == 0, "Identity probe fully retired without playback session");
            using var a = new App.VideoWallpaperController(standard.Desktop, Console.WriteLine);
            var compatibility = await (Video.VideoBackendFactory.SelectAsync(dispatcher, App.VideoEngine.Compatibility, []));
            check(compatibility.Desktop.BackendName.Contains("MFPlay"), "Compatibility selects retained MFPlay");
            using var b = new App.VideoWallpaperController(compatibility.Desktop, Console.WriteLine);
            string path = Path.Combine(fixtures, "loop-colors.mp4");
            await a.StartAsync(path); check(a.HasSession, "Packaged Standard prepares and presents real MP4");
            check(Video.MpvClient.LiveClients == 1 && Video.MpvVideoSession.LiveSessions == 1, "Exactly one native Standard client and session active");
            a.SetAudio(true, 25); a.SetPause(App.VideoPauseReason.Manual, true);
            await a.StopAsync(); check(Video.MpvClient.LiveClients == 0 && Video.MpvVideoSession.LiveSessions == 0, "Standard native retirement completes before MFPlay start");
            b.SetAudio(true, 25); b.SetPause(App.VideoPauseReason.Manual, true);
            await b.StartAsync(path); check(b.HasSession && b.State == App.VideoWallpaperState.Paused, "Same video starts through Compatibility with pause/audio policy");
            await b.StopAsync(); check(!b.HasSession, "MFPlay retired before restarting Standard");
            await a.StartAsync(path); check(a.HasSession && a.State == App.VideoWallpaperState.Paused, "Standard can restart after MFPlay with preserved manual pause");
            await a.StopAsync(); check(Video.MpvClient.LiveClients == 0 && Video.MpvVideoSession.LiveSessions == 0, "Native shutdown leaves zero Standard-owned playback objects");
            await a.StartAsync(Path.Combine(fixtures, "corrupt.mp4"));
            check(!a.HasSession && a.State == App.VideoWallpaperState.Failed && !standard.Fallback, "Corrupt media fails normally without native backend hopping");
    }
    private static Video.VideoBackendSelection PumpSelection(Task<Video.VideoBackendSelection> task) { Pump(task); return task.Result; }
}
