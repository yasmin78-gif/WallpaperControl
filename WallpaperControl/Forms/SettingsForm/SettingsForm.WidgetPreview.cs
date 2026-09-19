using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Settings dialog WidgetPreview members. See README.md in this directory for the code map.
    internal sealed partial class SettingsForm
    {
        /// <summary>
        /// Clones the initial widget settings, applies the dialog values, and publishes a live preview.
        /// </summary>
        private void NotifyWidgetPreviewChanged()
        {
            if (widgetPreviewChanged != null)
                widgetPreviewChanged(ReadWidgetSettings(applySaveDefaults: false));
        }
        /// <summary>
        /// Reads editable widget values into a clone, preserving positions and unexposed settings.
        /// </summary>
        /// <param name="applySaveDefaults">True to apply save-only defaults; false to retain live-preview values.</param>
        /// <returns>A settings snapshot built from the controls, optionally applying save-time defaults.</returns>
        private WidgetSettings ReadWidgetSettings(bool applySaveDefaults)
        {
            WidgetSettings preview = initialWidgetSettings.Clone();
            preview.ClockEnabled = clockEnabledCheckBox.Checked;
            preview.ClockLocked = clockLockedCheckBox.Checked;
            preview.ClockSize = (int)clockSizeNumeric.Value;
            preview.ClockShowSeconds = clockSecondsCheckBox.Checked;
            preview.ClockStyle = GetSelectedClockStyle();
            preview.ClockLanguageCode = previewLanguageCode;
            preview.NextEnabled = nextWidgetEnabledCheckBox.Checked;
            preview.NextLocked = nextWidgetLockedCheckBox.Checked;
            preview.NextStyle = GetSelectedNextStyle();
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
            preview.WeatherEnabled = weatherWidgetEnabledCheckBox.Checked;
            preview.WeatherLocked = weatherWidgetLockedCheckBox.Checked;
            preview.WeatherRefreshMinutes = GetWeatherRefreshMinutes();
            preview.WeatherStyle = GetSelectedWeatherStyle();
            // Live previews retain an empty location; saving uses the historical default.
            preview.WeatherLocationName = applySaveDefaults && string.IsNullOrWhiteSpace(weatherLocationTextBox.Text)
                ? "Karlsruhe"
                : weatherLocationTextBox.Text.Trim();
            preview.WeatherShowForecast = weatherShowForecastCheckBox.Checked;
            preview.CalendarEnabled = calendarWidgetEnabledCheckBox.Checked;
            preview.CalendarLocked = calendarWidgetLockedCheckBox.Checked;
            preview.CalendarStyle = GetSelectedCalendarStyle();
            preview.CalendarMaximumHeight = (int)calendarMaximumHeightNumeric.Value;
            preview.CalendarMaxEntries = GetCalendarMaxEntries();
            preview.CalendarShowLocation = calendarShowLocationCheckBox.Checked;
            preview.CalendarRefreshMinutes = GetCalendarRefreshMinutes();
            preview.CalendarSources = new(calendarSources);

            return preview;
        }

    }
}
