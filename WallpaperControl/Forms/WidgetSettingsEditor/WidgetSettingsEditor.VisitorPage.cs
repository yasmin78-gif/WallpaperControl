namespace WallpaperControl;

internal sealed partial class WidgetSettingsEditor
{
    private readonly CheckBox visitorEnabled = new(), visitorLocked = new(), visitorStyleOpacity = new();
    private readonly ComboBox visitorStyle = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private readonly NumericUpDown visitorScale = new() { Minimum = 50, Maximum = 200, Increment = 10, Width = 120 };
    private readonly NumericUpDown visitorFont = new() { Minimum = 8, Maximum = 24, Width = 120 };
    private readonly NumericUpDown visitorOpacity = new() { Minimum = 0, Maximum = 100, Width = 120 };
    private int? visitorBackgroundArgb, visitorTextArgb;

    private void InitializeVisitorPage(TabPage page)
    {
        page.AutoScroll = true;
        var layout = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Location = new(18, 18), Padding = new(0, 0, 20, 24) };
        page.Controls.Add(layout);
        AddWallpaperInfoControl(layout, new Label { Font = CreateOwnedFont("Segoe UI", 12, FontStyle.Bold) }, "VisitorTitle");
        AddWallpaperInfoControl(layout, visitorEnabled, "VisitorWidget");
        AddWallpaperInfoControl(layout, visitorLocked, "SettingsWidgetLocked");
        AddWallpaperInfoControl(layout, new Label(), "SettingsSystemStyle"); layout.Controls.Add(visitorStyle);
        AddWallpaperInfoControl(layout, new Label(), "VisitorWidgetScale"); layout.Controls.Add(visitorScale);
        AddWallpaperInfoControl(layout, new Label(), "WidgetFontSize"); layout.Controls.Add(visitorFont);
        AddWallpaperInfoControl(layout, visitorStyleOpacity, "VisitorStyleOpacity");
        AddWallpaperInfoControl(layout, new Label(), "VisitorOpacity"); layout.Controls.Add(visitorOpacity);
        foreach (bool background in new[] { true, false })
        {
            Button color = new() { AutoSize = true };
            AddWallpaperInfoControl(layout, color, background ? "VisitorBackground" : "VisitorTextColor");
            color.Click += (_, _) =>
            {
                var palette = WidgetDrawing.GetPalette((SystemWidgetStyle)Math.Max(0, visitorStyle.SelectedIndex));
                using ColorDialog dialog = new() { FullOpen = true, Color = Color.FromArgb(background ? visitorBackgroundArgb ?? palette.panel.ToArgb() : visitorTextArgb ?? palette.title.ToArgb()) };
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (background) visitorBackgroundArgb = dialog.Color.ToArgb(); else visitorTextArgb = dialog.Color.ToArgb();
                NotifyWidgetPreviewChanged();
            };
        }
        Button reset = new() { AutoSize = true };
        AddWallpaperInfoControl(layout, reset, "VisitorResetColors");
        reset.Click += (_, _) => { visitorBackgroundArgb = visitorTextArgb = null; visitorStyleOpacity.Checked = true; NotifyWidgetPreviewChanged(); };
        LoadVisitorControls(initialWidgetSettings);
        visitorEnabled.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
        visitorLocked.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
        visitorStyle.SelectedIndexChanged += (_, _) => NotifyWidgetPreviewChanged();
        visitorScale.ValueChanged += (_, _) => NotifyWidgetPreviewChanged();
        visitorFont.ValueChanged += (_, _) => NotifyWidgetPreviewChanged();
        visitorOpacity.ValueChanged += (_, _) => NotifyWidgetPreviewChanged();
        visitorStyleOpacity.CheckedChanged += (_, _) => { visitorOpacity.Enabled = !visitorStyleOpacity.Checked; NotifyWidgetPreviewChanged(); };
    }
    private void LoadVisitorControls(WidgetSettings value)
    {
        var visitor = value.Visitor;
        visitorEnabled.Checked = visitor.Enabled; visitorLocked.Checked = visitor.Locked;
        visitorScale.Value = Math.Clamp(visitor.ScalePercent, 50, 200); visitorFont.Value = Math.Clamp(visitor.FontSize, 8, 24);
        visitorStyleOpacity.Checked = !visitor.BackgroundOpacityPercent.HasValue;
        visitorOpacity.Value = Math.Clamp(visitor.BackgroundOpacityPercent ?? 44, 0, 100); visitorOpacity.Enabled = !visitorStyleOpacity.Checked;
        visitorBackgroundArgb = visitor.BackgroundArgb; visitorTextArgb = visitor.TextArgb;
        RefreshWidgetStyleChoices(visitorStyle, visitor.Style);
    }
    private VisitorWidgetSettings ReadVisitorControls()
    {
        var value = initialWidgetSettings.Visitor.Clone();
        value.Enabled = visitorEnabled.Checked; value.Locked = visitorLocked.Checked;
        value.Style = (SystemWidgetStyle)Math.Max(0, visitorStyle.SelectedIndex);
        value.ScalePercent = (int)visitorScale.Value; value.FontSize = (int)visitorFont.Value;
        value.BackgroundOpacityPercent = visitorStyleOpacity.Checked ? null : (int)visitorOpacity.Value;
        value.BackgroundArgb = visitorBackgroundArgb; value.TextArgb = visitorTextArgb;
        return value;
    }
}
