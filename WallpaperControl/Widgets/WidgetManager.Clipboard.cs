namespace WallpaperControl;
internal sealed partial class WidgetManager
{
    private ClipboardService? clipboardService;
    private ClipboardWidgetForm? clipboardWidget;
    private void ApplyClipboardWidget(WidgetSettings target,bool restoreLocations)
    {
        if(!target.Clipboard.Enabled) { DisposeClipboard(); return; }
        if(clipboardService==null)
        {
            try { clipboardService=new(new ClipboardListener()); }
            catch(Exception ex) { AppLogger.Info($"Clipboard listener unavailable: type={ex.GetType().Name}"); return; }
        }
        clipboardService.Configure(target.Clipboard.Capacity);
        if(clipboardWidget==null||clipboardWidget.IsDisposed)
        {
            clipboardWidget=new(clipboardService,target.Clipboard,target.ClockLanguageCode,
                point=> { settings.Clipboard.Location=point; if(!previewMode)settings.Save(registryPath); });
            clipboardWidget.SetActivitySuspended(activitySuspended); clipboardWidget.SetPowerSuspended(packagePowerSuspended);
            RegisterDesktopWidget(clipboardWidget); clipboardWidget.Show();
            if(!DesktopWidgetNative.AttachToDesktop(clipboardWidget,target.Clipboard.Location)) { clipboardWidget.Hide(); AppLogger.Info("Clipboard desktop attachment failed"); }
        }
        else
        {
            clipboardWidget.Apply(target.Clipboard,target.ClockLanguageCode);
            if(restoreLocations)clipboardWidget.Location=WidgetSettings.EnsureVisible(target.Clipboard.Location,clipboardWidget.Size);
            DesktopWidgetNative.KeepOnDesktop(clipboardWidget);
        }
        if(previewMode)DesktopWidgetNative.EnableInteraction(clipboardWidget);
    }
    private void DisposeClipboard() { clipboardWidget?.Close(); clipboardWidget?.Dispose(); clipboardWidget=null; clipboardService?.Dispose(); clipboardService=null; }
}
