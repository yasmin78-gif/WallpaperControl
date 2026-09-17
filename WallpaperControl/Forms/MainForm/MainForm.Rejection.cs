using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Main-window rejection responsibilities; see README.md in this directory for the code map.
    public partial class MainForm
    {
        /// <summary>
        /// Opens the configured rejected-image folder in Explorer when available.
        /// </summary>
        private void OpenRejectedFolder()
        {
            string sourceFolder =
                folderTextBox.Text;

            if (string.IsNullOrWhiteSpace(sourceFolder) ||
                !Directory.Exists(sourceFolder))
            {
                MessageBox.Show(
                    Localization.Get("MsgWallpaperFolderUnavailable"),
                    "Wallpaper Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            string rejectedFolder =
                GetRejectedFolder(
                    sourceFolder);

            try
            {
                Directory.CreateDirectory(
                    rejectedFolder);

                WallpaperFileActions.OpenFolder(rejectedFolder);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Localization.Get("MsgOpenRejectedFolderFailed") +
                    ex.Message,
                    "Wallpaper Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Resolves the rejection destination using the root-folder and subfolder preferences.
        /// </summary>
        /// <param name="sourceFolder">The wallpaper folder path.</param>
        /// <returns>The rejection folder selected by the configured root and subfolder preferences.</returns>
        private string GetRejectedFolder(
            string sourceFolder)
        {
            if (string.IsNullOrWhiteSpace(
                rejectRootFolder))
            {
                return Path.Combine(
                    sourceFolder,
                    "Aussortiert");
            }

            if (!rejectUseSubfolder)
            {
                return rejectRootFolder;
            }

            string normalizedSource =
                sourceFolder.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

            string folderName =
                Path.GetFileName(
                    normalizedSource);

            if (string.IsNullOrWhiteSpace(
                folderName))
            {
                folderName = "Wallpaper";
            }

            return Path.Combine(
                rejectRootFolder,
                folderName);
        }

        /// <summary>
        /// Starts the asynchronous rejection of the current wallpaper.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private async void RejectButton_Click(
            object? sender,
            EventArgs e)
        {
            await RejectCurrentWallpaperAsync();
        }

        /// <summary>
        /// Advances away from the current image and moves it to the rejection folder.
        /// </summary>
        /// <returns>A task representing completion of the asynchronous operation.</returns>
        private async Task RejectCurrentWallpaperAsync()
        {
            if (slideshowPaused || fullscreenPolicy.IsPaused)
                return;

            string? path =
                GetCurrentWallpaperPath();

            if (string.IsNullOrWhiteSpace(path) ||
                !File.Exists(path))
            {
                MessageBox.Show(
                    Localization.Get("MsgCurrentWallpaperNotFound"),
                    "Wallpaper Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            try
            {
                string? sourceFolder =
                    Path.GetDirectoryName(path);

                if (string.IsNullOrWhiteSpace(
                    sourceFolder))
                {
                    return;
                }

                bool advanced =
                    await AdvanceWallpaperAsync(
                        DesktopSlideshowDirection.Forward);

                if (!advanced)
                {
                    return;
                }

                if (!customSlideshowEngineActive)
                {
                    DateTime waitUntil =
                        DateTime.UtcNow.AddSeconds(2);

                    while (string.Equals(
                               GetCurrentWallpaperPath(),
                               path,
                               StringComparison.OrdinalIgnoreCase) &&
                           DateTime.UtcNow < waitUntil)
                    {
                        await Task.Delay(50);
                    }
                }

                if (string.Equals(
                        GetCurrentWallpaperPath(),
                        path,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                string rejectFolder =
                    GetRejectedFolder(
                        sourceFolder);

                Directory.CreateDirectory(
                    rejectFolder);

                string destination =
                    GetUniqueDestinationPath(
                        rejectFolder,
                        Path.GetFileName(path));

                File.Move(
                    path,
                    destination);

                lastRejectedSourcePath = path;
                lastRejectedDestinationPath = destination;
                undoRejectButton.Enabled = true;

                UpdateWallpaperCount();
                UpdateHistoryButton();
                UpdateCurrentWallpaperDisplay();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Localization.Get("MsgRejectFailed") +
                    ex.Message,
                    "Wallpaper Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Restores the last rejected wallpaper from the undo action.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private void UndoRejectButton_Click(
            object? sender,
            EventArgs e)
        {
            UndoLastReject();
        }

        /// <summary>
        /// Moves the last rejected image back to its source and refreshes related UI state.
        /// </summary>
        private void UndoLastReject()
        {
            if (string.IsNullOrWhiteSpace(lastRejectedSourcePath) ||
                string.IsNullOrWhiteSpace(lastRejectedDestinationPath))
            {
                undoRejectButton.Enabled = false;
                return;
            }

            if (!File.Exists(lastRejectedDestinationPath))
            {
                lastRejectedSourcePath = null;
                lastRejectedDestinationPath = null;
                undoRejectButton.Enabled = false;
                return;
            }

            try
            {
                string restorePath =
                    lastRejectedSourcePath;

                if (File.Exists(restorePath))
                {
                    string? restoreFolder =
                        Path.GetDirectoryName(restorePath);

                    if (string.IsNullOrWhiteSpace(restoreFolder))
                    {
                        return;
                    }

                    restorePath =
                        GetUniqueDestinationPath(
                            restoreFolder,
                            Path.GetFileName(restorePath));
                }

                File.Move(
                    lastRejectedDestinationPath,
                    restorePath);

                lastRejectedSourcePath = null;
                lastRejectedDestinationPath = null;
                undoRejectButton.Enabled = false;

                UpdateWallpaperCount();
                UpdateHistoryButton();
                UpdateCurrentWallpaperDisplay();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Localization.Get("MsgUndoRejectFailed") +
                    ex.Message,
                    "Wallpaper Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Finds an unused destination filename without overwriting an existing rejected image.
        /// </summary>
        /// <param name="folder">The wallpaper folder path.</param>
        /// <param name="fileName">The original filename to preserve or suffix when a collision exists.</param>
        /// <returns>A destination path with a numeric suffix when the original filename already exists.</returns>
        private static string GetUniqueDestinationPath(
            string folder,
            string fileName)
        {
            string destination =
                Path.Combine(
                    folder,
                    fileName);

            if (!File.Exists(destination))
            {
                return destination;
            }

            string name =
                Path.GetFileNameWithoutExtension(
                    fileName);

            string extension =
                Path.GetExtension(
                    fileName);

            int number = 2;

            do
            {
                destination =
                    Path.Combine(
                        folder,
                        $"{name} ({number}){extension}");

                number++;
            }
            while (File.Exists(destination));

            return destination;
        }

        /// <summary>
        /// Loads the rejection folder and subfolder preferences.
        /// </summary>
        private void LoadRejectSettings()
        {
            var settings = appSettings.LoadRejectSettings();
            rejectRootFolder = settings.RootFolder;
            rejectUseSubfolder = settings.UseSubfolder;
        }

        /// <summary>
        /// Persists the current rejection folder and subfolder preferences.
        /// </summary>
        private void SaveRejectSettings() => appSettings.SaveRejectSettings(new RejectSettings
        {
            RootFolder = rejectRootFolder, UseSubfolder = rejectUseSubfolder
        });
    }
}
