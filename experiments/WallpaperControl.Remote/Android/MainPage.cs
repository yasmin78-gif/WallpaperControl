using System.Net.Http.Json;
using System.Text.Json;
using ZXing.Net.Maui.Controls;
namespace RemoteExperiment.AndroidApp;

public sealed class MainPage : ContentPage
{
    private const string StorageKey = "wc-remote-experiment.connection.v1";
    private readonly Label connection = new() { Text = "Nicht verbunden" };
    private readonly Label state = new() { Text = "Nur synthetische Testzustände" };
    private readonly Label pairing = new();
    private readonly Label discoveryStatus = new();
    private readonly Entry manual = new() { Placeholder = "Manuelle lokale HTTPS-Adresse (Pin bleibt gespeichert)" };
    private readonly List<Button> commands = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private RemoteClient? client;
    private SavedConnection? saved;
    private NsdDiscovery? discovery;
    private CancellationTokenSource? loop;
    private string? discovered;
    private bool loading;
    private string pairingStage = "QR lesen";
    public MainPage()
    {
        Title = "WC Remote – Experiment"; BackgroundColor = Color.FromArgb("#10141c");
        var layout = new VerticalStackLayout { Padding = 20, Spacing = 14 };
        Content = new ScrollView { Content = layout };
        layout.Add(new Label { Text = "PHASE 01 – keine echten Wallpapers", FontSize = 22 }); layout.Add(connection); layout.Add(pairing); layout.Add(discoveryStatus);
        Add(layout, "QR mit Kamera scannen", ScanAsync);
        layout.Add(manual); Add(layout, "Gespeicherten PC neu verbinden", async () => { if (saved == null) return; if (!string.IsNullOrWhiteSpace(manual.Text)) { Trust.Endpoint(manual.Text); saved = saved with { Server = saved.Server with { Address = manual.Text } }; } await ConnectAsync(); });
        layout.Add(state);
        foreach (var action in new[] { ("Besuchermodus EIN", "visitor", "true"), ("Besuchermodus AUS", "visitor", "false"), ("Weiter", "next", ""), ("Zurück", "previous", ""), ("Pause", "pause", "true"), ("Fortsetzen", "pause", "false"), ("Video", "mode", "video"), ("Diashow", "mode", "slideshow"), ("Shuffle EIN", "shuffle", "true"), ("Intervall 1 Minute", "interval", "60000") })
        {
            var button = Add(layout, action.Item1, async () =>
            {
                if (client?.Connected != true) return;
                if (action.Item2 == "visitor" && action.Item3 == "false" && !await DisplayAlertAsync("Besuchermodus deaktivieren?", "In der späteren Anwendung würden normale Hintergründe wieder sichtbar. Hier werden nur Testdaten geändert.", "Deaktivieren", "Abbrechen")) return;
                var result = await client.SendAsync(action.Item2, action.Item3); pairing.Text = result.Status + " " + result.Error;
            });
            button.CommandParameter = (action.Item2, action.Item3); commands.Add(button); button.IsEnabled = false;
        }
        Add(layout, "TLS-Negativtest: falscher Pin REST und WSS", ProbeWrongPinAsync);
        Add(layout, "TLS-Test: gespeicherter Pin (REST und WSS separat)", () => ProbeAsync(saved));
        foreach (var label in new[] { connection, state, pairing, discoveryStatus }) label.TextColor = Colors.White;
    }
    private Button Add(VerticalStackLayout layout, string title, Func<Task> action)
    {
        var button = new Button { Text = title };
        button.Clicked += async (_, _) => { button.IsEnabled = false; try { await action(); } catch (Exception ex) { pairing.Text = "Fehler: " + ex.GetType().Name; } finally { button.IsEnabled = true; Render(); } }; layout.Add(button); return button;
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing(); if (loading) return; loading = true;
        if (OperatingSystem.IsAndroidVersionAtLeast(37) && await Permissions.RequestAsync<LocalNetworkPermission>() != PermissionStatus.Granted) { pairing.Text = "Lokaler Netzwerkzugriff nicht erlaubt"; return; }
        try { string? json = await SecureStorage.Default.GetAsync(StorageKey); if (json != null) saved = JsonSerializer.Deserialize(json, WireJson.Type<SavedConnection>()); }
        catch { pairing.Text = "Sicherer Speicher nicht lesbar: neu koppeln"; }
        Resume();
    }
    public void Resume()
    {
        if (loop != null) return; loop = new(); var cancellation = loop.Token;
        if (saved != null) StartDiscovery();
        _ = RunAsync(cancellation);
    }
    public void Suspend() { loop?.Cancel(); loop?.Dispose(); loop = null; discovery?.Stop(); _ = DisconnectAsync(); }
    private async Task DisconnectAsync() { await gate.WaitAsync(); try { if (client != null) { await client.DisposeAsync(); client = null; } } finally { gate.Release(); MainThread.BeginInvokeOnMainThread(Render); } }
    private async Task RunAsync(CancellationToken cancellation)
    {
        int delay = 1;
        while (!cancellation.IsCancellationRequested)
        {
            try { if (saved != null && client?.Connected != true) { await ConnectAsync(); delay = 1; } }
            catch { MainThread.BeginInvokeOnMainThread(() => { connection.Text = "Verbindung unterbrochen – Wiederverbindung"; Render(); }); delay = Math.Min(delay * 2, 30); }
            try { await Task.Delay(TimeSpan.FromSeconds(delay), cancellation); } catch (OperationCanceledException) { break; }
        }
    }
    private void StartDiscovery()
    {
        discovery?.Stop(); if (saved == null) return;
        discovery = new(saved.Server.ServerId);
        discovery.Found += address => discovered = address;
        discovery.Status += text => MainThread.BeginInvokeOnMainThread(() => discoveryStatus.Text = text);
        try { discovery.Start(); } catch { discoveryStatus.Text = "NSD nicht verfügbar; gespeicherte/manuelle Adresse verwenden"; }
    }
    private async Task ConnectAsync()
    {
        if (saved == null) return; await gate.WaitAsync();
        try
        {
            if (client != null) await client.DisposeAsync();
            var attempt = saved;
            if (discovered != null) attempt = saved with { Server = saved.Server with { Address = discovered } };
            client = new(attempt); client.Changed += () => MainThread.BeginInvokeOnMainThread(Render);
            try { await client.StartAsync(); }
            catch when (discovered != null) { discovered = null; throw; }
            saved = attempt; await SaveAsync();
        }
        finally { gate.Release(); MainThread.BeginInvokeOnMainThread(Render); }
    }
    private async Task SaveAsync() { if (saved != null) await SecureStorage.Default.SetAsync(StorageKey, JsonSerializer.Serialize(saved, WireJson.Type<SavedConnection>())); }
    private void Render()
    {
        bool connected = client?.Connected == true;
        connection.Text = connected ? "Verbunden (TLS + Auth + WSS + Vollsnapshot)" : "Offline / Synchronisierung";
        if (client?.State is { } s) state.Text = $"Epoche {s.Epoch}\nRevision {s.Revision}\n{s.Mode}; Besucher {s.Visitor}; Pause {s.Paused}\nShuffle {s.Shuffle}; Intervall {s.Interval} ms; {s.Layout}" + (connected ? "" : "\nVERALTETER ZUSTAND");
        foreach (var button in commands)
        {
            var action = ((string Type, string Value))button.CommandParameter;
            button.IsEnabled = connected && !(client!.State?.Visitor == true && action.Type == "mode") && !(action.Type == "visitor" && action.Value == "false" && saved?.Credential.CanDisableVisitor != true);
        }
    }
    private async Task ScanAsync()
    {
        if (await Permissions.RequestAsync<Permissions.Camera>() != PermissionStatus.Granted) { pairing.Text = "Kamerarecht fehlt"; return; }
        var scanner = new CameraBarcodeReaderView { HeightRequest = 500, IsDetecting = true, Options = new() { Formats = ZXing.Net.Maui.BarcodeFormats.TwoDimensional, AutoRotate = true, Multiple = false } };
        var page = new ContentPage { Title = "Lokalen Test-QR scannen", Content = scanner };
        bool handled = false;
        scanner.BarcodesDetected += (_, detected) => MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (handled) return; handled = true; scanner.IsDetecting = false;
            await Navigation.PopAsync();
            try
            {
                pairingStage = "QR lesen";
                string? value = detected.Results.FirstOrDefault()?.Value;
                if (value == null || value.Length > 4096) throw new ArgumentException("QR size");
                var qr = JsonSerializer.Deserialize(value, WireJson.Type<PairQr>()) ?? throw new ArgumentException("QR");
                if (qr.Version != 1 || qr.Pin.Length != 64 || qr.Secret.Length != 64 || qr.ServerId.Length != 32) throw new ArgumentException("QR schema");
                var endpoint = Trust.Endpoint(qr.Address);
                if (System.Net.IPAddress.IsLoopback(System.Net.IPAddress.Parse(endpoint.DnsSafeHost.Trim('[', ']'))))
                { pairing.Text = "Dieser QR enthält die PC-Loopbackadresse. Am PC den privaten Ethernet-/WLANadapter wählen und einen neuen QR erzeugen."; return; }
                await PairAsync(qr);
            }
            catch (JsonException ex) { pairing.Text = $"JSON-Fehler bei: {pairingStage}; Zeile {ex.LineNumber}, Byte {ex.BytePositionInLine}. App 0.1.1"; }
            catch (HttpRequestException ex) { pairing.Text = $"HTTP-Fehler bei: {pairingStage}; Status {(ex.StatusCode is { } status ? ((int)status).ToString() : "keine HTTP-Antwort")}. App 0.1.1"; }
            catch (Exception ex) { pairing.Text = $"Kopplung fehlgeschlagen bei: {pairingStage}; {ex.GetType().Name}. App 0.1.1"; }
        });
        page.Disappearing += (_, _) => scanner.IsDetecting = false;
        await Navigation.PushAsync(page);
    }
    private async Task PairAsync(PairQr qr)
    {
        using var http = new HttpClient(Trust.Handler(qr.Pin)) { BaseAddress = Trust.Endpoint(qr.Address), Timeout = TimeSpan.FromSeconds(10) };
        pairingStage = "Kopplungsanfrage senden";
        using var response = await http.PostAsJsonAsync("api/v1/pairing/requests", new PairRequest(qr.Session, qr.Secret, "Android Testgerät"), WireJson.Type<PairRequest>()); response.EnsureSuccessStatusCode();
        pairingStage = $"Kopplungsantwort lesen (HTTP {(int)response.StatusCode})";
        var pending = await response.Content.ReadFromJsonAsync(WireJson.Type<PairPending>()) ?? throw new InvalidOperationException("pairing");
        pairing.Text = "Vergleichscode " + pending.Code + " – jetzt am PC bestätigen";
        for (int i = 0; i < 150; i++)
        {
            await Task.Delay(2000);
            pairingStage = "PC-Freigabe abfragen";
            using var claimed = await http.PostAsJsonAsync("api/v1/pairing/claim", new ClaimRequest(pending.Claim), WireJson.Type<ClaimRequest>());
            if ((int)claimed.StatusCode == 409) continue;
            claimed.EnsureSuccessStatusCode(); pairingStage = $"Zugangsdaten lesen (HTTP {(int)claimed.StatusCode})";
            var credential = await claimed.Content.ReadFromJsonAsync(WireJson.Type<Credential>()) ?? throw new InvalidOperationException("credential");
            saved = new(qr with { Secret = "", Session = "" }, credential); discovered = null;
            pairingStage = "Sicher speichern"; await SaveAsync();
            pairingStage = "Geräteerkennung starten"; StartDiscovery();
            pairingStage = "WSS verbinden und Vollzustand lesen"; await ConnectAsync(); pairing.Text = "Gekoppelt und sicher gespeichert"; return;
        }
        throw new TimeoutException("Pairing expired");
    }
    public sealed class LocalNetworkPermission : Permissions.BasePlatformPermission
    {
        public override (string androidPermission, bool isRuntime)[] RequiredPermissions => [("android.permission.ACCESS_LOCAL_NETWORK", true)];
    }
    private async Task ProbeWrongPinAsync()
    {
        if (saved == null) return;
        var wrong = saved with { Server = saved.Server with { Pin = new string('0', 64) } };
        await ProbeAsync(wrong);
    }
    private async Task ProbeAsync(SavedConnection? target)
    {
        if (target == null) return;
        bool restRejected = false, wssRejected = false;
        using (var rest = new HttpClient(Trust.Handler(target.Server.Pin)) { Timeout = TimeSpan.FromSeconds(10) })
        {
            rest.DefaultRequestHeaders.Authorization = new("Bearer", target.Credential.Token);
            try { using var response = await rest.GetAsync(new Uri(Trust.Endpoint(target.Server.Address), "api/v1/state")); response.EnsureSuccessStatusCode(); } catch (HttpRequestException) { restRejected = true; }
        }
        await using (var test = new RemoteClient(target)) try { await test.ProbeWssAsync(); } catch { wssRejected = true; }
        pairing.Text = $"TLS-Probe: REST abgewiesen={restRejected}, WSS abgewiesen={wssRejected}. Verbindungs-/Authfehler ebenfalls prüfen!";
    }
}
