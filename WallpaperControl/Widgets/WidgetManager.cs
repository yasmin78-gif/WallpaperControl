using System;
using System.Collections.Generic;
using System.Drawing;

namespace WallpaperControl
{
    internal sealed class WidgetManager : IDisposable
    {
        private readonly string registryPath;
        private readonly NotesStore notesStore;
        private NotesWidgetForm? notesWidget;
        private bool notesDialogOpen;
        private readonly Action next;
        private readonly Func<bool, WallpaperInfoSnapshot>? wallpaperInfoSource;
        private WallpaperInfoWidgetForm? wallpaperInfoWidget;
        private readonly IcsCalendarProvider calendarProvider = new();
        private readonly DesktopShowMonitor desktopShowMonitor;
        private readonly HashSet<Form> desktopWidgets = new();
        private WidgetSettings settings;
        private ClockWidgetForm? clock;
        private NextWidgetForm? nextWidget;
        private SystemWidgetForm? systemWidget;
        private WeatherWidgetForm? weatherWidget;
        private CalendarWidgetForm? calendarWidget;
        private bool previewMode;
        private WebWidgetForm? webWidget;

        /// <summary>
        /// Loads widget preferences and stores the callback used by the next-wallpaper widget.
        /// </summary>
        /// <param name="next">The callback that requests the next wallpaper.</param>
        public WidgetManager(Action next, Func<bool, WallpaperInfoSnapshot>? wallpaperInfoSource = null,
            string registryPath = WidgetSettings.RegistryPath, NotesStore? notesStore = null)
        {
            this.next = next;
            this.wallpaperInfoSource = wallpaperInfoSource;
            this.registryPath = registryPath;
            this.notesStore = notesStore ?? new NotesStore();
            settings = WidgetSettings.Load(registryPath);
            desktopShowMonitor = new DesktopShowMonitor(RestoreDesktopWidgetBand);
        }

        public WidgetSettings Settings => settings.Clone();

        internal void SetEditing(bool active) => previewMode = active;

        /// <summary>
        /// Creates or updates widgets from saved preferences outside settings-preview mode.
        /// </summary>
        public void Start()
        {
            previewMode = false;
            ApplyVisualState(settings, restoreLocations: true);
        }

        /// <summary>
        /// Applies uncommitted widget preferences while preserving positions changed during the live preview.
        /// </summary>
        /// <param name="previewSettings">The uncommitted widget preferences to preview.</param>
        public void Preview(WidgetSettings previewSettings)
        {
            previewMode = true;

            // Keep locations from the current live widget state. This allows
            // a widget to be moved while the settings dialog is open without
            // every checkbox/size change snapping it back to the old position.
            WebWidgetSettings webPreview = previewSettings.Web.Clone();
            webPreview.CopyGeometry(settings.Web);
            settings.Web = webPreview;
            settings.NotesEnabled = previewSettings.NotesEnabled;
            settings.NotesLocked = previewSettings.NotesLocked;
            settings.NotesMaximumHeight = previewSettings.NotesMaximumHeight;
            settings.NotesStyle = previewSettings.NotesStyle;
            settings.WallpaperInfoFontSize = previewSettings.WallpaperInfoFontSize;
            settings.ClockEnabled = previewSettings.ClockEnabled;
            settings.ClockLocked = previewSettings.ClockLocked;
            settings.ClockSize = previewSettings.ClockSize;
            settings.ClockShowSeconds = previewSettings.ClockShowSeconds;
            settings.ClockStyle = previewSettings.ClockStyle;
            settings.ClockLanguageCode = previewSettings.ClockLanguageCode;
            settings.WallpaperInfoEnabled = previewSettings.WallpaperInfoEnabled;
            settings.WallpaperInfoLocked = previewSettings.WallpaperInfoLocked;
            settings.WallpaperInfoShowAdvanced = previewSettings.WallpaperInfoShowAdvanced;
            settings.WallpaperInfoShowExtension = previewSettings.WallpaperInfoShowExtension;
            settings.WallpaperInfoHiddenSuffix = previewSettings.WallpaperInfoHiddenSuffix;
            settings.WallpaperInfoStyle = previewSettings.WallpaperInfoStyle;
            settings.NextEnabled = previewSettings.NextEnabled;
            settings.NextLocked = previewSettings.NextLocked;
            settings.NextStyle = previewSettings.NextStyle;
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
            settings.CalendarMaximumHeight = previewSettings.CalendarMaximumHeight;
            settings.CalendarMaxEntries = previewSettings.CalendarMaxEntries;
            settings.CalendarShowLocation = previewSettings.CalendarShowLocation;
            settings.CalendarRefreshMinutes = previewSettings.CalendarRefreshMinutes;
            settings.CalendarSources = new(previewSettings.CalendarSources);

            ApplyVisualState(settings, restoreLocations: false);
        }

        /// <summary>
        /// Persists accepted widget preferences while retaining positions collected during live preview.
        /// </summary>
        /// <param name="committedSettings">The accepted widget preferences to persist.</param>
        public void CommitPreview(WidgetSettings committedSettings)
        {
            // Preserve locations collected by the live preview. The dialog only
            // owns the enable/lock/size values; drag operations belong to the
            // widget windows themselves.
            WebWidgetSettings webGeometry = settings.Web.Clone();
            Point notesLocation = settings.NotesLocation;
            Point clockLocation = settings.ClockLocation;
            Point infoLocation = settings.WallpaperInfoLocation;
            Point nextLocation = settings.NextLocation;
            Point systemLocation = settings.SystemLocation;
            Point weatherLocation = settings.WeatherLocation;
            Point calendarLocation = settings.CalendarLocation;

            settings = committedSettings.Clone();
            settings.Web.CopyGeometry(webGeometry);
            settings.NotesLocation = notesLocation;
            settings.ClockLocation = clockLocation;
            settings.WallpaperInfoLocation = infoLocation;
            settings.NextLocation = nextLocation;
            settings.SystemLocation = systemLocation;
            settings.WeatherLocation = weatherLocation;
            settings.CalendarLocation = calendarLocation;
            previewMode = false;
            settings.Save(registryPath);

            ApplyVisualState(settings, restoreLocations: true);
        }

        /// <summary>
        /// Restores the original widget settings and removes widgets enabled only for preview.
        /// </summary>
        /// <param name="originalSettings">The preferences to restore when preview is canceled.</param>
        public void CancelPreview(WidgetSettings originalSettings)
        {
            previewMode = false;
            settings = originalSettings.Clone();

            // Nothing was persisted during preview, so restoring the original
            // visual state is enough. This also removes widgets that were only
            // temporarily enabled in the settings dialog.
            ApplyVisualState(settings, restoreLocations: true);
        }

        /// <summary>
        /// Creates, updates, or disposes widget windows to match the requested settings and preview mode.
        /// </summary>
        /// <param name="target">The settings instance that receives the current widget positions and visual state.</param>
        /// <param name="restoreLocations">True to restore saved positions as well as visual preferences.</param>
        private void ApplyVisualState(WidgetSettings target, bool restoreLocations)
        {
            // Preview mode keeps the widget windows interactive even though the
            // settings dialog is modal. The Lock checkboxes themselves still
            // apply immediately, so the preview always matches the current UI.
            ApplyWebWidget(target, restoreLocations);
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

                    RegisterDesktopWidget(clock);
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
                        target.NextStyle,
                        target.ClockLanguageCode,
                        target.NextLocation,
                        next,
                        SaveNextLocation);

                    RegisterDesktopWidget(nextWidget);
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
                        target.NextStyle,
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

                    RegisterDesktopWidget(systemWidget);
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

                    RegisterDesktopWidget(weatherWidget);
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

            bool calendarSourceChanged = calendarProvider.SetSources(target.CalendarSources);

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
                        SaveCalendarLocation,
                        target.CalendarMaximumHeight);

                    RegisterDesktopWidget(calendarWidget);
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
                        target.ClockLanguageCode,
                        target.CalendarMaximumHeight);
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
            if (target.WallpaperInfoEnabled)
            {
                if (wallpaperInfoWidget == null || wallpaperInfoWidget.IsDisposed)
                {
                    wallpaperInfoWidget = new WallpaperInfoWidgetForm(target, p =>
                    {
                        settings.WallpaperInfoLocation = p;
                        if (!previewMode) settings.Save(registryPath);
                    });
                    wallpaperInfoWidget.SetActivitySuspended(activitySuspended);
                    RegisterDesktopWidget(wallpaperInfoWidget);
                    wallpaperInfoWidget.Show();
                    if (!DesktopWidgetNative.AttachToDesktop(wallpaperInfoWidget, target.WallpaperInfoLocation))
                    {
                        wallpaperInfoWidget.Hide();
                        AppLogger.Warning("Wallpaper info widget could not be attached to the desktop.",
                            new InvalidOperationException("AttachToDesktop returned false."));
                    }
                    else if (previewMode) DesktopWidgetNative.EnableInteraction(wallpaperInfoWidget);
                }
                else
                {
                    wallpaperInfoWidget.Apply(target);
                    if (restoreLocations)
                        wallpaperInfoWidget.Location = WidgetSettings.EnsureVisible(target.WallpaperInfoLocation, wallpaperInfoWidget.Size);
                    DesktopWidgetNative.KeepOnDesktop(wallpaperInfoWidget);
                    if (previewMode) DesktopWidgetNative.EnableInteraction(wallpaperInfoWidget);
                }
                RefreshWallpaperInfo();
            }
            else
            {
                wallpaperInfoWidget?.Close();
                wallpaperInfoWidget?.Dispose();
                wallpaperInfoWidget = null;
            }
            if (target.NotesEnabled)
            {
                if (notesWidget == null || notesWidget.IsDisposed)
                {
                    notesWidget = new NotesWidgetForm(notesStore, target, p =>
                    {
                        settings.NotesLocation = p;
                        if (!previewMode) settings.Save(registryPath);
                    }, EditNote);
                    notesWidget.SetActivitySuspended(activitySuspended);
                    RegisterDesktopWidget(notesWidget);
                    notesWidget.Show();
                    if (!DesktopWidgetNative.AttachToDesktop(notesWidget, target.NotesLocation))
                    {
                        notesWidget.Hide();
                        AppLogger.Warning("Notes widget could not be attached to the desktop.", new InvalidOperationException());
                    }
                    else if (previewMode) DesktopWidgetNative.EnableInteraction(notesWidget);
                }
                else
                {
                    notesWidget.Apply(target);
                    if (restoreLocations) notesWidget.Location = WidgetSettings.EnsureVisible(target.NotesLocation, notesWidget.Size);
                    DesktopWidgetNative.KeepOnDesktop(notesWidget);
                    if (previewMode) DesktopWidgetNative.EnableInteraction(notesWidget);
                }
            }
            else { notesWidget?.Close(); notesWidget?.Dispose(); notesWidget = null; }
            SetActivitySuspended(activitySuspended);
        }

        /// <summary>
        /// Stores and persists the clock widget&apos;s position according to the current preview state.
        /// </summary>
        /// <param name="p">The window location in screen coordinates.</param>
        private void SaveClockLocation(Point p)
        {
            settings.ClockLocation = p;
            if (!previewMode)
            {
                settings.Save(registryPath);
            }
        }

        /// <summary>
        /// Stores and persists the next-wallpaper widget&apos;s position according to the current preview state.
        /// </summary>
        /// <param name="p">The window location in screen coordinates.</param>
        private void SaveNextLocation(Point p)
        {
            settings.NextLocation = p;
            if (!previewMode)
            {
                settings.Save(registryPath);
            }
        }

        /// <summary>
        /// Stores and persists the system widget&apos;s position according to the current preview state.
        /// </summary>
        /// <param name="p">The window location in screen coordinates.</param>
        private void SaveSystemLocation(Point p)
        {
            settings.SystemLocation = p;
            if (!previewMode)
            {
                settings.Save(registryPath);
            }
        }

        /// <summary>
        /// Stores and persists the weather widget&apos;s position according to the current preview state.
        /// </summary>
        /// <param name="p">The window location in screen coordinates.</param>
        private void SaveWeatherLocation(Point p)
        {
            settings.WeatherLocation = p;
            if (!previewMode)
            {
                settings.Save(registryPath);
            }
        }

        /// <summary>
        /// Stores and persists the calendar widget&apos;s position according to the current preview state.
        /// </summary>
        /// <param name="p">The window location in screen coordinates.</param>
        private void SaveCalendarLocation(Point p)
        {
            settings.CalendarLocation = p;
            if (!previewMode)
            {
                settings.Save(registryPath);
            }
        }

        /// <summary>
        /// Restores all active widget windows to the desktop Z-order band after Windows Show Desktop changes it.
        /// </summary>
        private void RestoreDesktopWidgetBand()
        {
            if (activitySuspended)
                return;

            foreach (Form widget in desktopWidgets)
            {
                RestoreDesktopWidget(widget);
            }
        }

        /// <summary>
        /// Registers a desktop widget for shared shell/Z-order handling. Future widget types only need
        /// to register here when their window is created; closed widgets remove themselves automatically.
        /// </summary>
        private void RegisterDesktopWidget(Form widget)
        {
            if (!desktopWidgets.Add(widget))
                return;

            widget.FormClosed += DesktopWidget_FormClosed;
        }

        /// <summary>
        /// Removes closed widgets from the active desktop-widget collection.
        /// </summary>
        private void DesktopWidget_FormClosed(object? sender, FormClosedEventArgs e)
        {
            if (sender is not Form widget)
                return;

            widget.FormClosed -= DesktopWidget_FormClosed;
            desktopWidgets.Remove(widget);
        }

        /// <summary>
        /// Restores one visible widget without changing its parent, owner, position, or rendering model.
        /// </summary>
        /// <param name="widget">The widget to restore when it is currently available.</param>
        private static void RestoreDesktopWidget(Form? widget)
        {
            if (widget == null || widget.IsDisposed || !widget.IsHandleCreated || !widget.Visible)
                return;

            DesktopWidgetNative.KeepOnDesktop(widget);
        }

        /// <summary>Reads a coherent UI-thread snapshot only for an active, unsuspended widget.</summary>
        internal void RefreshWallpaperInfo()
        {
            if (activitySuspended || wallpaperInfoWidget == null || wallpaperInfoWidget.IsDisposed) return;
            wallpaperInfoWidget.SetData(wallpaperInfoSource?.Invoke(settings.WallpaperInfoShowAdvanced) ?? default);
        }

        private void EditNote(NoteEntry? entry)
        {
            if (notesDialogOpen) return;
            notesDialogOpen = true;
            try { using NoteEditorForm editor = new(notesStore, entry, settings.ClockLanguageCode, settings.NotesStyle); editor.ShowDialog(); }
            finally { notesDialogOpen = false; }
        }

        internal void ShowNotesManager(IWin32Window owner, string language)
        {
            using NotesManagerForm manager = new(notesStore, language, settings.NotesStyle);
            manager.ShowDialog(owner);
        }

        internal void RefreshWebTheme(bool dark) => webWidget?.ApplyTheme(dark);

        private void ApplyWebWidget(WidgetSettings target, bool restoreLocations)
        {
            if (!target.Web.Enabled)
            {
                webWidget?.Close(); webWidget?.Dispose(); webWidget = null; return;
            }
            if (webWidget == null || webWidget.IsDisposed)
            {
                webWidget = new WebWidgetForm(target.Web, target.ClockLanguageCode);
                webWidget.GeometrySettled += (_, _) =>
                {
                    if (webWidget == null) return;
                    settings.Web.CopyGeometry(webWidget.Configuration);
                    if (!previewMode) settings.Save(registryPath);
                };
                RegisterDesktopWidget(webWidget);
                webWidget.SetActivitySuspended(activitySuspended);
                webWidget.Show();
            }
            else webWidget.ApplyConfiguration(target.Web, target.ClockLanguageCode, restoreLocations);
            webWidget.SetActivitySuspended(activitySuspended);
            DesktopWidgetNative.EnableInteraction(webWidget);
        }

        private bool activitySuspended;
        /// <summary>
        /// Forwards automatic suspension to the widgets that perform periodic background work.
        /// </summary>
        /// <param name="suspended">True to pause background activity; false to resume it.</param>
        internal void SetActivitySuspended(bool suspended)
        {
            bool resumed = activitySuspended && !suspended;
            activitySuspended = suspended;
            webWidget?.SetActivitySuspended(suspended);
            wallpaperInfoWidget?.SetActivitySuspended(suspended);
            if (resumed) RefreshWallpaperInfo();
            notesWidget?.SetActivitySuspended(suspended);
            clock?.SetActivitySuspended(suspended);
            systemWidget?.SetActivitySuspended(suspended);
            weatherWidget?.SetActivitySuspended(suspended);
            calendarWidget?.SetActivitySuspended(suspended);
            if (resumed)
                desktopShowMonitor.RepairAfterResume();
        }
        /// <summary>
        /// Closes widget windows and releases their services and calendar provider.
        /// </summary>
        public void Dispose()
        {
            desktopShowMonitor.Dispose();
            webWidget?.Close(); webWidget?.Dispose(); webWidget = null;

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

            wallpaperInfoWidget?.Close();
            wallpaperInfoWidget?.Dispose();
            wallpaperInfoWidget = null;

            notesWidget?.Close();
            notesWidget?.Dispose();
            notesWidget = null;
            desktopWidgets.Clear();
            calendarProvider.Dispose();
        }
    }
}
