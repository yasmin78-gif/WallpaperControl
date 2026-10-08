namespace WallpaperControl;

/// <summary>One bounded native queue for Twitch and packages, with a stable action per notice.</summary>
internal sealed class DesktopNotifications : IDisposable
{
    private sealed record Notice(string Title, string Body, Action Click, string Group, DateTimeOffset Created);
    private readonly NotifyIcon icon;
    private readonly Queue<Notice> pending = new();
    private readonly Func<bool?> fullscreen;
    private readonly Action<string, string> display;
    private readonly Func<Uri, bool> open;
    private readonly Func<bool> visible;
    private Notice? active;
    private readonly NativeNotificationBridge? bridge;
    private readonly Func<NotificationSoundKind> sound;
    private readonly Func<string, NotificationSoundKind>? soundForGroup;
    private readonly Action<NotificationSoundKind>? playSound;
    private NotificationSoundKind activeSound;
    private bool soundPlayed;
    private bool noticeDisplayed;
    private readonly Func<DateTimeOffset> now;
    private DateTimeOffset displayRequestedAt;
    private bool showing, disposed, activating;
    internal DesktopNotifications(NotifyIcon icon, Func<bool?>? fullscreen = null,
        Action<string, string>? display = null, Func<Uri, bool>? open = null, Func<bool>? visible = null,
        Func<NotificationSoundKind>? sound = null, Action<NotificationSoundKind>? playSound = null, Func<DateTimeOffset>? now = null, Func<string, NotificationSoundKind>? soundForGroup = null)
    {
        this.icon = icon; this.fullscreen = fullscreen ?? FullscreenActivityDetector.GetFullscreenState;
        this.soundForGroup = soundForGroup;
        this.sound = sound ?? (() => NotificationSoundKind.WindowsStandard); this.playSound = playSound;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        if (display == null) bridge = new(icon, OnShown, hidden: OnHidden);
        this.display = display ?? ((title, text) =>
        {
            if (bridge?.Show(title, text, silent: activeSound != NotificationSoundKind.WindowsStandard) != true)
                throw new InvalidOperationException("Windows notification unavailable.");
        });
        this.open = open ?? TwitchLinks.Open; this.visible = visible ?? (() => icon.Visible);
        icon.BalloonTipClicked += Clicked; icon.BalloonTipClosed += Closed;
    }
    internal void Show(TwitchStream stream, string language)
    {
        if (TwitchLinks.Channel(stream.Login) is not Uri uri) return;
        Show(string.Format(Localization.Get("TwitchLiveNotificationTitle", language), stream.DisplayName),
            Localization.Get("TwitchLiveNotificationBody", language), () => open(uri), "twitch");
    }
    internal void Show(string title, string body, Action clicked, string group)
    {
        if (disposed || !visible() || fullscreen() != false) return;
        DateTimeOffset timestamp = now();
        // Some Windows suppression paths send no close callback. A later alert must not stay blocked forever.
        if (showing && timestamp - displayRequestedAt >= TimeSpan.FromMinutes(2))
        {
            active = null; showing = false; noticeDisplayed = false;
            bridge?.Show("", "", silent: true);
        }
        var fresh = pending.Where(item => timestamp - item.Created < TimeSpan.FromMinutes(2)).ToArray();
        pending.Clear(); foreach (var item in fresh) pending.Enqueue(item);
        // Keep only fresh alerts; never build an unbounded queue when Windows suppresses notifications.
        if (pending.Count < 32) pending.Enqueue(new(title, body, clicked, group, timestamp));
        Pump();
    }
    internal void Clear(string? group = null)
    {
        if (group == null) { pending.Clear(); active = null; return; }
        var retained = pending.Where(item => item.Group != group).ToArray(); pending.Clear();
        foreach (var item in retained) pending.Enqueue(item);
        if (active?.Group == group) active = null;
    }
    private void Pump()
    {
        if (showing || disposed || activating) return;
        if (fullscreen() != false) { Clear(); return; }
        while (pending.TryDequeue(out var item))
        {
            if (now() - item.Created >= TimeSpan.FromMinutes(2)) continue;
            active = item; activeSound = soundForGroup?.Invoke(item.Group) ?? sound(); soundPlayed = false; noticeDisplayed = false;
            displayRequestedAt = now();
            showing = true;
            try { display(item.Title, item.Body); }
            catch (Exception ex)
            { active = null; showing = false; AppLogger.Info($"Notification display unavailable: {ex.GetType().Name}."); continue; }
            return;
        }
    }
    private void Clicked(object? sender, EventArgs e) => OnClicked();
    internal void OnClicked()
    {
        var notice = active; active = null; showing = false; activating = true;
        try
        {
            if (notice != null && fullscreen() == false) notice.Click();
        }
        catch (Exception ex) { AppLogger.Info($"Notification action failed: {ex.GetType().Name}."); }
        finally { activating = false; }
        // Windows may send Clicked without Closed. Retire this balloon before presenting the next one.
        SchedulePump();
    }
    private void Closed(object? sender, EventArgs e) => OnClosed();
    private void SchedulePump() { if (bridge != null) bridge.Post(Pump); else Pump(); }
    internal void OnClosed() { active = null; showing = false; noticeDisplayed = false; SchedulePump(); }
    internal void OnHidden() { if (noticeDisplayed) OnClosed(); }
    internal void OnShown()
    {
        if (disposed || active == null) return;
        noticeDisplayed = true;
        if (soundPlayed || fullscreen() != false) return;
        soundPlayed = true;
        if (activeSound is not (NotificationSoundKind.Off or NotificationSoundKind.WindowsStandard))
        {
            try { playSound?.Invoke(activeSound); }
            catch (Exception ex) { AppLogger.Info($"Notification sound unavailable: {ex.GetType().Name}."); }
        }
    }
    public void Dispose() { if (disposed) return; disposed = true; Clear(); bridge?.Dispose(); icon.BalloonTipClicked -= Clicked; icon.BalloonTipClosed -= Closed; }
}
