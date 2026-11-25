using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace WallpaperControl.Remote;

public sealed record RemoteAddress(IPAddress Address, int PrefixLength);
public sealed record RemoteAdapter(string Id, string Name, uint Index, RemoteAddress[] Addresses)
{
    public override string ToString() => $"{Name} · {string.Join(", ", Addresses.Select(a => a.Address))}";
}

public static class RemoteNetwork
{
    private static readonly object ProfilesGate = new();
    private static Task<HashSet<Guid>>? profileQuery;
    public static async Task<RemoteAdapter[]> PrivateAdaptersAsync(CancellationToken cancellation = default)
    {
        Task<HashSet<Guid>> query;
        lock (ProfilesGate)
        {
            // At most one native RPC can remain pending if Network List Service is unhealthy.
            if (profileQuery == null || profileQuery.IsCompleted) profileQuery = Task.Run(ReadPrivateAdapterIds);
            query = profileQuery;
        }
        var privateIds = await query.WaitAsync(TimeSpan.FromSeconds(3), cancellation);
        return NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
                .Where(n => !new[] { "vpn", "proton", "tunnel", "tap", "wireguard", "virtual", "nordlynx", "tailscale", "zerotier", "hamachi" }.Any(s => (n.Name + " " + n.Description).Contains(s, StringComparison.OrdinalIgnoreCase)))
                .Where(n => Guid.TryParse(n.Id, out var id) && privateIds.Contains(id))
                .Select(n =>
                {
                    var p = n.GetIPProperties(); int index = p.GetIPv4Properties()?.Index ?? p.GetIPv6Properties()?.Index ?? 0;
                    return new RemoteAdapter(n.Id, n.Name, (uint)index, p.UnicastAddresses.Where(u => RemoteTrust.Local(u.Address) && !IPAddress.IsLoopback(u.Address) && !u.Address.IsIPv4MappedToIPv6)
                        .OrderBy(u => u.Address.AddressFamily == AddressFamily.InterNetwork ? 0 : 1).Select(u => new RemoteAddress(u.Address, u.PrefixLength)).ToArray());
                }).Where(n => n.Index != 0 && n.Addresses.Length != 0).ToArray();
    }
    private static HashSet<Guid> ReadPrivateAdapterIds()
    {
        object? manager = null, connections = null;
        var result = new HashSet<Guid>();
        static void Release(object? value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
        try
        {
            manager = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"), throwOnError: true)!);
            connections = ((dynamic)manager!).GetNetworkConnections();
            foreach (object connection in (System.Collections.IEnumerable)connections)
            {
                object? network = null;
                try
                {
                    network = ((dynamic)connection).GetNetwork();
                    if ((bool)((dynamic)network).IsConnected && (int)((dynamic)network).GetCategory() == 1)
                        result.Add(((INetworkConnectionNative)connection).GetAdapterId());
                }
                finally { Release(network); Release(connection); }
            }
            return result;
        }
        finally { Release(connections); Release(manager); }
    }
    // GUID returns are not Automation VARIANTs; use the native dual-interface vtable for GetAdapterId.
    [ComImport, Guid("DCB00005-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface INetworkConnectionNative
    {
        [return: MarshalAs(UnmanagedType.Interface)] object GetNetwork();
        bool IsConnectedToInternet { [return: MarshalAs(UnmanagedType.VariantBool)] get; }
        bool IsConnected { [return: MarshalAs(UnmanagedType.VariantBool)] get; }
        int GetConnectivity();
        Guid GetConnectionId();
        Guid GetAdapterId();
        int GetDomainType();
    }

    public static bool SameSubnet(IPAddress remote, RemoteAddress local)
    {
        if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();
        if (remote.AddressFamily != local.Address.AddressFamily || !RemoteTrust.Local(remote)) return false;
        if (remote.AddressFamily == AddressFamily.InterNetworkV6 && remote.ScopeId != 0 && local.Address.ScopeId != 0 && remote.ScopeId != local.Address.ScopeId) return false;
        byte[] first = remote.GetAddressBytes(), second = local.Address.GetAddressBytes();
        int prefix = local.PrefixLength;
        if (prefix <= 0 || prefix > first.Length * 8) return false;
        for (int i = 0; i < first.Length; i++)
        {
            int bits = Math.Clamp(prefix - i * 8, 0, 8); int mask = (0xff << (8 - bits)) & 0xff;
            if ((first[i] & mask) != (second[i] & mask)) return false;
        }
        return true;
    }
}
