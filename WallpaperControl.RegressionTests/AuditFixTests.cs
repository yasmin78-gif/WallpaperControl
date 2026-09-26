extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using Microsoft.Win32;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

internal static class AuditFixTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static void Set(object target, string name, object? value) => target.GetType().GetField(name, Members)!.SetValue(target, value);
    private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Members)!.GetValue(target)!;
    private static void Call(object target, string name) => target.GetType().GetMethod(name, Members)!.Invoke(target, null);

    internal static void Run(Action<bool, string> check)
    {
        RegistryValues(check);
        SchedulerAndSelection(check);
        Renderer(check);
        Updates(check);
        CalendarTitles(check);
        ActionableAnalyzerTests.Run(check);
    }

    private static void RegistryValues(Action<bool, string> check)
    {
        string path = @"Software\WallpaperControl.AuditTests-" + Guid.NewGuid().ToString("N");
        try
        {
            App.WidgetSettings original = new()
            {
                WeatherEnabled = true, CalendarEnabled = true,
                CalendarSources = new() { new() { Url = "https://example.invalid/test.ics" } }
            };
            original.Save(path);
            using RegistryKey key = Registry.CurrentUser.OpenSubKey(path, true)!;
            foreach (object invalid in new object[] { "invalid", long.MaxValue, new byte[] { 1, 2 }, new[] { "1", "2" } })
            {
                key.SetValue("ClockWidgetSize", invalid);
                key.SetValue("ClockWidgetEnabled", invalid);
                App.WidgetSettings loaded = App.WidgetSettings.Load(path);
                check(loaded.ClockSize == 150 && !loaded.ClockEnabled && loaded.WeatherEnabled &&
                    loaded.CalendarEnabled && loaded.CalendarSources.SequenceEqual(original.CalendarSources),
                    "M1 malformed " + invalid.GetType().Name + " is isolated from later widget preferences");
                loaded.Clone().Save(path);
                check(App.WidgetSettings.Load(path).CalendarSources.SequenceEqual(original.CalendarSources),
                    "M1 ordinary save preserves encrypted sources after malformed " + invalid.GetType().Name);
            }
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, false); }
    }

    private static void SchedulerAndSelection(Action<bool, string> check)
    {
        // Exercise real timer/UI methods without constructing COM or loading user preferences.
        var form = (App.MainForm)RuntimeHelpers.GetUninitializedObject(typeof(App.MainForm));
        using ManualResetEventSlim fired = new();
        using System.Threading.Timer timer = new(_ => fired.Set(), null, Timeout.Infinite, Timeout.Infinite);
        Set(form, "customSlideshowPreciseTimer", timer);
        Set(form, "fullscreenPolicy", new App.FullscreenPausePolicy());
        Set(form, "customSlideshowEngineActive", true);
        Set(form, "customSlideshowChangeRunning", true);
        Set(form, "customSlideshowNextChange", DateTime.Now.AddHours(-2));
        Call(form, "ArmCustomSlideshowPreciseTimer");
        check(!fired.Wait(60), "M3 overdue timer stays disabled while a transition is running");
        Set(form, "customSlideshowChangeRunning", false);
        Call(form, "ArmCustomSlideshowPreciseTimer");
        check(fired.Wait(1000), "M3 timer can resume after the in-flight transition finishes");
        timer.Change(Timeout.Infinite, Timeout.Infinite);

        using ComboBox interval = new();
        Type option = typeof(App.MainForm).GetNestedType("DisplayOption`1", BindingFlags.NonPublic)!.MakeGenericType(typeof(uint));
        interval.Items.Add(Activator.CreateInstance(option, 60000u, "minute")!);
        interval.SelectedIndex = 0;
        using TextBox folder = new();
        Set(form, "intervalComboBox", interval);
        Set(form, "folderTextBox", folder);
        Set(form, "fullscreenUpdateRunning", true);
        Set(form, "customSlideshowLastInterval", 60000u);
        Call(form, "ProcessPreciseCustomSlideshowTick");
        DateTime next = Get<DateTime>(form, "customSlideshowNextChange");
        check(next > DateTime.Now && next.Second == 0 && next.Millisecond == 0,
            "M3 two hours of missed deadlines skip directly to a future aligned minute");

        using CancellationTokenSource cancellation = new();
        Set(form, "customWallpaperCancellation", cancellation);
        Set(form, "customSlideshowNextChange", DateTime.Now.AddHours(-2));
        fired.Reset();
        int applied = 0;
        form.ApplyExplicitWallpaper(() => applied++, true);
        check(applied == 1 && cancellation.IsCancellationRequested && Get<bool>(form, "slideshowPaused") && !fired.Wait(60),
            "M2 explicit selection cancels the old change, pauses custom scheduling and disables its timer");
    }

    private sealed class Host : App.IPersistentWallpaperHost
    {
        public bool IsDisposed { get; private set; }
        public string? CurrentWallpaperPath { get; private set; }
        public Task Transition = Task.CompletedTask;
        public bool Initialize(string path) { CurrentWallpaperPath = path; return true; }
        public bool EnsureDesktopPlacement() => !IsDisposed;
        public void SetActivitySuspended(bool value) { }
        public void SetWallpaperPosition(App.DesktopWallpaperPosition value) { }
        public Task TransitionToAsync(string path, App.WallpaperTransitionKind kind, int duration,
            App.WallpaperTransitionDirection direction, App.WallpaperZoomMode zoom, CancellationToken token) => Transition;
        public void CommitCurrentPath(string path) => CurrentWallpaperPath = path;
        public void Dispose() => IsDisposed = true;
    }

    private static void Renderer(Action<bool, string> check)
    {
        string file = Path.GetTempFileName();
        try
        {
            App.PersistentDesktopTransitionManager.Shutdown();
            App.PersistentDesktopTransitionManager.SetWallpaperPosition(App.DesktopWallpaperPosition.Fill);
            Host host = new();
            App.PersistentDesktopTransitionManager.TryStartSession(file, () => { }, () => host);
            host.CommitCurrentPath("animated-image");
            check(!App.PersistentDesktopTransitionManager.HasExternalWallpaperChange(file) &&
                App.PersistentDesktopTransitionManager.HasExternalWallpaperChange("external-image"),
                "M2 native baseline distinguishes custom animation from external wallpaper changes");
            App.PersistentDesktopTransitionManager.TryStartSession("new-native-baseline", () => { });
            check(!App.PersistentDesktopTransitionManager.HasExternalWallpaperChange("new-native-baseline"),
                "M2 restarting ownership updates the native baseline even when reusing a renderer");
            try { App.PersistentDesktopTransitionManager.ApplyNativeSelection(() => throw new IOException("test")); }
            catch (IOException) { }
            check(!host.IsDisposed && App.PersistentDesktopTransitionManager.GetDisplayedWallpaperPath() == "animated-image",
                "M2 failed native selection retains the displayed renderer");
            host.Dispose();
            check(App.PersistentDesktopTransitionManager.GetDisplayedWallpaperPath() == null,
                "M2 disposed hosts cannot report the current wallpaper");
            App.PersistentDesktopTransitionManager.Shutdown();

            host = new();
            App.PersistentDesktopTransitionManager.TryStartSession(file, () => { }, () => host);
            // Avoid native composition wait; replacement happens while a fake animation is pending.
            typeof(App.PersistentDesktopTransitionManager).GetField("compositionReady", BindingFlags.Static | BindingFlags.NonPublic)!
                .SetValue(null, Task.CompletedTask);
            TaskCompletionSource completion = new();
            host.Transition = completion.Task;
            int fallbacks = 0;
            Task change = App.PersistentDesktopTransitionManager.ApplyCoreAsync(file, file,
                App.WallpaperTransitionKind.DesktopFade, 100, App.WallpaperTransitionDirection.Left, App.WallpaperZoomMode.In,
                (_, _) => { fallbacks++; return Task.CompletedTask; }, CancellationToken.None);
            App.PersistentDesktopTransitionManager.ApplyNativeSelection(() => { });
            completion.SetResult();
            PumpUntil(() => change.IsCompleted);
            check(change.IsCanceled && host.IsDisposed && fallbacks == 0 &&
                App.PersistentDesktopTransitionManager.GetDisplayedWallpaperPath() == null,
                "M2 superseded animation neither commits nor restores its image through native fallback");

            host = new();
            App.PersistentDesktopTransitionManager.TryStartSession(file, () => { }, () => host);
            TaskCompletionSource settling = new();
            typeof(App.PersistentDesktopTransitionManager).GetField("compositionReady", BindingFlags.Static | BindingFlags.NonPublic)!
                .SetValue(null, settling.Task);
            change = App.PersistentDesktopTransitionManager.ApplyCoreAsync(file, file,
                App.WallpaperTransitionKind.DesktopFade, 100, App.WallpaperTransitionDirection.Left, App.WallpaperZoomMode.In,
                (_, _) => { fallbacks++; return Task.CompletedTask; }, CancellationToken.None);
            App.PersistentDesktopTransitionManager.ApplyNativeSelection(() => { });
            settling.SetResult();
            PumpUntil(() => change.IsCompleted);
            check(change.IsCanceled && fallbacks == 0,
                "M2 selection during renderer initialization prevents a stale native fallback");

            check(App.PersistentDesktopTransitionManager.SupportsConfiguration(1, App.DesktopWallpaperPosition.Fill) &&
                !App.PersistentDesktopTransitionManager.SupportsConfiguration(2, App.DesktopWallpaperPosition.Fill) &&
                App.PersistentDesktopTransitionManager.SupportsConfiguration(1, App.DesktopWallpaperPosition.Span) &&
                !App.PersistentDesktopTransitionManager.SupportsConfiguration(2, App.DesktopWallpaperPosition.Span),
                "M4 single-monitor Span supports animations; multi-monitor layouts retain native rendering");
            App.PersistentDesktopTransitionManager.SetWallpaperPosition(App.DesktopWallpaperPosition.Span);
            bool ownership = false;
            host = new();
            check(App.PersistentDesktopTransitionManager.TryStartSession(file, () => ownership = true, () => host) && ownership,
                "M4 Span can acquire animated slideshow ownership");
            typeof(App.PersistentDesktopTransitionManager).GetField("compositionReady", BindingFlags.Static | BindingFlags.NonPublic)!
                .SetValue(null, Task.CompletedTask);
            App.PersistentDesktopTransitionManager.ApplyCoreAsync(file, file, App.WallpaperTransitionKind.DesktopFade,
                100, App.WallpaperTransitionDirection.Left, App.WallpaperZoomMode.In,
                (_, _) => { fallbacks++; return Task.CompletedTask; }, CancellationToken.None).GetAwaiter().GetResult();
            check(fallbacks == 0 && App.PersistentDesktopTransitionManager.GetDisplayedWallpaperPath() == file,
                "M4 Span runs the animated host without a native fallback");
        }
        finally
        {
            App.PersistentDesktopTransitionManager.Shutdown();
            App.PersistentDesktopTransitionManager.SetWallpaperPosition(App.DesktopWallpaperPosition.Fill);
            File.Delete(file);
        }
    }

    private static void PumpUntil(Func<bool> done)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && watch.Elapsed < TimeSpan.FromSeconds(3)) { Application.DoEvents(); Thread.Sleep(5); }
        if (!done()) throw new TimeoutException("Audit UI continuation timed out.");
    }

    private static void Updates(Action<bool, string> check)
    {
        check(App.UpdateService.ParseVersion("v2.0")!.ToString(3) == "2.0.0" &&
            App.UpdateService.ParseVersion("v2.0") == App.UpdateService.ParseVersion("v2.0.0"),
            "M6 two-part release tags normalize for display and comparison");
        check(App.UpdateService.ParseVersion("v2.1.3+build")!.ToString(3) == "2.1.3" &&
            App.UpdateService.ParseVersion("invalid") == null,
            "M6 existing release tags and invalid input retain their behavior");
        foreach (bool dispose in new[] { false, true })
        {
            using App.SettingsForm form = new(false, "system", 0, 0, 0, 0, 0, 0, 0, 0,
                "", true, false, true, true, true, 92);
            TaskCompletionSource<App.UpdateCheckResult> pending = new();
            CancellationToken token = default;
            Task task = form.CheckForUpdatesAsync(value => { token = value; return pending.Task; });
            if (dispose) form.Dispose();
            else typeof(App.SettingsForm).GetMethod("OnFormClosed", Members)!.Invoke(form,
                new object[] { new FormClosedEventArgs(CloseReason.UserClosing) });
            check(token.IsCancellationRequested, "M5 " + (dispose ? "disposal" : "closing") + " cancels the manual update request");
            pending.SetResult(new(App.UpdateCheckStatus.UpdateAvailable, new Version(1, 8, 5), new Version(2, 0, 0), null));
            PumpUntil(() => task.IsCompleted);
            task.GetAwaiter().GetResult();
            check(task.IsCompletedSuccessfully, "M5 late update result after window closure completes without showing a dialog");
        }
    }

    private static void CalendarTitles(Action<bool, string> check)
    {
        string day = DateTime.Today.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        var entries = WallpaperControl.IcsCalendarProvider.ParseSource(
            $"BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:no-title\r\nDTSTART:{day}T090000\r\nDTEND:{day}T100000\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n",
            "test", DateTime.Today, CancellationToken.None);
        check(entries.Single().Title == string.Empty, "L1 missing ICS title stays language-neutral in cached data");
        foreach (var (language, title) in new[] { ("de", "(ohne Titel)"), ("en", "(untitled)"),
            ("fr", "(sans titre)"), ("es", "(sin título)"), ("ja", "（タイトルなし）") })
            check(App.CalendarWidgetForm.GetEventTitle(entries.Single().Title, language) == title &&
                App.CalendarWidgetForm.GetEventTitle("Appointment", language) == "Appointment",
                "L1 title fallback is localized at display time in " + language);
    }
}
