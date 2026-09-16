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
        /// <summary>
        /// Returns the hosted wallpaper path, falling back to the Windows desktop wallpaper API.
        /// </summary>
        private string? GetCurrentWallpaperPath()
        {
            string? hostedWallpaper =
                PersistentDesktopTransitionManager.GetDisplayedWallpaperPath();

            if (!string.IsNullOrWhiteSpace(hostedWallpaper) &&
                File.Exists(hostedWallpaper))
            {
                return hostedWallpaper;
            }

            IDesktopWallpaper? wallpaper = null;

            try
            {
                wallpaper =
                    (IDesktopWallpaper)
                    new DesktopWallpaper();

                uint monitorCount =
                    wallpaper.GetMonitorDevicePathCount();

                if (monitorCount > 0)
                {
                    string monitorId =
                        wallpaper.GetMonitorDevicePathAt(0);

                    return wallpaper.GetWallpaper(
                        monitorId);
                }

                return wallpaper.GetWallpaper(null);
            }
            catch (Exception ex)
            {
                AppLogger.Warning("Could not read the current Windows wallpaper path.", ex);
                return null;
            }
            finally
            {
                ReleaseComObject(wallpaper);
            }
        }

        /// <summary>
        /// Requests the next wallpaper from the main-window action button.
        /// </summary>
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
        private void AdvanceWallpaper(
            DesktopSlideshowDirection direction)
        {
            _ = AdvanceWallpaperAsync(direction);
        }

        /// <summary>
        /// Advances through the application engine or native fallback while respecting pause state.
        /// </summary>
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
        private async Task<bool> AdvanceCustomWallpaperAsync(
            DesktopSlideshowDirection direction)
        {
            await UpdateFullscreenPauseAsync();
            if (fullscreenPolicy.IsPaused) return false;
            if (customSlideshowChangeRunning)
                return false;

            string folder = folderTextBox.Text;

            if (string.IsNullOrWhiteSpace(folder) ||
                !Directory.Exists(folder))
            {
                return false;
            }

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
                string next;

                if (shuffleCheckBox.Checked && files.Length > 1)
                {
                    string[] candidates = files
                        .Where(path => !string.Equals(
                            path,
                            current,
                            StringComparison.OrdinalIgnoreCase))
                        .ToArray();

                    next = candidates[customSlideshowRandom.Next(candidates.Length)];
                }
                else
                {
                    int currentIndex = Array.FindIndex(
                        files,
                        path => string.Equals(
                            path,
                            current,
                            StringComparison.OrdinalIgnoreCase));

                    if (direction == DesktopSlideshowDirection.Backward)
                    {
                        int previousIndex = currentIndex <= 0
                            ? files.Length - 1
                            : currentIndex - 1;

                        next = files[previousIndex];
                    }
                    else
                    {
                        int nextIndex = currentIndex < 0
                            ? 0
                            : (currentIndex + 1) % files.Length;

                        next = files[nextIndex];
                    }
                }

                await wallpaperTransitionService.ApplyAsync(
                    current,
                    next,
                    selectedTransitionKind,
                    selectedTransitionDurationMilliseconds,
                    selectedTransitionDirection,
                    selectedZoomMode);

                _ = RefreshCurrentWallpaperSoonAsync();

                return string.Equals(
                    GetCurrentWallpaperPath(),
                    next,
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                if (exitRequested ||
                    IsDisposed ||
                    Disposing)
                {
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
                customSlideshowChangeRunning = false;
                UpdateCurrentWallpaperDisplay();
            }
        }

        /// <summary>
        /// Waits for Windows to settle before refreshing the displayed wallpaper information.
        /// </summary>
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
