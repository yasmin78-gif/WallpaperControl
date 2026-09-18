extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

internal static class StabilizationTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    internal static void Run(Action<bool, string> check)
    {
        CalendarLimits(check);
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                Renderer(check);
                Candidates(check);
                CalendarLifecycle(check);
                WeatherLifecycle(check);
                PrivateSources(check);
                UpdateAction(check);
                PositionFrames(check);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30))) throw new TimeoutException("Stabilization UI checks timed out.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, Members)!.GetValue(instance)!;
    private static void Set(object instance, string name, object? value) => instance.GetType().GetField(name, Members)!.SetValue(instance, value);
    private static void Shown(Form form)
    {
        _ = form.Handle;
        form.GetType().GetMethod("OnShown", Members)!.Invoke(form, new object[] { EventArgs.Empty });
    }
    private static void PumpUntil(Func<bool> done)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (!done() && watch.Elapsed < TimeSpan.FromSeconds(3)) { Application.DoEvents(); Thread.Sleep(5); }
        if (!done()) throw new TimeoutException("UI continuation did not finish.");
    }

    private static void Renderer(Action<bool, string> check)
    {
        string file = Path.GetTempFileName();
        try
        {
            App.PersistentDesktopTransitionManager.Shutdown();
            int ownership = 0;
            using FakeHost attachFailure = new() { Attach = false };
            bool started = App.PersistentDesktopTransitionManager.TryStartSession(file, () => ownership++, () => attachFailure);
            check(!started && ownership == 0 && attachFailure.IsDisposed &&
                App.PersistentDesktopTransitionManager.GetDisplayedWallpaperPath() == null,
                "H1 attachment failure disposes unpublished host and leaves native scheduling owned by Windows");
            using FakeHost imageFailure = new() { LoadFailure = true };
            started = App.PersistentDesktopTransitionManager.TryStartSession(file, () => ownership++, () => imageFailure);
            check(!started && ownership == 0 && imageFailure.IsDisposed,
                "H1 initial frame failure disposes resources without taking scheduling ownership");
            using FakeHost placementFailure = new() { Placement = false };
            check(!App.PersistentDesktopTransitionManager.TryStartSession(file, () => ownership++, () => placementFailure)
                && placementFailure.IsDisposed && ownership == 0, "H1 final placement failure cannot publish a renderer");

            foreach (App.DesktopWallpaperPosition position in Enum.GetValues<App.DesktopWallpaperPosition>())
            {
                App.PersistentDesktopTransitionManager.SetWallpaperPosition(position);
                using FakeHost good = new();
                started = App.PersistentDesktopTransitionManager.TryStartSession(file, () => ownership++, () => good);
                check(started && good.InitialPosition == position && good.CurrentWallpaperPath == file,
                    "H1/M7 retry initializes a ready renderer with loaded position " + position);
                App.DesktopWallpaperPosition changed = position == App.DesktopWallpaperPosition.Fit
                    ? App.DesktopWallpaperPosition.Center : App.DesktopWallpaperPosition.Fit;
                App.PersistentDesktopTransitionManager.SetWallpaperPosition(changed);
                check(good.Position == changed, "M7 accepted subsequent position reaches existing renderer from " + position);
                App.PersistentDesktopTransitionManager.Shutdown();
            }

            int factories = 0;
            using FakeHost concurrent = new();
            Task[] requests = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
                App.PersistentDesktopTransitionManager.TryStartSession(file, () => { }, () =>
                { Interlocked.Increment(ref factories); return concurrent; }))).ToArray();
            Task.WaitAll(requests);
            check(factories == 1 && concurrent.InitializeCalls == 1,
                "H1 concurrent initialization requests publish exactly one ready host");
            App.PersistentDesktopTransitionManager.Shutdown();
            using FakeHost shutdown = new() { DuringInitialize = App.PersistentDesktopTransitionManager.Shutdown };
            check(!App.PersistentDesktopTransitionManager.TryStartSession(file, () => ownership++, () => shutdown)
                && shutdown.IsDisposed, "H1 shutdown during initialization prevents late publication");
            using FakeHost failedOwnership = new();
            bool threw = false;
            try { App.PersistentDesktopTransitionManager.TryStartSession(file, () => throw new InvalidOperationException(), () => failedOwnership); }
            catch (InvalidOperationException) { threw = true; }
            check(threw && failedOwnership.IsDisposed && App.PersistentDesktopTransitionManager.GetDisplayedWallpaperPath() == null,
                "H1 failed ownership action removes its renderer");
            using FakeHost lostShell = new();
            App.PersistentDesktopTransitionManager.TryStartSession(file, () => { }, () => lostShell);
            int fallbacks = 0;
            Task change = App.PersistentDesktopTransitionManager.ApplyCoreAsync(file, file, App.WallpaperTransitionKind.DesktopFade,
                100, App.WallpaperTransitionDirection.Left, App.WallpaperZoomMode.In,
                (path, token) => { token.ThrowIfCancellationRequested(); if (path == file) fallbacks++; return Task.CompletedTask; }, CancellationToken.None);
            lostShell.Placement = false;
            PumpUntil(() => change.IsCompleted);
            change.GetAwaiter().GetResult();
            check(fallbacks == 1 && lostShell.IsDisposed && App.PersistentDesktopTransitionManager.GetDisplayedWallpaperPath() == null,
                "H1 shell placement lost during settling disposes overlay and uses the direct fallback exactly once");
        }
        finally { App.PersistentDesktopTransitionManager.Shutdown(); File.Delete(file); }
    }

    private sealed class FakeHost : App.IPersistentWallpaperHost
    {
        internal bool Attach = true, Placement = true, LoadFailure;
        internal int InitializeCalls;
        internal App.DesktopWallpaperPosition Position, InitialPosition;
        internal Action? DuringInitialize;
        public bool IsDisposed { get; private set; }
        public string? CurrentWallpaperPath { get; private set; }
        public bool Initialize(string path)
        {
            InitializeCalls++;
            InitialPosition = Position;
            DuringInitialize?.Invoke();
            if (LoadFailure) throw new ArgumentException("Synthetic decoder failure");
            if (!Attach) return false;
            CurrentWallpaperPath = path;
            return true;
        }
        public bool EnsureDesktopPlacement() => Placement && !IsDisposed;
        public void SetActivitySuspended(bool suspended) { }
        public void SetWallpaperPosition(App.DesktopWallpaperPosition position) => Position = position;
        public Task TransitionToAsync(string path, App.WallpaperTransitionKind kind, int milliseconds,
            App.WallpaperTransitionDirection direction, App.WallpaperZoomMode zoomMode, CancellationToken token) => Task.CompletedTask;
        public void CommitCurrentPath(string path) => CurrentWallpaperPath = path;
        public void Dispose() => IsDisposed = true;
    }

    private static void Candidates(Action<bool, string> check)
    {
        string folder = Path.Combine(Path.GetTempPath(), "WallpaperControl-candidates-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string[] files = { Path.Combine(folder, "A.png"), Path.Combine(folder, "B.jpg"), Path.Combine(folder, "C.png") };
        try
        {
            using (Bitmap image = new(4, 4)) { image.Save(files[0]); image.Save(files[2]); }
            File.WriteAllText(files[1], "not a JPEG");
            List<string> displayed = new();
            List<string> tried = new();
            Task<bool> Display(string path)
            {
                tried.Add(path);
                try { using Image image = Image.FromFile(path); displayed.Add(path); return Task.FromResult(true); }
                catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException or IOException or ExternalException) { return Task.FromResult(false); }
            }
            string? result = App.WallpaperCandidateRunner.TryAdvanceAsync(files, files[0], false, false,
                new Random(1), Display).GetAwaiter().GetResult();
            check(result == files[2] && tried.SequenceEqual(new[] { files[1], files[2] }) && displayed.SequenceEqual(new[] { files[2] }),
                "H3 valid A / corrupt B / valid C advances to C and records only successfully displayed C");
            int attempts = 0;
            result = App.WallpaperCandidateRunner.TryAdvanceAsync(files, files[0], false, false, new Random(1),
                _ => { attempts++; return Task.FromResult(false); }).GetAwaiter().GetResult();
            check(result == null && attempts == files.Length, "H3 all-invalid sequential pass is bounded to one attempt per file");
            using (Bitmap image = new(4, 4)) image.Save(files[1], System.Drawing.Imaging.ImageFormat.Jpeg);
            result = App.WallpaperCandidateRunner.TryAdvanceAsync(files, files[0], false, false, new Random(1), Display).GetAwaiter().GetResult();
            check(result == files[1], "H3 repaired file is retried on a future request");
            result = App.WallpaperCandidateRunner.TryAdvanceAsync(files, files[0], false, true, new Random(1), Display).GetAwaiter().GetResult();
            check(result == files[2], "H3 backward selection retains wraparound order");
            tried.Clear();
            result = App.WallpaperCandidateRunner.TryAdvanceAsync(files, files[0], true, false, new Random(1),
                path => { tried.Add(path); return Task.FromResult(false); }).GetAwaiter().GetResult();
            check(result == null && tried.Count == 2 && tried.Distinct().Count() == 2 && !tried.Contains(files[0]),
                "H3 shuffle excludes current wallpaper and retries without replacement");
        }
        finally { Directory.Delete(folder, true); }
    }

    private static void CalendarLifecycle(Action<bool, string> check)
    {
        var provider = new FakeCalendar();
        using App.CalendarWidgetForm form = new(false, App.SystemWidgetStyle.Minimal, 3, true, 15, "en", provider, Point.Empty, _ => { });
        var timer = Field<System.Windows.Forms.Timer>(form, "refreshTimer");
        check(!timer.Enabled, "M4 calendar does not schedule before initial show");
        Shown(form);
        check(timer.Enabled && provider.Refreshes == 1, "M4 initial show starts calendar refresh scheduling");
        check(form.ClientSize.Height <= Math.Min(1800, Screen.FromControl(form).WorkingArea.Height),
            "H4 widget bounds excessive same-day rows to its work area and render height limit");
        timer.Interval = 10;
        PumpUntil(() => provider.Refreshes >= 2);
        check(provider.Refreshes >= 2, "M4 calendar refreshes periodically after initial show");
        form.SetActivitySuspended(true);
        int refreshes = provider.Refreshes;
        form.RefreshCalendar();
        check(!timer.Enabled && provider.Refreshes == refreshes, "M4 suspension disables timer and explicit refresh");
        form.Apply(false, App.SystemWidgetStyle.Minimal, 3, false, 30, "en");
        check(!timer.Enabled, "M4 settings Apply cannot restart a suspended timer");
        form.SetActivitySuspended(false);
        check(timer.Enabled && provider.Refreshes == refreshes + 1, "M4 resume restarts timer and refreshes once");
        form.Apply(false, App.SystemWidgetStyle.Minimal, 3, true, 15, "en");
        check(ReferenceEquals(timer, Field<System.Windows.Forms.Timer>(form, "refreshTimer")) && provider.Refreshes == refreshes + 1,
            "M4 repeated Apply keeps a single timer without extra initial refresh");
        form.Dispose();
        form.SetActivitySuspended(false);
        form.RefreshCalendar();
        check(!timer.Enabled && provider.Refreshes == refreshes + 1, "M4 disposed calendar cannot reactivate scheduling");
        using App.CalendarWidgetForm suspended = new(false, App.SystemWidgetStyle.Minimal, 3, true, 15, "en", provider, Point.Empty, _ => { });
        suspended.SetActivitySuspended(true);
        Shown(suspended);
        check(!Field<System.Windows.Forms.Timer>(suspended, "refreshTimer").Enabled && provider.Refreshes == refreshes + 1,
            "M4 initially suspended calendar does not fetch or start timer when shown");
    }

    private sealed class FakeCalendar : App.ICalendarProvider
    {
        internal int Refreshes;
        public string ProviderName => "Fixture";
        public string StatusResourceKey => "CalendarStatusConnected";
        public DateTime? LastRefresh => null;
        public Task RefreshAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); Refreshes++; return Task.CompletedTask; }
        public IReadOnlyList<App.CalendarEvent> GetUpcoming(DateTime from, int maxEntries, string languageCode) =>
            Enumerable.Range(0, 1000).Select(i => new App.CalendarEvent(from.Date, from.Date.AddDays(1), true, "Synthetic", "Place", "Fixture")).ToArray();
    }

    private static void WeatherLifecycle(Action<bool, string> check)
    {
        using App.WeatherWidgetForm form = new(false, 15, App.SystemWidgetStyle.Minimal, "Synthetic", "en", false, Point.Empty, _ => { });
        object service = Field<object>(form, "weatherService");
        Field<HttpClient>(service, "httpClient").Dispose();
        using SlowWeather handler = new();
        using HttpClient client = new(handler);
        Set(service, "httpClient", client);
        Shown(form);
        check(handler.Started, "M10 initial weather refresh starts a pending request");
        form.Dispose();
        handler.Complete();
        PumpUntil(() => handler.Finished);
        // Drain the captured WinForms continuations, including OnShown's continuation.
        for (int i = 0; i < 20; i++) { Application.DoEvents(); Thread.Sleep(5); }
        var timer = Field<System.Windows.Forms.Timer>(form, "timer");
        form.SetActivitySuspended(false);
        form.Apply(false, 15, App.SystemWidgetStyle.Minimal, "Synthetic", "en", false);
        check(form.IsDisposed && !timer.Enabled && Field<object?>(form, "snapshot") == null,
            "M10 pending initial request completes after disposal without restarting timer or publishing state");

        using App.WeatherWidgetForm live = new(false, 15, App.SystemWidgetStyle.Minimal, "Synthetic", "en", false, Point.Empty, _ => { });
        object liveService = Field<object>(live, "weatherService");
        Field<HttpClient>(liveService, "httpClient").Dispose();
        using SlowWeather liveHandler = new();
        using HttpClient liveClient = new(liveHandler);
        Set(liveService, "httpClient", liveClient);
        Shown(live);
        var liveTimer = Field<System.Windows.Forms.Timer>(live, "timer");
        check(liveTimer.Enabled, "M10 normal initial weather refresh schedules timer");
        live.SetActivitySuspended(true);
        check(!liveTimer.Enabled, "M10 weather suspension stops timer during pending HTTP");
        live.SetActivitySuspended(false);
        check(liveTimer.Enabled, "M10 weather resume restarts scheduling");
        live.Dispose();
        liveHandler.Complete();
    }

    private sealed class SlowWeather : HttpMessageHandler
    {
        private readonly TaskCompletionSource<HttpResponseMessage> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Started, Finished;
        internal void Complete() => completion.TrySetCanceled();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Started = true;
            try { return await completion.Task.WaitAsync(token).ConfigureAwait(false); }
            finally { Finished = true; }
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, StringBuilder? lParam);

    private static string NativeText(TextBox box)
    {
        StringBuilder value = new(4096);
        _ = SendMessage(box.Handle, 0x000D, (IntPtr)value.Capacity, value);
        return value.ToString();
    }

    private static void PrivateSources(Action<bool, string> check)
    {
        using App.PrivateCalendarTextBox box = new() { Text = "https://test/secret-one\r\nhttps://test/secret-two" };
        string original = box.Text;
        check(NativeText(box) == "********" && SendMessage(box.Handle, 0x00D2, IntPtr.Zero, null) != IntPtr.Zero,
            "M6 hidden native edit contains only a genuinely masked placeholder, never private URLs");
        check(box.AccessibilityObject.Value == "********", "M6 hidden accessibility value does not expose source URLs");
        box.RevealSources();
        check(box.Multiline && !box.ReadOnly && NativeText(box) == original, "M6 explicit reveal restores editable multiple URLs");
        box.Text = "https://test/edited\r\nhttps://test/added";
        string edited = box.Text;
        box.HideSources();
        check(NativeText(box) == "********" && box.Text == edited && !box.CanUndo,
            "M6 hiding removes native secrets and undo text while preserving edited sources");
        box.RevealSources();
        box.Text = "https://test/remaining";
        box.HideSources();
        check(box.Text == "https://test/remaining" && !NativeText(box).Contains("remaining", StringComparison.Ordinal),
            "M6 removing a source survives hide and does not disclose the remaining URL");
        App.WidgetSettings settings = new() { CalendarIcsUrl = original, CalendarHolidayIcsUrl = original };
        using App.SettingsForm form = new(false, "system", 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, "", true, false, true, true, true, 92, settings, null);
        foreach (string field in new[] { "calendarIcsUrlTextBox", "calendarHolidayIcsUrlTextBox" })
        {
            App.PrivateCalendarTextBox editor = Field<App.PrivateCalendarTextBox>(form, field);
            check(editor.Text == original && NativeText(editor) == "********" && editor.AccessibilityObject.Value == "********",
                "M6 actual SettingsForm conceals " + field);
        }
    }

    private static void UpdateAction(Action<bool, string> check)
    {
        Uri uri = new("https://github.com/example/releases/tag/v1.8.4");
        foreach (bool enter in new[] { false, true })
        {
            using App.UpdateDialog dialog = new(App.UpdateDialogKind.UpdateAvailable, "1.8.3", "1.8.4", false, 100, "en");
            dialog.Opacity = 0;
            dialog.ShowInTaskbar = false;
            dialog.Show();
            ((Button)dialog.AcceptButton!).Select();
            if (enter) typeof(Form).GetMethod("ProcessDialogKey", Members)!.Invoke(dialog, new object[] { Keys.Enter });
            else dialog.AcceptButton!.PerformClick();
            int opened = 0;
            App.UpdateDialog.OpenReleaseIfAccepted(dialog.DialogResult, uri, actual => { if (actual == uri) opened++; });
            check(opened == 1, enter ? "M5 Enter launches the release once through the shared contract" : "M5 View Release launches the release once through the shared contract");
        }
        int cancelledOpens = 0;
        foreach (DialogResult result in new[] { DialogResult.Cancel, DialogResult.None, DialogResult.No })
            App.UpdateDialog.OpenReleaseIfAccepted(result, uri, _ => cancelledOpens++);
        App.UpdateDialog.OpenReleaseIfAccepted(DialogResult.OK, null, _ => cancelledOpens++);
        check(cancelledOpens == 0, "M5 cancel, close and absent URL never launch a release");
    }

    private static void PositionFrames(Action<bool, string> check)
    {
        string file = Path.Combine(Path.GetTempPath(), "WallpaperControl-position-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            using (Bitmap image = new(20, 10)) { using Graphics g = Graphics.FromImage(image); g.Clear(Color.Red); image.Save(file); }
            using App.PersistentDesktopWallpaperHost host = new();
            host.ClientSize = new Size(100, 100);
            host.CommitCurrentPath(file);
            Set(host, "currentFrame", new Bitmap(100, 100));
            host.SetActivitySuspended(true);
            Task transition = host.TransitionToAsync(file, App.WallpaperTransitionKind.DesktopFade, 100,
                App.WallpaperTransitionDirection.Left, App.WallpaperZoomMode.In, CancellationToken.None);
            Bitmap oldNext = Field<Bitmap>(host, "nextFrame");
            host.SetWallpaperPosition(App.DesktopWallpaperPosition.Fit);
            Bitmap current = Field<Bitmap>(host, "currentFrame"), next = Field<Bitmap>(host, "nextFrame");
            check(!ReferenceEquals(next, oldNext) && current.GetPixel(50, 5).ToArgb() == Color.Black.ToArgb()
                && next.GetPixel(50, 5).ToArgb() == Color.Black.ToArgb() && !transition.IsCompleted,
                "M7 position change during transition rerenders both frames as Fit without finishing animation");
            host.SetWallpaperPosition(App.DesktopWallpaperPosition.Stretch);
            check(Field<Bitmap>(host, "currentFrame").GetPixel(50, 5).R > 200 && Field<Bitmap>(host, "nextFrame").GetPixel(50, 5).R > 200,
                "M7 subsequent Stretch updates both active transition frames consistently");
            host.Dispose();
            try { transition.GetAwaiter().GetResult(); } catch (OperationCanceledException) { } catch (ObjectDisposedException) { }
        }
        finally { File.Delete(file); }
    }

    private static void CalendarLimits(Action<bool, string> check)
    {
        DateTime today = DateTime.Today;
        string day = today.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        string Event(string body, string id = "test") => $"BEGIN:VEVENT\r\nUID:{id}\r\nDTSTART:{day}T090000\r\nDTEND:{day}T100000\r\nSUMMARY:Fixture\r\n{body}END:VEVENT\r\n";
        string Feed(string events) => "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Tests//EN\r\n" + events + "END:VCALENDAR\r\n";
        bool Rejected(string text)
        {
            try { _ = WallpaperControl.IcsCalendarProvider.ParseSource(text, "Fixture", today, CancellationToken.None); return false; }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException) { return true; }
        }
        Stopwatch watch = Stopwatch.StartNew();
        check(Rejected(Feed(Event("RRULE:FREQ=SECONDLY\r\n"))) && watch.Elapsed < TimeSpan.FromSeconds(3),
            "H4 dense unbounded SECONDLY recurrence is rejected before expensive expansion");
        string hours = string.Join(",", Enumerable.Range(0, 24).Select(i => i.ToString(CultureInfo.InvariantCulture)));
        check(Rejected(Feed(Event("RRULE:FREQ=YEARLY;BYDAY=MO,TU,WE,TH,FR,SA,SU;BYHOUR=" + hours +
            ";BYMINUTE=0,4,8,12,16,20,24,28,32,36,40,44,48,52,56\r\n"))),
            "H4 recurrence budget includes implicit day expansion within yearly intervals");
        var normal = WallpaperControl.IcsCalendarProvider.ParseSource(Feed(Event("RRULE:FREQ=DAILY;COUNT=5\r\n")), "Fixture", today, CancellationToken.None);
        check(normal.Count == 5, "H4 normal daily recurrence remains unchanged");
        var unboundedDaily = WallpaperControl.IcsCalendarProvider.ParseSource(Feed(Event("RRULE:FREQ=DAILY\r\n")), "Fixture", today, CancellationToken.None);
        check(unboundedDaily.Count == 90, "H4 ordinary uncounted daily rule is restricted to the widget's 90-day horizon");
        check(Rejected(Feed(Event("RRULE:FREQ=SECONDLY\r\n")).Replace("END:VEVENT\r\nEND:VCALENDAR\r\n", "", StringComparison.Ordinal)),
            "H4 unclosed components cannot bypass recurrence preflight");
        watch.Restart();
        var impossible = WallpaperControl.IcsCalendarProvider.ParseSource(
            Feed(Event("RRULE:FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=30\r\n")), "Fixture", today, CancellationToken.None);
        check(impossible.Count <= 1 && watch.Elapsed < TimeSpan.FromSeconds(3),
            "H4 impossible recurrence ends within the clamped horizon");
        using (CancellationTokenSource cancelled = new())
        {
            cancelled.Cancel();
            bool observed = false;
            try { _ = WallpaperControl.IcsCalendarProvider.ParseSource(Feed(Event("")), "Fixture", today, cancelled.Token); }
            catch (OperationCanceledException) { observed = true; }
            check(observed, "H4 cancellation is checked before parsing and expansion");
        }
        check(Rejected(Feed(string.Concat(Enumerable.Range(0, WallpaperControl.CalendarFeedLimits.MaxEvents + 1).Select(i => Event("", i.ToString(CultureInfo.InvariantCulture)))))),
            "H4 excessive VEVENT count is rejected before Calendar.Load materializes events");
        string longEvent = $"BEGIN:VEVENT\r\nUID:long\r\nDTSTART;VALUE=DATE:19000101\r\nDTEND;VALUE=DATE:99991231\r\nSUMMARY:Long\r\nEND:VEVENT\r\n";
        using FeedResponse handler = new(Feed(longEvent));
        using HttpClient client = new(handler);
        using WallpaperControl.IcsCalendarProvider provider = new(client);
        provider.SetSource("https://calendar.test/private");
        provider.RefreshAsync().GetAwaiter().GetResult();
        var expanded = provider.GetUpcoming(today, 9, "en");
        check(expanded.Count == 9 && expanded[0].Start.Date == today && expanded[^1].Start.Date == today.AddDays(8),
            "H4 centuries-long all-day event expands only the upcoming displayed days");
        handler.Text = Feed(string.Concat(Enumerable.Range(0, 200).Select(i => Event("", i.ToString(CultureInfo.InvariantCulture)))));
        provider.RefreshAsync().GetAwaiter().GetResult();
        check(provider.GetUpcoming(today, 9, "en").Count == WallpaperControl.CalendarFeedLimits.MaxDisplayRows + 1,
            "H4 excessive same-day events are capped with one overflow indicator row");
        handler.Text = new string('x', WallpaperControl.CalendarFeedLimits.MaxResponseBytes + 1);
        provider.RefreshAsync().GetAwaiter().GetResult();
        check(provider.StatusResourceKey == "CalendarStatusStale" && provider.GetUpcoming(today, 9, "en").Count > 0,
            "H4 oversized response preserves previous good calendar cache without leaking private URL");
        using CountingStream unknownLength = new(WallpaperControl.CalendarFeedLimits.MaxResponseBytes + 10000);
        using StreamContent oversized = new(unknownLength);
        bool sizeLimited = false;
        try { WallpaperControl.CalendarFeedLimits.ReadResponseAsync(oversized, CancellationToken.None).GetAwaiter().GetResult(); }
        catch (InvalidDataException) { sizeLimited = true; }
        check(sizeLimited && unknownLength.ReadBytes <= WallpaperControl.CalendarFeedLimits.MaxResponseBytes + 8192,
            "H4 streaming size limit also applies without Content-Length and stops reading promptly");
        using CancellationTokenSource downloadCancellation = new();
        using CountingStream cancelledStream = new(100000) { AfterRead = downloadCancellation.Cancel };
        using StreamContent cancelledContent = new(cancelledStream);
        bool downloadCancelled = false;
        try { WallpaperControl.CalendarFeedLimits.ReadResponseAsync(cancelledContent, downloadCancellation.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { downloadCancelled = true; }
        check(downloadCancelled && cancelledStream.ReadBytes <= 8192,
            "H4 cancellation interrupts an in-progress streamed response");
        using SlowWeather pendingFeed = new();
        using HttpClient pendingClient = new(pendingFeed);
        using WallpaperControl.IcsCalendarProvider pendingProvider = new(pendingClient);
        using CancellationTokenSource stop = new();
        pendingProvider.SetSource("https://calendar.test/private");
        Task pendingRefresh = pendingProvider.RefreshAsync(stop.Token);
        pendingProvider.Dispose();
        stop.Cancel();
        bool refreshCancelled = false;
        try { pendingRefresh.GetAwaiter().GetResult(); } catch (OperationCanceledException) { refreshCancelled = true; }
        check(refreshCancelled, "H4/M4 disposing a provider during refresh releases coordination safely after cancellation");
    }

    private sealed class CountingStream(int length) : Stream
    {
        internal int ReadBytes;
        internal Action? AfterRead;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => ReadBytes; set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            int count = Math.Min(buffer.Length, length - ReadBytes);
            buffer.Span[..count].Clear();
            ReadBytes += count;
            AfterRead?.Invoke();
            return ValueTask.FromResult(count);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class FeedResponse(string text) : HttpMessageHandler
    {
        internal string Text = text;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Text) });
    }
}
