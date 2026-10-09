namespace WallpaperControl;
internal sealed partial class WidgetSettingsEditor
{
    private readonly CheckBox clipboardEnabled=new(),clipboardLocked=new();
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
        AddWallpaperInfoControl(layout,new Label { MaximumSize=new(540,0) },"ClipboardHint");
        LoadClipboardControls(initialWidgetSettings);
        clipboardEnabled.CheckedChanged+=(_,_)=>NotifyWidgetPreviewChanged(); clipboardLocked.CheckedChanged+=(_,_)=>NotifyWidgetPreviewChanged();
        clipboardStyle.SelectedIndexChanged+=(_,_)=>NotifyWidgetPreviewChanged(); clipboardCapacity.SelectedIndexChanged+=(_,_)=>NotifyWidgetPreviewChanged(); clipboardHeight.ValueChanged+=(_,_)=>NotifyWidgetPreviewChanged();
    }
    private void LoadClipboardControls(WidgetSettings value)
    {
        clipboardEnabled.Checked=value.Clipboard.Enabled; clipboardLocked.Checked=value.Clipboard.Locked;
        clipboardHeight.Value=CalendarViewport.NormalizeMaximum(value.Clipboard.MaximumHeight);
        clipboardCapacity.SelectedItem=ClipboardWidgetSettings.Capacities.Contains(value.Clipboard.Capacity)?value.Clipboard.Capacity:10;
        RefreshWidgetStyleChoices(clipboardStyle,value.Clipboard.Style);
    }
}
