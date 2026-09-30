namespace WallpaperControl;

internal static class AmazonLogistics
{
    internal const string Carrier = "amazon-logistics";
    // Intentionally only DE + 10 ASCII digits or TBA + 12 ASCII digits.
    internal static bool Recognizes(string? number)
    {
        if (number == null) return false;
        number = number.Trim();
        int prefix = number.Length == 12 && number.StartsWith("DE", StringComparison.OrdinalIgnoreCase) ? 2
            : number.Length == 15 && number.StartsWith("TBA", StringComparison.OrdinalIgnoreCase) ? 3 : 0;
        return prefix != 0 && number.AsSpan(prefix).IndexOfAnyExceptInRange('0', '9') < 0;
    }
    internal static bool IsLocal(TrackedShipment shipment) => shipment.TrackingMode == "manual" && shipment.Provider == "local" && shipment.CarrierCode == Carrier;
}
