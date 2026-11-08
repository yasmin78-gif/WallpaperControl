namespace WallpaperControl;

internal sealed partial class WidgetManager
{
    private VisitorModeService? visitorService;
    private VisitorModeWidgetForm? visitorWidget;
    private bool visitorWidgetsStarted;

    internal void ConfigureVisitorMode(VisitorModeService service)
    {
        if (visitorService != null) visitorService.Changed -= UpdateVisitorWidget;
        visitorService = service;
        service.Changed += UpdateVisitorWidget;
    }
    private void UpdateVisitorWidget()
    {
        if (!visitorWidgetsStarted || visitorService == null) return;
        if (!visitorService.Settings.WidgetEnabled)
        { visitorWidget?.Close(); visitorWidget?.Dispose(); visitorWidget = null; return; }
        if (visitorWidget == null)
        {
            visitorWidget = new(visitorService, point =>
            {
                _ = visitorService.ConfigureAsync(s => s with { WidgetX = point.X, WidgetY = point.Y }, reloadMedia: false);
            });
            visitorWidget.Location = WidgetSettings.EnsureVisible(new(visitorService.Settings.WidgetX, visitorService.Settings.WidgetY), visitorWidget.Size);
            RegisterDesktopWidget(visitorWidget);
        }
        visitorWidget.RefreshState();
        ShowWallpaperWidget(visitorWidget, visitorWidget.Location);
    }
    private void DisposeVisitorWidget()
    {
        if (visitorService != null) visitorService.Changed -= UpdateVisitorWidget;
        visitorService = null;
        visitorWidget?.Close(); visitorWidget?.Dispose(); visitorWidget = null;
    }
}
