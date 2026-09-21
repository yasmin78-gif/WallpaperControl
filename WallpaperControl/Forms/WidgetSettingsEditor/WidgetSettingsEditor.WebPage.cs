namespace WallpaperControl;

internal sealed partial class WidgetSettingsEditor
{
    private readonly CheckBox webEnabled = new();
    private readonly CheckBox webLocked = new();
    private readonly CheckBox webInteraction = new();
    private readonly CheckBox webStartup = new();
    private readonly TextBox webName = new() { Width = 430, MaxLength = 160 };
    private readonly TextBox webUrl = new() { Width = 430, MaxLength = 8192 };
    private readonly NumericUpDown webZoom = new() { Minimum = 50, Maximum = 200, Increment = 10, Width = 120 };
    private readonly ComboBox webStyle = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private readonly Label webError = new() { AutoSize = true, MaximumSize = new Size(490, 0) };
    private string appliedWebUrl = "";

    private void InitializeWebPage(TabPage page)
    {
        FlowLayoutPanel layout = new() { Location = new Point(18, 18), AutoSize = true,
            FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 0, 24, 24) };
        page.Controls.Add(layout);
        AddWallpaperInfoControl(layout, new Label { Font = CreateOwnedFont("Segoe UI", 12, FontStyle.Bold) }, "WebTitle");
        AddWallpaperInfoControl(layout, webEnabled, "WebEnabled");
        AddWallpaperInfoControl(layout, new Label(), "WebName"); layout.Controls.Add(webName);
        AddWallpaperInfoControl(layout, new Label(), "WebUrl"); layout.Controls.Add(webUrl);
        Button apply = new(); AddWallpaperInfoControl(layout, apply, "WebApplyUrl");
        layout.Controls.Add(webError);
        AddWallpaperInfoControl(layout, new Label(), "WebZoom"); layout.Controls.Add(webZoom);
        AddWallpaperInfoControl(layout, webInteraction, "WebInteraction");
        AddWallpaperInfoControl(layout, webStartup, "WebStartup");
        AddWallpaperInfoControl(layout, webLocked, "WebLocked");
        AddWallpaperInfoControl(layout, new Label(), "SettingsSystemStyle"); layout.Controls.Add(webStyle);
        LoadWebControls(initialWidgetSettings.Web);
        foreach (CheckBox checkbox in new[] { webEnabled, webInteraction, webStartup, webLocked })
            checkbox.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
        webZoom.ValueChanged += (_, _) => NotifyWidgetPreviewChanged();
        webName.TextChanged += (_, _) => NotifyWidgetPreviewChanged();
        webStyle.SelectedIndexChanged += (_, _) => NotifyWidgetPreviewChanged();
        webUrl.TextChanged += (_, _) => LocalizeWebControls();
        apply.Click += (_, _) => ApplyWebUrl();
        webUrl.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ApplyWebUrl(); } };
    }
    private void ApplyWebUrl()
    {
        LocalizeWebControls();
        if (!WebWidgetSettings.IsValidUrl(webUrl.Text)) return;
        appliedWebUrl = webUrl.Text.Trim(); NotifyWidgetPreviewChanged();
    }
    private void LocalizeWebControls()
    {
        webName.PlaceholderText = Localization.Get("WebTitle", previewLanguageCode);
        webName.AccessibleName = Localization.Get("WebName", previewLanguageCode);
        webUrl.AccessibleName = Localization.Get("WebUrl", previewLanguageCode);
        webZoom.AccessibleName = Localization.Get("WebZoom", previewLanguageCode);
        webError.Text = WebWidgetSettings.IsValidUrl(webUrl.Text) ? "" : Localization.Get("WebInvalidUrl", previewLanguageCode);
        webError.ForeColor = darkMode ? Color.LightSalmon : Color.Firebrick;
    }
    private void LoadWebControls(WebWidgetSettings value)
    {
        webEnabled.Checked = value.Enabled; webLocked.Checked = value.Locked;
        webInteraction.Checked = value.AllowInteraction; webStartup.Checked = value.ReloadOnStartup;
        webName.Text = value.DisplayName; webUrl.Text = value.Url; appliedWebUrl = value.Url;
        webZoom.Value = Math.Clamp(value.Zoom, 50, 200);
        RefreshWidgetStyleChoices(webStyle, value.Style); LocalizeWebControls();
    }
    private void ReadWebControls(WebWidgetSettings value)
    {
        value.Enabled = webEnabled.Checked; value.Locked = webLocked.Checked;
        value.AllowInteraction = webInteraction.Checked; value.ReloadOnStartup = webStartup.Checked;
        value.DisplayName = webName.Text.Trim(); value.Url = webUrl.Text.Trim();
        value.Zoom = (int)webZoom.Value; value.Style = (SystemWidgetStyle)Math.Max(0, webStyle.SelectedIndex);
    }
    private void DisposeWebControls()
    {
        webEnabled.Dispose(); webLocked.Dispose(); webInteraction.Dispose(); webStartup.Dispose();
        webName.Dispose(); webUrl.Dispose(); webZoom.Dispose(); webStyle.Dispose(); webError.Dispose();
    }
}
