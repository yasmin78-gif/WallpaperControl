using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WallpaperControl
{
    internal sealed class SettingsForm : Form
    {
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
            int windowOpacityPercent)
        {
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
                new Size(570, 650);

            Font =
                new Font("Segoe UI", 10);

            TabControl tabControl =
                new TabControl
                {
                    Location = new Point(20, 18),
                    Size = new Size(530, 555)
                };

            TabPage hotkeysPage =
                new TabPage
                {
                    Text = Localization.Get(
                        "SettingsTabHotkeys",
                        previewLanguageCode),
                    Tag = "SettingsTabHotkeys"
                };

            TabPage behaviorPage =
                new TabPage
                {
                    Text = Localization.Get(
                        "SettingsTabBehavior",
                        previewLanguageCode),
                    Tag = "SettingsTabBehavior"
                };

            TabPage appearancePage =
                new TabPage
                {
                    Text = Localization.Get(
                        "SettingsTabAppearance",
                        previewLanguageCode),
                    Tag = "SettingsTabAppearance"
                };

            tabControl.TabPages.Add(
                hotkeysPage);

            tabControl.TabPages.Add(
                behaviorPage);

            tabControl.TabPages.Add(
                appearancePage);

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
                Font = new Font(
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
                        new Font(
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
                        new Font(
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
                        new Font(
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
                        new Point(18, 235),
                    AutoSize = true,
                    Font =
                        new Font(
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
                        new Point(18, 275),
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
                        new Point(18, 307),
                    AutoSize = true,
                    Checked =
                        closeToTrayEnabled
                };

            behaviorPage.Controls.Add(
                rejectTitleLabel);

            behaviorPage.Controls.Add(
                rejectFolderLabel);

            behaviorPage.Controls.Add(
                rejectRootTextBox);

            behaviorPage.Controls.Add(
                rejectRootBrowseButton);

            behaviorPage.Controls.Add(
                rejectSubfolderCheckBox);

            behaviorPage.Controls.Add(
                rejectHintLabel);

            behaviorPage.Controls.Add(
                generalTitleLabel);

            behaviorPage.Controls.Add(
                autostartCheckBox);

            behaviorPage.Controls.Add(
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
                        new Font(
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
                        new Font(
                            "Segoe UI",
                            8.25f)
                };

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
                        new Point(18, 165),
                    Size =
                        new Size(175, 25)
                };

            languageComboBox =
                new ComboBox
                {
                    Location =
                        new Point(205, 160),
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

            appearancePage.Controls.Add(
                languageLabel);

            appearancePage.Controls.Add(
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
                        new Point(20, 595),
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
                        new Point(330, 595),
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
                        new Point(440, 595),
                    Size =
                        new Size(110, 38)
                };

            saveButton.Click +=
                (_, _) =>
                    SaveAndClose();

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

            ApplyPreviewLocalization(
                Localization.IsLanguageAvailable("de")
                ? "de"
                : Localization.CurrentLanguage);
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
            RejectRootFolder =
                rejectRootTextBox.Text.Trim();
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
            Color background =
                darkMode
                    ? Color.FromArgb(32, 32, 32)
                    : SystemColors.Control;

            Color foreground =
                darkMode
                    ? Color.FromArgb(235, 235, 235)
                    : SystemColors.ControlText;

            Color inputBackground =
                darkMode
                    ? Color.FromArgb(48, 48, 48)
                    : SystemColors.Window;

            Color buttonBackground =
                darkMode
                    ? Color.FromArgb(50, 50, 50)
                    : SystemColors.Control;

            BackColor = background;
            ForeColor = foreground;

            ApplyThemeToControls(
                Controls,
                darkMode,
                background,
                foreground,
                inputBackground,
                buttonBackground);

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
                        darkMode
                            ? Color.FromArgb(
                                85,
                                85,
                                85)
                            : Color.FromArgb(
                                180,
                                180,
                                180);
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

        protected override void Dispose(
            bool disposing)
        {
            if (disposing)
            {
                SystemEvents.UserPreferenceChanged -=
                    SystemEvents_UserPreferenceChanged;
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
    }
}
