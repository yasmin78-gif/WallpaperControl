namespace WallpaperControl;

internal sealed partial class WidgetSettingsEditor
{
    private readonly CheckBox packageEnabled = new(), packageLocked = new();
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
    { packageEnabled.Checked = value.PackageEnabled; packageLocked.Checked = value.PackageLocked; packageMaximum.Value = CalendarViewport.NormalizeMaximum(value.PackageMaximumHeight); RefreshWidgetStyleChoices(packageStyle, value.PackageStyle); UpdatePackageConnection(); }
    private void ConnectPackagePreview()
    {
        packageEnabled.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
        packageLocked.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
        packageStyle.SelectedIndexChanged += (_, _) => NotifyWidgetPreviewChanged();
        packageMaximum.ValueChanged += (_, _) => NotifyWidgetPreviewChanged();
    }
}
