extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using Video = WallpaperApp::WallpaperControl.Video;
using System.Diagnostics;
using System.Runtime.InteropServices;

internal static class VideoNativePhase2Checks
{
    private sealed class Player : IDisposable
    {
        private readonly Video.VideoRenderForm window = new() { Location = new(-20000, -20000), Size = new(320, 180) };
        internal readonly Video.MfPlayer Native;
        internal readonly nint Hwnd;
        private bool ready;
        private Exception? error;
        internal Player(string path)
        {
            try
            {
                window.Show(); Hwnd = window.Handle;
                Native = new(Hwnd, path, Event);
            }
            catch { window.Dispose(); throw; }
        }
        private void Event(nint header)
        {
            try
            {
                Marshal.ThrowExceptionForHR(Marshal.ReadInt32(header, 4));
                switch (Marshal.ReadInt32(header))
                {
                    case 5: Native.SetCreatedItem(Marshal.ReadIntPtr(header, 32)); break;
                    case 6: Native.Fill(window.ClientSize); ready = true; break;
                }
            }
            catch (Exception ex) { error = ex; }
        }
        internal void Prepare()
        {
            Wait(() => ready || error != null);
            if (error != null) throw error;
            Native.Play();
            Wait(() => Native.HasPresentedFrame || error != null);
            if (error != null) throw error;
            Native.Pause();
        }
        public void Dispose() { try { Native.Dispose(); } finally { window.Dispose(); } }
    }
    private static void Pump(int milliseconds)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(2); }
    }
    private static void Wait(Func<bool> complete)
    {
        var clock = Stopwatch.StartNew();
        while (!complete())
        {
            if (clock.Elapsed.TotalSeconds > 15) throw new TimeoutException("Native Phase 2 check");
            Pump(5);
        }
    }
    private static Exception? Failure(Task work)
    {
        Wait(() => work.IsCompleted);
        try { work.GetAwaiter().GetResult(); return null; }
        catch (Exception ex) { return ex; }
    }
    internal static void Run(string fixtures, Action<bool, string> check)
    {
        string supported = Path.Combine(fixtures, "h264.mp4");
        string folder = Path.Combine(fixtures, "phase2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string locked = Path.Combine(folder, "locked.mp4");
        File.Copy(supported, locked);
        using var dispatcher = new Video.VideoRenderForm();
        var desktop = new Video.MediaFoundationVideoDesktop(dispatcher);
        using (File.Open(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            check(Failure(desktop.ValidateAsync(locked, default)) is IOException or UnauthorizedAccessException,
                "Native Phase 2: unreadable/exclusively locked file fails safely before decoder creation");
        check(Failure(desktop.ValidateAsync(locked, default)) == null, "Native Phase 2: same file validates after releasing read lock");
        File.Delete(locked);
        check(!Video.VideoFileValidation.IsLocalMp4(locked), "Native Phase 2: disappeared local file is rejected");
        bool missingRejected = false;
        try { using var missing = new Video.MfPlayer(dispatcher.Handle, locked, _ => { }); }
        catch (FileNotFoundException) { missingRejected = true; }
        check(missingRejected, "Native Phase 2: file lost between validation and player creation leaves no MF player");
        bool corruptRejected = false;
        using (var corrupt = new Player(Path.Combine(fixtures, "corrupt.mp4")))
        {
            try { corrupt.Prepare(); }
            catch (COMException) { corruptRejected = true; }
        }
        check(corruptRejected, "Native Phase 2: actual async MF parsing error is contained and disposed");
        // Warm driver/codec caches before checking repeated native lifetimes.
        for (int i = 0; i < 8; i++) { using var warmup = new Player(supported); warmup.Prepare(); }
        Pump(100);
        using var process = Process.GetCurrentProcess(); process.Refresh();
        long baselinePrivate = process.PrivateMemorySize64;
        int baselineHandles = process.HandleCount;
        var baselineTypes = NativeHandleSnapshot.Capture();
        bool frames = true, muted = true, windowsReleased = true, buffersReleased = true;
        for (int i = 0; i < 24; i++)
        {
            using var player = new Player(supported);
            player.Prepare();
            frames &= player.Native.HasPresentedFrame;
            muted &= player.Native.Muted;
            player.Dispose(); player.Dispose();
            windowsReleased &= !Video.VideoNative.IsWindow(player.Hwnd);
            buffersReleased &= !player.Native.HasPresentedFrame && !player.Native.TryCaptureFrame(out _);
        }
        check(frames && muted, "Native Phase 2: 24 fresh MF players decode a valid frame with mute preserved");
        check(windowsReleased && buffersReleased, "Native Phase 2: 24 disposals release HWNDs and cannot return retained EVR buffers");
        Pump(200); process.Refresh();
        long growth = process.PrivateMemorySize64 - baselinePrivate;
        int handles = process.HandleCount - baselineHandles;
        var finalTypes = NativeHandleSnapshot.Capture();
        int vendorGrowth = finalTypes.NvidiaIpc - baselineTypes.NvidiaIpc;
        Console.WriteLine(FormattableString.Invariant($"Native Phase 2 handle attribution: totalDelta={finalTypes.Total - baselineTypes.Total}; nvidiaIpcDelta={vendorGrowth}; otherDelta={finalTypes.Total - baselineTypes.Total - vendorGrowth}"));
        Console.WriteLine(FormattableString.Invariant($"Native Phase 2 resources: cycles=24; privateGrowthMiB={growth / 1048576.0:F2}; handleDelta={handles}"));
        check(growth < 32L * 1048576, "Native Phase 2: warmed repeated player creation stays below 32 MiB private growth");
        check(finalTypes.Total - baselineTypes.Total - vendorGrowth <= 32, "Native Phase 2: non-vendor handles remain bounded; total and named NVIDIA IPC growth reported separately");
        using var disposed = new Player(supported); disposed.Prepare(); disposed.Dispose();
        foreach (var operation in new (string Name, Action Call)[]
        {
            ("Play", disposed.Native.Play), ("Pause", disposed.Native.Pause),
            ("Seek", () => disposed.Native.Seek(0)), ("Position", () => disposed.Native.Position100ns())
        })
        {
            bool rejected = false;
            try { operation.Call(); } catch (ObjectDisposedException) { rejected = true; }
            check(rejected, $"Native Phase 2: {operation.Name} after disposal cannot touch released COM pointers");
        }
    }
}

