namespace WallpaperControl;

/// <summary>Observes complete successful snapshots only. No additional network requests.</summary>
internal sealed class TwitchLiveNotifications : IDisposable
{
    private readonly TwitchService service;
    private readonly Action<TwitchStream> notify;
    private readonly Func<bool?> fullscreen;
    private readonly Dictionary<string, DateTimeOffset> seen = new(StringComparer.Ordinal);
    private string user = "";
    private bool enabled, suspended, baselinePending = true;
    internal TwitchLiveNotifications(TwitchService service, Action<TwitchStream> notify, Func<bool?>? fullscreen = null)
    { this.service = service; this.notify = notify; this.fullscreen = fullscreen ?? FullscreenActivityDetector.GetFullscreenState; service.Refreshed += Observe; service.Changed += ConnectionChanged; }
    internal void Configure(bool value)
    {
        if (enabled == value) return;
        enabled = value; baselinePending = true; seen.Clear();
        if (value && service.LastUpdated != null && service.Error == TwitchError.None)
        {
            user = service.UserId; baselinePending = false;
            foreach (var stream in service.Streams) seen[stream.UserId] = stream.StartedAt;
        }
    }
    internal void SetSuspended(bool value)
    { if (suspended == value) return; suspended = value; if (value) baselinePending = true; }
    private void ConnectionChanged()
    { if (service.LastUpdated == null) { baselinePending = true; seen.Clear(); user = ""; } }
    private void Observe()
    {
        if (!enabled) return;
        if (user != service.UserId) { seen.Clear(); baselinePending = true; user = service.UserId; }
        bool suppress = baselinePending || suspended || fullscreen() != false;
        baselinePending = false;
        foreach (var stream in service.Streams)
        {
            if (stream.UserId.Length == 0 || stream.StartedAt == default) continue;
            bool fresh = !seen.TryGetValue(stream.UserId, out var previous) || stream.StartedAt > previous;
            if (!fresh) continue;
            seen[stream.UserId] = stream.StartedAt;
            if (!suppress && TwitchLinks.Channel(stream.Login) != null)
            {
                try { notify(stream); }
                catch (Exception ex) { AppLogger.Info($"Twitch notification failed: {ex.GetType().Name}."); }
            }
        }
    }
    public void Dispose() { service.Refreshed -= Observe; service.Changed -= ConnectionChanged; seen.Clear(); }
}
