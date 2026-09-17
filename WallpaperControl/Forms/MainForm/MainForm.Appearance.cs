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

            foreach (Control control in Controls)
            {
                if (control is MainFormComboBox combo)
                {
                    combo.BackColor = inputBackground;
                    combo.ForeColor = inputForeground;
                    combo.MutedColor = AppTheme.TextSecondary(darkMode);
                    combo.ItemHeight = Math.Max(combo.Font.Height + 8, 26);
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

        /// <summary>
        /// Arranges the controls without a warning area.
        /// </summary>
        private void SetNormalLayout() => ArrangeMainForm(0);

        /// <summary>
        /// Reserves space for a warning and, when needed, the activation button.
        /// </summary>
        /// <param name="showActivateButton">Whether the warning layout should include the activation action.</param>
        private void SetWarningLayout(bool showActivateButton) => ArrangeMainForm(showActivateButton ? 76 : 40);

        /// <summary>
        /// Positions the main-window controls using the requested warning-area offset.
        /// </summary>
        /// <param name="offset">The vertical layout offset in logical pixels.</param>
        private void ArrangeMainForm(int offset)
        {
            SuspendLayout();
            float scale = DeviceDpi / 96f;
            // Scales a logical layout dimension for the current display density.
            int Px(int value) => (int)Math.Round(value * scale);
            // Positions and sizes a control using the scaled main-window layout coordinates.
            void Place(Control control, int x, int y, int width, int height)
            {
                if (control is Label label) label.AutoSize = false;
                control.SetBounds(Px(x), Px(y), Px(width), Px(height));
            }
            Place(mainHeading, 48, 14, 329, 30);
            Place(aboutButton, 12, 15, 28, 28);
            Place(settingsButton, 385, 15, 28, 28);
            Place(statusLabel, 25, 50, 375, 25);
            Place(activateButton, 25, 80, 375, 34);
            Place(folderLabel, 25, 56 + offset, 375, 24);
            Place(folderTextBox, 25, 84 + offset, 300, 28);
            Place(folderButton, 335, 83 + offset, 65, 28);
            Place(wallpaperCountLabel, 25, 114 + offset, 375, 20);
            Place(intervalLabel, 25, 143 + offset, 375, 24);
            Place(intervalComboBox, 25, 171 + offset, 375, 28);
            Place(windowsIntervalLabel, 25, 202 + offset, 375, 20);
            Place(shuffleCheckBox, 25, 224 + offset, 375, 24);
            Place(positionLabel, 25, 256 + offset, 375, 24);
            Place(positionComboBox, 25, 284 + offset, 375, 28);
            Place(transitionLabel, 25, 322 + offset, 125, 24);
            Place(directionHeading, 160, 322 + offset, 115, 24);
            Place(transitionDurationLabel, 285, 322 + offset, 115, 24);
            Place(transitionComboBox, 25, 350 + offset, 125, 28);
            Place(transitionDirectionComboBox, 160, 350 + offset, 115, 28);
            Place(transitionDurationComboBox, 285, 350 + offset, 115, 28);
            Place(nextWallpaperButton, 25, 396 + offset, 375, 44);
            Place(currentHeading, 25, 458 + offset, 375, 22);
            Place(currentWallpaperLabel, 25, 481 + offset, 375, 24);
            Place(pauseButton, 25, 518 + offset, 180, 34);
            Place(pinButton, 220, 518 + offset, 180, 34);
            Place(explorerButton, 25, 562 + offset, 180, 34);
            Place(rejectButton, 220, 562 + offset, 180, 34);
            Place(undoRejectButton, 25, 605 + offset, 375, 30);
            Place(historyButton, 25, 646 + offset, 180, 30);
            Place(statisticsButton, 220, 646 + offset, 180, 30);
            int tab = 0;
            foreach (Control control in new Control[] { folderButton, intervalComboBox, shuffleCheckBox,
                positionComboBox, transitionComboBox, transitionDirectionComboBox, transitionDurationComboBox,
                nextWallpaperButton, pauseButton, pinButton, explorerButton, rejectButton, undoRejectButton,
                historyButton, statisticsButton }) control.TabIndex = tab++;
            ClientSize = new Size(Px(425), Px(690 + offset));
            ResumeLayout();
        }
    }
}
