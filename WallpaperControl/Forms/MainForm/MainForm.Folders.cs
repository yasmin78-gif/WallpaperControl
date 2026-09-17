using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Main-window folders responsibilities; see README.md in this directory for the code map.
    public partial class MainForm
    {
        /// <summary>
        /// Reads the configured slideshow source from Windows and displays its folder path.
        /// </summary>
        private void LoadSlideshowFolder()
        {
            IDesktopWallpaper? wallpaper = null;
            IShellItemArray? array = null;
            IShellItem? item = null;

            bool folderFound = false;

            try
            {
                wallpaper =
                    (IDesktopWallpaper)
                    new DesktopWallpaper();

                wallpaper.GetSlideshow(out array);

                if (array != null)
                {
                    array.GetCount(
                        out uint count);

                    if (count > 0)
                    {
                        array.GetItemAt(
                            0,
                            out item);

                        item.GetDisplayName(
                            SIGDN.FILESYSPATH,
                            out IntPtr pathPointer);

                        if (pathPointer != IntPtr.Zero)
                        {
                            try
                            {
                                string? path =
                                    Marshal.PtrToStringUni(
                                        pathPointer);

                                if (!string.IsNullOrWhiteSpace(
                                    path))
                                {
                                    folderTextBox.Text =
                                        path;

                                    appSettings.SaveLastWallpaperFolder(
                                        path);

                                    folderFound = true;
                                }
                            }
                            finally
                            {
                                Marshal.FreeCoTaskMem(
                                    pathPointer);
                            }
                        }
                    }
                }
            }
            catch
            {
            }
            finally
            {
                ReleaseComObject(item);
                ReleaseComObject(array);
                ReleaseComObject(wallpaper);
            }

            if (!folderFound)
            {
                string? saved =
                    appSettings.LoadLastWallpaperFolder();

                if (!string.IsNullOrWhiteSpace(
                    saved))
                {
                    folderTextBox.Text =
                        saved;
                }
            }
        }

        /// <summary>
        /// Counts supported images in the selected folder and updates the count label.
        /// </summary>
        private void UpdateWallpaperCount()
        {
            string folder = folderTextBox.Text;

            if (string.IsNullOrWhiteSpace(folder) ||
                !Directory.Exists(folder))
            {
                wallpaperCountLabel.Text = Localization.Get("WallpaperCountZero");
                return;
            }

            try
            {
                string[] extensions =
                {
                    ".jpg", ".jpeg", ".png", ".bmp",
                    ".gif", ".tif", ".tiff", ".webp"
                };

                int count = Directory.EnumerateFiles(
                        folder,
                        "*",
                        SearchOption.TopDirectoryOnly)
                    .Count(file =>
                        extensions.Contains(
                            Path.GetExtension(file),
                            StringComparer.OrdinalIgnoreCase));

                wallpaperCountLabel.Text =
                    count == 1
                    ? Localization.Get("WallpaperCountOne")
                    : string.Format(
                        Localization.Get("WallpaperCountMany"),
                        count);
            }
            catch
            {
                wallpaperCountLabel.Text = Localization.Get("WallpaperCountUnavailable");
            }
        }

        /// <summary>
        /// Replaces the folder watcher so file changes are observed only for the current source.
        /// </summary>
        private void ConfigureWallpaperFolderWatcher()
        {
            if (wallpaperFolderWatcher != null)
            {
                wallpaperFolderWatcher.EnableRaisingEvents = false;
                wallpaperFolderWatcher.Dispose();
                wallpaperFolderWatcher = null;
            }

            string folder = folderTextBox.Text;

            if (string.IsNullOrWhiteSpace(folder) ||
                !Directory.Exists(folder))
            {
                return;
            }

            try
            {
                wallpaperFolderWatcher =
                    new FileSystemWatcher(folder)
                    {
                        Filter = "*.*",
                        IncludeSubdirectories = false,
                        NotifyFilter =
                            NotifyFilters.FileName |
                            NotifyFilters.CreationTime |
                            NotifyFilters.LastWrite
                    };

                wallpaperFolderWatcher.Created +=
                    WallpaperFolderWatcher_Changed;

                wallpaperFolderWatcher.Deleted +=
                    WallpaperFolderWatcher_Changed;

                wallpaperFolderWatcher.Renamed +=
                    WallpaperFolderWatcher_Renamed;

                wallpaperFolderWatcher.EnableRaisingEvents = true;
            }
            catch
            {
                wallpaperFolderWatcher?.Dispose();
                wallpaperFolderWatcher = null;
            }
        }

        /// <summary>
        /// Schedules a debounced count refresh after supported images change.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private void WallpaperFolderWatcher_Changed(
            object sender,
            FileSystemEventArgs e)
        {
            if (!WallpaperImageInfo.IsSupportedWallpaperExtension(e.FullPath))
                return;

            ScheduleWallpaperCountUpdate();
        }

        /// <summary>
        /// Schedules a count refresh when a rename affects a supported image.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private void WallpaperFolderWatcher_Renamed(
            object sender,
            RenamedEventArgs e)
        {
            if (!WallpaperImageInfo.IsSupportedWallpaperExtension(e.OldFullPath) &&
                !WallpaperImageInfo.IsSupportedWallpaperExtension(e.FullPath))
            {
                return;
            }

            ScheduleWallpaperCountUpdate();
        }

        /// <summary>
        /// Marshals file notifications to the UI thread and restarts the count debounce timer.
        /// </summary>
        private void ScheduleWallpaperCountUpdate()
        {
            if (IsDisposed || Disposing)
                return;

            // Restarts the debounce timer to combine consecutive folder-change notifications.
            void RestartTimer()
            {
                if (IsDisposed || Disposing)
                    return;

                wallpaperCountDebounceTimer.Stop();
                wallpaperCountDebounceTimer.Start();
            }

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke((Action)RestartTimer);
                }
                catch
                {
                }

                return;
            }

            RestartTimer();
        }

        /// <summary>
        /// Accepts dragged folders and supported wallpaper files.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private void MainForm_DragEnter(
            object? sender,
            DragEventArgs e)
        {
            if (!e.Data!.GetDataPresent(
                DataFormats.FileDrop))
            {
                e.Effect =
                    DragDropEffects.None;

                return;
            }

            if (e.Data.GetData(
                    DataFormats.FileDrop)
                is not string[] paths ||
                paths.Length == 0)
            {
                e.Effect =
                    DragDropEffects.None;

                return;
            }

            string path =
                paths[0];

            if (Directory.Exists(path) ||
                IsSupportedWallpaperFile(path))
            {
                e.Effect =
                    DragDropEffects.Copy;
            }
            else
            {
                e.Effect =
                    DragDropEffects.None;
            }
        }

        /// <summary>
        /// Resolves the dropped path and applies its wallpaper source folder.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private void MainForm_DragDrop(
            object? sender,
            DragEventArgs e)
        {
            if (e.Data!.GetData(
                    DataFormats.FileDrop)
                is not string[] paths ||
                paths.Length == 0)
            {
                return;
            }

            string droppedPath =
                paths[0];

            string? folder = null;

            if (Directory.Exists(
                droppedPath))
            {
                folder =
                    droppedPath;
            }
            else if (IsSupportedWallpaperFile(
                droppedPath))
            {
                folder =
                    Path.GetDirectoryName(
                        droppedPath);
            }

            if (string.IsNullOrWhiteSpace(folder) ||
                !Directory.Exists(folder))
            {
                MessageBox.Show(
                    Localization.Get("MsgDropWallpaperFolderOrImage"),
                    "Wallpaper Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            ApplyNewWallpaperFolder(folder);
        }

        /// <summary>
        /// Validates a dropped file before using its parent folder as a slideshow source.
        /// </summary>
        /// <param name="path">The image or folder path to process.</param>
        /// <returns>True when the path has a supported wallpaper image extension.</returns>
        private static bool IsSupportedWallpaperFile(
            string path)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            string extension =
                Path.GetExtension(path);

            return extension.Equals(
                       ".jpg",
                       StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(
                       ".jpeg",
                       StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(
                       ".png",
                       StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(
                       ".bmp",
                       StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(
                       ".gif",
                       StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(
                       ".tif",
                       StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(
                       ".tiff",
                       StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(
                       ".webp",
                       StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Applies a new source folder and refreshes slideshow state and folder observation.
        /// </summary>
        /// <param name="folder">The wallpaper folder path.</param>
        private void ApplyNewWallpaperFolder(
            string folder)
        {
            slideshowPaused = false;

            lastRejectedSourcePath = null;
            lastRejectedDestinationPath = null;
            undoRejectButton.Enabled = false;

            wallpaperHistory.Clear();
            historyButton.Enabled = false;
            historyButton.Text = Localization.Get("History");

            HideWallpaperPreview();

            appSettings.SaveLastWallpaperFolder(
                folder);

            SetWallpaperFolder(
                folder);

            StartCustomSlideshowEngine();

            // Replace an image from the previous source folder. The engine selects
            // the first image in sequence, or a random image when shuffle is on.
            if (customSlideshowEngineActive)
            {
                _ = AdvanceCustomWallpaperAsync(
                    DesktopSlideshowDirection.Forward);
            }

            CheckSlideshowStatus();
        }

        /// <summary>
        /// Prompts for a wallpaper source folder and applies the selected path.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private void FolderButton_Click(
            object? sender,
            EventArgs e)
        {
            using FolderBrowserDialog dialog =
                new FolderBrowserDialog();

            dialog.Description =
                Localization.Get("SelectWallpaperFolder");

            dialog.UseDescriptionForTitle =
                true;

            if (!string.IsNullOrWhiteSpace(
                folderTextBox.Text))
            {
                dialog.SelectedPath =
                    folderTextBox.Text;
            }

            if (dialog.ShowDialog(this) !=
                DialogResult.OK)
            {
                return;
            }

            ApplyNewWallpaperFolder(
                dialog.SelectedPath);
        }

        /// <summary>
        /// Creates a shell item collection and assigns it as the native Windows slideshow source.
        /// </summary>
        /// <param name="path">The image or folder path to process.</param>
        private void SetWallpaperFolder(
            string path)
        {
            IShellItem? folderItem = null;
            IShellItemArray? folderArray = null;
            IDesktopWallpaper? wallpaper = null;

            try
            {
                Guid shellItemGuid =
                    typeof(IShellItem).GUID;

                int result =
                    SHCreateItemFromParsingName(
                        path,
                        IntPtr.Zero,
                        ref shellItemGuid,
                        out folderItem);

                Marshal.ThrowExceptionForHR(
                    result);

                Guid shellItemArrayGuid =
                    typeof(IShellItemArray).GUID;

                result =
                    SHCreateShellItemArrayFromShellItem(
                        folderItem,
                        ref shellItemArrayGuid,
                        out folderArray);

                Marshal.ThrowExceptionForHR(
                    result);

                wallpaper =
                    (IDesktopWallpaper)
                    new DesktopWallpaper();

                wallpaper.SetSlideshow(
                    folderArray);

                folderTextBox.Text =
                    path;

                appSettings.SaveLastWallpaperFolder(
                    path);

                UpdateWallpaperCount();
                ConfigureWallpaperFolderWatcher();
                ApplySlideshowOptions();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Localization.Get("MsgSetWallpaperFolderFailed") +
                    ex.Message,
                    "Wallpaper Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                ReleaseComObject(folderArray);
                ReleaseComObject(folderItem);
                ReleaseComObject(wallpaper);
            }
        }
    }
}
