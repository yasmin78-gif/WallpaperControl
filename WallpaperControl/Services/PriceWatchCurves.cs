namespace WallpaperControl;

// Display geometry only: interpolated values must never enter observations, tooltips or statistics.
internal sealed record PriceWatchCurveKnot(DateTimeOffset Time, double Cents);
internal sealed record PriceWatchCurveSegment(PriceWatchCurveKnot From, PriceWatchCurveKnot Control1,
    PriceWatchCurveKnot Control2, PriceWatchCurveKnot To)
{
    internal double Value(double t)
    {
        double u = 1 - t;
        return u * u * u * From.Cents + 3 * u * u * t * Control1.Cents
            + 3 * u * t * t * Control2.Cents + t * t * t * To.Cents;
    }
}

internal static class PriceWatchCurves
{
    // PCHIP weighted harmonic tangents for nonuniform time intervals, expressed as cubic Beziers.
    internal static PriceWatchCurveSegment[] Smooth(IReadOnlyList<PriceWatchCurveKnot> points)
    {
        int n = points.Count;
        if (n < 2) return [];
        var h = new double[n - 1]; var d = new double[n - 1]; var m = new double[n];
        for (int i = 0; i < n - 1; i++)
        {
            h[i] = (points[i + 1].Time - points[i].Time).TotalSeconds;
            if (h[i] <= 0 || !double.IsFinite(points[i].Cents) || !double.IsFinite(points[i + 1].Cents))
                return []; // Never connect contradictory/nonchronological knots.
            d[i] = (points[i + 1].Cents - points[i].Cents) / h[i];
        }
        if (n == 2) m[0] = m[1] = d[0];
        else
        {
            m[0] = Endpoint(h[0], h[1], d[0], d[1]);
            m[n - 1] = Endpoint(h[n - 2], h[n - 3], d[n - 2], d[n - 3]);
            for (int i = 1; i < n - 1; i++)
            {
                if (d[i - 1] == 0 || d[i] == 0 || Math.Sign(d[i - 1]) != Math.Sign(d[i])) continue;
                double w1 = 2 * h[i] + h[i - 1], w2 = h[i] + 2 * h[i - 1];
                m[i] = (w1 + w2) / (w1 / d[i - 1] + w2 / d[i]);
            }
        }
        var segments = new PriceWatchCurveSegment[n - 1];
        for (int i = 0; i < n - 1; i++)
        {
            var a = points[i]; var b = points[i + 1];
            double lo = Math.Min(a.Cents, b.Cents), hi = Math.Max(a.Cents, b.Cents);
            long third = (b.Time - a.Time).Ticks / 3;
            segments[i] = new(a,
                new(a.Time.AddTicks(third), Math.Clamp(a.Cents + m[i] * h[i] / 3, lo, hi)),
                new(b.Time.AddTicks(-third), Math.Clamp(b.Cents - m[i + 1] * h[i] / 3, lo, hi)), b);
        }
        return segments;
    }
    private static double Endpoint(double h0, double h1, double d0, double d1)
    {
        double m = ((2 * h0 + h1) * d0 - h0 * d1) / (h0 + h1);
        if (Math.Sign(m) != Math.Sign(d0)) return 0;
        return Math.Sign(d0) != Math.Sign(d1) && Math.Abs(m) > Math.Abs(3 * d0) ? 3 * d0 : m;
    }

    internal static PriceWatchCurveSegment[] History(PriceWatchHistorySeries series)
    {
        var result = new List<PriceWatchCurveSegment>(); var run = new List<PriceWatchCurveKnot>();
        foreach (var point in series.Points)
        {
            if (!PriceWatchHistorySeries.Valid(point.Deal))
            { result.AddRange(Smooth(run)); run.Clear(); }
            else run.Add(new(point.Timestamp, point.Deal!.Price.AmountInt));
        }
        result.AddRange(Smooth(run)); return result.ToArray();
    }

    internal static PriceWatchCurveSegment[] Connections(PriceWatchHistorySeries series, PriceWatchLocalPoint[] local)
    {
        // An ending null/conflict prevents a historical anchor, even if older valid prices exist.
        var last = series.Points.LastOrDefault();
        if (last == null || !PriceWatchHistorySeries.Valid(last.Deal)) return [];
        var observations = local.Select(p => p.Observation).Where(p => p.Shop == series.Shop
            && p.Currency == "EUR" && p.Source == "local" && p.Cents >= 0 && p.CapturedAt >= last.Timestamp)
            .OrderBy(p => p.CapturedAt).ToArray();
        if (observations.Select(p => (p.Id, p.Live)).Distinct().Count() > 1) return [];
        var chain = new List<PriceWatchCurveKnot> { new(last.Timestamp, last.Deal!.Price.AmountInt) };
        DateTimeOffset? expires = null;
        foreach (var group in observations.GroupBy(p => p.CapturedAt))
        {
            if (group.Select(p => p.Cents).Distinct().Count() != 1) break;
            var point = group.First();
            if (point.CapturedAt == last.Timestamp)
            {
                if (point.Cents != last.Deal.Price.AmountInt) return [];
                expires = point.Expiry; continue;
            }
            if (expires <= point.CapturedAt || point.Expiry <= point.CapturedAt) break;
            chain.Add(new(point.CapturedAt, point.Cents)); expires = point.Expiry;
        }
        return Smooth(chain);
    }
}
