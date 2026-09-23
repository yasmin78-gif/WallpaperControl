extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using System.Reflection;
using System.Diagnostics;

internal static class SchedulerDiagnosticsTests
{
    private sealed class DeferredTransition(Func<string, Task> apply) : App.IWallpaperTransition
    {
        public Task ApplyAsync(string? current, string next, int duration, App.WallpaperTransitionDirection direction,
            App.WallpaperZoomMode zoom, CancellationToken cancellationToken = default) => apply(next);
    }
    private sealed class Transition(Action<string> apply) : App.IWallpaperTransition
    {
        public Task ApplyAsync(string? current, string next, int duration, App.WallpaperTransitionDirection direction,
            App.WallpaperZoomMode zoom, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); apply(next); return Task.CompletedTask;
        }
    }
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static T Get<T>(object value, string name) => (T)value.GetType().GetField(name, Members)!.GetValue(value)!;
    private static void Set(object value, string name, object data) => value.GetType().GetField(name, Members)!.SetValue(value, data);
    private static object? Call(object value, string name, params object?[] args) => value.GetType().GetMethod(name, Members)!.Invoke(value, args);

    internal static void Run(Action<bool, string> check)
    {
        using Task work = new(() => Exercise(check));
        Thread thread = new(() => work.RunSynchronously(TaskScheduler.Default));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join(); work.GetAwaiter().GetResult();
    }

    private static void Exercise(Action<bool, string> check)
    {
        string registry = @"Software\WallpaperControl.SchedulerTests-" + Guid.NewGuid().ToString("N");
        string fixture = Path.Combine(Path.GetTempPath(), "WallpaperControl.SchedulerTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        using App.WidgetManager manager = new(() => { }, registryPath: registry,
            notesStore: new App.NotesStore(Path.Combine(fixture, "notes.json")));
        string? displayed = null;
        int applied = 0;
        using App.MainForm main = new(manager, () => App.DesktopSlideshowState.Enabled | App.DesktopSlideshowState.Slideshow, () => displayed);
        try
        {
            _ = main.Handle;
            Set(main, "fullscreenUpdateRunning", true); // Deterministic samples, independent of the user's foreground window.
            Set(main, "customSlideshowEngineActive", true);
            Set(main, "slideshowPaused", false);
            Set(main, "statistics", new App.WallpaperStatistics(saveOverride: () => { }));
            var transitions = Get<IReadOnlyDictionary<App.WallpaperTransitionKind, App.IWallpaperTransition>>(
                Get<object>(main, "wallpaperTransitionService"), "transitions");
            ((Dictionary<App.WallpaperTransitionKind, App.IWallpaperTransition>)transitions)[App.WallpaperTransitionKind.Direct] =
                new Transition(path => { displayed = path; applied++; });
            Set(main, "selectedTransitionKind", App.WallpaperTransitionKind.Direct);
            Get<TextBox>(main, "folderTextBox").Text = Path.Combine(fixture, "missing");
            ComboBox interval = Get<ComboBox>(main, "intervalComboBox");
            Type option = typeof(App.MainForm).GetNestedType("DisplayOption`1", BindingFlags.NonPublic)!.MakeGenericType(typeof(uint));
            // No user preference writes from this isolated selection control.
            using ComboBox isolatedInterval = new();
            isolatedInterval.Items.Add(Activator.CreateInstance(option, 60000u, "minute")!);
            isolatedInterval.SelectedIndex = 0;
            Set(main, "intervalComboBox", isolatedInterval);
            var timer = Get<System.Threading.Timer>(main, "customSlideshowPreciseTimer");
            var policy = Get<App.FullscreenPausePolicy>(main, "fullscreenPolicy");
            DateTime Deadline() => Get<DateTime>(main, "customSlideshowNextChange");
            void Due() { Set(main, "customSlideshowNextChange", DateTime.Now.AddMilliseconds(-100)); Call(main, "ProcessPreciseCustomSlideshowTick"); }
            Call(main, "RecalculateCustomSlideshowSchedule");
            check(Deadline() > DateTime.Now && Deadline().Second == 0, "Scheduler: interval plans a future aligned deadline");
            DateTime future = Deadline();
            Call(main, "ProcessPreciseCustomSlideshowTick");
            check(Deadline() == future, "Scheduler: early tick does not advance");

            // Real timer -> BeginInvoke -> actual tick and advance guards. No desktop image is changed.
            Set(main, "customSlideshowNextChange", DateTime.Now.AddMilliseconds(80));
            DateTime due = Deadline();
            Call(main, "ArmCustomSlideshowPreciseTimer");
            Stopwatch wait = Stopwatch.StartNew();
            while (Deadline() == due && wait.ElapsedMilliseconds < 3000) { Application.DoEvents(); Thread.Sleep(5); }
            check(Deadline() > due && Deadline() > DateTime.Now, "Scheduler: real automatic callback reaches advance and rearms after unavailable-folder skip");
            check(Get<long>(main, "schedulerCallbackUtcTicks") > 0 && Get<long>(main, "schedulerDispatchUtcTicks") > 0,
                "Scheduler: background callback and UI dispatch observed separately");
            using (Bitmap image = new(2, 2)) image.Save(Path.Combine(fixture, "wallpaper.png"));
            Get<TextBox>(main, "folderTextBox").Text = fixture;
            Due();
            check(applied == 1 && displayed == Path.Combine(fixture, "wallpaper.png") && Deadline() > DateTime.Now,
                "Scheduler: due automatic change executes through image decoding/transition/completion and schedules next deadline");
            DateTime manualDeadline = Deadline();
            bool advanced = ((Task<bool>)Call(main, "AdvanceWallpaperAsync", App.DesktopSlideshowDirection.Forward)!).GetAwaiter().GetResult();
            check(advanced && applied == 2 && Deadline() == manualDeadline && !Get<bool>(main, "customSlideshowChangeRunning"),
                "Scheduler: complete manual Next path applies image and preserves future deadline");
            foreach (bool automaticFirst in new[] { true, false })
            {
                TaskCompletionSource finish = new(); int entered = 0;
                ((Dictionary<App.WallpaperTransitionKind, App.IWallpaperTransition>)transitions)[App.WallpaperTransitionKind.Direct] =
                    new DeferredTransition(async path => { entered++; await finish.Task; displayed = path; });
                Task<bool>? manual = null;
                if (automaticFirst) Due();
                else manual = (Task<bool>)Call(main, "AdvanceWallpaperAsync", App.DesktopSlideshowDirection.Forward)!;
                check(Get<bool>(main, "customSlideshowChangeRunning"), "Overlapping Next: first transition is pending; automaticFirst=" + automaticFirst);
                if (automaticFirst)
                    check(!((Task<bool>)Call(main, "AdvanceWallpaperAsync", App.DesktopSlideshowDirection.Forward)!).GetAwaiter().GetResult(),
                        "Overlapping Next: widget/manual request is skipped during automatic transition");
                else Due();
                check(entered == 1, "Overlapping Next: exactly one transition enters; automaticFirst=" + automaticFirst);
                finish.SetResult();
                wait.Restart();
                while (Get<bool>(main, "customSlideshowChangeRunning") && wait.ElapsedMilliseconds < 3000) { Application.DoEvents(); Thread.Sleep(5); }
                check(!Get<bool>(main, "customSlideshowChangeRunning") && Deadline() > DateTime.Now && (manual == null || manual.Result),
                    "Overlapping Next: completion releases guard and rearms scheduler; automaticFirst=" + automaticFirst);
            }
            Get<TextBox>(main, "folderTextBox").Text = Path.Combine(fixture, "missing");
            future = Deadline();
            Call(main, "CompleteCustomSlideshowSchedule", false);
            check(Deadline() == future, "Scheduler: manual completion retains a future deadline");
            Set(main, "customSlideshowNextChange", DateTime.Now.AddMinutes(-15));
            Call(main, "CompleteCustomSlideshowSchedule", false);
            check(Deadline() > DateTime.Now && Deadline().Second == 0, "Scheduler: manual completion repairs an overdue deadline");
            Set(main, "customSlideshowNextChange", DateTime.Now.AddMinutes(-15));
            Call(main, "ProcessPreciseCustomSlideshowTick");
            check(Deadline() > DateTime.Now, "Scheduler: overdue automatic callback skips missed boundaries and continues");

            Call(main, "PauseSlideshow");
            future = Deadline(); Due();
            check(Get<bool>(main, "slideshowPaused") && Deadline() < DateTime.Now, "Scheduler: manual pause blocks due change");
            ((Task<bool>)Call(main, "ResumeSlideshowAsync", false)!).GetAwaiter().GetResult();
            check(!Get<bool>(main, "slideshowPaused") && Deadline() > DateTime.Now, "Scheduler: manual resume plans future deadline");
            DateTime now = DateTime.UtcNow;
            policy.Update(true, true, now); Due();
            check(policy.IsPaused && Deadline() < DateTime.Now, "Scheduler: fullscreen blocks due change");
            Set(main, "pauseOnFullscreen", false);
            Set(main, "fullscreenUpdateRunning", false);
            ((Task)Call(main, "UpdateFullscreenPauseAsync")!).GetAwaiter().GetResult();
            Set(main, "fullscreenUpdateRunning", true);
            check(!policy.IsPaused && Deadline() > DateTime.Now, "Scheduler: fullscreen resume propagates to widgets and reschedules");
            Call(main, "PauseSlideshow");
            policy.Update(true, true, now); policy.Update(false, false, now);
            Call(main, "ArmCustomSlideshowPreciseTimer");
            check(Get<bool>(main, "slideshowPaused"), "Scheduler: fullscreen exit preserves manual pause");
            ((Task<bool>)Call(main, "ResumeSlideshowAsync", false)!).GetAwaiter().GetResult();

            Set(main, "customSlideshowNextChange", DateTime.Now.AddMinutes(-15));
            DateTime overdue = Deadline();
            Call(main, "ObserveScheduler");
            DateTime throttle = Get<DateTime>(main, "nextSchedulerAnomalyLogUtc");
            Call(main, "ObserveScheduler");
            check(throttle > DateTime.UtcNow && Get<DateTime>(main, "nextSchedulerAnomalyLogUtc") == throttle && Deadline() == overdue,
                "Scheduler: overdue diagnostics throttled and do not change scheduling");
            Set(main, "customSlideshowNextChange", DateTime.MaxValue);
            Set(main, "nextSchedulerAnomalyLogUtc", default(DateTime));
            Call(main, "ObserveScheduler");
            check(Deadline() == DateTime.MaxValue && Get<DateTime>(main, "nextSchedulerAnomalyLogUtc") > DateTime.UtcNow,
                "Scheduler: invalid deadline diagnosed without speculative recovery");
            Call(main, "RecalculateCustomSlideshowSchedule");

            isolatedInterval.SelectedIndex = -1;
            Call(main, "ProcessPreciseCustomSlideshowTick");
            check(Get<bool>(main, "schedulerIntervalRetryLogged"), "Scheduler: invalid interval retry is diagnosed");
            isolatedInterval.SelectedIndex = 0;
            Call(main, "ProcessPreciseCustomSlideshowTick");
            check(!Get<bool>(main, "schedulerIntervalRetryLogged") && Deadline() > DateTime.Now,
                "Scheduler: interval selection recovers with future deadline");

            // A blocked profile exercises initialization/retry without launching a browser or using personal data.
            string blockedProfile = Path.Combine(fixture, "file-not-directory");
            File.WriteAllText(blockedProfile, "test");
            using App.WebWidgetForm web = new(new App.WebWidgetSettings { Url = "https://example.invalid", ReloadOnStartup = false }, "en", blockedProfile);
            long logStart = new FileInfo(App.AppLogger.LogFilePath).Length;
            string secret = "https://private.invalid/?password=do-not-log-this";
            Call(web, "ReportFailure", "WebNavigationFailed", new InvalidOperationException(secret));
            using (FileStream log = new(App.AppLogger.LogFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                log.Seek(logStart, SeekOrigin.Begin);
                using StreamReader reader = new(log);
                string entry = reader.ReadToEnd();
                check(entry.Contains("exceptionType=InvalidOperationException") && entry.Contains("hresult=0x") && !entry.Contains(secret),
                    "Web: exception diagnostics retain type/code without navigation or credential content");
            }
            Call(main, "ObserveScheduler");
            long quietStart = new FileInfo(App.AppLogger.LogFilePath).Length;
            for (int i = 0; i < 20; i++) Call(main, "ObserveScheduler");
            check(new FileInfo(App.AppLogger.LogFilePath).Length == quietStart, "Scheduler: unchanged healthy polls do not produce log entries");
            future = Deadline();
            void Isolated(string name) => check(Deadline() == future && ReferenceEquals(timer, Get<object>(main, "customSlideshowPreciseTimer"))
                && Get<bool>(main, "customSlideshowEngineActive") && !Get<bool>(main, "slideshowPaused") && !policy.IsPaused
                && !Get<bool>(main, "customSlideshowChangeRunning"), "Scheduler/Web isolation: " + name);
            for (int i = 0; i < 3; i++) { web.HandleProcessFailure(CoreWebView2ProcessFailedKind.GpuProcessExited); Isolated("GPU failure " + i); }
            check(!Get<bool>(web, "failedProcess"), "Web: GPU failure does not mark browser process dead");
            Call(web, "Reload_Click", null, EventArgs.Empty); Isolated("initialization retry failure");
            check(!Get<bool>(web, "initializing") && !Get<bool>(web, "ready"), "Web: failed recovery leaves initialization flag clear");
            web.SetActivitySuspended(true); Call(web, "Reload_Click", null, EventArgs.Empty); web.SetActivitySuspended(false);
            Isolated("suspend/resume and suppressed reload");
            web.HandleProcessFailure(CoreWebView2ProcessFailedKind.BrowserProcessExited); Isolated("browser failure");
            for (int i = 0; i < 3; i++) { Call(web, "Reload_Click", null, EventArgs.Empty); Isolated("repeated recovery " + i); }

            // Confirm independence of the live timer, not just its object identity/deadline fields.
            Set(main, "customSlideshowNextChange", DateTime.Now.AddMilliseconds(80)); due = Deadline();
            Call(main, "ArmCustomSlideshowPreciseTimer");
            web.HandleProcessFailure(CoreWebView2ProcessFailedKind.GpuProcessExited);
            wait.Restart();
            while (Deadline() == due && wait.ElapsedMilliseconds < 3000) { Application.DoEvents(); Thread.Sleep(5); }
            check(Deadline() > due, "Scheduler/Web isolation: armed timer still fires after GPU failure");
            Set(main, "intervalComboBox", interval);
        }
        finally
        {
            Set(main, "customSlideshowEngineActive", false);
            Get<System.Threading.Timer>(main, "customSlideshowPreciseTimer").Change(Timeout.Infinite, Timeout.Infinite);
            Registry.CurrentUser.DeleteSubKeyTree(registry, false);
            Directory.Delete(fixture, true);
        }
    }
}
