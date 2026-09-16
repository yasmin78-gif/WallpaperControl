using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Settings dialog Hotkeys members. See README.md in this directory for the code map.
    internal sealed partial class SettingsForm
    {
        /// <summary>
        /// Creates the modifier and key selectors for one shortcut and connects their enabled state.
        /// </summary>
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

        /// <summary>
        /// Builds localized modifier combinations, including the disabled-shortcut option.
        /// </summary>
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

        /// <summary>
        /// Builds the available shortcut keys.
        /// </summary>
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

        /// <summary>
        /// Selects a stored shortcut combination and updates whether the key selector is enabled.
        /// </summary>
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

        /// <summary>
        /// Selects the numeric choice matching the requested value.
        /// </summary>
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

        /// <summary>
        /// Rebuilds localized modifier choices while preserving the current shortcut value.
        /// </summary>
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

        /// <summary>
        /// Connects shortcut selector changes to duplicate validation.
        /// </summary>
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

        /// <summary>
        /// Refreshes the warning for duplicate active shortcut combinations.
        /// </summary>
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

        /// <summary>
        /// Restores the default shortcut combinations in the dialog controls.
        /// </summary>
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

        /// <summary>
        /// Checks whether two enabled shortcut combinations use the same modifiers and key.
        /// </summary>
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

        /// <summary>
        /// Reads a shortcut combination from its selectors, honoring the disabled option.
        /// </summary>
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
    }
}
