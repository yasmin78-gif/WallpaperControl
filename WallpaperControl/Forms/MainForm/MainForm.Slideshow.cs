using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Main-window slideshow responsibilities; see README.md in this directory for the code map.
    public partial class MainForm
    {
        /// <summary>
        /// Toggles the user's manual pause state from the main-window pause button.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private async void PauseButton_Click(
            object? sender,
            EventArgs e)
        {
            await ToggleSlideshowPauseAsync(refreshDisplay: false);
        }

        /// <summary>
        /// Pauses the slideshow while retaining the current image and updating the controls.
        /// </summary>
        private void PauseSlideshow()
        {
            if (customSlideshowEngineActive)
            {
                slideshowPaused = true;
                LogScheduler("manual pause enabled; timer stopped");
                customSlideshowPreciseTimer.Change(
                    Timeout.Infinite,
                    Timeout.Infinite);
                CheckSlideshowStatus();
                return;
            }

            string? wallpaperPath =
                GetCurrentWallpaperPath();

            if (string.IsNullOrWhiteSpace(
                wallpaperPath))
            {
                MessageBox.Show(
                    Localization.Get("MsgCurrentWallpaperUnknown"),
                    "Wallpaper Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            IDesktopWallpaper? wallpaper = null;

            try
            {
                wallpaper =
                    (IDesktopWallpaper)
                    new DesktopWallpaper();

                wallpaper.SetWallpaper(
                    null,
                    wallpaperPath);

                slideshowPaused = true;

                CheckSlideshowStatus();
            }
            catch (Exception ex)
            {
                slideshowPaused = false;

                MessageBox.Show(
                    Localization.Get("MsgPauseFailed") +
                    ex.Message,
                    "Wallpaper Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                ReleaseComObject(wallpaper);
            }
        }

        /// <summary>
        /// Restores the configured slideshow, optionally reporting failures to the user.
        /// </summary>
        /// <param name="showError">True to show a user-facing error if the operation fails.</param>
        /// <returns>A task whose result is true when the slideshow was resumed; otherwise, false.</returns>
        private async Task<bool> ResumeSlideshowAsync(
            bool showError)
        {
            if (customSlideshowEngineActive)
            {
                slideshowPaused = false;
                LogScheduler("manual pause disabled; resume");
                RecalculateCustomSlideshowSchedule();
                CheckSlideshowStatus();
                await Task.CompletedTask;
                return true;
            }

            string? folder =
                appSettings.LoadLastWallpaperFolder();

            if (string.IsNullOrWhiteSpace(folder))
            {
                if (showError)
                {
                    MessageBox.Show(
                        Localization.Get("MsgWallpaperFolderUnknown"),
                        "Wallpaper Control",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                return false;
            }

            try
            {
                if (!SetWallpaperFolder(folder, showError)) return false;

                await Task.Delay(300);

                slideshowPaused = false;
                StartCustomSlideshowEngine();

                CheckSlideshowStatus();

                return true;
            }
            catch (Exception ex)
            {
                if (showError)
                {
                    MessageBox.Show(
                        Localization.Get("MsgResumeFailed") +
                        ex.Message,
                        "Wallpaper Control",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }

                AppLogger.Warning("Could not resume the slideshow.", new InvalidOperationException(ex.GetType().Name));
                return false;
            }
        }

        /// <summary>
        /// Keeps the current wallpaper as a fixed image and updates slideshow state.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private async void PinButton_Click(
            object? sender,
            EventArgs e)
        {
            string? wallpaperPath =
                GetCurrentWallpaperPath();

            if (string.IsNullOrWhiteSpace(
                wallpaperPath))
            {
                MessageBox.Show(
                    Localization.Get("MsgCurrentWallpaperUnknown"),
                    "Wallpaper Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            IDesktopWallpaper? wallpaper = null;

            try
            {
                wallpaper =
                    (IDesktopWallpaper)
                    new DesktopWallpaper();

                customSlideshowEngineActive = false;
                LogScheduler("stopped: pin wallpaper");
                customWallpaperCancellation?.Cancel();
                customSlideshowPreciseTimer.Change(
                    Timeout.Infinite,
                    Timeout.Infinite);
                slideshowPaused = false;

                PersistentDesktopTransitionManager.ApplyNativeSelection(() => wallpaper.SetWallpaper(
                    null,
                    wallpaperPath));

                // Allow Windows to switch from slideshow mode to a fixed image.
                await Task.Delay(300);

                CheckSlideshowStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Localization.Get("MsgPinFailed") +
                    ex.Message,
                    "Wallpaper Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                ReleaseComObject(wallpaper);
            }
        }

        /// <summary>
        /// Activates the selected folder and starts application-controlled slideshow scheduling.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private async void ActivateButton_Click(
            object? sender,
            EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(
                folderTextBox.Text))
            {
                MessageBox.Show(
                    Localization.Get("MsgSelectWallpaperFolderFirst"),
                    "Wallpaper Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            if (!SetWallpaperFolder(folderTextBox.Text)) return;
            slideshowPaused = false;

            // Windows updates slideshow state asynchronously; delay the UI refresh.
            await Task.Delay(300);

            StartCustomSlideshowEngine();
            CheckSlideshowStatus();
        }

        /// <summary>
        /// Queries whether Windows currently reports an active slideshow.
        /// </summary>
        /// <returns>True when Windows reports both the Enabled and Slideshow flags; false if the query fails.</returns>
        private bool IsSlideshowCurrentlyActive()
        {
            IDesktopWallpaper? wallpaper = null;

            try
            {
                wallpaper =
                    (IDesktopWallpaper)
                    new DesktopWallpaper();

                wallpaper.GetStatus(
                    out DesktopSlideshowState state);

                return
                    (state &
                     DesktopSlideshowState.Enabled) != 0 &&
                    (state &
                     DesktopSlideshowState.Slideshow) != 0;
            }
            catch (Exception ex)
            {
                AppLogger.Warning("Could not determine whether the Windows slideshow is active.", ex);
                return false;
            }
            finally
            {
                ReleaseComObject(wallpaper);
            }
        }
        /// <summary>
        /// Toggles manual pause and optionally refreshes displays used by tray and hotkey actions.
        /// </summary>
        /// <param name="refreshDisplay">True to refresh the wallpaper label and tray action after toggling pause.</param>
        /// <returns>A task representing completion of the asynchronous operation.</returns>
        private async Task ToggleSlideshowPauseAsync(bool refreshDisplay)
        {
            if (slideshowPaused)
            {
                await ResumeSlideshowAsync(
                    showError: true);
            }
            else
            {
                PauseSlideshow();
            }

            if (refreshDisplay)
            {
                UpdateCurrentWallpaperDisplay();
                UpdateTrayPauseText();
            }
        }

    }
}
