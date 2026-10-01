extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using Microsoft.Win32;
using System.Net;
using System.Text;
using System.Text.Json;

internal static class PostRefreshTests
{
    private const string Secret = "post-refresh-secret-never-log";
    private sealed class Handler : HttpMessageHandler
    {
        internal int Posts, Gets;
        internal Func<CancellationToken, Task<HttpResponseMessage>> Reply = _ => Task.FromResult(Response());
        internal string? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.Method == HttpMethod.Post) Posts++; else Gets++;
            if (request.RequestUri!.AbsolutePath != "/public/v1/trackers/track") throw new Exception("Unexpected endpoint");
            Body = await request.Content!.ReadAsStringAsync(token);
            return await Reply(token);
        }
    }
    private static HttpResponseMessage Response(string id = "tracker-1", int status = 200) => new((HttpStatusCode)status)
    { Content = new StringContent(JsonSerializer.Serialize(new { data = new { trackings = new[] { new {
        tracker = new { trackerId = id, trackingNumber = "ABCDE123", courierCode = new[] { "inferred" } },
        shipment = new { statusMilestone = "in_transit", statusCategory = "transit" },
        events = new[] { new { eventId = "new", occurrenceDatetime = "2026-10-01T12:00:00Z", status = "Current result" } }
    } } } }), Encoding.UTF8, "application/json") };
    internal static void Run(Action<bool, string> check) => Task.Run(() => RunAsync(check)).GetAwaiter().GetResult();
    private static async Task RunAsync(Action<bool, string> check)
    {
        string key = @"Software\WallpaperControl.PostRefreshTests-" + Guid.NewGuid().ToString("N");
        string path = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"), "post.json");
        var credentials = new App.TrackingCredentialStore(key);
        try
        {
            check(credentials.Save("ship24", new App.TrackingCredential(Secret)), "POST refresh isolated credentials");
            using var handler = new Handler(); using var client = new HttpClient(handler);
            using var provider = new App.Ship24TrackingProvider(credentials, client);
            var store = new App.TrackingStore(path);
            var old = new App.TrackedShipment { Provider = "ship24", ProviderTrackerId = "tracker-1", TrackingNumber = "ABCDE123",
                CarrierCode = "inferred", StatusMilestone = "in_transit", LastSuccessfulRefresh = DateTimeOffset.UtcNow.AddHours(-4),
                DisplayName = "Keep name", Events = new() { new() { EventId = "old" } } };
            check(store.Save(old), "POST refresh initial shipment stored");
            using var service = new App.PackageTrackingService(store, provider);
            using var scheduler = new App.PackageRefreshScheduler(service, useTimer: false);
            scheduler.Configure(true, 30); await scheduler.EvaluateAsync("startup");
            check(handler.Posts == 1 && handler.Gets == 0, "Due automatic shipment uses POST only");
            using (var body = JsonDocument.Parse(handler.Body!)) check(body.RootElement.GetProperty("trackingNumber").GetString() == old.TrackingNumber && !body.RootElement.TryGetProperty("courierCode", out _), "POST refresh preserves original auto-detection payload");
            var good = service.Shipments.Single();
            check(good.Id == old.Id && good.DisplayName == old.DisplayName && good.ProviderTrackerId == old.ProviderTrackerId, "POST same tracker updates same local shipment");
            check(good.Events.Single().EventId == "new" && good.LastSuccessfulRefresh > old.LastSuccessfulRefresh, "POST successful result and refresh timestamp advance");
            await scheduler.EvaluateAsync(); check(handler.Posts == 1, "POST automatic skips fresh shipment");
            await service.RefreshAsync(); check(handler.Posts == 2 && handler.Gets == 0, "POST manual bypasses due interval");
            good = service.Shipments.Single();
            handler.Reply = _ => Task.FromResult(Response("unexpected"));
            check((await service.RefreshAsync()).Outcome == App.PackageOperation.ProviderError, "POST tracker mismatch fails safely");
            check(JsonSerializer.Serialize(service.Shipments.Single()) == JsonSerializer.Serialize(good) && service.RefreshFailures[good.Id] == App.TrackingProviderFailure.InvalidResponse, "POST mismatch preserves entire last good snapshot");
            handler.Reply = _ => Task.FromResult(Response(status: 503));
            await service.RefreshAsync(); check(JsonSerializer.Serialize(service.Shipments.Single()) == JsonSerializer.Serialize(good), "POST HTTP failure preserves events status and successful timestamp");
            handler.Reply = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("invalid-json-" + Secret) });
            await service.RefreshAsync();
            check(JsonSerializer.Serialize(service.Shipments.Single()) == JsonSerializer.Serialize(good), "Malformed POST response preserves last successful shipment");
            handler.Reply = async token => { await Task.Delay(Timeout.Infinite, token); return Response(); };
            using (var cancellation = new CancellationTokenSource(30))
            {
                try { await service.RefreshAsync(cancellation.Token); check(false, "Cancelled POST must throw"); }
                catch (OperationCanceledException) { check(!service.Busy && JsonSerializer.Serialize(service.Shipments.Single()) == JsonSerializer.Serialize(good), "Cancelled POST releases gate and preserves snapshot"); }
            }
            handler.Reply = _ => Task.FromResult(Response());
            store.Save(good with { RequestedCarrierCode = "explicit" }); await service.RefreshAsync();
            using (var body = JsonDocument.Parse(handler.Body!)) check(body.RootElement.GetProperty("courierCode")[0].GetString() == "explicit", "POST optional original courier hint replayed");
            var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            handler.Reply = token => pending.Task.WaitAsync(token);
            var manual = service.RefreshAsync(); int requests = handler.Posts;
            await scheduler.EvaluateAsync("resume");
            check((await service.RefreshAsync()).Outcome == App.PackageOperation.Busy && handler.Posts == requests, "POST manual holds shared gate against manual and automatic overlap");
            pending.SetResult(Response()); await manual;
            good = service.Shipments.Single();
            store.Save(good with { LastSuccessfulRefresh = DateTimeOffset.UtcNow.AddHours(-4) });
            pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var automatic = scheduler.EvaluateAsync("timer"); requests = handler.Posts;
            check((await service.RefreshAsync()).Outcome == App.PackageOperation.Busy, "POST automatic holds gate against manual overlap");
            await scheduler.EvaluateAsync("resume"); check(handler.Posts == requests, "POST automatic cycles do not overlap");
            pending.SetResult(Response()); await automatic;
            handler.Reply = _ => Task.FromResult(Response());
            good = service.Shipments.Single();
            store.Save(good with { StatusMilestone = "delivered", LastSuccessfulRefresh = DateTimeOffset.UtcNow.AddDays(-2) });
            requests = handler.Posts; await service.RefreshAsync(); await scheduler.EvaluateAsync();
            check(handler.Posts == requests && handler.Gets == 0, "Delivered shipments skip both manual automatic POST and GET");
            store.Save(good with { TrackingNumber = "DE1234567890", LastSuccessfulRefresh = DateTimeOffset.UtcNow.AddDays(-2) });
            await service.RefreshAsync(); await scheduler.EvaluateAsync();
            check(handler.Posts == requests, "Recognized Amazon never reaches Ship24 even with legacy provider metadata");
            store.Save(good with { TrackingMode = "manual", Provider = "local", CarrierCode = "amazon-logistics" });
            await service.RefreshAsync(); check(handler.Posts == requests, "Local manual shipment never reaches Ship24");
            store.Save(good with { LastSuccessfulRefresh = DateTimeOffset.UtcNow.AddDays(-2) });
            scheduler.SetSuspended(true); await scheduler.EvaluateAsync(); check(handler.Posts == requests, "POST fullscreen defers due refresh");
            scheduler.SetSuspended(false); await scheduler.EvaluateAsync("resume"); check(handler.Posts == requests + 1, "POST fullscreen exit evaluates once");
            store.Save(service.Shipments.Single() with { LastSuccessfulRefresh = DateTimeOffset.UtcNow.AddDays(-2) });
            scheduler.Configure(false, 30); requests = handler.Posts; await scheduler.EvaluateAsync();
            check(handler.Posts == requests, "POST disabled automatic scheduling sends nothing");
            await service.RefreshAsync(); check(handler.Posts == requests + 1, "POST manual remains available when automatic disabled");
            check(!File.ReadAllText(App.AppLogger.LogFilePath).Contains(Secret), "POST diagnostics never expose API key");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(key, false); if (File.Exists(path)) File.Delete(path); }
    }
}
