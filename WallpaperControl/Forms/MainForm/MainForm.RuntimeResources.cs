namespace WallpaperControl;

public partial class MainForm
{
    private RuntimeResourceTelemetry? runtimeResources;
    private void ObserveRuntimeResources()
    {
        if (wallpaperOwnership.Closed || IsDisposed) return;
        bool video = wallpaperOwnership.Mode == WallpaperOperatingMode.VideoWallpaper;
        var reasons = video ? videoWallpaper?.PauseReasons ?? VideoPauseReason.None :
            (slideshowPaused ? VideoPauseReason.Manual : VideoPauseReason.None) |
            (fullscreenPolicy.IsPaused ? VideoPauseReason.Fullscreen : VideoPauseReason.None);
        runtimeResources?.Observe(new(wallpaperOwnership.Mode, video ? videoWallpaper?.State ?? VideoWallpaperState.Stopped : VideoWallpaperState.Stopped,
            reasons, videoWallpaper?.ChangePending == true));
    }
}
