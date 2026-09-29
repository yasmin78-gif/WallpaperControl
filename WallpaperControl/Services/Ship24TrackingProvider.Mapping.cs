using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WallpaperControl;

internal sealed partial class Ship24TrackingProvider
{
    // Vendor JSON remains entirely inside the adapter. Optional fields are deliberately tolerant.
    private static JsonElement Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;
    private static string? Text(JsonElement element, string name) => String(Property(element, name));
    private static string? String(JsonElement element) => element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    private static string? FirstString(JsonElement element) => element.ValueKind == JsonValueKind.Array
        ? element.EnumerateArray().Select(String).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) : null;

    private static DateTimeOffset? Timestamp(string? value, bool documentedUtc = false)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.Contains('T')) return null;
        bool offset = value.EndsWith('Z') || (value.Length >= 6 && value[^3] == ':' && value[^6] is '+' or '-');
        if (!offset && !documentedUtc) return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            documentedUtc ? DateTimeStyles.AssumeUniversal : DateTimeStyles.None, out var date) && date != default ? date : null;
    }

    private static TrackingEvent MapEvent(JsonElement element)
    {
        string? raw = Text(element, "occurrenceDatetime") ?? Text(element, "datetime");
        DateTimeOffset? occurred = Timestamp(raw);
        if (occurred == null && raw != null && Timestamp(raw + Text(element, "utcOffset")) is { } legacy)
            occurred = legacy;
        if (Property(element, "hasNoTime").ValueKind == JsonValueKind.True) occurred = null;
        var result = new TrackingEvent
        {
            OccurredAt = occurred, RawOccurredAt = raw,
            Description = Text(element, "status"), Location = Text(element, "location"),
            CarrierCode = Text(element, "courierCode"), SourceCode = Text(element, "sourceCode"),
            StatusCategory = Text(element, "statusCategory"), StatusMilestone = Text(element, "statusMilestone")
        };
        // Deterministic fallback survives event reordering; no recipient data or credentials are retained.
        string? id = Text(element, "eventId");
        return result with { EventId = string.IsNullOrWhiteSpace(id)
            ? "derived-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(result)))) : id };
    }

    private TrackedShipment Map(JsonElement root, string expected, bool byId)
    {
        var trackings = Property(Property(root, "data"), "trackings");
        if (trackings.ValueKind != JsonValueKind.Array) throw new TrackingProviderException(TrackingProviderFailure.InvalidResponse);
        var matches = trackings.EnumerateArray().Where(t => string.Equals(Text(Property(t, "tracker"),
            byId ? "trackerId" : "trackingNumber"), expected, byId ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1) throw new TrackingProviderException(TrackingProviderFailure.InvalidResponse);
        var tracking = matches[0];
        var tracker = Property(tracking, "tracker");
        string? trackerId = Text(tracker, "trackerId"), number = Text(tracker, "trackingNumber");
        if (string.IsNullOrWhiteSpace(trackerId) || string.IsNullOrWhiteSpace(number))
            throw new TrackingProviderException(TrackingProviderFailure.InvalidResponse);
        var shipment = Property(tracking, "shipment");
        var eventsJson = Property(tracking, "events");
        var events = new List<TrackingEvent>();
        if (eventsJson.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in eventsJson.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) throw new TrackingProviderException(TrackingProviderFailure.InvalidResponse);
                events.Add(MapEvent(item));
            }
        }
        else if (eventsJson.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            throw new TrackingProviderException(TrackingProviderFailure.InvalidResponse);
        events = events.OrderByDescending(e => e.OccurredAt).ThenBy(e => e.EventId, StringComparer.Ordinal).ToList();
        var delivery = Property(shipment, "delivery");
        var carrierEta = Property(delivery, "courierEstimatedDeliveryDate");
        // Carrier prediction may be a range. Prefer its start, then end, then the general UTC estimate.
        DateTimeOffset? eta = Timestamp(String(carrierEta)) ?? Timestamp(Text(carrierEta, "from"))
            ?? Timestamp(Text(carrierEta, "to")) ?? Timestamp(Text(delivery, "estimatedDeliveryDate"), documentedUtc: true);
        var delivered = Timestamp(Text(Property(Property(tracking, "statistics"), "timestamps"), "deliveredDatetime"))
            ?? events.FirstOrDefault(e => e.StatusMilestone == "delivered" && e.OccurredAt != null)?.OccurredAt;
        return new TrackedShipment
        {
            Provider = ProviderId, ProviderTrackerId = trackerId, TrackingNumber = number,
            CarrierCode = FirstString(Property(tracker, "courierCode")),
            StatusCategory = Text(shipment, "statusCategory"), StatusMilestone = Text(shipment, "statusMilestone"),
            EstimatedDelivery = eta, DeliveredAt = delivered, Events = events,
            LastRelevantEvent = events.FirstOrDefault(e => e.OccurredAt != null),
            // CreatedAt is local record creation when the provider supplies no creation timestamp.
            CreatedAt = Timestamp(Text(tracker, "createdAt"), documentedUtc: true) ?? DateTimeOffset.UtcNow,
            LastSuccessfulRefresh = DateTimeOffset.UtcNow
        };
    }
}
