using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Main-window tray responsibilities; see README.md in this directory for the code map.
    public partial class MainForm
    {
        /// <summary>
        /// Toggles manual slideshow pause from the tray menu.
        /// </summary>
        private async void TrayPauseItem_Click(
            object? sender,
            EventArgs e)
        {
            await ToggleSlideshowPauseAsync(refreshDisplay: true);
        }

        /// <summary>
        /// Restores and activates the main window while preventing overlapping restore operations.
        /// </summary>
        private void RestoreFromTray()
        {
            if (IsDisposed ||
                Disposing)
            {
                return;
            }

            // Defer restoration until the NotifyIcon event has completed to avoid
            // reentrant WinForms visibility and resize changes.
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed ||
                    Disposing)
                {
                    return;
                }

                restoringFromTray = true;

                try
                {
                    ShowInTaskbar = true;

                    if (!Visible)
                    {
                        Show();
                    }

                    WindowState =
                        FormWindowState.Normal;

                    // Explicitly restore the native window from its minimized state.
                    ShowWindow(
                        Handle,
                        SW_RESTORE);

                    BringToFront();
                    Activate();

                    SetForegroundWindow(
                        Handle);

                    UpdateCurrentWallpaperDisplay();
                }
                finally
                {
                    restoringFromTray = false;
                }
            }));
        }

        /// <summary>
        /// Updates the tray action text to match the user's manual pause state.
        /// </summary>
        private void UpdateTrayPauseText()
        {
            trayPauseItem.Text =
                slideshowPaused
                ? Localization.Get("ResumeSlideshow")
                : Localization.Get("PauseSlideshow");
        }
    }
}
