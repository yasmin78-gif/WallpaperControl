namespace WallpaperControl
{
    internal sealed partial class WidgetSettingsEditor
    {
        // These controls are owned and disposed recursively by the WinForms Controls tree.
#pragma warning disable CA2213
        private readonly CheckBox wallpaperInfoEnabled = new();
        private readonly CheckBox wallpaperInfoLocked = new();
        private readonly CheckBox wallpaperInfoAdvanced = new();
        private readonly CheckBox wallpaperInfoExtension = new();
        private readonly TextBox wallpaperInfoSuffix = new();
        private readonly ComboBox wallpaperInfoStyle = new();

#pragma warning restore CA2213
        private readonly NumericUpDown wallpaperInfoFontSize = new() { Minimum = 10, Maximum = 24, Width = 120 };

        /// <summary>Uses a vertical, auto-sized layout so all five languages can grow without collisions.</summary>
        private void InitializeWallpaperInfoPage(TabPage page)
        {
            page.AutoScroll = true;
            FlowLayoutPanel layout = new()
            {
                Location = new Point(18, 18), AutoSize = true,
                FlowDirection = FlowDirection.TopDown, WrapContents = false,
                Padding = new Padding(0, 0, 24, 24)
            };
            page.Controls.Add(layout);
            Label title = new() { Font = CreateOwnedFont("Segoe UI", 12, FontStyle.Bold) };
            AddWallpaperInfoControl(layout, title, "WallpaperInfoTitle");
            wallpaperInfoEnabled.Checked = initialWidgetSettings.WallpaperInfoEnabled;
            wallpaperInfoLocked.Checked = initialWidgetSettings.WallpaperInfoLocked;
            wallpaperInfoAdvanced.Checked = initialWidgetSettings.WallpaperInfoShowAdvanced;
            wallpaperInfoExtension.Checked = initialWidgetSettings.WallpaperInfoShowExtension;
            AddWallpaperInfoControl(layout, wallpaperInfoEnabled, "WallpaperInfoEnabled");
            AddWallpaperInfoControl(layout, wallpaperInfoLocked, "SettingsWidgetLocked");
            AddWallpaperInfoControl(layout, wallpaperInfoAdvanced, "WallpaperInfoAdvanced");
            AddWallpaperInfoControl(layout, wallpaperInfoExtension, "WallpaperInfoExtension");
            AddWallpaperInfoControl(layout, new Label(), "WallpaperInfoSuffix");
            wallpaperInfoSuffix.Width = 430;
            wallpaperInfoSuffix.Text = initialWidgetSettings.WallpaperInfoHiddenSuffix;
            wallpaperInfoSuffix.AccessibleName = Localization.Get("WallpaperInfoSuffix", previewLanguageCode);
            layout.Controls.Add(wallpaperInfoSuffix);
            AddWallpaperInfoControl(layout, new Label(), "SettingsSystemStyle");
            wallpaperInfoStyle.Width = 280;
            wallpaperInfoStyle.DropDownStyle = ComboBoxStyle.DropDownList;
            RefreshWidgetStyleChoices(wallpaperInfoStyle, initialWidgetSettings.WallpaperInfoStyle);
            layout.Controls.Add(wallpaperInfoStyle);
            AddWallpaperInfoControl(layout, new Label(), "WidgetFontSize");
            wallpaperInfoFontSize.Value = Math.Clamp(initialWidgetSettings.WallpaperInfoFontSize, 10, 24);
            layout.Controls.Add(wallpaperInfoFontSize);
        }

        private void AddWallpaperInfoControl(FlowLayoutPanel layout, Control control, string key)
        {
            if (control is Label label) label.UseMnemonic = false;
            if (control is ButtonBase button) button.UseMnemonic = false;
            control.AutoSize = true;
            control.Tag = key;
            control.Text = Localization.Get(key, previewLanguageCode);
            control.Margin = new Padding(0, 4, 0, 12);
            layout.Controls.Add(control);
        }

        private SystemWidgetStyle GetWallpaperInfoStyle() =>
            wallpaperInfoStyle.SelectedIndex is >= 0 and <= 2
                ? (SystemWidgetStyle)wallpaperInfoStyle.SelectedIndex : SystemWidgetStyle.Minimal;

        /// <summary>Subscribe only after every settings page is initialized.</summary>
        private void ConnectWallpaperInfoPreview()
        {
            wallpaperInfoFontSize.ValueChanged += (_, _) => NotifyWidgetPreviewChanged();
            wallpaperInfoEnabled.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            wallpaperInfoLocked.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            wallpaperInfoAdvanced.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            wallpaperInfoExtension.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            wallpaperInfoSuffix.TextChanged += (_, _) => NotifyWidgetPreviewChanged();
            wallpaperInfoStyle.SelectedIndexChanged += (_, _) => NotifyWidgetPreviewChanged();
        }
    }
}
