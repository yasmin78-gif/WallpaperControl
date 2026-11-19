extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using System.Reflection;

internal static class PriceWatchHistoryTests
{
    private static readonly Guid Id = new("10000000-0000-4000-8000-000000000001");
    private static readonly DateTimeOffset End = DateTimeOffset.Parse("2026-11-19T12:00:00Z");
    private static App.PriceWatchHistory Point(int shop, double hours, int? cents) => new(new(shop, shop == 35 ? "GOG" : "Steam"),
        End.AddHours(hours), cents == null ? null : new(new(cents.Value / 100m, cents.Value, "EUR"), new(40, 4000, "EUR"), 50));

    internal static async Task RunAsync(Action<bool, string> check)
    {
        var a = Point(35, -24, 2000); var b = Point(35, -12, 1000); var gap = Point(35, -6, null);
        var c = Point(35, -3, 3000); var d = Point(35, -1, 2500);
        var input = new[] { d, gap, b, a, c, b, Point(61, -5, 0), Point(16, -4, 1), Point(35, -1000, 1), Point(35, 1, 1) };
        var series = App.PriceWatchHistorySeries.Create(input, [35, 61, 52], End.AddDays(-30), End);
        check(series[0].Points.SequenceEqual(new[] { a, b, gap, c, d }), "History sorts actual instants, deduplicates, and clips both range boundaries");
        check(series[0].Steps().SequenceEqual(new[] { (a, b), (c, d) }), "History steps never bridge null gaps or extend past first/last evidence");
        check(series.Length == 3 && series[2].Points.Length == 0, "History includes selected empty merchants and excludes globally unselected stores");
        check(App.PriceWatchHistorySeries.Minimum(series)?.Deal?.Price.AmountInt == 0, "Genuine zero EUR historical price participates in minimum");
        check(App.PriceWatchHistorySeries.Minimum(series.Take(1)) == b, "History minimum respects visible merchants and uses selling price, not regular price");
        var conflict = App.PriceWatchHistorySeries.Create([a, a with { Deal = b.Deal }], [35], End.AddDays(-30), End)[0];
        check(conflict.Points.Single().Deal == null, "Conflicting prices at one instant create an explicit gap");
        var invalid = a with { Deal = new(new(2, 200, "USD"), new(40, 4000, "EUR"), 50) };
        check(App.PriceWatchHistorySeries.Create([invalid], [35], End.AddDays(-30), End)[0].Points.Single().Deal == null,
            "Invalid currency becomes a gap rather than zero or a bridge");
        check(App.PriceWatchHistorySeries.Minimum([]) == null && !App.PriceWatchHistorySeries.Valid(null), "No evidence produces no invented minimum");

        int calls = 0; var clock = End;
        using var cache = new App.PriceWatchHistoryCache((live, id, days, shops, token) =>
        { calls++; return Task.FromResult(new App.PriceWatchReply<App.PriceWatchHistory[]>(clock, input)); }, () => clock);
        await cache.LoadAsync(false, Id, 30, [61, 35], default);
        await cache.LoadAsync(false, Id, 30, [35, 61], default);
        check(calls == 1, "History RAM cache normalizes merchant order and avoids repeat downloads");
        await cache.LoadAsync(true, Id, 30, [35, 61], default);
        await cache.LoadAsync(false, Id, 7, [35, 61], default);
        await cache.LoadAsync(false, Id, 30, [35], default);
        check(calls == 4, "History cache separates mode, range, and merchant selection");
        clock = clock.AddMinutes(16);
        await cache.LoadAsync(false, Id, 30, [35, 61], default);
        check(calls == 5, "History cache expires after fifteen minutes without background timers");
        for (int i = 1; i <= 17; i++) await cache.LoadAsync(false, new Guid(i, 0, 0, new byte[8]), 30, [35], default);
        int before = calls; await cache.LoadAsync(false, Id, 30, [35, 61], default);
        check(calls == before + 1, "History cache evicts older entries at its bounded entry limit");
        try { await cache.LoadAsync(false, Id, 365, [35], default); check(false, "Unsupported history period rejected"); }
        catch (App.PriceWatchException) { check(true, "Unsupported history period rejected without transport"); }

        var ready = new TaskCompletionSource<App.PriceWatchReply<App.PriceWatchHistory[]>>(TaskCreationOptions.RunContinuationsAsynchronously);
        int sharedCalls = 0; CancellationToken transportToken = default;
        using var shared = new App.PriceWatchHistoryCache((live, id, days, shops, token) =>
        { sharedCalls++; transportToken = token; return ready.Task; });
        using var firstCancel = new CancellationTokenSource();
        var first = shared.LoadAsync(false, Id, 30, [35], firstCancel.Token);
        var second = shared.LoadAsync(false, Id, 30, [35], default);
        firstCancel.Cancel();
        try { await first; check(false, "Cancelled history subscriber exits"); }
        catch (OperationCanceledException) { check(true, "Cancelled history subscriber exits"); }
        check(sharedCalls == 1 && !transportToken.IsCancellationRequested, "Shared history transport survives while another subscriber remains");
        ready.SetResult(new(End, input)); await second;
        check(sharedCalls == 1, "Concurrent identical history queries use one source request");
        using var closing = new CancellationTokenSource();
        using var waiting = new App.PriceWatchHistoryCache(async (live, id, days, shops, token) =>
        { transportToken = token; await Task.Delay(Timeout.Infinite, token); return new(End, []); });
        var pending = waiting.LoadAsync(false, Id, 30, [35], closing.Token); closing.Cancel();
        try { await pending; } catch (OperationCanceledException) { }
        check(transportToken.IsCancellationRequested, "Last history subscriber cancellation stops the underlying request");
        int retries = 0;
        using var failures = new App.PriceWatchHistoryCache((live, id, days, shops, token) =>
        { retries++; throw new App.PriceWatchException(App.PriceWatchFailure.Busy, TimeSpan.FromSeconds(30)); });
        for (int i = 0; i < 2; i++)
            try { await failures.LoadAsync(false, Id, 30, [35], default); }
            catch (App.PriceWatchException ex) { check(ex.RetryAfter == TimeSpan.FromSeconds(30), "History keeps the client Retry-After failure intact"); }
        check(retries == 2, "Failed history requests are never cached as valid empty data");
        var offline = new App.PriceWatchTestData();
        foreach (int days in new[] { 7, 30, 90 })
        {
            var fixture = await offline.HistoryAsync(Id, days, App.PriceWatchShops.Supported.Select(s => s.Id).ToArray(), default);
            check(fixture.Data.Any(p => p.Deal == null) && fixture.Data.Any(p => p.Deal?.Cut > 0)
                && fixture.Data.All(p => p.Shop.Id != 52), "Offline history provides gaps, discounts and empty merchants for " + days);
        }
        var fixedSource = new App.PriceWatchTestData(() => End);
        int[] allShops = App.PriceWatchShops.Supported.Select(s => s.Id).ToArray();
        var full = await fixedSource.HistoryAsync(Id, 90, allShops, default);
        foreach (int days in new[] { 7, 30, 90 })
        {
            var window = await fixedSource.HistoryAsync(Id, days, allShops, default);
            check(window.Data.SequenceEqual(full.Data.Where(p => p.Timestamp >= End.AddDays(-days))),
                "Offline " + days + " day window is an exact slice of the same dated event basis");
        }
        check(full.Data.Where(p => p.Deal == null).Select(p => p.Shop.Id).Order().SequenceEqual(new[] { 35, 62 })
            && full.Data.Where(p => p.Deal == null).Select(p => p.Timestamp).Distinct().Count() == 2,
            "Offline gaps affect only GOG and Ubisoft at distinct calendar instants");
        check(full.Data.Where(p => p.Shop.Id == 61).All(p => p.Deal != null)
            && full.Data.Any(p => p.Shop.Id == 61 && p.Timestamp > full.Data.First(p => p.Shop.Id == 35 && p.Deal == null).Timestamp),
            "One merchant's missing data never creates a common offline outage");
        var gog = full.Data.Where(p => p.Shop.Id == 35).Select(p => (p.Timestamp, p.Deal?.Cut));
        var steam = full.Data.Where(p => p.Shop.Id == 61).Select(p => (p.Timestamp, p.Deal?.Cut));
        check(!gog.SequenceEqual(steam), "Offline merchants have individually timed price developments");
        clock = End;
        using var fixedCache = new App.PriceWatchHistoryCache((live, id, days, shops, token) =>
            fixedSource.HistoryAsync(id, days, shops, token), () => End);
        var seven = await fixedCache.LoadAsync(false, Id, 7, allShops, default);
        var ninety = await fixedCache.LoadAsync(false, Id, 90, allShops, default);
        var sevenAgain = await fixedCache.LoadAsync(false, Id, 7, allShops, default);
        check(seven.Data.SequenceEqual(sevenAgain.Data) && ninety.Data.Length > seven.Data.Length,
            "Switching cached periods restores only the matching event slice");
    }

    internal static void RunUi(Action<bool, string> check)
    {
        var points = new[] { Point(35, -24, 2000), Point(35, -12, 1000), Point(35, -6, null), Point(35, -1, 3000) };
        foreach (int dpi in new[] { 96, 144, 192 })
        {
            var area = App.PriceWatchHistoryChart.PlotArea(new(640 * dpi / 96, 220 * dpi / 96), dpi);
            check(area.Width > 0 && area.Height > 0 && Math.Abs(area.Width / dpi * 96 - 546) < .1,
                "History plot geometry scales without clipping at DPI " + dpi);
        }
        using var chart = new App.PriceWatchHistoryChart("de") { Size = new(640, 220), ForeColor = Color.White, BackColor = Color.FromArgb(12, 25, 32) };
        chart.CreateControl();
        var data = App.PriceWatchHistorySeries.Create(points, [35], End.AddDays(-2), End);
        chart.Present(data, End.AddDays(-2), End, 2500);
        var point = chart.Position(points[1], App.PriceWatchHistoryChart.PlotArea(chart.Size, chart.DeviceDpi));
        check(chart.Hit(System.Drawing.Point.Round(point)) == points[1] && chart.Hit(System.Drawing.Point.Empty) == null, "History tooltip hits an actual price point, never arbitrary intermediate values");
        check(chart.PointText(points[1]).Contains("10,00") && chart.PointText(points[1]).Contains("40,00") && chart.PointText(points[1]).Contains("GOG"),
            "History tooltip identifies selling/regular price and merchant separately");
        using var bitmap = new Bitmap(chart.Width, chart.Height); chart.DrawToBitmap(bitmap, chart.ClientRectangle);
        check(bitmap.GetPixel(400, 100).A > 0, "Owner-drawn history chart renders with a target reference line");

        using var host = new Form();
        var calls = new List<TaskCompletionSource<App.PriceWatchReply<App.PriceWatchHistory[]>>>();
        var tokens = new List<CancellationToken>();
        using var view = new App.PriceWatchHistoryView("de", [35, 61], (days, token) =>
        { var tcs = new TaskCompletionSource<App.PriceWatchReply<App.PriceWatchHistory[]>>(TaskCreationOptions.RunContinuationsAsynchronously); calls.Add(tcs); tokens.Add(token); return tcs.Task; });
        host.Controls.Add(view); host.Show(); Application.DoEvents();
        var period = Descendants(view).OfType<ComboBox>().Single();
        check(period.SelectedIndex == 1 && calls.Count == 0, "History defaults to thirty days and stays unloaded until requested");
        var old = view.LoadAsync(); period.SelectedIndex = 0; var newer = view.LoadAsync();
        var state = (Label)typeof(App.PriceWatchHistoryView).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
        var summary = (Label)typeof(App.PriceWatchHistoryView).GetField("summary", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
        App.PriceWatchReply<App.PriceWatchHistory[]>? Current() => (App.PriceWatchReply<App.PriceWatchHistory[]>?)
            typeof(App.PriceWatchHistoryView).GetField("reply", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view);
        check(Current() == null && state.Text == "Preisverlauf wird geladen …" && summary.Text.EndsWith("—"),
            "Period reload clears prior prices and minimum while showing the dedicated loading hint");
        check(tokens[0].IsCancellationRequested, "History period change cancels its preceding operation");
        calls[1].SetResult(new(End, points.Take(1).ToArray())); Pump(newer);
        calls[0].SetResult(new(End, points)); Pump(old);
        var current = (App.PriceWatchReply<App.PriceWatchHistory[]>?)typeof(App.PriceWatchHistoryView)
            .GetField("reply", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view);
        check(current?.Data.Length == 1, "Late history response cannot overwrite a newer range");
        check(summary.Text.Contains("20,00"), "New period minimum is recomputed solely from its accepted reply");
        period.SelectedIndex = 2;
        var failed = view.LoadAsync();
        check(Current() == null && summary.Text.EndsWith("—"), "Previous period is not retained under the ninety-day label");
        calls[^1].SetException(new App.PriceWatchException(App.PriceWatchFailure.Connection)); Pump(failed);
        check(Current() == null && !state.Text.Contains("geladen") && state.Text.Length > 0 && summary.Text.EndsWith("—"),
            "Reload failure shows an error without displaying the previous period or its minimum");
        var recovery = view.LoadAsync();
        var recoveredPoint = Point(61, -2, 500) with { Timestamp = DateTimeOffset.UtcNow.AddDays(-1) };
        calls[^1].SetResult(new(DateTimeOffset.UtcNow, [recoveredPoint])); Pump(recovery);
        check(Current()?.Data.Single() == recoveredPoint && summary.Text.Contains("5,00"),
            "Successful retry restores only the selected window and its own lowest price");
        int before = calls.Count;
        Descendants(view).OfType<CheckBox>().First().Checked = false;
        check(calls.Count == before, "History legend filter changes no global settings and makes no source request");
        var closing = view.LoadAsync(); view.Dispose();
        check(tokens[^1].IsCancellationRequested, "Disposing the history view cancels a running load");
        calls[^1].SetResult(new(End, points)); Pump(closing); host.Close();
        CheckDetailLayouts(check);
    }
    private static void CheckDetailLayouts(Action<bool, string> check)
    {
        var source = new App.PriceWatchTestData();
        var game = source.SearchAsync("Cyberpunk", default).GetAwaiter().GetResult().Data[0];
        int[] shops = App.PriceWatchShops.Supported.Select(s => s.Id).ToArray();
        var prices = source.PricesAsync(game.Id, shops, default).GetAwaiter().GetResult();
        var info = source.InfoAsync(game.Id, default).GetAwaiter().GetResult();
        var fixtures = App.PriceWatchTestData.Details(game);
        string artifacts = Path.Combine(Path.GetTempPath(), "PriceWatch-Phase15A1"); Directory.CreateDirectory(artifacts);
        foreach (var style in new[] { App.SystemWidgetStyle.Minimal, App.SystemWidgetStyle.Clean, App.SystemWidgetStyle.Glow })
        {
            using var form = new App.PriceWatchDetailForm(new(game, prices, 2500), "Testdaten", "de", style,
                () => Task.CompletedTask, () => false, info: info.Data, refreshInfo: () => Task.CompletedTask,
                description: fixtures.Description, coverSource: fixtures.Cover, saveTarget: _ => true, shops: shops,
                loadHistory: (days, token) => source.HistoryAsync(game.Id, days, shops, token),
                readObservations: () => App.PriceWatchTestData.ObservationExamples(game.Id, shops)) { Opacity = 0 };
            form.Show(); Application.DoEvents();
            var history = (App.PriceWatchHistoryView)form.Controls.Find("PriceWatchHistoryView", true).Single();
            ((Button)form.Controls.Find("PriceWatchHistoryToggle", true).Single()).PerformClick(); Application.DoEvents();
            check(form.Controls.Find("PriceWatchHistoryChart", true).Single().Visible
                && Descendants(history).OfType<CheckBox>().Count() == 7, "Expanded history preserves all seven store toggles in " + style);
            var viewport = form.Controls.OfType<Panel>().Single();
            viewport.ScrollControlIntoView(history); Application.DoEvents();
            check(!viewport.HorizontalScroll.Visible && history.Width <= viewport.ClientSize.Width,
                "History fits standard detail width without horizontal scrolling in " + style);
            using (var image = new Bitmap(form.Width, form.Height))
            { form.DrawToBitmap(image, form.ClientRectangle); image.Save(Path.Combine(artifacts, style + ".png")); }
            form.Size = form.MinimumSize; Application.DoEvents();
            var buttons = form.Controls.Find("PriceWatchRefreshInfo", true).Single().Parent!.Controls.OfType<Button>().ToArray();
            viewport.ScrollControlIntoView(buttons.Last().Parent); Application.DoEvents();
            check(!viewport.HorizontalScroll.Visible && buttons.All(b => b.Parent!.ClientRectangle.Contains(b.Bounds)),
                "Small detail layout keeps all actions accessible through vertical scrolling in " + style);
            form.Close();
        }
    }
    private static IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>()
        .SelectMany(c => new[] { c }.Concat(Descendants(c)));
    private static void Pump(Task task)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!task.IsCompleted && DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(5); }
        if (!task.IsCompleted) throw new TimeoutException("History UI check timed out.");
        task.GetAwaiter().GetResult();
    }
}
