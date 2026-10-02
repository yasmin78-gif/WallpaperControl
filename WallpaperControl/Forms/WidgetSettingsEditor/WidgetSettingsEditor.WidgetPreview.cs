using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Widget editor WidgetPreview members.
    internal sealed partial class WidgetSettingsEditor
    {
        /// <summary>
        /// Clones the initial widget settings, applies the dialog values, and publishes a live preview.
        /// </summary>
        private void NotifyWidgetPreviewChanged()
        {
            if (!loadingControls && widgetPreviewChanged != null)
                {
                    WidgetSettings value = ReadWidgetSettings(applySaveDefaults: false);
                    value.Web.Url = appliedWebUrl;
                    widgetPreviewChanged(value);
                }
        }
        /// <summary>
        /// Reads editable widget values into a clone, preserving positions and unexposed settings.
        /// </summary>
        /// <param name="applySaveDefaults">True to apply save-only defaults; false to retain live-preview values.</param>
        /// <returns>A settings snapshot built from the controls, optionally applying save-time defaults.</returns>
        internal WidgetSettings ReadWidgetSettings(bool applySaveDefaults)
        {
            WidgetSettings preview = initialWidgetSettings.Clone();
            preview.PackageStatusNotifications = packageNotifications.Checked;
            ReadWebControls(preview.Web);
            preview.TwitchEnabled = twitchEnabled.Checked; preview.TwitchLocked = twitchLocked.Checked;
            preview.TwitchStyle = (SystemWidgetStyle)Math.Max(0, twitchStyle.SelectedIndex); preview.TwitchMaximumHeight = (int)twitchMaximum.Value;
            preview.TwitchRefreshMinutes = TwitchIntervalMinutes;
            preview.TwitchLiveNotifications = twitchNotifications.Checked;
            preview.PackageEnabled = packageEnabled.Checked; preview.PackageLocked = packageLocked.Checked; preview.PackageMaximumHeight = (int)packageMaximum.Value;
            preview.PackageStyle = (SystemWidgetStyle)Math.Max(0, packageStyle.SelectedIndex);
            preview.PackageAutomaticRefresh = packageAutomatic.Checked; preview.PackageRefreshMinutes = PackageIntervalMinutes;
            preview.NotesEnabled = notesEnabled.Checked;
            preview.NotesLocked = notesLocked.Checked;
            preview.NotesMaximumHeight = (int)notesMaximumHeight.Value;
            preview.NotesStyle = (SystemWidgetStyle)Math.Max(0, notesStyle.SelectedIndex);
            preview.WallpaperInfoFontSize = (int)wallpaperInfoFontSize.Value;
            preview.ClockEnabled = clockEnabledCheckBox.Checked;
            preview.ClockLocked = clockLockedCheckBox.Checked;
            preview.ClockSize = (int)clockSizeNumeric.Value;
            preview.ClockShowSeconds = clockSecondsCheckBox.Checked;
            preview.ClockStyle = GetSelectedClockStyle();
            preview.ClockLanguageCode = previewLanguageCode;
            preview.WallpaperInfoEnabled = wallpaperInfoEnabled.Checked;
            preview.WallpaperInfoLocked = wallpaperInfoLocked.Checked;
            preview.WallpaperInfoShowAdvanced = wallpaperInfoAdvanced.Checked;
            preview.WallpaperInfoShowExtension = wallpaperInfoExtension.Checked;
            preview.WallpaperInfoHiddenSuffix = applySaveDefaults ? wallpaperInfoSuffix.Text.Trim() : wallpaperInfoSuffix.Text;
            preview.WallpaperInfoStyle = GetWallpaperInfoStyle();
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
