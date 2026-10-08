namespace WallpaperControl;
internal sealed partial class WidgetManager
{
    private NowPlayingService? nowPlaying;
    private NowPlayingWidgetForm? nowPlayingWidget;
    private void ApplyNowPlayingWidget(WidgetSettings target, bool restoreLocations)
    {
        if (!target.NowPlaying.Enabled) { DisposeNowPlaying(); return; }
        nowPlaying ??= new();
        if (nowPlayingWidget == null || nowPlayingWidget.IsDisposed)
        {
            nowPlayingWidget = new(nowPlaying,target.NowPlaying,target.ClockLanguageCode,
                point => { settings.NowPlaying.Location = point; if (!previewMode) settings.Save(registryPath); });
            nowPlayingWidget.SetActivitySuspended(activitySuspended); nowPlayingWidget.SetPowerSuspended(packagePowerSuspended);
            RegisterDesktopWidget(nowPlayingWidget);
            _ = nowPlayingWidget.Handle;
            bool attached = DesktopWidgetNative.AttachToDesktop(nowPlayingWidget,target.NowPlaying.Location, showWindow: false);
            nowPlayingWidget.SetDesktopReady(attached);
            if (!attached) AppLogger.Info("Now Playing desktop attachment failed");
        }
        else
        {
            nowPlayingWidget.Apply(target.NowPlaying,target.ClockLanguageCode);
            if (restoreLocations) nowPlayingWidget.Location = WidgetSettings.EnsureVisible(target.NowPlaying.Location,nowPlayingWidget.Size);
            if (nowPlayingWidget.ShouldShow) DesktopWidgetNative.KeepOnDesktop(nowPlayingWidget);
        }
        if (previewMode) DesktopWidgetNative.EnableInteraction(nowPlayingWidget);
    }
    private void DisposeNowPlaying() { nowPlayingWidget?.Close(); nowPlayingWidget?.Dispose(); nowPlayingWidget = null; nowPlaying?.Dispose(); nowPlaying = null; }
}
