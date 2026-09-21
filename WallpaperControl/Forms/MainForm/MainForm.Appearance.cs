using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Main-window appearance responsibilities; see README.md in this directory for the code map.
    public partial class MainForm
    {
        /// <summary>
        /// Creates a font whose lifetime is owned by this form and ended during disposal.
        /// </summary>
        /// <param name="familyName">The name of the font family to create.</param>
        /// <param name="emSize">The font size in the units used by the drawing operation.</param>
        /// <param name="style">The weight and decoration applied to the font.</param>
        /// <returns>The font owned by the form; it is released when the form is disposed.</returns>
        private Font CreateOwnedFont(string familyName, float emSize, FontStyle style = FontStyle.Regular)
        {
            Font font = new Font(familyName, emSize, style);
            ownedFonts.Add(font);
            return font;
        }

        /// <summary>
        /// Refreshes the theme after Windows personalization changes, marshaling back to the UI thread.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private void SystemEvents_UserPreferenceChanged(
            object sender,
            UserPreferenceChangedEventArgs e)
        {
            if (IsDisposed)
                return;

            if (!string.Equals(
                    themeMode,
                    "system",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            BeginInvoke(() =>
            {
                ApplyWindowsTheme();
            });
        }

        /// <summary>
        /// Applies the selected light, dark, or system theme to the main window and its menus.
        /// </summary>
        private void ApplyWindowsTheme()
        {
            darkMode =
                themeMode.ToLowerInvariant() switch
                {
                    "dark" => true,
                    "light" => false,
                    _ => WindowsTheme.IsDarkMode()
                };

            Color background =
                AppTheme.WindowBackground(darkMode);

            widgetManager.RefreshWebTheme(darkMode);

            Color foreground =
                AppTheme.TextPrimary(darkMode);

            Color inputBackground =
                AppTheme.InputBackground(darkMode);

            Color inputForeground =
                darkMode
                ? AppTheme.DarkTextPrimary
                : SystemColors.WindowText;

            Color buttonBackground =
                AppTheme.ControlBackground(darkMode);

            BackColor = background;
            ForeColor = foreground;

            folderLabel.ForeColor = foreground;
            wallpaperCountLabel.ForeColor = foreground;
            intervalLabel.ForeColor = foreground;

            statusLabel.ForeColor =
                darkMode
                ? Color.Orange
                : Color.DarkOrange;

            folderTextBox.BackColor =
                inputBackground;

            folderTextBox.ForeColor =
                inputForeground;

            intervalComboBox.BackColor =
                inputBackground;

            intervalComboBox.ForeColor =
                inputForeground;

            windowsIntervalLabel.ForeColor =
                AppTheme.TextSecondary(darkMode);

            shuffleCheckBox.ForeColor =
                foreground;

            positionLabel.ForeColor =
                foreground;

            positionComboBox.BackColor =
                inputBackground;

            positionComboBox.ForeColor =
                inputForeground;

            settingsButton.BackColor =
                background;

            settingsButton.ForeColor =
                AppTheme.TextSecondary(darkMode);

            settingsButton.FlatAppearance.MouseOverBackColor =
                AppTheme.ControlHover(darkMode);

            settingsButton.FlatAppearance.MouseDownBackColor =
                AppTheme.ControlPressed(darkMode);

            aboutButton.BackColor =
                background;

            aboutButton.ForeColor =
                AppTheme.TextSecondary(darkMode);

            aboutButton.FlatAppearance.MouseOverBackColor =
                AppTheme.ControlHover(darkMode);

            aboutButton.FlatAppearance.MouseDownBackColor =
                AppTheme.ControlPressed(darkMode);

            StyleButton(
                folderButton,
                buttonBackground,
                foreground);

            StyleButton(
                activateButton,
                buttonBackground,
                foreground);

            StyleButton(
                pauseButton,
                buttonBackground,
                foreground);

            StyleButton(
                pinButton,
                buttonBackground,
                foreground);

            StyleButton(
                nextWallpaperButton,
                buttonBackground,
                foreground);
            StyleButton(
                explorerButton,
                buttonBackground,
                foreground);

            StyleButton(
                rejectButton,
                buttonBackground,
                foreground);

            StyleButton(
                undoRejectButton,
                buttonBackground,
                foreground);

            StyleButton(
                historyButton,
                buttonBackground,
                foreground);

            StyleButton(
                statisticsButton,
                buttonBackground,
                foreground);

            historyMenu.BackColor =
                AppTheme.MenuBackground(darkMode);

            historyMenu.ForeColor =
                foreground;

            rejectMenu.BackColor =
                AppTheme.MenuBackground(darkMode);

            rejectMenu.ForeColor =
                foreground;

            currentWallpaperLabel.ForeColor =
                foreground;

            wallpaperPreviewForm.BackColor =
                AppTheme.PanelBackground(darkMode);

            wallpaperPreviewInfoLabel.ForeColor =
                foreground;

            wallpaperPreviewPictureBox.BackColor =
                Color.Black;

            trayMenu.BackColor =
                AppTheme.MenuBackground(darkMode);

            trayMenu.ForeColor =
                foreground;

            foreach (Control control in WallpaperControls())
            {
                if (control is MainFormComboBox combo)
                {
                    combo.BackColor = inputBackground;
                    combo.ForeColor = inputForeground;
                    combo.MutedColor = AppTheme.TextSecondary(darkMode);
                    combo.ItemHeight = Math.Max(combo.Font.Height + (int)Math.Round(8 * DeviceDpi / 96f),
                        (int)Math.Round(26 * DeviceDpi / 96f));
                }
                if (control is MainFormButton button)
                    button.MutedColor = AppTheme.TextSecondary(darkMode);
            }
            mainHeading.ForeColor = foreground;
            currentHeading.ForeColor = foreground;
            directionHeading.ForeColor = foreground;
            wallpaperCountLabel.ForeColor = AppTheme.TextSecondary(darkMode);
            nextWallpaperButton.BackColor = Color.FromArgb(29, 105, 184);
            nextWallpaperButton.ForeColor = Color.White;
            nextWallpaperButton.FlatAppearance.BorderColor = Color.FromArgb(29, 105, 184);
            nextWallpaperButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(37, 121, 207);
            nextWallpaperButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(23, 85, 151);
            ApplyWallpaperPageTheme();
            UpdateWidgetPresentation();
            ApplyTitleBarTheme();
            Invalidate(true);
        }

        /// <summary>
        /// Applies foreground, background, and border colors to a main-window button.
        /// </summary>
        /// <param name="button">The button whose appearance is updated.</param>
        /// <param name="background">The background color to apply.</param>
        /// <param name="foreground">The primary foreground color to apply.</param>
        private void StyleButton(
            Button button,
            Color background,
            Color foreground)
        {
            button.UseVisualStyleBackColor = false;
            button.BackColor = background;
            button.ForeColor = foreground;
            button.FlatStyle = FlatStyle.Flat;

            button.FlatAppearance.BorderColor =
                AppTheme.Border(darkMode);

            button.FlatAppearance.MouseOverBackColor =
                AppTheme.ControlHover(darkMode);

            button.FlatAppearance.MouseDownBackColor =
                AppTheme.ControlPressed(darkMode);
        }

        /// <summary>
        /// Updates the native title bar to match the current theme.
        /// </summary>
        private void ApplyTitleBarTheme()
        {
            WindowsTheme.ApplyTitleBar(this, darkMode);
        }

    }
}
