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
        private CancellationTokenSource? manualUpdateCancellation;

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            manualUpdateCancellation?.Cancel();
            base.OnFormClosed(e);
        }

        /// <summary>
        /// Checks for a release on request and displays the result while preventing repeated clicks.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private async void CheckForUpdatesButton_Click(
            object? sender,
            EventArgs e)
        {
            await CheckForUpdatesAsync(async token =>
            {
                using UpdateService service = new();
                return await service.CheckAsync(token);
            });
        }

        internal async Task CheckForUpdatesAsync(Func<CancellationToken, Task<UpdateCheckResult>> check)
        {
            if (manualUpdateCancellation != null || IsDisposed || Disposing) return;
            using CancellationTokenSource cancellation = new();
            manualUpdateCancellation = cancellation;
            checkForUpdatesButton.Enabled = false;

            try
            {
                UpdateCheckResult result =
                    await check(cancellation.Token);
                if (cancellation.IsCancellationRequested || IsDisposed || Disposing) return;

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
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception ex)
            {
                if (cancellation.IsCancellationRequested || IsDisposed || Disposing) return;
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
                manualUpdateCancellation = null;
                if (!cancellation.IsCancellationRequested && !IsDisposed && !Disposing)
                    checkForUpdatesButton.Enabled = true;
            }
        }
    }
}
