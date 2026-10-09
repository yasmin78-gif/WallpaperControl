namespace WallpaperControl;
internal sealed partial class WidgetSettingsEditor
{
    private readonly CheckBox launcherEnabled=new(),launcherLocked=new(),launcherNames=new();
    private readonly ComboBox launcherStyle=new() { DropDownStyle=ComboBoxStyle.DropDownList,Width=280 };
    private readonly NumericUpDown launcherColumns=new() { Minimum=2,Maximum=6,Width=100 },launcherHeight=new() { Minimum=300,Maximum=1400,Increment=50,Width=100 };
    private LauncherEntryList launcherList=null!;
    private List<LauncherEntry> launcherEntries=[];
    private Func<List<LauncherEntry>>? launcherRead;
    internal void ConfigureLauncher(Func<List<LauncherEntry>> read,Action<List<LauncherEntry>> save) { launcherRead=read; launcherList.Configure(read,save); }
    internal void RefreshLauncherEntries()=>launcherList.RefreshEntries();
    private void InitializeLauncherPage(TabPage page)
    {
        var layout=new FlowLayoutPanel { AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Location=new(18,18) }; page.Controls.Add(layout);
        AddWallpaperInfoControl(layout,new Label { Font=CreateOwnedFont("Segoe UI",12,FontStyle.Bold) },"LauncherTitle");
        AddWallpaperInfoControl(layout,launcherEnabled,"LauncherEnabled"); AddWallpaperInfoControl(layout,launcherLocked,"SettingsWidgetLocked");
        AddWallpaperInfoControl(layout,new Label(),"SettingsSystemStyle"); layout.Controls.Add(launcherStyle);
        AddWallpaperInfoControl(layout,launcherNames,"LauncherNames"); AddWallpaperInfoControl(layout,new Label(),"LauncherColumns"); layout.Controls.Add(launcherColumns);
        AddWallpaperInfoControl(layout,new Label(),"SettingsCalendarMaximumHeight"); layout.Controls.Add(launcherHeight);
        AddWallpaperInfoControl(layout,new Label(),"LauncherEntries");
        launcherList=new(()=>launcherEntries.ToList(),value=> { launcherEntries=value.ToList(); NotifyWidgetPreviewChanged(); },previewLanguageCode,SystemWidgetStyle.Glow,darkMode); layout.Controls.Add(launcherList);
        AddWallpaperInfoControl(layout,new Label { MaximumSize=new(550,0) },"LauncherHint"); LoadLauncherControls(initialWidgetSettings);
        launcherEnabled.CheckedChanged+=(_,_)=>NotifyWidgetPreviewChanged(); launcherLocked.CheckedChanged+=(_,_)=>NotifyWidgetPreviewChanged(); launcherNames.CheckedChanged+=(_,_)=>NotifyWidgetPreviewChanged();
        launcherColumns.ValueChanged+=(_,_)=>NotifyWidgetPreviewChanged(); launcherHeight.ValueChanged+=(_,_)=>NotifyWidgetPreviewChanged(); launcherStyle.SelectedIndexChanged+=(_,_)=> { launcherList.ApplyPresentation(previewLanguageCode,(SystemWidgetStyle)Math.Max(0,launcherStyle.SelectedIndex),darkMode); NotifyWidgetPreviewChanged(); };
    }
    private void LoadLauncherControls(WidgetSettings value)
    {
        launcherEnabled.Checked=value.Launcher.Enabled; launcherLocked.Checked=value.Launcher.Locked; launcherNames.Checked=value.Launcher.ShowNames;
        launcherColumns.Value=Math.Clamp(value.Launcher.Columns,2,6); launcherHeight.Value=CalendarViewport.NormalizeMaximum(value.Launcher.MaximumHeight); launcherEntries=value.Launcher.Entries.ToList();
        RefreshWidgetStyleChoices(launcherStyle,value.Launcher.Style); launcherList.RefreshEntries();
    }
}
