using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace WallpaperControl.Remote;

public sealed class RemoteConnection : IAsyncDisposable
{
    private readonly SavedRemote saved;
    private readonly HttpClient http;
    private readonly HttpMessageInvoker websocketHttp;
    private HubConnection? hub;
    private bool disposed;
    private int generation;
    private readonly HashSet<string> retiredEpochs = new(StringComparer.Ordinal);
    public RemoteState? State { get; private set; }
    public bool Connected { get; private set; }
    public bool Unauthorized { get; private set; }
    public event Action? Changed;
    public RemoteConnection(SavedRemote saved, bool allowLoopbackForTests = false)
    {
        this.saved = saved;
        http = new(RemoteTrust.Handler(saved.Server.Pin)) { BaseAddress = RemoteTrust.Endpoint(saved.Address, allowLoopbackForTests), Timeout = TimeSpan.FromSeconds(45), MaxResponseContentBufferSize = 65536 };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", saved.Credential.Token);
        websocketHttp = new(RemoteTrust.Handler(saved.Server.Pin));
    }
    private void Accept(RemoteState snapshot)
    {
        if (snapshot.ServerId != saved.Server.ServerId || snapshot.ApiVersion != 1) throw new InvalidOperationException("Incompatible server");
        if (retiredEpochs.Contains(snapshot.Epoch)) return;
        if (State != null && snapshot.Epoch != State.Epoch) retiredEpochs.Add(State.Epoch);
        if (State == null || snapshot.Epoch != State.Epoch || snapshot.Revision >= State.Revision) { State = snapshot; Changed?.Invoke(); }
    }
    public async Task ConnectAsync(CancellationToken cancellation = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromSeconds(5));
        await ProbeWebSocketAsync(deadline.Token); await RefreshAsync(deadline.Token); Connected = true; Changed?.Invoke();
    }
    // Bounded one-shot consumers use the exact same pinned/authenticated REST path without a hub.
    public async Task ConnectRestAsync(CancellationToken cancellation = default)
    {
        if (disposed) throw new ObjectDisposedException(nameof(RemoteConnection));
        await RefreshAsync(cancellation); Connected = true; Changed?.Invoke();
    }
    public async Task ProbeWebSocketAsync(CancellationToken cancellation = default)
    {
        if (disposed) throw new ObjectDisposedException(nameof(RemoteConnection));
        Connected = false; Unauthorized = false; Changed?.Invoke(); int request = ++generation;
        if (hub != null) await hub.DisposeAsync();
        hub = new HubConnectionBuilder().WithUrl(new Uri(http.BaseAddress!, "/api/v1/events"), options =>
        {
            options.SkipNegotiation = true; options.Transports = HttpTransportType.WebSockets;
            options.ApplicationMaxBufferSize = 65536; options.TransportMaxBufferSize = 65536;
            options.Headers["Authorization"] = "Bearer " + saved.Credential.Token;
            options.WebSocketFactory = async (context, token) =>
            {
                var socket = new ClientWebSocket(); socket.Options.SetRequestHeader("Authorization", "Bearer " + saved.Credential.Token);
                try { await socket.ConnectAsync(context.Uri, websocketHttp, token); return socket; }
                catch { socket.Dispose(); throw; }
            };
        }).AddJsonProtocol(options => options.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, RemoteJsonContext.Default)).Build();
        hub.KeepAliveInterval = TimeSpan.FromSeconds(5); hub.ServerTimeout = TimeSpan.FromSeconds(15); hub.HandshakeTimeout = TimeSpan.FromSeconds(5);
        hub.On<RemoteState>("StateChanged", state => { if (request == generation && !disposed) Accept(state); });
        hub.Closed += _ => { if (request == generation) { Connected = false; Changed?.Invoke(); } return Task.CompletedTask; };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try { await hub.StartAsync(deadline.Token); }
        catch (Exception ex)
        {
            for (Exception? cause = ex; cause != null; cause = cause.InnerException)
                if (cause is HttpRequestException requestError && requestError.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) Unauthorized = true;
            Changed?.Invoke(); throw;
        }
    }
    public async Task<RemoteState> RefreshAsync(CancellationToken cancellation = default)
    {
        using var response = await http.GetAsync("api/v1/state", cancellation);
        if (response.StatusCode == HttpStatusCode.Unauthorized) Unauthorized = true;
        response.EnsureSuccessStatusCode();
        var state = await response.Content.ReadFromJsonAsync(RemoteJson.Type<RemoteState>(), cancellation) ?? throw new InvalidOperationException("Missing state");
        Accept(state); return state;
    }
    public async Task<RemoteResult> SendAsync(string type, string value = "", CancellationToken cancellation = default)
    {
        if (!Connected || State == null) throw new InvalidOperationException("Disconnected");
        var command = new RemoteCommand(Guid.NewGuid().ToString("N"), State.Epoch, State.Revision, type, value);
        try
        {
            using var response = await http.PostAsJsonAsync("api/v1/commands", command, RemoteJson.Type<RemoteCommand>(), cancellation);
            if (response.StatusCode == HttpStatusCode.Unauthorized) { Unauthorized = true; Connected = false; Changed?.Invoke(); }
            if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Conflict or HttpStatusCode.Forbidden)) response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync(RemoteJson.Type<RemoteResult>(), cancellation) ?? throw new InvalidOperationException("Missing command result");
            Accept(result.State); return result;
        }
        catch
        {
            // Never replay navigation after an uncertain response; retrieve only the original result.
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                using var resultResponse = await http.GetAsync("api/v1/commands/" + command.Id, timeout.Token);
                if (resultResponse.IsSuccessStatusCode && await resultResponse.Content.ReadFromJsonAsync(RemoteJson.Type<RemoteResult>(), timeout.Token) is { } result) { Accept(result.State); return result; }
            }
            catch { }
            Connected = false; Changed?.Invoke(); throw;
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true; generation++; Connected = false;
        if (hub != null) await hub.DisposeAsync(); http.Dispose(); websocketHttp.Dispose(); Changed?.Invoke();
    }
}

public static class RemoteEnrollment
{
    public static async Task<SavedRemote> PairAsync(RemoteQr qr, Action<string> showCode, Func<SavedRemote, Task> save, CancellationToken cancellation)
    {
        if (qr.Version != 1 || qr.ServerId?.Length != 32 || qr.Pin?.Length != 64 || qr.Secret?.Length != 64 || qr.Session?.Length != 32 || qr.Addresses == null || qr.Addresses.Length is < 1 or > 16) throw new ArgumentException("Invalid QR");
        string address = qr.Addresses.First();
        using var http = new HttpClient(RemoteTrust.Handler(qr.Pin)) { BaseAddress = RemoteTrust.Endpoint(address), Timeout = TimeSpan.FromSeconds(5), MaxResponseContentBufferSize = 65536 };
        using var requested = await http.PostAsJsonAsync("api/v1/pairing/requests", new PairRequest(qr.Session, qr.Secret, "Android"), RemoteJson.Type<PairRequest>(), cancellation);
        requested.EnsureSuccessStatusCode();
        var pending = await requested.Content.ReadFromJsonAsync(RemoteJson.Type<PairPending>(), cancellation) ?? throw new InvalidOperationException("Missing pairing response");
        showCode(pending.Code);
        using var expires = CancellationTokenSource.CreateLinkedTokenSource(cancellation); expires.CancelAfter(TimeSpan.FromMinutes(5));
        while (true)
        {
            await Task.Delay(1500, expires.Token);
            using var claim = await http.PostAsJsonAsync("api/v1/pairing/claim", new ClaimRequest(pending.Claim), RemoteJson.Type<ClaimRequest>(), expires.Token);
            if (claim.StatusCode == HttpStatusCode.Conflict) continue;
            claim.EnsureSuccessStatusCode();
            var credential = await claim.Content.ReadFromJsonAsync(RemoteJson.Type<DeviceCredential>(), expires.Token) ?? throw new InvalidOperationException("Missing credentials");
            if (credential.DeviceId?.Length != 32 || credential.Token?.Length != 64 || !credential.Token.All(Uri.IsHexDigit)) throw new InvalidOperationException("Invalid device credential");
            var connection = new SavedRemote(qr with { Session = "", Secret = "" }, credential, address);
            await save(connection); // A failure here leaves pairing incomplete and never claims successful persistence.
            try { using var completed = await http.PostAsJsonAsync("api/v1/pairing/complete", new ClaimRequest(pending.Claim), RemoteJson.Type<ClaimRequest>(), expires.Token); } catch (HttpRequestException) { }
            return connection;
        }
    }
}
