using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Microsoft.Win32;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Coordinates accepting, resetting, and disposing the settings dialog. See Forms/SettingsForm/README.md.
    internal sealed partial class SettingsForm : Form
    {
        /// <summary>
        /// Restores the dialog's default values and refreshes appearance and widget previews.
        /// </summary>
        private void ResetAllSettings()
        {
            SetDefaultHotkeys();

            rejectRootTextBox.Text = "";
            rejectSubfolderCheckBox.Checked = true;
            autostartCheckBox.Checked = false;
            closeToTrayCheckBox.Checked = true;
            automaticUpdateCheckCheckBox.Checked = true;
            pauseOnFullscreenCheckBox.Checked = true;

            ResetAppearanceSettings();

            clockEnabledCheckBox.Checked = false;
            clockLockedCheckBox.Checked = false;
            clockSizeNumeric.Value = 150;
            clockSecondsCheckBox.Checked = false;
            RefreshClockStyleChoices(ClockWidgetStyle.Chrome);
            nextWidgetEnabledCheckBox.Checked = false;
            nextWidgetLockedCheckBox.Checked = false;
            RefreshNextStyleChoices(SystemWidgetStyle.Minimal);
            systemWidgetEnabledCheckBox.Checked = false;
            systemWidgetLockedCheckBox.Checked = false;
            systemWidgetRefreshComboBox.SelectedIndex = 1;
            RefreshSystemStyleChoices(SystemWidgetStyle.Glow);
            systemShowCpuCheckBox.Checked = true;
            systemShowRamCheckBox.Checked = true;
            systemShowGpuCheckBox.Checked = true;
            systemShowVramCheckBox.Checked = true;
            systemShowNetworkCheckBox.Checked = true;
            systemShowDrivesCheckBox.Checked = true;
            weatherWidgetEnabledCheckBox.Checked = false;
            weatherWidgetLockedCheckBox.Checked = false;
            weatherLocationTextBox.Text = "Karlsruhe";
            weatherWidgetRefreshComboBox.SelectedIndex = 1;
            RefreshWeatherStyleChoices(SystemWidgetStyle.Glow);
            weatherShowForecastCheckBox.Checked = true;
            calendarWidgetEnabledCheckBox.Checked = false;
            calendarWidgetLockedCheckBox.Checked = false;
            RefreshCalendarStyleChoices(SystemWidgetStyle.Glow);
            calendarMaxEntriesComboBox.SelectedIndex = 2;
            calendarRefreshComboBox.SelectedIndex = 1;
            calendarIcsUrlTextBox.Text = "";
            calendarHolidayIcsUrlTextBox.Text = "";
            calendarShowLocationCheckBox.Checked = true;

            ApplyPreviewLocalization(
                Localization.IsLanguageAvailable("de")
                ? "de"
                : Localization.CurrentLanguage);
        }

        /// <summary>
        /// Validates shortcuts and the rejection path, captures accepted values, and closes with an OK result.
        /// </summary>
        private void SaveAndClose()
        {
            GetComboValues(
                nextModifierCombo,
                nextKeyCombo,
                out uint nextModifiers,
                out uint nextKey);

            GetComboValues(
                pauseModifierCombo,
                pauseKeyCombo,
                out uint pauseModifiers,
                out uint pauseKey);

            GetComboValues(
                explorerModifierCombo,
                explorerKeyCombo,
                out uint explorerModifiers,
                out uint explorerKey);

            GetComboValues(
                rejectModifierCombo,
                rejectKeyCombo,
                out uint rejectModifiers,
                out uint rejectKey);

            (uint modifiers, uint key)[] activeHotkeys =
            {
                (nextModifiers, nextKey),
                (pauseModifiers, pauseKey),
                (explorerModifiers, explorerKey),
                (rejectModifiers, rejectKey)
            };

            bool duplicateFound = false;

            for (int i = 0; i < activeHotkeys.Length && !duplicateFound; i++)
            {
                for (int j = i + 1; j < activeHotkeys.Length; j++)
                {
                    if (IsDuplicate(
                        activeHotkeys[i].modifiers,
                        activeHotkeys[i].key,
                        activeHotkeys[j].modifiers,
                        activeHotkeys[j].key))
                    {
                        duplicateFound = true;
                        break;
                    }
                }
            }

            if (duplicateFound)
            {
                MessageBox.Show(
                    this,
                    Localization.Get(
                        "SettingsDuplicateHotkey",
                        previewLanguageCode),
                    "Wallpaper Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }


            NextModifiers = nextModifiers;
            NextKey = nextKey;
            PauseModifiers = pauseModifiers;
            PauseKey = pauseKey;
            ExplorerModifiers = explorerModifiers;
            ExplorerKey = explorerKey;
            RejectModifiers = rejectModifiers;
            RejectKey = rejectKey;

            string rejectRoot =
                rejectRootTextBox.Text.Trim();

            if (!string.IsNullOrWhiteSpace(rejectRoot))
            {
                try
                {
                    if (!Path.IsPathFullyQualified(rejectRoot))
                    {
                        throw new ArgumentException();
                    }

                    rejectRoot = Path.GetFullPath(rejectRoot);
                }
                catch (Exception ex) when (
                    ex is ArgumentException or
                    NotSupportedException or
                    PathTooLongException)
                {
                    MessageBox.Show(
                        this,
                        Localization.Get(
                            "SettingsRejectRootInvalid",
                            previewLanguageCode),
                        "Wallpaper Control",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);

                    rejectRootTextBox.Focus();
                    rejectRootTextBox.SelectAll();
                    return;
                }
            }

            RejectRootFolder = rejectRoot;
            RejectUseSubfolder =
                rejectSubfolderCheckBox.Checked;

            AutostartEnabled =
                autostartCheckBox.Checked;

            CloseToTrayEnabled =
                closeToTrayCheckBox.Checked;

            PauseOnFullscreen = pauseOnFullscreenCheckBox.Checked;
            AutomaticUpdateCheckEnabled =
                automaticUpdateCheckCheckBox.Checked;

            WindowOpacityPercent =
                opacityTrackBar.Value;

            ThemeMode =
                AppSettingsStore.NormalizeThemeMode(
                    previewThemeMode);

            WidgetSettings = ReadWidgetSettings(applySaveDefaults: true);

            if (!Localization.IsLanguageAvailable(
                previewLanguageCode))
            {
                RefreshLanguageChoices(
                    Localization.CurrentLanguage);

                previewLanguageCode =
                    Localization.CurrentLanguage;
            }

            Localization.SetLanguage(
                previewLanguageCode);

            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>
        /// Applies the current preview theme to the newly created native title bar.
        /// </summary>
        protected override void OnHandleCreated(
            EventArgs e)
        {
            base.OnHandleCreated(e);

            ApplyTitleBarTheme(
                ResolvePreviewDarkMode());
        }

        /// <summary>
        /// Creates a font owned by the dialog and tracked for disposal.
        /// </summary>
        private Font CreateOwnedFont(string familyName, float emSize, FontStyle style = FontStyle.Regular)
        {
            Font font = new Font(familyName, emSize, style);
            ownedFonts.Add(font);
            return font;
        }

        /// <summary>
        /// Unsubscribes from Windows preference changes and releases dialog-owned fonts.
        /// </summary>
        protected override void Dispose(
            bool disposing)
        {
            if (disposing)
            {
                SystemEvents.UserPreferenceChanged -=
                    SystemEvents_UserPreferenceChanged;

                foreach (Font font in ownedFonts)
                {
                    font.Dispose();
                }
                ownedFonts.Clear();
            }

            base.Dispose(
                disposing);
        }
    }
}
