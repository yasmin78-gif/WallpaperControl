namespace WallpaperControl;

/// <summary>One serialized OAuth/API session for settings and widget. All published data is a complete snapshot.</summary>
internal sealed partial class TwitchService : IDisposable
{
    private readonly ITwitchApi api;
    private readonly ITwitchTokenStore store;
    private readonly Func<DateTimeOffset> now;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? operation;
    private TwitchTokens? credentials;
    private TwitchIdentity? identity;
    private bool loaded, disposed;
    private int busy, generation;
    private DateTimeOffset validatedAt, retryAt, attemptedAt;
    internal bool Busy => Volatile.Read(ref busy) != 0;
    internal bool Connected => identity != null;
    internal string UserId => identity?.UserId ?? "";
    internal string EmptyStateKey => Error != TwitchError.None ? "TwitchError" + Error
        : !loaded || Busy && LastUpdated == null || Connected && LastUpdated == null ? "TwitchLoading"
        : Connected ? "TwitchEmpty" : "TwitchDisconnected";
    internal event Action? Refreshed;
    internal string DisplayName => identity?.DisplayName ?? "";
    internal TwitchError Error { get; private set; }
    internal IReadOnlyList<TwitchStream> Streams { get; private set; } = Array.Empty<TwitchStream>();
    internal DateTimeOffset? LastUpdated { get; private set; }
    internal event Action? Changed;
    internal TwitchService(ITwitchApi api, ITwitchTokenStore store, Func<DateTimeOffset>? now = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    { this.api = api; this.store = store; this.now = now ?? (() => DateTimeOffset.UtcNow); this.delay = delay ?? Task.Delay; }

    private void Notify() { if (!disposed) Changed?.Invoke(); }
    private void Fail(TwitchException ex)
    {
        Error = ex.Error;
        if (ex.Error is TwitchError.Reconnect or TwitchError.Unauthorized)
        {
            credentials = null; identity = null; Streams = Array.Empty<TwitchStream>(); LastUpdated = null;
            try { store.Clear(); } catch (TwitchException) { Error = TwitchError.Storage; }
        }
        retryAt = ex.Error == TwitchError.RateLimit ? ex.RetryAfter ?? now().AddMinutes(5) : now().AddMinutes(5);
        AppLogger.Info($"Twitch operation failed: {ex.Error}.");
    }
    private async Task<bool> RunAsync(Func<CancellationToken, Task> work, CancellationToken token)
    {
        if (disposed || Interlocked.CompareExchange(ref busy, 1, 0) != 0) return false;
        int current = generation;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        operation = linked; Notify();
        try { await work(linked.Token); linked.Token.ThrowIfCancellationRequested(); return current == generation; }
        catch (OperationCanceledException) { if (!disposed && current == generation) Error = TwitchError.Cancelled; return false; }
        catch (TwitchException ex) { if (!disposed && current == generation) Fail(ex); return false; }
        catch (Exception ex) { if (!disposed && current == generation) { Error = TwitchError.Unavailable; retryAt = now().AddMinutes(5); AppLogger.Info($"Twitch operation failed: {ex.GetType().Name}."); } return false; }
        finally { operation = null; Interlocked.Exchange(ref busy, 0); Notify(); }
    }
    private void Load()
    { if (!loaded) { credentials = store.Load(); loaded = true; } }
    private async Task ValidateAsync(CancellationToken token, bool force = false)
    {
        Load(); if (credentials == null) return;
        if (!force && identity != null && now() - validatedAt < TimeSpan.FromHours(1)) return;
        TwitchIdentity user;
        try { user = await api.ValidateAsync(credentials.AccessToken, token); }
        catch (TwitchException ex) when (ex.Error == TwitchError.Reconnect && credentials.RefreshToken.Length > 0)
        {
            // Public-client refresh tokens rotate and are single use: persist the new pair before any next request.
            var replacement = await api.RefreshTokenAsync(credentials.RefreshToken, token);
            token.ThrowIfCancellationRequested(); store.Save(replacement); credentials = replacement;
            user = await api.ValidateAsync(replacement.AccessToken, token);
        }
        token.ThrowIfCancellationRequested();
        string name = identity?.UserId == user.UserId ? identity.DisplayName : await api.GetDisplayNameAsync(credentials.AccessToken, user.UserId, token);
        token.ThrowIfCancellationRequested(); identity = user with { DisplayName = name }; validatedAt = now(); Error = TwitchError.None;
    }
    internal Task<bool> InitializeAsync(CancellationToken token = default) => now() < retryAt ? Task.FromResult(false) : RunAsync(async ct =>
    { await ValidateAsync(ct); }, token);
    internal Task<bool> ValidateIfDueAsync(CancellationToken token = default) => !Connected || now() - validatedAt < TimeSpan.FromHours(1)
        ? Task.FromResult(true) : InitializeAsync(token);
    internal Task<bool> ConnectAsync(Action<TwitchDeviceCode> showCode, CancellationToken token = default) => Error == TwitchError.RateLimit && now() < retryAt
        ? Task.FromResult(false) : RunAsync(async ct =>
    {
        var code = await api.StartDeviceAsync(ct); ct.ThrowIfCancellationRequested(); showCode(code);
        var expires = now().AddSeconds(code.ExpiresIn); int interval = code.Interval;
        while (now() < expires)
        {
            await delay(TimeSpan.FromSeconds(interval), ct); ct.ThrowIfCancellationRequested();
            if (now() >= expires) break;
            TwitchTokens? pair;
            try { pair = await api.PollDeviceAsync(code.DeviceCode, ct); }
            catch (TwitchApi.SlowDownException) { interval = checked(interval + 5); continue; }
            if (pair == null) continue;
            var user = await api.ValidateAsync(pair.AccessToken, ct);
            string name = await api.GetDisplayNameAsync(pair.AccessToken, user.UserId, ct);
            ct.ThrowIfCancellationRequested(); store.Save(pair); credentials = pair; loaded = true;
            identity = user with { DisplayName = name }; validatedAt = now(); Error = TwitchError.None; retryAt = default; attemptedAt = default;
            Streams = Array.Empty<TwitchStream>(); LastUpdated = null;
            AppLogger.Info("Twitch connected."); return;
        }
        throw new TwitchException(TwitchError.Expired);
    }, token);
    internal Task<bool> RefreshAsync(CancellationToken token = default)
    {
        if (disposed || now() < retryAt || (attemptedAt != default && now() - attemptedAt < TimeSpan.FromSeconds(10))) return Task.FromResult(false);
        return RunAsync(async ct =>
        {
            attemptedAt = now(); await ValidateAsync(ct);
            if (identity == null || credentials == null) return;
            IReadOnlyList<TwitchStream> streams;
            try { streams = await api.GetStreamsAsync(credentials.AccessToken, identity.UserId, ct); }
            catch (TwitchException ex) when (ex.Error == TwitchError.Reconnect)
            {
                await ValidateAsync(ct, force: true); // One refresh/validation and one retry, never a request loop.
                streams = await api.GetStreamsAsync(credentials!.AccessToken, identity!.UserId, ct);
            }
            ct.ThrowIfCancellationRequested(); Streams = streams; LastUpdated = now(); Error = TwitchError.None;
            AppLogger.Info($"Twitch refreshed: live={streams.Count}.");
            Refreshed?.Invoke();
        }, token);
    }
    internal void CancelPending() { generation++; operation?.Cancel(); }
    internal void Disconnect()
    {
        CancelPending(); credentials = null; identity = null; loaded = true; Streams = Array.Empty<TwitchStream>(); LastUpdated = null;
        try { store.Clear(); Error = TwitchError.None; } catch (TwitchException ex) { Error = ex.Error; }
        AppLogger.Info("Twitch disconnected locally."); Notify();
    }
    public void Dispose()
    { if (disposed) return; disposed = true; generation++; lifetime.Cancel(); api.Dispose(); lifetime.Dispose(); Changed = null; Refreshed = null; }
}
