namespace WallpaperControl;

/// <summary>One UI-thread timer per Twitch session; missed intervals are evaluated once on resume.</summary>
internal sealed class TwitchRefreshScheduler : IDisposable
{
    private readonly TwitchService service;
    private readonly Func<DateTimeOffset> now;
    private readonly CancellationTokenSource lifetime = new();
    private readonly System.Windows.Forms.Timer? timer;
    private bool suspended, powerSuspended, disposed, refreshEnabled = true, evaluating;
    private DateTimeOffset next;
    private int intervalMinutes = 5;
    internal int IntervalMinutes => intervalMinutes;
    internal static int NormalizeInterval(int minutes) => minutes is 1 or 5 or 10 or 15 ? minutes : 5;
    internal static DateTimeOffset NextBoundary(DateTimeOffset time, int minutes)
    {
        long ticks = TimeSpan.FromMinutes(NormalizeInterval(minutes)).Ticks;
        return new DateTimeOffset((time.UtcTicks / ticks + 1) * ticks, TimeSpan.Zero);
    }
    internal TwitchRefreshScheduler(TwitchService service, Func<DateTimeOffset>? now = null, bool useTimer = true)
    {
        this.service = service; this.now = now ?? (() => DateTimeOffset.UtcNow);
        service.Changed += SessionChanged;
        if (useTimer) { timer = new() { Interval = 1000 }; timer.Tick += Tick; timer.Start(); }
    }
    internal void Configure(bool enabled, int minutes = 5)
    {
        int interval = NormalizeInterval(minutes);
        if (refreshEnabled == enabled && intervalMinutes == interval) return;
        refreshEnabled = enabled; intervalMinutes = interval; next = default;
        if (timer != null) timer.Interval = 1000;
        if (!enabled) service.CancelPending();
    }
    internal void SetSuspended(bool value) { if (suspended == value) return; suspended = value; if (value) service.CancelPending(); else _ = EvaluateAsync(); }
    internal void SetPowerSuspended(bool value) { if (powerSuspended == value) return; powerSuspended = value; if (value) service.CancelPending(); else _ = EvaluateAsync(); }
    private void SessionChanged() { if (!evaluating && !service.Busy && (service.LastUpdated == null || next == default)) _ = EvaluateAsync(); }
    private async void Tick(object? sender, EventArgs e) { await EvaluateAsync(); }
    internal async Task EvaluateAsync()
    {
        if (disposed || suspended || powerSuspended || service.Busy || evaluating) return;
        evaluating = true;
        try
        {
            if (!refreshEnabled) { await service.ValidateIfDueAsync(lifetime.Token); return; }
            await service.ValidateIfDueAsync(lifetime.Token);
            if (disposed || suspended || powerSuspended || service.Busy) return;
            if (service.LastUpdated != null && now() < next) return;
            if (next == default && service.LastUpdated is DateTimeOffset updated)
            { next = NextBoundary(updated, intervalMinutes); if (now() < next) return; }
            next = NextBoundary(now(), intervalMinutes);
            await service.RefreshAsync(lifetime.Token);
        }
        finally
        {
            evaluating = false;
            if (!disposed && timer != null)
            {
                var boundary = next > now() ? next : NextBoundary(now(), intervalMinutes);
                timer.Interval = refreshEnabled && !suspended && !powerSuspended && service.LastUpdated != null
                    ? (int)Math.Clamp(Math.Ceiling((boundary - now()).TotalMilliseconds), 1, 900_000) : 30_000;
            }
        }
    }
    public void Dispose()
    { if (disposed) return; disposed = true; service.Changed -= SessionChanged; if (timer != null) { timer.Stop(); timer.Tick -= Tick; timer.Dispose(); } lifetime.Cancel(); lifetime.Dispose(); }
}
