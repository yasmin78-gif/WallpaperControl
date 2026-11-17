using System.Globalization;

namespace WallpaperControl;

internal readonly record struct PriceWatchOfferColumns(string Merchant, string Price, string Discount);

internal static class PriceWatchOfferDisplay
{
    internal const int ShopCount = 2;
    internal static readonly IReadOnlyList<PriceWatchShop> Shops = [new(35, "GOG"), new(61, "Steam")];

    internal static IEnumerable<PriceWatchOffer> ForShop(PriceWatchEntry entry, int shopId) =>
        entry.Snapshot?.Data.SelectMany(item => item.Deals).Where(offer => offer.Shop.Id == shopId) ?? [];

    internal static string Price(PriceWatchOffer offer, CultureInfo culture) =>
        $"{offer.Price.Amount.ToString("N2", culture)} €";

    internal static string Discount(PriceWatchOffer offer, CultureInfo culture) =>
        $"{(offer.Cut == 0 ? "" : "−")}{offer.Cut.ToString("0.##", culture)} %";

    internal static PriceWatchOfferColumns Columns(PriceWatchShop shop, PriceWatchOffer? offer,
        CultureInfo culture, string noOffer) =>
        offer == null ? new(shop.Name, noOffer, "") : new(shop.Name, Price(offer, culture), Discount(offer, culture));

    internal static string TagNames(PriceWatchOfferTag[]? tags) =>
        string.Join(", ", (tags ?? []).Select(tag => tag?.Name).OfType<string>()
            .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase));
}
