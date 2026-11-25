using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace WallpaperControl.Remote;

public sealed class RemoteNetworkWatcher : IDisposable
{
    private object? manager;
    private IConnectionPoint? connection;
    private readonly Sink sink;
    private int cookie;
    public RemoteNetworkWatcher(Action changed)
    {
        sink = new(changed);
        try
        {
            manager = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"), true)!);
            var id = typeof(INetworkEvents).GUID;
            ((IConnectionPointContainer)manager!).FindConnectionPoint(ref id, out connection);
            connection!.Advise(sink, out cookie);
        }
        catch { Dispose(); throw; }
    }
    [ComVisible(true), Guid("DCB00004-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface INetworkEvents
    {
        void NetworkAdded(Guid networkId);
        void NetworkDeleted(Guid networkId);
        void NetworkConnectivityChanged(Guid networkId, int connectivity);
        void NetworkPropertyChanged(Guid networkId, int flags);
    }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class Sink(Action changed) : INetworkEvents
    {
        private void Notify() { try { changed(); } catch { } }
        public void NetworkAdded(Guid networkId) => Notify();
        public void NetworkDeleted(Guid networkId) => Notify();
        public void NetworkConnectivityChanged(Guid networkId, int connectivity) => Notify();
        public void NetworkPropertyChanged(Guid networkId, int flags) => Notify();
    }
    public void Dispose()
    {
        if (connection != null)
        {
            if (cookie != 0) { try { connection.Unadvise(cookie); } catch (COMException) { } cookie = 0; }
            Marshal.ReleaseComObject(connection); connection = null;
        }
        if (manager != null) { Marshal.ReleaseComObject(manager); manager = null; }
        GC.KeepAlive(sink);
    }
}
