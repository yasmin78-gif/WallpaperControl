namespace WallpaperControl;

internal sealed partial class WidgetManager
{
    private VisitorModeService? visitorService;
    private VisitorModeWidgetForm? visitorWidget;
    private bool visitorWidgetsStarted;

    internal void ConfigureVisitorMode(VisitorModeService service)
    {
        if (visitorService != null) visitorService.Changed -= VisitorStateChanged;
        visitorService = service;
        service.Changed += VisitorStateChanged;
    }
    private void VisitorStateChanged() { UpdateVisitorWidget(); UpdateWallpaperWidget(nextWidget); }
    private void UpdateVisitorWidget(bool restoreLocations = false)
    {
        if (!visitorWidgetsStarted || visitorService == null) return;
        if (!settings.Visitor.Enabled)
        { visitorWidget?.Close(); visitorWidget?.Dispose(); visitorWidget = null; return; }
        if (visitorWidget == null)
        {
            visitorWidget = new(visitorService, point =>
            {
                settings.Visitor.Location = point;
                if (!previewMode) settings.Save(registryPath);
            });
            visitorWidget.Apply(settings.Visitor, settings.ClockLanguageCode);
            visitorWidget.Location = WidgetSettings.EnsureVisible(settings.Visitor.Location, visitorWidget.Size);
            RegisterDesktopWidget(visitorWidget);
        }
        visitorWidget.Apply(settings.Visitor, settings.ClockLanguageCode);
        if (restoreLocations) visitorWidget.Location = WidgetSettings.EnsureVisible(settings.Visitor.Location, visitorWidget.Size);
        visitorWidget.RefreshState();
        ShowWallpaperWidget(visitorWidget, visitorWidget.Location);
    }
    private void DisposeVisitorWidget()
    {
        if (visitorService != null) visitorService.Changed -= VisitorStateChanged;
        visitorService = null;
        visitorWidget?.Close(); visitorWidget?.Dispose(); visitorWidget = null;
    }
}
