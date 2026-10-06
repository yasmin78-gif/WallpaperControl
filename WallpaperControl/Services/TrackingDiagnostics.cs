using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace WallpaperControl;

internal static class TrackingDiagnostics
{
    // Stable correlation without logging tracking numbers, names or provider IDs.
    internal static string Summary(TrackedShipment shipment)
    {
        string correlation = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(shipment.TrackingNumber.ToUpperInvariant())))[..12];
        DateTimeOffset? newest = shipment.Events.Where(e => e.OccurredAt.HasValue)
            .Select(e => e.OccurredAt).Max();
        return $"shipmentKey={correlation}; category={Safe(shipment.StatusCategory)}; milestone={Safe(shipment.StatusMilestone)}; latestEventUtc={newest?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? "unknown"}; events={shipment.Events.Count}";
    }

    private static string Safe(string? value) => string.IsNullOrEmpty(value) ? "unknown"
        : new string(value.Take(64).Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '_').ToArray());
}
