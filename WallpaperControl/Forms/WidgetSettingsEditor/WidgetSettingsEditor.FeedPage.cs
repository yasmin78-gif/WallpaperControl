namespace WallpaperControl;
internal sealed partial class WidgetSettingsEditor
{
    private readonly CheckBox feedEnabled = new(), feedLocked = new();
    private readonly ComboBox feedStyle = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private readonly NumericUpDown feedHeight = new() { Minimum = 300, Maximum = 1400, Increment = 50, Width = 100 };
    private Action? feedManage;
    internal void ConfigureFeeds(Action manage) => feedManage = manage;
    private void InitializeFeedPage(TabPage page)
    {
        var layout = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Location = new(18,18) }; page.Controls.Add(layout);
        AddWallpaperInfoControl(layout, new Label { Font = CreateOwnedFont("Segoe UI",12,FontStyle.Bold) }, "FeedTitle");
        AddWallpaperInfoControl(layout, feedEnabled, "FeedEnabled"); AddWallpaperInfoControl(layout, feedLocked, "SettingsWidgetLocked");
        AddWallpaperInfoControl(layout, new Label(), "SettingsSystemStyle"); layout.Controls.Add(feedStyle);
        AddWallpaperInfoControl(layout, new Label(), "SettingsCalendarMaximumHeight"); layout.Controls.Add(feedHeight);
        var manage = new Button { AutoSize = true }; AddWallpaperInfoControl(layout, manage, "FeedManage"); manage.Click += (_,_) => feedManage?.Invoke();
        AddWallpaperInfoControl(layout, new Label { MaximumSize = new(540,0) }, "FeedHint");
        LoadFeedControls(initialWidgetSettings);
        feedEnabled.CheckedChanged += (_,_) => NotifyWidgetPreviewChanged(); feedLocked.CheckedChanged += (_,_) => NotifyWidgetPreviewChanged();
        feedStyle.SelectedIndexChanged += (_,_) => NotifyWidgetPreviewChanged(); feedHeight.ValueChanged += (_,_) => NotifyWidgetPreviewChanged();
    }
    private void LoadFeedControls(WidgetSettings value)
    { feedEnabled.Checked = value.Feed.Enabled; feedLocked.Checked = value.Feed.Locked; feedHeight.Value = CalendarViewport.NormalizeMaximum(value.Feed.MaximumHeight); RefreshWidgetStyleChoices(feedStyle, value.Feed.Style); }
}
