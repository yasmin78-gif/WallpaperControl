extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using Microsoft.Win32;
using System.Net;
using System.Text;
using System.Text.Json;

internal static class Ship24Tests
{
    private const string Number = "SHIP24_SAMPLE_IN_TRANSIT_000";
    private const string Secret = "offline-ship24-secret-never-log";
    private const string Fixture = """
        {"data":{"trackings":[{"tracker":{"trackerId":"tracker-1","trackingNumber":"SHIP24_SAMPLE_IN_TRANSIT_000",
        "courierCode":["sample"],"createdAt":"2026-09-20T10:00:00Z"},
        "shipment":{"statusCategory":"transit","statusMilestone":"in_transit","future":123,
        "delivery":{"estimatedDeliveryDate":"2026-10-02T12:00:00Z","courierEstimatedDeliveryDate":{"from":"2026-10-01T14:00:00+02:00","to":null}}},
        "events":[
        {"eventId":"old","status":"Shipper created a label, we have not received the package yet.","occurrenceDatetime":"2026-09-28T15:00:00+02:00"},
        {"eventId":"latest","status":"The package has arrived at our warehouse.","occurrenceDatetime":"2026-09-28T12:00:00-04:00",
        "location":"Roissy-en-France, 95700 (FR)","statusCategory":"future-category","statusMilestone":"future-milestone","sourceCode":"courier","courierCode":"sample"},
        {"eventId":"middle","occurrenceDatetime":"2026-09-28T14:00:00Z"},
        {"eventId":"bad","occurrenceDatetime":"not-a-date"},
        {"eventId":"local","occurrenceDatetime":"2026-09-29T12:00:00"},
        {"eventId":"date","occurrenceDatetime":"2026-09-29","hasNoTime":true},
        {"status":null,"location":null}],
        "statistics":{"timestamps":{"deliveredDatetime":"2026-09-29T18:00:00+02:00"}},"future":{"ignored":true}}]}}
        """;

    private sealed class Handler : HttpMessageHandler
    {
        internal Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Reply = (_, _) => Task.FromResult(Response(Fixture));
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; return Reply(request, cancellationToken); }
    }
    private static HttpResponseMessage Response(string json, int status = 200) => new((HttpStatusCode)status)
        { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class SlowStream : MemoryStream
    {
        internal bool WasDisposed;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { await Task.Delay(Timeout.Infinite, cancellationToken); return 0; }
        protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
    }

    // Keep the existing synchronous UI test runner on its original thread/apartment.
    internal static void Run(Action<bool, string> check) => Task.Run(() => RunAsync(check)).GetAwaiter().GetResult();
    private static async Task RunAsync(Action<bool, string> check)
    {
        string registry = @"Software\WallpaperControl.Ship24Tests-" + Guid.NewGuid().ToString("N");
        string storage = Path.Combine(Path.GetTempPath(), "ship24-" + Guid.NewGuid().ToString("N") + ".json");
        var credentials = new App.TrackingCredentialStore(registry);
        using var handler = new Handler();
        using var client = new HttpClient(handler);
        using var provider = new App.Ship24TrackingProvider(credentials, client);
        try
        {
            await Error(() => provider.TestConnectionAsync(), App.TrackingProviderFailure.MissingCredential);
            check(handler.Calls == 0, "Ship24 missing credential sends no HTTP request");
            check(credentials.Save("ship24", new App.TrackingCredential(Secret)), "Ship24 uses isolated protected credential store");
            handler.Reply = async (request, token) =>
            {
                check(request.Method == HttpMethod.Post && request.RequestUri!.AbsoluteUri == "https://api.ship24.com/public/v1/trackers/track", "Ship24 create endpoint");
                check(request.Headers.Authorization?.Scheme == "Bearer" && request.Headers.Authorization.Parameter == Secret, "Ship24 bearer from credential store");
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                check(body.RootElement.GetProperty("trackingNumber").GetString() == Number
                    && body.RootElement.GetProperty("courierCode")[0].GetString() == "sample", "Ship24 create tracking number and optional carrier");
                return Response(Fixture);
            };
            var before = DateTimeOffset.UtcNow;
            var shipment = await provider.CreateTrackerAsync(Number, "sample");
            check(shipment.Provider == provider.ProviderId && shipment.ProviderTrackerId == "tracker-1" && shipment.TrackingNumber == Number, "Ship24 identity mapping");
            check(shipment.CarrierCode == "sample" && shipment.StatusCategory == "transit" && shipment.StatusMilestone == "in_transit", "Ship24 carrier and shipment status");
            check(shipment.Events.Count == 7 && shipment.LastRelevantEvent?.EventId == "latest", "Ship24 complete unsorted history uses actual instants");
            check(shipment.LastRelevantEvent?.OccurredAt == DateTimeOffset.Parse("2026-09-28T16:00:00Z")
                && shipment.LastRelevantEvent.OccurredAt.Value.Offset == TimeSpan.FromHours(-4), "Ship24 UTC offsets preserved without local timezone conversion");
            check(shipment.LastRelevantEvent?.Description == "The package has arrived at our warehouse."
                && shipment.LastRelevantEvent.Location == "Roissy-en-France, 95700 (FR)"
                && shipment.LastRelevantEvent.SourceCode == "courier", "Ship24 event text location and source");
            check(shipment.LastRelevantEvent?.StatusCategory == "future-category" && shipment.LastRelevantEvent.StatusMilestone == "future-milestone", "Ship24 unknown event statuses preserved");
            check(shipment.EstimatedDelivery == DateTimeOffset.Parse("2026-10-01T12:00:00Z"), "Ship24 prefers carrier ETA range");
            check(shipment.DeliveredAt == DateTimeOffset.Parse("2026-09-29T16:00:00Z"), "Ship24 delivered statistics mapping");
            check(shipment.LastSuccessfulRefresh >= before && shipment.CreatedAt == DateTimeOffset.Parse("2026-09-20T10:00:00Z"), "Ship24 successful refresh and creation timestamps");
            check(shipment.Events.Single(e => e.EventId == "local").OccurredAt == null
                && shipment.Events.Single(e => e.EventId == "local").RawOccurredAt == "2026-09-29T12:00:00"
                && shipment.Events.Single(e => e.EventId == "bad").OccurredAt == null, "Ship24 invalid and unknown-zone dates retained without fabricated instants");
            check(new App.TrackingStore(storage).Save(shipment)
                && new App.TrackingStore(storage).Shipments.Single().Events.Count == 7, "Ship24 nullable events and missing statuses survive storage");

            handler.Reply = (request, _) =>
            {
                check(request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/public/v1/trackers/tracker-1/results", "Ship24 get endpoint");
                return Task.FromResult(Response(Fixture));
            };
            var fetched = await provider.GetTrackingAsync("tracker-1");
            check(fetched.Events.Select(e => e.EventId).SequenceEqual(shipment.Events.Select(e => e.EventId)), "Ship24 repeat response has stable fallback event identities");
            handler.Reply = (request, _) =>
            {
                check(request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/public/v1/couriers" && request.Content == null,
                    "Ship24 connection check is authenticated read-only courier listing");
                return Task.FromResult(Response("{\"data\":{\"couriers\":[]}}"));
            };
            check(await provider.TestConnectionAsync(), "Ship24 connection success");
            check(client.DefaultRequestHeaders.Authorization == null, "Ship24 does not retain key in shared client defaults");

            async Task<App.TrackedShipment> Map(string json)
            { handler.Reply = (_, _) => Task.FromResult(Response(json)); return await provider.GetTrackingAsync("tracker-1"); }
            const string minimal = "{\"data\":{\"trackings\":[{\"tracker\":{\"trackerId\":\"tracker-1\",\"trackingNumber\":\"ABCDE\"}}]}}";
            var empty = await Map(minimal);
            check(empty.Events.Count == 0 && empty.LastRelevantEvent == null && empty.EstimatedDelivery == null
                && empty.DeliveredAt == null && empty.CarrierCode == null && empty.StatusCategory == null, "Ship24 missing optional values are null");
            var future = await Map(Fixture.Replace("\"transit\"", "\"future-shipment\"").Replace("\"in_transit\"", "\"future-step\""));
            check(future.StatusCategory == "future-shipment" && future.StatusMilestone == "future-step", "Ship24 future shipment statuses and additional JSON accepted");
            var fallback = await Map(Fixture.Replace("2026-10-01T14:00:00+02:00", "invalid"));
            check(fallback.EstimatedDelivery == DateTimeOffset.Parse("2026-10-02T12:00:00Z"), "Ship24 invalid carrier ETA falls back to general estimate");
            var legacy = await Map(Fixture.Replace("\"occurrenceDatetime\":\"2026-09-29T12:00:00\"",
                "\"datetime\":\"2026-09-29T12:00:00\",\"utcOffset\":\"+02:00\""));
            check(legacy.LastRelevantEvent?.EventId == "local" && legacy.LastRelevantEvent.OccurredAt == DateTimeOffset.Parse("2026-09-29T10:00:00Z"), "Ship24 legacy timestamp with explicit offset");
            var deliveryEvent = await Map(Fixture.Replace("2026-09-29T18:00:00+02:00", "invalid").Replace("future-milestone", "delivered"));
            check(deliveryEvent.DeliveredAt == deliveryEvent.LastRelevantEvent!.OccurredAt, "Ship24 derives delivery from dated delivered event");
            foreach (string invalid in new[] { "{", "null", "{}", "{\"data\":{\"trackings\":[]}}", minimal.Replace("tracker-1", "wrong") })
            {
                handler.Reply = (_, _) => Task.FromResult(Response(invalid));
                await Error(() => provider.GetTrackingAsync("tracker-1"), App.TrackingProviderFailure.InvalidResponse);
            }
            foreach (var (status, code, failure) in new[]
            {
                (401, "", App.TrackingProviderFailure.Unauthorized), (403, "", App.TrackingProviderFailure.Forbidden),
                (404, "", App.TrackingProviderFailure.TrackerNotFound), (429, "", App.TrackingProviderFailure.RateLimit),
                (500, "", App.TrackingProviderFailure.ServiceError), (503, "", App.TrackingProviderFailure.ServiceError),
                (402, "quota_limit_reached", App.TrackingProviderFailure.QuotaExhausted),
                (400, "validation_error", App.TrackingProviderFailure.InvalidTrackingNumber)
            })
            {
                handler.Reply = (_, _) => Task.FromResult(Response(JsonSerializer.Serialize(new { errors = new[] { new { code, message = Secret } } }), status));
                await Error(() => provider.GetTrackingAsync("tracker-1"), failure, status);
            }
            handler.Reply = (_, _) => throw new HttpRequestException(Secret);
            await Error(() => provider.GetTrackingAsync("tracker-1"), App.TrackingProviderFailure.NetworkError);
            handler.Reply = async (_, token) => { await Task.Delay(Timeout.Infinite, token); return Response("{}"); };
            using (var shortProvider = new App.Ship24TrackingProvider(credentials, client, TimeSpan.FromMilliseconds(30)))
                await Error(() => shortProvider.GetTrackingAsync("tracker-1"), App.TrackingProviderFailure.Timeout);
            using var slowBody = new SlowStream();
            handler.Reply = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(slowBody) });
            using (var shortProvider = new App.Ship24TrackingProvider(credentials, client, TimeSpan.FromMilliseconds(30)))
                await Error(() => shortProvider.GetTrackingAsync("tracker-1"), App.TrackingProviderFailure.Timeout);
            check(slowBody.WasDisposed, "Ship24 timeout includes response body and disposes response stream");
            handler.Reply = async (_, token) => { await Task.Delay(Timeout.Infinite, token); return Response("{}"); };
            using (var cancel = new CancellationTokenSource(30))
            {
                try { await provider.GetTrackingAsync("tracker-1", cancel.Token); check(false, "Ship24 cancellation must throw"); }
                catch (OperationCanceledException ex) { check(ex.CancellationToken == cancel.Token && !ex.ToString().Contains(Secret), "Ship24 cancellation propagated safely"); }
            }
            int calls = handler.Calls;
            await Error(() => provider.CreateTrackerAsync("!"), App.TrackingProviderFailure.InvalidTrackingNumber);
            check(handler.Calls == calls, "Ship24 invalid tracking number rejected before request");
            using (var canceled = new CancellationTokenSource())
            {
                canceled.Cancel();
                try { await provider.TestConnectionAsync(canceled.Token); check(false, "Ship24 pre-cancel must throw"); }
                catch (OperationCanceledException) { check(handler.Calls == calls, "Ship24 pre-cancel sends no request"); }
            }
            handler.Reply = (_, _) => Task.FromResult(Response("<html>" + Secret, 503));
            await Error(() => provider.GetTrackingAsync("tracker-1"), App.TrackingProviderFailure.ServiceError, 503);
            handler.Reply = (_, _) => Task.FromResult(Response("\"" + new string('x', 4 * 1024 * 1024) + "\""));
            await Error(() => provider.GetTrackingAsync("tracker-1"), App.TrackingProviderFailure.InvalidResponse, 200);
            check(credentials.Save("ship24", new App.TrackingCredential("invalid\r\n" + Secret)), "Ship24 stores synthetic invalid header credential for validation test");
            calls = handler.Calls;
            await Error(() => provider.TestConnectionAsync(), App.TrackingProviderFailure.InvalidCredential);
            check(handler.Calls == calls, "Ship24 rejects header injection before transport");
            string log = File.ReadAllText(App.AppLogger.LogFilePath);
            check(!log.Contains(Secret) && !log.Contains(Number), "Ship24 logs contain neither credentials nor full tracking number");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(registry, false);
            if (File.Exists(storage)) File.Delete(storage);
        }

        async Task Error(Func<Task> action, App.TrackingProviderFailure expected, int? status = null)
        {
            try { await action(); check(false, "Ship24 expected " + expected); }
            catch (App.TrackingProviderException ex)
            {
                check(ex.Failure == expected && (status == null || ex.HttpStatusCode == status), "Ship24 structured error " + expected);
                check(!ex.ToString().Contains(Secret) && ex.InnerException == null, "Ship24 exception excludes upstream secrets " + expected);
            }
        }
    }
}
