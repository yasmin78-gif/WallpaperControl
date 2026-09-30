namespace WallpaperControl;

/// <summary>
/// UI-thread scheduler. A timer evaluates current data, never queues missed intervals.
/// Network awaits return to the UI context; PackageTrackingService owns the shared operation gate.
/// </summary>
internal sealed class PackageRefreshScheduler : IDisposable
{
    private readonly PackageTrackingService service;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly Action<string> log;
    private readonly bool useTimer;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<Guid, DateTimeOffset> attempts = new();
    private System.Windows.Forms.Timer? timer;
    private bool enabled, suspended, powerSuspended, running, disposed;
    private int intervalMinutes = 30;
    private string nextReason = "startup";
    internal static int NormalizeInterval(int minutes) => minutes is 15 or 30 or 60 or 120 ? minutes : 30;

    internal PackageRefreshScheduler(PackageTrackingService service, Func<DateTimeOffset>? utcNow = null,
        Action<string>? log = null, bool useTimer = true)
    {
        this.service = service; this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        this.log = log ?? AppLogger.Info; this.useTimer = useTimer;
        service.Disposing += Dispose;
    }

    internal void Configure(bool automatic, int minutes)
    {
        if (disposed) return;
        int normalized = NormalizeInterval(minutes);
        bool changed = enabled != automatic || intervalMinutes != normalized;
        if (!changed) return;
        enabled = automatic; intervalMinutes = normalized;
        log($"Package scheduler {(enabled ? "enabled" : "disabled")}; interval={intervalMinutes} minutes.");
        if (!enabled) { StopTimer(); return; }
        PlanSoon(timer == null ? "startup/settings" : "settings");
    }

    internal void SetSuspended(bool value)
    {
        if (disposed || suspended == value) return;
        suspended = value;
        if (value) log("Package scheduler fullscreen/activity deferral.");
        else PlanSoon("fullscreen/activity resume");
    }

    internal void SetPowerSuspended(bool value)
    {
        if (disposed) return;
        powerSuspended = value;
        if (!value) PlanSoon("power resume");
    }

    private void PlanSoon(string reason)
    {
        if (!enabled || disposed) return;
        nextReason = reason;
        if (!useTimer) return;
        if (timer == null) { timer = new System.Windows.Forms.Timer(); timer.Tick += Tick; }
        // Defer startup/resume/settings work until the message loop has finished the current UI operation.
        timer.Interval = 1000; timer.Start();
    }

    private async void Tick(object? sender, EventArgs e)
    {
        if (timer == null || disposed) return;
        timer.Interval = 30_000;
        string reason = nextReason; nextReason = "timer";
        await EvaluateAsync(reason);
    }

    private bool Eligible(TrackedShipment shipment, DateTimeOffset now)
    {
        if (!enabled || disposed || suspended || powerSuspended || shipment.TrackingMode != "provider"
            || shipment.Provider != "ship24" || string.IsNullOrWhiteSpace(shipment.ProviderTrackerId)
            || shipment.StatusMilestone == "delivered") return false;
        var interval = TimeSpan.FromMinutes(intervalMinutes);
        return (shipment.LastSuccessfulRefresh == null || now - shipment.LastSuccessfulRefresh.Value >= interval)
            && (!attempts.TryGetValue(shipment.Id, out var attempted) || now - attempted >= interval);
    }

    internal async Task EvaluateAsync(string reason = "timer")
    {
        if (disposed || !enabled || suspended || powerSuspended || running || service.Busy || !service.CanWrite) return;
        var now = utcNow();
        var shipments = service.Shipments;
        foreach (var id in attempts.Keys.Where(id => !shipments.Any(s => s.Id == id)).ToArray()) attempts.Remove(id);
        int eligible = shipments.Count(s => Eligible(s, now));
        if (eligible == 0 && reason == "timer") return;
        log($"Package scheduler evaluation ({reason}): eligible={eligible}, local/manual={shipments.Count(s => s.TrackingMode != "provider")}, delivered={shipments.Count(s => s.TrackingMode == "provider" && s.StatusMilestone == "delivered")}, fresh={shipments.Count(s => s.TrackingMode == "provider" && s.StatusMilestone != "delivered" && s.LastSuccessfulRefresh != null && now - s.LastSuccessfulRefresh.Value < TimeSpan.FromMinutes(intervalMinutes))}.");
        if (eligible == 0) return;
        running = true;
        var started = new List<Guid>();
        log("Package automatic cycle started.");
        try
        {
            var result = await service.RefreshAutomaticAsync(s => Eligible(s, utcNow()), s =>
            { attempts[s.Id] = utcNow(); started.Add(s.Id); }, lifetime.Token);
            foreach (var id in started)
            {
                // Backoff starts at completion, including timeouts; no immediate retry after a long request.
                if (service.RefreshFailures.TryGetValue(id, out var failure))
                { attempts[id] = utcNow(); log($"Package automatic provider failure: {failure}."); }
                else if (result.Outcome == PackageOperation.StorageError) attempts[id] = utcNow();
                else attempts.Remove(id); // Successful data schedules itself via LastSuccessfulRefresh.
            }
            log($"Package automatic cycle completed: attempted={started.Count}, result={result.Outcome}.");
        }
        catch (OperationCanceledException) { log("Package automatic cycle cancelled."); }
        catch (Exception ex)
        {
            foreach (var id in started) attempts[id] = utcNow();
            // Only the exception type is safe: transport messages can contain URLs or response content.
            log($"Package automatic cycle failed: {ex.GetType().Name}.");
        }
        finally { running = false; }
    }

    private void StopTimer()
    {
        if (timer == null) return;
        timer.Stop(); timer.Tick -= Tick; timer.Dispose(); timer = null;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; enabled = false; StopTimer(); service.Disposing -= Dispose;
        lifetime.Cancel(); lifetime.Dispose();
        log("Package scheduler disposed.");
    }
}
