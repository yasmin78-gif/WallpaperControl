namespace WallpaperControl;

internal sealed partial class WidgetManager
{
    private WallpaperOperatingMode wallpaperMode;

    /// <summary>Only presentation changes; user preferences and widget instances are retained.</summary>
    internal void SetWallpaperMode(WallpaperOperatingMode mode)
    {
        if (wallpaperMode == mode) return;
        wallpaperMode = mode;
        UpdateWallpaperWidget(nextWidget);
        UpdateWallpaperWidget(wallpaperInfoWidget);
        if (mode == WallpaperOperatingMode.ImageSlideshow) RefreshWallpaperInfo();
    }

    private bool ModeAllowsWidget(Form widget) => wallpaperMode == WallpaperOperatingMode.ImageSlideshow
        || widget is not NextWidgetForm and not WallpaperInfoWidgetForm;

    private void UpdateWallpaperWidget(Form? widget)
    {
        if (widget == null || widget.IsDisposed) return;
        ShowWallpaperWidget(widget, widget.Location);
    }

    /// <summary>Shared by initial creation, settings preview and mode restoration.</summary>
    private bool ShowWallpaperWidget(Form widget, Point location)
    {
        if (!ModeAllowsWidget(widget)) { widget.Hide(); return true; }
        if (widget.Visible) return DesktopWidgetNative.KeepOnDesktop(widget);
        widget.Show();
        bool attached = DesktopWidgetNative.AttachToDesktop(widget, location);
        if (!attached) widget.Hide();
        else if (previewMode) DesktopWidgetNative.EnableInteraction(widget);
        return attached;
    }
}
