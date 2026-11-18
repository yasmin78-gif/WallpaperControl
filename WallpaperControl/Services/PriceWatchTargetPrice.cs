using System.Globalization;

namespace WallpaperControl;

internal static class PriceWatchTargetPrice
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    internal static bool TryParse(string text, out int? cents)
    {
        cents = null;
        text = text.Trim();
        if (text.Length == 0) return true;
        string[] parts = text.Split(',');
        if (parts.Length > 2 || parts[0].Length == 0 || parts.Any(part => part.Any(ch => ch is < '0' or > '9'))
            || parts.Length == 2 && parts[1].Length is not (1 or 2)) return false;
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, German, out decimal amount)
            || amount > int.MaxValue / 100m) return false;
        cents = decimal.ToInt32(amount * 100);
        return true;
    }

    internal static string Format(int cents) => (cents / 100m).ToString("F2", German);

    internal static bool IsReached(PriceWatchEntry entry, PriceWatchOffer offer, DateTimeOffset now,
        IReadOnlyList<int>? selected = null) =>
        entry.TargetCents is >= 0 && (selected ?? PriceWatchShops.Legacy).Contains(offer.Shop.Id)
        && PriceWatchShops.ValidOffer(offer, now)
        && entry.Snapshot?.Data.Any(item => item.Id == entry.Game.Id && item.Deals.Contains(offer)) == true
        && offer.Price.Currency == "EUR" && offer.Price.AmountInt >= 0
        && offer.Price.Amount == offer.Price.AmountInt / 100m
        && (offer.Expiry == null || offer.Expiry > now)
        && offer.Price.AmountInt <= entry.TargetCents;
}
