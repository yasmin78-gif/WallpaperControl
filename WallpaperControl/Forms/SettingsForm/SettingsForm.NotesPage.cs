namespace WallpaperControl
{
    internal sealed partial class SettingsForm
    {
        private Action<IWin32Window, string>? manageNotes;
        private readonly Button notesManageButton = new();
        internal void ConfigureNotesManager(Action<IWin32Window, string> callback) { manageNotes = callback; notesManageButton.Enabled = true; }
        private Button? settingsNotesNavigationButton;
        private readonly CheckBox notesEnabled = new();
        private readonly CheckBox notesLocked = new();
        private readonly NumericUpDown notesMaximumHeight = new() { Minimum = 300, Maximum = 1000, Increment = 25, Width = 140 };
        private readonly ComboBox notesStyle = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };

        private void InitializeNotesPage(TabPage page)
        {
            page.AutoScroll = true;
            FlowLayoutPanel layout = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                Location = new Point(18, 18), Padding = new Padding(0, 0, 24, 24) };
            page.Controls.Add(layout);
            AddWallpaperInfoControl(layout, new Label { Font = CreateOwnedFont("Segoe UI", 12, FontStyle.Bold) }, "NotesTitle");
            notesEnabled.Checked = initialWidgetSettings.NotesEnabled;
            notesLocked.Checked = initialWidgetSettings.NotesLocked;
            notesMaximumHeight.Value = Math.Clamp(initialWidgetSettings.NotesMaximumHeight, 300, 1000);
            AddWallpaperInfoControl(layout, notesEnabled, "NotesEnabled");
            AddWallpaperInfoControl(layout, notesLocked, "SettingsWidgetLocked");
            AddWallpaperInfoControl(layout, new Label(), "SettingsCalendarMaximumHeight"); layout.Controls.Add(notesMaximumHeight);
            AddWallpaperInfoControl(layout, new Label(), "SettingsSystemStyle");
            RefreshWidgetStyleChoices(notesStyle, initialWidgetSettings.NotesStyle); layout.Controls.Add(notesStyle);
            notesManageButton.Enabled = manageNotes != null;
            AddWallpaperInfoControl(layout, notesManageButton, "NotesManage");
            notesManageButton.Click += (_, _) => manageNotes?.Invoke(this, previewLanguageCode);
        }

        private void ConnectNotesPreview()
        {
            notesEnabled.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            notesLocked.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            notesMaximumHeight.ValueChanged += (_, _) => NotifyWidgetPreviewChanged();
            notesStyle.SelectedIndexChanged += (_, _) => NotifyWidgetPreviewChanged();
        }
    }
}
