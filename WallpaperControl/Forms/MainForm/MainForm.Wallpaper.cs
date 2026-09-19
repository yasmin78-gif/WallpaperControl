using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Main-window wallpaper responsibilities; see README.md in this directory for the code map.
    public partial class MainForm
    {
        private CancellationTokenSource? customWallpaperCancellation;
        /// <summary>
        /// Returns the hosted wallpaper path, falling back to the Windows desktop wallpaper API.
        /// </summary>
        /// <returns>The current wallpaper path, or null when it cannot be determined.</returns>
        private string? GetCurrentWallpaperPath()
        {
            string? hostedWallpaper =
                PersistentDesktopTransitionManager.GetDisplayedWallpaperPath();

            IDesktopWallpaper? wallpaper = null;

            try
            {
                wallpaper =
                    (IDesktopWallpaper)
                    new DesktopWallpaper();

                uint monitorCount =
                    wallpaper.GetMonitorDevicePathCount();

                string nativePath = wallpaper.GetWallpaper(monitorCount > 0
                    ? wallpaper.GetMonitorDevicePathAt(0) : null);
                if (PersistentDesktopTransitionManager.HasExternalWallpaperChange(nativePath))
                {
                    customWallpaperCancellation?.Cancel();
                    PersistentDesktopTransitionManager.Shutdown();
                    slideshowPaused = customSlideshowEngineActive || slideshowPaused;
                    ArmCustomSlideshowPreciseTimer();
                    return nativePath;
                }
                if (!string.IsNullOrWhiteSpace(hostedWallpaper) && File.Exists(hostedWallpaper))
                {
                    if (!PersistentDesktopTransitionManager.SupportsConfiguration(
                            Screen.AllScreens.Length, wallpaper.GetPosition()))
                    {
                        customWallpaperCancellation?.Cancel();
                        PersistentDesktopTransitionManager.ApplyNativeSelection(
                            () => wallpaper.SetWallpaper(null, hostedWallpaper));
                    }
                    return hostedWallpaper;
                }
                return nativePath;
            }
            catch (Exception ex)
            {
                AppLogger.Warning("Could not read the current Windows wallpaper path.", ex);
                return hostedWallpaper;
            }
            finally
            {
                ReleaseComObject(wallpaper);
            }
        }

        /// <summary>
        /// Requests the next wallpaper from the main-window action button.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private void NextWallpaperButton_Click(
            object? sender,
            EventArgs e)
        {
            AdvanceWallpaper(
                DesktopSlideshowDirection.Forward);
        }

        /// <summary>
        /// Starts an asynchronous wallpaper advance from synchronous UI and widget callbacks.
        /// </summary>
        /// <param name="direction">Whether to move forward or backward in the slideshow.</param>
        private void AdvanceWallpaper(
            DesktopSlideshowDirection direction)
        {
            _ = AdvanceWallpaperAsync(direction);
        }

        /// <summary>
        /// Advances through the application engine or native fallback while respecting pause state.
        /// </summary>
        /// <param name="direction">Whether to move forward or backward in the slideshow.</param>
        /// <returns>A task whose result indicates whether the slideshow advance was accepted or the custom wallpaper change succeeded.</returns>
        private async Task<bool> AdvanceWallpaperAsync(
            DesktopSlideshowDirection direction)
        {
            await UpdateFullscreenPauseAsync();
            if (fullscreenPolicy.IsPaused) return false;
            if (slideshowPaused)
                return false;

            if (customSlideshowEngineActive)
            {
                return await AdvanceCustomWallpaperAsync(direction);
            }

            IDesktopWallpaper? wallpaper = null;

            try
            {
                wallpaper =
                    (IDesktopWallpaper)
                    new DesktopWallpaper();

                wallpaper.AdvanceSlideshow(
                    null,
                    direction);

                _ = RefreshCurrentWallpaperSoonAsync();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Localization.Get("MsgAdvanceFailed") +
                    ex.Message,
                    "Wallpaper Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                return false;
            }
            finally
            {
                ReleaseComObject(wallpaper);
            }
        }

        /// <summary>
        /// Chooses the next supported image and applies its transition without overlapping changes.
        /// </summary>
        /// <param name="direction">Whether to move forward or backward in the slideshow.</param>
        /// <returns>A task whose result is true when the selected wallpaper became the current wallpaper; false when blocked, unavailable, or unsuccessful.</returns>
        private async Task<bool> AdvanceCustomWallpaperAsync(
            DesktopSlideshowDirection direction, bool automatic = false)
        {
            await UpdateFullscreenPauseAsync();
            if (fullscreenPolicy.IsPaused) return false;
            if (customSlideshowChangeRunning)
                return false;
            if (slideshowPaused || !customSlideshowEngineActive) return false;

            string folder = folderTextBox.Text;

            if (string.IsNullOrWhiteSpace(folder) ||
                !Directory.Exists(folder))
            {
                return false;
            }

            using CancellationTokenSource cancellation = new();
            customWallpaperCancellation = cancellation;
            try
            {
                customSlideshowChangeRunning = true;
                rejectButton.Enabled = false;

                string[] files = Directory.EnumerateFiles(
                        folder,
                        "*",
                        SearchOption.TopDirectoryOnly)
                    .Where(WallpaperImageInfo.IsSupportedWallpaperExtension)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (files.Length == 0)
                    return false;

                string? current = GetCurrentWallpaperPath();
                string? next = await WallpaperCandidateRunner.TryAdvanceAsync(files, current,
                    shuffleCheckBox.Checked, direction == DesktopSlideshowDirection.Backward,
                    customSlideshowRandom, async candidate =>
                    {
                        if (exitRequested || IsDisposed || Disposing || cancellation.IsCancellationRequested) return false;
                        try
                        {
                            // Validate using the same Windows decoder even for the direct path.
                            using (Image image = Image.FromFile(candidate)) { _ = image.Width; }
                            await wallpaperTransitionService.ApplyAsync(current, candidate,
                                selectedTransitionKind, selectedTransitionDurationMilliseconds,
                                selectedTransitionDirection, selectedZoomMode, cancellation.Token);
                            cancellation.Token.ThrowIfCancellationRequested();
                            return string.Equals(GetCurrentWallpaperPath(), candidate,
                                StringComparison.OrdinalIgnoreCase);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                            or ArgumentException or OutOfMemoryException or System.Runtime.InteropServices.ExternalException)
                        {
                            // GDI+ also reports invalid image data as OutOfMemoryException.
                            AppLogger.Warning("Skipping an unreadable wallpaper candidate.", ex);
                            return false;
                        }
                    });
                if (next == null) return false;
                _ = RefreshCurrentWallpaperSoonAsync();
                return true;
            }
            catch (Exception) when (cancellation.IsCancellationRequested) { return false; }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex)
            {
                if (exitRequested ||
                    IsDisposed ||
                    Disposing)
                {
                    return false;
                }

                if (automatic)
                {
                    AppLogger.Warning("Automatic wallpaper advance failed.", ex);
                    return false;
                }

                MessageBox.Show(
                    Localization.Get("MsgAdvanceFailed") +
                    ex.Message,
                    "Wallpaper Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                return false;
            }
            finally
            {
                customWallpaperCancellation = null;
                customSlideshowChangeRunning = false;
                if (!exitRequested && !IsDisposed && !Disposing)
                {
                    if (customSlideshowNextChange <= DateTime.Now && customSlideshowLastInterval > 0)
                        customSlideshowNextChange = GetNextAlignedChange(DateTime.Now, customSlideshowLastInterval);
                    ArmCustomSlideshowPreciseTimer();
                    UpdateCurrentWallpaperDisplay();
                }
            }
        }

        /// <summary>
        /// Waits for Windows to settle before refreshing the displayed wallpaper information.
        /// </summary>
        /// <returns>A task representing completion of the asynchronous operation.</returns>
        private async Task RefreshCurrentWallpaperSoonAsync()
        {
            await Task.Delay(300);

            if (!IsDisposed)
            {
                UpdateCurrentWallpaperDisplay();
            }
        }

        /// <summary>
        /// Refreshes the current image label, history, statistics, and related action state.
        /// </summary>
        private void UpdateCurrentWallpaperDisplay()
        {
            string? path =
                GetCurrentWallpaperPath();

            if (!string.Equals(
                path,
                lastDisplayedWallpaperPath,
                StringComparison.OrdinalIgnoreCase))
            {
                lastDisplayedWallpaperPath =
                    path;

                currentWallpaperLabel.Text =
                    string.IsNullOrWhiteSpace(path)
                    ? Localization.Get("CurrentWallpaperEmpty")
                    : Path.GetFileName(path);
                toolTip.SetToolTip(currentWallpaperLabel, path ?? string.Empty);

                if (!string.IsNullOrWhiteSpace(path) &&
                    File.Exists(path))
                {
                    statistics.RecordView(path);
                    AddWallpaperToHistory(path);
                }

                if (wallpaperPreviewForm.Visible)
                {
                    UpdateWallpaperPreview(path);
                }
            }

            bool exists =
                !string.IsNullOrWhiteSpace(path) &&
                File.Exists(path);

            explorerButton.Enabled =
                exists;

            rejectButton.Enabled =
                exists &&
                !slideshowPaused &&
                !customSlideshowChangeRunning;
        }
    }
}
