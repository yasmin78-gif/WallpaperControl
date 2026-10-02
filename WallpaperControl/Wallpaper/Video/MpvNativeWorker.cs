using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace WallpaperControl.Video;

// One native execution context for the pinned module, for the process lifetime.
// This bounds worker ownership and serializes access during A/B handoffs.
// Wake callbacks enqueue work; idle blocks without a timer or polling.
internal static class MpvNativeWorker
{
    private static readonly BlockingCollection<Action> queue = new();
    private static readonly Thread thread = Start();
    private static Thread Start()
    {
        var value = new Thread(() =>
        {
            int hr = CoInitializeEx(0, 2);
            try { foreach (var action in queue.GetConsumingEnumerable()) action(); }
            finally { if (hr >= 0) CoUninitialize(); }
        })
        { IsBackground = true, Name = "WallpaperControl mpv runtime" };
        value.SetApartmentState(ApartmentState.STA);
        value.Start(); return value;
    }
    internal static void Post(Action action) { GC.KeepAlive(thread); queue.Add(action); }
    internal static int Pending => queue.Count;
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}
