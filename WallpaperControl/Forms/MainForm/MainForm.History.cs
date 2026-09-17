using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Main-window history responsibilities; see README.md in this directory for the code map.
    public partial class MainForm
    {
        /// <summary>
        /// Applies an image selected in the statistics dialog and updates slideshow state.
        /// </summary>
        /// <param name="path">The image or folder path to process.</param>
        private async void SetWallpaperFromStatistics(
            string path)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                !File.Exists(path))
            {
                return;
            }

            IDesktopWallpaper? wallpaper = null;

            try
            {
                bool slideshowWasActive =
                    IsSlideshowCurrentlyActive();

                wallpaper =
                    (IDesktopWallpaper)
                    new DesktopWallpaper();

                wallpaper.SetWallpaper(
                    null,
                    path);

                // An image explicitly selected from statistics should remain visible.
                // If a slideshow was active, treat this selection as a pause for the
                // current application session.
                slideshowPaused =
                    slideshowWasActive;

                await Task.Delay(250);

                CheckSlideshowStatus();

                // Count only actual image changes; retain lastDisplayedWallpaperPath.
                UpdateCurrentWallpaperDisplay();

                // Selecting the image already displayed must not increase its count.
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Localization.Get(
                        "StatisticsSetWallpaperError") +
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
        /// Clears the stored wallpaper statistics through the statistics service.
        /// </summary>
        private void ResetPersistentStatistics()
        {
            statistics.Reset(GetCurrentWallpaperPath());
        }

        /// <summary>
        /// Refreshes the history button&apos;s enabled state and entry count.
        /// </summary>
        private void UpdateHistoryButton()
        {
            int availableCount =
                wallpaperHistory.Count(
                    File.Exists);

            historyButton.Enabled =
                availableCount > 0;

            historyButton.Text =
                availableCount > 0
                ? string.Format(
                    Localization.Get("HistoryCount"),
                    availableCount)
                : Localization.Get("History");
        }

        /// <summary>
        /// Adds a wallpaper to the bounded history while avoiding duplicate entries.
        /// </summary>
        /// <param name="path">The image or folder path to process.</param>
        private void AddWallpaperToHistory(
            string path)
        {
            wallpaperHistory.RemoveAll(
                item =>
                    string.Equals(
                        item,
                        path,
                        StringComparison.OrdinalIgnoreCase));

            wallpaperHistory.Insert(
                0,
                path);

            while (wallpaperHistory.Count >
                   MaxWallpaperHistory)
            {
                wallpaperHistory.RemoveAt(
                    wallpaperHistory.Count - 1);
            }

            UpdateHistoryButton();
        }

        /// <summary>
        /// Builds and opens the wallpaper history menu.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private void HistoryButton_Click(
            object? sender,
            EventArgs e)
        {
            wallpaperHistory.RemoveAll(
                path => !File.Exists(path));

            UpdateHistoryButton();

            BuildHistoryMenu();

            if (historyMenu.Items.Count == 0)
            {
                return;
            }

            historyMenu.Show(
                historyButton,
                new Point(
                    0,
                    historyButton.Height));
        }

        /// <summary>
        /// Recreates history entries and wires their selection and preview handlers.
        /// </summary>
        private void BuildHistoryMenu()
        {
            historyMenu.Items.Clear();

            string? currentPath =
                GetCurrentWallpaperPath();

            for (int i = 0;
                 i < wallpaperHistory.Count;
                 i++)
            {
                string path =
                    wallpaperHistory[i];

                if (!File.Exists(path))
                {
                    continue;
                }

                string fileName =
                    Path.GetFileName(path);

                bool isCurrent =
                    !string.IsNullOrWhiteSpace(currentPath) &&
                    string.Equals(
                        path,
                        currentPath,
                        StringComparison.OrdinalIgnoreCase);

                string text =
                    $"{i + 1}. {fileName}" +
                    (isCurrent
                        ? Localization.Get("HistoryCurrentSuffix")
                        : string.Empty);

                ToolStripMenuItem item =
                    new ToolStripMenuItem(text)
                    {
                        Tag = path
                    };

                item.Click +=
                    HistoryItem_Click;

                item.MouseEnter +=
                    HistoryItem_MouseEnter;

                item.MouseLeave +=
                    HistoryItem_MouseLeave;

                historyMenu.Items.Add(item);
            }

            if (historyMenu.Items.Count == 0)
            {
                historyButton.Enabled = false;
                historyButton.Text = Localization.Get("History");
            }
        }

        /// <summary>
        /// Shows a preview for the hovered history entry.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private void HistoryItem_MouseEnter(
            object? sender,
            EventArgs e)
        {
            if (sender is not ToolStripMenuItem item ||
                item.Tag is not string path ||
                !File.Exists(path))
            {
                return;
            }

            ShowHistoryWallpaperPreview(path);
        }

        /// <summary>
        /// Hides the history preview when the pointer leaves an entry.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private void HistoryItem_MouseLeave(
            object? sender,
            EventArgs e)
        {
            HideWallpaperPreview();
        }

        /// <summary>
        /// Loads and positions a preview beside the selected history menu item.
        /// </summary>
        /// <param name="path">The image or folder path to process.</param>
        private void ShowHistoryWallpaperPreview(
            string path)
        {
            UpdateWallpaperPreview(path);

            Rectangle menuBounds =
                historyMenu.Bounds;

            Screen screen =
                Screen.FromRectangle(menuBounds);

            Rectangle area =
                screen.WorkingArea;

            int x =
                menuBounds.Right + 8;

            int y =
                menuBounds.Top;

            if (x + wallpaperPreviewForm.Width >
                area.Right)
            {
                x =
                    menuBounds.Left
                    - wallpaperPreviewForm.Width
                    - 8;
            }

            if (x < area.Left)
            {
                x = area.Left;
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

            // Form.Show would activate the preview and dismiss the open context menu.
            // Show the native window without activation instead.
            ShowWindow(
                wallpaperPreviewForm.Handle,
                SW_SHOWNOACTIVATE);
        }

        /// <summary>
        /// Opens an existing history image using the registered default application.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private void HistoryItem_Click(
            object? sender,
            EventArgs e)
        {
            if (sender is not ToolStripMenuItem item ||
                item.Tag is not string path ||
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
                    Localization.Get("MsgOpenHistoryImageFailed") +
                    ex.Message,
                    "Wallpaper Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Opens wallpaper statistics and connects image-selection and reset actions.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private void StatisticsButton_Click(
            object? sender,
            EventArgs e)
        {
            using StatisticsForm dialog =
                new StatisticsForm(
                    darkMode,
                    windowOpacityPercent,
                    statistics.ViewCounts,
                    statistics.LastShown,
                    statistics.DailyViewCounts,
                    statistics.RecurrenceCounts,
                    statistics.RecurrenceSeconds,
                    folderTextBox.Text,
                    statistics.StartedAt,
                    statistics.DailyStartedAt,
                    statistics.RecurrenceStartedAt,
                    statistics.Remove,
                    SetWallpaperFromStatistics,
                    ResetPersistentStatistics);

            dialog.ShowDialog(this);
        }

        /// <summary>
        /// Formats the localized history caption with its current entry count.
        /// </summary>
        private void UpdateHistoryButtonText()
        {
            historyButton.Text =
                wallpaperHistory.Count > 0
                ? string.Format(
                    Localization.CurrentCulture,
                    Localization.Get("HistoryCount"),
                    wallpaperHistory.Count)
                : Localization.Get("History");
        }
    }
}
