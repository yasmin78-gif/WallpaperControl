using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Settings dialog General members. See README.md in this directory for the code map.
    internal sealed partial class SettingsForm
    {
        /// <summary>
        /// Checks for a release on request and displays the result while preventing repeated clicks.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private async void CheckForUpdatesButton_Click(
            object? sender,
            EventArgs e)
        {
            checkForUpdatesButton.Enabled = false;

            try
            {
                using UpdateService updateService = new();
                UpdateCheckResult result =
                    await updateService.CheckAsync();

                string currentVersion =
                    result.CurrentVersion.ToString(3);

                if (result.Status == UpdateCheckStatus.UpToDate)
                {
                    using UpdateDialog upToDateDialog = new(
                        UpdateDialogKind.UpToDate,
                        currentVersion,
                        null,
                        ResolvePreviewDarkMode(),
                        opacityTrackBar.Value,
                        previewLanguageCode);
                    upToDateDialog.ShowDialog(this);
                    return;
                }

                if (result.Status == UpdateCheckStatus.UpdateAvailable &&
                    result.LatestVersion != null)
                {
                    string latestVersion =
                        result.LatestVersion.ToString(3);

                    using UpdateDialog updateAvailableDialog = new(
                        UpdateDialogKind.UpdateAvailable,
                        currentVersion,
                        latestVersion,
                        ResolvePreviewDarkMode(),
                        opacityTrackBar.Value,
                        previewLanguageCode);

                    DialogResult answer = updateAvailableDialog.ShowDialog(this);

                    UpdateDialog.OpenReleaseIfAccepted(answer, result.ReleaseUri, releaseUri =>
                    {
                        Process.Start(
                            new ProcessStartInfo
                            {
                                FileName = releaseUri.AbsoluteUri,
                                UseShellExecute = true
                            });
                    });

                    return;
                }

                using UpdateDialog failedDialog = new(
                    UpdateDialogKind.Failed,
                    currentVersion,
                    null,
                    ResolvePreviewDarkMode(),
                    opacityTrackBar.Value,
                    previewLanguageCode);
                failedDialog.ShowDialog(this);
            }
            catch (Exception ex)
            {
                AppLogger.Warning(
                    "Manual update check failed.",
                    ex);

                string fallbackVersion =
                    Application.ProductVersion.Split('+')[0];
                using UpdateDialog exceptionDialog = new(
                    UpdateDialogKind.Failed,
                    fallbackVersion,
                    null,
                    ResolvePreviewDarkMode(),
                    opacityTrackBar.Value,
                    previewLanguageCode);
                exceptionDialog.ShowDialog(this);
            }
            finally
            {
                if (!IsDisposed)
                    checkForUpdatesButton.Enabled = true;
            }
        }
    }
}
