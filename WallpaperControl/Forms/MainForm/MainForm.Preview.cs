using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Main-window preview responsibilities; see README.md in this directory for the code map.
    public partial class MainForm
    {
        /// <summary>
        /// Shows the current wallpaper preview when the pointer enters its label.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private void CurrentWallpaperLabel_MouseEnter(
            object? sender,
            EventArgs e)
        {
            ShowWallpaperPreview();
        }

        /// <summary>
        /// Hides the preview when the pointer leaves the current wallpaper label.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private void CurrentWallpaperLabel_MouseLeave(
            object? sender,
            EventArgs e)
        {
            HideWallpaperPreview();
        }

        /// <summary>
        /// Loads and positions a preview of the current wallpaper beside the main window.
        /// </summary>
        private void ShowWallpaperPreview()
        {
            string? path =
                GetCurrentWallpaperPath();

            if (string.IsNullOrWhiteSpace(path) ||
                !File.Exists(path))
            {
                return;
            }

            UpdateWallpaperPreview(path);

            Point screenPoint =
                currentWallpaperLabel.PointToScreen(
                    new Point(
                        currentWallpaperLabel.Width + 8,
                        0));

            Screen screen =
                Screen.FromControl(this);

            int x = screenPoint.X;
            int y = screenPoint.Y;

            Rectangle area =
                screen.WorkingArea;

            if (x + wallpaperPreviewForm.Width >
                area.Right)
            {
                x =
                    currentWallpaperLabel
                        .PointToScreen(Point.Empty).X
                    - wallpaperPreviewForm.Width
                    - 8;
            }

            if (y + wallpaperPreviewForm.Height >
                area.Bottom)
            {
                y =
                    area.Bottom
                    - wallpaperPreviewForm.Height;
            }

            if (y < area.Top)
            {
                y = area.Top;
            }

            wallpaperPreviewForm.Location =
                new Point(x, y);

            wallpaperPreviewForm.Show(this);
        }

        /// <summary>
        /// Hides the preview without activating another window or reloading its image.
        /// </summary>
        private void HideWallpaperPreview()
        {
            if (wallpaperPreviewForm.IsHandleCreated &&
                wallpaperPreviewForm.Visible)
            {
                ShowWindow(
                    wallpaperPreviewForm.Handle,
                    SW_HIDE);
            }
        }

        /// <summary>
        /// Replaces the preview image and displays its file metadata.
        /// </summary>
        /// <param name="path">The image or folder path to process.</param>
        private void UpdateWallpaperPreview(
            string? path)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                !File.Exists(path))
            {
                HideWallpaperPreview();
                return;
            }

            try
            {
                Image? oldImage =
                    wallpaperPreviewPictureBox.Image;

                using (Image source =
                    Image.FromFile(path))
                {
                    wallpaperPreviewPictureBox.Image =
                        new Bitmap(source);
                }

                oldImage?.Dispose();

                FileInfo fileInfo =
                    new FileInfo(path);

                string resolution =
                    WallpaperImageInfo.GetImageResolutionText(path);

                string sizeText =
                    FormatFileSize(fileInfo.Length);

                wallpaperPreviewInfoLabel.Text =
                    $"{Path.GetFileName(path)}\n" +
                    $"{resolution}   •   {sizeText}   •   " +
                    fileInfo.LastWriteTime.ToString(
                        "g",
                        Localization.CurrentCulture) + "\n" +
                    path;
            }
            catch
            {
                wallpaperPreviewInfoLabel.Text =
                    path;
            }
        }

        /// <summary>
        /// Formats a file size for the wallpaper preview.
        /// </summary>
        /// <param name="bytes">The size in bytes.</param>
        /// <returns>The file size formatted with a readable unit.</returns>
        private static string FormatFileSize(
            long bytes)
        {
            const double KB = 1024.0;
            const double MB = KB * 1024.0;
            const double GB = MB * 1024.0;

            if (bytes >= GB)
            {
                return
                    $"{bytes / GB:0.##} GB";
            }

            if (bytes >= MB)
            {
                return
                    $"{bytes / MB:0.##} MB";
            }

            if (bytes >= KB)
            {
                return
                    $"{bytes / KB:0.##} KB";
            }

            return string.Format(
                Localization.CurrentCulture,
                Localization.Get(
                    bytes == 1
                    ? "FileSizeByteSingular"
                    : "FileSizeBytePlural"),
                bytes);
        }

        /// <summary>
        /// Shows the current wallpaper in Explorer from the toolbar action.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private void ExplorerButton_Click(
            object? sender,
            EventArgs e)
        {
            ShowCurrentWallpaperInExplorer();
        }

        /// <summary>
        /// Selects the current wallpaper file in Explorer when it exists.
        /// </summary>
        private void ShowCurrentWallpaperInExplorer()
        {
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
                string? folder =
                    Path.GetDirectoryName(path);

                if (string.IsNullOrWhiteSpace(folder) ||
                    !Directory.Exists(folder))
                {
                    return;
                }

                WallpaperFileActions.RevealInExplorer(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Localization.Get("MsgFileManagerFailed") +
                    ex.Message,
                    "Wallpaper Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Opens the current wallpaper using the registered default application.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private void CurrentWallpaperLabel_Click(object? sender, EventArgs e)
        {
            string? path =
                GetCurrentWallpaperPath();

            if (string.IsNullOrWhiteSpace(path) ||
                !File.Exists(path))
            {
                return;
            }

            try
            {
                WallpaperFileActions.OpenImage(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Localization.Get("MsgOpenImageFailed") +
                    ex.Message,
                    "Wallpaper Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }
}
