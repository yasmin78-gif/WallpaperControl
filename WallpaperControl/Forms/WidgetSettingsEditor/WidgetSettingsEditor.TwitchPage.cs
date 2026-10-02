namespace WallpaperControl;

internal sealed partial class WidgetSettingsEditor
{
    private readonly CheckBox twitchEnabled = new(), twitchLocked = new(), twitchNotifications = new();
    private readonly ComboBox twitchStyle = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private readonly ComboBox twitchInterval = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private int TwitchIntervalMinutes => twitchInterval.SelectedIndex switch { 0 => 1, 2 => 10, 3 => 15, _ => 5 };
    private void LocalizeTwitchInterval(int minutes)
    {
        twitchInterval.Items.Clear();
        foreach (int value in new[] { 1, 5, 10, 15 }) twitchInterval.Items.Add(string.Format(Localization.Get("TwitchRefreshMinutesFormat", previewLanguageCode), value));
        twitchInterval.SelectedIndex = TwitchRefreshScheduler.NormalizeInterval(minutes) switch { 1 => 0, 10 => 2, 15 => 3, _ => 1 };
    }
    private readonly NumericUpDown twitchMaximum = new() { Minimum = 300, Maximum = 1400, Increment = 25, Width = 140 };
    private readonly Label twitchConnection = new() { AutoSize = true, MaximumSize = new(540, 0) };
    private readonly Button twitchConnect = new() { AutoSize = true }, twitchDisconnect = new() { AutoSize = true };
    private TwitchService? twitchService;
    private Action<IWin32Window, string, bool>? twitchLogin;
    private readonly CancellationTokenSource twitchLifetime = new();
    internal void ConfigureTwitch(TwitchService service, Action<IWin32Window, string, bool> login)
    {
        if (twitchService != null) twitchService.Changed -= UpdateTwitchConnection;
        twitchService = service; twitchLogin = login; service.Changed += UpdateTwitchConnection; UpdateTwitchConnection();
    }
    private void UpdateTwitchConnection()
    {
        if (IsDisposed || Disposing) return;
        if (IsHandleCreated && InvokeRequired) { BeginInvoke(UpdateTwitchConnection); return; }
        twitchConnection.Text = twitchService?.Error is TwitchError error && error != TwitchError.None ? Localization.Get("TwitchError" + error, previewLanguageCode)
            : twitchService?.Connected == true ? string.Format(Localization.Get("TwitchConnected", previewLanguageCode), twitchService.DisplayName)
            : Localization.Get(twitchService?.EmptyStateKey == "TwitchLoading" ? "TwitchLoading" : "TwitchDisconnected", previewLanguageCode);
        twitchConnect.Enabled = twitchService != null && !twitchService.Busy;
        twitchConnect.Visible = twitchService?.Connected != true;
        twitchDisconnect.Visible = twitchService?.Connected == true || twitchService?.Error == TwitchError.Storage;
        twitchDisconnect.Enabled = twitchService != null;
    }
    private void InitializeTwitchPage(TabPage page)
    {
        var layout = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Location = new(18, 18), Padding = new(0, 0, 24, 24) };
        page.Controls.Add(layout);
        AddWallpaperInfoControl(layout, new Label { Font = CreateOwnedFont("Segoe UI", 12, FontStyle.Bold) }, "TwitchTitle");
        AddWallpaperInfoControl(layout, twitchEnabled, "TwitchEnabled"); AddWallpaperInfoControl(layout, twitchLocked, "SettingsWidgetLocked");
        AddWallpaperInfoControl(layout, new Label(), "SettingsSystemStyle"); layout.Controls.Add(twitchStyle);
        AddWallpaperInfoControl(layout, new Label(), "SettingsCalendarMaximumHeight"); layout.Controls.Add(twitchMaximum);
        AddWallpaperInfoControl(layout, new Label(), "TwitchRefreshInterval"); layout.Controls.Add(twitchInterval);
        AddWallpaperInfoControl(layout, twitchNotifications, "TwitchLiveNotifications");
        layout.Controls.Add(twitchConnection); AddWallpaperInfoControl(layout, twitchConnect, "TwitchConnect"); AddWallpaperInfoControl(layout, twitchDisconnect, "TwitchDisconnect");
        AddWallpaperInfoControl(layout, new Label { MaximumSize = new(540, 0) }, "TwitchPrivacy");
        AddWallpaperInfoControl(layout, new Label { MaximumSize = new(540, 0) }, "TwitchRefreshHint");
        page.Enter += async (_, _) => { if (twitchService != null) await twitchService.InitializeAsync(twitchLifetime.Token); };
        twitchConnect.Click += (_, _) => { twitchLogin?.Invoke(this, previewLanguageCode, darkMode); UpdateTwitchConnection(); };
        twitchDisconnect.Click += (_, _) => { twitchService?.Disconnect(); UpdateTwitchConnection(); };
        LoadTwitchControls(initialWidgetSettings);
        twitchEnabled.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged(); twitchLocked.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
        twitchStyle.SelectedIndexChanged += (_, _) => NotifyWidgetPreviewChanged(); twitchMaximum.ValueChanged += (_, _) => NotifyWidgetPreviewChanged();
        twitchInterval.SelectedIndexChanged += (_, _) => NotifyWidgetPreviewChanged();
        twitchNotifications.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
    }
    private void LoadTwitchControls(WidgetSettings value)
    { twitchEnabled.Checked = value.TwitchEnabled; twitchLocked.Checked = value.TwitchLocked; twitchNotifications.Checked = value.TwitchLiveNotifications; twitchMaximum.Value = CalendarViewport.NormalizeMaximum(value.TwitchMaximumHeight); RefreshWidgetStyleChoices(twitchStyle, value.TwitchStyle); LocalizeTwitchInterval(value.TwitchRefreshMinutes); UpdateTwitchConnection(); }
    private void DisposeTwitchControls()
    { twitchLifetime.Cancel(); twitchLifetime.Dispose(); if (twitchService != null) twitchService.Changed -= UpdateTwitchConnection; }
}
