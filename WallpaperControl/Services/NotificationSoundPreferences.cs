using System.Text.Json;
namespace WallpaperControl;
internal sealed record NotificationSoundPreferences
{
    public NotificationSoundKind Twitch { get; init; } = NotificationSoundKind.Chime;
    public NotificationSoundKind Packages { get; init; } = NotificationSoundKind.Chime;
    public NotificationSoundKind Feeds { get; init; } = NotificationSoundKind.Chime;
    public NotificationSoundKind Notes { get; init; } = NotificationSoundKind.Chime;
    internal static NotificationSoundPreferences FromLegacy(NotificationSoundKind sound) => new() { Twitch = sound, Packages = sound, Feeds = sound, Notes = sound };
    internal static NotificationSoundPreferences Parse(string? json, NotificationSoundKind legacy)
    {
        try
        {
            var value = json == null ? FromLegacy(legacy) : JsonSerializer.Deserialize<NotificationSoundPreferences>(json) ?? FromLegacy(legacy);
            return value with { Twitch = NotificationSounds.Normalize((int)value.Twitch), Packages = NotificationSounds.Normalize((int)value.Packages), Feeds = NotificationSounds.Normalize((int)value.Feeds), Notes = NotificationSounds.Normalize((int)value.Notes) };
        }
        catch (JsonException) { return FromLegacy(legacy); }
    }
    internal NotificationSoundKind ForGroup(string group) => group switch { "twitch" => Twitch, "package" => Packages, "feed" => Feeds, "notes" => Notes, _ => NotificationSoundKind.Off };
}
