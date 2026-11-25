using System.Net;
using Android.Content;
using Android.Net;

namespace WallpaperControl.Remote.AndroidApp;

internal static class AndroidAddress
{
    // A link-local scope in a desktop QR is a PC interface index, never the phone's interface index.
    internal static string Normalize(string address)
    {
        var uri = RemoteTrust.Endpoint(address);
        var ip = IPAddress.Parse(uri.DnsSafeHost.Trim('[', ']'));
        if (!ip.IsIPv6LinkLocal) return address;
        var manager = (ConnectivityManager)global::Android.App.Application.Context.GetSystemService(Context.ConnectivityService)!;
#pragma warning disable CA1422 // API-31-compatible enumeration; only used to locate a physical LAN interface for scoped addresses.
        var networks = manager.GetAllNetworks();
#pragma warning restore CA1422
        foreach (var network in networks)
        {
            var capabilities = manager.GetNetworkCapabilities(network);
            if (capabilities?.HasTransport(TransportType.Wifi) != true && capabilities?.HasTransport(TransportType.Ethernet) != true) continue;
            string? name = manager.GetLinkProperties(network)?.InterfaceName;
            var adapter = name == null ? null : Java.Net.NetworkInterface.GetByName(name);
            if (adapter?.Index is > 0) { ip.ScopeId = adapter.Index; return RemoteTrust.Address(ip, uri.Port); }
        }
        throw new InvalidOperationException("Physical LAN interface unavailable");
    }
}
