namespace WallpaperControl;

[Flags]
internal enum VideoPauseReason { None = 0, Manual = 1, Fullscreen = 2 }
internal enum VideoWallpaperState { Stopped, Initializing, Playing, Paused, Recovering, Failed }
internal readonly record struct VideoShellGeneration(nint Shell, nint Host, nint DefView, uint ProcessId, long ProcessStartTicks);
internal interface IVideoSession : IDisposable
{
    ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    Task PresentAsync(CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); Present(); return Task.CompletedTask; }
    Task InitializeAsync(CancellationToken cancellation);
    // Candidates stay hidden until a real composited sample is ready.
    Task PrepareAsync(CancellationToken cancellation) => Task.CompletedTask;
    void Present() { }
    void SetAudio(bool enabled, int volume) { }
    Task SynchronizePolicyAsync(CancellationToken cancellation) => Task.CompletedTask;
    bool IsAttached(VideoShellGeneration shell);
    long Position { get; }
    void Play();
    void Pause();
    void Seek(long position);
    Task SeekAsync(long position, CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); Seek(position); return Task.CompletedTask; }
    event Action<Exception>? Failed;
}
internal interface IVideoDesktop
{
    string BackendName => "Windows Media Foundation/MFPlay";
    bool RecoverNativeFailures => false;
    int MonitorCount { get; }
    Task ValidateAsync(string path, CancellationToken cancellation);
    bool FileExists(string path);
    VideoShellGeneration? ResolveShell();
    IVideoSession Create(string path, VideoShellGeneration shell);
}

// UI-thread ownership: at most one published player and one hidden candidate.
// Publication alone replaces a stable session; stale continuations own no state.
internal sealed class VideoWallpaperController : IDisposable
{
    private sealed class OwnedSession(IVideoSession value, Action<string> log)
    {
        internal IVideoSession Value { get; } = value;
        internal Action<Exception>? Handler;
        internal bool AudioFailed;
        private Task? retirement;
        public Task DisposeAsync() => retirement ??= RetireAsync();
        private async Task RetireAsync()
        {
            if (Handler != null) Value.Failed -= Handler;
            Handler = null;
            try { await Value.DisposeAsync(); log("session released; cleanup completed"); }
            catch (Exception ex) { log($"session cleanup failed; type={ex.GetType().Name}; hr=0x{ex.HResult:X8}"); }
        }
    }
    private readonly IVideoDesktop desktop;
    private readonly Func<DateTime> now;
    private readonly Action<string> log;
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private Task cleanup = Task.CompletedTask;
    private bool retiringPublished;
    private OwnedSession? session, initializingSession;
    private CancellationTokenSource? cancellation;
    private VideoShellGeneration? shell;
    private int generation, attempts, policyRevision;
    private bool disposed, desired, ticking, recoverable, starting;
    private long position;
    private string path = "";
    private DateTime nextAttempt;
    internal VideoWallpaperState State { get; private set; }
    internal VideoPauseReason PauseReasons { get; private set; }
    internal string ErrorKey { get; private set; } = "";
    internal int RecoveryAttempts => attempts;
    internal bool HasSession => session != null;
    internal string ActivePath => path;
    internal bool ChangePending => starting;
    internal bool SoundEnabled { get; private set; }
    internal int Volume { get; private set; } = 50;
    internal event Action? Changed;

    internal VideoWallpaperController(IVideoDesktop desktop, Action<string> log, Func<DateTime>? now = null)
    { this.desktop = desktop; this.log = log; this.now = now ?? (() => DateTime.UtcNow); }
    internal void SetAudio(bool enabled, int volume)
    {
        if (disposed) return;
        SoundEnabled = enabled; Volume = Math.Clamp(volume, 0, 100);
        policyRevision++;
        if (session != null && !retiringPublished) ApplyAudio(session);
    }
    private void ApplyAudio(OwnedSession owned)
    {
        if (owned.AudioFailed) return;
        try { owned.Value.SetAudio(SoundEnabled, Volume); }
        catch (Exception ex)
        {
            owned.AudioFailed = true;
            log($"audio control failed; silent fallback; type={ex.GetType().Name}; hr=0x{ex.HResult:X8}");
            try { owned.Value.SetAudio(false, 0); }
            catch { try { owned.Value.Pause(); } catch { /* Shutdown still owns cleanup. */ } }
        }
    }
    private void Status(VideoWallpaperState state) { State = state; Changed?.Invoke(); }
    private VideoWallpaperState PolicyState => PauseReasons == VideoPauseReason.None ? VideoWallpaperState.Playing : VideoWallpaperState.Paused;
    private void InvalidatePending()
    {
        generation++;
        cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null;
        // The transaction owns its candidate until its finally has joined
        // native retirement. Cancellation cannot create a third player.
        var pending = initializingSession; initializingSession = null;
        if (pending != null) QueueCleanup(pending);
    }
    internal void CancelPendingStart()
    {
        bool wasStarting = starting;
        InvalidatePending(); starting = false;
        if (desired && !disposed) cancellation = new();
        if (!wasStarting) return;
        log("media change cancelled; previous session retained");
        if (session != null) Status(PolicyState);
        else if (!recoverable) { desired = false; Status(VideoWallpaperState.Stopped); }
    }
    internal async Task<bool> StartAsync(string selectedPath, Func<Task>? beforeCreate = null)
    {
        if (disposed) return false;
        InvalidatePending();
        cancellation = new(); desired = true; starting = true; ErrorKey = ""; attempts = 0;
        int lease = generation;
        var token = cancellation.Token;
        Status(VideoWallpaperState.Initializing);
        log($"media change requested; request={lease}; from={path}; to={selectedPath}");
        log($"initialization; technology={desktop.BackendName}; mute=true; fill=true");
        bool entered = false;
        try
        {
            await lifecycle.WaitAsync(token); entered = true;
            await cleanup;
            if (!Current(lease)) return false;
            if (desktop.MonitorCount != 1) { RejectChange("VideoErrorMonitor"); return false; }
            if (!desktop.FileExists(selectedPath) || !string.Equals(Path.GetExtension(selectedPath), ".mp4", StringComparison.OrdinalIgnoreCase))
            { RejectChange("VideoErrorFile"); return false; }
            await desktop.ValidateAsync(selectedPath, token);
            if (!Current(lease)) return false;
            // Suspend image ownership only after file/codec validation succeeds.
            if (beforeCreate != null) await beforeCreate();
            if (!Current(lease)) return false;
            var target = desktop.ResolveShell();
            if (target == null)
            {
                if (session != null) throw new InvalidOperationException("Shell hierarchy unavailable during media change");
                path = selectedPath; shell = null; recoverable = true; position = 0; starting = false;
                BeginIncident(); return false;
            }
            return await InitializeAsync(lease, token, selectedPath, target.Value, 0);
        }
        catch (OperationCanceledException) when (!Current(lease) || token.IsCancellationRequested) { return false; }
        catch (Exception ex) { if (Current(lease)) RejectChange("VideoErrorPlayback", ex); return false; }
        finally { if (Current(lease)) starting = false; if (entered) lifecycle.Release(); }
    }
    private bool Current(int lease) => !disposed && desired && generation == lease;
    private async Task<bool> InitializeAsync(int lease, CancellationToken token, string file, VideoShellGeneration target, long restorePosition)
    {
        OwnedSession? candidate = null;
        try
        {
            candidate = new(desktop.Create(file, target), log);
            initializingSession = candidate;
            var owned = candidate;
            Exception? initializationError = null;
            candidate.Handler = ex =>
            {
                initializationError = ex;
                if (!ReferenceEquals(session, owned)) return;
                log($"video backend error; technology={desktop.BackendName}; type={ex.GetType().Name}; hr=0x{ex.HResult:X8}");
                if (starting)
                {
                    // An old decoder error cannot dispose a newer candidate.
                    session = null; QueueCleanup(owned); recoverable = false;
                }
                else if (desktop.RecoverNativeFailures && recoverable && State != VideoWallpaperState.Recovering) BeginIncident();
                else Fail("VideoErrorPlayback", ex);
            };
            candidate.Value.Failed += candidate.Handler;
            await candidate.Value.InitializeAsync(token).WaitAsync(token);
            if (!Current(lease)) return false;
            if (restorePosition > 0) await candidate.Value.SeekAsync(restorePosition, token);
            await candidate.Value.PrepareAsync(token).WaitAsync(token);
            if (!Current(lease)) return false;
            if (initializationError != null) throw initializationError;
            if (desktop.ResolveShell() != target || !candidate.Value.IsAttached(target))
                throw new InvalidOperationException("Shell changed during video initialization");
            ApplyPolicy(candidate.Value); // MFPlay policy errors still reject before commit; mpv remains paused until published.
            await candidate.Value.PresentAsync(token).WaitAsync(token);
            if (!Current(lease)) return false;
            var old = session;
            session = candidate; initializingSession = null; candidate = null;
            path = file; shell = target; position = restorePosition; recoverable = true; ErrorKey = ""; starting = false;
            // Complete old playback shutdown before unmuting the published player.
            // Candidates never receive enabled audio, including stale continuations.
            retiringPublished = true;
            try { if (old != null) await old.DisposeAsync(); await cleanup; }
            finally { retiringPublished = false; }
            // A newer request can cancel immediately after the explicit commit.
            // The committed B remains stable; never reactivate a stopped B.
            if (!ReferenceEquals(session, owned)) return false;
            int policy;
            do
            {
                policy = policyRevision;
                ApplyPolicy(owned.Value); ApplyAudio(owned);
                // A committed B stays stable even if a later request cancelled
                // the preparation token. Wait for real native policy replies.
                await owned.Value.SynchronizePolicyAsync(CancellationToken.None);
            }
            while (ReferenceEquals(session, owned) && policy != policyRevision);
            if (!ReferenceEquals(session, owned)) return false;
            log($"video started; request={lease}; file={file}; reasons={PauseReasons}");
            if (Current(lease)) Status(PolicyState);
            LogPlayback();
            if (attempts > 0) log($"recovery success; attempts={attempts}");
            else log("media change committed; previous session released");
            return true;
        }
        finally
        {
            if (ReferenceEquals(initializingSession, candidate)) initializingSession = null;
            if (candidate != null) await candidate.DisposeAsync();
        }
    }
    private void LogPlayback() => log(PauseReasons == VideoPauseReason.None ? "playback resumed; reasons=None" : $"playback paused; reasons={PauseReasons}");
    internal void SetPause(VideoPauseReason reason, bool enabled)
    {
        if (disposed) return;
        var previous = PauseReasons;
        PauseReasons = enabled ? previous | reason : previous & ~reason;
        if (previous == PauseReasons) return;
        policyRevision++;
        log($"pause policy; reason={reason}; enabled={enabled}; reasons={PauseReasons}");
        try
        {
            if (session != null)
            {
                if (!retiringPublished) ApplyPolicy(session.Value);
                if (!starting) Status(PolicyState); else Changed?.Invoke();
                LogPlayback();
            }
            else Changed?.Invoke();
        }
        catch (Exception ex) { Fail("VideoErrorPlayback", ex); }
    }
    private void ApplyPolicy(IVideoSession value)
    { if (PauseReasons == VideoPauseReason.None) value.Play(); else value.Pause(); }
    private void CapturePosition()
    { try { if (session != null) position = session.Value.Position; } catch { /* Retain last successful sample. */ } }
    private void ReleaseSession()
    {
        var old = session; session = null;
        if (old != null) QueueCleanup(old);
        // The active transaction retires its own candidate after cancellation.
    }
    private void QueueCleanup(OwnedSession value) => cleanup = JoinCleanupAsync(cleanup, value);
    private static async Task JoinCleanupAsync(Task previous, OwnedSession value)
    { await previous; await value.DisposeAsync(); }
    internal async Task StopAsync()
    {
        Stop();
        await lifecycle.WaitAsync();
        try { await cleanup; log("playback stopped; cleanup completed"); } finally { lifecycle.Release(); }
    }
    private void BeginIncident()
    {
        CapturePosition(); ReleaseSession();
        attempts = 0; nextAttempt = now().AddSeconds(1);
        Status(VideoWallpaperState.Recovering); log("recovery incident started");
    }
    internal async Task TickAsync()
    {
        if (disposed || !desired || ticking || starting || !recoverable) return;
        ticking = true;
        int lease = generation;
        var token = cancellation?.Token ?? CancellationToken.None;
        bool entered = false;
        try
        {
            await lifecycle.WaitAsync(token); entered = true;
            await cleanup;
            if (!Current(lease)) return;
            if (!desktop.FileExists(path)) { recoverable = false; Fail("VideoErrorFile"); return; }
            if (desktop.MonitorCount != 1) { recoverable = false; Fail("VideoErrorMonitor"); return; }
            var resolved = desktop.ResolveShell();
            bool newShell = resolved != null && resolved != shell;
            if (newShell)
            {
                log("Shell generation changed"); shell = resolved;
                if (State != VideoWallpaperState.Recovering) BeginIncident();
                else log("shell loss coalesced with current recovery incident");
            }
            else if (session != null && (resolved == null || !session.Value.IsAttached(resolved.Value)))
            { log("renderer lost"); BeginIncident(); }
            if (State == VideoWallpaperState.Recovering && now() >= nextAttempt)
            {
                attempts++; log($"recovery attempt={attempts}/3");
                try
                {
                    var target = desktop.ResolveShell() ?? throw new InvalidOperationException("Shell hierarchy unavailable");
                    await desktop.ValidateAsync(path, token);
                    if (!Current(lease)) return;
                    await cleanup;
                    await InitializeAsync(lease, token, path, target, position);
                }
                catch (Exception ex) when (Current(lease))
                {
                    ReleaseSession();
                    log($"recovery attempt failed; type={ex.GetType().Name}; hr=0x{ex.HResult:X8}");
                    if (attempts >= 3) { log("recovery aborted; budget exhausted; attempts=3/3"); Fail("VideoErrorRecovery"); }
                    else nextAttempt = now().AddSeconds(attempts == 1 ? 3 : 6);
                }
            }
            if (Current(lease)) CapturePosition();
        }
        catch (OperationCanceledException) when (!Current(lease)) { }
        catch (Exception ex) { if (Current(lease)) Fail("VideoErrorPlayback", ex); }
        finally { ticking = false; if (entered) lifecycle.Release(); }
    }
    private void RejectChange(string key, Exception? ex = null)
    {
        ErrorKey = key; starting = false;
        log($"media change rejected; previousRetained={session != null}; status={key}; type={ex?.GetType().Name ?? "policy"}; hr=0x{ex?.HResult ?? 0:X8}");
        if (session != null) Status(PolicyState);
        else { recoverable = false; Status(VideoWallpaperState.Failed); }
    }
    private void Fail(string key, Exception? ex = null)
    {
        InvalidatePending(); starting = false;
        ReleaseSession(); ErrorKey = key;
        log($"failed safely; status={key}; type={ex?.GetType().Name ?? "policy"}; hr=0x{ex?.HResult ?? 0:X8}");
        Status(VideoWallpaperState.Failed);
        // Only a new Shell generation or explicit Apply opens another budget.
    }
    internal void Stop()
    {
        InvalidatePending(); desired = starting = false;
        ReleaseSession(); attempts = 0; ErrorKey = ""; recoverable = false; path = ""; shell = null; position = 0;
        Status(VideoWallpaperState.Stopped); log("playback stop requested; native cleanup queued");
    }
    public void Dispose() { if (disposed) return; disposed = true; Stop(); Changed = null; }
}
