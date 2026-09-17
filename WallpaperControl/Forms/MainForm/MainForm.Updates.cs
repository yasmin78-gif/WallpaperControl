using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Main-window updates responsibilities; see README.md in this directory for the code map.
    public partial class MainForm
    {
        /// <summary>
        /// Loads the preference for scheduled release checks.
        /// </summary>
        private void LoadAutomaticUpdateCheckSetting() => automaticUpdateCheckEnabled = appSettings.LoadAutomaticUpdateCheckSetting();

        /// <summary>
        /// Starts a scheduled asynchronous release check.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private async void AutomaticUpdateCheckTimer_Tick(
            object? sender,
            EventArgs e)
        {
            await CheckForUpdatesAutomaticallyAsync();
        }

        /// <summary>
        /// Checks for a new release while deferring network work and notifications during fullscreen activity.
        /// </summary>
        /// <returns>A task representing completion of the asynchronous operation.</returns>
        private async Task CheckForUpdatesAutomaticallyAsync()
        {
            if (pauseOnFullscreen && (fullscreenPolicy.IsPaused || FullscreenActivityDetector.IsFullscreenActive()))
            {
                deferredUpdateCheck = true;
                return;
            }
            deferredUpdateCheck = false;
            if (!automaticUpdateCheckEnabled ||
                automaticUpdateCheckRunning ||
                IsDisposed)
            {
                return;
            }

            automaticUpdateCheckRunning = true;

            try
            {
                UpdateCheckResult result;

                try
                {
                    using UpdateService updateService = new();
                    using var cancellation = new CancellationTokenSource();
                    automaticUpdateCancellation = cancellation;
                    try { result = await updateService.CheckAsync(cancellation.Token); }
                    finally { automaticUpdateCancellation = null; }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // Automatic checks must never interrupt normal use.
                    AppLogger.Warning(
                        "Automatic update check failed.",
                        ex);
                    return;
                }

                if (result.Status != UpdateCheckStatus.UpdateAvailable ||
                    result.LatestVersion == null ||
                    IsDisposed)
                {
                    // Up-to-date and failed automatic checks stay silent.
                    return;
                }

                if (pauseOnFullscreen && (fullscreenPolicy.IsPaused || FullscreenActivityDetector.IsFullscreenActive()))
                {
                    deferredUpdateCheck = true;
                    return;
                }
                string currentVersion =
                    result.CurrentVersion.ToString(3);
                string latestVersion =
                    result.LatestVersion.ToString(3);

                using UpdateDialog updateDialog = new(
                    UpdateDialogKind.UpdateAvailable,
                    currentVersion,
                    latestVersion,
                    darkMode,
                    windowOpacityPercent);

                DialogResult answer =
                    updateDialog.ShowDialog(this);

                if (answer == DialogResult.Yes &&
                    result.ReleaseUri != null)
                {
                    try
                    {
                        Process.Start(
                            new ProcessStartInfo
                            {
                                FileName = result.ReleaseUri.AbsoluteUri,
                                UseShellExecute = true
                            });
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warning(
                            "Could not open update release page.",
                            ex);
                    }
                }
            }
            finally
            {
                automaticUpdateCheckRunning = false;
            }
        }
    }
}
