extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using Microsoft.Win32;

internal static class TwitchNotificationTests
{
    private sealed class Store : App.ITwitchTokenStore
    {
        internal bool Empty;
        public App.TwitchTokens? Load() => Empty ? null : new() { AccessToken = "fake", RefreshToken = "fake" };
        public void Save(App.TwitchTokens tokens) { }
        public void Clear() { }
    }
    private sealed class Api : App.ITwitchApi
    {
        internal int Calls;
        internal IReadOnlyList<App.TwitchStream> Items = [];
        internal bool Fail;
        internal TaskCompletionSource? Hold;
        public Task<App.TwitchDeviceCode> StartDeviceAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<App.TwitchTokens?> PollDeviceAsync(string code, CancellationToken ct) => throw new NotSupportedException();
        public Task<App.TwitchTokens> RefreshTokenAsync(string token, CancellationToken ct) => throw new NotSupportedException();
        public Task<App.TwitchIdentity> ValidateAsync(string token, CancellationToken ct) => Task.FromResult(new App.TwitchIdentity("test", "test", "Test", 3600));
        public Task<string> GetDisplayNameAsync(string token, string id, CancellationToken ct) => Task.FromResult("Test");
        public async Task<IReadOnlyList<App.TwitchStream>> GetStreamsAsync(string token, string id, CancellationToken ct)
        { Calls++; if (Hold != null) await Hold.Task.WaitAsync(ct); if (Fail) throw new App.TwitchException(App.TwitchError.Offline); return Items; }
        public void Dispose() { }
    }
    private static App.TwitchStream Stream(int id, DateTimeOffset started) => new(id.ToString(), "channel" + id, "Channel " + id, "Game", "Title", 42, started, "");
    internal static void Run(Action<bool, string> check)
    {
        RunAsync(check).GetAwaiter().GetResult();
        string path = @"Software\WallpaperControl.TwitchNotificationTests\" + Guid.NewGuid().ToString("N");
        try
        {
            check(!new App.WidgetSettings().TwitchLiveNotifications, "Live notifications default off");
            var settings = new App.WidgetSettings { TwitchLiveNotifications = true }; settings.Save(path);
            check(App.WidgetSettings.Load(path).TwitchLiveNotifications && settings.Clone().TwitchLiveNotifications, "Notification preference persists and clones");
            settings.TwitchLiveNotifications = false; settings.Save(path);
            check(!App.WidgetSettings.Load(path).TwitchLiveNotifications, "Notification preference can be disabled persistently");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, false); }
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var editor = new App.WidgetSettingsEditor(new App.WidgetSettings { TwitchLiveNotifications = true });
                using var icon = new System.Windows.Forms.NotifyIcon();
                var shown = new List<string>(); var opened = new List<Uri>(); bool fullscreen = false;
                using var balloons = new App.DesktopNotifications(icon, () => fullscreen,
                    (title, body) => { shown.Add(title); check(body == "Zum Kanal öffnen klicken.", "Notification body contains only channel opening hint"); }, uri => { opened.Add(uri); return true; }, () => true);
                var started = DateTimeOffset.UtcNow;
                balloons.Show(Stream(1, started), "de"); balloons.Show(Stream(2, started), "de");
                check(shown.Count == 1 && shown[0] == "Channel 1 ist jetzt live.", "Native notifications serialize without replacing click target");
                balloons.OnClicked();
                check(opened.Count == 1 && opened[0].AbsolutePath == "/channel1" && shown.Count == 2, "Native notification click opens its original channel and advances queue");
                balloons.OnClosed();
                balloons.Show(Stream(3, started), "de"); balloons.Show(Stream(4, started), "de");
                balloons.Clear(); balloons.OnClicked();
                check(opened.Count == 1 && shown.Count == 3, "Disabling clears queued alerts and invalidates old click target");
                fullscreen = true; balloons.Show(Stream(5, started), "de");
                check(shown.Count == 3, "Native notification presenter independently suppresses fullscreen");
                fullscreen = false; balloons.Show(Stream(6, started), "de");
                check(shown.Count == 4, "Native notification presenter resumes without suppressed backlog");
                balloons.Dispose(); balloons.Show(Stream(7, started), "de");
                check(shown.Count == 4, "Disposed presenter creates no notifications");
                check(editor.ReadWidgetSettings(false).TwitchLiveNotifications, "Editor loads notification setting into preview");
                foreach (string language in new[] { "de", "en", "fr", "es", "ja" })
                {
                    editor.ApplyPresentation(false, language);
                    check(editor.ReadWidgetSettings(false).TwitchLiveNotifications, "Localization preserves notification setting " + language);
                    foreach (var key in new[] { "TwitchLoading", "TwitchLiveNotifications", "TwitchLiveNotificationTitle", "TwitchLiveNotificationBody" })
                        check(App.Localization.Get(key, language) != key, "Notification/startup translation " + language + "/" + key);
                }
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if (error != null) throw error;
    }
    private static async Task RunAsync(Action<bool, string> check)
    {
        DateTimeOffset time = new(2026, 10, 2, 18, 34, 59, TimeSpan.Zero);
        foreach (int minutes in new[] { 1, 5, 10, 15 })
        {
            var boundary = App.TwitchRefreshScheduler.NextBoundary(time, minutes);
            check(boundary > time && boundary.Second == 0 && boundary.Minute % minutes == 0, minutes + " minute refresh aligns to the clock");
            check(App.TwitchRefreshScheduler.NextBoundary(boundary, minutes) == boundary.AddMinutes(minutes), minutes + " minute exact boundary advances once");
            var api = new Api(); using var service = new App.TwitchService(api, new Store(), () => time);
            using var scheduler = new App.TwitchRefreshScheduler(service, () => time, false); scheduler.Configure(true, minutes);
            check(service.EmptyStateKey == "TwitchLoading", "Startup presents loading before token validation " + minutes);
            await scheduler.EvaluateAsync();
            check(api.Calls == 1 && service.Connected && service.EmptyStateKey == "TwitchEmpty", "Startup immediately fetches streams and displays successful empty response " + minutes);
            // A request at :59 followed one second later is coalesced by the existing ten-second API throttle.
            time = boundary.AddMinutes(minutes); await scheduler.EvaluateAsync(); await scheduler.EvaluateAsync();
            check(api.Calls == 2, "Clock boundary refresh has no duplicate " + minutes);
        }
        var emptyApi = new Api(); using (var disconnected = new App.TwitchService(emptyApi, new Store { Empty = true }, () => time))
        { await disconnected.RefreshAsync(); check(disconnected.EmptyStateKey == "TwitchDisconnected" && emptyApi.Calls == 0, "Missing login is disconnected after initialization, not an empty live list"); }

        var heldApi = new Api { Hold = new() }; using (var held = new App.TwitchService(heldApi, new Store(), () => time))
        {
            using var scheduler = new App.TwitchRefreshScheduler(held, () => time, false);
            var start = scheduler.EvaluateAsync(); await scheduler.EvaluateAsync();
            check(held.EmptyStateKey == "TwitchLoading" && heldApi.Calls == 1, "Slow first fetch stays loading and does not overlap");
            scheduler.SetSuspended(true); await start;
            time += TimeSpan.FromSeconds(11); heldApi.Hold = null; scheduler.SetSuspended(false); await scheduler.EvaluateAsync();
            check(heldApi.Calls == 2 && held.EmptyStateKey == "TwitchEmpty", "Cancelled startup fetch retries immediately on resume after API throttle");
        }
        var source = new Api { Items = [Stream(1, time)] }; using var session = new App.TwitchService(source, new Store(), () => time);
        var notified = new List<App.TwitchStream>(); bool fullscreen = false;
        using var observer = new App.TwitchLiveNotifications(session, notified.Add, () => fullscreen);
        async Task Refresh() { time += TimeSpan.FromMinutes(6); await session.RefreshAsync(); }
        await Refresh(); check(notified.Count == 0, "Disabled notifications issue no alert");
        observer.Configure(true); await Refresh(); check(notified.Count == 0, "Initial enabled snapshot suppresses existing live streams");
        source.Items = [source.Items[0], Stream(2, time)]; await Refresh();
        check(notified.Count == 1 && notified[0].UserId == "2", "New live channel produces exactly one alert");
        await Refresh(); check(notified.Count == 1, "Repeated snapshot does not repeat alert");
        source.Fail = true; await Refresh(); check(notified.Count == 1, "Network error does not notify or reset baseline");
        source.Fail = false; await Refresh(); check(notified.Count == 1, "Recovery after network error does not repeat alert");
        source.Items = []; await Refresh(); source.Items = [Stream(2, notified[0].StartedAt)]; await Refresh();
        check(notified.Count == 1, "Same broadcast reappearing after empty snapshot remains deduplicated");
        source.Items = [Stream(2, time)]; await Refresh(); check(notified.Count == 2, "A later broadcast from same channel notifies again");
        fullscreen = true; source.Items = [Stream(3, time)]; await Refresh(); check(notified.Count == 2, "Fullscreen suppresses live notification");
        fullscreen = false; await Refresh(); check(notified.Count == 2, "Fullscreen suppression has no deferred alert");
        observer.SetSuspended(true); source.Items = [Stream(4, time)]; observer.SetSuspended(false); await Refresh();
        check(notified.Count == 2, "First snapshot after suspended polling establishes baseline without backlog");
        source.Items = [source.Items[0], Stream(5, time)]; await Refresh(); check(notified.Count == 3, "Notifications resume for subsequent new broadcasts");
        observer.Configure(false); source.Items = [Stream(6, time)]; await Refresh(); observer.Configure(true); await Refresh();
        check(notified.Count == 3, "Re-enabling notification suppresses already running broadcasts");
        observer.Dispose(); source.Items = [Stream(7, time)]; await Refresh(); check(notified.Count == 3, "Disposed observer removes subscriptions");
    }
}
