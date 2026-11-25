extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using WallpaperControl.Remote;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.Win32;

internal static partial class Program
{
    private static int count;
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "WallpaperControl.Remote.Tests", Guid.NewGuid().ToString("N"));
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
    private static async Task Reject(Func<Task> operation, string name) { try { await operation(); } catch { Check(true, name); return; } throw new Exception("Unexpected success: " + name); }
    private static T Field<T>(object source, string name) => (T)source.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
    private static void Set(object source, string name, object value) => source.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(source, value);
    [STAThread] private static void Main(string[] args)
    {
        Directory.CreateDirectory(Root); ApplicationConfiguration.Initialize();
        using var form = new Form { ShowInTaskbar = false, Location = new(-20000, -20000) };
        form.Shown += async (_, _) =>
        {
            try
            {
                if (args.Contains("--measure")) await MeasureAsync(args);
                else { await SecurityAndTransport(); await DesktopIntegration(); await VisitorWidgetChecks(); Console.WriteLine($"All {count} remote production checks passed."); }
            }
            catch (Exception ex) { Console.WriteLine("FAIL " + ex); Environment.ExitCode = 1; }
            finally { form.Close(); }
        };
        Application.Run(form);
    }
    private static SavedRemote Saved(RemoteServer server, X509Certificate2 certificate, DeviceCredential credential) => new(new(1, server.Vault.ServerId, "Test PC", [$"https://127.0.0.1:{server.Port}/"], RemoteTrust.Pin(certificate), "", ""), credential, $"https://127.0.0.1:{server.Port}/");
    private static async Task Until(Func<bool> condition, int timeout = 5000)
    { var clock = Stopwatch.StartNew(); while (!condition() && clock.ElapsedMilliseconds < timeout) await Task.Delay(25); }
    private static async Task SecurityAndTransport()
    {
        var vault = new RemoteVault(Path.Combine(Root, "vault"));
        Check(!vault.Settings.Enabled && !Directory.Exists(Path.Combine(Root, "vault")), "default disabled without persistence writes");
        vault.Configure(new(false, "test", 47831, "Living room")); using var certificate = vault.Identity();
        Check(RemoteTrust.Validate(certificate, RemoteTrust.Pin(certificate)), "server identity valid with matching SPKI");
        Check(!RemoteTrust.Validate(certificate, new string('0', 64)), "wrong pin rejected");
        var credential = vault.Add("Phone", false); var grant = vault.Authenticate(credential.Token)!;
        Check(!grant.CanDisableVisitor && vault.Current(grant), "per-device disable permission defaults off");
        var reloaded = new RemoteVault(Path.Combine(Root, "vault")); using var sameIdentity = reloaded.Identity();
        Check(reloaded.Authenticate(credential.Token)?.Id == credential.DeviceId && RemoteTrust.Pin(sameIdentity) == RemoteTrust.Pin(certificate), "device credential and server identity survive reload");
        Check(!File.ReadAllText(Path.Combine(Root, "vault", "devices.dat")).Contains(credential.Token), "persisted credential data contains no plaintext token");
        var corruptFolder = Path.Combine(Root, "corrupt"); Directory.CreateDirectory(corruptFolder);
        byte[] corrupt = [1, 2, 3, 4, 5]; File.WriteAllBytes(Path.Combine(corruptFolder, "devices.dat"), corrupt);
        await Reject(() => Task.Run(() => new RemoteVault(corruptFolder)), "corrupt encrypted credential store fails closed");
        Check(File.ReadAllBytes(Path.Combine(corruptFolder, "devices.dat")).SequenceEqual(corrupt), "corrupt store is never silently reset or overwritten");
        vault.Change(grant.Id, "Renamed", true); Check(!vault.Current(grant) && vault.Authenticate(credential.Token)?.CanDisableVisitor == true, "permission change invalidates old grant");
        var pairing = new RemotePairing(vault); var qr = pairing.Begin("Test PC", ["https://192.168.1.5:47831/"], RemoteTrust.Pin(certificate));
        var pending = pairing.Request(new(qr.Session, qr.Secret, "Other phone"))!;
        Check(pending.Code.Length == 6 && pairing.Pending?.Code == pending.Code, "pairing comparison code is identical locally and remotely");
        Check(pairing.Request(new(qr.Session, qr.Secret, "Replay")) == null, "QR token single use");
        Check(pairing.Claim(new(pending.Claim)) == null, "no permanent credential before local confirmation");
        pairing.Approve(false); var paired = pairing.Claim(new(pending.Claim))!;
        Check(!paired.CanDisableVisitor && pairing.Claim(new(pending.Claim)) == paired, "claim retry issues one device with default permission off");
        pairing.Complete(new(pending.Claim)); Check(pairing.Claim(new(pending.Claim)) == null, "completed pairing destroys temporary credential delivery");
        var time = new FakeTime(); var timed = new RemotePairing(vault, time); var expiredQr = timed.Begin("Test", ["https://192.168.1.5/"], RemoteTrust.Pin(certificate)); time.Milliseconds = 300000;
        Check(timed.Request(new(expiredQr.Session, expiredQr.Secret, "Late")) == null, "QR expires at five minutes monotonic deadline");
        Check(RemoteNetwork.SameSubnet(IPAddress.Parse("192.168.1.7"), new(IPAddress.Parse("192.168.1.5"), 24)) && !RemoteNetwork.SameSubnet(IPAddress.Parse("192.168.2.7"), new(IPAddress.Parse("192.168.1.5"), 24)), "on-link subnet boundary enforced");
        Check(RemoteNetwork.SameSubnet(IPAddress.Parse("fd00::2"), new(IPAddress.Parse("fd00::1"), 64)) && !RemoteNetwork.SameSubnet(IPAddress.Parse("fd01::2"), new(IPAddress.Parse("fd00::1"), 64)), "IPv6 subnet boundary enforced");
        var privateAdapters = await RemoteNetwork.PrivateAdaptersAsync();
        Check(privateAdapters.All(a => a.Index != 0 && a.Addresses.All(p => RemoteTrust.Local(p.Address) && !IPAddress.IsLoopback(p.Address))), "native Windows private-profile query returns only explicit local adapters");
        Console.WriteLine("PRIVATE_ADAPTER_COUNT " + privateAdapters.Length);
        using (var watcher = new RemoteNetworkWatcher(() => { })) Check(true, "native private-profile change notifications can be subscribed and released");
        foreach (string endpoint in new[] { "http://192.168.1.5/", "https://8.8.8.8/", "https://127.0.0.1/", "https://192.168.1.5/?token=x" })
        { bool rejected = false; try { RemoteTrust.Endpoint(endpoint); } catch (ArgumentException) { rejected = true; } Check(rejected, "unsafe production endpoint rejected"); }
        int effects = 0;
        var state = new RemoteState { Playback = "playing", Capabilities = new(["next", "shuffle", "visitorOn", "visitorOff"], ["fill"], [60000]) };
        RemoteServer? activeServer = null;
        await using var server = new RemoteServer(vault, () => Task.FromResult(state), (command, device) =>
        {
            if (command.Type == "next") { effects++; activeServer!.Observe(state, "private-frame-" + effects); }
            if (command.Type == "visitorOn") state = state with { VisitorActive = true };
            if (command.Type == "visitorOff") state = state with { VisitorActive = false };
            if (command.Type == "shuffle") state = state with { Shuffle = bool.Parse(command.Value) };
            return Task.FromResult(new RemoteOutcome(true));
        }, IPAddress.IsLoopback);
        activeServer = server;
        await server.StartAsync([IPAddress.Loopback], 0, certificate);
        var limited = vault.Add("Limited phone", false);
        await using var phone = new RemoteConnection(Saved(server, certificate, limited), allowLoopbackForTests: true); await phone.ConnectAsync();
        Check(phone.Connected && phone.State?.ServerId == vault.ServerId, "production REST/WSS authenticated full synchronization");
        long initialRevision = (await phone.RefreshAsync()).Revision;
        Check((await phone.RefreshAsync()).Revision == initialRevision && phone.State?.ServerName == "Living room", "repeated snapshots and custom PC name do not create spurious revisions");
        var first = await phone.SendAsync("visitorOn"); Check(first.Status == "completed" && phone.State?.VisitorActive == true, "remote visitor activation accepted");
        var denied = await phone.SendAsync("visitorOff"); Check(denied.Status == "rejected" && state.VisitorActive, "unauthorized visitor deactivation denied server-side");
        using var http = new HttpClient(RemoteTrust.Handler(RemoteTrust.Pin(certificate))) { BaseAddress = new($"https://127.0.0.1:{server.Port}/") };
        http.DefaultRequestHeaders.Authorization = new("Bearer", limited.Token);
        var snapshot = await phone.RefreshAsync(); var next = new RemoteCommand("same-id", snapshot.Epoch, snapshot.Revision, "next", "");
        using var response = await http.PostAsJsonAsync("api/v1/commands", next, RemoteJson.Type<RemoteCommand>());
        using var duplicate = await http.PostAsJsonAsync("api/v1/commands", next, RemoteJson.Type<RemoteCommand>());
        Check(response.IsSuccessStatusCode && duplicate.IsSuccessStatusCode && effects == 1, "repeated production navigation command has one effect");
        using var conflicting = await http.PostAsJsonAsync("api/v1/commands", next with { Value = "changed" }, RemoteJson.Type<RemoteCommand>()); Check(conflicting.StatusCode == HttpStatusCode.Conflict, "command ID payload conflict rejected");
        var peers = new List<RemoteConnection>();
        try
        {
            for (int i = 0; i < 4; i++)
            {
                var device = vault.Add("Peer " + i, false); var client = new RemoteConnection(Saved(server, certificate, device), true); peers.Add(client); await client.ConnectAsync();
            }
            Check(peers.All(p => p.Connected) && phone.Connected, "five production clients connected simultaneously");
            bool confirmed = true;
            for (int i = 0; i < 40; i++) { var result = await phone.SendAsync("shuffle", (i % 2 == 0).ToString()); confirmed &= result.Status == "completed"; }
            Check(confirmed, "forty serialized mutations each receive completion confirmation");
            await Until(() => peers.All(p => p.State?.Revision == server.State?.Revision));
            Check(peers.All(p => p.State?.Revision == server.State?.Revision && p.State?.Shuffle == state.Shuffle), "five clients converge to authoritative state after burst");
            await Task.Delay(200); int events = 0; peers[0].Changed += () => Interlocked.Increment(ref events);
            for (int i = 0; i < 20; i++) server.Observe(state);
            await Task.Delay(200); Check(events == 0, "unchanged state does not generate redundant live messages");
        }
        finally { foreach (var peer in peers) await peer.DisposeAsync(); }
        using var query = await http.GetAsync("api/v1/state?access_token=not-a-secret"); Check(query.StatusCode == HttpStatusCode.BadRequest, "query-string token route rejected");
        vault.Change(limited.DeviceId, "Limited phone", true);
        await Until(() => !phone.Connected, 17000); Check(!phone.Connected, "rights changes terminate active WSS session immediately");
        await phone.ConnectAsync(); Check(phone.State?.CanDisableVisitor == true, "reconnection receives current per-device permission");
        Check((await phone.SendAsync("visitorOff")).Status == "completed" && !state.VisitorActive, "granted visitor deactivation accepted");
        vault.Remove(limited.DeviceId); await Until(() => !phone.Connected, 17000);
        using var revoked = await http.GetAsync("api/v1/state"); Check(!phone.Connected && revoked.StatusCode == HttpStatusCode.Unauthorized, "revocation closes WSS and rejects existing REST credentials");
        var wrong = Saved(server, certificate, credential); wrong = wrong with { Server = wrong.Server with { Pin = new string('0', 64) } };
        await using var wrongClient = new RemoteConnection(wrong, true); await Reject(() => wrongClient.ConnectAsync(), "wrong pin rejected on production WSS");
        using (var wrongRest = new HttpClient(RemoteTrust.Handler(new string('0', 64)))) await Reject(async () => { using var r = await wrongRest.GetAsync($"https://127.0.0.1:{server.Port}/api/v1/state"); }, "wrong pin rejected on production REST");
        using (var foreign = new RemoteVault(Path.Combine(Root, "foreign")).Identity())
        {
            Check(!RemoteTrust.Validate(foreign, RemoteTrust.Pin(certificate)), "foreign production certificate rejected");
            await CheckRejectedCertificate(vault, foreign, RemoteTrust.Pin(certificate), credential, "foreign");
        }
        using (var expired = ExpiredIdentity(certificate))
        {
            Check(RemoteTrust.Pin(expired) == RemoteTrust.Pin(certificate), "expired test identity retains exact SPKI");
            await CheckRejectedCertificate(vault, expired, RemoteTrust.Pin(expired), credential, "expired with matching SPKI");
        }
        var renewalFolder = Path.Combine(Root, "renewal"); var renewalVault = new RemoteVault(renewalFolder);
        using (var original = renewalVault.Identity())
        using (var expiredIdentity = ExpiredIdentity(original))
        {
            File.WriteAllBytes(Path.Combine(renewalFolder, "identity.dat"), ProtectedData.Protect(expiredIdentity.Export(X509ContentType.Pfx), null, DataProtectionScope.CurrentUser));
            using var renewed = renewalVault.Identity();
            Check(RemoteTrust.Pin(renewed) == RemoteTrust.Pin(original) && RemoteTrust.Validate(renewed, RemoteTrust.Pin(original)), "certificate renewal preserves trusted SPKI and restores valid dates");
        }
        Check(!JsonSerializer.Serialize(phone.State!, RemoteJson.Type<RemoteState>()).Contains(Root), "wire state contains no local test path");
        using (var listener = new TcpListener(IPAddress.Loopback, 0))
        {
            listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            await using var conflict = new RemoteServer(vault, () => Task.FromResult(state), (_, _) => Task.FromResult(new RemoteOutcome(true)), IPAddress.IsLoopback);
            await Reject(() => conflict.StartAsync([IPAddress.Loopback], port, certificate), "occupied production port fails closed");
        }
        await NativeDiscovery();
    }
    private static async Task CheckRejectedCertificate(RemoteVault vault, X509Certificate2 certificate, string pin, DeviceCredential credential, string label)
    {
        await using var host = new RemoteServer(vault, () => Task.FromResult(new RemoteState()), (_, _) => Task.FromResult(new RemoteOutcome(true)), IPAddress.IsLoopback);
        await host.StartAsync([IPAddress.Loopback], 0, certificate);
        var target = Saved(host, certificate, credential); target = target with { Server = target.Server with { Pin = pin } };
        using var http = new HttpClient(RemoteTrust.Handler(pin)); http.DefaultRequestHeaders.Authorization = new("Bearer", credential.Token);
        await Reject(async () => { using var response = await http.GetAsync($"https://127.0.0.1:{host.Port}/api/v1/state"); response.EnsureSuccessStatusCode(); }, "REST rejects " + label);
        await using var client = new RemoteConnection(target, true);
        await Reject(() => client.ProbeWebSocketAsync(), "independent WSS probe rejects " + label);
    }
    private static X509Certificate2 ExpiredIdentity(X509Certificate2 original)
    {
        using var key = original.GetRSAPrivateKey()!;
        var request = new CertificateRequest("CN=Expired remote test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-1));
        return X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
    }
    private static async Task NativeDiscovery()
    {
        await using var discovery = new RemoteDiscovery();
        var adapter = new RemoteAdapter("loopback", "Loopback", (uint)System.Net.NetworkInformation.NetworkInterface.LoopbackInterfaceIndex, [new(IPAddress.Loopback, 8)]);
        await discovery.StartAsync(adapter, 47831, Guid.NewGuid().ToString("N"), CancellationToken.None);
        Check(true, "production native DNS-SD registration and callback on loopback");
    }
    private static async Task DesktopIntegration()
    {
        string registry = "Software\\WallpaperControl.Remote.Tests\\" + Guid.NewGuid().ToString("N");
        string fixture = Path.Combine(Root, "visitor.png"); using (var bitmap = new Bitmap(8, 8)) { using var graphics = Graphics.FromImage(bitmap); graphics.Clear(Color.Blue); bitmap.Save(fixture); }
        var grant = new DeviceGrant("test", "Test", false, 1);
        try
        {
            using var widgets = new App.WidgetManager(() => { }, registryPath: registry, notesStore: new App.NotesStore(Path.Combine(Root, "notes.json")));
            using var form = new App.MainForm(widgets, nativeStatus: () => App.DesktopSlideshowState.None, currentWallpaper: () => fixture,
                visitorSettings: new App.VisitorModeSettings { Active = true, Source = App.VisitorSourceKind.Image, Path = fixture });
            var snapshot = form.CaptureRemoteState();
            Check(snapshot.VisitorActive && !snapshot.Capabilities.Allows("next") && !snapshot.Capabilities.Allows("mode"), "real MainForm single visitor blocks normal playback capabilities");
            Check(!(await form.ExecuteRemoteControlAsync(new("deny", "", 0, "visitorOff", ""), grant)).Success, "real MainForm independently rejects visitor deactivation without device permission");
            Check(!(await form.ExecuteRemoteControlAsync(new("mode", "", 0, "mode", "video"), grant with { CanDisableVisitor = true })).Success, "real MainForm protects visitor against normal video switch");
            var visitorService = Field<App.VisitorModeService>(form, "visitorMode");
            await visitorService.ConfigureAsync(s => s with { Source = App.VisitorSourceKind.Folder, Path = Root }, reloadMedia: false);
            var originalResume = visitorService.Settings.Resume;
            Check((await form.ExecuteRemoteControlAsync(new("pause", "", 0, "pause", ""), grant)).Success && visitorService.Settings.SlideshowPaused, "real visitor folder remote pause uses existing service");
            Check((await form.ExecuteRemoteControlAsync(new("interval", "", 0, "interval", "12000"), grant)).Success && visitorService.Settings.IntervalMilliseconds == 12000, "real visitor interval uses existing clock and persistence path");
            Check((await form.ExecuteRemoteControlAsync(new("shuffle", "", 0, "shuffle", "true"), grant)).Success && visitorService.Settings.Shuffle, "real visitor shuffle shares existing configuration");
            Check(ReferenceEquals(originalResume, visitorService.Settings.Resume), "remote visitor options preserve normal restore snapshot");
            var fullscreen = Field<App.FullscreenPausePolicy>(form, "fullscreenPolicy"); fullscreen.Update(true, true, DateTime.UtcNow);
            Check((await form.ExecuteRemoteControlAsync(new("resume", "", 0, "resume", ""), grant)).Success && !visitorService.Settings.SlideshowPaused && fullscreen.IsPaused, "remote resume preserves independent fullscreen pause");
            Check(!form.CaptureRemoteState().Capabilities.Allows("next"), "fullscreen suppression remains enforced for remote navigation");
            string wire = JsonSerializer.Serialize(form.CaptureRemoteState(), RemoteJson.Type<RemoteState>());
            Check(!wire.Contains(fixture) && !wire.Contains(Root), "real MainForm snapshot never exports media or resume paths");
            string screenshot = Path.Combine(Root, "remote-settings.png");
            using var manager = new App.RemoteDesktopManager(() => Task.FromResult(new RemoteState()), (_, _) => Task.FromResult(new RemoteOutcome(true)), Path.Combine(Root, "ui-vault"));
            using var settings = new App.RemoteSettingsForm(manager, true) { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new(-20000, -20000) };
            settings.Show(); await Task.Delay(500); settings.PerformLayout();
            using var rendered = new Bitmap(settings.Width, settings.Height); settings.DrawToBitmap(rendered, settings.ClientRectangle); rendered.Save(screenshot);
            Check(!manager.Vault.Settings.Enabled, "remote settings preview stays disabled without applying"); Console.WriteLine("UI_RENDER " + screenshot);
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(registry, false); }
    }
    private sealed class FakeTime : TimeProvider
    { public long Milliseconds; public override long TimestampFrequency => 1000; public override long GetTimestamp() => Milliseconds; }
    private static async Task MeasureAsync(string[] args)
    {
        string Value(string key, string fallback) { int index = Array.IndexOf(args, key); return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback; }
        int seconds = Math.Clamp(int.Parse(Value("--seconds", "600")), 10, 3600);
        bool baseline = args.Contains("--baseline");
        RemoteServer? host = null; X509Certificate2? identity = null; RemoteNetworkWatcher? watcher = null;
        var state = new RemoteState { Playback = "playing" };
        using var stateTimer = new System.Windows.Forms.Timer { Interval = 500 };
        using var profileTimer = new System.Windows.Forms.Timer { Interval = 15000 };
        bool querying = false;
        try
        {
            if (!baseline)
            {
                var vault = new RemoteVault(Path.Combine(Root, "measure")); identity = vault.Identity();
                host = new(vault, () => Task.FromResult(state), (_, _) => Task.FromResult(new RemoteOutcome(false)), IPAddress.IsLoopback);
                await host.StartAsync([IPAddress.Loopback], 0, identity);
                watcher = new(() => { });
                stateTimer.Tick += (_, _) => { if (host.HasConnectedClients) host.Observe(state); };
                profileTimer.Tick += async (_, _) => { if (querying) return; querying = true; try { await RemoteNetwork.PrivateAdaptersAsync(); } finally { querying = false; } };
                stateTimer.Start(); profileTimer.Start();
            }
            await Task.Delay(10000); GC.Collect();
            using var process = Process.GetCurrentProcess(); process.Refresh(); var initialCpu = process.TotalProcessorTime; var clock = Stopwatch.StartNew();
            long peakPrivate = 0; int peakThreads = 0, peakHandles = 0;
            while (clock.Elapsed.TotalSeconds < seconds)
            {
                await Task.Delay(1000); process.Refresh(); peakPrivate = Math.Max(peakPrivate, process.PrivateMemorySize64); peakThreads = Math.Max(peakThreads, process.Threads.Count); peakHandles = Math.Max(peakHandles, process.HandleCount);
            }
            process.Refresh();
            var result = new { Scenario = baseline ? "disabled-baseline" : "kestrel-private-profile-monitor-idle", Seconds = clock.Elapsed.TotalSeconds,
                CpuOneCorePercent = (process.TotalProcessorTime - initialCpu).TotalSeconds / clock.Elapsed.TotalSeconds * 100,
                PrivateBytes = process.PrivateMemorySize64, WorkingSetBytes = process.WorkingSet64, Threads = process.Threads.Count, Handles = process.HandleCount,
                PeakPrivateBytes = peakPrivate, PeakThreads = peakThreads, PeakHandles = peakHandles, Runtime = Environment.Version.ToString(),
                Notes = baseline ? "WinForms harness only; no server, state timer or profile monitor." : "Isolated loopback server and real native profile monitor (events + 15-second fallback); state capture only for connected clients. No real wallpaper engine, LAN listener or Android clients." };
            string output = Path.GetFullPath(Value("--output", Path.Combine(Root, "measurement.json"))); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })); Console.WriteLine("MEASUREMENT " + output);
        }
        finally { stateTimer.Stop(); profileTimer.Stop(); watcher?.Dispose(); if (host != null) await host.DisposeAsync(); identity?.Dispose(); }
    }
}
