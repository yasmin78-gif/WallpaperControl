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
        /// <param name="selectedStyle">The widget style to keep selected.</param>
        private void RefreshSystemStyleChoices(SystemWidgetStyle selectedStyle)
        {
            RefreshWidgetStyleChoices(systemWidgetStyleComboBox, selectedStyle);
        }

        /// <summary>
        /// Creates a localized checkbox for an optional system-widget metric.
        /// </summary>
        /// <param name="resourceKey">The localization resource key used for the caption.</param>
        /// <param name="x">The horizontal coordinate.</param>
        /// <param name="y">The vertical coordinate.</param>
        /// <param name="isChecked">The initial checkbox state.</param>
        /// <returns>The checkbox for the requested system-monitor module.</returns>
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
        /// <returns>The selected system widget style, with the default used for an invalid selection.</returns>
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
        /// <returns>The selected system-monitor refresh interval in seconds.</returns>
        private int GetSystemRefreshSeconds() =>
            systemWidgetRefreshComboBox.SelectedIndex switch
            {
                0 => 1,
                2 => 5,
                _ => 2
            };
    }
}
