namespace WallpaperControl;

internal sealed partial class SettingsForm
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
    public NotificationSoundKind NotificationSound { get; private set; } = NotificationSoundKind.Chime;
    private void AddNotificationControl(FlowLayoutPanel layout, Control control, string key)
    {
        control.AutoSize = true; control.Tag = key; control.Text = Localization.Get(key, previewLanguageCode); layout.Controls.Add(control);
    }
    private void InitializeNotificationPage(TabPage page, NotificationSoundKind selection)
    {
        var layout = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Location = new(18, 18), Padding = new(0, 0, 24, 24) };
        page.Controls.Add(layout);
        AddNotificationControl(layout, new Label { Font = CreateOwnedFont("Segoe UI", 12, FontStyle.Bold) }, "NotificationSettings");
        AddNotificationControl(layout, new Label(), "NotificationSound"); layout.Controls.Add(notificationSound);
        var preview = new Button { AutoSize = true }; AddNotificationControl(layout, preview, "NotificationSoundPreview");
        preview.Click += (_, _) => notificationPreviewSound.Play(SelectedNotificationSound);
        AddNotificationControl(layout, new Label { MaximumSize = new(540, 0) }, "NotificationSoundHint");
        NotificationSound = NotificationSounds.Normalize((int)selection);
        LocalizeNotificationSounds(NotificationSound);

    }
}
