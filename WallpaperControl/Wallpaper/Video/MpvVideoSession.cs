using System.Globalization;

namespace WallpaperControl.Video;

internal sealed class MpvVideoSession : IVideoSession
{
    private readonly Control dispatcher;
    private readonly MpvVideoHost host;
    private readonly MpvClient client;
    private readonly string file;
    private readonly VideoShellGeneration shell;
    private readonly TaskCompletionSource loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource frame = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object gate = new();
    private bool firstFrame, restarted, awaitingSeek, disposed, prepared, published;
    private Task? retirement;
    private long position;
    private static long nextId;
    internal long Id { get; } = Interlocked.Increment(ref nextId);
    internal static int LiveSessions;
    internal static int PeakSessions;
    internal (nint Host, nint Child) Windows => (host.Handle, host.Child);
    internal int LoadedEvents { get; private set; }
    internal int RestartEvents { get; private set; }
    public event Action<Exception>? Failed;
    internal MpvVideoSession(Control dispatcher, MpvRuntime runtime, string file, VideoShellGeneration shell)
    {
        this.dispatcher = dispatcher; this.file = file; this.shell = shell;
        host = new(shell);
        try { client = new(runtime, host.Handle, Receive); PeakSessions = Math.Max(PeakSessions, Interlocked.Increment(ref LiveSessions)); }
        catch { host.Dispose(); throw; }
        Log($"created; hidden=true; mute=true; live={LiveSessions}; peak={PeakSessions}");
    }
    private void Log(string text) => AppLogger.Info($"Video mpv: pid={Environment.ProcessId}; session={Id}; {text}");
    private void Receive(MpvClient.Event item)
    {
        lock (gate)
        {
            if (disposed) return;
            if (item.Type == 8) { LoadedEvents++; loaded.TrySetResult(); Log($"file loaded; count={LoadedEvents}"); }
            // This internal marker is encapsulated behind the SHA/build/ABI gate.
            // video-latency-hacks=no makes the pinned VO wait for presentation.
            if (item.Type == 20 && awaitingSeek) { awaitingSeek = false; firstFrame = restarted = false; }
            if (!awaitingSeek && item.Type == 2 && item.Text.Contains("first video frame after restart shown", StringComparison.Ordinal)) firstFrame = true;
            if (!awaitingSeek && item.Type == 21)
            {
                restarted = true; RestartEvents++;
                double at = System.Diagnostics.Stopwatch.GetTimestamp() * 1000d / System.Diagnostics.Stopwatch.Frequency;
                Log($"playback restart; count={RestartEvents}; qpcMs={at.ToString("F3", CultureInfo.InvariantCulture)}; frameDrops={client.Read("frame-drop-count")}; decoderDrops={client.Read("decoder-frame-drop-count")}");
            }
            if (firstFrame && restarted) frame.TrySetResult();
            if (item.Error < 0 || item.Type is -1 or 1 or 7)
            {
                Log($"native event failure; event={item.Type}; nativeError={item.Error}; detail={item.Text}");
                var error = new InvalidOperationException($"mpv event={item.Type}; nativeError={item.Error}");
                loaded.TrySetException(error); frame.TrySetException(error); DispatchFailure(error);
            }
        }
    }
    private void DispatchFailure(Exception error)
    {
        Log($"native failure; type={error.GetType().Name}; hr=0x{error.HResult:X8}");
        try { dispatcher.BeginInvoke((Action)(() => { if (!disposed) Failed?.Invoke(error); })); }
        catch (InvalidOperationException) { /* UI shutdown owns retirement. */ }
    }
    public async Task InitializeAsync(CancellationToken cancellation)
    {
        await client.Initialized.WaitAsync(TimeSpan.FromSeconds(15), cancellation);
        await client.CommandAsync("loadfile", file).WaitAsync(TimeSpan.FromSeconds(15), cancellation);
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellation);
    }
    public async Task PrepareAsync(CancellationToken cancellation)
    {
        Task readiness;
        lock (gate) readiness = frame.Task;
        await readiness.WaitAsync(TimeSpan.FromSeconds(15), cancellation);
        await client.SetAsync("pause", "yes").WaitAsync(TimeSpan.FromSeconds(15), cancellation);
        await client.SetAsync("mute", "yes").WaitAsync(TimeSpan.FromSeconds(15), cancellation);
        await client.Queue(() =>
        {
            if (client.Read("pause") != "yes" || client.Read("mute") != "yes" ||
                !double.TryParse(client.Read("video-out-params/w"), CultureInfo.InvariantCulture, out double width) || width <= 0)
                throw new InvalidOperationException("mpv candidate readiness incomplete");
        }).WaitAsync(TimeSpan.FromSeconds(15), cancellation);
        cancellation.ThrowIfCancellationRequested();
        host.FindChild();
        if (VideoNative.Resolve() != shell || !host.Attached(shell)) throw new InvalidOperationException("mpv attachment invalid");
        prepared = true; Log("prepared; frame=true; pause=true; mute=true; attachment=true");
    }
    public Task PresentAsync(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!prepared || VideoNative.Resolve() != shell || !host.Attached(shell)) throw new InvalidOperationException("mpv presentation invalid");
        host.Present(); published = true; Log($"presented; host=0x{host.Handle:X}; child=0x{host.Child:X}");
        return Task.CompletedTask;
    }
    public void Present() => throw new InvalidOperationException("mpv requires asynchronous lifecycle");
    internal static double MapVolume(int volume) => 100 * Math.Cbrt(Math.Clamp(volume, 0, 100) / 100d);
    private async Task ApplyAsync(string property, string value)
    {
        if (disposed) return;
        try { await client.SetAsync(property, value); }
        catch (Exception ex) { if (!disposed) DispatchFailure(ex); }
    }
    public void SetAudio(bool enabled, int volume)
    {
        _ = ApplyAudioAsync(published && enabled, volume);
    }
    public Task SynchronizePolicyAsync(CancellationToken cancellation) => client.PolicyCompletion.WaitAsync(TimeSpan.FromSeconds(15), cancellation);
    private async Task ApplyAudioAsync(bool enabled, int volume)
    {
        if (disposed) return;
        try
        {
            // One queued transaction avoids policy interleaving at awaits.
            await client.SetManyAsync(("volume", MapVolume(volume).ToString(CultureInfo.InvariantCulture)), ("mute", enabled ? "no" : "yes"));
        }
        catch (Exception ex) { if (!disposed) DispatchFailure(ex); }
    }
    public bool IsAttached(VideoShellGeneration current)
    {
        if (disposed || !host.Attached(current)) return false;
        _ = CapturePositionAsync();
        return true;
    }
    private async Task CapturePositionAsync()
    {
        try { await client.Queue(() => { if (double.TryParse(client.Read("time-pos"), CultureInfo.InvariantCulture, out double seconds)) Interlocked.Exchange(ref position, (long)(seconds * 10_000_000)); }); }
        catch (Exception ex) { if (!disposed) DispatchFailure(ex); }
    }
    public long Position => Interlocked.Read(ref position);
    internal async Task<IReadOnlyDictionary<string, string?>> SnapshotAsync()
    {
        var result = new Dictionary<string, string?>();
        await client.Queue(() =>
        {
            foreach (string key in new[] { "time-pos", "pause", "mute", "volume", "hwdec-current", "frame-drop-count", "decoder-frame-drop-count", "avsync" }) result[key] = client.Read(key);
        });
        return result;
    }
    public void Play() { if (published) _ = ApplyAsync("pause", "no"); }
    public void Pause() { if (published) _ = ApplyAsync("pause", "yes"); }
    public void Seek(long value)
    {
        _ = SeekObservedAsync(value);
    }
    public async Task SeekAsync(long value, CancellationToken cancellation)
    {
        lock (gate) { awaitingSeek = true; firstFrame = restarted = false; frame = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        await client.CommandAsync("seek", (value / 10_000_000d).ToString(CultureInfo.InvariantCulture), "absolute+exact").WaitAsync(TimeSpan.FromSeconds(15), cancellation);
    }
    private async Task SeekObservedAsync(long value)
    {
        try { await SeekAsync(value, CancellationToken.None); }
        catch (Exception ex) { if (!disposed) DispatchFailure(ex); }
    }
    public ValueTask DisposeAsync()
    {
        if (retirement != null) return new(retirement);
        lock (gate)
        {
            disposed = true; Failed = null; loaded.TrySetCanceled(); frame.TrySetCanceled();
            if (loaded.Task.IsFaulted) _ = loaded.Task.Exception;
            if (frame.Task.IsFaulted) _ = frame.Task.Exception;
        }
        return new(retirement = RetireAsync());
    }
    private async Task RetireAsync()
    {
        // Expose the published successor (or the already frozen image) even
        // if a driver takes a long time to complete native destruction.
        host.Hide();
        try { await client.DisposeAsync(); }
        finally { host.Dispose(); Interlocked.Decrement(ref LiveSessions); Log("released; native shutdown acknowledged; host disposed"); }
    }
    public void Dispose() { _ = DisposeAsync(); }
}
