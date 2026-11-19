extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using Microsoft.Data.Sqlite;
using System.Text.Json;

internal static class PriceWatchObservationTests
{
    private static readonly Guid Id = new("10000000-0000-4000-8000-000000000001");
    private static readonly App.PriceWatchGame Game = new(Id, "Offline observation game", "game");
    private static App.PriceWatchOffer Offer(int shop, int cents, DateTimeOffset time, DateTimeOffset? expiry = null) =>
        new(new(shop, App.PriceWatchShops.Supported.Single(s => s.Id == shop).Name), new(cents / 100m, cents, "EUR"),
            new(80, 8000, "EUR"), 0, new("https://fixture.invalid/offer"), time.AddDays(-10), expiry);

    internal static void Run(Action<bool, string> check)
    {
        string folder = Path.Combine(Path.GetTempPath(), "PriceWatchObservations-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try { Storage(check, Path.Combine(folder, "legacy.sqlite")); }
        finally { Directory.Delete(folder, true); }
        Presentation(check);
    }
    private static void Storage(Action<bool, string> check, string path)
    {
        var now = DateTimeOffset.UtcNow;
        var stamp = now.AddMinutes(-5);
        var snapshot = new App.PriceWatchReply<App.PriceWatchPrices[]>(stamp, [new(Id,
            [Offer(35, 2000, stamp), Offer(61, 3000, stamp), Offer(48, 7000, stamp), Offer(35, 2500, stamp)])]);
        // Simulate the existing schema with a stored snapshot, before the additive migration.
        using (var old = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            old.Open(); using var command = old.CreateCommand();
            command.CommandText = """
                CREATE TABLE watched(live INTEGER NOT NULL,id TEXT NOT NULL,title TEXT NOT NULL,kind TEXT,snapshot TEXT,
                    fetched_ticks INTEGER,PRIMARY KEY(live,id));
                INSERT INTO watched VALUES(1,$id,'Existing game','game',$snapshot,$time);
                CREATE TABLE target_prices(live INTEGER NOT NULL,id TEXT NOT NULL,cents INTEGER NOT NULL,
                    PRIMARY KEY(live,id),FOREIGN KEY(live,id) REFERENCES watched(live,id) ON DELETE CASCADE);
                INSERT INTO target_prices VALUES(1,$id,2500);
                CREATE TABLE price_alerts(live INTEGER NOT NULL,id TEXT NOT NULL,reached INTEGER NOT NULL,
                    PRIMARY KEY(live,id),FOREIGN KEY(live,id) REFERENCES target_prices(live,id) ON DELETE CASCADE);
                INSERT INTO price_alerts VALUES(1,$id,1);
                CREATE TABLE price_monitor_state(live INTEGER PRIMARY KEY,last_attempt INTEGER,last_success INTEGER);
                INSERT INTO price_monitor_state VALUES(1,$time,$time);
                """;
            command.Parameters.AddWithValue("$id", Id.ToString("D"));
            command.Parameters.AddWithValue("$snapshot", JsonSerializer.Serialize(snapshot));
            command.Parameters.AddWithValue("$time", stamp.UtcTicks); command.ExecuteNonQuery();
        }
        var store = new App.PriceWatchStore(path, () => now);
        check(store.Read(true).Single().Snapshot?.FetchedAt == stamp && store.Read(true).Single().TargetCents == 2500
            && store.ReadObservations(true, Id).Length == 0,
            "Observation migration retains the old snapshot without inventing historical observations");
        store.SaveTarget(true, Id, 2500);
        store.SaveSnapshot(true, Id, snapshot);
        check(store.ReadMonitorState(true).Success == stamp
            && store.ObservePriceAlert(true, store.Read(true).Single(), [35, 61, 48], snapshot, now) == null,
            "Observation migration preserves automatic schedule and reached alarm state without duplicate alert");
        var captured = store.ReadObservations(true, Id);
        check(captured.Length == 3 && captured.Single(p => p.Shop == 35).Cents == 2000,
            "One successful fetch stores the cheapest valid offer per merchant");
        check(captured.All(p => p.CapturedAt == stamp && p.Currency == "EUR" && p.Source == "local" && p.Live),
            "Observation timestamp is the actual proxy fetchedAt, not the vendor change time or local clock");
        store.SaveSnapshot(true, Id, snapshot);
        check(store.ReadObservations(true, Id).Length == 3, "Repeated cached fetch never duplicates local observations");
        store.SaveSnapshot(true, Id, snapshot with { FetchedAt = stamp.AddMinutes(1) });
        check(store.ReadObservations(true, Id).Length == 6, "A later successful fetch keeps prior local observations");
        store.Add(false, Game); store.SaveSnapshot(false, Id, snapshot);
        var other = Game with { Id = Guid.NewGuid() }; store.Add(true, other);
        check(store.ReadObservations(false, Id).Length == 3 && store.ReadObservations(true, other.Id).Length == 0,
            "Local observations isolate live/test modes and edition UUIDs");
        var invalid = new App.PriceWatchReply<App.PriceWatchPrices[]>(now.AddMinutes(-1), [new(Id,
            [Offer(16, 1, stamp, now.AddSeconds(-1)), Offer(27, 100, stamp) with { Price = new(1, 100, "USD") }])]);
        store.SaveSnapshot(true, Id, invalid);
        check(store.ReadObservations(true, Id).Length == 6, "Expired and non-EUR offers create no observation");
        check(!store.SaveSnapshot(true, Id, snapshot) && store.ReadObservations(true, Id).Length == 6,
            "Rejected late price replies cannot append observations or overwrite newer prices");
        now = now.AddDays(90);
        check(store.ReadObservations(true, Id).Length == 0 && store.Read(true).Single(p => p.Game.Id == Id).TargetCents == 2500,
            "Ninety-day retention purges observations while preserving watchlist and wish price");
        var recent = new App.PriceWatchReply<App.PriceWatchPrices[]>(now.AddMinutes(-1), [new(Id, [Offer(35, 1800, now)])]);
        store.SaveSnapshot(true, Id, recent);
        check(store.ReadObservations(true, Id).Length == 1, "Recording continues after retention cleanup");
        store.Remove(true, Id);
        check(store.ReadObservations(true, Id).Length == 0 && store.ReadObservations(false, Id).Length == 0,
            "Removing a game cascades its observations; expired other-mode rows remain purged");
    }

    private static void Presentation(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new App.PriceWatchReply<App.PriceWatchPrices[]>(now.AddMinutes(-10), [new(Id,
            [Offer(35, 2000, now), Offer(61, 3000, now), Offer(48, 7000, now), Offer(52, 4000, now, now.AddMinutes(-1))])]);
        var entry = new App.PriceWatchEntry(Game, snapshot, 2500);
        App.PriceWatchObservation[] observations = [new(Id, true, 35, now.AddDays(-2), 1000, 4000),
            new(Id, true, 61, now.AddDays(-1), 2000, null), new(Id, false, 61, now.AddDays(-1), 1, null),
            new(Guid.NewGuid(), true, 61, now.AddDays(-1), 2, null), new(Id, true, 61, now.AddDays(-40), 5, null)];
        var points = App.PriceWatchLocalPrices.Create(entry, true, observations, [35, 61, 48, 52], now.AddDays(-30), now, now);
        check(points.Count(p => p.IsLast) == 3 && points.Count(p => !p.IsLast) == 2,
            "Latest snapshot diamonds and older local dots remain separate; expired latest offers have no diamond");
        var formerlyValid = new App.PriceWatchObservation(Id, true, 52, now.AddDays(-2), 4000, null,
            Expiry: now.AddMinutes(-1));
        var expiredHistory = App.PriceWatchLocalPrices.Create(entry, true, [formerlyValid], [52], now.AddDays(-30), now, now);
        check(expiredHistory.Length == 1 && !expiredHistory[0].IsLast,
            "Expired offers are not latest diamonds, but valid past observations retain their historical meaning");
        check(points.Any(p => p.IsLast && p.Observation.Shop == 48) && points.All(p => p.Observation.Cents > 2),
            "A merchant with no ITAD events still has a last-price diamond; other modes/editions are excluded");
        var duplicate = observations.Concat([new(Id, true, 35, snapshot.FetchedAt, 2000, 8000)]);
        check(App.PriceWatchLocalPrices.Create(entry, true, duplicate, [35], now.AddDays(-30), now, now).Length == 2,
            "Snapshot and observation of the same fetch draw one diamond rather than duplicate markers");
        check(App.PriceWatchLocalPrices.Create(entry, true, observations, [61], now.AddDays(-.5), now, now).Length == 1,
            "Visible window and merchant filtering also apply to local prices");
        var expired = snapshot with { FetchedAt = now.AddDays(-31) };
        check(App.PriceWatchLocalPrices.Create(entry with { Snapshot = expired }, true, [], [35], now.AddDays(-30), now, now).Length == 0,
            "Old stored snapshot is never moved to today or outside its real capture window");
        using var chart = new App.PriceWatchHistoryChart("de") { Size = new(640, 220) };
        chart.CreateControl(); chart.Present([], now.AddDays(-30), now, 2500, points);
        var diamond = points.Single(p => p.IsLast && p.Observation.Shop == 48);
        var position = chart.LocalPosition(diamond, App.PriceWatchHistoryChart.PlotArea(chart.Size, chart.DeviceDpi));
        check(chart.HitLocal(Point.Round(position)) == diamond && chart.Hit(Point.Round(position)) == null,
            "Local diamond tooltip hit is separate from ITAD-history events");
        check(chart.LocalText(diamond).Contains("Letzter erfasster Preis") && chart.LocalText(diamond).Contains("70,00")
            && chart.LocalText(diamond).Contains("Lokale Beobachtung"), "Diamond tooltip identifies source, amount and captured instant");
        var history = new App.PriceWatchHistory(new(35, "GOG"), now.AddDays(-1),
            new(new(50, 5000, "EUR"), new(80, 8000, "EUR"), 0));
        var series = App.PriceWatchHistorySeries.Create([history], [35, 48], now.AddDays(-30), now);
        check(App.PriceWatchHistorySeries.Minimum(series)?.Deal?.Price.AmountInt == 5000,
            "Cheaper local observations and diamonds do not enter the ITAD historical minimum");
        chart.Present(series, now.AddDays(-30), now, 2500, points);
        check(chart.LocalPosition(diamond, App.PriceWatchHistoryChart.PlotArea(chart.Size, chart.DeviceDpi)).Y > 0,
            "Combined price scale includes high local prices alongside ITAD prices and target");
        foreach (int dpi in new[] { 96, 144, 192 })
        {
            var area = App.PriceWatchHistoryChart.PlotArea(new(640 * dpi / 96, 220 * dpi / 96), dpi);
            var p = chart.LocalPosition(diamond, area);
            check(area.Contains(p), "Local marker geometry remains within scaled plot at DPI " + dpi);
        }
    }
}
