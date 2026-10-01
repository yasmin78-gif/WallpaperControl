namespace WallpaperControl;

[Flags]
internal enum VideoPauseReason { None = 0, Manual = 1, Fullscreen = 2 }
internal enum VideoWallpaperState { Stopped, Initializing, Playing, Paused, Recovering, Failed }
internal readonly record struct VideoShellGeneration(nint Shell, nint Host, nint DefView, uint ProcessId, long ProcessStartTicks);
internal interface IVideoSession : IDisposable
{
    Task InitializeAsync(CancellationToken cancellation);
    bool IsAttached(VideoShellGeneration shell);
    long Position { get; }
    void Play();
    void Pause();
    void Seek(long position);
    event Action<Exception>? Failed;
}
internal interface IVideoDesktop
{
    int MonitorCount { get; }
    Task ValidateAsync(string path, CancellationToken cancellation);
    bool FileExists(string path);
    VideoShellGeneration? ResolveShell();
    IVideoSession Create(string path, VideoShellGeneration shell);
}

// All methods and native events are serialized on the application's UI thread.
// The clock and native boundary are injectable; tests never touch Explorer/MF.
internal sealed class VideoWallpaperController : IDisposable
{
    private readonly IVideoDesktop desktop;
    private readonly Func<DateTime> now;
    private readonly Action<string> log;
    private IVideoSession? session;
    private IVideoSession? initializingSession;
    private CancellationTokenSource? cancellation;
    private VideoShellGeneration? shell;
    private int generation, attempts;
    private bool disposed, desired, ticking, recoverable;
    private long position;
    private string path = "";
    private DateTime nextAttempt;
    internal VideoWallpaperState State { get; private set; }
    internal VideoPauseReason PauseReasons { get; private set; }
    internal string ErrorKey { get; private set; } = "";
    internal int RecoveryAttempts => attempts;
    internal event Action? Changed;

    internal VideoWallpaperController(IVideoDesktop desktop, Action<string> log, Func<DateTime>? now = null)
    { this.desktop = desktop; this.log = log; this.now = now ?? (() => DateTime.UtcNow); }
    private void Status(VideoWallpaperState state) { State = state; Changed?.Invoke(); }
    internal async Task StartAsync(string selectedPath)
    {
        Stop();
        if (disposed) return;
        desired = true; recoverable = false; path = selectedPath; position = 0; shell = null;
        cancellation = new();
        int lease = generation;
        var token = cancellation.Token;
        Status(VideoWallpaperState.Initializing);
        log("initialization; technology=Windows Media Foundation/MFPlay; mute=true; fill=true");
        try
        {
            if (desktop.MonitorCount != 1) { Fail("VideoErrorMonitor"); return; }
            if (!desktop.FileExists(path) || !string.Equals(Path.GetExtension(path), ".mp4", StringComparison.OrdinalIgnoreCase))
            { Fail("VideoErrorFile"); return; }
            await desktop.ValidateAsync(path, token);
            if (!Current(lease)) return;
            recoverable = true;
            shell = desktop.ResolveShell();
            if (shell == null) { BeginIncident(); return; }
            await InitializeAsync(lease, token);
        }
        catch (OperationCanceledException) when (!Current(lease) || token.IsCancellationRequested) { }
        catch (Exception ex) { if (Current(lease)) Fail("VideoErrorPlayback", ex); }
    }
    private bool Current(int lease) => !disposed && desired && generation == lease;
    private async Task InitializeAsync(int lease, CancellationToken token)
    {
        IVideoSession? candidate = null;
        try
        {
            candidate = desktop.Create(path, shell!.Value);
            initializingSession = candidate;
            var owned = candidate;
            Exception? initializationError = null;
            candidate.Failed += ex =>
            {
                initializationError = ex;
                if (Current(lease) && ReferenceEquals(session, owned)) Fail("VideoErrorPlayback", ex);
            };
            await candidate.InitializeAsync(token);
            if (!Current(lease)) return;
            if (initializationError != null) throw initializationError;
            if (desktop.ResolveShell() != shell || !candidate.IsAttached(shell!.Value))
                throw new InvalidOperationException("Shell changed during video initialization");
            if (position > 0) candidate.Seek(position);
            session = candidate; candidate = null;
            ApplyPause();
            if (attempts > 0) log($"recovery success; attempts={attempts}");
        }
        finally
        {
            if (ReferenceEquals(initializingSession, candidate) || ReferenceEquals(initializingSession, session)) initializingSession = null;
            candidate?.Dispose();
        }
    }
    internal void SetPause(VideoPauseReason reason, bool enabled)
    {
        var previous = PauseReasons;
        PauseReasons = enabled ? previous | reason : previous & ~reason;
        if (previous == PauseReasons) return;
        log($"pause policy; reason={reason}; enabled={enabled}");
        try { if (session != null) ApplyPause(); else Changed?.Invoke(); }
        catch (Exception ex) { Fail("VideoErrorPlayback", ex); }
    }
    private void ApplyPause()
    {
        if (session == null) return;
        if (PauseReasons == VideoPauseReason.None) { session.Play(); Status(VideoWallpaperState.Playing); log("playback resumed"); }
        else { session.Pause(); Status(VideoWallpaperState.Paused); log($"playback paused; reasons={PauseReasons}"); }
    }
    private void CapturePosition()
    { try { if (session != null) position = session.Position; } catch { /* Retain last successful sample. */ } }
    private void ReleaseSession()
    {
        var old = session; session = null;
        var pending = initializingSession; initializingSession = null;
        try { old?.Dispose(); }
        finally { if (!ReferenceEquals(pending, old)) pending?.Dispose(); }
    }
    private void BeginIncident()
    {
        CapturePosition(); ReleaseSession();
        attempts = 0; nextAttempt = now().AddSeconds(1);
        Status(VideoWallpaperState.Recovering); log("recovery incident started");
    }
    internal async Task TickAsync()
    {
        if (disposed || !desired || ticking || State == VideoWallpaperState.Initializing || !recoverable) return;
        ticking = true;
        int lease = generation;
        try
        {
            if (!desktop.FileExists(path)) { recoverable = false; Fail("VideoErrorFile"); return; }
            if (desktop.MonitorCount != 1) { recoverable = false; Fail("VideoErrorMonitor"); return; }
            var resolved = desktop.ResolveShell(); // Never consult cached HWNDs as authoritative hosts.
            bool newShell = resolved != null && resolved != shell;
            if (newShell)
            {
                log("Shell generation changed"); shell = resolved; BeginIncident();
            }
            else if (session != null && (resolved == null || !session.IsAttached(resolved.Value)))
            { log("renderer lost"); BeginIncident(); }
            if (State == VideoWallpaperState.Recovering && now() >= nextAttempt)
            {
                attempts++; log($"recovery attempt={attempts}/3");
                try
                {
                    shell = desktop.ResolveShell() ?? throw new InvalidOperationException("Shell hierarchy unavailable");
                    // The selected file may have been replaced since startup.
                    // Re-validate before recreating any native playback surface.
                    await desktop.ValidateAsync(path, cancellation!.Token);
                    if (!Current(lease)) return;
                    await InitializeAsync(lease, cancellation!.Token);
                }
                catch (Exception ex) when (Current(lease))
                {
                    ReleaseSession();
                    log($"recovery attempt failed; type={ex.GetType().Name}; hr=0x{ex.HResult:X8}");
                    if (attempts >= 3) Fail("VideoErrorRecovery");
                    else nextAttempt = now().AddSeconds(attempts == 1 ? 3 : 6);
                }
            }
            if (Current(lease)) CapturePosition();
        }
        catch (OperationCanceledException) when (!Current(lease)) { }
        catch (Exception ex) { if (Current(lease)) Fail("VideoErrorPlayback", ex); }
        finally { ticking = false; }
    }
    private void Fail(string key, Exception? ex = null)
    {
        ReleaseSession(); ErrorKey = key;
        log($"failed safely; status={key}; type={ex?.GetType().Name ?? "policy"}; hr=0x{ex?.HResult ?? 0:X8}");
        Status(VideoWallpaperState.Failed);
        // Keep desired mode and the failed generation. Only a genuine new Shell
        // generation or an explicit Apply creates another incident/start request.
    }
    internal void Stop()
    {
        generation++; desired = false;
        cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null;
        ReleaseSession(); attempts = 0; ErrorKey = "";
        Status(VideoWallpaperState.Stopped); log("playback stopped; cleanup completed");
    }
    public void Dispose() { if (disposed) return; disposed = true; Stop(); Changed = null; }
}
