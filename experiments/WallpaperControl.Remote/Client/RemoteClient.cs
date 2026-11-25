using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace RemoteExperiment;

// Explicit managed TLS validation for REST and ClientWebSocket. No query-string tokens.
public sealed class RemoteClient : IAsyncDisposable
{
    private readonly SavedConnection saved;
    private readonly HttpClient http;
    private readonly HttpMessageInvoker websocketHttp;
    private HubConnection? hub;
    private readonly bool automaticReconnect;
    private readonly TimeSpan connectTimeout;
    public Snapshot? State { get; private set; }
    public bool Connected { get; private set; }
    public event Action? Changed;
    public RemoteClient(SavedConnection connection, bool automaticReconnect = true, TimeSpan? connectTimeout = null)
    {
        this.automaticReconnect = automaticReconnect;
        this.connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(5);
        saved = connection;
        http = new(Trust.Handler(connection.Server.Pin)) { BaseAddress = Trust.Endpoint(connection.Server.Address), Timeout = TimeSpan.FromSeconds(10) };
        websocketHttp = new HttpMessageInvoker(Trust.Handler(connection.Server.Pin));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", connection.Credential.Token);
    }
    private void Accept(Snapshot state)
    {
        if (state.ServerId != saved.Server.ServerId) throw new InvalidOperationException("serverIdentity");
        if (State == null || state.Epoch != State.Epoch || state.Revision >= State.Revision) { State = state; Changed?.Invoke(); }
    }
    public async Task StartAsync(CancellationToken cancellation = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(connectTimeout);
        await ProbeWssAsync(deadline.Token); await RefreshAsync(deadline.Token); Connected = true; Changed?.Invoke();
    }
    // Separate transport probe deliberately does not touch REST. Used by device TLS acceptance.
    public async Task ProbeWssAsync(CancellationToken cancellation = default)
    {
        Connected = false; Changed?.Invoke();
        if (hub != null) await hub.DisposeAsync();
        var builder = new HubConnectionBuilder().WithUrl(new Uri(http.BaseAddress!, "/api/v1/events"), options =>
        {
            options.Transports = HttpTransportType.WebSockets; options.SkipNegotiation = true;
            options.Headers["Authorization"] = "Bearer " + saved.Credential.Token;
            options.HttpMessageHandlerFactory = _ => Trust.Handler(saved.Server.Pin);
            options.WebSocketFactory = async (context, cancellation) =>
            {
                var socket = new System.Net.WebSockets.ClientWebSocket();
                socket.Options.SetRequestHeader("Authorization", "Bearer " + saved.Credential.Token);
                try { await socket.ConnectAsync(context.Uri, websocketHttp, cancellation); return socket; }
                catch { socket.Dispose(); throw; }
            };
        });
        if (automaticReconnect) builder.WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5) });
        hub = builder.Build();
        hub.HandshakeTimeout = connectTimeout;
        hub.ServerTimeout = TimeSpan.FromSeconds(15);
        hub.KeepAliveInterval = TimeSpan.FromSeconds(5);
        hub.On<Snapshot>("StateChanged", Accept);
        hub.Reconnecting += _ => { Connected = false; Changed?.Invoke(); return Task.CompletedTask; };
        hub.Closed += _ => { Connected = false; Changed?.Invoke(); return Task.CompletedTask; };
        hub.Reconnected += async _ => { try { await RefreshAsync(); Connected = true; Changed?.Invoke(); } catch { Connected = false; Changed?.Invoke(); } };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(connectTimeout);
        await hub.StartAsync(deadline.Token);
    }
    public async Task<Snapshot> RefreshAsync(CancellationToken cancellation = default) { var state = await http.GetFromJsonAsync("/api/v1/state", WireJson.Type<Snapshot>(), cancellation) ?? throw new InvalidOperationException("emptyState"); Accept(state); return state; }
    public async Task<Result> SendAsync(string type, string value)
    {
        if (!Connected || State == null) throw new InvalidOperationException("offline");
        return await SendRawAsync(new(Guid.NewGuid().ToString("N"), State.Epoch, State.Revision, type, value));
    }
    public async Task<Result> SendRawAsync(Command command)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("/api/v1/commands", command, WireJson.Type<Command>());
            if ((int)response.StatusCode is not (200 or 409)) response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync(WireJson.Type<Result>()) ?? throw new InvalidOperationException("emptyResult"); Accept(result.State); return result;
        }
        catch { Connected = false; Changed?.Invoke(); throw; }
    }
    public async ValueTask DisposeAsync() { Connected = false; if (hub != null) await hub.DisposeAsync(); http.Dispose(); websocketHttp.Dispose(); }
}
