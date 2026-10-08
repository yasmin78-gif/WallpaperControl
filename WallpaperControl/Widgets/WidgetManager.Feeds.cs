namespace WallpaperControl;
internal sealed partial class WidgetManager
{
    private FeedService? feeds;
    private FeedWidgetForm? feedWidget;
    private bool feedDialogOpen;
    internal Action<FeedSource,int,string>? ShowFeedNotification { get; set; }
    internal Action? ClearFeedNotifications { get; set; }
    private FeedService Feeds
    {
        get
        {
            if (feeds == null) { feeds = new(); feeds.NewEntries += (source,count) => ShowFeedNotification?.Invoke(source,count,settings.ClockLanguageCode); }
            return feeds;
        }
    }
    internal void OpenFeedNotification(Guid source)
    {
        if (!settings.Feed.Sources.Any(s=>s.Id == source)) return;
        var value = settings.Feed.Clone(); value.Selected = source; SaveFeedContents(value);
        if (feedWidget != null) DesktopWidgetNative.KeepOnDesktop(feedWidget);
    }
    internal void ShowFeeds(IWin32Window? owner, string language, bool add = false)
    {
        if (feedDialogOpen) return; feedDialogOpen = true;
        try
        {
            if (add)
            {
                if (settings.Feed.Sources.Count >= 20) return;
                using var dialog = new FeedEditorForm(Feeds, null, language);
                if (dialog.ShowDialog(owner) == DialogResult.OK && dialog.Result != null)
                { var value = settings.Feed.Clone(); value.Sources.Add(dialog.Result); SaveFeedContents(value); }
            }
            else { using var dialog = new FeedManagerForm(Feeds, settings.Feed, SaveFeedContents, language); dialog.ShowDialog(owner); }
        }
        finally { feedDialogOpen = false; }
    }
    private void SaveFeedContents(FeedWidgetSettings value)
    {
        if (settings.Feed.Sources.Any(old => old.Notifications && !value.Sources.Any(current => current.Id == old.Id && current.Url == old.Url && current.Notifications))) ClearFeedNotifications?.Invoke();
        settings.Feed.Sources = value.Sources.ToList(); settings.Feed.Selected = value.Selected; settings.Feed.Display = value.Display; settings.Feed.Period = value.Period;
        // Feed management, like notes and shipments, commits separately from visual widget preferences.
        if (previewMode)
        {
            var saved = WidgetSettings.Load(registryPath); saved.Feed.Sources = value.Sources.ToList(); saved.Feed.Selected = value.Selected; saved.Feed.Display = value.Display; saved.Feed.Period = value.Period; saved.Save(registryPath);
        }
        else settings.Save(registryPath);
        Feeds.Configure(settings.Feed); feedWidget?.Apply(settings.Feed, settings.ClockLanguageCode); _ = Feeds.RefreshAsync();
    }
    private void ApplyFeedWidget(WidgetSettings target, bool restoreLocations)
    {
        if (!target.Feed.Enabled)
        {
            ClearFeedNotifications?.Invoke();
            feedWidget?.Close(); feedWidget?.Dispose(); feedWidget = null;
            if (feeds != null) feeds.Configure(target.Feed); return;
        }
        Feeds.Configure(target.Feed); Feeds.SetSuspended(activitySuspended || packagePowerSuspended);
        if (feedWidget == null || feedWidget.IsDisposed)
        {
            feedWidget = new(Feeds, target.Feed, target.ClockLanguageCode,
                point => { settings.Feed.Location = point; if (!previewMode) settings.Save(registryPath); }, SaveFeedContents,
                () => ShowFeeds(null, settings.ClockLanguageCode), () => ShowFeeds(null, settings.ClockLanguageCode, true));
            feedWidget.SetActivitySuspended(activitySuspended); feedWidget.SetPowerSuspended(packagePowerSuspended);
            RegisterDesktopWidget(feedWidget); feedWidget.Show();
            if (!DesktopWidgetNative.AttachToDesktop(feedWidget, target.Feed.Location)) { feedWidget.Hide(); AppLogger.Info("Feed desktop attachment failed"); }
        }
        else
        {
            feedWidget.Apply(target.Feed, target.ClockLanguageCode);
            if (restoreLocations) feedWidget.Location = WidgetSettings.EnsureVisible(target.Feed.Location, feedWidget.Size);
            DesktopWidgetNative.KeepOnDesktop(feedWidget);
        }
        if (previewMode) DesktopWidgetNative.EnableInteraction(feedWidget);
        _ = Feeds.RefreshAsync();
    }
    private void PreserveFeedContents(WidgetSettings value, bool preserveLocation = true)
    { if (preserveLocation) value.Feed.Location = settings.Feed.Location; value.Feed.Sources = settings.Feed.Sources.ToList(); value.Feed.Selected = settings.Feed.Selected; value.Feed.Display = settings.Feed.Display; value.Feed.Period = settings.Feed.Period; }
    private void DisposeFeeds() { ShowFeedNotification = null; ClearFeedNotifications = null; feedWidget?.Close(); feedWidget?.Dispose(); feedWidget = null; feeds?.Dispose(); feeds = null; }
}
