namespace WallpaperControl;

public partial class MainForm
{
    // Do not call GetCurrentWallpaperPath here: it can change renderer ownership.
    private void LogWallpaperSnapshot(string reason)
    {
        IDesktopWallpaper? desktop = null;
        try
        {
            string? rendered = PersistentDesktopTransitionManager.GetDisplayedWallpaperPath();
            desktop = (IDesktopWallpaper)new DesktopWallpaper();
            uint count = desktop.GetMonitorDevicePathCount();
            string native = desktop.GetWallpaper(count > 0 ? desktop.GetMonitorDevicePathAt(0) : null);
            bool externalChange = PersistentDesktopTransitionManager.HasExternalWallpaperChange(native);
            AppLogger.Info($"Wallpaper snapshot: reason={reason}; renderer={WallpaperDiagnostics.PathInfo(rendered)}; native={WallpaperDiagnostics.PathInfo(native)}; tracked={WallpaperDiagnostics.PathInfo(lastDisplayedWallpaperPath)}; label=[{WallpaperDiagnostics.Escape(currentWallpaperLabel.Text)}]; folder={WallpaperDiagnostics.PathInfo(folderTextBox.Text)}; folderExists={Directory.Exists(folderTextBox.Text)}; manualPause={slideshowPaused}; fullscreenPause={fullscreenPolicy.IsPaused}; changing={customSlideshowChangeRunning}; {PersistentDesktopTransitionManager.RendererDiagnostic}");
            bool trackedMismatch = rendered != null && lastDisplayedWallpaperPath != null && !string.Equals(rendered, lastDisplayedWallpaperPath, StringComparison.OrdinalIgnoreCase);
            if (rendered != null && (!File.Exists(rendered) || externalChange || trackedMismatch))
                AppLogger.Info($"Wallpaper snapshot WARNING: rendererFileMissing={!File.Exists(rendered)}; nativeChangedSinceRendererStart={externalChange}; trackedRendererMismatch={trackedMismatch}; reason={reason}");
        }
        catch (Exception ex)
        {
            // Diagnostics must not interfere with sleep, resume or slideshow control.
            AppLogger.Info($"Wallpaper snapshot unavailable: reason={reason}; type={ex.GetType().Name}");
        }
        finally { ReleaseComObject(desktop); }
    }
}
