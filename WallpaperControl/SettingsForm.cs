using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
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
        private readonly TrackBar opacityTrackBar;
        private readonly Label opacityValueLabel;
        private string previewLanguageCode;
        private bool updatingLanguagePreview;

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

        public SettingsForm(
            bool darkMode,
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

            Text = Localization.Get(
                "SettingsTitle",
                previewLanguageCode);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(520, 710);
            Font = new Font("Segoe UI", 10);

            Label titleLabel = new Label
            {
                Text = Localization.Get("SettingsHotkeysTitle", previewLanguageCode),
                Tag = "SettingsHotkeysTitle",
                Location = new Point(25, 20),
                AutoSize = true,
                Font = new Font(
                    "Segoe UI",
                    12,
                    FontStyle.Bold)
            };

            Label hintLabel = new Label
            {
                Text = Localization.Get("SettingsHotkeysHint", previewLanguageCode),
                Tag = "SettingsHotkeysHint",
                Location = new Point(25, 50),
                Size = new Size(470, 24)
            };

            CreateHotkeyRow(
                "SettingsHotkeyNext",
                85,
                out nextModifierCombo,
                out nextKeyCombo);

            CreateHotkeyRow(
                "SettingsHotkeyPause",
                125,
                out pauseModifierCombo,
                out pauseKeyCombo);

            CreateHotkeyRow(
                "SettingsHotkeyExplorer",
                165,
                out explorerModifierCombo,
                out explorerKeyCombo);

            CreateHotkeyRow(
                "SettingsHotkeyReject",
                205,
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

            Button resetHotkeysButton = new Button
            {
                Text = Localization.Get("SettingsResetHotkeys", previewLanguageCode),
                Tag = "SettingsResetHotkeys",
                Location = new Point(295, 240),
                Size = new Size(200, 32)
            };

            resetHotkeysButton.Click +=
                (_, _) => SetDefaultHotkeys();

            Controls.Add(resetHotkeysButton);

            Label rejectTitleLabel = new Label
            {
                Text = Localization.Get("SettingsRejectTitle", previewLanguageCode),
                Tag = "SettingsRejectTitle",
                Location = new Point(25, 325),
                AutoSize = true,
                Font = new Font(
                    "Segoe UI",
                    12,
                    FontStyle.Bold)
            };

            Label rejectFolderLabel = new Label
            {
                Text = Localization.Get("SettingsRejectFolder", previewLanguageCode),
                Tag = "SettingsRejectFolder",
                Location = new Point(25, 290),
                AutoSize = true
            };

            rejectRootTextBox = new TextBox
            {
                Location = new Point(25, 352),
                Size = new Size(415, 28),
                ReadOnly = true,
                Text = rejectRootFolder
            };

            rejectRootBrowseButton = new Button
            {
                Text = "...",
                Location = new Point(450, 351),
                Size = new Size(45, 29)
            };

            rejectRootBrowseButton.Click +=
                RejectRootBrowseButton_Click;

            rejectSubfolderCheckBox = new CheckBox
            {
                Text = Localization.Get("SettingsRejectSubfolder", previewLanguageCode),
                Tag = "SettingsRejectSubfolder",
                Location = new Point(25, 390),
                AutoSize = true,
                Checked = rejectUseSubfolder
            };

            Label rejectHintLabel = new Label
            {
                Text = Localization.Get("SettingsRejectHint", previewLanguageCode),
                Tag = "SettingsRejectHint",
                Location = new Point(25, 418),
                Size = new Size(470, 38),
                Font = new Font("Segoe UI", 8.25f)
            };

            Controls.Add(rejectTitleLabel);
            Controls.Add(rejectFolderLabel);
            Controls.Add(rejectRootTextBox);
            Controls.Add(rejectRootBrowseButton);
            Controls.Add(rejectSubfolderCheckBox);
            Controls.Add(rejectHintLabel);

            Label generalTitleLabel = new Label
            {
                Text = Localization.Get("SettingsGeneralTitle", previewLanguageCode),
                Tag = "SettingsGeneralTitle",
                Location = new Point(25, 460),
                AutoSize = true,
                Font = new Font(
                    "Segoe UI",
                    12,
                    FontStyle.Bold)
            };

            autostartCheckBox = new CheckBox
            {
                Text = Localization.Get("SettingsAutostart", previewLanguageCode),
                Tag = "SettingsAutostart",
                Location = new Point(25, 495),
                AutoSize = true,
                Checked = autostartEnabled
            };

            closeToTrayCheckBox = new CheckBox
            {
                Text = Localization.Get(
                    "SettingsCloseToTray",
                    previewLanguageCode),
                Tag = "SettingsCloseToTray",
                Location = new Point(25, 523),
                AutoSize = true,
                Checked = closeToTrayEnabled
            };

            Label languageLabel = new Label
            {
                Text = Localization.Get("SettingsLanguageLabel", previewLanguageCode),
                Tag = "SettingsLanguageLabel",
                Location = new Point(25, 560),
                Size = new Size(175, 25)
            };

            languageComboBox = new ComboBox
            {
                Location = new Point(205, 555),
                Size = new Size(290, 28),
                DropDownStyle =
                    ComboBoxStyle.DropDownList
            };

            RefreshLanguageChoices(
                previewLanguageCode);

SelectLanguage(
                previewLanguageCode);

            languageComboBox.SelectedIndexChanged +=
                LanguageComboBox_SelectedIndexChanged;

            Controls.Add(generalTitleLabel);
            Controls.Add(autostartCheckBox);
            Controls.Add(closeToTrayCheckBox);
            Controls.Add(languageLabel);
            Controls.Add(languageComboBox);

            Label opacityLabel = new Label
            {
                Text = Localization.Get(
                    "SettingsWindowOpacity",
                    previewLanguageCode),
                Tag = "SettingsWindowOpacity",
                Location = new Point(25, 600),
                Size = new Size(175, 25)
            };

            opacityTrackBar = new TrackBar
            {
                Location = new Point(205, 593),
                Size = new Size(235, 30),
                Minimum = 92,
                Maximum = 100,
                SmallChange = 1,
                LargeChange = 1,
                TickFrequency = 1,
                TickStyle = TickStyle.None,
                AutoSize = false,
                Value = Math.Clamp(
                    windowOpacityPercent,
                    92,
                    100)
            };

            opacityValueLabel = new Label
            {
                Location = new Point(450, 600),
                Size = new Size(45, 25),
                TextAlign = ContentAlignment.TopRight
            };

            UpdateOpacityPreview();

            opacityTrackBar.ValueChanged +=
                (_, _) => UpdateOpacityPreview();

            Controls.Add(opacityLabel);
            Controls.Add(opacityTrackBar);
            Controls.Add(opacityValueLabel);

            Button defaultsButton = new Button
            {
                Text = Localization.Get("SettingsRestoreDefaults", previewLanguageCode),
                Tag = "SettingsRestoreDefaults",
                Location = new Point(25, 655),
                Size = new Size(200, 38)
            };

            defaultsButton.Click +=
                (_, _) => ResetAllSettings();

            Button cancelButton = new Button
            {
                Text = Localization.Get("SettingsCancel", previewLanguageCode),
                Tag = "SettingsCancel",
                Location = new Point(280, 655),
                Size = new Size(100, 38),
                DialogResult = DialogResult.Cancel
            };

            Button saveButton = new Button
            {
                Text = Localization.Get("SettingsSave", previewLanguageCode),
                Tag = "SettingsSave",
                Location = new Point(390, 655),
                Size = new Size(105, 38)
            };

            saveButton.Click +=
                (_, _) => SaveAndClose();

            Controls.Add(titleLabel);
            Controls.Add(hintLabel);
            Controls.Add(defaultsButton);
            Controls.Add(cancelButton);
            Controls.Add(saveButton);

            AcceptButton = saveButton;
            CancelButton = cancelButton;

            ApplyTheme(
                darkMode,
                titleLabel,
                hintLabel,
                defaultsButton,
                cancelButton,
                saveButton,
                rejectRootBrowseButton,
                resetHotkeysButton);
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

            Controls.Add(label);
            Controls.Add(modifierCombo);
            Controls.Add(keyCombo);
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
                MOD_CONTROL | MOD_ALT,
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

        private void ResetAllSettings()
        {
            SetDefaultHotkeys();

            rejectRootTextBox.Text = "";
            rejectSubfolderCheckBox.Checked = true;
            autostartCheckBox.Checked = false;
            closeToTrayCheckBox.Checked = true;

            opacityTrackBar.Value = 92;

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
            bool darkMode,
            Label titleLabel,
            Label hintLabel,
            params Button[] buttons)
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

            BackColor = background;
            ForeColor = foreground;

            titleLabel.ForeColor = foreground;
            hintLabel.ForeColor = foreground;

            foreach (Control control in Controls)
            {
                if (control is Label label)
                {
                    label.ForeColor = foreground;
                }
                else if (control is ComboBox combo)
                {
                    combo.BackColor = inputBackground;
                    combo.ForeColor = foreground;
                }
                else if (control is TrackBar trackBar)
                {
                    trackBar.BackColor = background;
                    trackBar.ForeColor = foreground;
                }
            }

            foreach (Button button in buttons)
            {
                button.UseVisualStyleBackColor = false;
                button.BackColor =
                    darkMode
                    ? Color.FromArgb(50, 50, 50)
                    : SystemColors.Control;

                button.ForeColor = foreground;
                button.FlatStyle = FlatStyle.Flat;

                button.FlatAppearance.BorderColor =
                    darkMode
                    ? Color.FromArgb(85, 85, 85)
                    : Color.FromArgb(180, 180, 180);
            }

            int darkValue =
                darkMode ? 1 : 0;

            if (IsHandleCreated)
            {
                DwmSetWindowAttribute(
                    Handle,
                    20,
                    ref darkValue,
                    sizeof(int));
            }
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
