namespace WallpaperControl;

internal sealed class PackageEditorForm : Form
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly TextBox name = new() { Dock = DockStyle.Top, MaxLength = 200 };
    private readonly TextBox number = new() { Dock = DockStyle.Top, MaxLength = 50 };
    internal PackageEditorForm(PackageTrackingService service, TrackedShipment? shipment, string language)
    {
        Text = Localization.Get(shipment == null ? "PackageAdd" : "PackageEdit", language);
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new(96, 96);
        ClientSize = new(580, 360); MinimumSize = new(440, 360); ShowInTaskbar = false; StartPosition = FormStartPosition.CenterScreen;
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 1, Padding = new Padding(16) };
        void Label(string key) => fields.Controls.Add(new Label { Text = Localization.Get(key, language), AutoSize = true, MaximumSize = new(520, 0), Margin = new Padding(0, 8, 0, 8) });
        Label("PackageName"); fields.Controls.Add(name); Label("PackageNumber"); fields.Controls.Add(number);
        Label("PackageCarrier");
        fields.SizeChanged += (_, _) =>
        {
            foreach (Label label in fields.Controls.OfType<Label>()) label.MaximumSize = new(Math.Max(100, fields.ClientSize.Width - fields.Padding.Horizontal - 24 * DeviceDpi / 96), 0);
        };
        var carrier = new Label { AutoSize = true, Text = Localization.Get("PackageAutomatic", language) }; fields.Controls.Add(carrier);
        var amazonNotice = new Label { AutoSize = true, MaximumSize = new(520, 0), Margin = new Padding(0, 8, 0, 8) }; fields.Controls.Add(amazonNotice);
        void UpdateDetection()
        {
            bool local = shipment == null ? AmazonLogistics.Recognizes(number.Text) : AmazonLogistics.IsLocal(shipment);
            carrier.Text = local ? "Amazon Logistics" : Localization.Get("PackageAutomatic", language);
            amazonNotice.Text = local ? Localization.Get("PackageAmazonNotice", language) : "";
            if (local) ClientSize = new(ClientSize.Width, Math.Max(ClientSize.Height, 440 * DeviceDpi / 96));
        }
        number.TextChanged += (_, _) => UpdateDetection();
        if (shipment != null) { name.Text = shipment.DisplayName; number.Text = shipment.TrackingNumber; number.ReadOnly = true; Label("PackageNumberReadOnly"); }
        UpdateDetection();
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
        var cancel = new Button { Text = Localization.Get("SettingsCancel", language), AutoSize = true, DialogResult = DialogResult.Cancel, MinimumSize = new(100, 32) };
        var save = new Button { Text = Localization.Get("SettingsSave", language), AutoSize = true, MinimumSize = new(100, 32) };
        footer.Controls.Add(cancel); footer.Controls.Add(save); Controls.Add(fields); Controls.Add(footer);
        AcceptButton = save; CancelButton = cancel;
        save.Click += async (_, _) =>
        {
            save.Enabled = false; fields.Enabled = false;
            try
            {
                PackageResult result = shipment == null ? await service.AddAsync(number.Text, name.Text, lifetime.Token) : service.Rename(shipment.Id, name.Text);
                if (IsDisposed || Disposing) return;
                if (result.Outcome == PackageOperation.Success) { DialogResult = DialogResult.OK; Close(); }
                else if (result.Failure == TrackingProviderFailure.MissingCredential)
                { using var setup = new Ship24SetupForm(new TrackingCredentialStore(), language); setup.ShowDialog(this); }
                else MessageBox.Show(this, PackagePresentation.Error(result, language), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (OperationCanceledException) { }
            finally { if (!IsDisposed) { save.Enabled = true; fields.Enabled = true; } }
        };
        FormClosing += (_, _) => lifetime.Cancel();
        NotesDialogStyle.Apply(this, SystemWidgetStyle.Minimal);
    }
    protected override void Dispose(bool disposing) { if (disposing && !IsDisposed) { lifetime.Cancel(); lifetime.Dispose(); } base.Dispose(disposing); }
}
