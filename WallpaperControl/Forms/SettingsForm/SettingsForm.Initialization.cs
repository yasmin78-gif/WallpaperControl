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
        /// <param name="widgetSettings">The widget preferences used to initialize the dialog.</param>
        /// <param name="widgetPreviewChanged">The optional callback that applies uncommitted widget preferences.</param>
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
            int windowOpacityPercent,
            WidgetSettings widgetSettings,
            Action<WidgetSettings>? widgetPreviewChanged = null)
        {
            #region Window and preview state

            initialWidgetSettings = widgetSettings.Clone();
            this.widgetPreviewChanged = widgetPreviewChanged;
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
            TabPage clockPage = CreateSettingsPage("SettingsNavClock");
            TabPage nextWidgetPage = CreateSettingsPage("SettingsNavNextWallpaper");
            TabPage systemWidgetPage = CreateSettingsPage("SettingsNavSystem");
            TabPage weatherWidgetPage = CreateSettingsPage("SettingsNavWeather");
            TabPage calendarWidgetPage = CreateSettingsPage("SettingsNavCalendar");
            TabPage appearancePage = CreateSettingsPage("SettingsNavAppearance");
            TabPage languagePage = CreateSettingsPage("SettingsNavLanguage");

            tabControl.TabPages.Add(hotkeysPage);
            tabControl.TabPages.Add(generalPage);
            tabControl.TabPages.Add(rejectPage);
            tabControl.TabPages.Add(clockPage);
            tabControl.TabPages.Add(nextWidgetPage);
            tabControl.TabPages.Add(systemWidgetPage);
            tabControl.TabPages.Add(weatherWidgetPage);
            tabControl.TabPages.Add(calendarWidgetPage);
            tabControl.TabPages.Add(appearancePage);
            tabControl.TabPages.Add(languagePage);

            AddSettingsNavigationButton(navigationPanel, tabControl, generalPage, "⚙", "SettingsNavGeneral", 72);
            AddSettingsNavigationButton(navigationPanel, tabControl, rejectPage, "▣", "SettingsNavReject", 118);
            AddSettingsNavigationButton(navigationPanel, tabControl, hotkeysPage, "⌨", "SettingsTabHotkeys", 164);

            settingsWidgetsToggleButton = new Button
            {
                Tag = "SettingsTabWidgets",
                Location = new Point(14, 210),
                Size = new Size(192, 34),
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            settingsWidgetsToggleButton.FlatAppearance.BorderSize = 0;
            settingsWidgetsToggleButton.Click += (_, _) =>
            {
                settingsWidgetsExpanded = !settingsWidgetsExpanded;
                UpdateWidgetsNavigationLayout();
            };
            navigationPanel.Controls.Add(settingsWidgetsToggleButton);

            settingsClockNavigationButton = AddSettingsNavigationButton(navigationPanel, tabControl, clockPage, "◷", "SettingsNavClock", 246, 14);
            settingsNextNavigationButton = AddSettingsNavigationButton(navigationPanel, tabControl, nextWidgetPage, "▷", "SettingsNavNextWallpaper", 292, 14);
            settingsSystemNavigationButton = AddSettingsNavigationButton(navigationPanel, tabControl, systemWidgetPage, "▥", "SettingsNavSystem", 338, 14);
            settingsWeatherNavigationButton = AddSettingsNavigationButton(navigationPanel, tabControl, weatherWidgetPage, "☀", "SettingsNavWeather", 384, 14);
            settingsCalendarNavigationButton = AddSettingsNavigationButton(navigationPanel, tabControl, calendarWidgetPage, "▣", "SettingsNavCalendar", 430, 14);
            settingsAppearanceNavigationButton = AddSettingsNavigationButton(navigationPanel, tabControl, appearancePage, "◐", "SettingsNavAppearance", 490);
            settingsLanguageNavigationButton = AddSettingsNavigationButton(navigationPanel, tabControl, languagePage, "◎", "SettingsNavLanguage", 536);
            UpdateWidgetsNavigationLayout();

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

            #region Clock widget page

            Label widgetsTitle = new Label
            {
                Text = Localization.Get("SettingsWidgetsTitle", previewLanguageCode),
                Tag = "SettingsWidgetsTitle",
                Location = new Point(18, 14),
                AutoSize = true,
                Font = CreateOwnedFont("Segoe UI", 12, FontStyle.Bold)
            };

            clockSettingsPreview = new ClockSettingsPreview
            {
                Location = new Point(18, 46),
                Size = new Size(810, 180),
                Style = initialWidgetSettings.ClockStyle,
                ShowSeconds = initialWidgetSettings.ClockShowSeconds,
                LanguageCode = previewLanguageCode
            };

            Label clockStyleLabel = new Label
            {
                Text = Localization.Get("SettingsClockStyle", previewLanguageCode),
                Tag = "SettingsClockStyle",
                Location = new Point(18, 238),
                Size = new Size(220, 24),
                Font = CreateOwnedFont("Segoe UI", 9.5f, FontStyle.Bold)
            };

            clockStyleComboBox = new ComboBox
            {
                Visible = false,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            RefreshClockStyleChoices(initialWidgetSettings.ClockStyle);

            string[] styleKeys = { "ClockStyleMinimal", "ClockStyleChrome", "ClockStyleClean", "ClockStyleGlow", "ClockStyleClassic" };
            for (int i = 0; i < styleKeys.Length; i++)
            {
                ClockWidgetStyle cardStyle = (ClockWidgetStyle)i;
                ClockStyleCard card = new ClockStyleCard
                {
                    Location = new Point(18 + i * 160, 266),
                    Size = new Size(148, 92),
                    Style = cardStyle,
                    Caption = Localization.Get(styleKeys[i], previewLanguageCode),
                    Selected = cardStyle == initialWidgetSettings.ClockStyle
                };
                card.Click += (_, _) => SelectClockStyle(cardStyle);
                clockStyleCards.Add(card);
                clockPage.Controls.Add(card);
            }

            GroupBox clockOptions = new GroupBox
            {
                Text = Localization.Get("SettingsClockEnabled", previewLanguageCode),
                Tag = "SettingsClockEnabled",
                Location = new Point(18, 370),
                Size = new Size(500, 190)
            };

            clockEnabledCheckBox = new CheckBox
            {
                Text = Localization.Get("SettingsClockEnabled", previewLanguageCode),
                Tag = "SettingsClockEnabled",
                Location = new Point(18, 28),
                AutoSize = true,
                Checked = initialWidgetSettings.ClockEnabled
            };

            clockSecondsCheckBox = new CheckBox
            {
                Text = Localization.Get("SettingsClockShowSeconds", previewLanguageCode),
                Tag = "SettingsClockShowSeconds",
                Location = new Point(250, 28),
                AutoSize = true,
                Checked = initialWidgetSettings.ClockShowSeconds
            };

            Label clockSizeLabel = new Label
            {
                Text = Localization.Get("SettingsClockSize", previewLanguageCode),
                Tag = "SettingsClockSize",
                Location = new Point(18, 68),
                Size = new Size(210, 25)
            };

            clockSizeNumeric = new NumericUpDown
            {
                Location = new Point(250, 64),
                Size = new Size(90, 28),
                Minimum = 70,
                Maximum = 240,
                Increment = 5,
                Value = Math.Clamp(initialWidgetSettings.ClockSize, 70, 240)
            };

            clockLockedCheckBox = new CheckBox
            {
                Text = Localization.Get("SettingsWidgetLocked", previewLanguageCode),
                Tag = "SettingsWidgetLocked",
                Location = new Point(18, 108),
                AutoSize = true,
                Checked = initialWidgetSettings.ClockLocked
            };

            clockOptions.Controls.Add(clockEnabledCheckBox);
            clockOptions.Controls.Add(clockSecondsCheckBox);
            clockOptions.Controls.Add(clockSizeLabel);
            clockOptions.Controls.Add(clockSizeNumeric);
            clockOptions.Controls.Add(clockLockedCheckBox);

            #endregion

            #region Next wallpaper widget page

            Label nextWidgetPageTitle = new Label
            {
                Text = Localization.Get("SettingsNavNextWallpaper", previewLanguageCode),
                Tag = "SettingsNavNextWallpaper",
                Location = new Point(18, 18),
                AutoSize = true,
                Font = CreateOwnedFont("Segoe UI", 12, FontStyle.Bold)
            };
            nextWidgetPage.Controls.Add(nextWidgetPageTitle);

            GroupBox nextOptions = new GroupBox
            {
                Text = Localization.Get("SettingsNextWidgetTitle", previewLanguageCode),
                Tag = "SettingsNextWidgetTitle",
                Location = new Point(18, 58),
                Size = new Size(500, 150)
            };

            nextWidgetEnabledCheckBox = new CheckBox
            {
                Text = Localization.Get("SettingsNextWidgetEnabled", previewLanguageCode),
                Tag = "SettingsNextWidgetEnabled",
                Location = new Point(18, 32),
                AutoSize = true,
                Checked = initialWidgetSettings.NextEnabled
            };

            nextWidgetLockedCheckBox = new CheckBox
            {
                Text = Localization.Get("SettingsWidgetLocked", previewLanguageCode),
                Tag = "SettingsWidgetLocked",
                Location = new Point(18, 72),
                AutoSize = true,
                Checked = initialWidgetSettings.NextLocked
            };

            nextOptions.Controls.Add(nextWidgetEnabledCheckBox);
            nextOptions.Controls.Add(nextWidgetLockedCheckBox);

            Label nextStyleLabel = new Label
            {
                Text = Localization.Get("SettingsSystemStyle", previewLanguageCode),
                Tag = "SettingsSystemStyle",
                Location = new Point(18, 114),
                AutoSize = true
            };
            nextWidgetStyleComboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(180, 110),
                Size = new Size(220, 28)
            };
            RefreshNextStyleChoices(initialWidgetSettings.NextStyle);
            nextOptions.Controls.Add(nextStyleLabel);
            nextOptions.Controls.Add(nextWidgetStyleComboBox);

            #endregion

            #region System widget page

            Label systemWidgetPageTitle = new Label
            {
                Text = Localization.Get("SettingsNavSystem", previewLanguageCode),
                Tag = "SettingsNavSystem",
                Location = new Point(18, 18),
                AutoSize = true,
                Font = CreateOwnedFont("Segoe UI", 12, FontStyle.Bold)
            };
            systemWidgetPage.Controls.Add(systemWidgetPageTitle);

            GroupBox systemOptions = new GroupBox
            {
                Text = Localization.Get("SettingsSystemWidgetTitle", previewLanguageCode),
                Tag = "SettingsSystemWidgetTitle",
                Location = new Point(18, 58),
                Size = new Size(620, 430)
            };

            systemWidgetEnabledCheckBox = new CheckBox
            {
                Text = Localization.Get("SettingsSystemWidgetEnabled", previewLanguageCode),
                Tag = "SettingsSystemWidgetEnabled",
                Location = new Point(18, 32),
                AutoSize = true,
                Checked = initialWidgetSettings.SystemEnabled
            };

            systemWidgetLockedCheckBox = new CheckBox
            {
                Text = Localization.Get("SettingsWidgetLocked", previewLanguageCode),
                Tag = "SettingsWidgetLocked",
                Location = new Point(18, 68),
                AutoSize = true,
                Checked = initialWidgetSettings.SystemLocked
            };

            Label systemStyleLabel = new Label
            {
                Text = Localization.Get("SettingsSystemStyle", previewLanguageCode),
                Tag = "SettingsSystemStyle",
                Location = new Point(18, 110),
                AutoSize = true
            };

            systemWidgetStyleComboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(190, 106),
                Size = new Size(170, 30)
            };
            systemWidgetStyleComboBox.Items.Add(Localization.Get("ClockStyleMinimal", previewLanguageCode));
            systemWidgetStyleComboBox.Items.Add(Localization.Get("ClockStyleClean", previewLanguageCode));
            systemWidgetStyleComboBox.Items.Add(Localization.Get("ClockStyleGlow", previewLanguageCode));
            systemWidgetStyleComboBox.SelectedIndex = Math.Clamp((int)initialWidgetSettings.SystemStyle, 0, 2);

            Label systemRefreshLabel = new Label
            {
                Text = Localization.Get("SettingsSystemRefresh", previewLanguageCode),
                Tag = "SettingsSystemRefresh",
                Location = new Point(18, 150),
                AutoSize = true
            };

            systemWidgetRefreshComboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(190, 146),
                Size = new Size(125, 30)
            };
            systemWidgetRefreshComboBox.Items.AddRange(new object[] { "1 s", "2 s", "5 s" });
            systemWidgetRefreshComboBox.SelectedIndex = initialWidgetSettings.SystemRefreshSeconds switch { 1 => 0, 5 => 2, _ => 1 };

            Label modulesLabel = new Label
            {
                Text = Localization.Get("SettingsSystemModules", previewLanguageCode),
                Tag = "SettingsSystemModules",
                Location = new Point(18, 194),
                AutoSize = true,
                Font = CreateOwnedFont("Segoe UI", 9f, FontStyle.Bold)
            };

            systemShowCpuCheckBox = CreateSystemModuleCheckBox("SettingsSystemCpu", 18, 225, initialWidgetSettings.SystemShowCpu);
            systemShowRamCheckBox = CreateSystemModuleCheckBox("SettingsSystemRam", 18, 258, initialWidgetSettings.SystemShowRam);
            systemShowGpuCheckBox = CreateSystemModuleCheckBox("SettingsSystemGpu", 18, 291, initialWidgetSettings.SystemShowGpu);
            systemShowVramCheckBox = CreateSystemModuleCheckBox("SettingsSystemVram", 300, 225, initialWidgetSettings.SystemShowVram);
            systemShowNetworkCheckBox = CreateSystemModuleCheckBox("SettingsSystemNetwork", 300, 258, initialWidgetSettings.SystemShowNetwork);
            systemShowDrivesCheckBox = CreateSystemModuleCheckBox("SettingsSystemDrives", 300, 291, initialWidgetSettings.SystemShowDrives);

            Label systemHint = new Label
            {
                Text = Localization.Get("SettingsSystemWidgetHint", previewLanguageCode),
                Tag = "SettingsSystemWidgetHint",
                Location = new Point(18, 345),
                Size = new Size(570, 55)
            };

            systemOptions.Controls.Add(systemWidgetEnabledCheckBox);
            systemOptions.Controls.Add(systemWidgetLockedCheckBox);
            systemOptions.Controls.Add(systemStyleLabel);
            systemOptions.Controls.Add(systemWidgetStyleComboBox);
            systemOptions.Controls.Add(systemRefreshLabel);
            systemOptions.Controls.Add(systemWidgetRefreshComboBox);
            systemOptions.Controls.Add(modulesLabel);
            systemOptions.Controls.Add(systemShowCpuCheckBox);
            systemOptions.Controls.Add(systemShowRamCheckBox);
            systemOptions.Controls.Add(systemShowGpuCheckBox);
            systemOptions.Controls.Add(systemShowVramCheckBox);
            systemOptions.Controls.Add(systemShowNetworkCheckBox);
            systemOptions.Controls.Add(systemShowDrivesCheckBox);
            systemOptions.Controls.Add(systemHint);
            systemWidgetPage.Controls.Add(systemOptions);

            #endregion

            #region Weather widget page

            Label weatherWidgetPageTitle = new Label
            {
                Text = Localization.Get("SettingsNavWeather", previewLanguageCode),
                Tag = "SettingsNavWeather",
                Location = new Point(18, 18),
                AutoSize = true,
                Font = CreateOwnedFont("Segoe UI", 12, FontStyle.Bold)
            };
            weatherWidgetPage.Controls.Add(weatherWidgetPageTitle);

            GroupBox weatherOptions = new GroupBox
            {
                Text = Localization.Get("SettingsWeatherWidgetTitle", previewLanguageCode),
                Tag = "SettingsWeatherWidgetTitle",
                Location = new Point(18, 58),
                Size = new Size(620, 330)
            };

            weatherWidgetEnabledCheckBox = new CheckBox
            {
                Text = Localization.Get("SettingsWeatherWidgetEnabled", previewLanguageCode),
                Tag = "SettingsWeatherWidgetEnabled",
                Location = new Point(18, 32),
                AutoSize = true,
                Checked = initialWidgetSettings.WeatherEnabled
            };

            weatherWidgetLockedCheckBox = new CheckBox
            {
                Text = Localization.Get("SettingsWidgetLocked", previewLanguageCode),
                Tag = "SettingsWidgetLocked",
                Location = new Point(18, 68),
                AutoSize = true,
                Checked = initialWidgetSettings.WeatherLocked
            };

            Label weatherLocationLabel = new Label
            {
                Text = Localization.Get("SettingsWeatherLocation", previewLanguageCode),
                Tag = "SettingsWeatherLocation",
                Location = new Point(18, 110),
                AutoSize = true
            };

            weatherLocationTextBox = new TextBox
            {
                Location = new Point(190, 106),
                Size = new Size(205, 28),
                Text = initialWidgetSettings.WeatherLocationName
            };

            Button weatherApplyLocationButton = new Button
            {
                Text = Localization.Get("SettingsWeatherApplyLocation", previewLanguageCode),
                Tag = "SettingsWeatherApplyLocation",
                Location = new Point(405, 104),
                Size = new Size(110, 31),
                Cursor = Cursors.Hand
            };

            Label weatherStyleLabel = new Label
            {
                Text = Localization.Get("SettingsSystemStyle", previewLanguageCode),
                Tag = "SettingsSystemStyle",
                Location = new Point(18, 150),
                AutoSize = true
            };

            weatherWidgetStyleComboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(190, 146),
                Size = new Size(170, 30)
            };
            weatherWidgetStyleComboBox.Items.Add(Localization.Get("ClockStyleMinimal", previewLanguageCode));
            weatherWidgetStyleComboBox.Items.Add(Localization.Get("ClockStyleClean", previewLanguageCode));
            weatherWidgetStyleComboBox.Items.Add(Localization.Get("ClockStyleGlow", previewLanguageCode));
            weatherWidgetStyleComboBox.SelectedIndex = Math.Clamp((int)initialWidgetSettings.WeatherStyle, 0, 2);

            Label weatherRefreshLabel = new Label
            {
                Text = Localization.Get("SettingsWeatherRefresh", previewLanguageCode),
                Tag = "SettingsWeatherRefresh",
                Location = new Point(18, 190),
                AutoSize = true
            };

            weatherWidgetRefreshComboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(190, 186),
                Size = new Size(125, 30)
            };
            weatherWidgetRefreshComboBox.Items.AddRange(new object[] { "15 min", "30 min", "60 min", "120 min" });
            weatherWidgetRefreshComboBox.SelectedIndex = initialWidgetSettings.WeatherRefreshMinutes switch { 15 => 0, 60 => 2, 120 => 3, _ => 1 };

            weatherShowForecastCheckBox = new CheckBox
            {
                Text = Localization.Get("SettingsWeatherForecast", previewLanguageCode),
                Tag = "SettingsWeatherForecast",
                Location = new Point(18, 232),
                AutoSize = true,
                Checked = initialWidgetSettings.WeatherShowForecast
            };

            Label weatherHint = new Label
            {
                Text = Localization.Get("SettingsWeatherHint", previewLanguageCode),
                Tag = "SettingsWeatherHint",
                Location = new Point(18, 270),
                Size = new Size(570, 45)
            };

            weatherOptions.Controls.Add(weatherWidgetEnabledCheckBox);
            weatherOptions.Controls.Add(weatherWidgetLockedCheckBox);
            weatherOptions.Controls.Add(weatherLocationLabel);
            weatherOptions.Controls.Add(weatherLocationTextBox);
            weatherOptions.Controls.Add(weatherApplyLocationButton);
            weatherOptions.Controls.Add(weatherStyleLabel);
            weatherOptions.Controls.Add(weatherWidgetStyleComboBox);
            weatherOptions.Controls.Add(weatherRefreshLabel);
            weatherOptions.Controls.Add(weatherWidgetRefreshComboBox);
            weatherOptions.Controls.Add(weatherShowForecastCheckBox);
            weatherOptions.Controls.Add(weatherHint);
            weatherWidgetPage.Controls.Add(weatherOptions);

            #endregion

            #region Calendar widget page

            Label calendarWidgetPageTitle = new Label
            {
                Text = Localization.Get("SettingsNavCalendar", previewLanguageCode),
                Tag = "SettingsNavCalendar",
                Location = new Point(18, 18),
                AutoSize = true,
                Font = CreateOwnedFont("Segoe UI", 12, FontStyle.Bold)
            };
            calendarWidgetPage.Controls.Add(calendarWidgetPageTitle);

            GroupBox calendarOptions = new GroupBox
            {
                Text = Localization.Get("SettingsCalendarWidgetTitle", previewLanguageCode),
                Tag = "SettingsCalendarWidgetTitle",
                Location = new Point(18, 58),
                Size = new Size(620, 535)
            };

            calendarWidgetEnabledCheckBox = new CheckBox
            {
                Text = Localization.Get("SettingsCalendarWidgetEnabled", previewLanguageCode),
                Tag = "SettingsCalendarWidgetEnabled",
                Location = new Point(18, 32),
                AutoSize = true,
                Checked = initialWidgetSettings.CalendarEnabled
            };

            calendarWidgetLockedCheckBox = new CheckBox
            {
                Text = Localization.Get("SettingsWidgetLocked", previewLanguageCode),
                Tag = "SettingsWidgetLocked",
                Location = new Point(18, 68),
                AutoSize = true,
                Checked = initialWidgetSettings.CalendarLocked
            };

            Label calendarSourceLabel = new Label
            {
                Text = Localization.Get("SettingsCalendarSource", previewLanguageCode),
                Tag = "SettingsCalendarSource",
                Location = new Point(18, 108),
                AutoSize = true
            };

            calendarIcsUrlTextBox = new TextBox
            {
                Location = new Point(18, 132),
                Size = new Size(390, 44),
                Text = initialWidgetSettings.CalendarIcsUrl,
                Multiline = true,
                AcceptsReturn = true,
                ScrollBars = ScrollBars.Vertical,
                UseSystemPasswordChar = true
            };

            Label calendarHolidaySourceLabel = new Label
            {
                Text = Localization.Get("SettingsCalendarHolidaySource", previewLanguageCode),
                Tag = "SettingsCalendarHolidaySource",
                Location = new Point(18, 184),
                AutoSize = true
            };

            calendarHolidayIcsUrlTextBox = new TextBox
            {
                Location = new Point(18, 208),
                Size = new Size(390, 44),
                Text = initialWidgetSettings.CalendarHolidayIcsUrl,
                Multiline = true,
                AcceptsReturn = true,
                ScrollBars = ScrollBars.Vertical,
                UseSystemPasswordChar = true
            };

            Button calendarShowSourceButton = new Button
            {
                Text = Localization.Get("SettingsCalendarShowSource", previewLanguageCode),
                Tag = "SettingsCalendarShowSource",
                Location = new Point(414, 130),
                Size = new Size(86, 31)
            };

            Button calendarApplySourceButton = new Button
            {
                Text = Localization.Get("SettingsCalendarApplySource", previewLanguageCode),
                Tag = "SettingsCalendarApplySource",
                Location = new Point(506, 130),
                Size = new Size(96, 31)
            };

            calendarShowSourceButton.Click += (_, _) =>
            {
                calendarIcsUrlTextBox.UseSystemPasswordChar = !calendarIcsUrlTextBox.UseSystemPasswordChar;
                calendarHolidayIcsUrlTextBox.UseSystemPasswordChar = calendarIcsUrlTextBox.UseSystemPasswordChar;
                string key = calendarIcsUrlTextBox.UseSystemPasswordChar ? "SettingsCalendarShowSource" : "SettingsCalendarHideSource";
                calendarShowSourceButton.Tag = key;
                calendarShowSourceButton.Text = Localization.Get(key, previewLanguageCode);
            };
            calendarApplySourceButton.Click += (_, _) => NotifyWidgetPreviewChanged();
            calendarIcsUrlTextBox.KeyDown += (_, e) =>
            {
                if (e.Control && e.KeyCode == Keys.Enter)
                {
                    calendarApplySourceButton.PerformClick();
                    e.SuppressKeyPress = true;
                }
            };
            calendarHolidayIcsUrlTextBox.KeyDown += (_, e) =>
            {
                if (e.Control && e.KeyCode == Keys.Enter)
                {
                    calendarApplySourceButton.PerformClick();
                    e.SuppressKeyPress = true;
                }
            };

            Label calendarStyleLabel = new Label
            {
                Text = Localization.Get("SettingsSystemStyle", previewLanguageCode),
                Tag = "SettingsSystemStyle",
                Location = new Point(18, 284),
                AutoSize = true
            };

            calendarWidgetStyleComboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(190, 280),
                Size = new Size(170, 30)
            };
            calendarWidgetStyleComboBox.Items.Add(Localization.Get("ClockStyleMinimal", previewLanguageCode));
            calendarWidgetStyleComboBox.Items.Add(Localization.Get("ClockStyleClean", previewLanguageCode));
            calendarWidgetStyleComboBox.Items.Add(Localization.Get("ClockStyleGlow", previewLanguageCode));
            calendarWidgetStyleComboBox.SelectedIndex = Math.Clamp((int)initialWidgetSettings.CalendarStyle, 0, 2);

            Label calendarEntriesLabel = new Label
            {
                Text = Localization.Get("SettingsCalendarEntries", previewLanguageCode),
                Tag = "SettingsCalendarEntries",
                Location = new Point(18, 324),
                AutoSize = true
            };

            calendarMaxEntriesComboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(190, 320),
                Size = new Size(125, 30)
            };
            calendarMaxEntriesComboBox.Items.AddRange(new object[] { "3", "5", "9" });
            calendarMaxEntriesComboBox.SelectedIndex = initialWidgetSettings.CalendarMaxEntries switch { 3 => 0, 5 => 1, _ => 2 };

            Label calendarRefreshLabel = new Label
            {
                Text = Localization.Get("SettingsCalendarRefresh", previewLanguageCode),
                Tag = "SettingsCalendarRefresh",
                Location = new Point(18, 364),
                AutoSize = true
            };

            calendarRefreshComboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(190, 360),
                Size = new Size(125, 30)
            };
            calendarRefreshComboBox.Items.AddRange(new object[] { "15 min", "30 min", "60 min", "120 min" });
            calendarRefreshComboBox.SelectedIndex = initialWidgetSettings.CalendarRefreshMinutes switch { 15 => 0, 60 => 2, 120 => 3, _ => 1 };

            calendarShowLocationCheckBox = new CheckBox
            {
                Text = Localization.Get("SettingsCalendarShowLocation", previewLanguageCode),
                Tag = "SettingsCalendarShowLocation",
                Location = new Point(18, 402),
                AutoSize = true,
                Checked = initialWidgetSettings.CalendarShowLocation
            };

            Label calendarHint = new Label
            {
                Text = Localization.Get("SettingsCalendarHint", previewLanguageCode),
                Tag = "SettingsCalendarHint",
                Location = new Point(18, 438),
                Size = new Size(575, 58)
            };

            calendarOptions.Controls.Add(calendarWidgetEnabledCheckBox);
            calendarOptions.Controls.Add(calendarWidgetLockedCheckBox);
            calendarOptions.Controls.Add(calendarSourceLabel);
            calendarOptions.Controls.Add(calendarIcsUrlTextBox);
            calendarOptions.Controls.Add(calendarHolidaySourceLabel);
            calendarOptions.Controls.Add(calendarHolidayIcsUrlTextBox);
            calendarOptions.Controls.Add(calendarShowSourceButton);
            calendarOptions.Controls.Add(calendarApplySourceButton);
            calendarOptions.Controls.Add(calendarStyleLabel);
            calendarOptions.Controls.Add(calendarWidgetStyleComboBox);
            calendarOptions.Controls.Add(calendarEntriesLabel);
            calendarOptions.Controls.Add(calendarMaxEntriesComboBox);
            calendarOptions.Controls.Add(calendarRefreshLabel);
            calendarOptions.Controls.Add(calendarRefreshComboBox);
            calendarOptions.Controls.Add(calendarShowLocationCheckBox);
            calendarOptions.Controls.Add(calendarHint);
            calendarWidgetPage.Controls.Add(calendarOptions);

            Label widgetHint = new Label
            {
                Text = Localization.Get("SettingsWidgetsHint", previewLanguageCode),
                Tag = "SettingsWidgetsHint",
                Location = new Point(18, 535),
                Size = new Size(810, 45),
                Font = CreateOwnedFont("Segoe UI", 8.25f)
            };

            clockPage.Controls.Add(widgetsTitle);
            clockPage.Controls.Add(clockSettingsPreview);
            clockPage.Controls.Add(clockStyleLabel);
            clockPage.Controls.Add(clockStyleComboBox);
            clockPage.Controls.Add(clockOptions);
            nextWidgetPage.Controls.Add(nextOptions);
            clockPage.Controls.Add(widgetHint);

            #endregion

            #region Live widget preview subscriptions

            clockEnabledCheckBox.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            clockLockedCheckBox.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            clockSizeNumeric.ValueChanged += (_, _) => NotifyWidgetPreviewChanged();
            clockSecondsCheckBox.CheckedChanged += (_, _) =>
            {
                clockSettingsPreview.ShowSeconds = clockSecondsCheckBox.Checked;
                clockSettingsPreview.Invalidate();
                foreach (ClockStyleCard card in clockStyleCards) { card.ShowSeconds = clockSecondsCheckBox.Checked; card.Invalidate(); }
                NotifyWidgetPreviewChanged();
            };
            clockStyleComboBox.SelectedIndexChanged += (_, _) =>
            {
                UpdateClockStyleSelection();
                NotifyWidgetPreviewChanged();
            };
            nextWidgetEnabledCheckBox.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            nextWidgetLockedCheckBox.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            nextWidgetStyleComboBox.SelectedIndexChanged += (_, _) => NotifyWidgetPreviewChanged();
            systemWidgetEnabledCheckBox.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            systemWidgetLockedCheckBox.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            systemWidgetRefreshComboBox.SelectedIndexChanged += (_, _) => NotifyWidgetPreviewChanged();
            systemWidgetStyleComboBox.SelectedIndexChanged += (_, _) => NotifyWidgetPreviewChanged();
            systemShowCpuCheckBox.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            systemShowRamCheckBox.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            systemShowGpuCheckBox.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            systemShowVramCheckBox.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            systemShowNetworkCheckBox.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            systemShowDrivesCheckBox.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            weatherWidgetEnabledCheckBox.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            weatherWidgetLockedCheckBox.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            weatherWidgetRefreshComboBox.SelectedIndexChanged += (_, _) => NotifyWidgetPreviewChanged();
            weatherWidgetStyleComboBox.SelectedIndexChanged += (_, _) => NotifyWidgetPreviewChanged();
            weatherShowForecastCheckBox.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            weatherApplyLocationButton.Click += (_, _) => NotifyWidgetPreviewChanged();
            weatherLocationTextBox.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    weatherApplyLocationButton.PerformClick();
                    e.SuppressKeyPress = true;
                }
            };
            calendarWidgetEnabledCheckBox.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            calendarWidgetLockedCheckBox.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            calendarWidgetStyleComboBox.SelectedIndexChanged += (_, _) => NotifyWidgetPreviewChanged();
            calendarMaxEntriesComboBox.SelectedIndexChanged += (_, _) => NotifyWidgetPreviewChanged();
            calendarRefreshComboBox.SelectedIndexChanged += (_, _) => NotifyWidgetPreviewChanged();
            calendarShowLocationCheckBox.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();

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

            // ShowDialog can temporarily disable top-level windows that already
            // existed before the modal settings dialog was opened. Trigger the
            // widget preview after the dialog is visible so WidgetManager can
            // explicitly restore widget interaction for positioning.
            Shown += (_, _) => NotifyWidgetPreviewChanged();

            #endregion
        }
    }
}
