using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WallpaperControl;

internal sealed record PriceWatchGame(Guid Id, string Title, string? Type);
internal sealed record PriceWatchGameInfo(Guid Id, string Title, DateOnly? ReleaseDate, string[] Developers,
    string[] Publishers, Uri? Boxart);
internal sealed record PriceWatchShop(int Id, string Name);
internal sealed record PriceWatchMoney(decimal Amount, int AmountInt, string Currency);
internal sealed record PriceWatchOfferTag(int Id, string Name);
internal sealed record PriceWatchOffer(PriceWatchShop Shop, PriceWatchMoney Price, PriceWatchMoney Regular,
    decimal Cut, Uri Url, DateTimeOffset Timestamp, DateTimeOffset? Expiry,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PriceWatchOfferTag[]? Platforms = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PriceWatchOfferTag[]? Drm = null);
internal sealed record PriceWatchPrices(Guid Id, PriceWatchOffer[] Deals);
internal sealed record PriceWatchChange(PriceWatchMoney Price, PriceWatchMoney Regular, decimal Cut);
internal sealed record PriceWatchHistory(PriceWatchShop Shop, DateTimeOffset Timestamp, PriceWatchChange? Deal);
internal sealed record PriceWatchReply<T>(DateTimeOffset FetchedAt, T Data);
internal enum PriceWatchFailure { Credentials, Busy, GameNotFound, InvalidRequest, Timeout, Connection, InvalidResponse }
internal sealed class PriceWatchException(PriceWatchFailure failure, TimeSpan? retryAfter = null) : Exception(failure.ToString())
{
    internal PriceWatchFailure Failure { get; } = failure;
    internal TimeSpan? RetryAfter { get; } = retryAfter;
}

internal interface IPriceWatchSource
{
    Task<PriceWatchReply<PriceWatchGameInfo>> InfoAsync(Guid id, CancellationToken token);
    Task<PriceWatchReply<PriceWatchGame[]>> SearchAsync(string title, CancellationToken token);
    Task<PriceWatchReply<PriceWatchPrices[]>> PricesAsync(Guid id, CancellationToken token);
    Task<PriceWatchReply<PriceWatchPrices[]>> PricesAsync(Guid id, IReadOnlyList<int> shops, CancellationToken token) =>
        PricesAsync(id, token);
}

internal sealed class PriceWatchClient(HttpClient http, Func<TrackingCredential?> credentials,
    TimeSpan? timeout = null) : IPriceWatchSource
{
    private static readonly Uri Origin = new("https://api.nightmahr.de/");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private long nextRequestUtcTicks;
    private sealed record Envelope<T>(string? Source, DateTimeOffset FetchedAt, T? Data, bool Fixture);
    internal static HttpClient CreateHttpClient() => new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        Timeout = Timeout.InfiniteTimeSpan,
        MaxResponseContentBufferSize = 512 * 1024
    };

    public async Task<PriceWatchReply<PriceWatchGame[]>> SearchAsync(string title, CancellationToken token)
    {
        var reply = await PostAsync<PriceWatchGame[]>("search", new { title }, token);
        if (reply.Data.Length > 15 || reply.Data.Any(game =>
            game is null || game.Id == Guid.Empty || string.IsNullOrWhiteSpace(game.Title)
                || game.Type is not (null or "game" or "package" or "dlc")))
            throw new PriceWatchException(PriceWatchFailure.InvalidResponse);
        return reply;
    }

    internal async Task<PriceWatchReply<Dictionary<string, Guid?>>> LookupAsync(Uri url, CancellationToken token)
    {
        var reply = await PostAsync<Dictionary<string, Guid?>>("lookup", new { url = url.AbsoluteUri }, token);
        if (reply.Data.Count != 1 || reply.Data.Values.Any(id => id == Guid.Empty))
            throw new PriceWatchException(PriceWatchFailure.InvalidResponse);
        return reply;
    }

    public Task<PriceWatchReply<PriceWatchPrices[]>> PricesAsync(Guid id, CancellationToken token) =>
        PricesAsync(id, PriceWatchShops.Legacy, token);

    public async Task<PriceWatchReply<PriceWatchPrices[]>> PricesAsync(Guid id, IReadOnlyList<int> shops, CancellationToken token)
    {
        int[] selected = PriceWatchShops.Validate(shops);
        var reply = await PostAsync<PriceWatchPrices[]>("prices", new { id, shops = selected }, token);
        if (reply.Data.Length > 1 || reply.Data.Any(item => item is null || item.Id != id || item.Deals is null
            || item.Deals.Any(deal => deal is null || !ValidShop(deal.Shop) || !selected.Contains(deal.Shop.Id) || !ValidMoney(deal.Price)
                || !ValidMoney(deal.Regular) || deal.Cut is < 0 or > 100 || deal.Timestamp == default
                || deal.Url is not { IsAbsoluteUri: true, Scheme: "https", UserInfo.Length: 0 })))
            throw new PriceWatchException(PriceWatchFailure.InvalidResponse);
        return reply;
    }

    public async Task<PriceWatchReply<PriceWatchGameInfo>> InfoAsync(Guid id, CancellationToken token)
    {
        if (id == Guid.Empty) throw new PriceWatchException(PriceWatchFailure.InvalidRequest);
        var reply = await PostAsync<PriceWatchGameInfo>("info", new { id }, token);
        var info = reply.Data;
        if (info.Id != id || string.IsNullOrWhiteSpace(info.Title)
            || info.Developers == null || info.Developers.Any(string.IsNullOrWhiteSpace)
            || info.Publishers == null || info.Publishers.Any(string.IsNullOrWhiteSpace)
            || info.Boxart is { } cover && (!cover.IsAbsoluteUri || cover.Scheme != "https"
                || PriceWatchShopLinks.Validate(cover) == null))
            throw new PriceWatchException(PriceWatchFailure.InvalidResponse);
        return reply;
    }

    internal Task<PriceWatchReply<PriceWatchHistory[]>> HistoryAsync(Guid id, int days, CancellationToken token) =>
        HistoryAsync(id, days, PriceWatchShops.Legacy, token);

    internal async Task<PriceWatchReply<PriceWatchHistory[]>> HistoryAsync(Guid id, int days, IReadOnlyList<int> shops,
        CancellationToken token)
    {
        int[] selected = PriceWatchShops.Validate(shops);
        var reply = await PostAsync<PriceWatchHistory[]>("history", new { id, shops = selected, days }, token);
        if (reply.Data.Any(point => point is null || !ValidShop(point.Shop) || !selected.Contains(point.Shop.Id) || point.Timestamp == default
            || point.Deal is { } deal && (!ValidMoney(deal.Price) || !ValidMoney(deal.Regular) || deal.Cut is < 0 or > 100)))
            throw new PriceWatchException(PriceWatchFailure.InvalidResponse);
        return reply;
    }

    private static bool ValidShop(PriceWatchShop? shop) => shop != null && PriceWatchShops.IsSupported(shop.Id)
        && !string.IsNullOrWhiteSpace(shop.Name);
    private static bool ValidMoney(PriceWatchMoney? value) => value is { Currency: "EUR", AmountInt: >= 0 }
        && value.Amount == value.AmountInt / 100m;

    private async Task<PriceWatchReply<T>> PostAsync<T>(string path, object payload, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        long remaining = Interlocked.Read(ref nextRequestUtcTicks) - DateTimeOffset.UtcNow.UtcTicks;
        if (remaining > 0)
            throw new PriceWatchException(PriceWatchFailure.Busy, TimeSpan.FromTicks(remaining));
        var credential = credentials() ?? throw new PriceWatchException(PriceWatchFailure.Credentials);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Origin, path));
        request.Content = JsonContent.Create(payload, options: Json);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(credential.Reveal())));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(12));
        try
        {
            // ResponseContentRead applies the configured buffer limit before parsing.
            using var response = await http.SendAsync(request, deadline.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var retry = response.Headers.RetryAfter;
                TimeSpan? wait = retry?.Delta;
                if (retry?.Date is { } date)
                    wait = date - DateTimeOffset.UtcNow;
                if (wait < TimeSpan.Zero)
                    wait = TimeSpan.Zero;
                if (wait is { } delay && delay > TimeSpan.Zero)
                {
                    long now = DateTimeOffset.UtcNow.UtcTicks;
                    Interlocked.Exchange(ref nextRequestUtcTicks, now + Math.Min(delay.Ticks, DateTimeOffset.MaxValue.UtcTicks - now));
                }
                throw new PriceWatchException(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => PriceWatchFailure.Credentials,
                    HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable => PriceWatchFailure.Busy,
                    HttpStatusCode.NotFound when path == "info" => PriceWatchFailure.GameNotFound,
                    HttpStatusCode.BadRequest or HttpStatusCode.UnsupportedMediaType => PriceWatchFailure.InvalidRequest,
                    _ => PriceWatchFailure.Connection
                }, wait);
            }
            var envelope = await response.Content.ReadFromJsonAsync<Envelope<T>>(Json, deadline.Token).ConfigureAwait(false);
            if (envelope is not { Source: "IsThereAnyDeal", Fixture: false, Data: not null } || envelope.FetchedAt == default)
                throw new PriceWatchException(PriceWatchFailure.InvalidResponse);
            return new(envelope.FetchedAt, envelope.Data);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new PriceWatchException(PriceWatchFailure.Timeout);
        }
        catch (Exception error) when (error is HttpRequestException or IOException)
        {
            throw new PriceWatchException(PriceWatchFailure.Connection);
        }
        catch (Exception error) when (error is JsonException or NotSupportedException)
        {
            throw new PriceWatchException(PriceWatchFailure.InvalidResponse);
        }
    }
}
