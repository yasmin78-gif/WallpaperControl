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
    // Main-window settings responsibilities; see README.md in this directory for the code map.
    public partial class MainForm
    {
        /// <summary>
        /// Loads the wallpaper folder, slideshow options, and image positioning.
        /// </summary>
        private void LoadSettings()
        {
            LoadSlideshowFolder();
            LoadSlideshowOptions();
            LoadWallpaperPosition();
        }

        /// <summary>
        /// Reads whether Windows has an autostart entry for Wallpaper Control.
        /// </summary>
        private void LoadAutostartState()
        {
            try
            {
                using RegistryKey? key =
                    Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Run");

                string? value =
                    key?.GetValue(
                        "WallpaperControl")
                    as string;

                autostartEnabled =
                    !string.IsNullOrWhiteSpace(
                        value);
            }
            catch
            {
                autostartEnabled =
                    false;
            }
        }

        /// <summary>
        /// Loads the preference that controls whether closing hides the window to the tray.
        /// </summary>
        private void LoadCloseToTraySetting() => closeToTrayEnabled = appSettings.LoadCloseToTraySetting();

        /// <summary>
        /// Adds or removes the Windows autostart entry for the current executable.
        /// </summary>
        /// <param name="enabled">True to register this executable for Windows startup; false to remove the entry.</param>
        private void SetAutostart(
            bool enabled)
        {
            try
            {
                using RegistryKey key =
                    Registry.CurrentUser.CreateSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Run");

                if (enabled)
                {
                    key.SetValue(
                        "WallpaperControl",
                        "\"" +
                        Application.ExecutablePath +
                        "\" --tray",
                        RegistryValueKind.String);
                }
                else
                {
                    key.DeleteValue(
                        "WallpaperControl",
                        false);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Localization.Get("MsgAutostartFailed") +
                    ex.Message,
                    "Wallpaper Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Opens the application information dialog using the current theme.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private void AboutButton_Click(
            object? sender,
            EventArgs e)
        {
            using AboutForm dialog =
                new AboutForm(
                    darkMode,
                    windowOpacityPercent);

            dialog.ShowDialog(this);
        }

        /// <summary>
        /// Previews widget changes and commits accepted preferences, hotkeys, theme, and localization.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private void SettingsButton_Click(
            object? sender,
            EventArgs e)
        {
            WidgetSettings originalWidgetSettings =
                widgetManager.Settings;

            using SettingsForm dialog =
                new SettingsForm(
                    darkMode,
                    themeMode,
                    hotkeyNextModifiers,
                    hotkeyNextKey,
                    hotkeyPauseModifiers,
                    hotkeyPauseKey,
                    hotkeyExplorerModifiers,
                    hotkeyExplorerKey,
                    hotkeyRejectModifiers,
                    hotkeyRejectKey,
                    rejectRootFolder,
                    rejectUseSubfolder,
                    autostartEnabled,
                    closeToTrayEnabled,
                    automaticUpdateCheckEnabled,
                    pauseOnFullscreen,
                    windowOpacityPercent,
                    originalWidgetSettings,
                    previewSettings =>
                        widgetManager.Preview(previewSettings));
            dialog.ConfigureNotesManager(widgetManager.ShowNotesManager);

            string languageBefore =
                Localization.CurrentLanguage;

            long saveRevision = SettingsPersistence.FailureRevision;

            if (dialog.ShowDialog(this) !=
                DialogResult.OK)
            {
                widgetManager.CancelPreview(
                    originalWidgetSettings);
                return;
            }

            bool languageChanged =
                !string.Equals(
                    languageBefore,
                    Localization.CurrentLanguage,
                    StringComparison.OrdinalIgnoreCase);

            bool nextHotkeyChanged =
                hotkeyNextModifiers != dialog.NextModifiers ||
                hotkeyNextKey != dialog.NextKey;

            bool pauseHotkeyChanged =
                hotkeyPauseModifiers != dialog.PauseModifiers ||
                hotkeyPauseKey != dialog.PauseKey;

            bool explorerHotkeyChanged =
                hotkeyExplorerModifiers != dialog.ExplorerModifiers ||
                hotkeyExplorerKey != dialog.ExplorerKey;

            bool rejectHotkeyChanged =
                hotkeyRejectModifiers != dialog.RejectModifiers ||
                hotkeyRejectKey != dialog.RejectKey;

            hotkeyNextModifiers =
                dialog.NextModifiers;

            hotkeyNextKey =
                dialog.NextKey;

            hotkeyPauseModifiers =
                dialog.PauseModifiers;

            hotkeyPauseKey =
                dialog.PauseKey;

            hotkeyExplorerModifiers =
                dialog.ExplorerModifiers;

            hotkeyExplorerKey =
                dialog.ExplorerKey;

            hotkeyRejectModifiers =
                dialog.RejectModifiers;

            hotkeyRejectKey =
                dialog.RejectKey;

            rejectRootFolder =
                dialog.RejectRootFolder;

            rejectUseSubfolder =
                dialog.RejectUseSubfolder;

            bool newAutostartEnabled =
                dialog.AutostartEnabled;

            bool newCloseToTrayEnabled =
                dialog.CloseToTrayEnabled;

            bool newAutomaticUpdateCheckEnabled =
                dialog.AutomaticUpdateCheckEnabled;

            int newWindowOpacityPercent =
                dialog.WindowOpacityPercent;

            WidgetSettings newWidgetSettings =
                dialog.WidgetSettings;

            string newThemeMode =
                AppSettingsStore.NormalizeThemeMode(
                    dialog.ThemeMode);

            pauseOnFullscreen = dialog.PauseOnFullscreen;
            appSettings.SavePauseOnFullscreen(pauseOnFullscreen);
            _ = UpdateFullscreenPauseAsync();
            SaveHotkeySettings();
            SaveRejectSettings();

            if (newAutostartEnabled !=
                autostartEnabled)
            {
                SetAutostart(
                    newAutostartEnabled);

                autostartEnabled =
                    newAutostartEnabled;
            }

            if (newCloseToTrayEnabled !=
                closeToTrayEnabled)
            {
                closeToTrayEnabled =
                    newCloseToTrayEnabled;

                appSettings.SaveCloseToTraySetting(
                    closeToTrayEnabled);
            }

            if (newAutomaticUpdateCheckEnabled !=
                automaticUpdateCheckEnabled)
            {
                automaticUpdateCheckEnabled =
                    newAutomaticUpdateCheckEnabled;

                appSettings.SaveAutomaticUpdateCheckSetting(
                    automaticUpdateCheckEnabled);
            }

            if (newWindowOpacityPercent !=
                windowOpacityPercent)
            {
                windowOpacityPercent =
                    newWindowOpacityPercent;

                Opacity =
                    windowOpacityPercent / 100.0;

                appSettings.SaveWindowOpacityPercent(
                    windowOpacityPercent);
            }

            if (!string.Equals(
                    newThemeMode,
                    themeMode,
                    StringComparison.OrdinalIgnoreCase))
            {
                themeMode =
                    newThemeMode;

                appSettings.SaveThemeMode(
                    themeMode);

                ApplyWindowsTheme();
            }

            widgetManager.CommitPreview(newWidgetSettings);

            if (IsHandleCreated)
            {
                ReRegisterChangedHotKeys(
                    nextHotkeyChanged,
                    pauseHotkeyChanged,
                    explorerHotkeyChanged,
                    rejectHotkeyChanged,
                    showErrors: true);
            }

            if (languageChanged)
            {
                ApplyLocalization();
            }

            if (saveRevision != SettingsPersistence.FailureRevision)
                MessageBox.Show(this, Localization.Get("SettingsSaveFailed"), "Wallpaper Control",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
