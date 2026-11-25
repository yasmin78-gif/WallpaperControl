using System.Collections.Concurrent;
using System.Net;
using System.Runtime.InteropServices;

namespace WallpaperControl.Remote;

// Native requests stay rooted until DNS calls back. A timeout never frees memory still owned by DNS.
public sealed class RemoteDiscovery : IAsyncDisposable
{
    private static readonly ConcurrentDictionary<Guid, RemoteDiscovery> Roots = new();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void Completion(uint status, nint context, nint instance);
    [StructLayout(LayoutKind.Sequential)] private struct Request { public uint Version, InterfaceIndex; public nint Instance, Callback, Context, Credentials; public int Unicast; }
    private readonly Guid id = Guid.NewGuid();
    private readonly Completion callback;
    private nint instance, request, cancel;
    private TaskCompletionSource<uint> completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool registered, abandoned, disposed;
    private uint phase;
    private int disposalStarted;
    private volatile bool registrationComplete;
    private uint registrationStatus;
    public RemoteDiscovery() { callback = Complete; }
    private void Complete(uint status, nint context, nint returned)
    {
        uint currentPhase = phase;
        if (currentPhase == 1) { registrationStatus = status; registrationComplete = true; }
        if (returned != 0 && returned != instance) DnsServiceFreeInstance(returned);
        completed.TrySetResult(status);
        if (abandoned) _ = Task.Run(async () => { if (currentPhase == 1 && status == 0) { registered = true; await DisposeAsync(); } else Cleanup(); });
    }
    public async Task StartAsync(RemoteAdapter adapter, int port, string serverId, CancellationToken cancellation)
    {
        if (adapter.Index == 0 || Roots.Count >= 4) throw new InvalidOperationException("Discovery unavailable");
        nint ip4 = 0, ip6 = 0;
        try
        {
            foreach (var address in adapter.Addresses.Select(a => a.Address))
            {
                if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && ip4 == 0) { ip4 = Marshal.AllocHGlobal(4); Marshal.Copy(address.GetAddressBytes(), 0, ip4, 4); }
                if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 && ip6 == 0) { ip6 = Marshal.AllocHGlobal(16); Marshal.Copy(address.GetAddressBytes(), 0, ip6, 16); }
            }
            instance = DnsServiceConstructInstance($"WC-{serverId[..12]}._wallpaperctrl._tcp.local", $"wc-{serverId[..12]}.local", ip4, ip6, (ushort)port, 0, 0, 2, ["serverId", "version"], [serverId, "1"]);
        }
        finally { if (ip4 != 0) Marshal.FreeHGlobal(ip4); if (ip6 != 0) Marshal.FreeHGlobal(ip6); }
        if (instance == 0) throw new InvalidOperationException("Discovery unavailable");
        request = Marshal.AllocHGlobal(Marshal.SizeOf<Request>()); cancel = Marshal.AllocHGlobal(IntPtr.Size); Marshal.WriteIntPtr(cancel, 0);
        Marshal.StructureToPtr(new Request { Version = 1, InterfaceIndex = adapter.Index, Instance = instance, Callback = Marshal.GetFunctionPointerForDelegate(callback) }, request, false);
        Roots[id] = this; phase = 1;
        uint result = DnsServiceRegister(request, cancel);
        if (result != 9506) { Cleanup(); throw new InvalidOperationException("Discovery registration failed"); }
        try
        {
            uint status = await completed.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellation);
            if (status != 0) { Cleanup(); throw new InvalidOperationException("Discovery registration failed"); }
            registered = true;
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            abandoned = true; DnsServiceRegisterCancel(cancel);
            if (completed.Task.IsCompleted) _ = Task.Run(async () => { registered = completed.Task.Result == 0; await DisposeAsync(); });
            throw;
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        if (registrationComplete && registrationStatus == 0 && phase == 1) registered = true;
        if (!registered && !completed.Task.IsCompleted && request != 0) { abandoned = true; DnsServiceRegisterCancel(cancel); return; }
        if (Interlocked.Exchange(ref disposalStarted, 1) != 0) return;
        disposed = true;
        if (registered)
        {
            completed = new(TaskCreationOptions.RunContinuationsAsynchronously); phase = 2; abandoned = false; Marshal.WriteIntPtr(cancel, 0);
            uint result = DnsServiceDeRegister(request, 0);
            if (result == 9506)
            {
                try { await completed.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
                catch (TimeoutException) { abandoned = true; if (completed.Task.IsCompleted) Cleanup(); return; }
            }
        }
        Cleanup();
    }
    private void Cleanup()
    {
        lock (this)
        {
            if (instance != 0) { DnsServiceFreeInstance(instance); instance = 0; }
            if (request != 0) { Marshal.FreeHGlobal(request); request = 0; }
            if (cancel != 0) { Marshal.FreeHGlobal(cancel); cancel = 0; }
            Roots.TryRemove(id, out _); GC.KeepAlive(callback);
        }
    }
    [DllImport("dnsapi.dll", CharSet = CharSet.Unicode)] private static extern nint DnsServiceConstructInstance(string name, string host, nint ip4, nint ip6, ushort port, ushort priority, ushort weight, uint count, [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] keys, [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] values);
    [DllImport("dnsapi.dll")] private static extern uint DnsServiceRegister(nint request, nint cancel);
    [DllImport("dnsapi.dll")] private static extern uint DnsServiceDeRegister(nint request, nint cancel);
    [DllImport("dnsapi.dll")] private static extern uint DnsServiceRegisterCancel(nint cancel);
    [DllImport("dnsapi.dll")] private static extern void DnsServiceFreeInstance(nint instance);
}
