using Android.Content;
using Android.Net.Nsd;
using System.Text;
namespace RemoteExperiment.AndroidApp;

// Discovery only supplies untrusted address candidates. Stored pin never changes here.
public sealed class NsdDiscovery : Java.Lang.Object, NsdManager.IDiscoveryListener, NsdManager.IResolveListener
{
    private readonly NsdManager manager = (NsdManager)global::Android.App.Application.Context.GetSystemService(Context.NsdService)!;
    private readonly string serverId;
    private bool running;
    private readonly List<InfoCallback> callbacks = new();
    public event Action<string>? Found;
    public event Action<string>? Status;
    public NsdDiscovery(string id) { serverId = id; }
    public void Start() { if (!running) { manager.DiscoverServices("_wallpaperctrl._tcp.", NsdProtocol.DnsSd, this); running = true; } }
    public void Stop()
    {
        if (running) { try { manager.StopServiceDiscovery(this); } catch (Java.Lang.IllegalArgumentException) { } running = false; }
        if (OperatingSystem.IsAndroidVersionAtLeast(34)) foreach (var callback in callbacks) try { manager.UnregisterServiceInfoCallback(callback); } catch (Java.Lang.IllegalArgumentException) { }
        callbacks.Clear();
    }
    public void OnServiceFound(NsdServiceInfo? info)
    {
        if (!running || info?.ServiceType?.Contains("_wallpaperctrl._tcp") != true) return;
        if (OperatingSystem.IsAndroidVersionAtLeast(34))
        {
            if (callbacks.Count >= 16) return;
            var callback = new InfoCallback(this); callbacks.Add(callback);
            manager.RegisterServiceInfoCallback(info, global::Android.App.Application.Context.MainExecutor!, callback);
        }
        else
        {
#pragma warning disable CA1422 // Compatibility path for Android 12/13 only (API 31-33).
            manager.ResolveService(info, this);
#pragma warning restore CA1422
        }
    }
    public void OnServiceResolved(NsdServiceInfo? info)
    {
        if (!running || info?.Attributes == null || !info.Attributes.TryGetValue("serverId", out var id) || Encoding.UTF8.GetString(id) != serverId) return;
        string? host;
        if (OperatingSystem.IsAndroidVersionAtLeast(34)) host = info.HostAddresses?.FirstOrDefault()?.HostAddress;
        else
        {
#pragma warning disable CA1422 // Compatibility path for Android 12/13 only.
            host = info.Host?.HostAddress;
#pragma warning restore CA1422
        }
        if (host == null) return;
        string address = host.Contains(':') ? $"https://[{host}]:{info.Port}/" : $"https://{host}:{info.Port}/";
        try { Trust.Endpoint(address); Found?.Invoke(address); } catch (ArgumentException) { Status?.Invoke("NSD: nicht lokaler Kandidat verworfen"); }
    }
    public void OnDiscoveryStarted(string? type) => Status?.Invoke("NSD sucht (kein Vertrauensaufbau)");
    public void OnDiscoveryStopped(string? type) { }
    public void OnStartDiscoveryFailed(string? type, NsdFailure error) => Status?.Invoke("NSD Start fehlgeschlagen: " + error);
    public void OnStopDiscoveryFailed(string? type, NsdFailure error) { }
    public void OnServiceLost(NsdServiceInfo? info) { }
    public void OnResolveFailed(NsdServiceInfo? info, NsdFailure error) => Status?.Invoke("NSD Auflösung: " + error);
    private sealed class InfoCallback(NsdDiscovery owner) : Java.Lang.Object, NsdManager.IServiceInfoCallback
    {
        public void OnServiceInfoCallbackRegistered() { }
        public void OnServiceInfoCallbackRegistrationFailed(NsdFailure error) => owner.Status?.Invoke("NSD Registrierung: " + error);
        public void OnServiceInfoCallbackUnregistered() { }
        public void OnServiceLost() { }
        public void OnServiceLost(NsdServiceInfo? info) { }
        public void OnServiceUpdated(NsdServiceInfo? info) => owner.OnServiceResolved(info);
    }
}
