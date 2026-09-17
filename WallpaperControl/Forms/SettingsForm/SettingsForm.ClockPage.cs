using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Settings dialog ClockPage members. See README.md in this directory for the code map.
    internal sealed partial class SettingsForm
    {
        /// <summary>
        /// Selects a clock style and refreshes its cards and preview.
        /// </summary>
        /// <param name="style">The visual style used to render the widget.</param>
        private void SelectClockStyle(ClockWidgetStyle style)
        {
            int index = Math.Clamp((int)style, 0, 4);
            if (clockStyleComboBox.SelectedIndex != index)
                clockStyleComboBox.SelectedIndex = index;
            else
                UpdateClockStyleSelection();
        }

        /// <summary>
        /// Synchronizes clock-style cards and previews with the current control values.
        /// </summary>
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

        /// <summary>
        /// Rebuilds localized clock-style choices while retaining the selected style.
        /// </summary>
        /// <param name="selectedStyle">The widget style to keep selected.</param>
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

        /// <summary>
        /// Returns the selected clock style with a safe fallback for an empty selector.
        /// </summary>
        /// <returns>The selected clock widget style, with the default used for an invalid selection.</returns>
        private ClockWidgetStyle GetSelectedClockStyle()
        {
            int index = clockStyleComboBox?.SelectedIndex ?? (int)ClockWidgetStyle.Chrome;
            return Enum.IsDefined(typeof(ClockWidgetStyle), index)
                ? (ClockWidgetStyle)index
                : ClockWidgetStyle.Chrome;
        }
    }
}
