extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using System.Globalization;

internal static class PriceWatchMonitorTests
{
    private static readonly Guid Id = new("10000000-0000-4000-8000-000000000001");
    private static readonly Guid Other = new("10000000-0000-4000-8000-000000000002");

    internal static async Task RunAsync(Action<bool, string> check)
    {
        var now = new DateTimeOffset(2026, 11, 18, 8, 0, 0, TimeSpan.Zero);
        var state = new App.PriceWatchMonitorState(null, now.AddHours(-12));
        check(App.PriceWatchSchedule.Due(now, TimeZoneInfo.Utc, state, startup: false)
            && App.PriceWatchSchedule.Due(now.AddHours(12), TimeZoneInfo.Utc, state with { Success = now }, startup: false)
            && App.PriceWatchSchedule.Slot(now, TimeZoneInfo.Utc, next: true) == now.AddHours(12),
            "automatic schedule checks at 08:00 and 20:00 local time");
        check(App.PriceWatchSchedule.Due(now, TimeZoneInfo.Utc, state, startup: true)
            && !App.PriceWatchSchedule.Due(now, TimeZoneInfo.Utc, state with { Success = now.AddHours(-11) }, startup: true)
            && !App.PriceWatchSchedule.Due(now.AddMinutes(1), TimeZoneInfo.Utc, state with { Success = now.AddMinutes(-1) }, startup: false),
            "startup catch-up requires twelve hours and suppresses an immediate second cycle");
        var berlin = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
        var spring = new DateTimeOffset(2026, 3, 29, 6, 0, 0, TimeSpan.Zero);
        var previous = new DateTimeOffset(2026, 3, 28, 19, 0, 0, TimeSpan.Zero);
        check(App.PriceWatchSchedule.Slot(spring, berlin, next: false) == spring
            && App.PriceWatchSchedule.Due(spring, berlin, new(null, previous), startup: false),
            "daylight-saving transition retains the 08:00 Windows-local slot after an eleven-hour night");

        string directory = Path.Combine(Path.GetTempPath(), $"pricewatch-monitor-{Guid.NewGuid():N}");
        try
        {
            string path = Path.Combine(directory, "watchlist.sqlite");
            var store = new App.PriceWatchStore(path);
            var game = new App.PriceWatchGame(Id, "Cyberpunk 2077", "game");
            store.Add(false, game);
            store.Add(false, new(Other, "No target", "package"));
            store.Add(true, game);
            store.SaveTarget(false, Id, 3000);
            store.SaveTarget(true, Id, 3000);
            var alerts = new List<(App.PriceWatchAlert Alert, bool Live)>();
            int calls = 0, cents = 2000;
            bool fail = false, incomplete = false, malformed = false;
            App.PriceWatchMonitor Create(App.PriceWatchStore storage) => new(storage, (live, id, shops, token) =>
            {
                calls++;
                token.ThrowIfCancellationRequested();
                if (fail) throw new App.PriceWatchException(App.PriceWatchFailure.Connection);
                var reply = Reply(malformed ? Other : id, now, cents, incomplete ? [35] : shops);
                return Task.FromResult<App.PriceWatchReply<App.PriceWatchPrices[]>?>(reply);
            }, (alert, live) => alerts.Add((alert, live)), () => Task.CompletedTask,
                () => now, TimeZoneInfo.Utc, useTimer: false);

            using (var monitor = Create(store))
            {
                monitor.Configure(false, true, false, [35, 61]);
                await monitor.EvaluateAsync(force: true);
                check(calls == 0, "disabled automatic monitoring makes no request");
                monitor.Configure(true, true, false, [35, 61]);
                await monitor.EvaluateAsync();
                check(calls == 1 && alerts.Count == 1 && !alerts[0].Live && alerts[0].Alert.Offer.Shop.Id == 35,
                    "automatic check only fetches targeted games and emits a simulated cheapest-shop alert");
                await monitor.EvaluateAsync();
                check(calls == 1 && alerts.Count == 1, "a successful catch-up does not immediately run again");
                now = now.AddHours(12);
                await monitor.EvaluateAsync();
                check(calls == 2 && alerts.Count == 1, "a still-reached target does not produce a duplicate alert");
            }
            using (var restarted = Create(new(path)))
            {
                restarted.Configure(true, true, false, [35, 61]);
                now = now.AddHours(12);
                await restarted.EvaluateAsync();
                check(alerts.Count == 1, "a restarted monitor preserves the durable reached state");
                incomplete = true; cents = 5000;
                now = now.AddHours(12);
                await restarted.EvaluateAsync();
                incomplete = false; cents = 2000;
                now = now.AddHours(12);
                await restarted.EvaluateAsync();
                check(alerts.Count == 1, "an incomplete above-target reply cannot rearm the alert");
                fail = true; now = now.AddHours(12);
                var before = store.Read(false).Single(item => item.Game.Id == Id).Snapshot;
                await restarted.EvaluateAsync();
                check(store.Read(false).Single(item => item.Game.Id == Id).Snapshot?.FetchedAt == before?.FetchedAt,
                    "a failed automatic request retains the last valid price snapshot");
                fail = false; malformed = true; now = now.AddHours(12);
                await restarted.EvaluateAsync();
                malformed = false; now = now.AddHours(12);
                await restarted.EvaluateAsync();
                check(alerts.Count == 1, "a wrong-edition reply cannot reset a durable alarm");
                cents = 5000; now = now.AddHours(12);
                await restarted.EvaluateAsync();
                cents = 2000; now = now.AddHours(12);
                await restarted.EvaluateAsync();
                check(alerts.Count == 2, "complete above-target prices rearm the next valid price drop");
                store.SaveTarget(false, Id, 4000);
                now = now.AddHours(12);
                await restarted.EvaluateAsync();
                check(alerts.Count == 2, "raising a target while it remains reached does not repeat the alert");
                store.SaveTarget(false, Id, null);
                int beforeRemoval = calls;
                now = now.AddHours(12);
                await restarted.EvaluateAsync();
                check(calls == beforeRemoval, "removing a target removes the game from automatic checks");
                store.SaveTarget(false, Id, 3000);
                now = now.AddHours(12);
                await restarted.EvaluateAsync();
                check(alerts.Count == 3, "a newly configured target after removal has its own alarm state");
                restarted.Configure(true, true, true, [35, 61]);
                await restarted.EvaluateAsync(force: true);
                check(alerts.Count == 4 && alerts[^1].Live, "test and live modes keep independent durable alarms");
            }

            // Even a source that ignores cancellation must not save an obsolete selection.
            var pending = new TaskCompletionSource<App.PriceWatchReply<App.PriceWatchPrices[]>?>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var changing = new App.PriceWatchMonitor(store, (_, _, _, _) => pending.Task,
                (alert, live) => alerts.Add((alert, live)), () => Task.CompletedTask, () => now,
                TimeZoneInfo.Utc, useTimer: false);
            changing.Configure(true, true, false, [35]);
            var running = changing.EvaluateAsync(force: true);
            changing.Configure(true, true, false, [61]);
            pending.SetResult(Reply(Id, now.AddMinutes(1), 1000, [35]));
            await running;
            check(store.Read(false).Single(item => item.Game.Id == Id).Snapshot!.FetchedAt != now.AddMinutes(1),
                "a late automatic reply from the previous shop selection is discarded");
            var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var ending = new App.PriceWatchMonitor(store, async (_, _, _, token) =>
            {
                try { await Task.Delay(Timeout.Infinite, token); }
                finally { stopped.SetResult(); }
                return null;
            }, (_, _) => throw new Exception("Unexpected alert"), () => Task.CompletedTask,
                () => now, TimeZoneInfo.Utc, useTimer: false);
            ending.Configure(true, true, false, [35]);
            var finishing = ending.EvaluateAsync(force: true);
            ending.Dispose();
            await finishing.WaitAsync(TimeSpan.FromSeconds(5));
            check(stopped.Task.IsCompleted, "disposing automatic monitoring cancels its in-flight request");

            store.SaveTarget(false, Id, 3000);
            var targetChanged = new TaskCompletionSource<App.PriceWatchReply<App.PriceWatchPrices[]>?>(TaskCreationOptions.RunContinuationsAsynchronously);
            int inFlight = 0;
            using var changes = new App.PriceWatchMonitor(store, (_, _, _, _) =>
            {
                inFlight++;
                return targetChanged.Task;
            }, (_, _) => throw new Exception("Stale target alert"), () => Task.CompletedTask,
                () => now, TimeZoneInfo.Utc, useTimer: false);
            changes.Configure(true, true, false, [35]);
            var changingTarget = changes.EvaluateAsync(force: true);
            await changes.EvaluateAsync(force: true);
            check(inFlight == 1, "overlapping automatic evaluations do not duplicate the active request");
            store.SaveTarget(false, Id, 500);
            targetChanged.SetResult(Reply(Id, now.AddMinutes(2), 1000, [35]));
            await changingTarget;
            check(store.Read(false).Single(item => item.Game.Id == Id).Snapshot!.FetchedAt != now.AddMinutes(2),
                "changing the target during a query rejects a late result");

            var scheduledStore = new App.PriceWatchStore(Path.Combine(directory, "schedule.sqlite"));
            scheduledStore.Add(false, game);
            scheduledStore.SaveTarget(false, Id, 3000);
            now = new DateTimeOffset(2026, 11, 18, 18, 0, 0, TimeSpan.Zero);
            scheduledStore.SaveMonitorState(false, now.AddHours(-9), success: true);
            int timedCalls = 0;
            using var timed = new App.PriceWatchMonitor(scheduledStore, (_, id, selected, _) =>
            {
                timedCalls++;
                return Task.FromResult<App.PriceWatchReply<App.PriceWatchPrices[]>?>(Reply(id, now, 2000, selected));
            }, (_, _) => { }, () => Task.CompletedTask, () => now, TimeZoneInfo.Utc, useTimer: false);
            timed.Configure(true, true, false, [35]);
            await timed.EvaluateAsync();
            now = now.AddHours(2);
            await timed.EvaluateAsync();
            check(timedCalls == 1, "skipping an unnecessary startup catch-up still allows the next regular 20:00 check");
            var silentStore = new App.PriceWatchStore(Path.Combine(directory, "silent.sqlite"));
            silentStore.Add(false, game);
            silentStore.SaveTarget(false, Id, 3000);
            int silentNotices = 0;
            using var silent = new App.PriceWatchMonitor(silentStore, (_, id, selected, _) =>
                Task.FromResult<App.PriceWatchReply<App.PriceWatchPrices[]>?>(Reply(id, now, 2000, selected)),
                (_, _) => silentNotices++, () => Task.CompletedTask, () => now, TimeZoneInfo.Utc, useTimer: false);
            silent.Configure(true, false, false, [35]);
            await silent.EvaluateAsync(force: true);
            silent.Configure(true, true, false, [35]);
            await silent.EvaluateAsync(force: true);
            check(silentNotices == 0, "disabled notifications still maintain the alarm state without replaying an old hit when re-enabled");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static App.PriceWatchReply<App.PriceWatchPrices[]> Reply(Guid id, DateTimeOffset now, int cents, int[] shops) =>
        new(now, [new(id, shops.Select((shop, index) => new App.PriceWatchOffer(
            new(shop, shop == 35 ? "GOG" : "Steam"), new((cents + index * 100) / 100m, cents + index * 100, "EUR"),
            new(60m, 6000, "EUR"), 0, new($"https://fixture.invalid/offer/{shop}"), now, null)).ToArray())]);

    internal static void RunUi(Action<bool, string> check)
    {
        using (var editor = new App.WidgetSettingsEditor(new App.WidgetSettings(), dark: true, language: "de"))
        {
            editor.SelectWidget("PriceWatchTitle");
            var automatic = (CheckBox)editor.Controls.Find("PriceWatchAutomatic", true).Single();
            var notifications = (CheckBox)editor.Controls.Find("PriceWatchNotifications", true).Single();
            check(automatic.Checked && notifications.Checked && notifications.Enabled, "automatic monitoring and notices default to enabled");
            automatic.Checked = false;
            var values = editor.ReadWidgetSettings(false).PriceWatch;
            check(!notifications.Enabled && !values.AutomaticMonitoring && values.PriceNotifications,
                "notifications are unavailable while automatic monitoring is disabled");
        }
        using var icon = new NotifyIcon();
        var shown = new List<string>();
        var opened = new List<Uri>();
        using var desktop = new App.DesktopNotifications(icon, fullscreen: () => false,
            display: (title, body) => shown.Add(title + body), visible: () => true);
        var offer = Reply(Id, DateTimeOffset.UtcNow, 2999, [35]).Data.Single().Deals.Single();
        var alert = new App.PriceWatchAlert(Id, "Cyberpunk 2077: Ultimate Edition", offer, 3999);
        App.PriceWatchNotifications.Show(desktop, alert, "de", open: opened.Add, allowed: () => true);
        App.PriceWatchNotifications.Show(desktop, alert with { Id = Other }, "de", open: opened.Add, allowed: () => true);
        check(shown.Count == 1 && shown[0].Contains("Wunschpreis erreicht") && shown[0].Contains("39,99"),
            "native notification adapter serializes multiple alerts through the existing bounded queue");
        desktop.OnClicked();
        check(opened.Single() == offer.Url && shown.Count == 2, "clicking an alert opens the original validated shop URL and advances the queue");
        desktop.OnClosed();
        App.PriceWatchNotifications.Show(desktop, alert with { Offer = offer with { Url = new("file:///C:/invalid") } },
            "de", open: opened.Add, allowed: () => true);
        App.PriceWatchNotifications.Show(desktop, alert, "de", open: opened.Add, allowed: () => false);
        check(shown.Count == 2, "invalid links and Windows-suppressed notifications have no popup or click action");
        string directory = Path.Combine(Path.GetTempPath(), $"pricewatch-manual-alert-{Guid.NewGuid():N}");
        try
        {
            var store = new App.PriceWatchStore(Path.Combine(directory, "watchlist.sqlite"));
            var data = new App.PriceWatchTestData();
            var game = data.SearchAsync("Cyberpunk", default).Result.Data[0];
            store.Add(false, game);
            store.SaveTarget(false, game.Id, 3000);
            store.SaveMonitorState(false, DateTimeOffset.UtcNow, success: true);
            int simulated = 0;
            using var widget = new App.PriceWatchWidgetForm(_ => data, new() { Enabled = true, ShopIds = [35, 61] },
                "de", _ => { }, store, monitorPrices: true, alert: (_, live) =>
                {
                    if (live) throw new Exception("Offline alert entered the live notification path.");
                    simulated++;
                });
            widget.Show();
            Application.DoEvents();
            var refresh = typeof(App.PriceWatchWidgetForm).GetMethod("RefreshPricesAsync",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var first = (Task)refresh.Invoke(widget, [false])!;
            var wait = System.Diagnostics.Stopwatch.StartNew();
            while (!first.IsCompleted && wait.Elapsed < TimeSpan.FromSeconds(5)) Application.DoEvents();
            first.GetAwaiter().GetResult();
            var second = (Task)refresh.Invoke(widget, [false])!;
            wait.Restart();
            while (!second.IsCompleted && wait.Elapsed < TimeSpan.FromSeconds(5)) Application.DoEvents();
            second.GetAwaiter().GetResult();
            check(simulated == 1, "manual refresh reuses durable deduplication and emits only a simulated offline notification");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        check(Environment.OSVersion.Version.Build >= 22000, "notification adapter tests run on Windows 11 without emitting real notifications");
    }
}
