using System.Diagnostics;
using System.Text.Json.Serialization;

namespace WallpaperControl;

internal enum TwitchError { None, Offline, Unavailable, Cancelled, Expired, Reconnect, Unauthorized, RateLimit, InvalidResponse, Storage }
internal sealed class TwitchException(TwitchError error, DateTimeOffset? retryAfter = null) : Exception("Twitch request failed.")
{
    internal TwitchError Error { get; } = error;
    internal DateTimeOffset? RetryAfter { get; } = retryAfter;
}
[DebuggerDisplay("Twitch tokens (redacted)")]
internal sealed class TwitchTokens
{
    [DebuggerBrowsable(DebuggerBrowsableState.Never)] public string AccessToken { get; init; } = "";
    [DebuggerBrowsable(DebuggerBrowsableState.Never)] public string RefreshToken { get; init; } = "";
    public override string ToString() => "Twitch tokens (redacted)";
}
internal sealed record TwitchDeviceCode(string DeviceCode, string UserCode, Uri VerificationUri, int ExpiresIn, int Interval)
{
    public override string ToString() => "Twitch device authorization (redacted)";
}
internal sealed record TwitchIdentity(string UserId, string Login, string DisplayName, int ExpiresIn);
internal sealed record TwitchStream(
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("user_login")] string Login,
    [property: JsonPropertyName("user_name")] string DisplayName,
    [property: JsonPropertyName("game_name")] string Game,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("viewer_count")] int Viewers,
    [property: JsonPropertyName("started_at")] DateTimeOffset StartedAt,
    [property: JsonPropertyName("thumbnail_url")] string ThumbnailUrl);

internal interface ITwitchApi : IDisposable
{
    Task<TwitchDeviceCode> StartDeviceAsync(CancellationToken token);
    Task<TwitchTokens?> PollDeviceAsync(string code, CancellationToken token);
    Task<TwitchTokens> RefreshTokenAsync(string refreshToken, CancellationToken token);
    Task<TwitchIdentity> ValidateAsync(string accessToken, CancellationToken token);
    Task<string> GetDisplayNameAsync(string accessToken, string userId, CancellationToken token);
    Task<IReadOnlyList<TwitchStream>> GetStreamsAsync(string accessToken, string userId, CancellationToken token);
}
internal interface ITwitchTokenStore
{
    TwitchTokens? Load();
    void Save(TwitchTokens tokens);
    void Clear();
}
