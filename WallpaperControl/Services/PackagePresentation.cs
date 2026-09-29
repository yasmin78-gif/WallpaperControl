using System.Globalization;

namespace WallpaperControl;

internal static class PackagePresentation
{
    internal static IEnumerable<TrackingEvent> NewestEventsFirst(IEnumerable<TrackingEvent> events) =>
        events.OrderByDescending(EventSortTime);

    private static DateTime? EventSortTime(TrackingEvent entry)
    {
        if (entry.OccurredAt is DateTimeOffset instant) return instant.UtcDateTime;
        string? raw = entry.RawOccurredAt;
        if (string.IsNullOrWhiteSpace(raw)) return null;
        bool hasOffset = raw.EndsWith('Z') || (raw.Length >= 6 && raw[^3] == ':' && raw[^6] is '+' or '-');
        if (hasOffset && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var offsetTime))
            return offsetTime.UtcDateTime;
        // A courier's timezone-less date is a display-order fallback, not a UTC instant.
        // Mixed known/unknown zones cannot be ordered absolutely; never write this key back to the model.
        return DateTime.TryParseExact(raw, new[] { "yyyy-MM-dd", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF" },
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var localTime) ? localTime : null;
    }

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
    internal static string EventDate(TrackingEvent entry, string language)
    {
        if (entry.OccurredAt != null) return Date(entry.OccurredAt, language);
        var culture = CultureInfo.GetCultureInfo(language);
        // These source values have no timezone: format their clock fields without conversion.
        if (DateTime.TryParseExact(entry.RawOccurredAt, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var day)) return day.ToString("d", culture);
        if (DateTime.TryParseExact(entry.RawOccurredAt, new[] { "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF" },
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) return time.ToString("g", culture);
        return entry.RawOccurredAt ?? Localization.Get("PackageUnknownTime", language);
    }
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
