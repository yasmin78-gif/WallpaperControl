using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Settings dialog Rejection members. See README.md in this directory for the code map.
    internal sealed partial class SettingsForm
    {
        /// <summary>
        /// Prompts for the root folder used to store rejected wallpapers.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private void RejectRootBrowseButton_Click(
            object? sender,
            EventArgs e)
        {
            using FolderBrowserDialog dialog =
                new FolderBrowserDialog
                {
                    Description =
                        Localization.Get(
                            "SettingsSelectRejectFolder",
                            previewLanguageCode),
                    UseDescriptionForTitle = true
                };

            if (!string.IsNullOrWhiteSpace(
                rejectRootTextBox.Text) &&
                Directory.Exists(
                    rejectRootTextBox.Text))
            {
                dialog.SelectedPath =
                    rejectRootTextBox.Text;
            }

            if (dialog.ShowDialog(this) ==
                DialogResult.OK)
            {
                rejectRootTextBox.Text =
                    dialog.SelectedPath;
            }
        }
    }
}
