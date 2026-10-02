namespace WallpaperControl;

/// <summary>Optional profile presentation adapter, separate from the existing OAuth/stream contract.</summary>
internal interface ITwitchProfileApi
{
    Task<IReadOnlyDictionary<string, Uri>> GetProfileImagesAsync(string accessToken, IReadOnlyList<string> userIds, CancellationToken token);
}

internal sealed partial class TwitchApi : ITwitchProfileApi
{
    internal const int ProfileBatchSize = 100;
    public async Task<IReadOnlyDictionary<string, Uri>> GetProfileImagesAsync(string accessToken, IReadOnlyList<string> userIds, CancellationToken token)
    {
        var result = new Dictionary<string, Uri>(StringComparer.Ordinal);
        var ids = userIds.Where(id => id is { Length: > 0 and <= 20 } && id.All(char.IsAsciiDigit)).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var batch in ids.Chunk(ProfileBatchSize))
        {
            token.ThrowIfCancellationRequested();
            using var json = await SendAsync(Helix("users?" + string.Join("&", batch.Select(id => "id=" + Uri.EscapeDataString(id))), accessToken), token);
            Parse(() =>
            {
                foreach (var user in json.RootElement.GetProperty("data").EnumerateArray())
                {
                    string id = Text(user, "id");
                    if (!batch.Contains(id, StringComparer.Ordinal) || !user.TryGetProperty("profile_image_url", out var image) || image.ValueKind != System.Text.Json.JsonValueKind.String) continue;
                    if (Uri.TryCreate(image.GetString(), UriKind.Absolute, out var uri) && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo)) result[id] = uri;
                }
                return true;
            });
        }
        return result;
    }
}

internal sealed partial class TwitchService
{
    /// <summary>Read-only presentation lookup; never alters OAuth, stream data, errors or refresh scheduling.</summary>
    internal async Task<IReadOnlyDictionary<string, Uri>> GetProfileImagesAsync(IReadOnlyList<string> ids, CancellationToken token)
    {
        if (disposed || !Connected || credentials == null || api is not ITwitchProfileApi profiles) return new Dictionary<string, Uri>();
        int current = generation;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        var result = await profiles.GetProfileImagesAsync(credentials.AccessToken, ids, linked.Token);
        linked.Token.ThrowIfCancellationRequested();
        if (disposed || current != generation) throw new OperationCanceledException(linked.Token);
        return result;
    }
}
