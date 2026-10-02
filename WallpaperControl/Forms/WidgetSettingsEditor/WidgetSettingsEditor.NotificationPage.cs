namespace WallpaperControl;

internal sealed partial class WidgetSettingsEditor
{
    private readonly ComboBox notificationSound = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private readonly NotificationSounds notificationPreviewSound = new();
    private NotificationSoundKind SelectedNotificationSound => NotificationSounds.Normalize(notificationSound.SelectedIndex);
    private void LocalizeNotificationSounds(NotificationSoundKind selection)
    {
        notificationSound.Items.Clear();
        foreach (string key in new[] { "NotificationSoundOff", "NotificationSoundWindows", "NotificationSoundChime" })
            notificationSound.Items.Add(Localization.Get(key, previewLanguageCode));
        notificationSound.SelectedIndex = (int)NotificationSounds.Normalize((int)selection);
    }
    private void LoadNotificationControls(WidgetSettings settings) => LocalizeNotificationSounds(settings.NotificationSound);
    private void InitializeNotificationPage(TabPage page)
    {
        var layout = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Location = new(18, 18), Padding = new(0, 0, 24, 24) };
        page.Controls.Add(layout);
        AddWallpaperInfoControl(layout, new Label { Font = CreateOwnedFont("Segoe UI", 12, FontStyle.Bold) }, "NotificationSettings");
        AddWallpaperInfoControl(layout, new Label(), "NotificationSound"); layout.Controls.Add(notificationSound);
        var preview = new Button { AutoSize = true }; AddWallpaperInfoControl(layout, preview, "NotificationSoundPreview");
        preview.Click += (_, _) => notificationPreviewSound.Play(SelectedNotificationSound);
        AddWallpaperInfoControl(layout, new Label { MaximumSize = new(540, 0) }, "NotificationSoundHint");
        LoadNotificationControls(initialWidgetSettings);
        notificationSound.SelectedIndexChanged += (_, _) => NotifyWidgetPreviewChanged();
    }
}
