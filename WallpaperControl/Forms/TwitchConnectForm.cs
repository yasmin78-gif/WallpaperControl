namespace WallpaperControl;

internal sealed class TwitchConnectForm : Form
{
    private readonly TwitchService service;
    private readonly string language;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Label status = new() { AutoSize = true, MaximumSize = new(480, 0) };
    private readonly TextBox code = new() { ReadOnly = true, Width = 300 };
    internal TwitchConnectForm(TwitchService service, string language, bool dark)
    {
        this.service = service; this.language = language;
        Text = Localization.Get("TwitchConnect", language); FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MaximizeBox = MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new(530, 230);
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new(20), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        Controls.Add(layout); status.Text = Localization.Get("TwitchAuthorizing", language); layout.Controls.Add(status); layout.Controls.Add(code);
        var cancel = new Button { AutoSize = true, Text = Localization.Get("TwitchCancel", language) }; cancel.Click += (_, _) => Close(); layout.Controls.Add(cancel);
        BackColor = AppTheme.WindowBackground(dark); ForeColor = AppTheme.TextPrimary(dark);
        SettingsControlTheme.Apply(Controls, dark, BackColor, ForeColor, AppTheme.InputBackground(dark), AppTheme.ControlBackground(dark));
    }
    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        bool success = await service.ConnectAsync(value =>
        {
            code.Text = value.UserCode; status.Text = Localization.Get("TwitchEnterCode", language);
            if (!TwitchLinks.Open(value.VerificationUri)) { status.Text = Localization.Get("TwitchBrowserError", language); lifetime.Cancel(); }
        }, lifetime.Token);
        if (IsDisposed || Disposing || lifetime.IsCancellationRequested) return;
        if (success) { DialogResult = DialogResult.OK; Close(); }
        else status.Text = Localization.Get(service.Error == TwitchError.None ? "TwitchBusy" : "TwitchError" + service.Error, language);
    }
    protected override void OnFormClosing(FormClosingEventArgs e) { lifetime.Cancel(); base.OnFormClosing(e); }
    protected override void Dispose(bool disposing) { if (disposing && !IsDisposed) { lifetime.Cancel(); lifetime.Dispose(); } base.Dispose(disposing); }
}
