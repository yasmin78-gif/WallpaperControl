using System.Net;
using System.Runtime.InteropServices;
namespace RemoteExperiment;

// Experimental native Windows DNS-SD. Never registers on all interfaces (index zero).
public sealed class WindowsDiscovery : IAsyncDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void Completion(uint status, nint context, nint instance);
    [StructLayout(LayoutKind.Sequential)] private struct Request { public uint Version, InterfaceIndex; public nint Instance, Callback, Context, Credentials; public int Unicast; }
    private Completion? callback;
    private Request request;
    private nint original;
    private nint requestMemory;
    private bool registered;
    private TaskCompletionSource<uint>? completion;
    private void Completed(uint status, nint context, nint instance) { if (instance != 0 && instance != original) DnsServiceFreeInstance(instance); completion?.TrySetResult(status); }
    public async Task<uint> StartAsync(IPAddress address, uint index, int port, string serverId)
    {
        if (index == 0) throw new ArgumentException("Explicit interface required");
        nint buffer = Marshal.AllocHGlobal(16);
        try
        {
            Marshal.Copy(new byte[16], 0, buffer, 16); var bytes = address.GetAddressBytes(); Marshal.Copy(bytes, 0, buffer, bytes.Length);
            original = DnsServiceConstructInstance($"WCRemote-{serverId[..8]}._wallpaperctrl._tcp.local", $"wc-{serverId[..8]}.local", bytes.Length == 4 ? buffer : 0, bytes.Length == 16 ? buffer : 0, (ushort)port, 0, 0, 2, ["serverId", "version"], [serverId, "1"]);
        }
        finally { Marshal.FreeHGlobal(buffer); }
        if (original == 0) throw new InvalidOperationException("DNS instance unavailable");
        callback = Completed; completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        request = new() { Version = 1, InterfaceIndex = index, Instance = original, Callback = Marshal.GetFunctionPointerForDelegate(callback) };
        requestMemory = Marshal.AllocHGlobal(Marshal.SizeOf<Request>()); Marshal.StructureToPtr(request, requestMemory, false);
        uint result = DnsServiceRegister(requestMemory, 0);
        if (result != 9506) { DnsServiceFreeInstance(original); original = 0; Marshal.FreeHGlobal(requestMemory); requestMemory = 0; return result; }
        // Keep native request/callback lifetime until callback completes, including slow DNS service.
        uint status = await completion.Task; registered = status == 0; return status;
    }
    public async ValueTask DisposeAsync()
    {
        if (registered)
        {
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            uint result = DnsServiceDeRegister(requestMemory, 0);
            if (result == 9506) await completion.Task;
        }
        if (original != 0) { DnsServiceFreeInstance(original); original = 0; }
        if (requestMemory != 0) { Marshal.FreeHGlobal(requestMemory); requestMemory = 0; }
        GC.KeepAlive(callback);
    }
    [DllImport("dnsapi.dll", CharSet = CharSet.Unicode)] private static extern nint DnsServiceConstructInstance(string name, string host, nint ip4, nint ip6, ushort port, ushort priority, ushort weight, uint count, [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] keys, [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] values);
    [DllImport("dnsapi.dll")] private static extern uint DnsServiceRegister(nint request, nint cancel);
    [DllImport("dnsapi.dll")] private static extern uint DnsServiceDeRegister(nint request, nint cancel);
    [DllImport("dnsapi.dll")] private static extern void DnsServiceFreeInstance(nint instance);
}
