namespace WallpaperControl;

internal static class PriceWatchShops
{
    internal static readonly IReadOnlyList<PriceWatchShop> Supported =
    [
        new(35, "GOG"), new(61, "Steam"), new(16, "Epic Game Store"), new(27, "GamesPlanet DE"),
        new(62, "Ubisoft Store"), new(48, "Microsoft Store"), new(52, "EA Store")
    ];
    internal static int[] Legacy => [35, 61];
    internal static int[] Defaults => [35, 61, 16];
    internal static bool IsSupported(int id) => Supported.Any(shop => shop.Id == id);

    internal static int[] Normalize(IEnumerable<int>? ids)
    {
        int[] selected = (ids ?? []).Where(IsSupported).Distinct().ToArray();
        return selected.Length == 0 ? Defaults : selected;
    }

    internal static int[] Validate(IEnumerable<int> ids)
    {
        int[] selected = ids.Distinct().ToArray();
        if (selected.Length == 0 || selected.Any(id => !IsSupported(id)))
            throw new PriceWatchException(PriceWatchFailure.InvalidRequest);
        return selected;
    }

    internal static bool ValidOffer(PriceWatchOffer offer, DateTimeOffset now) =>
        IsSupported(offer.Shop.Id) && offer.Price is { Currency: "EUR", AmountInt: >= 0 }
        && offer.Price.Amount == offer.Price.AmountInt / 100m
        && offer.Regular is { Currency: "EUR", AmountInt: >= 0 }
        && offer.Regular.Amount == offer.Regular.AmountInt / 100m
        && offer.Cut is >= 0 and <= 100 && offer.Timestamp != default
        && (offer.Expiry == null || offer.Expiry > now)
        && PriceWatchShopLinks.Validate(offer.Url) != null;

    internal static PriceWatchOffer[] WidgetOffers(PriceWatchEntry entry, IReadOnlyList<int> selected, DateTimeOffset now) =>
        selected.Select((id, order) => (Offer: PriceWatchOfferDisplay.ForShop(entry, id)
                .Where(offer => ValidOffer(offer, now)).MinBy(offer => offer.Price.AmountInt), Order: order))
            .Where(item => item.Offer != null).OrderBy(item => item.Offer!.Price.AmountInt).ThenBy(item => item.Order)
            .Take(3).Select(item => item.Offer!).ToArray();
}
