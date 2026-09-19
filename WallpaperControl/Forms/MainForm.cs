using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Coordinates window startup, shutdown, and lifetime. Feature-specific members live in Forms/MainForm.
    public partial class MainForm : Form
    {
        /// <summary>
        /// Warms up the desktop renderer before starting widgets and scheduled update checks.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private async void MainForm_Shown(
            object? sender,
            EventArgs e)
        {
            await UpdateFullscreenPauseAsync();
            while (fullscreenPolicy.IsPaused && !IsDisposed)
            {
                await Task.Delay(1000);
                await UpdateFullscreenPauseAsync();
            }
            if (IsDisposed) return;
            // Initialize the persistent desktop renderer before the first
            // wallpaper transition, so Explorer/DWM's one-time taskbar refresh
            // happens during startup instead of inside the first wipe.
            nextWallpaperButton.Enabled = false;

            try
            {
                string? current =
                    GetCurrentWallpaperPath();

                await PersistentDesktopTransitionManager.InitializeHostAsync(
                    current);
            }
            catch (Exception ex)
            {
                // A failed warm-up must not prevent normal startup.
                // ApplyAsync can still retry the initialization later.
                AppLogger.Warning("Persistent desktop host warm-up failed.", ex);
            }
            finally
            {
                if (!IsDisposed)
                {
                    // Widgets belong to the persistent desktop host. Starting
                    // them only after the host warm-up prevents the full-screen
                    // wallpaper surface from being placed in front of them.
                    widgetManager.Start();
                    CheckSlideshowStatus();
                }
            }

            await CheckForUpdatesAutomaticallyAsync();

            if (!IsDisposed)
            {
                // Every application start performs its own update check.
                // While Wallpaper Control remains running, repeat the check
                // every 24 hours. No timestamp is carried across restarts.
                automaticUpdateCheckTimer.Start();
            }
        }

        /// <summary>
        /// Restores the native slideshow and releases timers, watchers, widgets, menus, and owned fonts.
        /// </summary>
        /// <param name="disposing">True when managed resources should be released during explicit disposal.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                customWallpaperCancellation?.Cancel();
                if (nativeSlideshowAutoPaused && !slideshowPaused)
                {
                    try { RestoreNativeSlideshowAfterFullscreen(); }
                    catch (Exception ex) { AppLogger.Warning("Could not restore Windows slideshow on exit.", ex); }
                }
                ReleaseComObject(fullscreenSavedSlideshow);
                fullscreenSavedSlideshow = null;
                SystemEvents.UserPreferenceChanged -=
                    SystemEvents_UserPreferenceChanged;

                if (IsHandleCreated)
                {
                    UnregisterHotKeys();
                }

                wallpaperRefreshTimer.Stop();
                wallpaperRefreshTimer.Dispose();

                customSlideshowPreciseTimer.Dispose();

                automaticUpdateCheckTimer.Stop();
                automaticUpdateCancellation?.Cancel();
                automaticUpdateCheckTimer.Tick -=
                    AutomaticUpdateCheckTimer_Tick;
                automaticUpdateCheckTimer.Dispose();

                toolTip.Dispose();
                widgetManager.Dispose();

                wallpaperCountDebounceTimer.Stop();
                wallpaperCountDebounceTimer.Dispose();

                if (wallpaperFolderWatcher != null)
                {
                    wallpaperFolderWatcher.EnableRaisingEvents = false;
                    wallpaperFolderWatcher.Dispose();
                    wallpaperFolderWatcher = null;
                }

                if (wallpaperPreviewPictureBox.Image != null)
                {
                    wallpaperPreviewPictureBox.Image.Dispose();
                    wallpaperPreviewPictureBox.Image = null;
                }

                wallpaperPreviewForm.Dispose();

                historyMenu.Dispose();
                rejectMenu.Dispose();

                trayIcon.Visible = false;
                trayIcon.Dispose();
                trayMenu.Dispose();

                foreach (Font font in ownedFonts)
                {
                    font.Dispose();
                }
                ownedFonts.Clear();
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Applies the title-bar theme and registers global hotkeys for the new window handle.
        /// </summary>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        protected override void OnHandleCreated(
            EventArgs e)
        {
            base.OnHandleCreated(e);

            ApplyTitleBarTheme();
            RegisterHotKeys(
                showErrors: false);
        }

        /// <summary>
        /// Persists the current window position through the settings store.
        /// </summary>
        private void SaveWindowPosition()
        {
            Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            appSettings.SaveWindowPosition(bounds.Location);
        }

        /// <summary>
        /// Restores a saved position only when it remains visible on the current monitors.
        /// </summary>
        private void RestoreWindowPosition()
        {
            StartPosition = FormStartPosition.Manual;

            Point? saved = appSettings.LoadWindowPosition();
            if (saved is Point position && AppSettingsStore.IsWindowPositionVisible(
                new Rectangle(position, ClientSize), Screen.AllScreens.Select(screen => screen.WorkingArea)))
            {
                Location = position;
                return;
            }
            Screen screen =
                Screen.PrimaryScreen ??
                Screen.AllScreens[0];

            Rectangle area = screen.WorkingArea;

            Location = new Point(
                area.Left + Math.Max(0, (area.Width - Width) / 2),
                area.Top + Math.Max(0, (area.Height - Height) / 2));
        }

        /// <summary>
        /// Hides a minimized window to the tray unless a restore operation is in progress.
        /// </summary>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        protected override void OnResize(
            EventArgs e)
        {
            base.OnResize(e);

            if (!restoringFromTray &&
                WindowState ==
                    FormWindowState.Minimized)
            {
                Hide();
                ShowInTaskbar = false;
            }
        }

        /// <summary>
        /// Handles close-to-tray behavior and coordinates slideshow restoration before exiting.
        /// </summary>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        protected override void OnFormClosing(
            FormClosingEventArgs e)
        {
            SaveWindowPosition();

            if (closeToTrayEnabled &&
                !exitRequested &&
                e.CloseReason ==
                    CloseReason.UserClosing)
            {
                e.Cancel = true;

                Hide();
                ShowInTaskbar = false;

                base.OnFormClosing(e);
                return;
            }

            if (slideshowPaused &&
                !closingAfterPauseResume)
            {
                e.Cancel = true;
                base.OnFormClosing(e);

                _ = ResumeSlideshowAndCloseAsync();
                return;
            }

            // Return slideshow ownership to Windows on exit. Its last native image
            // may become visible; a synthetic handoff is unreliable on Windows 11.
            if (customSlideshowEngineActive)
            {
                string? folder = appSettings.LoadLastWallpaperFolder();

                customSlideshowEngineActive = false;
                PersistentDesktopTransitionManager.Shutdown();

                if (!string.IsNullOrWhiteSpace(folder) &&
                    Directory.Exists(folder))
                {
                    SetWallpaperFolder(folder);
                }
            }

            statistics.Save();

            base.OnFormClosing(e);
        }

        /// <summary>
        /// Returns slideshow ownership to Windows before completing an explicit application exit.
        /// </summary>
        /// <returns>A task representing completion of the asynchronous operation.</returns>
        private async Task ResumeSlideshowAndCloseAsync()
        {
            if (closingAfterPauseResume)
            {
                return;
            }

            closingAfterPauseResume = true;

            try
            {
                bool resumed =
                    await ResumeSlideshowAsync(
                        showError: true);

                if (resumed &&
                    !IsDisposed &&
                    !Disposing)
                {
                    Close();
                }
            }
            catch (Exception ex)
            {
                if (!IsDisposed &&
                    !Disposing)
                {
                    MessageBox.Show(
                        ex.Message,
                        "Wallpaper Control",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
            finally
            {
                closingAfterPauseResume = false;
            }
        }
    }
}
