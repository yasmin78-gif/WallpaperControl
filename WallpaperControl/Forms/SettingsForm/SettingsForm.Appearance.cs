using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Settings dialog Appearance members. See README.md in this directory for the code map.
    internal sealed partial class SettingsForm
    {
        /// <summary>
        /// Applies the selected theme to the dialog preview without committing it.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private void ThemeComboBox_SelectedIndexChanged(
            object? sender,
            EventArgs e)
        {
            if (updatingThemePreview ||
                themeComboBox.SelectedItem
                    is not ThemeChoice choice)
            {
                return;
            }

            previewThemeMode =
                choice.Mode;

            ApplyTheme(
                ResolvePreviewDarkMode());
        }

        /// <summary>
        /// Refreshes the preview after Windows personalization changes on the UI thread.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private void SystemEvents_UserPreferenceChanged(
            object sender,
            UserPreferenceChangedEventArgs e)
        {
            if (IsDisposed ||
                !string.Equals(
                    previewThemeMode,
                    "system",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                BeginInvoke(() =>
                {
                    if (!IsDisposed)
                    {
                        ApplyTheme(
                            ResolvePreviewDarkMode());
                    }
                });
            }
            catch
            {
            }
        }

        /// <summary>
        /// Resolves the preview theme, consulting Windows when system mode is selected.
        /// </summary>
        /// <returns>True when the preview should use the dark palette.</returns>
        private bool ResolvePreviewDarkMode()
        {
            return AppSettingsStore.NormalizeThemeMode(
                previewThemeMode) switch
                {
                    "dark" => true,
                    "light" => false,
                    _ => WindowsTheme.IsDarkMode()
                };
        }

        /// <summary>
        /// Rebuilds localized theme choices while preserving the requested selection.
        /// </summary>
        /// <param name="selectedMode">The theme mode to keep selected.</param>
        private void RefreshThemeChoices(
            string selectedMode)
        {
            string normalized =
                AppSettingsStore.NormalizeThemeMode(
                    selectedMode);

            updatingThemePreview = true;

            try
            {
                themeComboBox.Items.Clear();

                themeComboBox.Items.Add(
                    new ThemeChoice(
                        Localization.Get(
                            "SettingsThemeSystem",
                            previewLanguageCode),
                        "system"));

                themeComboBox.Items.Add(
                    new ThemeChoice(
                        Localization.Get(
                            "SettingsThemeDark",
                            previewLanguageCode),
                        "dark"));

                themeComboBox.Items.Add(
                    new ThemeChoice(
                        Localization.Get(
                            "SettingsThemeLight",
                            previewLanguageCode),
                        "light"));

                for (int i = 0;
                     i < themeComboBox.Items.Count;
                     i++)
                {
                    if (themeComboBox.Items[i]
                            is ThemeChoice choice &&
                        string.Equals(
                            choice.Mode,
                            normalized,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        themeComboBox.SelectedIndex = i;
                        return;
                    }
                }

                themeComboBox.SelectedIndex = 0;
            }
            finally
            {
                updatingThemePreview = false;
            }
        }

        /// <summary>
        /// Applies the selected opacity to the dialog and updates its percentage label.
        /// </summary>
        private void UpdateOpacityPreview()
        {
            int value =
                opacityTrackBar.Value;

            opacityValueLabel.Text =
                $"{value}%";

            Opacity =
                value / 100.0;
        }

        /// <summary>
        /// Resets the appearance controls to their default preview values.
        /// </summary>
        private void ResetAppearanceSettings()
        {
            previewThemeMode =
                "system";

            RefreshThemeChoices(
                previewThemeMode);

            opacityTrackBar.Value = 92;

            ApplyTheme(
                ResolvePreviewDarkMode());
        }

        /// <summary>
        /// Applies preview colors to the dialog, its controls, navigation, and title bar.
        /// </summary>
        /// <param name="darkMode">True to use the dark palette; false to use the light palette.</param>
        private void ApplyTheme(
            bool darkMode)
        {
            // v1.8.1 settings palette: a slightly blue-tinted dark surface
            // keeps the sidebar, content and controls in the same visual family.
            Color background =
                AppTheme.WindowBackground(darkMode);

            Color foreground =
                AppTheme.TextPrimary(darkMode);

            Color inputBackground =
                AppTheme.InputBackground(darkMode);

            Color buttonBackground =
                AppTheme.ControlBackground(darkMode);

            BackColor = background;
            ForeColor = foreground;

            ApplyThemeToControls(
                Controls,
                darkMode,
                background,
                foreground,
                inputBackground,
                buttonBackground);

            if (settingsNavigationPanel != null)
            {
                settingsNavigationPanel.BackColor =
                    AppTheme.SidebarBackground(darkMode);
            }

            UpdateSettingsNavigationText();
            UpdateSettingsNavigationSelection();
            UpdateHotkeyValidation();
            ApplyTitleBarTheme(
                darkMode);

            Invalidate(
                true);
        }

        /// <summary>
        /// Recursively styles supported control types for the selected light or dark theme.
        /// </summary>
        /// <param name="controls">The controls to style or localize recursively.</param>
        /// <param name="darkMode">True to use the dark palette; false to use the light palette.</param>
        /// <param name="background">The background color to apply.</param>
        /// <param name="foreground">The primary foreground color to apply.</param>
        /// <param name="inputBackground">The background color used by editable controls.</param>
        /// <param name="buttonBackground">The background color used by action buttons.</param>
        private static void ApplyThemeToControls(
            Control.ControlCollection controls,
            bool darkMode,
            Color background,
            Color foreground,
            Color inputBackground,
            Color buttonBackground)
        {
            foreach (Control control
                in controls)
            {
                if (control is TabControl tabControl)
                {
                    tabControl.BackColor =
                        background;

                    tabControl.ForeColor =
                        foreground;
                }
                else if (control is TabPage tabPage)
                {
                    tabPage.BackColor =
                        background;

                    tabPage.ForeColor =
                        foreground;
                }
                else if (control is GroupBox groupBox)
                {
                    groupBox.BackColor =
                        background;

                    groupBox.ForeColor =
                        foreground;

                    groupBox.FlatStyle =
                        FlatStyle.Flat;
                }
                else if (control is Label label)
                {
                    label.BackColor =
                        Color.Transparent;

                    label.ForeColor =
                        foreground;
                }
                else if (control is CheckBox checkBox)
                {
                    checkBox.BackColor =
                        background;

                    checkBox.ForeColor =
                        foreground;
                }
                else if (control is TextBox textBox)
                {
                    textBox.BackColor =
                        inputBackground;

                    textBox.ForeColor =
                        foreground;
                }
                else if (control is ComboBox combo)
                {
                    combo.BackColor =
                        inputBackground;

                    combo.ForeColor =
                        foreground;
                }
                else if (control is TrackBar trackBar)
                {
                    trackBar.BackColor =
                        background;

                    trackBar.ForeColor =
                        foreground;
                }
                else if (control is Button button)
                {
                    button.UseVisualStyleBackColor =
                        false;

                    button.BackColor =
                        buttonBackground;

                    button.ForeColor =
                        foreground;

                    button.FlatStyle =
                        FlatStyle.Flat;

                    button.FlatAppearance.BorderColor =
                        AppTheme.Border(darkMode);

                    button.FlatAppearance.MouseOverBackColor =
                        AppTheme.ControlHover(darkMode);

                    button.FlatAppearance.MouseDownBackColor =
                        AppTheme.ControlPressed(darkMode);
                }

                if (control.HasChildren)
                {
                    ApplyThemeToControls(
                        control.Controls,
                        darkMode,
                        background,
                        foreground,
                        inputBackground,
                        buttonBackground);
                }
            }
        }

        /// <summary>
        /// Sets the native title-bar theme when the window handle is available.
        /// </summary>
        /// <param name="darkMode">True to use the dark palette; false to use the light palette.</param>
        private void ApplyTitleBarTheme(
            bool darkMode)
        {
            WindowsTheme.ApplyTitleBar(this, darkMode);
        }
    }
}
