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
            if (widgetPreviewChanged == null)
            {
                return;
            }

            WidgetSettings preview = initialWidgetSettings.Clone();
            preview.ClockEnabled = clockEnabledCheckBox.Checked;
            preview.ClockLocked = clockLockedCheckBox.Checked;
            preview.ClockSize = (int)clockSizeNumeric.Value;
            preview.ClockShowSeconds = clockSecondsCheckBox.Checked;
            preview.ClockStyle = GetSelectedClockStyle();
            preview.ClockLanguageCode = previewLanguageCode;
            preview.NextEnabled = nextWidgetEnabledCheckBox.Checked;
            preview.NextLocked = nextWidgetLockedCheckBox.Checked;
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
            preview.WeatherLocationName = weatherLocationTextBox.Text.Trim();
            preview.WeatherShowForecast = weatherShowForecastCheckBox.Checked;
            preview.CalendarEnabled = calendarWidgetEnabledCheckBox.Checked;
            preview.CalendarLocked = calendarWidgetLockedCheckBox.Checked;
            preview.CalendarStyle = GetSelectedCalendarStyle();
            preview.CalendarMaxEntries = GetCalendarMaxEntries();
            preview.CalendarShowLocation = calendarShowLocationCheckBox.Checked;
            preview.CalendarRefreshMinutes = GetCalendarRefreshMinutes();
            preview.CalendarIcsUrl = calendarIcsUrlTextBox.Text.Trim();
            preview.CalendarHolidayIcsUrl = calendarHolidayIcsUrlTextBox.Text.Trim();

            widgetPreviewChanged(preview);
        }
    }
}
