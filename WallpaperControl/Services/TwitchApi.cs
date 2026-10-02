using System.Net;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Text.Json;

namespace WallpaperControl;

/// <summary>Official Twitch public-client DCF and Helix endpoints. Response bodies never enter logs.</summary>
internal sealed partial class TwitchApi : ITwitchApi
{
    private readonly HttpClient client;
    internal TwitchApi(HttpMessageHandler? handler = null)
    { client = handler == null ? new() : new(handler); client.Timeout = TimeSpan.FromSeconds(30); }
    private static string Text(JsonElement obj, string key) => obj.GetProperty(key).GetString() ?? throw new TwitchException(TwitchError.InvalidResponse);
    private static TwitchTokens Tokens(JsonElement obj)
    {
        if (!obj.GetProperty("scope").EnumerateArray().Any(s => s.GetString() == TwitchConfiguration.Scope)) throw new TwitchException(TwitchError.Unauthorized);
        return new() { AccessToken = Text(obj, "access_token"), RefreshToken = Text(obj, "refresh_token") };
    }
    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, CancellationToken token, bool polling = false, bool refreshing = false)
    {
        using (request)
        {
            try
            {
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    DateTimeOffset? until = response.Headers.RetryAfter?.Date;
                    if (response.Headers.RetryAfter?.Delta is TimeSpan delta) until = DateTimeOffset.UtcNow + delta;
                    if (until == null && response.Headers.TryGetValues("Ratelimit-Reset", out var values) && long.TryParse(values.FirstOrDefault(), out var seconds))
                        until = DateTimeOffset.FromUnixTimeSeconds(Math.Clamp(seconds, 0, 253402300799));
                    throw new TwitchException(TwitchError.RateLimit, until ?? DateTimeOffset.UtcNow.AddMinutes(5));
                }
                if (response.StatusCode == HttpStatusCode.Unauthorized) throw new TwitchException(TwitchError.Reconnect);
                if ((int)response.StatusCode >= 500) throw new TwitchException(TwitchError.Unavailable);
                // Bounded payload; neither OAuth nor Helix needs unbounded response buffering.
                using var stream = await response.Content.ReadAsStreamAsync(token);
                using var buffer = new MemoryStream();
                byte[] bytes = new byte[8192]; int read;
                while ((read = await stream.ReadAsync(bytes, token)) > 0)
                { if (buffer.Length + read > 4 * 1024 * 1024) throw new TwitchException(TwitchError.InvalidResponse); buffer.Write(bytes, 0, read); }
                var json = JsonDocument.Parse(buffer.ToArray());
                if (!response.IsSuccessStatusCode)
                {
                    string message = json.RootElement.TryGetProperty("message", out var m) ? m.GetString() ?? ""
                        : json.RootElement.TryGetProperty("error", out var error) ? error.GetString() ?? "" : "";
                    json.Dispose();
                    if (polling && message == "authorization_pending") return JsonDocument.Parse("{\"pending\":true}");
                    if (polling && message == "slow_down") return JsonDocument.Parse("{\"slow_down\":true}");
                    if (polling && message is "expired_token" or "invalid device code") throw new TwitchException(TwitchError.Expired);
                    if (polling && message == "access_denied") throw new TwitchException(TwitchError.Cancelled);
                    throw new TwitchException(refreshing ? TwitchError.Reconnect : TwitchError.Unauthorized);
                }
                return json;
            }
            catch (HttpRequestException) { throw new TwitchException(NetworkInterface.GetIsNetworkAvailable() ? TwitchError.Unavailable : TwitchError.Offline); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TwitchException(TwitchError.Unavailable); }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentOutOfRangeException)
            { throw new TwitchException(TwitchError.InvalidResponse); }
        }
    }
    private static HttpRequestMessage Post(string path, params (string Key, string Value)[] values) => new(HttpMethod.Post, "https://id.twitch.tv/oauth2/" + path)
    { Content = new FormUrlEncodedContent(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value))) };
    private static HttpRequestMessage Helix(string path, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.twitch.tv/helix/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("Client-Id", TwitchConfiguration.ClientId); return request;
    }
    private static T Parse<T>(Func<T> parse)
    {
        try { return parse(); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException)
        { throw new TwitchException(TwitchError.InvalidResponse); }
    }
    public async Task<TwitchDeviceCode> StartDeviceAsync(CancellationToken token)
    {
        using var json = await SendAsync(Post("device", ("client_id", TwitchConfiguration.ClientId), ("scopes", TwitchConfiguration.Scope)), token);
        return Parse(() =>
        {
            var root = json.RootElement; var uri = new Uri(Text(root, "verification_uri"));
            if (uri.Scheme != "https" || uri.Host != "www.twitch.tv" || uri.AbsolutePath != "/activate") throw new TwitchException(TwitchError.InvalidResponse);
            int expiry = root.GetProperty("expires_in").GetInt32(), interval = root.GetProperty("interval").GetInt32();
            if (expiry <= 0 || interval <= 0) throw new TwitchException(TwitchError.InvalidResponse);
            return new TwitchDeviceCode(Text(root, "device_code"), Text(root, "user_code"), uri, expiry, interval);
        });
    }
    // A special typed exception carries only the slow-down instruction, never response text.
    internal sealed class SlowDownException : Exception;
    public async Task<TwitchTokens?> PollDeviceAsync(string code, CancellationToken token)
    {
        using var json = await SendAsync(Post("token", ("client_id", TwitchConfiguration.ClientId), ("scopes", TwitchConfiguration.Scope),
            ("device_code", code), ("grant_type", "urn:ietf:params:oauth:grant-type:device_code")), token, polling: true);
        if (json.RootElement.TryGetProperty("pending", out _)) return null;
        if (json.RootElement.TryGetProperty("slow_down", out _)) throw new SlowDownException();
        return Parse(() => Tokens(json.RootElement));
    }
    public async Task<TwitchTokens> RefreshTokenAsync(string refreshToken, CancellationToken token)
    {
        using var json = await SendAsync(Post("token", ("client_id", TwitchConfiguration.ClientId), ("refresh_token", refreshToken), ("grant_type", "refresh_token")), token, refreshing: true);
        return Parse(() => Tokens(json.RootElement));
    }
    public async Task<TwitchIdentity> ValidateAsync(string accessToken, CancellationToken token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://id.twitch.tv/oauth2/validate");
        request.Headers.Authorization = new("OAuth", accessToken);
        using var json = await SendAsync(request, token);
        return Parse(() =>
        {
            var root = json.RootElement;
            if (Text(root, "client_id") != TwitchConfiguration.ClientId || !root.GetProperty("scopes").EnumerateArray().Any(s => s.GetString() == TwitchConfiguration.Scope)) throw new TwitchException(TwitchError.Reconnect);
            string id = Text(root, "user_id"), login = Text(root, "login");
            if (id.Length == 0 || !id.All(char.IsAsciiDigit) || TwitchLinks.Channel(login) == null) throw new TwitchException(TwitchError.InvalidResponse);
            return new TwitchIdentity(id, login, login, root.GetProperty("expires_in").GetInt32());
        });
    }
    public async Task<string> GetDisplayNameAsync(string accessToken, string userId, CancellationToken token)
    {
        using var json = await SendAsync(Helix("users?id=" + Uri.EscapeDataString(userId), accessToken), token);
        return Parse(() => Text(json.RootElement.GetProperty("data").EnumerateArray().Single(), "display_name"));
    }
    public async Task<IReadOnlyList<TwitchStream>> GetStreamsAsync(string accessToken, string userId, CancellationToken token)
    {
        var result = new List<TwitchStream>(); var cursors = new HashSet<string>(StringComparer.Ordinal); string cursor = "";
        do
        {
            using var json = await SendAsync(Helix("streams/followed?first=100&user_id=" + Uri.EscapeDataString(userId) + (cursor.Length == 0 ? "" : "&after=" + Uri.EscapeDataString(cursor)), accessToken), token);
            Parse(() =>
            {
                foreach (var item in json.RootElement.GetProperty("data").EnumerateArray())
                {
                    var s = item.Deserialize<TwitchStream>() ?? throw new TwitchException(TwitchError.InvalidResponse);
                    if (string.IsNullOrEmpty(s.UserId) || TwitchLinks.Channel(s.Login) == null || s.DisplayName == null || s.Title == null || s.Game == null || s.Viewers < 0 || s.StartedAt == default) throw new TwitchException(TwitchError.InvalidResponse);
                    if (!result.Any(old => old.UserId == s.UserId)) result.Add(s);
                }
                var pagination = json.RootElement.GetProperty("pagination");
                cursor = pagination.TryGetProperty("cursor", out var next) ? next.GetString() ?? "" : "";
                if (cursor.Length > 0 && (!cursors.Add(cursor) || cursors.Count > 100)) throw new TwitchException(TwitchError.InvalidResponse);
                return true;
            });
        } while (cursor.Length > 0);
        return result;
    }
    public void Dispose() => client.Dispose();
}
