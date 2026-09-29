namespace WallpaperControl;

/// <summary>Future adapters obtain credentials separately. No transport or vendor types cross this boundary.</summary>
internal interface ITrackingProvider
{
    string ProviderId { get; }
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
    Task<TrackedShipment> CreateTrackerAsync(string trackingNumber, string? carrierCode = null,
        CancellationToken cancellationToken = default);
    Task<TrackedShipment> GetTrackingAsync(string providerTrackerId, CancellationToken cancellationToken = default);
}
