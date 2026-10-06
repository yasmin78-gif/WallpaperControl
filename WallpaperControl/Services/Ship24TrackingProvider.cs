using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace WallpaperControl;

/// <summary>Explicitly invoked adapter; no polling or application-start integration.
/// Injected clients remain caller-owned and must not follow redirects or log authorization headers.</summary>
internal sealed partial class Ship24TrackingProvider : ITrackingProvider, IDisposable
{
    public string ProviderId => "ship24";
    private readonly TrackingCredentialStore credentials;
    private readonly HttpClient client;
    private readonly bool ownsClient;
    private readonly TimeSpan timeout;
    private const int MaxResponseBytes = 4 * 1024 * 1024;

    internal Ship24TrackingProvider(TrackingCredentialStore credentials, HttpClient? client = null, TimeSpan? timeout = null)
    {
        this.credentials = credentials;
        this.timeout = timeout ?? TimeSpan.FromSeconds(30);
        if (this.timeout <= TimeSpan.Zero || this.timeout.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        ownsClient = client == null;
        this.client = client ?? new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false, CheckCertificateRevocationList = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        }) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    }

    // Documented authenticated, read-only operation. Does not register a shipment or consume a tracker slot.
    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        using var json = await SendAsync(HttpMethod.Get, "/public/v1/couriers", null, "Connection", cancellationToken).ConfigureAwait(false);
        if (Property(Property(json.RootElement, "data"), "couriers").ValueKind != JsonValueKind.Array)
            throw new TrackingProviderException(TrackingProviderFailure.InvalidResponse);
        return true;
    }

    public async Task<TrackedShipment> CreateTrackerAsync(string trackingNumber, string? carrierCode = null,
        CancellationToken cancellationToken = default)
        => await TrackAsync(trackingNumber, carrierCode, "Create", cancellationToken).ConfigureAwait(false);

    private async Task<TrackedShipment> TrackAsync(string trackingNumber, string? carrierCode, string operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(trackingNumber) || trackingNumber.Length is < 5 or > 50
            || !trackingNumber.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '/' or '.'))
            throw new TrackingProviderException(TrackingProviderFailure.InvalidTrackingNumber);
        var body = new Dictionary<string, object> { ["trackingNumber"] = trackingNumber };
        if (!string.IsNullOrWhiteSpace(carrierCode)) body["courierCode"] = new[] { carrierCode };
        using var json = await SendAsync(HttpMethod.Post, "/public/v1/trackers/track", body, operation, cancellationToken).ConfigureAwait(false);
        var mapped = Map(json.RootElement, trackingNumber, false) with { RequestedCarrierCode = carrierCode };
        AppLogger.Info($"Ship24 {operation}: returned; {TrackingDiagnostics.Summary(mapped)}");
        return mapped;
    }

    public async Task<TrackedShipment> GetTrackingAsync(string providerTrackerId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(providerTrackerId) || providerTrackerId.Length > 256)
            throw new TrackingProviderException(TrackingProviderFailure.TrackerNotFound);
        using var json = await SendAsync(HttpMethod.Get, "/public/v1/trackers/" + Uri.EscapeDataString(providerTrackerId) + "/results",
            null, "Get", cancellationToken).ConfigureAwait(false);
        var mapped = Map(json.RootElement, providerTrackerId, true);
        AppLogger.Info($"Ship24 Get: returned; {TrackingDiagnostics.Summary(mapped)}");
        return mapped;
    }

    public async Task<TrackedShipment> RefreshTrackingAsync(TrackedShipment shipment, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shipment.ProviderTrackerId))
            throw new TrackingProviderException(TrackingProviderFailure.TrackerNotFound);
        // Replay creation inputs, never the courier inferred by the provider.
        var result = await TrackAsync(shipment.TrackingNumber, shipment.RequestedCarrierCode, "Refresh", cancellationToken).ConfigureAwait(false);
        if (!string.Equals(result.ProviderTrackerId, shipment.ProviderTrackerId, StringComparison.Ordinal))
        {
            AppLogger.Info("Ship24 Refresh: InvalidResponse; tracker identity mismatch.");
            throw new TrackingProviderException(TrackingProviderFailure.InvalidResponse);
        }
        return result;
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body, string operation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var elapsed = Stopwatch.StartNew();
        int? status = null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        try
        {
            var loaded = credentials.Load(ProviderId, out var credential);
            if (loaded == TrackingCredentialLoadResult.Missing)
                throw new TrackingProviderException(TrackingProviderFailure.MissingCredential);
            if (credential == null)
                throw new TrackingProviderException(TrackingProviderFailure.InvalidCredential);
            using var request = new HttpRequestMessage(method, "https://api.ship24.com" + path);
            // Use request-scoped headers, never mutate a shared client's defaults.
            try { request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.Reveal()); }
            catch (FormatException) { throw new TrackingProviderException(TrackingProviderFailure.InvalidCredential); }
            if (body != null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            status = (int)response.StatusCode;
            using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(chunk, deadline.Token).ConfigureAwait(false)) != 0)
            {
                if (buffer.Length + count > MaxResponseBytes)
                    throw new TrackingProviderException(response.IsSuccessStatusCode ? TrackingProviderFailure.InvalidResponse : Failure(status.Value, null), status);
                buffer.Write(chunk, 0, count);
            }
            JsonDocument? json = null;
            try { json = JsonDocument.Parse(buffer.ToArray()); }
            catch (JsonException) when (!response.IsSuccessStatusCode) { }
            if (!response.IsSuccessStatusCode)
            {
                using (json)
                {
                    string? code = null;
                    var errors = json == null ? default : Property(json.RootElement, "errors");
                    if (errors.ValueKind == JsonValueKind.Array)
                        code = errors.EnumerateArray().Select(e => Text(e, "code")).FirstOrDefault();
                    throw new TrackingProviderException(Failure(status.Value, code), status);
                }
            }
            AppLogger.Info($"Ship24 {operation}: HTTP {status}; {elapsed.ElapsedMilliseconds} ms.");
            return json!;
        }
        catch (OperationCanceledException)
        {
            if (token.IsCancellationRequested) throw new OperationCanceledException(token);
            throw SafeFailure(TrackingProviderFailure.Timeout);
        }
        catch (HttpRequestException) { throw SafeFailure(TrackingProviderFailure.NetworkError); }
        catch (IOException) { throw SafeFailure(TrackingProviderFailure.NetworkError); }
        catch (JsonException) { throw SafeFailure(TrackingProviderFailure.InvalidResponse); }
        catch (TrackingProviderException ex)
        {
            AppLogger.Info($"Ship24 {operation}: {ex.Failure}; HTTP {status}; {elapsed.ElapsedMilliseconds} ms.");
            throw;
        }

        TrackingProviderException SafeFailure(TrackingProviderFailure failure)
        {
            AppLogger.Info($"Ship24 {operation}: {failure}; HTTP {status}; {elapsed.ElapsedMilliseconds} ms.");
            return new TrackingProviderException(failure, status);
        }
    }

    private static TrackingProviderFailure Failure(int status, string? code) => status switch
    {
        401 => TrackingProviderFailure.Unauthorized,
        _ when code == "quota_limit_reached" => TrackingProviderFailure.QuotaExhausted,
        403 => TrackingProviderFailure.Forbidden,
        429 => TrackingProviderFailure.RateLimit,
        404 => TrackingProviderFailure.TrackerNotFound,
        _ when code == "tracker_not_found" => TrackingProviderFailure.TrackerNotFound,
        _ when code == "validation_error" => TrackingProviderFailure.InvalidTrackingNumber,
        _ => TrackingProviderFailure.ServiceError
    };

    public void Dispose() { if (ownsClient) client.Dispose(); }
}
