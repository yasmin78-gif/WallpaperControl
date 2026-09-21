using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Microsoft.Win32;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Settings dialog Initialization members. See README.md in this directory for the code map.
    internal sealed partial class SettingsForm
    {
        /// <summary>
        /// Builds the settings pages and subscriptions in dependency order, then initializes live previews.
        /// </summary>
        /// <param name="darkMode">True to use the dark palette; false to use the light palette.</param>
        /// <param name="themeMode">The light, dark, or system theme preference.</param>
        /// <param name="nextModifiers">The modifier flags for the next-wallpaper shortcut.</param>
        /// <param name="nextKey">The virtual key for the next-wallpaper shortcut.</param>
        /// <param name="pauseModifiers">The modifier flags for the pause shortcut.</param>
        /// <param name="pauseKey">The virtual key for the pause shortcut.</param>
        /// <param name="explorerModifiers">The modifier flags for the Explorer shortcut.</param>
        /// <param name="explorerKey">The virtual key for the Explorer shortcut.</param>
        /// <param name="rejectModifiers">The modifier flags for the rejection shortcut.</param>
        /// <param name="rejectKey">The virtual key for the rejection shortcut.</param>
        /// <param name="rejectRootFolder">The configured root folder for rejected wallpapers.</param>
        /// <param name="rejectUseSubfolder">Whether rejection uses a subfolder of the wallpaper source.</param>
        /// <param name="autostartEnabled">Whether Windows should start the application at sign-in.</param>
        /// <param name="closeToTrayEnabled">Whether closing the window should hide it to the tray.</param>
        /// <param name="automaticUpdateCheckEnabled">Whether automatic release checks are enabled.</param>
        /// <param name="pauseOnFullscreen">Whether foreground fullscreen applications should suspend background activity.</param>
        /// <param name="windowOpacityPercent">The window opacity as a percentage.</param>
        public SettingsForm(
            bool darkMode,
            string themeMode,
            uint nextModifiers,
            uint nextKey,
            uint pauseModifiers,
            uint pauseKey,
            uint explorerModifiers,
            uint explorerKey,
            uint rejectModifiers,
            uint rejectKey,
            string rejectRootFolder,
            bool rejectUseSubfolder,
            bool autostartEnabled,
            bool closeToTrayEnabled,
            bool automaticUpdateCheckEnabled,
            bool pauseOnFullscreen,
            int windowOpacityPercent)
        {
            #region Window and preview state

            previewLanguageCode =
                Localization.CurrentLanguage;

            previewThemeMode =
                AppSettingsStore.NormalizeThemeMode(
                    themeMode);

            ThemeMode =
                previewThemeMode;

            Text = Localization.Get(
                "SettingsTitle",
                previewLanguageCode);

            FormBorderStyle =
                FormBorderStyle.FixedDialog;

            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition =
                FormStartPosition.CenterParent;

            ClientSize =
                new Size(1120, 760);

            Font =
                CreateOwnedFont("Segoe UI", 10);

            #endregion

            #region Sidebar and page navigation

            Panel navigationPanel =
                new Panel
                {
                    Location = new Point(0, 0),
                    Size = new Size(220, 710),
                    Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left,
                    Padding = new Padding(14, 18, 14, 18)
                };

            settingsNavigationPanel = navigationPanel;

            Label navigationTitle =
                new Label
                {
                    Text = "Wallpaper Control",
                    Location = new Point(18, 18),
                    Size = new Size(185, 34),
                    Font = CreateOwnedFont("Segoe UI", 12.5f, FontStyle.Bold),
                    TextAlign = ContentAlignment.MiddleLeft
                };

            navigationPanel.Controls.Add(navigationTitle);

            TabControl tabControl =
                new TabControl
                {
                    Location = new Point(235, 18),
                    Size = new Size(865, 665),
                    Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                    Appearance = TabAppearance.FlatButtons,
                    SizeMode = TabSizeMode.Fixed,
                    ItemSize = new Size(0, 1),
                    Multiline = true
                };

            settingsTabControl = tabControl;

            TabPage hotkeysPage =
                new TabPage
                {
                    Text = Localization.Get(
                        "SettingsTabHotkeys",
                        previewLanguageCode),
                    Tag = "SettingsTabHotkeys"
                };

            TabPage generalPage = CreateSettingsPage("SettingsNavGeneral");
            TabPage rejectPage = CreateSettingsPage("SettingsNavReject");
            TabPage appearancePage = CreateSettingsPage("SettingsNavAppearance");
            TabPage languagePage = CreateSettingsPage("SettingsNavLanguage");

            tabControl.TabPages.Add(hotkeysPage);
            tabControl.TabPages.Add(generalPage);
            tabControl.TabPages.Add(rejectPage);
            tabControl.TabPages.Add(appearancePage);
            tabControl.TabPages.Add(languagePage);

            AddSettingsNavigationButton(navigationPanel, tabControl, generalPage, "⚙", "SettingsNavGeneral", 72);
            AddSettingsNavigationButton(navigationPanel, tabControl, rejectPage, "▣", "SettingsNavReject", 118);
            AddSettingsNavigationButton(navigationPanel, tabControl, hotkeysPage, "⌨", "SettingsTabHotkeys", 164);

            settingsAppearanceNavigationButton = AddSettingsNavigationButton(navigationPanel, tabControl, appearancePage, "◐", "SettingsNavAppearance", 210);
            settingsLanguageNavigationButton = AddSettingsNavigationButton(navigationPanel, tabControl, languagePage, "◎", "SettingsNavLanguage", 256);

            tabControl.SelectedTab = generalPage;
            UpdateSettingsNavigationSelection();
            tabControl.SelectedIndexChanged += (_, _) => UpdateSettingsNavigationSelection();

            #endregion

            #region Hotkeys page

            Label titleLabel = new Label
            {
                Text = Localization.Get(
                    "SettingsHotkeysTitle",
                    previewLanguageCode),
                Tag = "SettingsHotkeysTitle",
                Location = new Point(18, 18),
                AutoSize = true,
                Font = CreateOwnedFont(
                    "Segoe UI",
                    12,
                    FontStyle.Bold)
            };

            Label hintLabel = new Label
            {
                Text = Localization.Get(
                    "SettingsHotkeysHint",
                    previewLanguageCode),
                Tag = "SettingsHotkeysHint",
                Location = new Point(18, 48),
                Size = new Size(475, 42)
            };

            hotkeysPage.Controls.Add(
                titleLabel);

            hotkeysPage.Controls.Add(
                hintLabel);

            CreateHotkeyRow(
                hotkeysPage,
                "SettingsHotkeyNext",
                100,
                out nextModifierCombo,
                out nextKeyCombo);

            CreateHotkeyRow(
                hotkeysPage,
                "SettingsHotkeyPause",
                145,
                out pauseModifierCombo,
                out pauseKeyCombo);

            CreateHotkeyRow(
                hotkeysPage,
                "SettingsHotkeyExplorer",
                190,
                out explorerModifierCombo,
                out explorerKeyCombo);

            CreateHotkeyRow(
                hotkeysPage,
                "SettingsHotkeyReject",
                235,
                out rejectModifierCombo,
                out rejectKeyCombo);

            SetComboValues(
                nextModifierCombo,
                nextKeyCombo,
                nextModifiers,
                nextKey);

            SetComboValues(
                pauseModifierCombo,
                pauseKeyCombo,
                pauseModifiers,
                pauseKey);

            SetComboValues(
                explorerModifierCombo,
                explorerKeyCombo,
                explorerModifiers,
                explorerKey);

            SetComboValues(
                rejectModifierCombo,
                rejectKeyCombo,
                rejectModifiers,
                rejectKey);

            hotkeyWarningLabel =
                new Label
                {
                    Text =
                        Localization.Get(
                            "SettingsHotkeyConflictInline",
                            previewLanguageCode),
                    Tag =
                        "SettingsHotkeyConflictInline",
                    Location =
                        new Point(18, 285),
                    Size =
                        new Size(475, 42),
                    Font =
                        CreateOwnedFont(
                            "Segoe UI",
                            8.5f,
                            FontStyle.Bold),
                    Visible = false
                };

            Button resetHotkeysButton =
                new Button
                {
                    Text =
                        Localization.Get(
                            "SettingsResetHotkeys",
                            previewLanguageCode),
                    Tag =
                        "SettingsResetHotkeys",
                    Location =
                        new Point(293, 345),
                    Size =
                        new Size(200, 34)
                };

            resetHotkeysButton.Click +=
                (_, _) =>
                {
                    SetDefaultHotkeys();
                    UpdateHotkeyValidation();
                };

            hotkeysPage.Controls.Add(
                hotkeyWarningLabel);

            hotkeysPage.Controls.Add(
                resetHotkeysButton);

            HookHotkeyValidation();

            #endregion

            #region Rejection and general pages

            Label rejectTitleLabel =
                new Label
                {
                    Text =
                        Localization.Get(
                            "SettingsRejectTitle",
                            previewLanguageCode),
                    Tag =
                        "SettingsRejectTitle",
                    Location =
                        new Point(18, 18),
                    AutoSize = true,
                    Font =
                        CreateOwnedFont(
                            "Segoe UI",
                            12,
                            FontStyle.Bold)
                };

            Label rejectFolderLabel =
                new Label
                {
                    Text =
                        Localization.Get(
                            "SettingsRejectFolder",
                            previewLanguageCode),
                    Tag =
                        "SettingsRejectFolder",
                    Location =
                        new Point(18, 58),
                    Size =
                        new Size(475, 24)
                };

            rejectRootTextBox =
                new TextBox
                {
                    Location =
                        new Point(18, 84),
                    Size =
                        new Size(425, 28),
                    ReadOnly = true,
                    Text =
                        rejectRootFolder
                };

            rejectRootBrowseButton =
                new Button
                {
                    Text = "...",
                    Location =
                        new Point(451, 83),
                    Size =
                        new Size(42, 29)
                };

            rejectRootBrowseButton.Click +=
                RejectRootBrowseButton_Click;

            rejectSubfolderCheckBox =
                new CheckBox
                {
                    Text =
                        Localization.Get(
                            "SettingsRejectSubfolder",
                            previewLanguageCode),
                    Tag =
                        "SettingsRejectSubfolder",
                    Location =
                        new Point(18, 124),
                    AutoSize = true,
                    Checked =
                        rejectUseSubfolder
                };

            Label rejectHintLabel =
                new Label
                {
                    Text =
                        Localization.Get(
                            "SettingsRejectHint",
                            previewLanguageCode),
                    Tag =
                        "SettingsRejectHint",
                    Location =
                        new Point(18, 154),
                    Size =
                        new Size(475, 48),
                    Font =
                        CreateOwnedFont(
                            "Segoe UI",
                            8.25f)
                };

            Label generalTitleLabel =
                new Label
                {
                    Text =
                        Localization.Get(
                            "SettingsGeneralTitle",
                            previewLanguageCode),
                    Tag =
                        "SettingsGeneralTitle",
                    Location =
                        new Point(18, 18),
                    AutoSize = true,
                    Font =
                        CreateOwnedFont(
                            "Segoe UI",
                            12,
                            FontStyle.Bold)
                };

            autostartCheckBox =
                new CheckBox
                {
                    Text =
                        Localization.Get(
                            "SettingsAutostart",
                            previewLanguageCode),
                    Tag =
                        "SettingsAutostart",
                    Location =
                        new Point(18, 62),
                    AutoSize = true,
                    Checked =
                        autostartEnabled
                };

            closeToTrayCheckBox =
                new CheckBox
                {
                    Text =
                        Localization.Get(
                            "SettingsCloseToTray",
                            previewLanguageCode),
                    Tag =
                        "SettingsCloseToTray",
                    Location =
                        new Point(18, 98),
                    AutoSize = true,
                    Checked =
                        closeToTrayEnabled
                };

            Label updatesTitleLabel =
                new Label
                {
                    Text =
                        Localization.Get(
                            "SettingsUpdatesTitle",
                            previewLanguageCode),
                    Tag = "SettingsUpdatesTitle",
                    Location = new Point(18, 154),
                    AutoSize = true,
                    Font = CreateOwnedFont(
                        "Segoe UI",
                        11,
                        FontStyle.Bold)
                };

            automaticUpdateCheckCheckBox =
                new CheckBox
                {
                    Text =
                        Localization.Get(
                            "SettingsAutomaticUpdateCheck",
                            previewLanguageCode),
                    Tag = "SettingsAutomaticUpdateCheck",
                    Location = new Point(18, 194),
                    AutoSize = true,
                    Checked = automaticUpdateCheckEnabled
                };

            Label updateCheckHintLabel =
                new Label
                {
                    Text =
                        Localization.Get(
                            "SettingsAutomaticUpdateCheckHint",
                            previewLanguageCode),
                    Tag = "SettingsAutomaticUpdateCheckHint",
                    Location = new Point(38, 224),
                    Size = new Size(650, 42),
                    Font = CreateOwnedFont(
                        "Segoe UI",
                        8.25f)
                };

            checkForUpdatesButton =
                new Button
                {
                    Text =
                        Localization.Get(
                            "SettingsCheckForUpdatesNow",
                            previewLanguageCode),
                    Tag = "SettingsCheckForUpdatesNow",
                    Location = new Point(18, 278),
                    Size = new Size(210, 36)
                };

            pauseOnFullscreenCheckBox = new CheckBox
            {
                Text = Localization.Get("SettingsPauseOnFullscreen", previewLanguageCode),
                Tag = "SettingsPauseOnFullscreen",
                Location = new Point(18, 346), Size = new Size(650, 30),
                Checked = pauseOnFullscreen
            };
            var fullscreenHint = new Label
            {
                Text = Localization.Get("SettingsPauseOnFullscreenHint", previewLanguageCode),
                Tag = "SettingsPauseOnFullscreenHint",
                Location = new Point(38, 380), Size = new Size(650, 62),
                Font = CreateOwnedFont("Segoe UI", 8.25f)
            };
            generalPage.Controls.Add(pauseOnFullscreenCheckBox);
            generalPage.Controls.Add(fullscreenHint);

            checkForUpdatesButton.Click +=
                CheckForUpdatesButton_Click;

            rejectPage.Controls.Add(
                rejectTitleLabel);

            rejectPage.Controls.Add(
                rejectFolderLabel);

            rejectPage.Controls.Add(
                rejectRootTextBox);

            rejectPage.Controls.Add(
                rejectRootBrowseButton);

            rejectPage.Controls.Add(
                rejectSubfolderCheckBox);

            rejectPage.Controls.Add(
                rejectHintLabel);

            generalPage.Controls.Add(
                generalTitleLabel);

            generalPage.Controls.Add(
                autostartCheckBox);

            generalPage.Controls.Add(
                closeToTrayCheckBox);

            generalPage.Controls.Add(
                updatesTitleLabel);

            generalPage.Controls.Add(
                automaticUpdateCheckCheckBox);

            generalPage.Controls.Add(
                updateCheckHintLabel);

            generalPage.Controls.Add(
                checkForUpdatesButton);

            #endregion

            #region Appearance and language pages

            Label appearanceTitleLabel =
                new Label
                {
                    Text =
                        Localization.Get(
                            "SettingsAppearanceTitle",
                            previewLanguageCode),
                    Tag =
                        "SettingsAppearanceTitle",
                    Location =
                        new Point(18, 18),
                    AutoSize = true,
                    Font =
                        CreateOwnedFont(
                            "Segoe UI",
                            12,
                            FontStyle.Bold)
                };

            Label themeLabel =
                new Label
                {
                    Text =
                        Localization.Get(
                            "SettingsThemeLabel",
                            previewLanguageCode),
                    Tag =
                        "SettingsThemeLabel",
                    Location =
                        new Point(18, 62),
                    Size =
                        new Size(175, 25)
                };

            themeComboBox =
                new ComboBox
                {
                    Location =
                        new Point(205, 57),
                    Size =
                        new Size(288, 28),
                    DropDownStyle =
                        ComboBoxStyle.DropDownList
                };

            RefreshThemeChoices(
                previewThemeMode);

            themeComboBox.SelectedIndexChanged +=
                ThemeComboBox_SelectedIndexChanged;

            Label themeHintLabel =
                new Label
                {
                    Text =
                        Localization.Get(
                            "SettingsThemeHint",
                            previewLanguageCode),
                    Tag =
                        "SettingsThemeHint",
                    Location =
                        new Point(18, 98),
                    Size =
                        new Size(475, 42),
                    Font =
                        CreateOwnedFont(
                            "Segoe UI",
                            8.25f)
                };

            Label languageTitleLabel = new Label
            {
                Text = Localization.Get("SettingsNavLanguage", previewLanguageCode),
                Tag = "SettingsNavLanguage",
                Location = new Point(18, 18),
                AutoSize = true,
                Font = CreateOwnedFont("Segoe UI", 12, FontStyle.Bold)
            };
            languagePage.Controls.Add(languageTitleLabel);

            Label languageLabel =
                new Label
                {
                    Text =
                        Localization.Get(
                            "SettingsLanguageLabel",
                            previewLanguageCode),
                    Tag =
                        "SettingsLanguageLabel",
                    Location =
                        new Point(18, 62),
                    Size =
                        new Size(175, 25)
                };

            languageComboBox =
                new ComboBox
                {
                    Location =
                        new Point(205, 57),
                    Size =
                        new Size(288, 28),
                    DropDownStyle =
                        ComboBoxStyle.DropDownList
                };

            RefreshLanguageChoices(
                previewLanguageCode);

            SelectLanguage(
                previewLanguageCode);

            languageComboBox.SelectedIndexChanged +=
                LanguageComboBox_SelectedIndexChanged;

            Label opacityLabel =
                new Label
                {
                    Text =
                        Localization.Get(
                            "SettingsWindowOpacity",
                            previewLanguageCode),
                    Tag =
                        "SettingsWindowOpacity",
                    Location =
                        new Point(18, 217),
                    Size =
                        new Size(175, 25)
                };

            opacityTrackBar =
                new TrackBar
                {
                    Location =
                        new Point(205, 210),
                    Size =
                        new Size(235, 30),
                    Minimum = 92,
                    Maximum = 100,
                    SmallChange = 1,
                    LargeChange = 1,
                    TickFrequency = 1,
                    TickStyle =
                        TickStyle.None,
                    AutoSize = false,
                    Value =
                        Math.Clamp(
                            windowOpacityPercent,
                            92,
                            100)
                };

            opacityValueLabel =
                new Label
                {
                    Location =
                        new Point(448, 217),
                    Size =
                        new Size(45, 25),
                    TextAlign =
                        ContentAlignment.TopRight
                };

            UpdateOpacityPreview();

            opacityTrackBar.ValueChanged +=
                (_, _) =>
                    UpdateOpacityPreview();

            resetAppearanceButton =
                new Button
                {
                    Text =
                        Localization.Get(
                            "SettingsResetAppearance",
                            previewLanguageCode),
                    Tag =
                        "SettingsResetAppearance",
                    Location =
                        new Point(293, 275),
                    Size =
                        new Size(200, 34)
                };

            resetAppearanceButton.Click +=
                (_, _) =>
                    ResetAppearanceSettings();

            appearancePage.Controls.Add(
                appearanceTitleLabel);

            appearancePage.Controls.Add(
                themeLabel);

            appearancePage.Controls.Add(
                themeComboBox);

            appearancePage.Controls.Add(
                themeHintLabel);

            languagePage.Controls.Add(
                languageLabel);

            languagePage.Controls.Add(
                languageComboBox);

            appearancePage.Controls.Add(
                opacityLabel);

            appearancePage.Controls.Add(
                opacityTrackBar);

            appearancePage.Controls.Add(
                opacityValueLabel);

            appearancePage.Controls.Add(
                resetAppearanceButton);

            #endregion

            #region Dialog actions and final subscriptions

            Button defaultsButton =
                new Button
                {
                    Text =
                        Localization.Get(
                            "SettingsRestoreDefaults",
                            previewLanguageCode),
                    Tag =
                        "SettingsRestoreDefaults",
                    Location =
                        new Point(20, 715),
                    Size =
                        new Size(210, 38)
                };

            defaultsButton.Click +=
                (_, _) =>
                    ResetAllSettings();

            Button cancelButton =
                new Button
                {
                    Text =
                        Localization.Get(
                            "SettingsCancel",
                            previewLanguageCode),
                    Tag =
                        "SettingsCancel",
                    Location =
                        new Point(880, 715),
                    Size =
                        new Size(100, 38),
                    DialogResult =
                        DialogResult.Cancel
                };

            Button saveButton =
                new Button
                {
                    Text =
                        Localization.Get(
                            "SettingsSave",
                            previewLanguageCode),
                    Tag =
                        "SettingsSave",
                    Location =
                        new Point(990, 715),
                    Size =
                        new Size(110, 38)
                };

            saveButton.Click +=
                (_, _) =>
                    SaveAndClose();

            Controls.Add(
                navigationPanel);

            Controls.Add(
                tabControl);

            Controls.Add(
                defaultsButton);

            Controls.Add(
                cancelButton);

            Controls.Add(
                saveButton);

            AcceptButton =
                saveButton;

            CancelButton =
                cancelButton;

            SystemEvents.UserPreferenceChanged +=
                SystemEvents_UserPreferenceChanged;

            ApplyTheme(
                ResolvePreviewDarkMode());

            UpdateHotkeyValidation();

            #endregion
        }
    }
}
