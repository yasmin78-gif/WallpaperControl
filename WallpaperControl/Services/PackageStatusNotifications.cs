namespace WallpaperControl;

/// <summary>Uses only durably saved provider refreshes, never initial loading or local user edits.</summary>
internal sealed class PackageStatusNotifications : IDisposable
{
    private readonly PackageTrackingService service;
    private readonly Action<TrackedShipment> notify;
    private readonly Func<bool?> fullscreen;
    private bool enabled, suspended;
    internal PackageStatusNotifications(PackageTrackingService service, Action<TrackedShipment> notify, Func<bool?>? fullscreen = null)
    { this.service = service; this.notify = notify; this.fullscreen = fullscreen ?? FullscreenActivityDetector.GetFullscreenState; service.Refreshed += Refreshed; }
    internal void Configure(bool value) => enabled = value;
    internal void SetSuspended(bool value) => suspended = value;
    internal static bool Changed(TrackedShipment before, TrackedShipment after) =>
        !string.Equals(before.StatusMilestone ?? "unknown", after.StatusMilestone ?? "unknown", StringComparison.OrdinalIgnoreCase);
    private void Refreshed(TrackedShipment before, TrackedShipment after)
    {
        if (!enabled || suspended || !Changed(before, after) || fullscreen() != false) return;
        // Do not alert on an older carrier event that would rewind the status timeline.
        if (before.LastRelevantEvent?.OccurredAt is DateTimeOffset oldTime
            && after.LastRelevantEvent?.OccurredAt is DateTimeOffset newTime && newTime < oldTime) return;
        try { notify(after); }
        catch (Exception ex) { AppLogger.Info($"Package notification failed: {ex.GetType().Name}."); }
    }
    public void Dispose() => service.Refreshed -= Refreshed;
}
