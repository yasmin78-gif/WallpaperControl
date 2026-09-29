using System.Diagnostics;

namespace WallpaperControl;

internal sealed class Ship24SetupForm : Form
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly TextBox keyInput = new() { UseSystemPasswordChar = true, MaxLength = 16384, Dock = DockStyle.Top };
    internal Ship24SetupForm(TrackingCredentialStore credentials, string language, Func<ITrackingProvider>? providerFactory = null, bool testOnShown = false)
    {
        Text = Localization.Get("PackageSetup", language);
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new(96, 96);
        ClientSize = new(640, 610); MinimumSize = new(500, 430); ShowInTaskbar = false; StartPosition = FormStartPosition.CenterParent;
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 1, Padding = new Padding(16) };
        Label Label(string key) { var label = new Label { Text = Localization.Get(key, language), AutoSize = true, MaximumSize = new(580, 0), Margin = new Padding(0, 5, 0, 10) }; fields.Controls.Add(label); return label; }
        Label("PackageSetupHelp"); Label("PackagePrivacy");
        var open = new Button { Text = Localization.Get("PackageOpenShip24", language), AutoSize = true, MinimumSize = new(120, 32) };
        open.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("https://www.ship24.com/pricing?product=s24-prod-track-api-shipment") { UseShellExecute = true }); }
            catch { MessageBox.Show(this, Localization.Get("PackageBrowserError", language), Text); }
        };
        fields.Controls.Add(open); var state = Label("PackageNoCredential");
        Label("PackageKey"); fields.Controls.Add(keyInput); Label("PackageSaveBeforeTest");
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
        Button Button(string resource) { var b = new Button { Text = Localization.Get(resource, language), AutoSize = true, MinimumSize = new(100, 32) }; actions.Controls.Add(b); return b; }
        var save = Button("SettingsSave"); var delete = Button("PackageDeleteKey"); var test = Button("PackageTest");
        fields.Controls.Add(actions);
        fields.SizeChanged += (_, _) =>
        {
            foreach (Label label in fields.Controls.OfType<Label>()) label.MaximumSize = new(Math.Max(100, fields.ClientSize.Width - fields.Padding.Horizontal - 24 * DeviceDpi / 96), 0);
        };
        var close = new Button { Text = Localization.Get("AboutClose", language), AutoSize = true, DialogResult = DialogResult.Cancel, MinimumSize = new(110, 32) };
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
        footer.Controls.Add(close); Controls.Add(fields); Controls.Add(footer); CancelButton = close;
        void RefreshState()
        {
            var loaded = credentials.Load("ship24", out _);
            state.Text = Localization.Get(loaded is TrackingCredentialLoadResult.Loaded or TrackingCredentialLoadResult.Recovered ? "PackageCredentialSaved" : "PackageNoCredential", language);
            test.Enabled = loaded is TrackingCredentialLoadResult.Loaded or TrackingCredentialLoadResult.Recovered;
            delete.Enabled = loaded != TrackingCredentialLoadResult.Missing;
        }
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(keyInput.Text)) return;
            bool saved = credentials.Save("ship24", new TrackingCredential(keyInput.Text.Trim()));
            keyInput.Clear(); RefreshState();
            if (!saved) state.Text = Localization.Get("PackageStorageError", language);
        };
        delete.Click += (_, _) =>
        {
            if (MessageBox.Show(this, Localization.Get("PackageDeleteKeyConfirm", language), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            bool deleted = credentials.Delete("ship24"); keyInput.Clear(); RefreshState();
            if (!deleted) state.Text = Localization.Get("PackageStorageError", language);
        };
        test.Click += async (_, _) =>
        {
            actions.Enabled = false; keyInput.Enabled = false; state.Text = Localization.Get("PackageBusy", language);
            ITrackingProvider provider = providerFactory?.Invoke() ?? new Ship24TrackingProvider(credentials);
            try
            {
                bool ok = await provider.TestConnectionAsync(lifetime.Token);
                if (!IsDisposed) state.Text = Localization.Get(ok ? "PackageConnected" : "PackageCredentialError", language);
            }
            catch (TrackingProviderException ex) { if (!IsDisposed) state.Text = PackagePresentation.Error(new(PackageOperation.ProviderError, ex.Failure), language); }
            catch (OperationCanceledException) { }
            finally { (provider as IDisposable)?.Dispose(); if (!IsDisposed) { actions.Enabled = true; keyInput.Enabled = true; } }
        };
        FormClosing += (_, _) => { keyInput.Clear(); lifetime.Cancel(); };
        RefreshState(); NotesDialogStyle.Apply(this, SystemWidgetStyle.Minimal);
        if (testOnShown) Shown += (_, _) => { if (test.Enabled) test.PerformClick(); };
    }
    protected override void Dispose(bool disposing) { if (disposing && !IsDisposed) { lifetime.Cancel(); lifetime.Dispose(); } base.Dispose(disposing); }
}
