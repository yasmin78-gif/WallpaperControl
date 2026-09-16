using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Settings dialog SystemPage members. See README.md in this directory for the code map.
    internal sealed partial class SettingsForm
    {
        /// <summary>
        /// Rebuilds localized system-widget styles while retaining the selected style.
        /// </summary>
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

        /// <summary>
        /// Creates a localized checkbox for an optional system-widget metric.
        /// </summary>
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

        /// <summary>
        /// Returns the selected system-widget style with a fallback for an empty selector.
        /// </summary>
        private SystemWidgetStyle GetSelectedSystemStyle()
        {
            int index = systemWidgetStyleComboBox?.SelectedIndex ?? (int)SystemWidgetStyle.Glow;
            return Enum.IsDefined(typeof(SystemWidgetStyle), index)
                ? (SystemWidgetStyle)index
                : SystemWidgetStyle.Glow;
        }

        /// <summary>
        /// Maps the system refresh selector to an interval in seconds.
        /// </summary>
        private int GetSystemRefreshSeconds() =>
            systemWidgetRefreshComboBox.SelectedIndex switch
            {
                0 => 1,
                2 => 5,
                _ => 2
            };
    }
}
