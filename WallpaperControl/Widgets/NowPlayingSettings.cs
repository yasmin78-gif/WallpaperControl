using System.Text.Json;
namespace WallpaperControl;
internal sealed class NowPlayingSettings
{
    public bool Enabled { get; set; }
    public bool Locked { get; set; }
    public bool HideEmpty { get; set; }
    public bool WheelVolume { get; set; }
    public SystemWidgetStyle Style { get; set; }
    public Point Location { get; set; } = new(700, 560);
    internal NowPlayingSettings Clone() => (NowPlayingSettings)MemberwiseClone();
    internal static NowPlayingSettings Parse(string json)
    {
        try { var value = JsonSerializer.Deserialize<NowPlayingSettings>(json) ?? new(); if (!Enum.IsDefined(value.Style)) value.Style = SystemWidgetStyle.Minimal; return value; }
        catch (JsonException) { return new(); }
    }
}
internal sealed record MediaSessionChoice(string Id, string Name);
internal enum MediaCommand { Previous, Toggle, Next, Seek }
internal sealed record NowPlayingState
{
    internal string? SessionId { get; init; }
    internal string Source { get; init; } = "";
    internal string Title { get; init; } = "";
    internal string Artist { get; init; } = "";
    internal string Album { get; init; } = "";
    internal byte[]? Cover { get; init; }
    internal bool Playing { get; init; }
    internal bool Previous { get; init; }
    internal bool Toggle { get; init; }
    internal bool Next { get; init; }
    internal bool Seek { get; init; }
    internal bool Unavailable { get; init; }
    internal TimeSpan Start { get; init; }
    internal TimeSpan End { get; init; }
    internal TimeSpan MinimumSeek { get; init; }
    internal TimeSpan MaximumSeek { get; init; }
    internal TimeSpan Position { get; init; }
    internal double Rate { get; init; } = 1;
    internal DateTimeOffset Updated { get; init; } = DateTimeOffset.UtcNow;
    internal TimeSpan PositionAt(DateTimeOffset now)
    {
        var seconds = Position.TotalSeconds + (Playing ? Math.Max(0, (now - Updated).TotalSeconds) * Rate : 0);
        return TimeSpan.FromSeconds(Math.Clamp(seconds, Start.TotalSeconds, Math.Max(Start.TotalSeconds, End.TotalSeconds)));
    }
    internal TimeSpan SeekAt(double fraction) => TimeSpan.FromSeconds(Math.Clamp(Start.TotalSeconds + Math.Clamp(fraction, 0, 1) * (End - Start).TotalSeconds, MinimumSeek.TotalSeconds, Math.Max(MinimumSeek.TotalSeconds, MaximumSeek.TotalSeconds)));
}
internal interface INowPlayingService : IDisposable
{
    event Action? Changed;
    NowPlayingState State { get; }
    IReadOnlyList<MediaSessionChoice> Sessions { get; }
    string? Selected { get; }
    void Select(string? id);
    void SetSuspended(bool value);
    Task<bool> CommandAsync(MediaCommand command, TimeSpan position = default);
}
