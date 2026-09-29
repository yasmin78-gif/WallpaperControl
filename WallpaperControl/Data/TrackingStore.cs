using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WallpaperControl;

/// <summary>Single-owner local store following NotesStore's atomic replacement and quarantine pattern.
/// No runtime code instantiates this store yet. Callers serialize writes after awaited provider operations.</summary>
internal sealed class TrackingStore
{
    private const long SizeLimit = 16 * 1024 * 1024;
    private sealed class Document
    {
        [JsonRequired] public int Version { get; set; } = 1;
        [JsonRequired] public List<TrackedShipment> Shipments { get; set; } = new();
        [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalData { get; set; }
    }
    private sealed class TimestampConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String) throw new JsonException("Invalid tracking timestamp.");
            string? text = reader.GetString();
            bool offset = text != null && (text.EndsWith('Z') || (text.Length >= 6
                && text[^3] == ':' && (text[^6] == '+' || text[^6] == '-')));
            if (!offset || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
                throw new JsonException("Tracking timestamp requires an explicit offset.");
            return value;
        }
        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
    }
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new TimestampConverter() } };
    internal static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WallpaperControl", "tracking.json");
    private readonly string path;
    private Document document = new();
    internal bool CanWrite { get; private set; } = true;
    internal bool LoadIssue { get; private set; }
    internal bool RecoveredFromBackup { get; private set; }
    internal IReadOnlyList<TrackedShipment> Shipments => Clone(document).Shipments.AsReadOnly();
    internal TrackingStore(string? path = null) { this.path = path ?? DefaultPath; Load(); }
    private static Document Clone(Document value) => JsonSerializer.Deserialize<Document>(JsonSerializer.Serialize(value, Options), Options)!;

    private static bool Valid(TrackedShipment s) => s != null && s.Id != Guid.Empty
        && !string.IsNullOrWhiteSpace(s.Provider) && !string.IsNullOrWhiteSpace(s.TrackingNumber)
        && s.CreatedAt != default && s.StatusCategory != null && s.StatusMilestone != null
        && s.Events != null && s.Events.Count <= 10000 && s.Events.All(ValidEvent)
        && (s.LastRelevantEvent == null || ValidEvent(s.LastRelevantEvent))
        && s.NotificationState != null && s.NotificationState.NotifiedEventIds != null
        && s.NotificationState.NotifiedEventIds.All(id => !string.IsNullOrWhiteSpace(id));
    private static bool ValidEvent(TrackingEvent e) => e != null && !string.IsNullOrWhiteSpace(e.EventId)
        && e.OccurredAt != default && e.StatusCategory != null && e.StatusMilestone != null;
    private static Document Read(string source)
    {
        using FileStream stream = new(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > SizeLimit) throw new JsonException("Tracking document too large.");
        using JsonDocument json = JsonDocument.Parse(stream);
        if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("Version", out var version)
            || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int number)) throw new JsonException("Missing tracking schema.");
        if (number != 1) throw new NotSupportedException("Unsupported tracking schema.");
        Document value = json.RootElement.Deserialize<Document>(Options) ?? throw new JsonException("Missing tracking document.");
        if (value.Shipments == null || value.Shipments.Count > 10000 || !value.Shipments.All(Valid)
            || value.Shipments.Select(s => s.Id).Distinct().Count() != value.Shipments.Count)
            throw new JsonException("Invalid tracking data.");
        return value;
    }
    private void Load()
    {
        foreach (string candidate in new[] { path, path + ".bak" })
        {
            try
            {
                document = Read(candidate);
                RecoveredFromBackup = candidate != path;
                if (RecoveredFromBackup) AppLogger.Info("Tracking data recovered from backup.");
                return;
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            catch (NotSupportedException) { LoadIssue = true; CanWrite = false; Log("load: future schema preserved"); return; }
            catch (JsonException)
            {
                LoadIssue = true; Log("load: invalid data quarantined");
                try { File.Move(candidate, candidate + ".corrupt-" + Guid.NewGuid().ToString("N")); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { CanWrite = false; Log("load: quarantine failed"); return; }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { LoadIssue = true; CanWrite = false; Log("load: storage unavailable"); return; }
        }
        // Never silently overwrite unrecoverable history with an empty document.
        if (LoadIssue) CanWrite = false;
    }
    internal bool Save(TrackedShipment shipment)
    {
        if (!Valid(shipment)) return false;
        return Commit(document.Shipments.Where(s => s.Id != shipment.Id).Append(shipment).ToList());
    }
    internal bool Delete(Guid id) => document.Shipments.Any(s => s.Id == id)
        && Commit(document.Shipments.Where(s => s.Id != id).ToList());
    private bool Commit(List<TrackedShipment> next)
    {
        if (!CanWrite || next.Count > 10000) return false;
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var snapshot = Clone(new Document { Shipments = next, AdditionalData = document.AdditionalData });
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, snapshot, Options);
                if (stream.Length > SizeLimit) throw new IOException("Tracking document too large.");
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
            document = snapshot;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        { Log("save failed"); return false; }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log("temporary cleanup failed"); }
        }
    }
    private static void Log(string operation) => AppLogger.Warning("Tracking store: " + operation,
        new InvalidOperationException("Tracking storage operation failed; payload details omitted."));
}
