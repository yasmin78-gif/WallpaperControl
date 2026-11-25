using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace WallpaperControl.Remote;

public sealed class RemoteHub(RemoteServer server) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var grant = Context.GetHttpContext()?.Items["remoteDevice"] as DeviceGrant;
        if (grant == null || !server.Vault.Current(grant) || !server.Accepting) { Context.Abort(); return; }
        server.Connections[Context.ConnectionId] = (grant, Context.Abort);
        await Groups.AddToGroupAsync(Context.ConnectionId, grant.Id);
        if (server.State is { } state) await Clients.Caller.SendAsync("StateChanged", state.ForDevice(grant.CanDisableVisitor), Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }
    public override Task OnDisconnectedAsync(Exception? exception) { server.Connections.TryRemove(Context.ConnectionId, out _); return base.OnDisconnectedAsync(exception); }
}

public sealed class RemoteServer : IAsyncDisposable
{
    public RemoteVault Vault { get; }
    public RemotePairing Pairing { get; }
    public RemoteState? State { get; private set; }
    public bool Accepting { get; private set; }
    public string Epoch { get; } = Guid.NewGuid().ToString("N");
    public int Port { get; private set; }
    public bool HasConnectedClients => !Connections.IsEmpty;
    internal ConcurrentDictionary<string, (DeviceGrant Grant, Action Abort)> Connections { get; } = new();
    private readonly Func<Task<RemoteState>> readState;
    private readonly Func<RemoteCommand, DeviceGrant, Task<RemoteOutcome>> execute;
    private readonly Func<IPAddress, bool> localPeer;
    private readonly Channel<RemoteState> updates = Channel.CreateBounded<RemoteState>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly SemaphoreSlim commands = new(1, 1);
    private readonly ConcurrentDictionary<(string Device, string Command), (string Hash, RemoteResult Result, DateTimeOffset At)> results = new();
    private readonly CancellationTokenSource stopping = new();
    private WebApplication? app;
    private Task? publishing;
    private bool disposed;
    private long revision;
    private string lastSignature = "";
    private string playbackStamp = "";
    private DateTimeOffset lastPublished;
    private readonly object stateGate = new();

    public RemoteServer(RemoteVault vault, Func<Task<RemoteState>> readState, Func<RemoteCommand, DeviceGrant, Task<RemoteOutcome>> execute, Func<IPAddress, bool> localPeer)
    {
        Vault = vault; Pairing = new(vault); this.readState = readState; this.execute = execute; this.localPeer = localPeer;
        vault.DevicesChanged += PermissionsChanged;
    }
    public void Observe(RemoteState state, string? privatePlaybackStamp = null)
    {
        if (!Accepting) return;
        lock (stateGate)
        {
            var semantic = state with { ServerId = "", ServerName = "", Epoch = "", Revision = 0, CapturedAt = default, RemainingMilliseconds = null };
            if (privatePlaybackStamp != null) playbackStamp = privatePlaybackStamp;
            string signature = JsonSerializer.Serialize(semantic, RemoteJson.Type<RemoteState>()) + "\n" + playbackStamp;
            bool changed = signature != lastSignature;
            if (changed) { lastSignature = signature; revision++; }
            State = state with { ServerId = Vault.ServerId, ServerName = Vault.Settings.Name, Epoch = Epoch, Revision = revision, CapturedAt = DateTimeOffset.UtcNow };
            if (changed || state.RemainingMilliseconds != null && DateTimeOffset.UtcNow - lastPublished >= TimeSpan.FromSeconds(1))
            { lastPublished = DateTimeOffset.UtcNow; updates.Writer.TryWrite(State); }
        }
    }
    private async Task<RemoteState> RefreshAsync() { Observe(await readState()); return State ?? throw new InvalidOperationException("Remote not ready"); }
    private void PermissionsChanged()
    {
        foreach (var connection in Connections.Values) if (!Vault.Current(connection.Grant)) connection.Abort();
        if (State is { } state) updates.Writer.TryWrite(state);
    }
    public void StopAccepting()
    {
        Accepting = false; Pairing.Cancel();
        foreach (var connection in Connections.Values) connection.Abort();
    }
    public async Task StartAsync(IPAddress[] addresses, int port, X509Certificate2 identity)
    {
        if (disposed || app != null || addresses.Length == 0 || addresses.Any(a => !RemoteTrust.Local(a))) throw new InvalidOperationException("Invalid binding");
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Production", ContentRootPath = AppContext.BaseDirectory });
        builder.Configuration.Sources.Clear(); // No external Kestrel endpoints or unencrypted URLs from environment/config files.
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(this);
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, RemoteJsonContext.Default));
        builder.Services.AddSignalR(o => { o.KeepAliveInterval = TimeSpan.FromSeconds(5); o.ClientTimeoutInterval = TimeSpan.FromSeconds(15); o.HandshakeTimeout = TimeSpan.FromSeconds(5); o.MaximumReceiveMessageSize = 8192; o.EnableDetailedErrors = false; });
        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = 429;
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(c => RateLimitPartition.GetFixedWindowLimiter(
                (c.Connection.RemoteIpAddress?.ToString() ?? "unknown") + (c.Request.Path == "/api/v1/pairing/requests" ? "pair" : "other"),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = c.Request.Path == "/api/v1/pairing/requests" ? 5 : 300, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        builder.WebHost.ConfigureKestrel(o => { o.AddServerHeader = false; o.Limits.MaxRequestBodySize = 8192; o.Limits.MaxConcurrentConnections = 48; o.Limits.MaxConcurrentUpgradedConnections = 32; o.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10); foreach (var address in addresses) o.Listen(address, port, l => l.UseHttps(identity)); });
        app = builder.Build();
        app.Use(async (context, next) =>
        {
            try { await next(context); }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { context.Abort(); }
            catch
            {
                if (context.Response.HasStarted) { context.Abort(); return; }
                context.Response.Clear(); context.Response.StatusCode = 500; context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"error\":\"operationFailed\"}");
            }
        });
        app.Use(async (context, next) =>
        {
            if (!Accepting || context.Connection.RemoteIpAddress is not { } peer || !localPeer(peer)) { context.Response.StatusCode = 503; return; }
            if (!context.Request.IsHttps || context.Request.QueryString.HasValue || context.Request.Headers.Origin.Count != 0) { context.Response.StatusCode = 400; return; }
            context.Response.Headers.CacheControl = "no-store";
            await next(context);
        });
        app.UseRateLimiter();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api/v1/pairing")) { await next(context); return; }
            string header = context.Request.Headers.Authorization.ToString();
            var grant = header.StartsWith("Bearer ", StringComparison.Ordinal) && header.Length < 256 ? Vault.Authenticate(header[7..]) : null;
            if (grant == null) { context.Response.StatusCode = 401; return; }
            context.Items["remoteDevice"] = grant; await next(context);
        });
        app.MapGet("/api/v1/state", (Func<HttpContext, Task<IResult>>)(async c =>
        {
            var grant = Grant(c); var snapshot = await RefreshAsync();
            if (!Accepting) return Results.StatusCode(503);
            if (!Vault.Current(grant)) return Results.Unauthorized();
            return Results.Json(snapshot.ForDevice(grant.CanDisableVisitor), RemoteJson.Type<RemoteState>());
        }));
        app.MapPost("/api/v1/commands", (RemoteCommand command, HttpContext context) => CommandAsync(command, context));
        app.MapGet("/api/v1/commands/{id}", (string id, HttpContext c) => results.TryGetValue((Grant(c).Id, id), out var value)
            ? Results.Json(value.Result with { State = value.Result.State.ForDevice(Grant(c).CanDisableVisitor) }, RemoteJson.Type<RemoteResult>()) : Results.NotFound());
        app.MapPost("/api/v1/pairing/requests", (PairRequest request) => Pairing.Request(request) is { } pending ? Results.Json(pending, RemoteJson.Type<PairPending>()) : Results.StatusCode(403));
        app.MapPost("/api/v1/pairing/claim", (ClaimRequest claim) => Pairing.Claim(claim) is { } credential ? Results.Json(credential, RemoteJson.Type<DeviceCredential>()) : Results.StatusCode(409));
        app.MapPost("/api/v1/pairing/complete", (ClaimRequest claim) => { Pairing.Complete(claim); return Results.NoContent(); });
        app.MapHub<RemoteHub>("/api/v1/events", o => { o.Transports = HttpTransportType.WebSockets; o.ApplicationMaxBufferSize = 16384; o.TransportMaxBufferSize = 16384; o.TransportSendTimeout = TimeSpan.FromSeconds(3); });
        Accepting = true;
        try { await app.StartAsync(); Port = new Uri(app.Urls.First()).Port; await RefreshAsync(); }
        catch { Accepting = false; throw; }
        var hub = app.Services.GetRequiredService<IHubContext<RemoteHub>>();
        publishing = Task.Run(async () =>
        {
            try
            {
                await foreach (var state in updates.Reader.ReadAllAsync(stopping.Token))
                {
                    if (Connections.IsEmpty) continue;
                    foreach (var device in Vault.Devices)
                        await hub.Clients.Group(device.Id).SendAsync("StateChanged", state.ForDevice(device.CanDisableVisitor), stopping.Token);
                }
            }
            catch (OperationCanceledException) { }
        });
    }
    private static DeviceGrant Grant(HttpContext context) => (DeviceGrant)context.Items["remoteDevice"]!;
    private async Task<IResult> CommandAsync(RemoteCommand command, HttpContext context)
    {
        var grant = Grant(context);
        if (string.IsNullOrEmpty(command.Id) || command.Id.Length > 64 || string.IsNullOrEmpty(command.Type) || command.Type.Length > 32 || command.Value == null || command.Value.Length > 64) return Results.BadRequest();
        string hash = RemoteTrust.Hash(JsonSerializer.Serialize(command, RemoteJson.Type<RemoteCommand>()));
        var key = (grant.Id, command.Id);
        if (results.TryGetValue(key, out var previous)) return previous.Hash == hash ? Results.Json(previous.Result with { State = previous.Result.State.ForDevice(grant.CanDisableVisitor) }, RemoteJson.Type<RemoteResult>()) : Results.Conflict();
        if (!await commands.WaitAsync(0)) return Results.Json(new RemoteResult(command.Id, "rejected", "busy", (State ?? new()).ForDevice(grant.CanDisableVisitor)), RemoteJson.Type<RemoteResult>(), statusCode: 409);
        try
        {
            if (!Accepting || !Vault.Current(grant)) return Results.Unauthorized();
            var state = await RefreshAsync();
            if (command.Epoch != state.Epoch || command.Revision != state.Revision) return Results.Json(new RemoteResult(command.Id, "rejected", "stale", state.ForDevice(grant.CanDisableVisitor)), RemoteJson.Type<RemoteResult>(), statusCode: 409);
            if (!state.ForDevice(grant.CanDisableVisitor).Capabilities.Allows(command.Type)) return Results.Json(new RemoteResult(command.Id, "rejected", "notAllowed", state.ForDevice(grant.CanDisableVisitor)), RemoteJson.Type<RemoteResult>(), statusCode: 403);
            RemoteOutcome outcome;
            try { outcome = await execute(command, grant); }
            catch { outcome = new(false, "operationFailed"); }
            if (!Vault.Current(grant)) return Results.Unauthorized();
            var result = new RemoteResult(command.Id, outcome.Success ? "completed" : "failed", outcome.ErrorCode, (await RefreshAsync()).ForDevice(grant.CanDisableVisitor));
            results[key] = (hash, result, DateTimeOffset.UtcNow);
            foreach (var stale in results.Where(p => p.Value.At < DateTimeOffset.UtcNow.AddMinutes(-10)).Select(p => p.Key).ToArray()) results.TryRemove(stale, out _);
            while (results.Count > 1024) results.TryRemove(results.Keys.First(), out _);
            return Results.Json(result, RemoteJson.Type<RemoteResult>());
        }
        finally { commands.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true; StopAccepting(); Vault.DevicesChanged -= PermissionsChanged;
        stopping.Cancel(); updates.Writer.TryComplete(); if (publishing != null) await publishing;
        if (app != null) { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)); try { await app.StopAsync(timeout.Token); } catch (OperationCanceledException) { } await app.DisposeAsync(); }
        stopping.Dispose();
    }
}
