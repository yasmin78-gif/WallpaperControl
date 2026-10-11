namespace WallpaperControl;
internal sealed partial class WidgetSettingsEditor
{
    private readonly CheckBox clipboardEnabled=new(),clipboardLocked=new(),clipboardCaptureScreenshots=new(),clipboardKeepScreenshots=new();
    private readonly ComboBox clipboardScreenshotLifetime=new() { DropDownStyle=ComboBoxStyle.DropDownList,Width=280 },clipboardScreenshotCapacity=new() { DropDownStyle=ComboBoxStyle.DropDownList,Width=100 };
    private readonly ComboBox clipboardStyle=new() { DropDownStyle=ComboBoxStyle.DropDownList,Width=280 },clipboardCapacity=new() { DropDownStyle=ComboBoxStyle.DropDownList,Width=100 };
    private readonly NumericUpDown clipboardHeight=new() { Minimum=300,Maximum=1400,Increment=50,Width=100 };
    private void InitializeClipboardPage(TabPage page)
    {
        var layout=new FlowLayoutPanel { AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Location=new(18,18) }; page.Controls.Add(layout);
        AddWallpaperInfoControl(layout,new Label { Font=CreateOwnedFont("Segoe UI",12,FontStyle.Bold) },"ClipboardTitle");
        AddWallpaperInfoControl(layout,clipboardEnabled,"ClipboardEnabled"); AddWallpaperInfoControl(layout,clipboardLocked,"SettingsWidgetLocked");
        AddWallpaperInfoControl(layout,new Label(),"SettingsSystemStyle"); layout.Controls.Add(clipboardStyle);
        AddWallpaperInfoControl(layout,new Label(),"ClipboardCapacity"); clipboardCapacity.Items.AddRange(ClipboardWidgetSettings.Capacities.Cast<object>().ToArray()); layout.Controls.Add(clipboardCapacity);
        AddWallpaperInfoControl(layout,new Label(),"SettingsCalendarMaximumHeight"); layout.Controls.Add(clipboardHeight);
        AddWallpaperInfoControl(layout,clipboardCaptureScreenshots,"ClipboardCaptureScreenshots");
        AddWallpaperInfoControl(layout,new Label(),"ClipboardScreenshotLifetime"); layout.Controls.Add(clipboardScreenshotLifetime);
        AddWallpaperInfoControl(layout,new Label(),"ClipboardScreenshotCapacity"); clipboardScreenshotCapacity.Items.AddRange(ClipboardWidgetSettings.ScreenshotCapacities.Cast<object>().ToArray()); layout.Controls.Add(clipboardScreenshotCapacity);
        AddWallpaperInfoControl(layout,clipboardKeepScreenshots,"ClipboardKeepScreenshots");
        AddWallpaperInfoControl(layout,new Label { MaximumSize=new(540,0) },"ClipboardHint");
        LoadClipboardControls(initialWidgetSettings);
        clipboardCaptureScreenshots.CheckedChanged+=(_,_)=>NotifyWidgetPreviewChanged(); clipboardKeepScreenshots.CheckedChanged+=(_,_)=>NotifyWidgetPreviewChanged();
        clipboardScreenshotLifetime.SelectedIndexChanged+=(_,_)=>NotifyWidgetPreviewChanged(); clipboardScreenshotCapacity.SelectedIndexChanged+=(_,_)=>NotifyWidgetPreviewChanged();
        clipboardEnabled.CheckedChanged+=(_,_)=>NotifyWidgetPreviewChanged(); clipboardLocked.CheckedChanged+=(_,_)=>NotifyWidgetPreviewChanged();
        clipboardStyle.SelectedIndexChanged+=(_,_)=>NotifyWidgetPreviewChanged(); clipboardCapacity.SelectedIndexChanged+=(_,_)=>NotifyWidgetPreviewChanged(); clipboardHeight.ValueChanged+=(_,_)=>NotifyWidgetPreviewChanged();
    }
    private void RefreshClipboardLifetime(int? hours=null)
    {
        int index=hours.HasValue?Array.IndexOf(ClipboardWidgetSettings.ScreenshotLifetimes,hours.Value):clipboardScreenshotLifetime.SelectedIndex;
        clipboardScreenshotLifetime.Items.Clear();
        foreach(string key in new[]{"ClipboardLifetimeHour","ClipboardLifetimeDay","ClipboardLifetimeWeek","ClipboardLifetimeManual"})clipboardScreenshotLifetime.Items.Add(Localization.Get(key,previewLanguageCode));
        clipboardScreenshotLifetime.SelectedIndex=index>=0?index:1;
    }
    private void LoadClipboardControls(WidgetSettings value)
    {
        clipboardCaptureScreenshots.Checked=value.Clipboard.CaptureScreenshots; clipboardKeepScreenshots.Checked=value.Clipboard.KeepScreenshots;
        RefreshClipboardLifetime(value.Clipboard.ScreenshotLifetimeHours);
        clipboardScreenshotCapacity.SelectedItem=value.Clipboard.ScreenshotCapacity;
        clipboardEnabled.Checked=value.Clipboard.Enabled; clipboardLocked.Checked=value.Clipboard.Locked;
        clipboardHeight.Value=CalendarViewport.NormalizeMaximum(value.Clipboard.MaximumHeight);
        clipboardCapacity.SelectedItem=ClipboardWidgetSettings.Capacities.Contains(value.Clipboard.Capacity)?value.Clipboard.Capacity:10;
        RefreshWidgetStyleChoices(clipboardStyle,value.Clipboard.Style);
    }
}
