using System.Globalization;
using System.Runtime.InteropServices;

namespace WallpaperControl;

internal static class PriceWatchNotifications
{
    internal static void Show(DesktopNotifications notifications, PriceWatchAlert alert, string language,
        Action<Uri>? open = null, Func<bool>? allowed = null)
    {
        if (!(allowed ?? WindowsAllowsNotifications)()
            || !PriceWatchShops.ValidOffer(alert.Offer, DateTimeOffset.UtcNow)
            || alert.Offer.Price.AmountInt > alert.TargetCents
            || PriceWatchShopLinks.Validate(alert.Offer.Url) is not { } url)
            return;
        var culture = CultureInfo.GetCultureInfo(language);
        string body = string.Format(culture, Localization.Get("PriceWatchAlertBody", language), alert.Title,
            alert.Offer.Shop.Name, PriceWatchOfferDisplay.Price(alert.Offer, culture),
            (alert.TargetCents / 100m).ToString("N2", culture));
        notifications.Show(Localization.Get("PriceWatchAlertTitle", language), body, () =>
        {
            if ((alert.Offer.Expiry == null || alert.Offer.Expiry > DateTimeOffset.UtcNow)
                && PriceWatchShopLinks.Validate(url) is { } current)
                (open ?? PriceWatchShopLinks.Open)(current);
        }, "pricewatch");
    }

    private static bool WindowsAllowsNotifications() =>
        SHQueryUserNotificationState(out int state) == 0 && state == 5;

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);
}
