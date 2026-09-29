using System.Globalization;

namespace WallpaperControl;

internal static class PackagePresentation
{
    internal static string Status(string? milestone, string language) => Localization.Get(milestone switch
    {
        "info_received" => "PackageInfoReceived", "in_transit" => "PackageInTransit",
        "out_for_delivery" => "PackageOutForDelivery", "available_for_pickup" => "PackagePickup",
        "delivered" => "PackageDelivered", "exception" or "failed_attempt" => "PackageException",
        _ => "PackageUnknown"
    }, language);
    internal static string Name(TrackedShipment s, string language) => string.IsNullOrWhiteSpace(s.DisplayName) ? Localization.Get("PackageUnnamed", language) : s.DisplayName;
    internal static string CarrierNumber(TrackedShipment s, bool shorten) =>
        (string.IsNullOrWhiteSpace(s.CarrierCode) ? "" : s.CarrierCode + " · ") +
        (shorten && s.TrackingNumber.Length > 16 ? s.TrackingNumber[..16] + "…" : s.TrackingNumber);
    internal static string Date(DateTimeOffset? value, string language) => value?.ToLocalTime().ToString("g", CultureInfo.GetCultureInfo(language)) ?? "";
    internal static string Format(string key, string value, string language) => string.Format(CultureInfo.GetCultureInfo(language), Localization.Get(key, language), value);
    internal static string Error(PackageResult result, string language) => Localization.Get(result.Outcome switch
    {
        PackageOperation.Busy => "PackageBusy", PackageOperation.Duplicate => "PackageDuplicate",
        PackageOperation.InvalidNumber => "PackageInvalidNumber", PackageOperation.StorageError => "PackageStorageError",
        PackageOperation.Missing => "PackageMissing", _ => result.Failure switch
        {
            TrackingProviderFailure.MissingCredential or TrackingProviderFailure.InvalidCredential or TrackingProviderFailure.Unauthorized => "PackageCredentialError",
            TrackingProviderFailure.Forbidden => "PackageForbidden", TrackingProviderFailure.QuotaExhausted => "PackageQuota",
            TrackingProviderFailure.RateLimit => "PackageRateLimit", TrackingProviderFailure.InvalidTrackingNumber => "PackageInvalidNumber",
            TrackingProviderFailure.TrackerNotFound => "PackageMissing", _ => "PackageRefreshError"
        }
    }, language);
}
