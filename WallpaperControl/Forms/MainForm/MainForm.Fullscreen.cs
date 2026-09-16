using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Main-window fullscreen responsibilities; see README.md in this directory for the code map.
    public partial class MainForm
    {
        /// <summary>
        /// Coordinates automatic fullscreen suspension without changing the user's manual pause state.
        /// </summary>
        private async Task UpdateFullscreenPauseAsync()
        {
            if (fullscreenUpdateRunning || IsDisposed) return;
            fullscreenUpdateRunning = true;
            try
            {
                if (!fullscreenPolicy.Update(pauseOnFullscreen,
                    pauseOnFullscreen && FullscreenActivityDetector.IsFullscreenActive(), DateTime.UtcNow)) return;
                bool paused = fullscreenPolicy.IsPaused;
                widgetManager.SetActivitySuspended(paused);
                PersistentDesktopTransitionManager.SetActivitySuspended(paused);
                if (paused)
                {
                    if (automaticUpdateCheckRunning)
                    {
                        deferredUpdateCheck = true;
                        automaticUpdateCancellation?.Cancel();
                    }
                    wallpaperPreviewForm.Hide();
                    customSlideshowPreciseTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    // The fallback Windows slideshow also needs to stop. Keep this
                    // separate from the user's manual pause state.
                    if (!customSlideshowEngineActive && !slideshowPaused && IsSlideshowCurrentlyActive())
                    {
                        string? path = GetCurrentWallpaperPath();
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            IDesktopWallpaper? wallpaper = null;
                            try
                            {
                                wallpaper = (IDesktopWallpaper)new DesktopWallpaper();
                                wallpaper.GetSlideshow(out var items);
                                ReleaseComObject(fullscreenSavedSlideshow);
                                fullscreenSavedSlideshow = items;
                                wallpaper.GetSlideshowOptions(out fullscreenSavedOptions, out fullscreenSavedInterval);
                                wallpaper.SetWallpaper(null, path);
                                nativeSlideshowAutoPaused = true;
                            }
                            finally { ReleaseComObject(wallpaper); }
                        }
                    }
                }
                else
                {
                    if (nativeSlideshowAutoPaused && !slideshowPaused)
                    {
                        if (fullscreenSavedSlideshow != null) RestoreNativeSlideshowAfterFullscreen();
                        else await ResumeSlideshowAsync(showError: false);
                    }
                    nativeSlideshowAutoPaused = false;
                    ReleaseComObject(fullscreenSavedSlideshow);
                    fullscreenSavedSlideshow = null;
                    if (customSlideshowEngineActive && !slideshowPaused)
                        RecalculateCustomSlideshowSchedule();
                    if (deferredWallpaperCount)
                    {
                        deferredWallpaperCount = false;
                        UpdateWallpaperCount();
                    }
                    if (deferredUpdateCheck) _ = CheckForUpdatesAutomaticallyAsync();
                }
                CheckSlideshowStatus();
            }
            catch (Exception ex) { AppLogger.Warning("Could not update fullscreen pause state.", ex); }
            finally { fullscreenUpdateRunning = false; }
        }

        /// <summary>
        /// Restores the saved Windows slideshow collection and options after automatic suspension.
        /// </summary>
        private void RestoreNativeSlideshowAfterFullscreen()
        {
            if (fullscreenSavedSlideshow == null) return;
            IDesktopWallpaper? wallpaper = null;
            try
            {
                wallpaper = (IDesktopWallpaper)new DesktopWallpaper();
                wallpaper.SetSlideshow(fullscreenSavedSlideshow);
                wallpaper.SetSlideshowOptions(fullscreenSavedOptions, fullscreenSavedInterval);
                nativeSlideshowAutoPaused = false;
            }
            finally { ReleaseComObject(wallpaper); }
        }
    }
}
