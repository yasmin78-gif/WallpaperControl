using Android.Content;
using Android.Net.Nsd;
using System.Text;
using System.Collections.Concurrent;
namespace WallpaperControl.Remote.AndroidApp;

// Discovery only supplies untrusted address candidates. Stored pin never changes here.
public sealed class NsdDiscovery : Java.Lang.Object, NsdManager.IDiscoveryListener, NsdManager.IResolveListener
{
    private readonly NsdManager manager = (NsdManager)global::Android.App.Application.Context.GetSystemService(Context.NsdService)!;
    private readonly string serverId;
    private static readonly ConcurrentDictionary<Guid, NsdDiscovery> Pending = new();
    private readonly Guid registration = Guid.NewGuid();
    private readonly object callbackGate = new();
    private volatile bool running;
    private bool discoveryStopped = true;
    private int resolves;
    private readonly List<InfoCallback> callbacks = new();
    public event Action<string>? Found;
    public event Action<string>? Status;
    public NsdDiscovery(string id) { serverId = id; }
    public void Start()
    {
        lock (callbackGate)
        {
            if (running) return;
            if (!discoveryStopped || callbacks.Count != 0 || Pending.Count >= 32) throw new InvalidOperationException("Discovery is still stopping");
            Pending[registration] = this; discoveryStopped = false; running = true;
            try { manager.DiscoverServices("_wallpaperctrl._tcp.", NsdProtocol.DnsSd, this); }
            catch { running = false; discoveryStopped = true; Pending.TryRemove(registration, out _); throw; }
        }
    }
    public void Stop()
    {
        lock (callbackGate)
        {
            bool wasRunning = running; running = false; Found = null;
            if (wasRunning) try { manager.StopServiceDiscovery(this); } catch (Java.Lang.IllegalArgumentException) { }
            if (OperatingSystem.IsAndroidVersionAtLeast(34)) foreach (var callback in callbacks.ToArray())
                try { manager.UnregisterServiceInfoCallback(callback); } catch (Java.Lang.IllegalArgumentException) { }
            // Stop/unregister are asynchronous. Keep Java peers and their managed implementations rooted
            // until every terminal callback. Never dispose or clear them just because Stop returned.
        }
        ReleaseIfFinished();
    }
    private void ReleaseIfFinished()
    { lock (callbackGate) if (discoveryStopped && callbacks.Count == 0 && resolves == 0) Pending.TryRemove(registration, out _); }
    private void InfoFinished(InfoCallback callback)
    { lock (callbackGate) callbacks.Remove(callback); ReleaseIfFinished(); }
    public void OnServiceFound(NsdServiceInfo? info)
    {
        lock (callbackGate)
        {
        if (!running || info?.ServiceType?.Contains("_wallpaperctrl._tcp") != true) return;
        if (OperatingSystem.IsAndroidVersionAtLeast(34))
        {
            if (callbacks.Count >= 16) return;
            var callback = new InfoCallback(this); callbacks.Add(callback);
            try { manager.RegisterServiceInfoCallback(info, global::Android.App.Application.Context.MainExecutor!, callback); }
            catch { callbacks.Remove(callback); }
        }
        else
        {
#pragma warning disable CA1422 // Compatibility path for Android 12/13 only (API 31-33).
            resolves++;
            try { manager.ResolveService(info, this); } catch { resolves--; }
#pragma warning restore CA1422
        }
        }
    }
    public void OnServiceResolved(NsdServiceInfo? info)
    {
        try { Publish(info); }
        finally { lock (callbackGate) resolves = Math.Max(0, resolves - 1); ReleaseIfFinished(); }
    }
    private void Publish(NsdServiceInfo? info)
    {
        if (!running || info?.Attributes == null || !info.Attributes.TryGetValue("serverId", out var id) || Encoding.UTF8.GetString(id) != serverId) return;
        string? host;
                if (OperatingSystem.IsAndroidVersionAtLeast(34))
        {
            foreach (var candidate in info.HostAddresses ?? [])
            {
                if (candidate.HostAddress is not { } text) continue;
                string endpoint = text.Contains(':') ? $"https://[{text}]:{info.Port}/" : $"https://{text}:{info.Port}/";
                try { RemoteTrust.Endpoint(endpoint); Found?.Invoke(endpoint); } catch (ArgumentException) { }
            }
            return;
        }
        else
        {
#pragma warning disable CA1422 // Compatibility path for Android 12/13 only.
            host = info.Host?.HostAddress;
#pragma warning restore CA1422
        }
        if (host == null) return;
        string address = host.Contains(':') ? $"https://[{host}]:{info.Port}/" : $"https://{host}:{info.Port}/";
        try { RemoteTrust.Endpoint(address); Found?.Invoke(address); } catch (ArgumentException) { Status?.Invoke("NSD: nicht lokaler Kandidat verworfen"); }
    }
    public void OnDiscoveryStarted(string? type) => Status?.Invoke("NSD sucht (kein Vertrauensaufbau)");
    public void OnDiscoveryStopped(string? type) { lock (callbackGate) discoveryStopped = true; ReleaseIfFinished(); }
    public void OnStartDiscoveryFailed(string? type, NsdFailure error)
    { lock (callbackGate) { running = false; discoveryStopped = true; } ReleaseIfFinished(); Status?.Invoke("NSD Start fehlgeschlagen: " + error); }
    public void OnStopDiscoveryFailed(string? type, NsdFailure error) { }
    public void OnServiceLost(NsdServiceInfo? info) { }
    public void OnResolveFailed(NsdServiceInfo? info, NsdFailure error)
    { lock (callbackGate) resolves = Math.Max(0, resolves - 1); ReleaseIfFinished(); Status?.Invoke("NSD Auflösung: " + error); }
    private sealed class InfoCallback(NsdDiscovery owner) : Java.Lang.Object, NsdManager.IServiceInfoCallback
    {
        public void OnServiceInfoCallbackRegistered() { }
        public void OnServiceInfoCallbackRegistrationFailed(NsdFailure error) { owner.InfoFinished(this); owner.Status?.Invoke("NSD Registrierung: " + error); }
        public void OnServiceInfoCallbackUnregistered() => owner.InfoFinished(this);
        public void OnServiceLost() { }
        public void OnServiceLost(NsdServiceInfo? info) { }
        public void OnServiceUpdated(NsdServiceInfo? info) => owner.Publish(info);
    }
}
