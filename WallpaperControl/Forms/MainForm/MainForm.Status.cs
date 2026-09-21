using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Main-window status responsibilities; see README.md in this directory for the code map.
    public partial class MainForm
    {
        private readonly Func<DesktopSlideshowState> readNativeSlideshowStatus;

        // Windows can complete SetSlideshow asynchronously. Reconcile on every
        // normal poll, even when the fullscreen policy itself has not changed.
        private void RefreshWallpaperUi()
        {
            if (fullscreenPolicy.IsPaused || IsDisposed) return;
            CheckSlideshowStatus();
            if (servicesEnabled) UpdateWallpaperPositionDisplay();
        }

        private static DesktopSlideshowState ReadNativeSlideshowStatus()
        {
            IDesktopWallpaper? wallpaper = null;
            try
            {
                wallpaper = (IDesktopWallpaper)new DesktopWallpaper();
                wallpaper.GetStatus(out DesktopSlideshowState state);
                return state;
            }
            finally { ReleaseComObject(wallpaper); }
        }

        /// <summary>
        /// Reconciles fullscreen, application, and native slideshow state with the visible controls.
        /// </summary>
        private void CheckSlideshowStatus()
        {
            if (servicesEnabled) UpdateCurrentWallpaperDisplay();
            if (fullscreenPolicy.IsPaused)
            {
                ShowPausedStatus();
                statusLabel.Text = Localization.Get("StatusFullscreenPaused");
                pauseButton.Text = Localization.Get(slideshowPaused ? "ResumeSlideshow" : "PauseSlideshow");
                return;
            }
            // The application engine has no native Windows slideshow status,
            // but still counts as an active slideshow in the interface.
            if (customSlideshowEngineActive && !slideshowPaused)
            {
                ShowActiveStatus();
                return;
            }

            // A manual pause is scoped to the current application session.
            if (slideshowPaused)
            {
                ShowPausedStatus();
                return;
            }

            try
            {
                DesktopSlideshowState state = readNativeSlideshowStatus();

                bool enabled =
                    (state &
                     DesktopSlideshowState.Enabled) != 0;

                bool slideshow =
                    (state &
                     DesktopSlideshowState.Slideshow) != 0;

                bool remoteDisabled =
                    (state &
                     DesktopSlideshowState
                         .DisabledByRemoteSession) != 0;

                if (remoteDisabled)
                {
                    ShowInactiveStatus(
                        Localization.Get("StatusRemoteDisabled"),
                        false);

                    return;
                }

                if (!enabled || !slideshow)
                {
                    ShowInactiveStatus(
                        Localization.Get("StatusInactive"),
                        true);

                    return;
                }

                ShowActiveStatus();
            }
            catch (Exception ex)
            {
                // Do not turn an unknown COM state into a false "active" state.
                // Keep the current UI state and record the diagnostic details.
                AppLogger.Warning("Could not query Windows slideshow status.", ex);
            }
        }

        /// <summary>
        /// Enables controls for an active slideshow and restores the normal layout.
        /// </summary>
        private void ShowActiveStatus()
        {
            statusLabel.Visible = false;
            activateButton.Visible = false;

            pauseButton.Text =
                Localization.Get("PauseSlideshow");

            pauseButton.Enabled = true;
            pinButton.Enabled = true;
            nextWallpaperButton.Enabled = !initializingDesktop;

            UpdateTrayPauseText();

            SetNormalLayout();
        }

        /// <summary>
        /// Updates controls and labels for a paused slideshow.
        /// </summary>
        private void ShowPausedStatus()
        {
            statusLabel.Text =
                Localization.Get("StatusPaused");

            statusLabel.Visible = true;
            activateButton.Visible = false;

            pauseButton.Text =
                Localization.Get("ResumeSlideshow");

            pauseButton.Enabled = true;
            pinButton.Enabled = true;
            nextWallpaperButton.Enabled = false;

            UpdateTrayPauseText();

            SetWarningLayout(false);
        }

        /// <summary>
        /// Shows the inactive-slideshow warning and optionally offers activation.
        /// </summary>
        /// <param name="message">The status message to display while the slideshow is inactive.</param>
        /// <param name="canActivate">Whether the inactive slideshow can be activated from the current configuration.</param>
        private void ShowInactiveStatus(
            string message,
            bool canActivate)
        {
            statusLabel.Text = message;
            statusLabel.Visible = true;

            activateButton.Visible =
                canActivate;

            pauseButton.Text =
                Localization.Get("PauseSlideshow");

            pauseButton.Enabled = false;
            nextWallpaperButton.Enabled = false;

            UpdateTrayPauseText();

            // Pinning requires an active slideshow.
            pinButton.Enabled = false;

            SetWarningLayout(canActivate);
        }
    }
}
