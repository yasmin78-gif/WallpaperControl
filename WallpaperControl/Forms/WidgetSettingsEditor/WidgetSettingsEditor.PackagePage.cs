namespace WallpaperControl;

internal sealed partial class WidgetSettingsEditor
{
    private readonly CheckBox packageEnabled = new(), packageLocked = new(), packageAutomatic = new();
    private readonly ComboBox packageInterval = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private int PackageIntervalMinutes => packageInterval.SelectedIndex switch { 0 => 15, 2 => 60, 3 => 120, _ => 30 };
    private void LocalizePackageInterval(int minutes)
    {
        packageInterval.Items.Clear();
        foreach (int value in new[] { 15, 30, 60, 120 })
            packageInterval.Items.Add(string.Format(Localization.Get("PackageRefreshMinutesFormat", previewLanguageCode), value));
        packageInterval.SelectedIndex = minutes switch { 15 => 0, 60 => 2, 120 => 3, _ => 1 };
    }
    private readonly ComboBox packageStyle = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private readonly NumericUpDown packageMaximum = new() { Minimum = 300, Maximum = 1400, Increment = 25, Width = 140 };
    private readonly Label packageConnection = new() { AutoSize = true };
    private Action<IWin32Window, string, string>? packageAction;
    private Func<bool>? packageHasCredential;
    internal void ConfigurePackages(Action<IWin32Window, string, string> action, Func<bool> hasCredential)
    { packageAction = action; packageHasCredential = hasCredential; UpdatePackageConnection(); }
    private void UpdatePackageConnection() => packageConnection.Text = Localization.Get(packageHasCredential?.Invoke() == true ? "PackageCredentialSaved" : "PackageNoCredential", previewLanguageCode);
    private void InitializePackagePage(TabPage page)
    {
        var layout = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Location = new(18, 18), Padding = new(0, 0, 24, 24) };
        page.Controls.Add(layout);
        AddWallpaperInfoControl(layout, new Label { Font = CreateOwnedFont("Segoe UI", 12, FontStyle.Bold) }, "PackageTitle");
        AddWallpaperInfoControl(layout, packageEnabled, "PackageEnabled");
        AddWallpaperInfoControl(layout, packageLocked, "SettingsWidgetLocked");
        AddWallpaperInfoControl(layout, new Label(), "SettingsSystemStyle"); layout.Controls.Add(packageStyle);
        AddWallpaperInfoControl(layout, new Label(), "SettingsCalendarMaximumHeight"); layout.Controls.Add(packageMaximum);
        AddWallpaperInfoControl(layout, packageAutomatic, "PackageAutomaticRefresh");
        AddWallpaperInfoControl(layout, new Label(), "PackageRefreshInterval"); layout.Controls.Add(packageInterval);
        layout.Controls.Add(packageConnection);
        foreach (string key in new[] { "PackageSetup", "PackageTest", "PackageManage", "PackageAdd" })
        {
            var button = new Button(); AddWallpaperInfoControl(layout, button, key);
            button.Click += (_, _) => { packageAction?.Invoke(this, previewLanguageCode, key); UpdatePackageConnection(); };
        }
        AddWallpaperInfoControl(layout, new Label { MaximumSize = new(540, 0) }, "PackageLocalActions");
        LoadPackageControls(initialWidgetSettings);
    }
    private void LoadPackageControls(WidgetSettings value)
    { packageAutomatic.Checked = value.PackageAutomaticRefresh; LocalizePackageInterval(value.PackageRefreshMinutes); packageInterval.Enabled = packageAutomatic.Checked; packageEnabled.Checked = value.PackageEnabled; packageLocked.Checked = value.PackageLocked; packageMaximum.Value = CalendarViewport.NormalizeMaximum(value.PackageMaximumHeight); RefreshWidgetStyleChoices(packageStyle, value.PackageStyle); UpdatePackageConnection(); }
    private void ConnectPackagePreview()
    {
        packageAutomatic.CheckedChanged += (_, _) => { packageInterval.Enabled = packageAutomatic.Checked; NotifyWidgetPreviewChanged(); };
        packageInterval.SelectedIndexChanged += (_, _) => NotifyWidgetPreviewChanged();
        packageEnabled.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
        packageLocked.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
        packageStyle.SelectedIndexChanged += (_, _) => NotifyWidgetPreviewChanged();
        packageMaximum.ValueChanged += (_, _) => NotifyWidgetPreviewChanged();
    }
}
