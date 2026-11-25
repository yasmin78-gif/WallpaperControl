using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace RemoteExperiment;

public sealed class StateHub(StateModel model, Func<Func<object?>, Task<object?>> dispatch) : Hub
{
    public override async Task OnConnectedAsync() { await Clients.Caller.SendAsync("StateChanged", (Snapshot)(await dispatch(() => model.State))!); await base.OnConnectedAsync(); }
}

public sealed class RemoteHost : IAsyncDisposable
{
    private WebApplication? app;
    private Task? sender;
    private readonly Channel<Snapshot> updates = Channel.CreateBounded<Snapshot>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly CancellationTokenSource stopping = new();
    public long AuthenticatedRequests;
    public long PublishedSnapshots;
    public long PublishedJsonBytes;
    public Pairing Pairing { get; } = new();
    public StateModel Model { get; }
    private readonly Func<Func<object?>, Task<object?>> dispatch;
    public int Port { get; private set; }
    public RemoteHost(StateModel model, Func<Func<object?>, Task<object?>> dispatcher) { Model = model; dispatch = dispatcher; Model.Changed += Publish; }
    private void Publish(Snapshot state) => updates.Writer.TryWrite(state);
    public async Task StartAsync(IPAddress address, int port, X509Certificate2 certificate)
    {
        if (!Trust.Local(address)) throw new ArgumentException("Non-local address rejected");
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders(); // Never log headers, pairing bodies or URLs.
        builder.WebHost.ConfigureKestrel(options => { options.Limits.MaxRequestBodySize = 8192; options.Limits.MaxConcurrentConnections = 32; options.Listen(address, port, listen => listen.UseHttps(certificate)); });
        builder.Services.AddSingleton(Model); builder.Services.AddSingleton(dispatch);
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter((context.Connection.RemoteIpAddress?.ToString() ?? "unknown") + (context.Request.Path == "/api/v1/pairing/requests" ? "-pair" : "-other"), _ => new FixedWindowRateLimiterOptions { PermitLimit = context.Request.Path == "/api/v1/pairing/requests" ? 5 : 6000, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        builder.Services.AddSignalR(options => { options.MaximumReceiveMessageSize = 8192; options.KeepAliveInterval = TimeSpan.FromSeconds(5); options.ClientTimeoutInterval = TimeSpan.FromSeconds(15); options.EnableDetailedErrors = false; });
        app = builder.Build();
        app.UseRateLimiter();
        app.Use(async (context, next) =>
        {
            if (context.Request.Headers.Origin.Count != 0) { context.Response.StatusCode = 403; return; }
            await next(context);
        });
        app.Use(async (context, next) =>
        {
            if (context.Request.QueryString.HasValue) { context.Response.StatusCode = 400; return; }
            if (!context.Request.IsHttps) { context.Response.StatusCode = 400; return; }
            if (context.Request.Path.StartsWithSegments("/api/v1/pairing")) { await next(context); return; }
            string auth = context.Request.Headers.Authorization.ToString();
            var device = auth.StartsWith("Bearer ", StringComparison.Ordinal) && auth.Length < 256 ? Pairing.Authenticate(auth[7..]) : null;
            if (device == null) { context.Response.StatusCode = 401; return; }
            Interlocked.Increment(ref AuthenticatedRequests);
            context.Items["device"] = device.Value;
            await next(context);
        });
        app.MapGet("/api/v1/state", async () => Results.Json((Snapshot)(await dispatch(() => Model.State))!));
        app.MapPost("/api/v1/commands", async (Command command, HttpContext context) =>
        {
            var device = ((string Device, bool Disable))context.Items["device"]!;
            var result = (Result)(await dispatch(() => Model.Execute(command, device.Device, device.Disable)))!;
            return Results.Json(result, statusCode: result.Status == "completed" ? 200 : 409);
        });
        app.MapPost("/api/v1/pairing/requests", (PairRequest request) => Pairing.Request(request) is { } pending ? Results.Json(pending) : Results.StatusCode(403));
        app.MapPost("/api/v1/pairing/claim", (ClaimRequest request) => Pairing.Claim(request.Claim) is { } device ? Results.Json(device) : Results.StatusCode(409));
        app.MapHub<StateHub>("/api/v1/events", options => { options.Transports = HttpTransportType.WebSockets; options.TransportSendTimeout = TimeSpan.FromSeconds(5); options.ApplicationMaxBufferSize = 16384; options.TransportMaxBufferSize = 16384; });
        await app.StartAsync();
        Port = new Uri(app.Urls.Single()).Port;
        var hub = app.Services.GetRequiredService<IHubContext<StateHub>>();
        sender = Task.Run(async () => { try { await foreach (var snapshot in updates.Reader.ReadAllAsync(stopping.Token)) { Interlocked.Increment(ref PublishedSnapshots); Interlocked.Add(ref PublishedJsonBytes, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(snapshot).Length); await hub.Clients.All.SendAsync("StateChanged", snapshot, stopping.Token); } } catch (OperationCanceledException) { } });
    }
    public async ValueTask DisposeAsync()
    {
        Model.Changed -= Publish; stopping.Cancel(); updates.Writer.TryComplete();
        if (sender != null) await sender;
        if (app != null) { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await app.StopAsync(timeout.Token); await app.DisposeAsync(); }
        stopping.Dispose();
    }
}
