using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WallpaperControl
{
    internal sealed class SettingsForm : Form
    {
        private readonly List<Font> ownedFonts = new();

        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;

        private readonly ComboBox nextModifierCombo;
        private readonly ComboBox nextKeyCombo;
        private readonly ComboBox pauseModifierCombo;
        private readonly ComboBox pauseKeyCombo;
        private readonly ComboBox explorerModifierCombo;
        private readonly ComboBox explorerKeyCombo;
        private readonly ComboBox rejectModifierCombo;
        private readonly ComboBox rejectKeyCombo;
        private readonly TextBox rejectRootTextBox;
        private readonly Button rejectRootBrowseButton;
        private readonly CheckBox rejectSubfolderCheckBox;
        private readonly CheckBox autostartCheckBox;
        private readonly CheckBox closeToTrayCheckBox;
        private readonly ComboBox languageComboBox;
        private readonly ComboBox themeComboBox;
        private readonly TrackBar opacityTrackBar;
        private readonly Label opacityValueLabel;
        private readonly Label hotkeyWarningLabel;
        private readonly Button resetAppearanceButton;
        private readonly CheckBox clockEnabledCheckBox;
        private readonly CheckBox clockLockedCheckBox;
        private readonly NumericUpDown clockSizeNumeric;
        private readonly CheckBox clockSecondsCheckBox;
        private readonly ComboBox clockStyleComboBox;
        private readonly ClockSettingsPreview clockSettingsPreview;
        private readonly List<ClockStyleCard> clockStyleCards = new();
        private readonly List<Button> settingsNavigationButtons = new();
        private readonly Dictionary<Button, TabPage> settingsNavigationPages = new();
        private TabControl? settingsTabControl;
        private Panel? settingsNavigationPanel;
        private Button? settingsWidgetsToggleButton;
        private Button? settingsClockNavigationButton;
        private Button? settingsNextNavigationButton;
        private Button? settingsSystemNavigationButton;
        private Button? settingsAppearanceNavigationButton;
        private Button? settingsLanguageNavigationButton;
        private bool settingsWidgetsExpanded = true;
        private readonly CheckBox nextWidgetEnabledCheckBox;
        private readonly CheckBox nextWidgetLockedCheckBox;
        private readonly CheckBox systemWidgetEnabledCheckBox;
        private readonly CheckBox systemWidgetLockedCheckBox;
        private readonly ComboBox systemWidgetRefreshComboBox;
        private readonly ComboBox systemWidgetStyleComboBox;
        private readonly CheckBox systemShowCpuCheckBox;
        private readonly CheckBox systemShowRamCheckBox;
        private readonly CheckBox systemShowGpuCheckBox;
        private readonly CheckBox systemShowVramCheckBox;
        private readonly CheckBox systemShowNetworkCheckBox;
        private readonly CheckBox systemShowDrivesCheckBox;
        private readonly WidgetSettings initialWidgetSettings;
        private readonly Action<WidgetSettings>? widgetPreviewChanged;
        private string previewLanguageCode;
        private string previewThemeMode;
        private bool updatingLanguagePreview;
        private bool updatingThemePreview;

        public uint NextModifiers { get; private set; }
        public uint NextKey { get; private set; }
        public uint PauseModifiers { get; private set; }
        public uint PauseKey { get; private set; }
        public uint ExplorerModifiers { get; private set; }
        public uint ExplorerKey { get; private set; }
        public uint RejectModifiers { get; private set; }
        public uint RejectKey { get; private set; }
        public string RejectRootFolder { get; private set; } = "";
        public bool RejectUseSubfolder { get; private set; } = true;
        public bool AutostartEnabled { get; private set; }
        public bool CloseToTrayEnabled { get; private set; } = true;
        public int WindowOpacityPercent { get; private set; } = 80;
        public string ThemeMode { get; private set; } = "system";
        public WidgetSettings WidgetSettings { get; private set; } = new();

        private sealed class Choice
        {
            public string Text { get; }
            public uint Value { get; }

            public Choice(
                string text,
                uint value)
            {
                Text = text;
                Value = value;
            }

            public override string ToString() =>
                Text;
        }

        private sealed class LanguageChoice
        {
            public string Text { get; }
            public string Code { get; }

            public LanguageChoice(
                string text,
                string code)
            {
                Text = text;
                Code = code;
            }

            public override string ToString() =>
                Text;
        }

        private sealed class ThemeChoice
        {
            public string Text { get; }
            public string Mode { get; }

            public ThemeChoice(
                string text,
                string mode)
            {
                Text = text;
                Mode = mode;
            }

            public override string ToString() =>
                Text;
        }

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
            int windowOpacityPercent,
            WidgetSettings widgetSettings,
            Action<WidgetSettings>? widgetPreviewChanged = null)
        {
            initialWidgetSettings = widgetSettings.Clone();
            this.widgetPreviewChanged = widgetPreviewChanged;
            previewLanguageCode =
                Localization.CurrentLanguage;

            previewThemeMode =
                NormalizeThemeMode(
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
            TabPage appearancePage = CreateSettingsPage("SettingsNavAppearance");
            TabPage languagePage = CreateSettingsPage("SettingsNavLanguage");

            tabControl.TabPages.Add(hotkeysPage);
            tabControl.TabPages.Add(generalPage);
            tabControl.TabPages.Add(rejectPage);
            tabControl.TabPages.Add(clockPage);
            tabControl.TabPages.Add(nextWidgetPage);
            tabControl.TabPages.Add(systemWidgetPage);
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
            settingsAppearanceNavigationButton = AddSettingsNavigationButton(navigationPanel, tabControl, appearancePage, "◐", "SettingsNavAppearance", 398);
            settingsLanguageNavigationButton = AddSettingsNavigationButton(navigationPanel, tabControl, languagePage, "◎", "SettingsNavLanguage", 444);
            UpdateWidgetsNavigationLayout();

            tabControl.SelectedTab = hotkeysPage;
            UpdateSettingsNavigationSelection();
            tabControl.SelectedIndexChanged += (_, _) => UpdateSettingsNavigationSelection();

            // ==========================================================
            // HOTKEYS
            // ==========================================================
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

            // ==========================================================
            // VERHALTEN
            // ==========================================================
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

            // ==========================================================
            // DARSTELLUNG
            // ==========================================================
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

            // ==========================================================
            // WIDGETS
            // ==========================================================
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
                Size = new Size(500, 150)
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

            // ==========================================================
            // FOOTER
            // ==========================================================
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
        }

        private TabPage CreateSettingsPage(string resourceKey)
        {
            return new TabPage
            {
                Text = Localization.Get(resourceKey, previewLanguageCode),
                Tag = resourceKey
            };
        }

        private Button AddSettingsNavigationButton(
            Panel navigationPanel,
            TabControl tabControl,
            TabPage page,
            string icon,
            string resourceKey,
            int y,
            int extraLeftPadding = 0)
        {
            Button button = new Button
            {
                Text = $"{icon}   {Localization.Get(resourceKey, previewLanguageCode)}",
                Tag = resourceKey,
                Location = new Point(14 + extraLeftPadding, y),
                Size = new Size(192 - extraLeftPadding, 42),
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0),
                Cursor = Cursors.Hand,
                TabStop = false
            };

            button.FlatAppearance.BorderSize = 0;
            button.Click += (_, _) => tabControl.SelectedTab = page;
            button.AccessibleDescription = icon;
            settingsNavigationButtons.Add(button);
            settingsNavigationPages[button] = page;
            navigationPanel.Controls.Add(button);
            return button;
        }

        private void UpdateWidgetsNavigationLayout()
        {
            if (settingsWidgetsToggleButton == null)
                return;

            settingsWidgetsToggleButton.Text =
                $"{(settingsWidgetsExpanded ? "▾" : "▸")}   {Localization.Get("SettingsTabWidgets", previewLanguageCode)}";

            if (settingsClockNavigationButton != null)
                settingsClockNavigationButton.Visible = settingsWidgetsExpanded;

            if (settingsNextNavigationButton != null)
                settingsNextNavigationButton.Visible = settingsWidgetsExpanded;

            if (settingsSystemNavigationButton != null)
                settingsSystemNavigationButton.Visible = settingsWidgetsExpanded;

            int appearanceY = settingsWidgetsExpanded ? 398 : 252;
            int languageY = settingsWidgetsExpanded ? 444 : 298;

            if (settingsAppearanceNavigationButton != null)
                settingsAppearanceNavigationButton.Location = new Point(14, appearanceY);

            if (settingsLanguageNavigationButton != null)
                settingsLanguageNavigationButton.Location = new Point(14, languageY);

            UpdateSettingsNavigationSelection();
        }

        private void UpdateSettingsNavigationSelection()
        {
            if (settingsTabControl == null)
                return;

            bool darkMode = ResolvePreviewDarkMode();
            Color normal = AppTheme.SidebarBackground(darkMode);
            Color selected = AppTheme.SelectionBackground(darkMode);
            Color foreground = darkMode ? AppTheme.DarkTextPrimary : Color.FromArgb(35, 35, 35);

            if (settingsWidgetsToggleButton != null)
            {
                settingsWidgetsToggleButton.BackColor = normal;
                settingsWidgetsToggleButton.ForeColor = foreground;
                settingsWidgetsToggleButton.FlatAppearance.MouseOverBackColor =
                    darkMode ? AppTheme.DarkControlHover : Color.FromArgb(225, 232, 239);
                settingsWidgetsToggleButton.FlatAppearance.MouseDownBackColor = selected;
            }

            for (int i = 0; i < settingsNavigationButtons.Count; i++)
            {
                Button button = settingsNavigationButtons[i];
                button.BackColor = settingsNavigationPages.TryGetValue(button, out TabPage? page) && page == settingsTabControl.SelectedTab ? selected : normal;
                button.ForeColor = foreground;
            }
        }

        private void UpdateSettingsNavigationText()
        {
            foreach (Button button in settingsNavigationButtons)
            {
                if (button.Tag is not string resourceKey)
                    continue;

                string icon = button.AccessibleDescription ?? "";
                button.Text = $"{icon}   {Localization.Get(resourceKey, previewLanguageCode)}";
            }

            UpdateWidgetsNavigationLayout();
        }

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

        private void CreateHotkeyRow(
            Control parent,
            string labelResourceKey,
            int y,
            out ComboBox modifierCombo,
            out ComboBox keyCombo)
        {
            Label label = new Label
            {
                Text = Localization.Get(
                    labelResourceKey,
                    previewLanguageCode),
                Tag = labelResourceKey,
                Location = new Point(25, y + 5),
                Size = new Size(175, 25)
            };

            modifierCombo = new ComboBox
            {
                Location = new Point(205, y),
                Size = new Size(145, 28),
                DropDownStyle =
                    ComboBoxStyle.DropDownList
            };

            foreach (Choice choice in GetModifierChoices())
            {
                modifierCombo.Items.Add(choice);
            }

            keyCombo = new ComboBox
            {
                Location = new Point(360, y),
                Size = new Size(135, 28),
                DropDownStyle =
                    ComboBoxStyle.DropDownList
            };

            foreach (Choice choice in GetKeyChoices())
            {
                keyCombo.Items.Add(choice);
            }

            ComboBox modifierComboLocal =
                modifierCombo;

            ComboBox keyComboLocal =
                keyCombo;

            modifierComboLocal.SelectedIndexChanged +=
                (_, _) =>
                {
                    Choice? choice =
                        modifierComboLocal.SelectedItem
                        as Choice;

                    keyComboLocal.Enabled =
                        choice != null &&
                        choice.Value != 0;
                };

            parent.Controls.Add(label);
            parent.Controls.Add(modifierCombo);
            parent.Controls.Add(keyCombo);
        }

        private Choice[] GetModifierChoices() =>
            new[]
            {
                new Choice(
                    Localization.Get(
                        "SettingsModifierDisabled",
                        previewLanguageCode),
                    0),
                new Choice(
                    Localization.Get(
                        "SettingsModifierCtrlAlt",
                        previewLanguageCode),
                    MOD_CONTROL | MOD_ALT),
                new Choice(
                    Localization.Get(
                        "SettingsModifierCtrlShift",
                        previewLanguageCode),
                    MOD_CONTROL | MOD_SHIFT),
                new Choice(
                    Localization.Get(
                        "SettingsModifierAltShift",
                        previewLanguageCode),
                    MOD_ALT | MOD_SHIFT),
                new Choice(
                    Localization.Get(
                        "SettingsModifierCtrlAltShift",
                        previewLanguageCode),
                    MOD_CONTROL | MOD_ALT | MOD_SHIFT),
                new Choice(
                    Localization.Get(
                        "SettingsModifierWinCtrl",
                        previewLanguageCode),
                    MOD_WIN | MOD_CONTROL),
                new Choice(
                    Localization.Get(
                        "SettingsModifierWinAlt",
                        previewLanguageCode),
                    MOD_WIN | MOD_ALT)
            };

        private static Choice[] GetKeyChoices()
        {
            List<Choice> choices = new();

            for (char c = 'A'; c <= 'Z'; c++)
            {
                choices.Add(
                    new Choice(
                        c.ToString(),
                        c));
            }

            for (char c = '0'; c <= '9'; c++)
            {
                choices.Add(
                    new Choice(
                        c.ToString(),
                        c));
            }

            choices.Add(new Choice("←", 0x25));
            choices.Add(new Choice("↑", 0x26));
            choices.Add(new Choice("→", 0x27));
            choices.Add(new Choice("↓", 0x28));
            choices.Add(new Choice("F1", 0x70));
            choices.Add(new Choice("F2", 0x71));
            choices.Add(new Choice("F3", 0x72));
            choices.Add(new Choice("F4", 0x73));
            choices.Add(new Choice("F5", 0x74));
            choices.Add(new Choice("F6", 0x75));
            choices.Add(new Choice("F7", 0x76));
            choices.Add(new Choice("F8", 0x77));
            choices.Add(new Choice("F9", 0x78));
            choices.Add(new Choice("F10", 0x79));
            choices.Add(new Choice("F11", 0x7A));
            choices.Add(new Choice("F12", 0x7B));

            return choices.ToArray();
        }

        private static void SetComboValues(
            ComboBox modifierCombo,
            ComboBox keyCombo,
            uint modifiers,
            uint key)
        {
            SelectChoice(
                modifierCombo,
                modifiers);

            SelectChoice(
                keyCombo,
                key);

            if (modifierCombo.SelectedIndex < 0)
            {
                modifierCombo.SelectedIndex = 0;
            }

            if (keyCombo.SelectedIndex < 0)
            {
                keyCombo.SelectedIndex = 0;
            }

            Choice? modifierChoice =
                modifierCombo.SelectedItem
                as Choice;

            keyCombo.Enabled =
                modifierChoice != null &&
                modifierChoice.Value != 0;
        }

        private static void SelectChoice(
            ComboBox combo,
            uint value)
        {
            for (int i = 0;
                 i < combo.Items.Count;
                 i++)
            {
                if (combo.Items[i] is Choice choice &&
                    choice.Value == value)
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
        }

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

        private bool ResolvePreviewDarkMode()
        {
            return NormalizeThemeMode(
                previewThemeMode) switch
                {
                    "dark" => true,
                    "light" => false,
                    _ => IsWindowsDarkMode()
                };
        }

        private static bool IsWindowsDarkMode()
        {
            try
            {
                using RegistryKey? key =
                    Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

                object? value =
                    key?.GetValue(
                        "AppsUseLightTheme");

                return value != null &&
                       Convert.ToInt32(value) == 0;
            }
            catch
            {
                return false;
            }
        }

        private static string NormalizeThemeMode(
            string? value)
        {
            return value?.Trim().ToLowerInvariant() switch
            {
                "dark" => "dark",
                "light" => "light",
                _ => "system"
            };
        }

        private void RefreshThemeChoices(
            string selectedMode)
        {
            string normalized =
                NormalizeThemeMode(
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

        private void LanguageComboBox_SelectedIndexChanged(
            object? sender,
            EventArgs e)
        {
            if (updatingLanguagePreview ||
                languageComboBox.SelectedItem
                    is not LanguageChoice choice)
            {
                return;
            }

            ApplyPreviewLocalization(
                choice.Code);
        }

        private void ApplyPreviewLocalization(
            string languageCode)
        {
            uint nextModifiers;
            uint nextKey;
            uint pauseModifiers;
            uint pauseKey;
            uint explorerModifiers;
            uint explorerKey;
            uint rejectModifiers;
            uint rejectKey;

            GetComboValues(
                nextModifierCombo,
                nextKeyCombo,
                out nextModifiers,
                out nextKey);

            GetComboValues(
                pauseModifierCombo,
                pauseKeyCombo,
                out pauseModifiers,
                out pauseKey);

            GetComboValues(
                explorerModifierCombo,
                explorerKeyCombo,
                out explorerModifiers,
                out explorerKey);

            GetComboValues(
                rejectModifierCombo,
                rejectKeyCombo,
                out rejectModifiers,
                out rejectKey);

            previewLanguageCode =
                languageCode;

            updatingLanguagePreview = true;

            try
            {
                Text = Localization.Get(
                    "SettingsTitle",
                    previewLanguageCode);

                ApplyLocalizedText(
                    Controls);

                RebuildModifierChoices(
                    nextModifierCombo,
                    nextModifiers);

                RebuildModifierChoices(
                    pauseModifierCombo,
                    pauseModifiers);

                RebuildModifierChoices(
                    explorerModifierCombo,
                    explorerModifiers);

                RebuildModifierChoices(
                    rejectModifierCombo,
                    rejectModifiers);

                RefreshLanguageChoices(
                    previewLanguageCode);

                RefreshThemeChoices(
                    previewThemeMode);

                RefreshClockStyleChoices(GetSelectedClockStyle());
                RefreshSystemStyleChoices(GetSelectedSystemStyle());

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
            }
            finally
            {
                updatingLanguagePreview = false;
            }

            NotifyWidgetPreviewChanged();
        }

        private void ApplyLocalizedText(
            Control.ControlCollection controls)
        {
            foreach (Control control in controls)
            {
                if (control.Tag
                    is string resourceKey)
                {
                    control.Text =
                        Localization.Get(
                            resourceKey,
                            previewLanguageCode);
                }

                if (control.HasChildren)
                {
                    ApplyLocalizedText(
                        control.Controls);
                }
            }
        }

        private void RebuildModifierChoices(
            ComboBox combo,
            uint selectedValue)
        {
            combo.Items.Clear();

            foreach (Choice choice in GetModifierChoices())
            {
                combo.Items.Add(choice);
            }

            SelectChoice(
                combo,
                selectedValue);

            if (combo.SelectedIndex < 0)
            {
                combo.SelectedIndex = 0;
            }
        }

        private void RefreshLanguageChoices(
            string selectedLanguage)
        {
            languageComboBox.Items.Clear();

            Localization.RefreshAvailableLanguages();

            foreach (SupportedLanguage language
                in Localization.AvailableLanguages)
            {
                languageComboBox.Items.Add(
                    new LanguageChoice(
                        Localization.Get(
                            language.DisplayNameResourceKey,
                            previewLanguageCode),
                        language.Code));
            }

            SelectLanguage(
                selectedLanguage);

            if (languageComboBox.SelectedIndex < 0 &&
                languageComboBox.Items.Count > 0)
            {
                languageComboBox.SelectedIndex = 0;
            }
        }

        private void SelectLanguage(
            string languageCode)
        {
            for (int i = 0;
                 i < languageComboBox.Items.Count;
                 i++)
            {
                if (languageComboBox.Items[i]
                    is LanguageChoice choice &&
                    string.Equals(
                        choice.Code,
                        languageCode,
                        StringComparison.OrdinalIgnoreCase))
                {
                    languageComboBox.SelectedIndex = i;
                    return;
                }
            }

            languageComboBox.SelectedIndex = 0;
        }

        private void HookHotkeyValidation()
        {
            ComboBox[] combos =
            {
                nextModifierCombo,
                nextKeyCombo,
                pauseModifierCombo,
                pauseKeyCombo,
                explorerModifierCombo,
                explorerKeyCombo,
                rejectModifierCombo,
                rejectKeyCombo
            };

            foreach (ComboBox combo
                in combos)
            {
                combo.SelectedIndexChanged +=
                    (_, _) =>
                        UpdateHotkeyValidation();
            }
        }

        private void UpdateHotkeyValidation()
        {
            if (hotkeyWarningLabel == null)
            {
                return;
            }

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

            (uint modifiers, uint key)[] values =
            {
                (nextModifiers, nextKey),
                (pauseModifiers, pauseKey),
                (explorerModifiers, explorerKey),
                (rejectModifiers, rejectKey)
            };

            bool duplicateFound = false;

            for (int i = 0;
                 i < values.Length &&
                 !duplicateFound;
                 i++)
            {
                for (int j = i + 1;
                     j < values.Length;
                     j++)
                {
                    if (IsDuplicate(
                        values[i].modifiers,
                        values[i].key,
                        values[j].modifiers,
                        values[j].key))
                    {
                        duplicateFound = true;
                        break;
                    }
                }
            }

            hotkeyWarningLabel.Visible =
                duplicateFound;

            hotkeyWarningLabel.ForeColor =
                ResolvePreviewDarkMode()
                    ? Color.FromArgb(
                        255,
                        175,
                        90)
                    : Color.DarkOrange;
        }

        private void SetDefaultHotkeys()
        {
            SetComboValues(
                nextModifierCombo,
                nextKeyCombo,
                MOD_CONTROL | MOD_ALT,
                0x27);

            SetComboValues(
                pauseModifierCombo,
                pauseKeyCombo,
                MOD_CONTROL | MOD_ALT,
                0x50);

            SetComboValues(
                explorerModifierCombo,
                explorerKeyCombo,
                MOD_CONTROL | MOD_ALT,
                0x45);

            SetComboValues(
                rejectModifierCombo,
                rejectKeyCombo,
                MOD_CONTROL | MOD_ALT | MOD_SHIFT,
                0x52);
        }

        private void UpdateOpacityPreview()
        {
            int value =
                opacityTrackBar.Value;

            opacityValueLabel.Text =
                $"{value}%";

            Opacity =
                value / 100.0;
        }

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

        private void ResetAllSettings()
        {
            SetDefaultHotkeys();

            rejectRootTextBox.Text = "";
            rejectSubfolderCheckBox.Checked = true;
            autostartCheckBox.Checked = false;
            closeToTrayCheckBox.Checked = true;

            ResetAppearanceSettings();

            clockEnabledCheckBox.Checked = false;
            clockLockedCheckBox.Checked = false;
            clockSizeNumeric.Value = 150;
            clockSecondsCheckBox.Checked = false;
            RefreshClockStyleChoices(ClockWidgetStyle.Chrome);
            nextWidgetEnabledCheckBox.Checked = false;
            nextWidgetLockedCheckBox.Checked = false;
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

            ApplyPreviewLocalization(
                Localization.IsLanguageAvailable("de")
                ? "de"
                : Localization.CurrentLanguage);
        }

        private void SelectClockStyle(ClockWidgetStyle style)
        {
            int index = Math.Clamp((int)style, 0, 4);
            if (clockStyleComboBox.SelectedIndex != index)
                clockStyleComboBox.SelectedIndex = index;
            else
                UpdateClockStyleSelection();
        }

        private void UpdateClockStyleSelection()
        {
            ClockWidgetStyle selected = GetSelectedClockStyle();
            if (clockSettingsPreview != null)
            {
                clockSettingsPreview.Style = selected;
                clockSettingsPreview.LanguageCode = previewLanguageCode;
                clockSettingsPreview.Invalidate();
            }
            foreach (ClockStyleCard card in clockStyleCards)
            {
                card.Selected = card.Style == selected;
                card.Caption = Localization.Get(card.Style switch
                {
                    ClockWidgetStyle.Minimal => "ClockStyleMinimal",
                    ClockWidgetStyle.Chrome => "ClockStyleChrome",
                    ClockWidgetStyle.Clean => "ClockStyleClean",
                    ClockWidgetStyle.Glow => "ClockStyleGlow",
                    _ => "ClockStyleClassic"
                }, previewLanguageCode);
                card.Invalidate();
            }
        }

        private void RefreshClockStyleChoices(ClockWidgetStyle selectedStyle)
        {
            if (clockStyleComboBox == null) return;

            clockStyleComboBox.BeginUpdate();
            clockStyleComboBox.Items.Clear();
            clockStyleComboBox.Items.Add(Localization.Get("ClockStyleMinimal", previewLanguageCode));
            clockStyleComboBox.Items.Add(Localization.Get("ClockStyleChrome", previewLanguageCode));
            clockStyleComboBox.Items.Add(Localization.Get("ClockStyleClean", previewLanguageCode));
            clockStyleComboBox.Items.Add(Localization.Get("ClockStyleGlow", previewLanguageCode));
            clockStyleComboBox.Items.Add(Localization.Get("ClockStyleClassic", previewLanguageCode));
            clockStyleComboBox.SelectedIndex = Math.Clamp((int)selectedStyle, 0, 4);
            clockStyleComboBox.EndUpdate();
            UpdateClockStyleSelection();
        }

        private ClockWidgetStyle GetSelectedClockStyle()
        {
            int index = clockStyleComboBox?.SelectedIndex ?? (int)ClockWidgetStyle.Chrome;
            return Enum.IsDefined(typeof(ClockWidgetStyle), index)
                ? (ClockWidgetStyle)index
                : ClockWidgetStyle.Chrome;
        }

        private void RefreshSystemStyleChoices(SystemWidgetStyle selectedStyle)
        {
            if (systemWidgetStyleComboBox == null) return;

            systemWidgetStyleComboBox.BeginUpdate();
            systemWidgetStyleComboBox.Items.Clear();
            systemWidgetStyleComboBox.Items.Add(Localization.Get("ClockStyleMinimal", previewLanguageCode));
            systemWidgetStyleComboBox.Items.Add(Localization.Get("ClockStyleClean", previewLanguageCode));
            systemWidgetStyleComboBox.Items.Add(Localization.Get("ClockStyleGlow", previewLanguageCode));
            systemWidgetStyleComboBox.SelectedIndex = Math.Clamp((int)selectedStyle, 0, 2);
            systemWidgetStyleComboBox.EndUpdate();
        }

        private CheckBox CreateSystemModuleCheckBox(string resourceKey, int x, int y, bool isChecked)
        {
            return new CheckBox
            {
                Text = Localization.Get(resourceKey, previewLanguageCode),
                Tag = resourceKey,
                Location = new Point(x, y),
                AutoSize = true,
                Checked = isChecked
            };
        }

        private SystemWidgetStyle GetSelectedSystemStyle()
        {
            int index = systemWidgetStyleComboBox?.SelectedIndex ?? (int)SystemWidgetStyle.Glow;
            return Enum.IsDefined(typeof(SystemWidgetStyle), index)
                ? (SystemWidgetStyle)index
                : SystemWidgetStyle.Glow;
        }

        private int GetSystemRefreshSeconds() =>
            systemWidgetRefreshComboBox.SelectedIndex switch
            {
                0 => 1,
                2 => 5,
                _ => 2
            };

        private void NotifyWidgetPreviewChanged()
        {
            if (widgetPreviewChanged == null)
            {
                return;
            }

            WidgetSettings preview = initialWidgetSettings.Clone();
            preview.ClockEnabled = clockEnabledCheckBox.Checked;
            preview.ClockLocked = clockLockedCheckBox.Checked;
            preview.ClockSize = (int)clockSizeNumeric.Value;
            preview.ClockShowSeconds = clockSecondsCheckBox.Checked;
            preview.ClockStyle = GetSelectedClockStyle();
            preview.ClockLanguageCode = previewLanguageCode;
            preview.NextEnabled = nextWidgetEnabledCheckBox.Checked;
            preview.NextLocked = nextWidgetLockedCheckBox.Checked;
            preview.SystemEnabled = systemWidgetEnabledCheckBox.Checked;
            preview.SystemLocked = systemWidgetLockedCheckBox.Checked;
            preview.SystemRefreshSeconds = GetSystemRefreshSeconds();
            preview.SystemStyle = GetSelectedSystemStyle();
            preview.SystemShowCpu = systemShowCpuCheckBox.Checked;
            preview.SystemShowRam = systemShowRamCheckBox.Checked;
            preview.SystemShowGpu = systemShowGpuCheckBox.Checked;
            preview.SystemShowVram = systemShowVramCheckBox.Checked;
            preview.SystemShowNetwork = systemShowNetworkCheckBox.Checked;
            preview.SystemShowDrives = systemShowDrivesCheckBox.Checked;

            widgetPreviewChanged(preview);
        }

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

            WindowOpacityPercent =
                opacityTrackBar.Value;

            ThemeMode =
                NormalizeThemeMode(
                    previewThemeMode);

            WidgetSettings = initialWidgetSettings.Clone();
            WidgetSettings.ClockEnabled = clockEnabledCheckBox.Checked;
            WidgetSettings.ClockLocked = clockLockedCheckBox.Checked;
            WidgetSettings.ClockSize = (int)clockSizeNumeric.Value;
            WidgetSettings.ClockShowSeconds = clockSecondsCheckBox.Checked;
            WidgetSettings.ClockStyle = GetSelectedClockStyle();
            WidgetSettings.ClockLanguageCode = previewLanguageCode;
            WidgetSettings.NextEnabled = nextWidgetEnabledCheckBox.Checked;
            WidgetSettings.NextLocked = nextWidgetLockedCheckBox.Checked;
            WidgetSettings.SystemEnabled = systemWidgetEnabledCheckBox.Checked;
            WidgetSettings.SystemLocked = systemWidgetLockedCheckBox.Checked;
            WidgetSettings.SystemRefreshSeconds = GetSystemRefreshSeconds();
            WidgetSettings.SystemStyle = GetSelectedSystemStyle();
            WidgetSettings.SystemShowCpu = systemShowCpuCheckBox.Checked;
            WidgetSettings.SystemShowRam = systemShowRamCheckBox.Checked;
            WidgetSettings.SystemShowGpu = systemShowGpuCheckBox.Checked;
            WidgetSettings.SystemShowVram = systemShowVramCheckBox.Checked;
            WidgetSettings.SystemShowNetwork = systemShowNetworkCheckBox.Checked;
            WidgetSettings.SystemShowDrives = systemShowDrivesCheckBox.Checked;

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

        private static bool IsDuplicate(
            uint modifiers1,
            uint key1,
            uint modifiers2,
            uint key2)
        {
            return modifiers1 != 0 &&
                   modifiers2 != 0 &&
                   modifiers1 == modifiers2 &&
                   key1 == key2;
        }

        private static void GetComboValues(
            ComboBox modifierCombo,
            ComboBox keyCombo,
            out uint modifiers,
            out uint key)
        {
            Choice? modifierChoice =
                modifierCombo.SelectedItem
                as Choice;

            Choice? keyChoice =
                keyCombo.SelectedItem
                as Choice;

            if (modifierChoice == null)
            {
                modifiers = 0;
                key = 0;
                return;
            }

            modifiers =
                modifierChoice.Value;

            key =
                modifiers == 0 ||
                keyChoice == null
                ? 0
                : keyChoice.Value;
        }

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

        protected override void OnHandleCreated(
            EventArgs e)
        {
            base.OnHandleCreated(e);

            ApplyTitleBarTheme(
                ResolvePreviewDarkMode());
        }

        private Font CreateOwnedFont(string familyName, float emSize, FontStyle style = FontStyle.Regular)
        {
            Font font = new Font(familyName, emSize, style);
            ownedFonts.Add(font);
            return font;
        }

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

        private void ApplyTitleBarTheme(
            bool darkMode)
        {
            if (!IsHandleCreated)
            {
                return;
            }

            int darkValue =
                darkMode ? 1 : 0;

            DwmSetWindowAttribute(
                Handle,
                20,
                ref darkValue,
                sizeof(int));
        }

        [DllImport("dwmapi.dll")]
        private static extern int
            DwmSetWindowAttribute(
                IntPtr hwnd,
                int attribute,
                ref int attributeValue,
                int attributeSize);
        private sealed class ClockStyleCard : Control
        {
            [Browsable(false)]
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public ClockWidgetStyle Style { get; set; }
            [Browsable(false)]
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public string Caption { get; set; } = "";
            [Browsable(false)]
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public bool Selected { get; set; }
            [Browsable(false)]
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public bool ShowSeconds { get; set; }

            public ClockStyleCard()
            {
                Cursor = Cursors.Hand;
                DoubleBuffered = true;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Color bg = Color.FromArgb(28, 31, 36);
                Color border = Selected ? Color.FromArgb(55, 145, 255) : Color.FromArgb(75, 80, 88);
                using SolidBrush b = new(bg);
                using Pen p = new(border, Selected ? 3f : 1f);
                Rectangle r = new(1, 1, Width - 3, Height - 3);
                g.FillRectangle(b, r);
                g.DrawRectangle(p, r);
                DrawMiniClock(g, new Rectangle(6, 5, Width - 12, 58), Style, ShowSeconds);
                using Font f = new("Segoe UI", 8.5f, Selected ? FontStyle.Bold : FontStyle.Regular);
                using SolidBrush tb = new(Color.FromArgb(235, 235, 235));
                using StringFormat sf = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
                g.DrawString(Caption, f, tb, new RectangleF(5, 65, Width - 10, 22), sf);
            }
        }

        private sealed class ClockSettingsPreview : Control
        {
            [Browsable(false)]
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public ClockWidgetStyle Style { get; set; } = ClockWidgetStyle.Chrome;
            [Browsable(false)]
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public bool ShowSeconds { get; set; }
            [Browsable(false)]
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public string LanguageCode { get; set; } = "de";

            public ClockSettingsPreview() { DoubleBuffered = true; }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using System.Drawing.Drawing2D.LinearGradientBrush bg = new(ClientRectangle, Color.FromArgb(12, 24, 34), Color.FromArgb(25, 20, 18), 0f);
                g.FillRectangle(bg, ClientRectangle);
                using Pen border = new(Color.FromArgb(80, 95, 110));
                g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
                DrawMiniClock(g, new Rectangle(80, 12, Width - 160, Height - 24), Style, ShowSeconds, true);
            }
        }

        private static void DrawMiniClock(Graphics g, Rectangle bounds, ClockWidgetStyle style, bool seconds, bool large = false)
        {
            string time = DateTime.Now.ToString(seconds ? "HH:mm:ss" : "HH:mm");
            string font = style == ClockWidgetStyle.Classic ? "Georgia" : "Segoe UI";
            FontStyle fs = style == ClockWidgetStyle.Clean ? FontStyle.Bold : FontStyle.Regular;
            float timeSize = large ? 54f : 23f;
            Color c = style == ClockWidgetStyle.Glow ? Color.FromArgb(205, 245, 255) : Color.FromArgb(238, 240, 243);
            using Font tf = new(font, timeSize, fs, GraphicsUnit.Pixel);
            using StringFormat sf = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            Rectangle timeRect = new(bounds.X, bounds.Y, bounds.Width, (int)(bounds.Height * .58));
            if (style == ClockWidgetStyle.Glow)
            {
                using SolidBrush glow = new(Color.FromArgb(80, 90, 210, 255));
                Rectangle gr = timeRect; gr.Offset(1, 1);
                g.DrawString(time, tf, glow, gr, sf);
            }
            else if (style == ClockWidgetStyle.Chrome)
            {
                using SolidBrush shadow = new(Color.FromArgb(170, 0, 0, 0));
                Rectangle sr = timeRect; sr.Offset(2, 3);
                g.DrawString(time, tf, shadow, sr, sf);
            }
            using SolidBrush tb = new(c);
            g.DrawString(time, tf, tb, timeRect, sf);

            int y = bounds.Y + (int)(bounds.Height * .62);
            if (style != ClockWidgetStyle.Clean)
            {
                using Pen lp = new(style == ClockWidgetStyle.Glow ? Color.FromArgb(160, 225, 255) : Color.FromArgb(210, 215, 220), large ? 2f : 1f);
                int gap = large ? 14 : 6;
                g.DrawLine(lp, bounds.X + bounds.Width / 8, y, bounds.X + bounds.Width / 2 - gap, y);
                g.DrawLine(lp, bounds.X + bounds.Width / 2 + gap, y, bounds.Right - bounds.Width / 8, y);
                if (style != ClockWidgetStyle.Classic)
                {
                    Point[] d = { new(bounds.X + bounds.Width / 2, y - 5), new(bounds.X + bounds.Width / 2 + 5, y), new(bounds.X + bounds.Width / 2, y + 5), new(bounds.X + bounds.Width / 2 - 5, y) };
                    using SolidBrush db = new(c); g.FillPolygon(db, d);
                }
            }
            using Font df = new(style == ClockWidgetStyle.Classic ? "Georgia" : "Segoe UI", large ? 18f : 8f, FontStyle.Regular, GraphicsUnit.Pixel);
            Rectangle dateRect = new(bounds.X, y + (large ? 8 : 3), bounds.Width, large ? 28 : 14);
            using SolidBrush dateBrush = new(c);
            g.DrawString("13. September 2026", df, dateBrush, dateRect, sf);
        }

    }
}
