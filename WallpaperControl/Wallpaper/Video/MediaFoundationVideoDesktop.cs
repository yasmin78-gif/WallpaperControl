using System.Runtime.InteropServices;

namespace WallpaperControl.Video;

internal sealed class MediaFoundationVideoDesktop : IVideoDesktop
{
    private readonly Control dispatcher;
    internal MediaFoundationVideoDesktop(Control dispatcher) { this.dispatcher = dispatcher; }
    public int MonitorCount => Screen.AllScreens.Length;
    public bool FileExists(string path) => VideoFileValidation.IsLocalMp4(path);
    // Serialize metadata readers even when a timed-out caller has moved on.
    // Native MF calls cannot be forcibly aborted inside this process.
    private readonly SemaphoreSlim validation = new(1, 1);
    public async Task ValidateAsync(string path, CancellationToken cancellation)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        try { await ValidateCoreAsync(path, operation.Token).WaitAsync(TimeSpan.FromSeconds(15), cancellation); }
        catch { operation.Cancel(); throw; }
    }
    private async Task ValidateCoreAsync(string path, CancellationToken cancellation)
    {
        await validation.WaitAsync(cancellation);
        try
        {
            await Task.Run(() =>
            {
                cancellation.ThrowIfCancellationRequested();
                using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) { }
                VideoFileValidation.ValidateCodec(path);
                cancellation.ThrowIfCancellationRequested();
            }, cancellation);
        }
        finally { validation.Release(); }
    }
    public VideoShellGeneration? ResolveShell() => VideoNative.Resolve();
    public IVideoSession Create(string path, VideoShellGeneration shell) => new MediaFoundationVideoSession(dispatcher, path, shell);
}

internal sealed class MediaFoundationVideoSession : IVideoSession
{
    private readonly VideoRenderForm render;
    private readonly Control dispatcher;
    private readonly MfPlayer player;
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly VideoShellGeneration shell;
    private readonly Rectangle screenBounds;
    private readonly nint rendererHandle;
    private bool disposed, playing, priming, prepared;
    private Exception? failure;
    private int loops;
    private static long nextId;
    private readonly long id = Interlocked.Increment(ref nextId);
    private void Log(string message) => AppLogger.Info($"Video MF: pid={Environment.ProcessId}; session={id}; {message}");
    public event Action<Exception>? Failed;
    internal MediaFoundationVideoSession(Control dispatcher, string path, VideoShellGeneration shell)
    {
        this.dispatcher = dispatcher; this.shell = shell;
        var previous = VideoNative.SetThreadDpiAwarenessContext(VideoNative.GetWindowDpiAwarenessContext(shell.Host));
        render = new();
        try
        {
            if (VideoNative.Resolve() != shell) throw new InvalidOperationException("Shell changed during attachment");
            nint hwnd = render.Handle;
            rendererHandle = hwnd;
            VideoNative.SetStyle(hwnd, -16, (VideoNative.GetStyle(hwnd, -16) & ~unchecked((int)0x80000000)) | 0x40000000);
            VideoNative.SetParent(hwnd, shell.Host);
            if (VideoNative.GetParent(hwnd) != shell.Host) throw new InvalidOperationException("Video SetParent failed");
            VideoNative.ConfigureDesktopComposition(hwnd, shell.DefView);
            var bounds = Screen.AllScreens[0].Bounds;
            screenBounds = bounds;
            var origin = bounds.Location;
            if (!VideoNative.ScreenToClient(shell.Host, ref origin)) throw new InvalidOperationException("Video coordinate conversion failed");
            // A direct disabled child immediately under DefView. No topmost,
            // widget ownership, focus stealing or input interception.
            // A staged decoder must not cover the old stable background.
            if (!VideoNative.SetWindowPos(hwnd, shell.DefView, origin.X, origin.Y, bounds.Width, bounds.Height, 0x10 | 0x20))
                throw new InvalidOperationException("Video placement failed");
            player = new MfPlayer(hwnd, path, OnNativeEvent);
            render.Paint += Paint;
        }
        catch { render.Dispose(); throw; }
        finally { if (previous != 0) VideoNative.SetThreadDpiAwarenessContext(previous); }
    }
    private void Paint(object? sender, PaintEventArgs e) { if (!disposed) player.Update(e.Graphics); }
    private void OnNativeEvent(nint header)
    {
        int type = Marshal.ReadInt32(header), hr = Marshal.ReadInt32(header, 4);
        if (type is 5 or 6) Log($"initialization event={type}; hr=0x{hr:X8}");
        // Native event memory expires when the callback returns.
        // MFPlay's default callback runs on the creating UI thread. Consume the
        // borrowed media item here instead of retaining COM references in a
        // queued delegate that might never run after dispatcher shutdown.
        if (type == 5 && hr >= 0 && !disposed)
        {
            try { player.SetCreatedItem(Marshal.ReadIntPtr(header, 32)); }
            catch (Exception ex) { hr = ex.HResult; }
        }
        try
        {
            dispatcher.BeginInvoke((Action)(() =>
            {
                try
                {
                    if (disposed) return;
                    Marshal.ThrowExceptionForHR(hr);
                    // Play is asynchronous. A policy change may have arrived
                    // while its request was pending in the native pipeline.
                    if (type == 0 && !playing && !priming) player.Pause();
                    if (type == 6) { player.Fill(render.ClientSize); ready.TrySetResult(); }
                    // The configured EVR retains its last presented surface
                    // through the EOS stop/seek, until it presents a new sample.
                    // A Play acknowledgement is not a frame-ready signal.
                    if (type == 11)
                    {
                        Log($"loop; count={++loops}; reasonsPlaying={playing}");
                        player.Seek(0); if (playing || priming) player.Play();
                    }
                }
                catch (Exception ex)
                {
                    failure = ex;
                    Log($"Media Foundation error; event={type}; type={ex.GetType().Name}; hr=0x{ex.HResult:X8}");
                    ready.TrySetException(ex); Failed?.Invoke(ex);
                }
            }));
        }
        catch (InvalidOperationException) { /* Dispatcher is shutting down. */ }
    }
    public Task InitializeAsync(CancellationToken cancellation) => ready.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellation);
    public async Task PrepareAsync(CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (prepared) return;
        priming = true;
        try
        {
            // Silent, hidden preroll produces an actual EVR sample. It never
            // changes the published session's manual/fullscreen pause policy.
            player.Play();
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (!player.HasPresentedFrame)
            {
                if (failure != null) throw failure;
                cancellation.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(disposed, this);
                if (deadline.Elapsed >= TimeSpan.FromSeconds(15)) throw new TimeoutException("First video frame unavailable");
                await Task.Delay(25, cancellation);
            }
            cancellation.ThrowIfCancellationRequested();
            if (failure != null) throw failure;
            player.Pause();
            prepared = true;
            Log("first composited frame ready; hidden=true; mute=true");
        }
        finally { priming = false; }
    }
    public void Present()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!prepared) throw new InvalidOperationException("Video candidate has no prepared frame");
        render.Show();
        var origin = screenBounds.Location;
        if (!VideoNative.ScreenToClient(shell.Host, ref origin) ||
            !VideoNative.SetWindowPos(rendererHandle, shell.DefView, origin.X, origin.Y, screenBounds.Width, screenBounds.Height, 0x10 | 0x20 | 0x40))
            throw new InvalidOperationException("Video publication failed");
        render.Invalidate(); render.Update();
        Log("video presented");
    }
    public bool IsAttached(VideoShellGeneration current) => !disposed && current == shell &&
        !render.IsDisposed && render.RendererHandle == rendererHandle && VideoNative.IsWindow(rendererHandle) &&
        VideoNative.GetParent(rendererHandle) == shell.Host && Screen.AllScreens.Length == 1 && Screen.AllScreens[0].Bounds == screenBounds;
    // Visibility is deliberately not a repair trigger: Show Desktop can change
    // visibility temporarily without destroying the renderer or its shell lease.
    public long Position => player.Position100ns();
    public void Play() { playing = true; player.Play(); }
    public void Pause() { playing = false; player.Pause(); }
    public void Seek(long position) => player.Seek(position);
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; Failed = null; ready.TrySetCanceled();
        render.Paint -= Paint;
        try { player.Dispose(); }
        finally
        {
            try { render.Hide(); render.Dispose(); }
            finally { Log($"video released; loops={loops}"); }
        }
    }
}

internal static class VideoFileValidation
{
    internal static bool IsLocalMp4(string path)
    {
        try
        {
            return Path.IsPathFullyQualified(path) && !path.StartsWith(@"\\", StringComparison.Ordinal) &&
            new DriveInfo(Path.GetPathRoot(path)!).DriveType != DriveType.Network &&
            Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase) && File.Exists(path);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { return false; }
    }
    [DllImport("mfplat.dll")] private static extern int MFStartup(int version, int flags);
    [DllImport("mfplat.dll")] private static extern int MFShutdown();
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint model);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)] private static extern int MFCreateSourceReaderFromURL(string url, nint attributes, out nint reader);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int NativeType(nint reader, uint stream, uint typeIndex, out nint type);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetGuid(nint attributes, ref Guid key, out Guid value);
    private static T Slot<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));
    internal static void ValidateCodec(string path)
    {
        if (!Environment.Is64BitProcess) throw new PlatformNotSupportedException("Video requires x64");
        int com = CoInitializeEx(0, 0);
        if (com < 0 && com != unchecked((int)0x80010106)) Marshal.ThrowExceptionForHR(com);
        bool started = false;
        nint reader = 0, type = 0;
        try
        {
            Marshal.ThrowExceptionForHR(MFStartup(0x20070, 0));
            started = true;
            Marshal.ThrowExceptionForHR(MFCreateSourceReaderFromURL(path, 0, out reader));
            Marshal.ThrowExceptionForHR(Slot<NativeType>(reader, 5)(reader, 0xFFFFFFFC, 0, out type));
            var key = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
            Marshal.ThrowExceptionForHR(Slot<GetGuid>(type, 10)(type, ref key, out var subtype));
            if (subtype != new Guid("34363248-0000-0010-8000-00aa00389b71"))
                throw new NotSupportedException("Phase 1 requires H.264");
        }
        finally
        {
            if (type != 0) Marshal.Release(type);
            if (reader != 0) Marshal.Release(reader);
            if (started) MFShutdown();
            if (com >= 0) CoUninitialize();
        }
    }
}
