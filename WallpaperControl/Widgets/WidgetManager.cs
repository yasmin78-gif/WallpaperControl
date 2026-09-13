using System;
using System.Drawing;

namespace WallpaperControl
{
    internal sealed class WidgetManager : IDisposable
    {
        private readonly Action next;
        private WidgetSettings settings;
        private ClockWidgetForm? clock;
        private NextWidgetForm? nextWidget;
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
            settings.ClockLanguageCode = previewSettings.ClockLanguageCode;
            settings.NextEnabled = previewSettings.NextEnabled;
            settings.NextLocked = previewSettings.NextLocked;

            ApplyVisualState(settings, restoreLocations: false);
        }

        public void CommitPreview(WidgetSettings committedSettings)
        {
            // Preserve locations collected by the live preview. The dialog only
            // owns the enable/lock/size values; drag operations belong to the
            // widget windows themselves.
            Point clockLocation = settings.ClockLocation;
            Point nextLocation = settings.NextLocation;

            settings = committedSettings.Clone();
            settings.ClockLocation = clockLocation;
            settings.NextLocation = nextLocation;
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

        public void Dispose()
        {
            clock?.Close();
            clock?.Dispose();
            clock = null;

            nextWidget?.Close();
            nextWidget?.Dispose();
            nextWidget = null;
        }
    }
}
