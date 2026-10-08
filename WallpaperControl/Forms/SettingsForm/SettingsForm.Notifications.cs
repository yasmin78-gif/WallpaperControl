namespace WallpaperControl;
internal sealed partial class SettingsForm
{
    private readonly ComboBox notificationSound = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private readonly ComboBox packageSound = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private readonly ComboBox feedSound = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private readonly ComboBox noteSound = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private readonly NotificationSounds notificationPreviewSound = new();
    public NotificationSoundPreferences NotificationTones { get; private set; } = new();
    public NotificationSoundKind NotificationSound { get; private set; } = NotificationSoundKind.Chime;
    private NotificationSoundKind SelectedNotificationSound => NotificationSounds.Normalize(notificationSound.SelectedIndex);
    private void LocalizeNotificationSounds(NotificationSoundKind selection)
    {
        foreach (var combo in new[] { notificationSound, packageSound, feedSound, noteSound })
        {
            var previous = combo == notificationSound ? selection : NotificationSounds.Normalize(combo.SelectedIndex);
            combo.Items.Clear();
            foreach (string key in new[] { "NotificationSoundOff", "NotificationSoundWindows", "NotificationSoundChime", "NotificationSoundSoft", "NotificationSoundBell", "NotificationSoundTriad" }) combo.Items.Add(Localization.Get(key,previewLanguageCode));
            combo.SelectedIndex = (int)previous;
        }
    }
    private void ResetNotificationTones()
    {
        foreach (var combo in new[] { notificationSound,packageSound,feedSound,noteSound }) combo.SelectedIndex = (int)NotificationSoundKind.Chime;
    }
    private void CaptureNotificationTones()
    {
        NotificationSound = SelectedNotificationSound;
        NotificationTones = new() { Twitch = SelectedNotificationSound, Packages = NotificationSounds.Normalize(packageSound.SelectedIndex), Feeds = NotificationSounds.Normalize(feedSound.SelectedIndex), Notes = NotificationSounds.Normalize(noteSound.SelectedIndex) };
    }
    private void AddNotificationControl(FlowLayoutPanel layout, Control control, string key)
    { control.AutoSize = true; control.Tag = key; control.Text = Localization.Get(key,previewLanguageCode); layout.Controls.Add(control); }
    private void InitializeNotificationPage(TabPage page, NotificationSoundKind legacy, NotificationSoundPreferences? tones)
    {
        NotificationSound = legacy; NotificationTones = tones ?? NotificationSoundPreferences.FromLegacy(legacy);
        var layout = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Location = new(18,18), Padding = new(0,0,24,24) }; page.Controls.Add(layout);
        AddNotificationControl(layout,new Label { Font = CreateOwnedFont("Segoe UI",12,FontStyle.Bold) },"NotificationSettings");
        LocalizeNotificationSounds(NotificationTones.Twitch);
        packageSound.SelectedIndex = (int)NotificationTones.Packages; feedSound.SelectedIndex = (int)NotificationTones.Feeds; noteSound.SelectedIndex = (int)NotificationTones.Notes;
        foreach (var pair in new[] { ("TwitchTitle",notificationSound), ("PackageTitle",packageSound), ("FeedTitle",feedSound), ("NotesReminderTone",noteSound) })
        {
            AddNotificationControl(layout,new Label(),pair.Item1);
            var row = new FlowLayoutPanel { AutoSize = true }; row.Controls.Add(pair.Item2);
            var preview = new Button { AutoSize = true, Tag = "NotificationSoundPreview", Text = Localization.Get("NotificationSoundPreview",previewLanguageCode) };
            preview.Click += (_,_) => notificationPreviewSound.Play(NotificationSounds.Normalize(pair.Item2.SelectedIndex)); row.Controls.Add(preview); layout.Controls.Add(row);
        }
        AddNotificationControl(layout,new Label { MaximumSize = new(600,0) },"NotificationTypeSoundHint");
    }
}
