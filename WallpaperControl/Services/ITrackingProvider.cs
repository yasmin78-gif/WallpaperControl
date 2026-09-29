namespace WallpaperControl;

/// <summary>Adapters obtain credentials separately. No transport or vendor types cross this boundary.
/// Fetches return remote snapshots: callers merge by Provider/ProviderTrackerId, preserving the stored
/// Id, DisplayName and NotificationState. Failures use TrackingProviderException; cancellation remains cancellation.</summary>
internal interface ITrackingProvider
{
    string ProviderId { get; }
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
    Task<TrackedShipment> CreateTrackerAsync(string trackingNumber, string? carrierCode = null,
        CancellationToken cancellationToken = default);
    Task<TrackedShipment> GetTrackingAsync(string providerTrackerId, CancellationToken cancellationToken = default);
}
