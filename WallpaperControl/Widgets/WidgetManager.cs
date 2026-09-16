using System;
using System.Drawing;

namespace WallpaperControl
{
    internal sealed class WidgetManager : IDisposable
    {
        private readonly Action next;
        private readonly IcsCalendarProvider calendarProvider = new();
        private WidgetSettings settings;
        private ClockWidgetForm? clock;
        private NextWidgetForm? nextWidget;
        private SystemWidgetForm? systemWidget;
        private WeatherWidgetForm? weatherWidget;
        private CalendarWidgetForm? calendarWidget;
        private bool previewMode;

        public WidgetManager(Action next)
        {
            this.next = next;
            settings = WidgetSettings.Load();
        }

        public WidgetSettings Settings => settings.Clone();

        public void Start()
        {
            previewMode = false;
            ApplyVisualState(settings, restoreLocations: true);
        }

        public void Preview(WidgetSettings previewSettings)
        {
            previewMode = true;

            // Keep locations from the current live widget state. This allows
            // a widget to be moved while the settings dialog is open without
            // every checkbox/size change snapping it back to the old position.
            settings.ClockEnabled = previewSettings.ClockEnabled;
            settings.ClockLocked = previewSettings.ClockLocked;
            settings.ClockSize = previewSettings.ClockSize;
            settings.ClockShowSeconds = previewSettings.ClockShowSeconds;
            settings.ClockStyle = previewSettings.ClockStyle;
            settings.ClockLanguageCode = previewSettings.ClockLanguageCode;
            settings.NextEnabled = previewSettings.NextEnabled;
            settings.NextLocked = previewSettings.NextLocked;
            settings.SystemEnabled = previewSettings.SystemEnabled;
            settings.SystemLocked = previewSettings.SystemLocked;
            settings.SystemRefreshSeconds = previewSettings.SystemRefreshSeconds;
            settings.SystemStyle = previewSettings.SystemStyle;
            settings.SystemShowCpu = previewSettings.SystemShowCpu;
            settings.SystemShowRam = previewSettings.SystemShowRam;
            settings.SystemShowGpu = previewSettings.SystemShowGpu;
            settings.SystemShowVram = previewSettings.SystemShowVram;
            settings.SystemShowNetwork = previewSettings.SystemShowNetwork;
            settings.SystemShowDrives = previewSettings.SystemShowDrives;
            settings.WeatherEnabled = previewSettings.WeatherEnabled;
            settings.WeatherLocked = previewSettings.WeatherLocked;
            settings.WeatherRefreshMinutes = previewSettings.WeatherRefreshMinutes;
            settings.WeatherStyle = previewSettings.WeatherStyle;
            settings.WeatherLocationName = previewSettings.WeatherLocationName;
            settings.WeatherShowForecast = previewSettings.WeatherShowForecast;
            settings.CalendarEnabled = previewSettings.CalendarEnabled;
            settings.CalendarLocked = previewSettings.CalendarLocked;
            settings.CalendarStyle = previewSettings.CalendarStyle;
            settings.CalendarMaxEntries = previewSettings.CalendarMaxEntries;
            settings.CalendarShowLocation = previewSettings.CalendarShowLocation;
            settings.CalendarRefreshMinutes = previewSettings.CalendarRefreshMinutes;
            settings.CalendarIcsUrl = previewSettings.CalendarIcsUrl;
            settings.CalendarHolidayIcsUrl = previewSettings.CalendarHolidayIcsUrl;

            ApplyVisualState(settings, restoreLocations: false);
        }

        public void CommitPreview(WidgetSettings committedSettings)
        {
            // Preserve locations collected by the live preview. The dialog only
            // owns the enable/lock/size values; drag operations belong to the
            // widget windows themselves.
            Point clockLocation = settings.ClockLocation;
            Point nextLocation = settings.NextLocation;
            Point systemLocation = settings.SystemLocation;
            Point weatherLocation = settings.WeatherLocation;
            Point calendarLocation = settings.CalendarLocation;

            settings = committedSettings.Clone();
            settings.ClockLocation = clockLocation;
            settings.NextLocation = nextLocation;
            settings.SystemLocation = systemLocation;
            settings.WeatherLocation = weatherLocation;
            settings.CalendarLocation = calendarLocation;
            previewMode = false;
            settings.Save();

            ApplyVisualState(settings, restoreLocations: true);
        }

        public void CancelPreview(WidgetSettings originalSettings)
        {
            previewMode = false;
            settings = originalSettings.Clone();

            // Nothing was persisted during preview, so restoring the original
            // visual state is enough. This also removes widgets that were only
            // temporarily enabled in the settings dialog.
            ApplyVisualState(settings, restoreLocations: true);
        }

        private void ApplyVisualState(WidgetSettings target, bool restoreLocations)
        {
            // Preview mode keeps the widget windows interactive even though the
            // settings dialog is modal. The Lock checkboxes themselves still
            // apply immediately, so the preview always matches the current UI.
            bool effectiveClockLocked = target.ClockLocked;
            bool effectiveNextLocked = target.NextLocked;

            if (target.ClockEnabled)
            {
                if (clock == null || clock.IsDisposed)
                {
                    clock = new ClockWidgetForm(
                        target.ClockSize,
                        effectiveClockLocked,
                        target.ClockShowSeconds,
                        target.ClockStyle,
                        target.ClockLanguageCode,
                        target.ClockLocation,
                        SaveClockLocation);

                    clock.Show();

                    if (!DesktopWidgetNative.AttachToDesktop(clock, target.ClockLocation))
                    {
                        clock.Hide();
                        AppLogger.Warning(
                            "Clock widget could not be attached to the desktop.",
                            new InvalidOperationException("AttachToDesktop returned false."));
                    }
                    else if (previewMode)
                    {
                        DesktopWidgetNative.EnableInteraction(clock);
                    }
                }
                else
                {
                    clock.Apply(
                        target.ClockSize,
                        effectiveClockLocked,
                        target.ClockShowSeconds,
                        target.ClockStyle,
                        target.ClockLanguageCode);

                    if (restoreLocations)
                    {
                        clock.Location = WidgetSettings.EnsureVisible(
                            target.ClockLocation,
                            clock.Size);
                    }

                    DesktopWidgetNative.KeepOnDesktop(clock);
                    if (previewMode)
                    {
                        DesktopWidgetNative.EnableInteraction(clock);
                    }
                }
            }
            else
            {
                clock?.Close();
                clock?.Dispose();
                clock = null;
            }

            if (target.NextEnabled)
            {
                if (nextWidget == null || nextWidget.IsDisposed)
                {
                    nextWidget = new NextWidgetForm(
                        effectiveNextLocked,
                        target.ClockLanguageCode,
                        target.NextLocation,
                        next,
                        SaveNextLocation);

                    nextWidget.Show();

                    if (!DesktopWidgetNative.AttachToDesktop(nextWidget, target.NextLocation))
                    {
                        nextWidget.Hide();
                        AppLogger.Warning(
                            "Next widget could not be attached to the desktop.",
                            new InvalidOperationException("AttachToDesktop returned false."));
                    }
                    else if (previewMode)
                    {
                        DesktopWidgetNative.EnableInteraction(nextWidget);
                    }
                }
                else
                {
                    nextWidget.Apply(
                        effectiveNextLocked,
                        target.ClockLanguageCode);

                    if (restoreLocations)
                    {
                        nextWidget.Location = WidgetSettings.EnsureVisible(
                            target.NextLocation,
                            nextWidget.Size);
                    }

                    DesktopWidgetNative.KeepOnDesktop(nextWidget);
                    if (previewMode)
                    {
                        DesktopWidgetNative.EnableInteraction(nextWidget);
                    }
                }
            }
            else
            {
                nextWidget?.Close();
                nextWidget?.Dispose();
                nextWidget = null;
            }

            if (target.SystemEnabled)
            {
                if (systemWidget == null || systemWidget.IsDisposed)
                {
                    systemWidget = new SystemWidgetForm(
                        target.SystemLocked,
                        target.SystemRefreshSeconds,
                        target.SystemStyle,
                        target.SystemShowCpu,
                        target.SystemShowRam,
                        target.SystemShowGpu,
                        target.SystemShowVram,
                        target.SystemShowNetwork,
                        target.SystemShowDrives,
                        target.SystemLocation,
                        SaveSystemLocation);

                    systemWidget.Show();
                    if (!DesktopWidgetNative.AttachToDesktop(systemWidget, target.SystemLocation))
                    {
                        systemWidget.Hide();
                        AppLogger.Warning(
                            "System widget could not be attached to the desktop.",
                            new InvalidOperationException("AttachToDesktop returned false."));
                    }
                    else if (previewMode)
                    {
                        DesktopWidgetNative.EnableInteraction(systemWidget);
                    }
                }
                else
                {
                    systemWidget.Apply(
                        target.SystemLocked,
                        target.SystemRefreshSeconds,
                        target.SystemStyle,
                        target.SystemShowCpu,
                        target.SystemShowRam,
                        target.SystemShowGpu,
                        target.SystemShowVram,
                        target.SystemShowNetwork,
                        target.SystemShowDrives);
                    if (restoreLocations)
                    {
                        systemWidget.Location = WidgetSettings.EnsureVisible(target.SystemLocation, systemWidget.Size);
                    }
                    DesktopWidgetNative.KeepOnDesktop(systemWidget);
                    if (previewMode) DesktopWidgetNative.EnableInteraction(systemWidget);
                }
            }
            else
            {
                systemWidget?.Close();
                systemWidget?.Dispose();
                systemWidget = null;
            }
      
            if (target.WeatherEnabled)
            {
                if (weatherWidget == null || weatherWidget.IsDisposed)
                {
                    weatherWidget = new WeatherWidgetForm(
                        target.WeatherLocked,
                        target.WeatherRefreshMinutes,
                        target.WeatherStyle,
                        target.WeatherLocationName,
                        target.ClockLanguageCode,
                        target.WeatherShowForecast,
                        target.WeatherLocation,
                        SaveWeatherLocation);

                    weatherWidget.Show();
                    if (!DesktopWidgetNative.AttachToDesktop(weatherWidget, target.WeatherLocation))
                    {
                        weatherWidget.Hide();
                        AppLogger.Warning(
                            "Weather widget could not be attached to the desktop.",
                            new InvalidOperationException("AttachToDesktop returned false."));
                    }
                    else if (previewMode)
                    {
                        DesktopWidgetNative.EnableInteraction(weatherWidget);
                    }
                }
                else
                {
                    weatherWidget.Apply(
                        target.WeatherLocked,
                        target.WeatherRefreshMinutes,
                        target.WeatherStyle,
                        target.WeatherLocationName,
                        target.ClockLanguageCode,
                        target.WeatherShowForecast);

                    if (restoreLocations)
                    {
                        weatherWidget.Location = WidgetSettings.EnsureVisible(target.WeatherLocation, weatherWidget.Size);
                    }
                    DesktopWidgetNative.KeepOnDesktop(weatherWidget);
                    if (previewMode) DesktopWidgetNative.EnableInteraction(weatherWidget);
                }
            }
            else
            {
                weatherWidget?.Close();
                weatherWidget?.Dispose();
                weatherWidget = null;
            }

            bool calendarSourceChanged = calendarProvider.SetSources(target.CalendarIcsUrl, target.CalendarHolidayIcsUrl);

            if (target.CalendarEnabled)
            {
                if (calendarWidget == null || calendarWidget.IsDisposed)
                {
                    calendarWidget = new CalendarWidgetForm(
                        target.CalendarLocked,
                        target.CalendarStyle,
                        target.CalendarMaxEntries,
                        target.CalendarShowLocation,
                        target.CalendarRefreshMinutes,
                        target.ClockLanguageCode,
                        calendarProvider,
                        target.CalendarLocation,
                        SaveCalendarLocation);

                    calendarWidget.Show();
                    if (!DesktopWidgetNative.AttachToDesktop(calendarWidget, target.CalendarLocation))
                    {
                        calendarWidget.Hide();
                        AppLogger.Warning(
                            "Calendar widget could not be attached to the desktop.",
                            new InvalidOperationException("AttachToDesktop returned false."));
                    }
                    else if (previewMode)
                    {
                        DesktopWidgetNative.EnableInteraction(calendarWidget);
                    }
                }
                else
                {
                    calendarWidget.Apply(
                        target.CalendarLocked,
                        target.CalendarStyle,
                        target.CalendarMaxEntries,
                        target.CalendarShowLocation,
                        target.CalendarRefreshMinutes,
                        target.ClockLanguageCode);
                    if (calendarSourceChanged)
                        calendarWidget.RefreshCalendar();

                    if (restoreLocations)
                    {
                        calendarWidget.Location = WidgetSettings.EnsureVisible(target.CalendarLocation, calendarWidget.Size);
                    }
                    DesktopWidgetNative.KeepOnDesktop(calendarWidget);
                    if (previewMode) DesktopWidgetNative.EnableInteraction(calendarWidget);
                }
            }
            else
            {
                calendarWidget?.Close();
                calendarWidget?.Dispose();
                calendarWidget = null;
            }
            SetActivitySuspended(activitySuspended);
        }

        private void SaveClockLocation(Point p)
        {
            settings.ClockLocation = p;
            if (!previewMode)
            {
                settings.Save();
            }
        }

        private void SaveNextLocation(Point p)
        {
            settings.NextLocation = p;
            if (!previewMode)
            {
                settings.Save();
            }
        }

        private void SaveSystemLocation(Point p)
        {
            settings.SystemLocation = p;
            if (!previewMode)
            {
                settings.Save();
            }
        }

        private void SaveWeatherLocation(Point p)
        {
            settings.WeatherLocation = p;
            if (!previewMode)
            {
                settings.Save();
            }
        }

        private void SaveCalendarLocation(Point p)
        {
            settings.CalendarLocation = p;
            if (!previewMode)
            {
                settings.Save();
            }
        }

        private bool activitySuspended;
        internal void SetActivitySuspended(bool suspended)
        {
            activitySuspended = suspended;
            clock?.SetActivitySuspended(suspended);
            systemWidget?.SetActivitySuspended(suspended);
            weatherWidget?.SetActivitySuspended(suspended);
            calendarWidget?.SetActivitySuspended(suspended);
        }
        public void Dispose()
        {
            clock?.Close();
            clock?.Dispose();
            clock = null;

            nextWidget?.Close();
            nextWidget?.Dispose();
            nextWidget = null;

            systemWidget?.Close();
            systemWidget?.Dispose();
            systemWidget = null;

            weatherWidget?.Close();
            weatherWidget?.Dispose();
            weatherWidget = null;

            calendarWidget?.Close();
            calendarWidget?.Dispose();
            calendarWidget = null;

            calendarProvider.Dispose();
        }
    }
}
