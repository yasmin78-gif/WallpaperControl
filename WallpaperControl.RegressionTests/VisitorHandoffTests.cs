extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using Video = WallpaperApp::WallpaperControl.Video;
using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Reflection;

internal static class VisitorHandoffTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Fields)!.GetValue(target)!;
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Fields)!.SetValue(target, value);
    private static Task Invoke(object target, string method) => (Task)target.GetType().GetMethod(method, Fields)!.Invoke(target, null)!;
    private static void Picture(string path, Color color)
    { using Bitmap image = new(24, 16); using (Graphics g = Graphics.FromImage(image)) g.Clear(color); image.Save(path, ImageFormat.Png); }
    private static Color Pixel(string path) { using Bitmap image = new(path); return image.GetPixel(image.Width / 2, image.Height / 2); }
    internal static void Run(Action<bool, string> check) => OnUi(() => Checks(check));
    internal static void Native(string video, string dll, Action<bool, string> check) => OnUi(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "WallpaperControl-HandoffNative-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string? selected = null;
            var shell = new App.VisitorWallpaperHandoff(Path.Combine(root, "shell"), path => selected = path);
            check(await shell.PublishVideoAsync(video, null, () => true, CancellationToken.None)
                && selected != null && Pixel(selected).B > 150 && Pixel(selected).R < 80,
                "Visitor native handoff: Windows extracts an actual blue video frame instead of a file icon");
            using Form target = new() { StartPosition = FormStartPosition.Manual, Location = new(-20000, -20000), ClientSize = new(320, 180), ShowInTaskbar = false };
            target.Show();
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var client = new Video.MpvClient(Video.MpvRuntime.Open(dll), target.Handle, item =>
            { if (item.Type == 2 && item.Text.Contains("first video frame after restart shown", StringComparison.Ordinal)) ready.TrySetResult(); });
            await client.Initialized.WaitAsync(TimeSpan.FromSeconds(10));
            await client.CommandAsync("loadfile", video).WaitAsync(TimeSpan.FromSeconds(10));
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await client.SetAsync("pause", "yes");
            string captured = Path.Combine(root, "mpv.png");
            await client.SaveVideoFrameAsync(captured, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            check(File.Exists(captured) && Pixel(captured).B > 150 && Pixel(captured).R < 80,
                "Visitor native handoff: pinned mpv exports the decoded video frame through its screenshot command");
        }
        finally
        {
            string full = Path.GetFullPath(root);
            if (Path.GetDirectoryName(full) == Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))
                && Path.GetFileName(full).StartsWith("WallpaperControl-HandoffNative-", StringComparison.Ordinal)) Directory.Delete(full, true);
        }
    });
    private static void OnUi(Func<Task> work)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            using Form loop = new() { ShowInTaskbar = false, Location = new(-20000, -20000), Size = new(16, 16) };
            loop.Shown += async (_, _) => { try { await work(); } catch (Exception ex) { failure = ex; } finally { loop.Close(); } };
            Application.Run(loop);
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) throw failure;
    }
    private static async Task Checks(Action<bool, string> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "WallpaperControl-Handoff-" + Guid.NewGuid().ToString("N"));
        string registry = @"Software\WallpaperControl.Handoff-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(root);
        try
        {
            string blue = Path.Combine(root, "visitor.png"), green = Path.Combine(root, "folder-next.png"), video = Path.Combine(root, "visitor.mp4");
            Picture(blue, Color.Blue); Picture(green, Color.Lime); File.WriteAllText(video, "Fake video fixture");
            string? native = null;
            var handoff = new App.VisitorWallpaperHandoff(Path.Combine(root, "cache"), path => native = path, readNative: () => native);
            check(handoff.PublishImage(blue) && native == handoff.StaticPath && Pixel(native).B == 255,
                "Visitor handoff: real visitor pixels are copied to a durable native image");
            for (int i = 0; i < 8; i++) handoff.PublishImage(i % 2 == 0 ? green : blue);
            check(Directory.GetFiles(Path.Combine(root, "cache"), "*.bmp").Length <= 3 && File.Exists(native),
                "Visitor handoff: checkpoint storage stays bounded and protects native image slots");
            var reopened = new App.VisitorWallpaperHandoff(Path.Combine(root, "cache"), path => native = path);
            check(reopened.HasStaticImage && reopened.PublishExistingOrNeutral() && Pixel(native!).B == 255,
                "Visitor handoff: a new process can reuse the last safe static image");
            int extracted = 0;
            var export = new App.VisitorWallpaperHandoff(Path.Combine(root, "export"), path => native = path,
                (_, destination, _) => { extracted++; Picture(destination, Color.Lime); return Task.FromResult(true); });
            check(await export.PublishVideoAsync(video, (path, _) => { Picture(path, Color.Blue); return Task.FromResult(true); }, () => true, CancellationToken.None)
                && Pixel(native!).B == 255 && extracted == 0, "Visitor handoff: live video frame takes priority over thumbnail extraction");
            check(await export.PublishVideoAsync(video, (_, _) => Task.FromResult(false), () => true, CancellationToken.None)
                && Pixel(native!).G == 255 && extracted == 1, "Visitor handoff: failed live capture falls back to a video thumbnail");
            string retained = native!;
            check(!await export.PublishVideoAsync(video, (path, _) => { Picture(path, Color.Red); return Task.FromResult(true); }, () => false, CancellationToken.None)
                && native == retained && Pixel(native!).G == 255, "Visitor handoff: superseded frame preparation cannot replace the committed wallpaper");
            using (var cancelled = new CancellationTokenSource())
            {
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var pending = export.PublishVideoAsync(video, async (path, _) =>
                { await release.Task; Picture(path, Color.Red); finished.TrySetResult(); return true; }, () => true, cancelled.Token);
                cancelled.Cancel(); bool aborted = false;
                try { await pending; } catch (OperationCanceledException) { aborted = true; }
                release.TrySetResult(); await finished.Task;
                check(aborted && native == retained && Pixel(native!).G == 255,
                    "Visitor handoff: late encoder completion after cancellation cannot publish a new wallpaper");
            }
            var noImage = new App.VisitorWallpaperHandoff(Path.Combine(root, "invalid"), path => native = path,
                (_, _, _) => Task.FromResult(false));
            check(!await noImage.PublishVideoAsync(video, (_, _) => throw new IOException("Decoder failed"), () => true, CancellationToken.None)
                && noImage.PublishNeutral() && Pixel(native!).R == 32, "Visitor handoff: decoder and extraction failure use neutral pixels, never normal media");

            // The source remains available only for the displayed selection; shutdown must retain its cached copy if removed.
            using (App.WidgetManager widgets = new(() => { }, registryPath: registry))
            using (App.MainForm form = new(widgets, () => 0, () => green))
            {
                var service = Get<App.VisitorModeService>(form, "visitorMode");
                form.VisitorHandoffForTests = handoff;
                await service.ConfigureAsync(s => s with { Source = App.VisitorSourceKind.Image, Path = blue });
                await service.SetActiveAsync(true);
                check(Pixel(native!).B == 255, "Visitor crash safety: image is already native before any shutdown callback");
                Picture(blue, Color.Red); Set(form, "visitorClosing", true);
                await Invoke(form, "PublishVisitorExitWallpaperAsync");
                check(Pixel(native!).B == 255, "Visitor exit: changed source file cannot replace the pixels that were actually displayed");
                File.Delete(blue);
                Set(form, "visitorClosing", true);
                await Invoke(form, "PublishVisitorExitWallpaperAsync");
                check(Pixel(native!).B == 255 && service.Active && service.Settings.Resume != null,
                    "Visitor exit: removed image still survives from the app-owned copy, with active state and normal snapshot retained");
            }
            check(Pixel(native!).B == 255, "Visitor cleanup: disposing the form cannot overwrite the safe native image");
            Picture(blue, Color.Blue);

            string folder = Path.Combine(root, "folder"); Directory.CreateDirectory(folder);
            string folderBlue = Path.Combine(folder, "a.png"), folderGreen = Path.Combine(folder, "b.png");
            Picture(folderBlue, Color.Blue); Picture(folderGreen, Color.Lime);
            using (App.WidgetManager widgets = new(() => { }, registryPath: registry))
            using (App.MainForm form = new(widgets, () => 0, () => green))
            {
                form.VisitorHandoffForTests = new(Path.Combine(root, "folder-cache"), path => native = path);
                var service = Get<App.VisitorModeService>(form, "visitorMode");
                await service.ConfigureAsync(s => s with { Source = App.VisitorSourceKind.Folder, Path = folder }); await service.SetActiveAsync(true);
                await Invoke(form, "AdvanceVisitorFolderAsync"); Set(form, "visitorClosing", true);
                await Invoke(form, "StopVisitorAndCloseAsync");
                check(Pixel(native!).G == 255 && Get<string>(form, "visitorCurrentPath") == folderGreen,
                    "Visitor folder exit: native wallpaper retains the last displayed image, not the first folder entry");
            }
            File.Delete(folderGreen); string folderVideo = Path.Combine(folder, "b.mp4"); File.WriteAllText(folderVideo, "Fake video fixture");
            var switching = new FakeDesktop(new());
            using (App.WidgetManager widgets = new(() => { }, registryPath: registry))
            using (App.MainForm form = new(widgets, () => 0, () => green))
            {
                form.VisitorHandoffForTests = new(Path.Combine(root, "switch-cache"), path => native = path);
                Set(form, "videoMonitorCount", (Func<int>)(() => 1));
                Set(form, "videoWallpaper", new App.VideoWallpaperController(switching, _ => { }));
                var service = Get<App.VisitorModeService>(form, "visitorMode");
                await service.ConfigureAsync(s => s with { Source = App.VisitorSourceKind.Folder, Path = folder }); await service.SetActiveAsync(true);
                switching.ValidationGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Task advancing = Invoke(form, "AdvanceVisitorFolderAsync");
                check(Get<bool>(form, "visitorAdvanceRunning"), "Visitor folder exit: media change is pending during the shutdown fixture");
                Set(form, "visitorClosing", true); await Invoke(form, "StopVisitorAndCloseAsync"); await advancing;
                check(Pixel(native!).B == 255 && service.Active && !Get<bool>(form, "visitorAdvanceRunning"),
                    "Visitor folder exit: cancelled video preparation cannot replace the last displayed image or reactivate normal playback");
                switching.ValidationGate.TrySetResult();
            }

            using (App.WidgetManager widgets = new(() => { }, registryPath: registry))
            using (App.MainForm form = new(widgets, () => 0, () => green))
            {
                form.VisitorHandoffForTests = new(Path.Combine(root, "missing-cache"), path => native = path);
                var service = Get<App.VisitorModeService>(form, "visitorMode");
                await service.ConfigureAsync(s => s with { Source = App.VisitorSourceKind.Image, Path = Path.Combine(root, "missing.png") }); await service.SetActiveAsync(true);
                Set(form, "visitorClosing", true); await Invoke(form, "StopVisitorAndCloseAsync");
                check(Pixel(native!).R == 32 && Pixel(native!).G == 38 && service.Active,
                    "Visitor invalid-source exit: neutral native fallback is retained with visitor mode still active");
            }
            int normalWrites = 0;
            using (App.WidgetManager widgets = new(() => { }, registryPath: registry))
            using (App.MainForm form = new(widgets, () => 0, () => green))
                form.VisitorHandoffForTests = new(Path.Combine(root, "normal-cache"), _ => normalWrites++);
            check(normalWrites == 0, "Visitor handoff: ordinary disposal outside visitor mode performs no visitor wallpaper writes");

            var events = new List<string>();
            var desktop = new FakeDesktop(events);
            using (App.WidgetManager widgets = new(() => { }, registryPath: registry))
            using (App.MainForm form = new(widgets, () => 0, () => green))
            {
                var service = Get<App.VisitorModeService>(form, "visitorMode");
                form.VisitorHandoffForTests = new(Path.Combine(root, "live"), path => { native = path; events.Add("native"); });
                Set(form, "videoMonitorCount", (Func<int>)(() => 1));
                var controller = new App.VideoWallpaperController(desktop, _ => { }); Set(form, "videoWallpaper", controller);
                await service.ConfigureAsync(s => s with { Source = App.VisitorSourceKind.Video, Path = video });
                await service.SetActiveAsync(true);
                check(Pixel(native!).B == 255 && desktop.Session?.Saves == 1,
                    "Visitor crash safety: initial video frame is native before activation completes");
                desktop.Session!.Color = Color.Lime;
                Set(form, "visitorCheckpointDue", DateTime.MinValue);
                await (Task)form.GetType().GetMethod("CheckpointVisitorVideoAsync", Fields)!.Invoke(form, new object[] { false })!;
                check(Pixel(native!).G == 255 && desktop.Session.Saves == 2,
                    "Visitor crash safety: running video refreshes the native checkpoint without stopping playback");
                desktop.Session.Color = Color.Blue; events.Clear(); Set(form, "visitorClosing", true);
                await Invoke(form, "StopVisitorAndCloseAsync");
                check(events.IndexOf("native") >= 0 && events.IndexOf("dispose") > events.IndexOf("native") && Pixel(native!).B == 255,
                    "Visitor exit: newest video frame is handed to Windows before the video session is disposed");
                check(service.Active && service.Settings.Resume != null, "Visitor exit: shutdown never deactivates the persisted visitor state");
            }
            string privateVideo = Path.Combine(root, "normal-private.mp4"); File.WriteAllText(privateVideo, "Fake normal video");
            var untrustedDesktop = new FakeDesktop(new());
            using (App.WidgetManager widgets = new(() => { }, registryPath: registry))
            using (App.MainForm form = new(widgets, () => 0, () => green))
            {
                form.VisitorHandoffForTests = new(Path.Combine(root, "approved-only"), path => native = path,
                    (source, destination, _) => { if (source != video) throw new Exception("Unapproved source"); Picture(destination, Color.Blue); return Task.FromResult(true); });
                Set(form, "videoMonitorCount", (Func<int>)(() => 1));
                var controller = new App.VideoWallpaperController(untrustedDesktop, _ => { }); Set(form, "videoWallpaper", controller);
                var service = Get<App.VisitorModeService>(form, "visitorMode");
                await service.ConfigureAsync(s => s with { Source = App.VisitorSourceKind.Video, Path = video }); await service.SetActiveAsync(true);
                await controller.StartAsync(privateVideo); untrustedDesktop.Session!.Color = Color.Red;
                var privateSession = untrustedDesktop.Session;
                Set(form, "visitorClosing", true); await Invoke(form, "StopVisitorAndCloseAsync");
                check(privateSession.Saves == 0 && Pixel(native!).B == 255,
                    "Visitor exit: a concurrently restored normal video is never captured as a safe visitor frame");
            }

            var frame = new Video.MfVideoFrame(new Size(1, 2), 0, new byte[] { 255, 0, 0, 0, 0, 0, 255, 0 });
            string png = Path.Combine(root, "evr.png"); frame.SavePng(png);
            using (Bitmap image = new(png)) check(image.GetPixel(0, 0).R == 255 && image.GetPixel(0, 1).B == 255 && image.GetPixel(0, 0).A == 255,
                "Visitor frame: EVR bottom-up BGRX pixels become upright opaque PNG pixels");
            var monitors = new Dictionary<int, string>();
            var multi = new App.VisitorWallpaperHandoff(Path.Combine(root, "multi-cache"), path => { monitors[0] = path; monitors[1] = path; });
            check(multi.PublishImage(blue) && monitors.Count == 2 && monitors.Values.All(path => Pixel(path).B == 255),
                "Visitor static handoff: one safe image can be published to every monitor without a video session");
            using (App.WidgetManager widgets = new(() => { }, registryPath: registry))
            using (App.MainForm form = new(widgets, () => 0, () => green))
            {
                form.VisitorHandoffForTests = new(Path.Combine(root, "multi-video"), path => { native = path; monitors[0] = monitors[1] = path; },
                    (source, destination, _) => { if (source != video) throw new Exception("Unapproved source"); Picture(destination, Color.Blue); return Task.FromResult(true); });
                Set(form, "videoMonitorCount", (Func<int>)(() => 2));
                var service = Get<App.VisitorModeService>(form, "visitorMode");
                await service.ConfigureAsync(s => s with { Source = App.VisitorSourceKind.Video, Path = video }); await service.SetActiveAsync(true);
                Set(form, "visitorClosing", true); await Invoke(form, "StopVisitorAndCloseAsync");
                check(monitors.Values.All(path => Pixel(path).B == 255) && Get<App.VideoWallpaperController?>(form, "videoWallpaper") == null,
                    "Visitor multi-monitor exit: configured video can supply a safe extracted still without starting an unsupported video renderer");
            }
            await ForcedTermination(root, registry, check);
        }
        finally
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(registry, false);
            string full = Path.GetFullPath(root);
            if (Path.GetDirectoryName(full) == Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))
                && Path.GetFileName(full).StartsWith("WallpaperControl-Handoff-", StringComparison.Ordinal)) Directory.Delete(full, true);
        }
    }
    private static async Task ForcedTermination(string root, string registry, Action<bool, string> check)
    {
        string childRoot = Path.Combine(root, "forced"); Directory.CreateDirectory(childRoot);
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--visitor-handoff-kill-child"); start.ArgumentList.Add(childRoot); start.ArgumentList.Add(registry);
        using var child = Process.Start(start)!;
        try
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(8);
            while (!File.Exists(Path.Combine(childRoot, "ready")) && !child.HasExited && DateTime.UtcNow < deadline) await Task.Delay(20);
            check(!child.HasExited && File.Exists(Path.Combine(childRoot, "ready")), "Visitor forced exit: child committed safe wallpaper before termination");
            child.Kill(); await child.WaitForExitAsync();
            string native = File.ReadAllText(Path.Combine(childRoot, "native-profile"));
            var settings = new App.VisitorModeStore(registry).Load();
            check(File.Exists(native) && Pixel(native).B == 255 && settings.Active && settings.Resume?.ImagePath == Path.Combine(childRoot, "normal-original.png"),
                "Visitor forced exit: killing the process skips shutdown but retains static pixels, active flag and normal resume snapshot");
            using App.WidgetManager widgets = new(() => { }, registryPath: registry);
            using App.MainForm restarted = new(widgets, () => 0, () => settings.Resume!.ImagePath, settings);
            string? visible = null;
            restarted.VisitorHandoffForTests = new(Path.Combine(childRoot, "cache"), path => visible = path);
            restarted.GetType().GetMethod("ShowVisitorCover", Fields)!.Invoke(restarted, null);
            var mode = Get<App.VisitorModeService>(restarted, "visitorMode");
            check(mode.Active && visible != null && Pixel(visible).B == 255
                && Get<App.WallpaperModeOwnership>(restarted, "wallpaperOwnership").Mode == App.WallpaperOperatingMode.Visitor,
                "Visitor restart: saved mode reserves ownership and presents the cached safe image before media playback");
            Set(restarted, "loading", true); Get<TextBox>(restarted, "folderTextBox").Text = childRoot;
            Get<ComboBox>(restarted, "intervalComboBox").SelectedIndex = 0; Set(restarted, "loading", false);
            string? restored = null;
            restarted.StartImageSessionForTests = (path, _) => { restored = path; return true; };
            await mode.SetActiveAsync(false);
            check(!mode.Active && restored == settings.Resume!.ImagePath && Get<bool>(restarted, "customSlideshowEngineActive")
                && Math.Abs((Get<DateTime>(restarted, "customSlideshowNextChange") - DateTime.Now).TotalSeconds - 23) < 1,
                "Visitor restart: later deactivation restores original image source and remaining timer from the persisted snapshot");
        }
        finally { if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); } }
    }
    internal static int KillChild(string root, string registry)
    {
        string source = Path.Combine(root, "safe.png"), original = Path.Combine(root, "normal-original.png"); Picture(source, Color.Blue); Picture(original, Color.Lime);
        new App.VisitorModeStore(registry).Save(new() { Active = true, Path = source,
            Resume = new() { ImagePath = original, Custom = true, Interval = 60000, RemainingTicks = TimeSpan.FromSeconds(23).Ticks } });
        var handoff = new App.VisitorWallpaperHandoff(Path.Combine(root, "cache"), path => File.WriteAllText(Path.Combine(root, "native-profile"), path));
        if (!handoff.PublishImage(source)) return 2;
        File.WriteAllText(Path.Combine(root, "ready"), "ready");
        Thread.Sleep(Timeout.Infinite); return 3;
    }
    private sealed class FakeDesktop(List<string> events) : App.IVideoDesktop
    {
        internal FakeSession? Session;
        internal TaskCompletionSource? ValidationGate;
        public int MonitorCount => 1;
        public bool FileExists(string path) => true;
        public Task ValidateAsync(string path, CancellationToken cancellation) => ValidationGate?.Task.WaitAsync(cancellation) ?? Task.CompletedTask;
        public App.VideoShellGeneration? ResolveShell() => new(1, 2, 3, 4, 5);
        public App.IVideoSession Create(string path, App.VideoShellGeneration shell) => Session = new(events);
    }
    private sealed class FakeSession(List<string> events) : App.IVideoSession
    {
        internal Color Color = Color.Blue;
        internal int Saves;
        public long Position => 123;
        public Task InitializeAsync(CancellationToken cancellation) => Task.CompletedTask;
        public bool IsAttached(App.VideoShellGeneration shell) => true;
        public void Seek(long position) { }
        public void Play() { }
        public void Pause() { }
        public Task<bool> SaveCurrentFrameAsync(string destination, CancellationToken cancellation)
        { cancellation.ThrowIfCancellationRequested(); Saves++; Picture(destination, Color); return Task.FromResult(true); }
        public void Dispose() => events.Add("dispose");
        public event Action<Exception>? Failed { add { } remove { } }
    }
}
