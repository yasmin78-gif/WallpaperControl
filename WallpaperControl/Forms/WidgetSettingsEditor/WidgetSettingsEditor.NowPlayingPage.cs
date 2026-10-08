namespace WallpaperControl;
internal sealed partial class WidgetSettingsEditor
{
    private readonly CheckBox nowPlayingEnabled = new(), nowPlayingLocked = new(), nowPlayingHideEmpty = new(), nowPlayingWheel = new();
    private readonly ComboBox nowPlayingStyle = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private void InitializeNowPlayingPage(TabPage page)
    {
        var layout = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Location = new(18,18) }; page.Controls.Add(layout);
        AddWallpaperInfoControl(layout,new Label { Font = CreateOwnedFont("Segoe UI",12,FontStyle.Bold) },"NowPlayingTitle");
        AddWallpaperInfoControl(layout,nowPlayingEnabled,"NowPlayingEnabled"); AddWallpaperInfoControl(layout,nowPlayingLocked,"SettingsWidgetLocked");
        AddWallpaperInfoControl(layout,new Label(),"SettingsSystemStyle"); layout.Controls.Add(nowPlayingStyle);
        AddWallpaperInfoControl(layout,nowPlayingHideEmpty,"NowPlayingHideEmpty"); AddWallpaperInfoControl(layout,nowPlayingWheel,"NowPlayingWheelVolume");
        AddWallpaperInfoControl(layout,new Label { MaximumSize = new(540,0) },"NowPlayingHint");
        LoadNowPlayingControls(initialWidgetSettings);
        foreach (var check in new[] { nowPlayingEnabled, nowPlayingLocked, nowPlayingHideEmpty, nowPlayingWheel }) check.CheckedChanged += (_,_) => NotifyWidgetPreviewChanged();
        nowPlayingStyle.SelectedIndexChanged += (_,_) => NotifyWidgetPreviewChanged();
    }
    private void LoadNowPlayingControls(WidgetSettings value)
    {
        nowPlayingEnabled.Checked = value.NowPlaying.Enabled; nowPlayingLocked.Checked = value.NowPlaying.Locked; nowPlayingHideEmpty.Checked = value.NowPlaying.HideEmpty;
        nowPlayingWheel.Checked = value.NowPlaying.WheelVolume; RefreshWidgetStyleChoices(nowPlayingStyle,value.NowPlaying.Style);
    }
}
