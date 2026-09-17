using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Settings dialog Language members. See README.md in this directory for the code map.
    internal sealed partial class SettingsForm
    {
        /// <summary>
        /// Applies the selected language to the dialog and widget previews.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
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

        /// <summary>
        /// Refreshes translated controls and choices while guarding against recursive preview events.
        /// </summary>
        /// <param name="languageCode">The language code used for localized text.</param>
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
                RefreshWeatherStyleChoices(GetSelectedWeatherStyle());
                RefreshNextStyleChoices(GetSelectedNextStyle());
                RefreshCalendarStyleChoices(GetSelectedCalendarStyle());

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

        /// <summary>
        /// Recursively applies resource-key translations to tagged controls.
        /// </summary>
        /// <param name="controls">The controls to style or localize recursively.</param>
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

        /// <summary>
        /// Rebuilds the list of available languages and restores the requested selection.
        /// </summary>
        /// <param name="selectedLanguage">The language code to keep selected.</param>
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

        /// <summary>
        /// Selects an available language code or the existing fallback choice.
        /// </summary>
        /// <param name="languageCode">The language code used for localized text.</param>
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
    }
}
