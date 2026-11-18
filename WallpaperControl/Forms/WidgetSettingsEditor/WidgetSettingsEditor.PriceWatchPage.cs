namespace WallpaperControl;

internal sealed partial class WidgetSettingsEditor
{
    private readonly CheckBox priceWatchEnabled = new(), priceWatchLocked = new(), priceWatchLive = new();
    private readonly CheckBox priceWatchDetailSizeLocked = new() { Name = "PriceWatchDetailSizeLocked" };
    private readonly ComboBox priceWatchStyle = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private readonly Dictionary<int, CheckBox> priceWatchShops = new();

    private void InitializePriceWatchPage(TabPage page)
    {
        page.AutoScroll = true;
        var layout = new FlowLayoutPanel
        {
            AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Location = new(18, 18),
            Padding = new(0, 0, 24, 24)
        };
        page.Controls.Add(layout);
        AddWallpaperInfoControl(layout, new Label { Font = CreateOwnedFont("Segoe UI", 12, FontStyle.Bold) }, "PriceWatchTitle");
        AddWallpaperInfoControl(layout, priceWatchEnabled, "PriceWatchEnabled");
        AddWallpaperInfoControl(layout, priceWatchLocked, "SettingsWidgetLocked");
        AddWallpaperInfoControl(layout, priceWatchDetailSizeLocked, "PriceWatchDetailSizeLocked");
        AddWallpaperInfoControl(layout, new Label(), "SettingsSystemStyle");
        layout.Controls.Add(priceWatchStyle);
        AddWallpaperInfoControl(layout, new Label(), "PriceWatchShops");
        foreach (var shop in PriceWatchShops.Supported)
        {
            var check = new CheckBox { Name = $"PriceWatchShop{shop.Id}", Text = shop.Name, AutoSize = true,
                Margin = new(0, 0, 0, 4) };
            priceWatchShops.Add(shop.Id, check);
            layout.Controls.Add(check);
        }
        AddWallpaperInfoControl(layout, priceWatchLive, "PriceWatchOptIn");
        var hint = new Label { MaximumSize = new(400, 0) };
        AddWallpaperInfoControl(layout, hint, "PriceWatchModeHint");
        priceWatchLive.Margin = new Padding(0, 16, 0, 4);
        var edit = new Button();
        var remove = new Button();
        AddWallpaperInfoControl(layout, edit, "PriceWatchEditCredentials");
        AddWallpaperInfoControl(layout, remove, "PriceWatchDeleteCredentials");
        edit.Click += (_, _) => EditPriceWatchCredentials();
        remove.Click += (_, _) =>
        {
            string T(string key) => Localization.Get(key, previewLanguageCode);
            if (MessageBox.Show(this, T("PriceWatchDeleteCredentials"), "PriceWatch", MessageBoxButtons.OKCancel) == DialogResult.OK)
                MessageBox.Show(this, T(PriceWatchCredentials.Current.Delete() ? "PriceWatchDeleted" : "PriceWatchStoreError"), "PriceWatch");
        };
        LoadPriceWatchControls(initialWidgetSettings);
        priceWatchEnabled.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
        priceWatchLocked.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
        priceWatchDetailSizeLocked.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
        priceWatchStyle.SelectedIndexChanged += (_, _) => NotifyWidgetPreviewChanged();
        priceWatchLive.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
        foreach (var check in priceWatchShops.Values)
            check.CheckedChanged += (_, _) =>
            {
                if (loadingControls) return;
                if (!priceWatchShops.Values.Any(item => item.Checked)) check.Checked = true;
                NotifyWidgetPreviewChanged();
            };
    }

    private void LoadPriceWatchControls(WidgetSettings value)
    {
        priceWatchEnabled.Checked = value.PriceWatch.Enabled;
        priceWatchLocked.Checked = value.PriceWatch.Locked;
        priceWatchDetailSizeLocked.Checked = value.PriceWatch.DetailSizeLocked;
        priceWatchLive.Checked = value.PriceWatch.Live;
        foreach (var (id, check) in priceWatchShops) check.Checked = value.PriceWatch.ShopIds.Contains(id);
        RefreshWidgetStyleChoices(priceWatchStyle, value.PriceWatch.Style);
    }

    private void EditPriceWatchCredentials()
    {
        string T(string key) => Localization.Get(key, previewLanguageCode);
        using var dialog = new Form
        {
            Text = T("PriceWatchEditCredentials"), StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, ShowInTaskbar = false, MinimizeBox = false, MaximizeBox = false,
            AutoScaleDimensions = new(96, 96), AutoScaleMode = AutoScaleMode.Dpi, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        var user = new TextBox { Width = 250, MaxLength = 128, AccessibleName = T("PriceWatchUsername") };
        var password = new TextBox { Width = 250, MaxLength = 1024, UseSystemPasswordChar = true, AccessibleName = T("PriceWatchPassword") };
        var save = new Button { Text = T("PriceWatchSaveCredentials"), AutoSize = true };
        var cancel = new Button { Text = T("PriceWatchCancel"), DialogResult = DialogResult.Cancel, AutoSize = true };
        var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Padding = new(12), Dock = DockStyle.Fill };
        var actions = new FlowLayoutPanel { AutoSize = true };
        actions.Controls.AddRange([save, cancel]);
        layout.Controls.AddRange([new Label { Text = user.AccessibleName, AutoSize = true }, user,
            new Label { Text = password.AccessibleName, AutoSize = true }, password, actions]);
        dialog.Controls.Add(layout);
        dialog.AcceptButton = save;
        dialog.CancelButton = cancel;
        save.Click += (_, _) =>
        {
            if (PriceWatchCredentials.Current.Save(user.Text.Trim(), password.Text))
            {
                password.Clear();
                dialog.DialogResult = DialogResult.OK;
            }
            else MessageBox.Show(dialog, T("PriceWatchStoreError"), "PriceWatch");
        };
        NotesDialogStyle.Apply(dialog, SystemWidgetStyle.Minimal);
        dialog.ShowDialog(this);
        password.Clear();
    }
}
