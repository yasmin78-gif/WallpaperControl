extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;

internal static class PriceWatchCurveTests
{
    internal static void Run(Action<bool, string> check)
    {
        var start = DateTimeOffset.Parse("2026-11-01T00:00:00Z");
        App.PriceWatchCurveKnot Knot(double hours, double value) => new(start.AddHours(hours), value);
        var knots = new[] { Knot(0, 5000), Knot(1, 1000), Knot(36, 1000), Knot(37, 7000), Knot(130, 3000) };
        var curves = App.PriceWatchCurves.Smooth(knots);
        check(curves.Length == 4 && curves.Select(s => s.From).Append(curves[^1].To).SequenceEqual(knots),
            "Smoothed curves retain all true price endpoints and unequal event timestamps");
        check(curves.All(s => s.Control1.Time == s.From.Time.AddTicks((s.To.Time - s.From.Time).Ticks / 3)
            && s.Control2.Time == s.To.Time.AddTicks(-(s.To.Time - s.From.Time).Ticks / 3)),
            "Bezier time controls preserve actual nonuniform time intervals");
        check(curves.All(s => s.Value(0) == s.From.Cents && s.Value(1) == s.To.Cents),
            "Curve endpoints exactly match observed prices");
        check(Enumerable.Range(0, 101).All(i => Math.Abs(curves[1].Value(i / 100d) - 1000) < 1e-8),
            "Equal neighboring prices produce a flat curve without artificial movement");
        bool bounded = true, monotone = true;
        var random = new Random(151);
        for (int fixture = 0; fixture < 80; fixture++)
        {
            var points = new List<App.PriceWatchCurveKnot>();
            double hours = 0;
            for (int i = 0; i < 15; i++) { hours += .001 + random.NextDouble() * 300; points.Add(Knot(hours, random.Next(0, 100000))); }
            foreach (var segment in App.PriceWatchCurves.Smooth(points))
            {
                double before = segment.From.Cents;
                for (int i = 0; i <= 200; i++)
                {
                    double value = segment.Value(i / 200d);
                    bounded &= value >= Math.Min(segment.From.Cents, segment.To.Cents) - 1e-7
                        && value <= Math.Max(segment.From.Cents, segment.To.Cents) + 1e-7;
                    monotone &= segment.To.Cents >= segment.From.Cents ? value >= before - 1e-7 : value <= before + 1e-7;
                    before = value;
                }
            }
        }
        check(bounded && monotone, "PCHIP remains bounded and monotone across 80 irregular time/price fixtures without overshoot");
        check(App.PriceWatchCurves.Smooth([Knot(0, 1)]).Length == 0
            && App.PriceWatchCurves.Smooth([Knot(0, 1), Knot(0, 2)]).Length == 0,
            "Single and contradictory simultaneous knots create no invented curve");

        App.PriceWatchHistory Price(int shop, int hours, int? cents) => new(new(shop, "Fixture"), start.AddHours(hours), cents == null ? null
            : new(new(cents.Value / 100m, cents.Value, "EUR"), new(1000, 100000, "EUR"), 0));
        var gapSeries = new App.PriceWatchHistorySeries(35,
            [Price(35, 0, 1000), Price(35, 1, 2000), Price(35, 2, null), Price(35, 3, 3000), Price(35, 5, 5000)]);
        var segments = App.PriceWatchCurves.History(gapSeries);
        check(segments.Length == 2 && segments.All(s => !(s.From.Time < start.AddHours(2) && s.To.Time > start.AddHours(2))),
            "Historical smoothing stops at null/conflict gaps and restarts in a separate run");
        var id = Guid.NewGuid();
        App.PriceWatchLocalPoint Local(int shop, int hour, int cents, bool last = false) =>
            new(new(id, true, shop, start.AddHours(hour), cents, null), last);
        var local = new[] { Local(35, 7, 1500), Local(35, 10, 2500, true), Local(61, 9, 8000, true) };
        var connections = App.PriceWatchCurves.Connections(gapSeries, local);
        check(connections.Length == 2 && connections[0].From.Time == start.AddHours(5)
            && connections[0].To.Time == start.AddHours(7) && connections[1].To.Time == start.AddHours(10),
            "Dashed geometry uses later real local instants in order and never mixes stores");
        check(App.PriceWatchCurves.Connections(new(35, gapSeries.Points.Append(Price(35, 6, null)).ToArray()), local).Length == 0,
            "An explicit gap after the last historical price blocks all connections to local prices");
        check(App.PriceWatchCurves.Connections(new(61, []), local).Length == 0,
            "Store without historical evidence retains standalone local points only");
        check(App.PriceWatchCurves.Connections(gapSeries, [Local(35, 5, 999), Local(35, 7, 2000)]).Length == 0,
            "Conflicting historical/local prices at one instant cannot be bridged");
        var conflict = new[] { Local(35, 7, 2000), Local(35, 7, 2500), Local(35, 9, 3000) };
        check(App.PriceWatchCurves.Connections(gapSeries, conflict).Length == 0,
            "Contradictory simultaneous local observations block an unknown connection");
        var expiring = Local(35, 7, 1500) with { Observation = Local(35, 7, 1500).Observation with { Expiry = start.AddHours(8) } };
        check(App.PriceWatchCurves.Connections(gapSeries, [expiring, Local(35, 10, 3000, true)]).Length == 1,
            "A known local offer expiry blocks the later unknown interval");
        check(App.PriceWatchHistorySeries.Minimum([gapSeries])?.Deal?.Price.AmountInt == 1000,
            "Curve geometry and local connectors do not alter the actual ITAD minimum");
        foreach (int dpi in new[] { 96, 144, 192 })
        {
            var area = App.PriceWatchHistoryChart.PlotArea(new(700 * dpi / 96, 220 * dpi / 96), dpi);
            check(area.Width > 0 && area.Right <= 700 * dpi / 96 && area.Bottom < 220 * dpi / 96,
                "Curve plot geometry fits at DPI " + dpi);
        }
    }
}
