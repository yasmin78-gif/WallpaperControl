namespace WallpaperControl;
internal sealed partial class WidgetManager
{
    private ClipboardService? clipboardService;
    private ClipboardWidgetForm? clipboardWidget;
    private bool clipboardStartupChecked;
    private Task? clipboardStartupCleanup;
    private readonly System.Windows.Forms.Timer clipboardImageMaintenance=new() { Interval=60_000 };
    private void MaintainDisabledClipboardImages()
    {
        if(clipboardService==null&&(clipboardStartupCleanup==null||clipboardStartupCleanup.IsCompleted))
            clipboardStartupCleanup=CleanStartupScreenshots(WidgetSettings.Load(registryPath).Clipboard);
    }
    private void DisposeClipboardMaintenance()=>clipboardImageMaintenance.Dispose();
    private string ClipboardImageDirectory=>registryPath==WidgetSettings.RegistryPath?ClipboardScreenshotStore.DefaultDirectory:
        Path.Combine(Path.GetTempPath(),"WallpaperControl-ClipboardTests",Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(registryPath))));
    private async Task CleanStartupScreenshots(ClipboardWidgetSettings options)
    {
        using var store=new ClipboardScreenshotStore(ClipboardImageDirectory);
        try { using var snapshot=await store.InitializeAsync(options,false); }
        catch(Exception ex) { ClipboardDiagnostics.Failure("startup cleanup",ex); }
    }
    private void ApplyClipboardWidget(WidgetSettings target,bool restoreLocations)
    {
        if(!clipboardStartupChecked) {
            clipboardStartupChecked=true;
            clipboardImageMaintenance.Tick+=(_,_)=>MaintainDisabledClipboardImages(); clipboardImageMaintenance.Start();
            if(!target.Clipboard.Enabled)clipboardStartupCleanup=CleanStartupScreenshots(WidgetSettings.Load(registryPath).Clipboard);
        }
        if(!target.Clipboard.Enabled) { DisposeClipboard(); return; }
        if(clipboardService==null)
        {
            try { clipboardService=new(new ClipboardListener(),new ClipboardScreenshotStore(ClipboardImageDirectory),previewMode?WidgetSettings.Load(registryPath).Clipboard:target.Clipboard,clipboardStartupCleanup); }
            catch(Exception ex) { ClipboardDiagnostics.Failure("listener/service initialization",ex); return; }
        }
        clipboardService.Configure(target.Clipboard,!previewMode);
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
