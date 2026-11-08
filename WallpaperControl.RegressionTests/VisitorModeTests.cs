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
            string fixtures = Path.Combine(Path.GetTempPath(), "WallpaperControl-Visitor-" + Guid.NewGuid());
            try
            {
                Directory.CreateDirectory(fixtures);
                using App.WidgetManager widgets = new(() => { }, registryPath: key);
                using App.MainForm form = new(widgets, () => 0, () => null);
                var service = Field<App.VisitorModeService>(form, "visitorMode");
                var toggle = Field<CheckBox>(form, "visitorSwitch");
                var content = Field<Panel>(form, "wallpaperContent");
                var source = Field<ComboBox>(form, "visitorSource");
                check(source.Items.Count == 3 && !toggle.Checked, "Visitor UI: three source types and compatible off default");
                toggle.Checked = true;
                check(service.Active && toggle.Checked && !Field<Panel>(form, "slideshowCard").Enabled && service.Error != null, "Visitor UI: main switch activates safe mode for missing source");
                service.SetActiveAsync(false).GetAwaiter().GetResult();
                check(!toggle.Checked && content.Enabled, "Visitor UI: service synchronizes main switch and restores controls");
                using (App.VisitorModeWidgetForm widget = new(service, _ => { }))
                {
                    widget.ToggleAsync().GetAwaiter().GetResult();
                    check(service.Active && toggle.Checked, "Visitor widget: toggle synchronizes shared service and main switch");
                    toggle.Checked = false;
                    check(!service.Active && widget.AccessibleDescription == App.Localization.Get("VisitorInactive"), "Visitor widget: main switch synchronizes widget in reverse");
                }
                var hotkey = Message.Create(IntPtr.Zero, 0x0312, (IntPtr)5, IntPtr.Zero);
                form.GetType().GetMethod("WndProc", Members)!.Invoke(form, new object[] { hotkey });
                check(service.Active && toggle.Checked, "Visitor hotkey: native message activates shared service without foreground window");
                service.SetActiveAsync(false).GetAwaiter().GetResult();
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
                string imageA = Path.Combine(fixtures, "a.png"), imageB = Path.Combine(fixtures, "b.png"), video = Path.Combine(fixtures, "c.mp4");
                using (System.Drawing.Bitmap bitmap = new(8, 8)) { bitmap.Save(imageA); bitmap.Save(imageB); }
                File.WriteAllBytes(video, new byte[] { 1 }); // Decoder is the injected fake, never a native player.
                Set(form, "videoMonitorCount", (Func<int>)(() => 1));
                Field<TextBox>(form, "videoPathText").Text = "queued.mp4";
                desktop.ValidationGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var pendingMode = (Task)form.GetType().GetMethod("ApplyWallpaperModeAsync", Members)!.Invoke(form,
                    new object[] { App.WallpaperOperatingMode.VideoWallpaper })!;
                // Run continuations through a test UI message pump only when the fake validator yields.
                var enteringVisitor = service.SetActiveAsync(true);
                var joined = Task.WhenAll(pendingMode, enteringVisitor);
                DateTime timeout = DateTime.UtcNow.AddSeconds(5);
                while (!joined.IsCompleted && DateTime.UtcNow < timeout) { Application.DoEvents(); Thread.Sleep(1); }
                if (!joined.IsCompleted) throw new TimeoutException("Visitor mode did not retire pending video within five seconds.");
                joined.GetAwaiter().GetResult();
                desktop.ValidationGate.TrySetResult(); desktop.ValidationGate = null;
                check(service.Active && ownership.Mode == App.WallpaperOperatingMode.Visitor && !controller.HasSession,
                    "Visitor race: activation cancels and joins pending normal video validation");
                long generation = ownership.Generation;
                form.ExecuteRemoteCommand("next");
                form.GetType().GetMethod("ProcessPreciseCustomSlideshowTick", Members)!.Invoke(form, null);
                ((Task)form.GetType().GetMethod("ApplyWallpaperModeAsync", Members)!.Invoke(form,
                    new object[] { App.WallpaperOperatingMode.ImageSlideshow })!).GetAwaiter().GetResult();
                check(ownership.Generation == generation && service.Active && !controller.HasSession,
                    "Visitor priority: remote next, scheduler and normal mode selector cannot override active mode");
                service.SetActiveAsync(false).GetAwaiter().GetResult();
                service.ConfigureAsync(s => s with { Source = App.VisitorSourceKind.Image, Path = imageA }).GetAwaiter().GetResult();
                service.SetActiveAsync(true).GetAwaiter().GetResult();
                check(service.Active && service.Error == null && Field<string>(form, "visitorCurrentPath") == imageA,
                    "Visitor media: valid single image decoded and selected through isolated renderer");
                service.ConfigureAsync(s => s with { Source = App.VisitorSourceKind.Video, Path = video }).GetAwaiter().GetResult();
                check(service.Error == null && controller.ActivePath == video && !controller.SoundEnabled,
                    "Visitor media: MP4 uses existing video controller and stays silent");
                service.ConfigureAsync(s => s with { Source = App.VisitorSourceKind.Folder, Path = fixtures, Shuffle = false }).GetAwaiter().GetResult();
                check(Field<string>(form, "visitorCurrentPath") == imageA, "Visitor folder: deterministic first image without altering normal source");
                ((Task)form.GetType().GetMethod("AdvanceVisitorFolderAsync", Members)!.Invoke(form, null)!).GetAwaiter().GetResult();
                check(Field<string>(form, "visitorCurrentPath") == imageB, "Visitor folder: existing candidate selection advances to next image");
                ((Task)form.GetType().GetMethod("AdvanceVisitorFolderAsync", Members)!.Invoke(form, null)!).GetAwaiter().GetResult();
                check(controller.ActivePath == video && service.Active, "Visitor folder: mixed playlist advances to video using existing controller");
                File.WriteAllText(imageA, "broken image");
                service.ConfigureAsync(s => s with { Source = App.VisitorSourceKind.Image, Path = imageA }).GetAwaiter().GetResult();
                check(service.Active && service.Error != null && !controller.HasSession, "Visitor media: corrupt image retains visitor ownership and retires video");
                service.ConfigureAsync(s => s with { Source = App.VisitorSourceKind.Folder, Path = Path.Combine(fixtures, "missing") }).GetAwaiter().GetResult();
                check(service.Active && service.Error != null && !controller.HasSession, "Visitor media: missing folder cannot fall back to normal playback");
                Set(form, "videoMonitorCount", (Func<int>)(() => 2));
                service.ConfigureAsync(s => s with { Source = App.VisitorSourceKind.Video, Path = video }).GetAwaiter().GetResult();
                check(service.Active && service.Error != null && !controller.HasSession, "Visitor media: unsupported multiple-monitor video keeps safe fallback");
                service.SetActiveAsync(false).GetAwaiter().GetResult();
                check(controller.ActivePath == "normal.mp4" && controller.PlaybackPosition == 87654321,
                    "Visitor media: source changes leave original normal video snapshot intact");

                ownership.Switch(App.WallpaperOperatingMode.ImageSlideshow);
                Set(form, "customSlideshowEngineActive", true); Set(form, "slideshowPaused", true);
                Set(form, "customSlideshowNextChange", DateTime.Now.AddSeconds(37));
                service.SetActiveAsync(true).GetAwaiter().GetResult();
                long remaining = service.Settings.Resume!.RemainingTicks;
                service.SetActiveAsync(false).GetAwaiter().GetResult();
                double restoredRemaining = (Field<DateTime>(form, "customSlideshowNextChange") - DateTime.Now).TotalSeconds;
                check(!service.Active && Field<bool>(form, "customSlideshowEngineActive") && Field<bool>(form, "slideshowPaused")
                    && Math.Abs(restoredRemaining - TimeSpan.FromTicks(remaining).TotalSeconds) < 1,
                    "Visitor resume: image engine, manual pause and remaining timer duration restored");
                form.GetType().GetMethod("SelectVisitorSection", Members)!.Invoke(form, null);
                check(Field<bool>(form, "showingVisitor") && !Field<bool>(form, "showingWidgets"), "Visitor navigation: dedicated category selected without changing widget preferences");
                var page = Field<Panel>(form, "visitorPage");
                form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents();
                form.PerformLayout(); page.PerformLayout();
                string output = Path.Combine(Path.GetTempPath(), "WallpaperControl-Visitor-Validation"); Directory.CreateDirectory(output);
                using (System.Drawing.Bitmap bitmap = new(form.Width, form.Height))
                { form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height)); bitmap.Save(Path.Combine(output, "visitor-ui.png")); }
                check(page.Bounds.Left >= Field<Panel>(form, "mainNavigation").Right, "Visitor navigation: sidebar reserves its own space");
                foreach (float factor in new[] { 1f, 1.5f, 4f / 3f })
                {
                    form.Scale(new System.Drawing.SizeF(factor, factor)); form.PerformLayout(); page.PerformLayout();
                    check(Field<TextBox>(form, "visitorPath").Right < page.ClientSize.Width && Field<NumericUpDown>(form, "visitorInterval").Minimum == 1,
                        "Visitor UI: scaled controls fit available width at factor " + factor);
                }
                using App.WidgetManager startupWidgets = new(() => { }, registryPath: key);
                var original = new App.VisitorResumeState { Video = true, VideoPath = "normal.mp4", VideoPosition = 123,
                    ImagePath = "original.png", Custom = true, ImagePaused = true, RemainingTicks = 123 };
                using App.MainForm startupForm = new(startupWidgets, () => 0, () => "must-not-be-started.png",
                    new App.VisitorModeSettings { Active = true, Resume = original });
                var startupService = Field<App.VisitorModeService>(startupForm, "visitorMode");
                check(startupService.Active && startupService.Settings.Resume == original
                    && Field<App.WallpaperModeOwnership>(startupForm, "wallpaperOwnership").Mode == App.WallpaperOperatingMode.Visitor
                    && !Field<bool>(startupForm, "customSlideshowEngineActive") && Field<CheckBox>(startupForm, "visitorSwitch").Checked,
                    "Visitor startup: constructor reserves visitor ownership and synchronizes UI before normal playback can start");
                var startupController = new App.VideoWallpaperController(new FakeDesktop(), _ => { });
                Set(startupForm, "videoWallpaper", startupController);
                Set(startupForm, "videoMonitorCount", (Func<int>)(() => 1));
                startupService.SetActiveAsync(false).GetAwaiter().GetResult();
                ((Task)startupForm.GetType().GetMethod("ApplyWallpaperModeAsync", Members)!.Invoke(startupForm,
                    new object[] { App.WallpaperOperatingMode.ImageSlideshow })!).GetAwaiter().GetResult();
                check(!startupService.Active && Field<bool>(startupForm, "customSlideshowEngineActive") && Field<bool>(startupForm, "slideshowPaused"),
                    "Visitor startup: restoring normal video also retains its later return to the original image mode");
            }
            catch (Exception ex) { error = ex; }
            finally
            {
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(key, false);
                string resolved = Path.GetFullPath(fixtures);
                if (Path.GetDirectoryName(resolved) == Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))
                    && Path.GetFileName(resolved).StartsWith("WallpaperControl-Visitor-", StringComparison.Ordinal) && Directory.Exists(resolved))
                    Directory.Delete(resolved, true);
            }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error != null) throw error;
    }

    private sealed class FakeDesktop : App.IVideoDesktop
    {
        internal FakeSession? Session;
        internal TaskCompletionSource? ValidationGate;
        public int MonitorCount => 1;
        public bool FileExists(string path) => true;
        public Task ValidateAsync(string path, CancellationToken cancellation) => ValidationGate?.Task.WaitAsync(cancellation) ?? Task.CompletedTask;
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
        bool covered = false;
        var captureFailure = new App.VisitorModeService(new(), _ => true, () => throw new IOException("Native snapshot unavailable"),
            () => { covered = true; return Task.CompletedTask; }, _ => Task.CompletedTask);
        await captureFailure.SetActiveAsync(true);
        check(captureFailure.Active && covered && captureFailure.Error != null, "Visitor: snapshot failure still activates safe desktop");
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
            using (var registry = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key, true)) registry!.SetValue("VisitorMode", "{broken");
            check(store.Load().Active, "Visitor: safety latch retains active mode when settings JSON is damaged");
        }
        finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(key, false); }
        var ownership = new App.WallpaperModeOwnership(); int lease = ownership.Generation;
        ownership.Switch(App.WallpaperOperatingMode.Visitor);
        check(!ownership.AllowsImages && !ownership.IsCurrentImage(lease), "Visitor: stale scheduler and image requests rejected");
        ownership.Switch(App.WallpaperOperatingMode.ImageSlideshow);
        check(!ownership.IsCurrentImage(lease), "Visitor: stale work rejected even after returning to images");
        var bindings = new[] { new App.HotkeyBinding(1, 3, 0x50, "Pause") };
        check(App.GlobalHotkeyManager.HasConflict(bindings, 3, 0x50) && !App.GlobalHotkeyManager.HasConflict(bindings, 0, 0x50)
            && !App.GlobalHotkeyManager.HasConflict(bindings, 3, 0x56), "Visitor hotkey: detects existing shortcuts and permits disabled or distinct bindings");
        var nativeModifiers = new List<uint>();
        var hotkeys = new App.GlobalHotkeyManager((_, _, modifiers, _) => { nativeModifiers.Add(modifiers); return true; }, (_, _) => true);
        hotkeys.Register(IntPtr.Zero, new[] { bindings[0], new App.HotkeyBinding(5, 3, 0x56, "Visitor") { NoRepeat = true } });
        check(nativeModifiers.SequenceEqual(new uint[] { 3, 0x4003 }), "Visitor hotkey: held key does not auto-toggle and existing shortcuts retain repeat policy");
    }
}
