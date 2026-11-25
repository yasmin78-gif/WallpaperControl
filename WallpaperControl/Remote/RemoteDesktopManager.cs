using System.Net.NetworkInformation;
using System.Security.Cryptography.X509Certificates;
using WallpaperControl.Remote;

namespace WallpaperControl;

internal sealed class RemoteDesktopManager : IDisposable
{
    internal RemoteVault Vault { get; }
    internal RemoteServer? Server { get; private set; }
    internal RemoteAdapter? Adapter { get; private set; }
    internal string Status { get; private set; } = "Deaktiviert";
    internal string[] Addresses => Adapter?.Addresses.Select(a => RemoteTrust.Address(a.Address, Vault.Settings.Port)).ToArray() ?? [];
    internal event Action? Changed;
    private readonly Func<Task<RemoteState>> readState;
    private readonly Func<RemoteCommand, DeviceGrant, Task<RemoteOutcome>> execute;
    private readonly SemaphoreSlim wake = new(0, 1);
    private CancellationTokenSource? runtime;
    private Task? running;
    private X509Certificate2? identity;
    private RemoteDiscovery? discovery;
    private RemoteNetworkWatcher? networkWatcher;
    private bool disposed;

    internal RemoteDesktopManager(Func<Task<RemoteState>> readState, Func<RemoteCommand, DeviceGrant, Task<RemoteOutcome>> execute, string? storageFolder = null)
    {
        this.readState = readState; this.execute = execute;
        Vault = new(storageFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WallpaperControl", "Remote"));
        Vault.DevicesChanged += Notify;
    }
    private void Notify() => Changed?.Invoke();
    private void SetStatus(string text) { if (Status == text) return; Status = text; Notify(); }
    internal Task InitializeAsync() => ApplyAsync(Vault.Settings, persist: false);
    internal async Task ApplyAsync(RemoteSettings settings, bool persist = true)
    {
        if (disposed) return;
        await StopAsync();
        if (persist) Vault.Configure(settings);
        if (!settings.Enabled) { SetStatus("Deaktiviert"); return; }
        try { networkWatcher = new(() => NetworkChanged(null, EventArgs.Empty)); }
        catch { SetStatus("Private Netzwerküberwachung ist nicht verfügbar. Fernsteuerung bleibt geschlossen."); return; }
        runtime = new(); NetworkChange.NetworkAddressChanged += NetworkChanged;
        running = RunAsync(settings, runtime.Token); SetStatus("Privates Netzwerk wird geprüft …");
    }
    private void NetworkChanged(object? sender, EventArgs e)
    {
        Server?.StopAccepting();
        try { wake.Release(); } catch (SemaphoreFullException) { }
    }
    private async Task RunAsync(RemoteSettings settings, CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                var adapters = await RemoteNetwork.PrivateAdaptersAsync(cancellation);
                var selected = adapters.FirstOrDefault(a => a.Id == settings.AdapterId);
                string signature = BindingSignature(selected);
                string previous = BindingSignature(Adapter);
                if (Server != null && (selected == null || signature != previous || !Server.Accepting || identity?.NotAfter.ToUniversalTime() <= DateTime.UtcNow.AddDays(7))) await StopServerAsync();
                if (selected == null) SetStatus("Kein freigegebener privater Heimnetzadapter verfügbar. VPN-/öffentliche Adapter bleiben gesperrt.");
                else if (Server == null)
                {
                    Adapter = selected; identity = Vault.Identity();
                    var allowed = selected.Addresses;
                    Server = new(Vault, readState, execute, peer => allowed.Any(a => RemoteNetwork.SameSubnet(peer, a)));
                    await Server.StartAsync(allowed.Select(a => a.Address).ToArray(), settings.Port, identity);
                    SetStatus("Aktiv · " + string.Join(" · ", Addresses));
                    discovery = new();
                    try { await discovery.StartAsync(selected, settings.Port, Vault.ServerId, cancellation); }
                    catch (Exception ex) when (ex is not OperationCanceledException) { SetStatus("HTTPS aktiv; automatische Erkennung nicht verfügbar. QR/manuelle Adresse verwenden."); AppLogger.Info("Remote discovery unavailable: " + ex.GetType().Name); }
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                await StopServerAsync(); SetStatus("Fernsteuerung nicht erreichbar (privates Netzwerk, Port oder sichere Ablage prüfen).");
                AppLogger.Info("Remote startup unavailable: " + ex.GetType().Name);
            }
            try { await wake.WaitAsync(TimeSpan.FromSeconds(15), cancellation); } catch (OperationCanceledException) { break; }
        }
        await StopServerAsync();
    }
    private static string BindingSignature(RemoteAdapter? adapter) => adapter == null ? "" :
        $"{adapter.Id}|{adapter.Name}|{adapter.Index}|{string.Join(";", adapter.Addresses.Select(a => $"{a.Address}/{a.PrefixLength}"))}";
    internal RemoteQr NewPairing()
    {
        if (Server?.Accepting != true || identity == null) throw new InvalidOperationException("Remote not ready");
        return Server.Pairing.Begin(Vault.Settings.Name, Addresses, RemoteTrust.Pin(identity));
    }
    internal void StopAccepting() { Server?.StopAccepting(); runtime?.Cancel(); }
    private async Task StopServerAsync()
    {
        var server = Server; Server = null;
        if (server != null) await server.DisposeAsync();
        if (discovery != null) { await discovery.DisposeAsync(); discovery = null; }
        identity?.Dispose(); identity = null; Adapter = null;
    }
    internal async Task StopAsync()
    {
        NetworkChange.NetworkAddressChanged -= NetworkChanged;
        networkWatcher?.Dispose(); networkWatcher = null;
        runtime?.Cancel(); if (running != null) await running;
        runtime?.Dispose(); runtime = null; running = null;
        SetStatus("Deaktiviert");
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; Vault.DevicesChanged -= Notify; Changed = null;
        StopAccepting(); _ = FinishDisposeAsync();
    }
    private async Task FinishDisposeAsync() { try { await StopAsync(); } catch (Exception ex) { AppLogger.Info("Remote shutdown: " + ex.GetType().Name); } }
}
