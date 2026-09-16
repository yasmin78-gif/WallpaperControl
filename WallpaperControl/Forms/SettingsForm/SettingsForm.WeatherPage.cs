using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Settings dialog WeatherPage members. See README.md in this directory for the code map.
    internal sealed partial class SettingsForm
    {
        /// <summary>
        /// Rebuilds localized weather-widget styles while retaining the selected style.
        /// </summary>
        private void RefreshWeatherStyleChoices(SystemWidgetStyle selectedStyle)
        {
            RefreshWidgetStyleChoices(weatherWidgetStyleComboBox, selectedStyle);
        }

        /// <summary>
        /// Returns the selected weather style with a fallback for an empty selector.
        /// </summary>
        private SystemWidgetStyle GetSelectedWeatherStyle()
        {
            int index = weatherWidgetStyleComboBox?.SelectedIndex ?? (int)SystemWidgetStyle.Glow;
            return Enum.IsDefined(typeof(SystemWidgetStyle), index)
                ? (SystemWidgetStyle)index
                : SystemWidgetStyle.Glow;
        }

        /// <summary>
        /// Maps the weather refresh selector to an interval in minutes.
        /// </summary>
        private int GetWeatherRefreshMinutes() =>
            weatherWidgetRefreshComboBox.SelectedIndex switch
            {
                0 => 15,
                2 => 60,
                3 => 120,
                _ => 30
            };
    }
}
