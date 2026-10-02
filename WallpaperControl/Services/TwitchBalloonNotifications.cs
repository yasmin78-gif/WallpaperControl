namespace WallpaperControl;

/// <summary>Serializes native tray notifications so each click retains its channel target.</summary>
internal sealed class TwitchBalloonNotifications : IDisposable
{
    private readonly NotifyIcon icon;
    private readonly Queue<(TwitchStream Stream, string Language, DateTimeOffset Created)> pending = new();
    private readonly Func<bool?> fullscreen;
    private readonly Action<string, string> display;
    private readonly Func<Uri, bool> open;
    private readonly Func<bool> visible;
    private Uri? active;
    private bool showing, disposed;
    internal TwitchBalloonNotifications(NotifyIcon icon, Func<bool?>? fullscreen = null,
        Action<string, string>? display = null, Func<Uri, bool>? open = null, Func<bool>? visible = null)
    {
        this.icon = icon; this.fullscreen = fullscreen ?? FullscreenActivityDetector.GetFullscreenState;
        this.display = display ?? ((title, text) => icon.ShowBalloonTip(10_000, title, text, ToolTipIcon.Info));
        this.open = open ?? TwitchLinks.Open; this.visible = visible ?? (() => icon.Visible);
        icon.BalloonTipClicked += Clicked; icon.BalloonTipClosed += Closed;
    }
    internal void Show(TwitchStream stream, string language)
    {
        if (disposed || !visible() || fullscreen() != false) return;
        // Keep only fresh alerts; never build an unbounded queue when Windows suppresses notifications.
        if (pending.Count < 32) pending.Enqueue((stream, language, DateTimeOffset.UtcNow));
        Pump();
    }
    internal void Clear() { pending.Clear(); active = null; }
    private void Pump()
    {
        if (showing || disposed) return;
        if (fullscreen() != false) { Clear(); return; }
        while (pending.TryDequeue(out var item))
        {
            if (DateTimeOffset.UtcNow - item.Created > TimeSpan.FromMinutes(2)) continue;
            active = TwitchLinks.Channel(item.Stream.Login);
            if (active == null) continue;
            showing = true;
            try { display(Localization.Get("TwitchLiveNotificationTitle", item.Language),
                string.Format(Localization.Get("TwitchLiveNotificationBody", item.Language), item.Stream.DisplayName)); }
            catch { active = null; showing = false; throw; }
            return;
        }
    }
    private void Clicked(object? sender, EventArgs e) => OnClicked();
    internal void OnClicked()
    {
        if (active is Uri uri && fullscreen() == false) open(uri);
        active = null;
        // Windows may send Clicked without Closed. Retire this balloon before presenting the next one.
        showing = false; Pump();
    }
    private void Closed(object? sender, EventArgs e) => OnClosed();
    internal void OnClosed() { active = null; showing = false; Pump(); }
    public void Dispose() { if (disposed) return; disposed = true; Clear(); icon.BalloonTipClicked -= Clicked; icon.BalloonTipClosed -= Closed; }
}
