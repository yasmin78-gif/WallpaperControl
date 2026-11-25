using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Security.Cryptography.X509Certificates;
using RemoteExperiment;

internal static class Program
{
    static int passed;
    static readonly List<object> measurements = new();
    static void Check(bool ok, string title) { if (!ok) throw new Exception(title); passed++; Console.WriteLine("PASS " + title); }
    static async Task Reject(Func<Task> action, string title) { try { await action(); } catch { Check(true, title); return; } throw new Exception("Accepted: " + title); }
    [STAThread] static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var form = new Form { ShowInTaskbar = false, WindowState = FormWindowState.Minimized };
        form.Shown += async (_, _) => { try { if (args.Contains("--dns-loopback")) await ProbeDns(); else { await Run(form); Console.WriteLine($"RESULT {passed} passed"); } } catch (Exception ex) { Console.WriteLine("FAIL " + ex); Environment.ExitCode = 1; } finally { form.Close(); } };
        Application.Run(form);
    }
    static Task<object?> Dispatch(Form form, Func<object?> operation)
    {
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        form.BeginInvoke(() => { try { completion.SetResult(operation()); } catch (Exception ex) { completion.SetException(ex); } }); return completion.Task;
    }
    static async Task Sample(string name, Func<Task>? workload = null)
    {
        GC.Collect(); using var process = Process.GetCurrentProcess(); var cpu = process.TotalProcessorTime; var watch = Stopwatch.StartNew();
        if (workload == null) await Task.Delay(3000); else await workload();
        process.Refresh(); var seconds = watch.Elapsed.TotalSeconds;
        measurements.Add(new { scenario = name, seconds, privateBytes = process.PrivateMemorySize64, workingSet = process.WorkingSet64, threads = process.Threads.Count, handles = process.HandleCount, cpuOneCorePercent = (process.TotalProcessorTime - cpu).TotalSeconds / seconds * 100 });
    }
    static SavedConnection Saved(RemoteHost host, X509Certificate2 cert, Credential credential, string? pin = null) => new(new(1, $"https://127.0.0.1:{host.Port}/", host.Model.State.ServerId, pin ?? Trust.Pin(cert), "", ""), credential);
    static async Task Run(Form form)
    {
        using var cert = Trust.CreateCertificate(); using var foreign = Trust.CreateCertificate(); using var expired = Trust.CreateCertificate(true);
        var pendingJson = "{\"claim\":\"test-claim\",\"code\":\"123456\"}";
        Check(JsonSerializer.Deserialize(pendingJson, WireJson.Type<PairPending>()) == new PairPending("test-claim", "123456"), "Android wire metadata reads camelCase pairing response");
        var saved = new SavedConnection(new(1, "https://127.0.0.1/", "server", "pin", "", ""), new("device", "test-token", false));
        Check(JsonSerializer.Deserialize(JsonSerializer.Serialize(saved), WireJson.Type<SavedConnection>()) == saved, "wire metadata reads previous PascalCase secure-storage data");
        Check(JsonSerializer.Deserialize(JsonSerializer.Serialize(saved, WireJson.Type<SavedConnection>()), WireJson.Type<SavedConnection>()) == saved, "wire metadata secure-storage roundtrip");
        Check(Trust.Validate(cert, Trust.Pin(cert)), "certificate SPKI/EKU/time valid");
        Check(!Trust.Validate(foreign, Trust.Pin(cert)), "foreign certificate policy");
        Check(!Trust.Validate(expired, Trust.Pin(expired)), "expired even with matching pin");
        foreach (var address in new[] { "http://127.0.0.1/", "https://example.com/", "https://8.8.8.8/", "https://user@127.0.0.1/", "https://127.0.0.1/?token=x" })
        { bool rejected = false; try { Trust.Endpoint(address); } catch (ArgumentException) { rejected = true; } Check(rejected, "reject unsafe endpoint " + address.Split('?')[0]); }
        long ticks = 0; using var timer = new System.Windows.Forms.Timer { Interval = 20 }; timer.Tick += (_, _) => ticks++; timer.Start();
        await Sample("baseline-winforms");
        var model = new StateModel("test-server");
        await using var host = new RemoteHost(model, operation => Dispatch(form, operation));
        var watch = Stopwatch.StartNew(); await host.StartAsync(IPAddress.Loopback, 0, cert); Check(host.Port > 0, "HTTPS startup explicit loopback");
        Console.WriteLine("HOST_START_MS " + watch.Elapsed.TotalMilliseconds);
        await Sample("kestrel-no-client");
        var token = new Credential("client-1", Trust.Secret(), false); host.Pairing.AddTestDevice(token);
        await using var client = new RemoteClient(Saved(host, cert, token)); await client.StartAsync(); Check(client.Connected, "REST + WSS correct pin authenticated");
        using (var anonymous = new HttpClient(Trust.Handler(Trust.Pin(cert)))) Check((await anonymous.GetAsync($"https://127.0.0.1:{host.Port}/api/v1/state")).StatusCode == HttpStatusCode.Unauthorized, "REST anonymous rejected");
        await using (var anonymous = new RemoteClient(Saved(host, cert, token with { Token = "wrong" }))) await Reject(() => anonymous.StartAsync(), "WSS invalid auth rejected");
        var result = await client.SendAsync("visitor", "true"); Check(result.Status == "completed", "client -> synthetic visitor");
        Check((await client.SendAsync("mode", "video")).Error == "visitorProtected", "normal mode blocked during visitor");
        Check((await client.SendAsync("visitor", "false")).Error == "visitorPermission", "disable permission defaults off");
        await Dispatch(form, () => model.Execute(new("desktop", model.State.Epoch, model.State.Revision, "visitor", "false"), "desktop", true));
        await Until(() => client.State?.Visitor == false); Check(client.State?.Visitor == false, "desktop -> WSS full state");
        var before = client.State!; var command = new Command("same-command", before.Epoch, before.Revision, "next", "");
        var first = await client.SendRawAsync(command); var duplicate = await client.SendRawAsync(command);
        Check(first == duplicate && duplicate.State.SyntheticAdvances == before.SyntheticAdvances + 1, "duplicate command exactly one effect in cache");
        Check((await client.SendRawAsync(command with { Value = "different" })).Error == "idReuse", "command ID payload conflict");
        Check((await client.SendRawAsync(command with { Id = "stale" })).Error == "stale", "stale revision rejected");
        Check((await client.SendRawAsync(command with { Id = "epoch", Epoch = "old" })).Error == "stale", "old epoch rejected");
        await Sample("one-client");
        var clients = new List<RemoteClient>();
        try
        {
            for (int i = 2; i <= 5; i++) { var device = new Credential("client-" + i, Trust.Secret(), true); host.Pairing.AddTestDevice(device); var c = new RemoteClient(Saved(host, cert, device)); await c.StartAsync(); clients.Add(c); }
            await Sample("five-clients");
            var revision = model.State.Revision;
            var simultaneous = await Task.WhenAll(clients.Select((c, i) => c.SendRawAsync(new("race-" + i, model.State.Epoch, revision, "shuffle", "true"))));
            Check(simultaneous.Count(r => r.Status == "completed") == 1, "concurrent clients revision serialized on UI");
            long tickBefore = ticks;
            await Sample("five-clients-500-changes", async () => { for (int i = 0; i < 500; i++) await Dispatch(form, () => model.Execute(new("burst-" + i, model.State.Epoch, model.State.Revision, "layout", i % 2 == 0 ? "fit" : "fill"), "desktop", true)); await Task.Delay(1000); });
            await Until(() => clients.All(c => c.State?.Revision == model.State.Revision)); Check(ticks > tickBefore + 10, "UI timer remains responsive during burst"); Check(clients.All(c => c.State?.Revision == model.State.Revision), "five clients receive latest full revision");
        }
        finally { foreach (var c in clients) await c.DisposeAsync(); }
        using (var occupied = new TcpListener(IPAddress.Loopback, 0))
        {
            occupied.Start(); int port = ((IPEndPoint)occupied.LocalEndpoint).Port;
            await using var conflict = new RemoteHost(new("conflict"), op => Dispatch(form, op)); await Reject(() => conflict.StartAsync(IPAddress.Loopback, port, cert), "occupied port fails cleanly");
        }
        using (var raw = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(2) }) await Reject(async () => { var response = await raw.GetAsync($"http://127.0.0.1:{host.Port}/api/v1/state"); response.EnsureSuccessStatusCode(); }, "no plaintext HTTP fallback");
        await CheckTls(form, cert, Trust.Pin(foreign), "wrong pin");
        await CheckTls(form, foreign, Trust.Pin(cert), "foreign certificate");
        await CheckTls(form, expired, Trust.Pin(expired), "expired certificate");
        await CheckPairing(host, cert);
        CheckPairingClock();
        await CheckSlowReader(form, host, cert, token);
        // Transport interruption/reconnect with the same port, new epoch and existing test credential.
        int restartPort;
        var oldModel = new StateModel("restart-server"); var live = new RemoteHost(oldModel, op => Dispatch(form, op)); await live.StartAsync(IPAddress.Loopback, 0, cert); restartPort = live.Port; live.Pairing.AddTestDevice(token);
        await using var reconnect = new RemoteClient(Saved(live, cert, token)); await reconnect.StartAsync(); string epoch = reconnect.State!.Epoch;
        await live.DisposeAsync(); await Until(() => !reconnect.Connected); Check(!reconnect.Connected, "disconnect disables controls state");
        await using var restarted = new RemoteHost(new("restart-server"), op => Dispatch(form, op)); restarted.Pairing.AddTestDevice(token); await restarted.StartAsync(IPAddress.Loopback, restartPort, cert);
        await Until(() => reconnect.Connected && reconnect.State?.Epoch != epoch, 12000); Check(reconnect.Connected && reconnect.State?.Epoch != epoch, "automatic WSS reconnect and REST full resync new epoch");
        await Sample("interrupted-and-reconnected");
        // Binding released after shutdown, including IPv6 loopback.
        await using (var ipv6 = new RemoteHost(new("ipv6"), op => Dispatch(form, op)))
        {
            await ipv6.StartAsync(IPAddress.IPv6Loopback, 0, cert); Check(ipv6.Port > 0, "IPv6 explicit HTTPS listener"); ipv6.Pairing.AddTestDevice(token);
            var configuration = Saved(ipv6, cert, token); configuration = configuration with { Server = configuration.Server with { Address = $"https://[::1]:{ipv6.Port}/" } };
            await using var v6client = new RemoteClient(configuration); await v6client.StartAsync(); Check(v6client.Connected && v6client.State?.ServerId == "ipv6", "IPv6 REST and WSS authenticated roundtrip");
        }
        measurements.Add(new { scenario = "application-network-counters-main-host", host.AuthenticatedRequests, host.PublishedSnapshots, host.PublishedJsonBytes, note = "Application JSON bytes per publication, not TLS wire bytes or per-client total" });
        string output = Path.Combine(AppContext.BaseDirectory, "measurements.json"); File.WriteAllText(output, JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true })); Console.WriteLine("METRICS " + output);
    }
    static async Task ProbeDns()
    {
        var discovery = new WindowsDiscovery();
        var result = discovery.StartAsync(IPAddress.Loopback, (uint)System.Net.NetworkInformation.NetworkInterface.LoopbackInterfaceIndex, 47831, Guid.NewGuid().ToString("N"));
        if (await Task.WhenAny(result, Task.Delay(10000)) != result) { Console.WriteLine("DNS_LOOPBACK_TIMEOUT: process exits without freeing pending native state"); Environment.ExitCode = 2; return; }
        Console.WriteLine("DNS_LOOPBACK_STATUS " + await result); await discovery.DisposeAsync();
    }
    static async Task Until(Func<bool> condition, int milliseconds = 5000) { var clock = Stopwatch.StartNew(); while (!condition() && clock.ElapsedMilliseconds < milliseconds) await Task.Delay(30); }
    static async Task CheckTls(Form form, X509Certificate2 certificate, string pin, string label)
    {
        await using var host = new RemoteHost(new("tls-server"), op => Dispatch(form, op)); await host.StartAsync(IPAddress.Loopback, 0, certificate);
        var device = new Credential("tls-client", Trust.Secret(), false); host.Pairing.AddTestDevice(device);
        using (var rest = new HttpClient(Trust.Handler(pin))) { rest.DefaultRequestHeaders.Authorization = new("Bearer", device.Token); await Reject(async () => { using var response = await rest.GetAsync($"https://127.0.0.1:{host.Port}/api/v1/state"); response.EnsureSuccessStatusCode(); }, "REST rejects " + label); }
        await using var client = new RemoteClient(Saved(host, certificate, device, pin)); await Reject(() => client.ProbeWssAsync(), "WSS rejects " + label);
        Check(host.AuthenticatedRequests == 0, "credentials never reach HTTP for " + label);
    }
    static async Task CheckPairing(RemoteHost host, X509Certificate2 certificate)
    {
        var session = Guid.NewGuid().ToString("N"); var secret = Trust.Secret(); host.Pairing.Begin(session, secret);
        using var http = new HttpClient(Trust.Handler(Trust.Pin(certificate))) { BaseAddress = new($"https://127.0.0.1:{host.Port}/") };
        using var response = await http.PostAsJsonAsync("api/v1/pairing/requests", new PairRequest(session, secret, "test phone"));
        var pending = (await response.Content.ReadFromJsonAsync(WireJson.Type<PairPending>()))!; Check(response.IsSuccessStatusCode && pending.Code.Length == 6, "pairing request comparison code with Android wire metadata");
        using var replay = await http.PostAsJsonAsync("api/v1/pairing/requests", new PairRequest(session, secret, "replay")); Check(replay.StatusCode == HttpStatusCode.Forbidden, "QR secret one-time reservation");
        using var early = await http.PostAsJsonAsync("api/v1/pairing/claim", new ClaimRequest(pending.Claim)); Check(early.StatusCode == HttpStatusCode.Conflict, "no credentials before local approval");
        host.Pairing.Confirm(false);
        using var granted = await http.PostAsJsonAsync("api/v1/pairing/claim", new ClaimRequest(pending.Claim), WireJson.Type<ClaimRequest>()); var credential = (await granted.Content.ReadFromJsonAsync(WireJson.Type<Credential>()))!;
        Check(!credential.CanDisableVisitor && credential.Token.Length == 64, "individual credential default disable permission false");
        using var retried = await http.PostAsJsonAsync("api/v1/pairing/claim", new ClaimRequest(pending.Claim)); Check(await retried.Content.ReadFromJsonAsync<Credential>() == credential, "claim retry does not issue second device");
        using var query = await http.GetAsync("api/v1/state?access_token=deliberately-not-a-real-token"); Check(query.StatusCode == HttpStatusCode.BadRequest, "query token rejected");
        for (int i = 0; i < 5; i++) { using var flood = await http.PostAsJsonAsync("api/v1/pairing/requests", new PairRequest(session, secret, "flood")); if (i == 4) Check((int)flood.StatusCode == 429, "pairing flood bounded before approval UI"); }
        using var browserRequest = new HttpRequestMessage(HttpMethod.Get, "api/v1/state"); browserRequest.Headers.Add("Origin", "https://untrusted.example"); using var browser = await http.SendAsync(browserRequest); Check(browser.StatusCode == HttpStatusCode.Forbidden, "browser origin denied");
    }
    sealed class FakeTime : TimeProvider
    {
        public long Milliseconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Milliseconds;
    }
    static void CheckPairingClock()
    {
        var time = new FakeTime(); var pairing = new Pairing { Clock = time }; string secret = Trust.Secret(); pairing.Begin("session", secret);
        time.Milliseconds = 299999; Check(pairing.Request(new("session", secret, "phone")) != null, "QR valid before five minutes");
        pairing.Begin("session", secret); time.Milliseconds += 300000; Check(pairing.Request(new("session", secret, "phone")) == null, "QR expired exactly at five minutes");
        Check(new Pairing().Request(new("session", secret, "phone")) == null, "QR invalid after server restart");
        time.Milliseconds = 0; pairing.Begin("session", secret); var pending = pairing.Request(new("session", secret, "phone"))!; pairing.Confirm(true); var device = pairing.Claim(pending.Claim)!;
        Check(device.CanDisableVisitor, "explicit per-device disable permission");
        var restored = new Pairing(); restored.ImportDevices(pairing.ExportDevices()); Check(restored.Authenticate(device.Token)?.Device == device.DeviceId, "hashed device credentials survive isolated store reload");
        time.Milliseconds = 300000; Check(pairing.Claim(pending.Claim) == null, "claim expires with original QR deadline");
        time.Milliseconds = 0; pairing.Begin("old", secret); pairing.Begin("new", secret); Check(pairing.Request(new("old", secret, "phone")) == null, "new QR invalidates prior session");
        Check(pairing.Request(new("new", "incorrect", "phone")) == null, "wrong QR secret rejected");
        var requests = Enumerable.Range(0, 8).Select(i => Task.Run(() => pairing.Request(new("new", secret, "phone")))).ToArray();
        Task.WaitAll(requests); Check(requests.Count(r => r.Result != null) == 1, "concurrent QR reservation has one winner");
        var qr = new PairQr(1, "https://127.0.0.1:47831/", "id", "pin", "session", "secret");
        using var generator = new QRCoder.QRCodeGenerator(); using var data = generator.CreateQrCode(JsonSerializer.Serialize(qr), QRCoder.QRCodeGenerator.ECCLevel.M); using var png = new QRCoder.PngByteQRCode(data);
        Check(png.GetGraphic(4).AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "QR generated locally as PNG");
        using var stream = new MemoryStream(png.GetGraphic(4)); using var bitmap = new Bitmap(stream);
        var rgb = new byte[bitmap.Width * bitmap.Height * 3];
        for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++) { var pixel = bitmap.GetPixel(x, y); int offset = (y * bitmap.Width + x) * 3; rgb[offset] = pixel.R; rgb[offset + 1] = pixel.G; rgb[offset + 2] = pixel.B; }
        var decoded = new ZXing.BarcodeReaderGeneric().Decode(new ZXing.RGBLuminanceSource(rgb, bitmap.Width, bitmap.Height, ZXing.RGBLuminanceSource.BitmapFormat.RGB24));
        Check(decoded?.Text == JsonSerializer.Serialize(qr), "QRCoder -> ZXing local QR roundtrip (not camera)");
    }
    static async Task CheckSlowReader(Form form, RemoteHost host, X509Certificate2 certificate, Credential credential)
    {
        using var socket = new System.Net.WebSockets.ClientWebSocket(); using var transport = new HttpMessageInvoker(Trust.Handler(Trust.Pin(certificate)));
        socket.Options.SetRequestHeader("Authorization", "Bearer " + credential.Token);
        await socket.ConnectAsync(new Uri($"wss://127.0.0.1:{host.Port}/api/v1/events"), transport, CancellationToken.None);
        await socket.SendAsync(System.Text.Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e"), System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
        // Intentionally never read this peer; publisher has bounded channel and transport buffers.
        await Sample("slow-nonreading-peer-3000-changes", async () => { for (int i = 0; i < 3000; i++) await Dispatch(form, () => host.Model.Execute(new("slow-" + i, host.Model.State.Epoch, host.Model.State.Revision, "shuffle", (i % 2 == 0).ToString()), "desktop", true)); await Task.Delay(1000); });
        Check(form.IsHandleCreated, "nonreading peer does not block UI dispatch"); socket.Abort();
    }
}
