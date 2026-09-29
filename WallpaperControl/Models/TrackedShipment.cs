using System.Text.Json;
using System.Text.Json.Serialization;

namespace WallpaperControl;

/// <summary>Provider-neutral snapshot. Open status strings deliberately retain future provider values.</summary>
internal sealed record TrackedShipment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Provider { get; init; } = string.Empty;
    public string? ProviderTrackerId { get; init; }
    public string TrackingNumber { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string? CarrierCode { get; init; }
    public string StatusCategory { get; init; } = "unknown";
    public string StatusMilestone { get; init; } = "unknown";
    public DateTimeOffset? EstimatedDelivery { get; init; }
    public TrackingEvent? LastRelevantEvent { get; init; }
    public List<TrackingEvent> Events { get; init; } = new();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSuccessfulRefresh { get; init; }
    public DateTimeOffset? DeliveredAt { get; init; }
    public TrackingNotificationState NotificationState { get; init; } = new();
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalData { get; init; }
    public override string ToString() => $"TrackedShipment {Id}";
}

internal sealed record TrackingEvent
{
    // A stable provider event ID or deterministic fingerprint, never a list index.
    public string EventId { get; init; } = string.Empty;
    public DateTimeOffset OccurredAt { get; init; }
    public string StatusCategory { get; init; } = "unknown";
    public string StatusMilestone { get; init; } = "unknown";
    public string? Description { get; init; }
    public string? Location { get; init; }
    public string? CarrierCode { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalData { get; init; }
    public override string ToString() => "TrackingEvent";
}

internal sealed record TrackingNotificationState
{
    // Retain all delivered identities, so out-of-order events cannot re-notify after restart.
    public HashSet<string> NotifiedEventIds { get; init; } = new(StringComparer.Ordinal);
    public string? LastNotifiedStatusKey { get; init; }
    public DateTimeOffset? LastNotifiedAt { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalData { get; init; }
}
