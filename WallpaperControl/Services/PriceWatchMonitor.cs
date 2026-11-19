namespace WallpaperControl;

internal sealed class PriceWatchMonitor : IDisposable
{
    private readonly PriceWatchStore store;
    private readonly Func<bool, Guid, int[], CancellationToken, Task<PriceWatchReply<PriceWatchPrices[]>?>> fetch;
    private readonly Action<PriceWatchAlert, bool> notify;
    private readonly Func<Action, Task> ui;
    private readonly Func<Task> changed;
    private readonly Func<DateTimeOffset> clock;
    private readonly TimeZoneInfo? configuredZone;
    private TimeZoneInfo Zone => configuredZone ?? TimeZoneInfo.Local;
    private readonly bool useTimer;
    private readonly System.Windows.Forms.Timer timer = new();
    private CancellationTokenSource? active;
    private bool enabled, notifications, live, running, disposed, startup = true;
    private int[] shops = [];
    private int version;
    internal PriceWatchException? LastFailure { get; private set; }

    internal PriceWatchMonitor(PriceWatchStore store,
        Func<bool, Guid, int[], CancellationToken, Task<PriceWatchReply<PriceWatchPrices[]>?>> fetch,
        Action<PriceWatchAlert, bool> notify, Func<Task> changed, Func<DateTimeOffset>? clock = null,
        TimeZoneInfo? zone = null, bool useTimer = true, Func<Action, Task>? ui = null)
    {
        this.store = store; this.fetch = fetch; this.notify = notify; this.changed = changed;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow); configuredZone = zone; this.useTimer = useTimer;
        this.ui = ui ?? (action => { action(); return Task.CompletedTask; });
        timer.Tick += Tick;
        if (useTimer)
        {
            Microsoft.Win32.SystemEvents.TimeChanged += TimeChanged;
            Microsoft.Win32.SystemEvents.PowerModeChanged += PowerChanged;
        }
    }

    internal void Configure(bool enabled, bool notifications, bool live, int[] shops)
    {
        if (disposed) return;
        if (this.enabled == enabled && this.notifications == notifications && this.live == live && this.shops.SequenceEqual(shops))
            return;
        version++;
        active?.Cancel();
        timer.Stop();
        startup = true;
        this.enabled = enabled; this.notifications = notifications; this.live = live; this.shops = shops.ToArray();
        if (enabled && useTimer) { timer.Interval = 1000; timer.Start(); }
    }

    internal void Invalidate()
    {
        version++;
        active?.Cancel();
    }

    private async void TimeChanged(object? sender, EventArgs e)
    {
        TimeZoneInfo.ClearCachedData();
        await ReplanAsync();
    }

    private async void PowerChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Resume) await ReplanAsync();
    }

    private Task ReplanAsync() => ui(() =>
    {
        if (!enabled || disposed) return;
        startup = false;
        timer.Stop();
        timer.Interval = 1000;
        timer.Start();
    });

    private async void Tick(object? sender, EventArgs e)
    {
        timer.Stop();
        await EvaluateAsync();
        await ui(() =>
        {
            if (!enabled || disposed || !useTimer) return;
            var next = PriceWatchSchedule.Slot(clock(), Zone, next: true);
            timer.Interval = (int)Math.Clamp((next - clock()).TotalMilliseconds, 1000, int.MaxValue);
            timer.Start();
        });
    }

    internal async Task EvaluateAsync(bool force = false)
    {
        if (!enabled || disposed || running) return;
        bool mode = live;
        int generation = version;
        int[] selected = shops.ToArray();
        var now = clock();
        bool startupCheck = startup;
        startup = false;
        CancellationTokenSource? operation = null;
        try
        {
            if (!force && !PriceWatchSchedule.Due(now, Zone, store.ReadMonitorState(mode), startupCheck)) return;
            running = true;
            LastFailure = null;
            operation = new CancellationTokenSource();
            active = operation;
            store.SaveMonitorState(mode, now, success: false);
            bool success = true;
            foreach (var expected in store.Read(mode).Where(entry => entry.TargetCents != null))
            {
                operation.Token.ThrowIfCancellationRequested();
                PriceWatchReply<PriceWatchPrices[]>? reply;
                try { reply = await fetch(mode, expected.Game.Id, selected, operation.Token); }
                catch (PriceWatchException error)
                {
                    success = false;
                    await ui(() => { if (generation == version && !disposed) LastFailure = error; });
                    if (error.Failure == PriceWatchFailure.Busy) break;
                    continue;
                }
                if (disposed || generation != version || operation.IsCancellationRequested) return;
                await ui(() =>
                {
                    if (disposed || generation != version || operation.IsCancellationRequested) { success = false; return; }
                    var current = store.Read(mode).FirstOrDefault(entry => entry.Game.Id == expected.Game.Id);
                    if (current?.TargetCents != expected.TargetCents) { success = false; return; }
                    if (reply == null || !ValidReply(expected.Game.Id, selected, reply))
                    {
                        success = false;
                        LastFailure = new(PriceWatchFailure.InvalidResponse);
                        return;
                    }
                    if (!store.SaveSnapshot(mode, expected.Game.Id, reply)) { success = false; return; }
                    Observe(mode, expected, selected, reply);
                });
            }
            await ui(() =>
            {
                if (success && generation == version && !disposed)
                    store.SaveMonitorState(mode, clock(), success: true);
            });
            await changed();
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            // Transport exception text can include credentials or response bodies.
            AppLogger.Info($"PriceWatch automatic check failed: {error.GetType().Name}.");
        }
        finally
        {
            await ui(() =>
            {
                if (operation != null && active == operation) { active = null; running = false; }
            });
            operation?.Dispose();
        }
    }

    internal static bool ValidReply(Guid id, int[] selected, PriceWatchReply<PriceWatchPrices[]> reply) =>
        reply.FetchedAt != default && reply.Data.Length <= 1
        && reply.Data.All(item => item.Id == id && item.Deals != null && item.Deals.All(offer =>
            selected.Contains(offer.Shop.Id) && PriceWatchShops.ValidOffer(offer with { Expiry = null }, DateTimeOffset.UtcNow)));

    internal void Observe(bool mode, PriceWatchEntry expected, int[] selected, PriceWatchReply<PriceWatchPrices[]> reply)
    {
        if (!enabled || disposed || live != mode || expected.TargetCents == null || !shops.SequenceEqual(selected)
            || !ValidReply(expected.Game.Id, selected, reply)) return;
        var alert = store.ObservePriceAlert(mode, expected, selected, reply, clock());
        if (alert != null && notifications) notify(alert, mode);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; enabled = false;
        active?.Cancel();
        if (useTimer)
        {
            Microsoft.Win32.SystemEvents.TimeChanged -= TimeChanged;
            Microsoft.Win32.SystemEvents.PowerModeChanged -= PowerChanged;
        }
        timer.Dispose();
    }
}
