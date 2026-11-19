namespace WallpaperControl;

// History is a change log, not a set of daily measurements. Never extend its endpoints.
internal sealed record PriceWatchHistorySeries(int Shop, PriceWatchHistory[] Points)
{
    internal static bool Valid(PriceWatchChange? deal) => deal != null && Money(deal.Price) && Money(deal.Regular)
        && deal.Cut is >= 0 and <= 100;
    private static bool Money(PriceWatchMoney? money) => money is { Currency: "EUR", AmountInt: >= 0 }
        && money.Amount == money.AmountInt / 100m;

    internal static PriceWatchHistorySeries[] Create(IEnumerable<PriceWatchHistory> data, int[] shops,
        DateTimeOffset start, DateTimeOffset end) => shops.Select(shop => new PriceWatchHistorySeries(shop,
            data.Where(p => p != null && p.Shop?.Id == shop && p.Timestamp >= start && p.Timestamp <= end)
                .GroupBy(p => p.Timestamp).OrderBy(g => g.Key).Select(g =>
                {
                    var points = g.Distinct().ToArray();
                    var first = points[0];
                    // A conflicting or malformed event is an explicit gap, never a zero or a bridge.
                    return first with { Deal = points.Length == 1 && Valid(first.Deal) ? first.Deal : null };
                }).ToArray())).ToArray();

    internal IEnumerable<(PriceWatchHistory From, PriceWatchHistory To)> Steps()
    {
        for (int i = 1; i < Points.Length; i++)
            if (Points[i - 1].Deal != null && Points[i].Deal != null)
                yield return (Points[i - 1], Points[i]);
    }

    internal static PriceWatchHistory? Minimum(IEnumerable<PriceWatchHistorySeries> series) =>
        series.SelectMany(s => s.Points).Where(p => Valid(p.Deal))
            .OrderBy(p => p.Deal!.Price.AmountInt).ThenBy(p => p.Timestamp).ThenBy(p => p.Shop.Id).FirstOrDefault();
}
