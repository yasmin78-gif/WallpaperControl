using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Settings dialog Choices members. See README.md in this directory for the code map.
    internal sealed partial class SettingsForm
    {
        /// <summary>
        /// Pairs a selector label with its stable settings value.
        /// </summary>
        private sealed class Choice
        {
            public string Text { get; }
            public uint Value { get; }

            /// <summary>Stores the display label and its stable selection value.</summary>
            public Choice(
                string text,
                uint value)
            {
                Text = text;
                Value = value;
            }

            /// <summary>Returns the label displayed by the selector.</summary>
            public override string ToString() =>
                Text;
        }

        /// <summary>
        /// Pairs a selector label with its stable settings value.
        /// </summary>
        private sealed class LanguageChoice
        {
            public string Text { get; }
            public string Code { get; }

            /// <summary>Stores the display label and its stable selection value.</summary>
            public LanguageChoice(
                string text,
                string code)
            {
                Text = text;
                Code = code;
            }

            /// <summary>Returns the label displayed by the selector.</summary>
            public override string ToString() =>
                Text;
        }

        /// <summary>
        /// Pairs a selector label with its stable settings value.
        /// </summary>
        private sealed class ThemeChoice
        {
            public string Text { get; }
            public string Mode { get; }

            /// <summary>Stores the display label and its stable selection value.</summary>
            public ThemeChoice(
                string text,
                string mode)
            {
                Text = text;
                Mode = mode;
            }

            /// <summary>Returns the label displayed by the selector.</summary>
            public override string ToString() =>
                Text;
        }
        /// <summary>Rebuilds the shared widget style options without changing selection event ordering.</summary>
        private void RefreshWidgetStyleChoices(ComboBox? comboBox, SystemWidgetStyle selectedStyle)
        {
            if (comboBox == null) return;

            comboBox.BeginUpdate();
            comboBox.Items.Clear();
            comboBox.Items.Add(Localization.Get("ClockStyleMinimal", previewLanguageCode));
            comboBox.Items.Add(Localization.Get("ClockStyleClean", previewLanguageCode));
            comboBox.Items.Add(Localization.Get("ClockStyleGlow", previewLanguageCode));
            comboBox.SelectedIndex = Math.Clamp((int)selectedStyle, 0, 2);
            comboBox.EndUpdate();
        }

    }
}
