namespace WallpaperControl;

internal sealed partial class WidgetManager
{
    private TwitchService? twitch;
    private TwitchWidgetForm? twitchWidget;
    private TwitchRefreshScheduler? twitchScheduler;
    private TwitchLiveNotifications? twitchNotifications;
    internal Action<TwitchStream, string>? ShowTwitchNotification { get; set; }
    internal Action? ClearTwitchNotifications { get; set; }
    internal TwitchService Twitch
    {
        get
        {
            if (twitch != null) return twitch;
            twitch = new(new TwitchApi(), new TwitchTokenStore());
            twitchNotifications = new(twitch, stream => ShowTwitchNotification?.Invoke(stream, settings.ClockLanguageCode));
            twitchScheduler = new(twitch); twitchScheduler.Configure(false);
            twitchScheduler.SetSuspended(activitySuspended); twitchScheduler.SetPowerSuspended(packagePowerSuspended);
            return twitch;
        }
    }
    internal void ShowTwitchConnection(IWin32Window owner, string language, bool dark)
    {
        using var dialog = new TwitchConnectForm(Twitch, language, dark);
        if (dialog.ShowDialog(owner) == DialogResult.OK && twitchWidget != null) _ = Twitch.RefreshAsync();
    }
    internal void SetTwitchPowerSuspended(bool value) { nowPlayingWidget?.SetPowerSuspended(value); clipboardWidget?.SetPowerSuspended(value); launcherWidget?.SetPowerSuspended(value); feedWidget?.SetPowerSuspended(value); feeds?.SetSuspended(value || activitySuspended); twitchNotifications?.SetSuspended(value || activitySuspended); if (value) { ClearTwitchNotifications?.Invoke(); ClearFeedNotifications?.Invoke(); } twitchScheduler?.SetPowerSuspended(value); twitchWidget?.SetPowerSuspended(value); }
    private void ApplyTwitchWidget(WidgetSettings target, bool restoreLocations)
    {
        if (!target.TwitchEnabled)
        {
            twitchNotifications?.Configure(false); ClearTwitchNotifications?.Invoke();
            twitchScheduler?.Configure(false);
            twitchWidget?.Close(); twitchWidget?.Dispose(); twitchWidget = null; return;
        }
        _ = Twitch;
        twitchNotifications!.Configure(target.TwitchLiveNotifications);
        twitchNotifications.SetSuspended(activitySuspended || packagePowerSuspended);
        if (!target.TwitchLiveNotifications) ClearTwitchNotifications?.Invoke();
        twitchScheduler!.Configure(true, target.TwitchRefreshMinutes);
        if (twitchWidget == null || twitchWidget.IsDisposed)
        {
            twitchWidget = new(Twitch, target, p => { settings.TwitchLocation = p; if (!previewMode) settings.Save(registryPath); });
            twitchWidget.SetPowerSuspended(packagePowerSuspended);
            twitchWidget.SetActivitySuspended(activitySuspended); RegisterDesktopWidget(twitchWidget); twitchWidget.Show();
            if (!DesktopWidgetNative.AttachToDesktop(twitchWidget, target.TwitchLocation))
            { twitchWidget.Hide(); AppLogger.Info("Twitch widget desktop attachment failed."); }
        }
        else
        {
            twitchWidget.Apply(target);
            if (restoreLocations) twitchWidget.Location = WidgetSettings.EnsureVisible(target.TwitchLocation, twitchWidget.Size);
            DesktopWidgetNative.KeepOnDesktop(twitchWidget);
        }
        if (previewMode) DesktopWidgetNative.EnableInteraction(twitchWidget);
        _ = twitchScheduler.EvaluateAsync();
    }
    private void DisposeTwitch()
    {
        twitchScheduler?.Dispose(); twitchScheduler = null;
        twitchNotifications?.Dispose(); twitchNotifications = null;
        ShowTwitchNotification = null; ClearTwitchNotifications = null;
        twitchWidget?.Close(); twitchWidget?.Dispose(); twitchWidget = null;
        twitch?.Dispose(); twitch = null;
    }
}
