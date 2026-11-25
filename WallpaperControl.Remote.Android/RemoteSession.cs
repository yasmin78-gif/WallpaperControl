using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace WallpaperControl.Remote.AndroidApp;

internal sealed class RemoteSession
{
    private const string StorageKey = "wallpapercontrol.remote.connection.v1";
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly SemaphoreSlim wake = new(0, 1);
    private readonly ConcurrentDictionary<string, DateTimeOffset> candidates = new();
    private CancellationTokenSource? lifetime;
    private NsdDiscovery? discovery;
    private RemoteConnection? connection;
    private bool network = true;
    private bool authorizationLost;
    private bool desiredRunning;
    private long received;
    private DateTimeOffset stateTimestamp;
    public SavedRemote? Saved { get; private set; }
    public RemoteState? State => connection?.State;
    public bool Connected => network && connection?.Connected == true && !authorizationLost;
    public bool Pending { get; private set; }
    public string Message { get; private set; } = "";
    public string Status { get; private set; } = "Noch kein PC gekoppelt";
    public event Action? Changed;
    private void Notify()
    {
        if (State is { } state && state.CapturedAt != stateTimestamp) { stateTimestamp = state.CapturedAt; received = Stopwatch.GetTimestamp(); }
        Changed?.Invoke();
    }
    public long? Remaining => !Connected || State?.RemainingMilliseconds == null ? null : Math.Max(0, State.RemainingMilliseconds.Value
        - (State.Playback == "paused" || State.Busy ? 0 : (long)Stopwatch.GetElapsedTime(received).TotalMilliseconds));
    public async Task InitializeAsync()
    {
        try
        {
            string? json = await SecureStorage.Default.GetAsync(StorageKey);
            if (json != null) Saved = JsonSerializer.Deserialize(json, RemoteJson.Type<SavedRemote>());
        }
        catch { Message = "Gespeicherte Verbindung ist nicht lesbar. Bitte den PC erneut koppeln."; }
        Resume(); Notify();
    }
    private async Task SaveAsync(SavedRemote saved)
    {
        await SecureStorage.Default.SetAsync(StorageKey, JsonSerializer.Serialize(saved, RemoteJson.Type<SavedRemote>()));
        Saved = saved;
    }
    public void Resume()
    {
        desiredRunning = true;
        if (lifetime != null) return;
        lifetime = new(); network = Connectivity.Current.NetworkAccess != NetworkAccess.None;
        Connectivity.Current.ConnectivityChanged += NetworkChanged;
        StartDiscovery(); _ = RunAsync(lifetime.Token);
    }
    public void Suspend() => Pause(permanent: true);
    private void Pause(bool permanent)
    {
        if (permanent) desiredRunning = false;
        Connectivity.Current.ConnectivityChanged -= NetworkChanged;
        lifetime?.Cancel(); lifetime?.Dispose(); lifetime = null; discovery?.Stop(); _ = DisconnectAsync();
    }
    private void Wake() { try { wake.Release(); } catch (SemaphoreFullException) { } }
    public void Reconnect() { authorizationLost = false; Wake(); }
    private void NetworkChanged(object? sender, ConnectivityChangedEventArgs args) => MainThread.BeginInvokeOnMainThread(() =>
    {
        network = args.NetworkAccess != NetworkAccess.None;
        if (network) { candidates.Clear(); StartDiscovery(); } else _ = DisconnectAsync();
        Wake(); Notify();
    });
    private void StartDiscovery()
    {
        discovery?.Stop(); if (Saved == null || lifetime == null) return;
        discovery = new(Saved.Server.ServerId);
        discovery.Found += address => { if (candidates.Count < 16 && candidates.TryAdd(address, DateTimeOffset.UtcNow)) Wake(); };
        try { discovery.Start(); } catch { /* Saved/manual address stays available; discovery never supplies trust. */ }
    }
    private async Task RunAsync(CancellationToken cancellation)
    {
        int delay = 1, attemptIndex = 0;
        while (!cancellation.IsCancellationRequested)
        {
            string previousStatus = Status;
            try
            {
                if (Saved == null) Status = "PC über QR-Code koppeln";
                else if (!network) Status = "Keine Netzwerkverbindung";
                else if (authorizationLost) Status = "Zugriff entzogen – am PC neu koppeln";
                else if (!Connected)
                {
                    Status = "Verbindung wird hergestellt …"; Notify();
                    string[] addresses = new[] { Saved.Address }.Concat(candidates.Where(c => c.Value > DateTimeOffset.UtcNow.AddMinutes(-2)).OrderByDescending(c => c.Value).Select(c => c.Key)).Concat(Saved.Server.Addresses).Distinct().Take(16).ToArray();
                    string address = addresses[attemptIndex % addresses.Length];
                    await ConnectAsync(address, cancellation); delay = 1; attemptIndex = 0; Status = "PC verbunden";
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { break; }
            catch
            {
                authorizationLost = connection?.Unauthorized == true;
                Status = authorizationLost ? "Zugriff entzogen – am PC neu koppeln" : "PC wird erneut verbunden …";
                delay = Math.Min(delay * 2, 5); attemptIndex++;
            }
            if (Status != previousStatus) Notify();
            try { if (await wake.WaitAsync(TimeSpan.FromSeconds(delay), cancellation)) { delay = 1; attemptIndex = 0; } }
            catch (OperationCanceledException) { break; }
        }
    }
    private async Task ConnectAsync(string address, CancellationToken cancellation)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            if (Saved == null) return;
            if (connection != null) await connection.DisposeAsync();
            var candidate = Saved with { Address = AndroidAddress.Normalize(address) };
            connection = new(candidate); connection.Changed += Notify;
            await connection.ConnectAsync(cancellation);
            if (Saved.Address != candidate.Address) await SaveAsync(candidate);
            Message = "";
        }
        finally { gate.Release(); }
    }
    private async Task DisconnectAsync()
    {
        await gate.WaitAsync();
        try { var old = connection; connection = null; if (old != null) { old.Changed -= Notify; await old.DisposeAsync(); } }
        finally { gate.Release(); Notify(); }
    }
    public async Task SendAsync(string type, string value = "")
    {
        if (!Connected || Pending || State?.Capabilities.Allows(type) != true) return;
        Pending = true; Message = "Wird am PC ausgeführt …"; Notify();
        try
        {
            var result = await connection!.SendAsync(type, value);
            Message = result.Status == "completed" ? "" : result.ErrorCode switch
            {
                "stale" => "Der PC-Zustand hat sich geändert. Bitte erneut wählen.",
                "busy" => "Der PC führt gerade eine andere Aktion aus.",
                "visitorPermission" or "notAllowed" => "Diese Aktion ist aktuell nicht freigegeben.",
                "videoUnavailable" => "Das hinterlegte Video ist nicht verfügbar.",
                _ => "Die Aktion konnte nicht vollständig ausgeführt werden. Aktuellen PC-Zustand prüfen."
            };
        }
        catch { Message = "Ergebnis nicht bestätigt. Zustand wird neu abgefragt; der Befehl wird nicht wiederholt."; Wake(); }
        finally { Pending = false; Notify(); }
    }
    public async Task PairAsync(RemoteQr qr, Action<string> showCode, CancellationToken cancellation)
    {
        Pause(permanent: false); await DisconnectAsync();
        try
        {
            if (qr.Addresses == null || qr.Addresses.Length is < 1 or > 16) throw new ArgumentException("Invalid QR addresses");
            var addresses = new List<string>();
            foreach (string address in qr.Addresses) try { addresses.Add(AndroidAddress.Normalize(address)); } catch (ArgumentException) { } catch (InvalidOperationException) { }
            if (addresses.Count == 0) throw new InvalidOperationException("No local address available");
            await RemoteEnrollment.PairAsync(qr with { Addresses = addresses.ToArray() }, showCode, SaveAsync, cancellation); authorizationLost = false; candidates.Clear();
        }
        finally { if (desiredRunning) Resume(); }
    }
    public async Task SetAddressAsync(string address)
    {
        if (Saved == null) throw new InvalidOperationException("Pair first");
        RemoteTrust.Endpoint(address); Pause(permanent: false); await DisconnectAsync();
        await SaveAsync(Saved with { Address = address }); candidates.Clear(); authorizationLost = false; if (desiredRunning) Resume();
    }
    public async Task ForgetAsync()
    {
        Pause(permanent: false); await DisconnectAsync(); SecureStorage.Default.Remove(StorageKey); Saved = null; candidates.Clear(); authorizationLost = false; Message = "Verbindung entfernt. Am PC kann das Gerät zusätzlich gelöscht werden."; if (desiredRunning) Resume();
    }
}
