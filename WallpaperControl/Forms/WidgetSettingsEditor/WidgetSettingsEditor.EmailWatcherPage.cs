namespace WallpaperControl;

internal sealed partial class WidgetSettingsEditor
{
    private readonly CheckBox emailEnabled = new(), emailLocked = new(), emailPreviewEnabled = new(), emailFullscreen = new();
    private readonly TextBox emailName = new(), emailServer = new(), emailUsername = new(), emailPassword = new() { UseSystemPasswordChar = true }, emailWebmail = new(), emailMailProgram = new();
    private readonly NumericUpDown emailPort = new() { Minimum = 1, Maximum = 65535 }, emailPoll = new() { Minimum = 15, Maximum = 3600 }, emailSeconds = new() { Minimum = 1, Maximum = 3600 }, emailHeight = new() { Minimum = 300, Maximum = 1400 };
    private readonly ComboBox emailPrograms = EmailCombo();
    private IReadOnlyList<EmailClientChoice> emailDetectedClients = [];
    private bool refreshingEmailPrograms;
    private readonly ComboBox emailSecurity = EmailCombo(), emailMode = EmailCombo(), emailSound = EmailCombo(), emailStyle = EmailCombo();
    private readonly CheckedListBox emailRulesList = new() { Width = 620, Height = 150, CheckOnClick = true };
    private readonly PictureBox emailPreview = new() { Size = new(390, 210), SizeMode = PictureBoxSizeMode.Zoom };
    private readonly Label emailTestStatus = new() { AutoSize = true, MaximumSize = new(620, 0) }, emailLiveStatus = new() { AutoSize = true, MaximumSize = new(620, 0) };
    private readonly Button emailTest = new() { AutoSize = true };
    private readonly Button emailSoundPreview = new() { AutoSize = true, Tag = "NotificationSoundPreview" };
    private readonly NotificationSounds emailPreviewSound = new();
    internal Action<EmailWatcherSound>? EmailPreviewSoundPlayer;
    private EmailWatcherSettings emailDraft = new();
    private string emailCipher = "", emailTestKey = "EmailPasswordHint";
    private readonly List<EmailRule> emailRules = [];
    private CancellationTokenSource? emailTestCancellation;
    private readonly System.Windows.Forms.Timer emailStatusTimer = new() { Interval = 1000 };
    internal Func<string>? EmailStatusSource;
    internal Func<EmailAccountSettings,CancellationToken,Task<string>> EmailConnectionTest=EmailMailbox.TestAsync;
    private static ComboBox EmailCombo() => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
    private void InitializeEmailWatcherPage(TabPage page)
    {
        var layout = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Location = new(18, 18), Width = 700 };
        page.Controls.Add(layout);
        AddWallpaperInfoControl(layout, new Label { Font = CreateOwnedFont("Segoe UI", 12, FontStyle.Bold) }, "EmailTitle");
        AddWallpaperInfoControl(layout, emailEnabled, "EmailEnabled");
        AddWallpaperInfoControl(layout, emailLiveStatus, "EmailDisabled");
        var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Width = 660 };
        table.ColumnStyles.Add(new(SizeType.Absolute, 240)); table.ColumnStyles.Add(new(SizeType.Absolute, 420)); layout.Controls.Add(table);
        void Row(string key, Control input) {
            int index = table.RowCount++; var label = new Label { Text = Localization.Get(key, previewLanguageCode), Tag = key, AutoSize = true, MaximumSize = new(230, 0), Margin = new(0, 6, 4, 6) };
            input.Width = input is NumericUpDown ? 100 : 390; input.Margin = new(0, 3, 0, 3); table.Controls.Add(label, 0, index); table.Controls.Add(input, 1, index);
        }
        Row("EmailAccountName", emailName); Row("EmailServer", emailServer); Row("EmailPort", emailPort); Row("EmailSecurity", emailSecurity);
        Row("EmailUsername", emailUsername); Row("EmailPassword", emailPassword); Row("EmailWebmail", emailWebmail); Row("EmailPolling", emailPoll);
        emailDetectedClients = EmailClientDiscovery.Discover();
        Row("EmailSelectProgram", emailPrograms);
        var programRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        emailMailProgram.Width = 255; programRow.Controls.Add(emailMailProgram);
        var browseProgram = new Button { AutoSize = true, Text = Localization.Get("EmailBrowseProgram", previewLanguageCode), Tag = "EmailBrowseProgram" };
        browseProgram.Click += (_, _) => {
            using var picker = new OpenFileDialog { Filter = "*.exe|*.exe", CheckFileExists = true, Multiselect = false };
            if (picker.ShowDialog(FindForm()) == DialogResult.OK) emailMailProgram.Text = picker.FileName;
        };
        programRow.Controls.Add(browseProgram); Row("EmailMailProgram", programRow);
        AddWallpaperInfoControl(layout, new Label { MaximumSize = new(620, 0) }, "EmailOpenTargetHint");
        AddWallpaperInfoControl(layout, emailTest, "EmailTestConnection"); layout.Controls.Add(emailTestStatus);
        AddWallpaperInfoControl(layout, new Label(), "EmailRules"); layout.Controls.Add(emailRulesList);
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false }; layout.Controls.Add(buttons);
        foreach (var (key, action) in new (string, Action)[] { ("EmailRuleAdd", () => EditEmailRule(false)), ("EmailRuleEdit", () => EditEmailRule(true)), ("EmailRuleDelete", DeleteEmailRule) }) {
            var button = new Button { AutoSize = true, Text = Localization.Get(key, previewLanguageCode), Tag = key }; button.Click += (_, _) => action(); buttons.Controls.Add(button);
        }
        var display = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Width = 660 }; display.ColumnStyles.Add(new(SizeType.Absolute, 240)); display.ColumnStyles.Add(new(SizeType.Absolute, 420)); layout.Controls.Add(display);
        foreach (var (key, input) in new (string, Control)[] { ("EmailMode", emailMode), ("EmailHideSeconds", emailSeconds), ("EmailSound", emailSound), ("SettingsSystemStyle", emailStyle), ("SettingsCalendarMaximumHeight", emailHeight) }) {
            int row = display.RowCount++; display.Controls.Add(new Label { Text = Localization.Get(key, previewLanguageCode), Tag = key, AutoSize = true, MaximumSize = new(230, 0) }, 0, row); if (input == emailSound) {
                var soundRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
                emailSound.Width = 250;
                emailSoundPreview.Text = Localization.Get("NotificationSoundPreview", previewLanguageCode);
                soundRow.Controls.Add(emailSound); soundRow.Controls.Add(emailSoundPreview); display.Controls.Add(soundRow, 1, row);
            } else display.Controls.Add(input, 1, row);
        }
        AddWallpaperInfoControl(layout, emailPreviewEnabled, "EmailPreviewEnabled"); AddWallpaperInfoControl(layout, emailFullscreen, "EmailHoldFullscreen"); AddWallpaperInfoControl(layout, emailLocked, "SettingsWidgetLocked");
        AddWallpaperInfoControl(layout, new Label { MaximumSize = new(620, 0) }, "EmailPrivacyHint");
        layout.Controls.Add(emailPreview);
        LoadEmailControls(initialWidgetSettings);
        emailPrograms.SelectedIndexChanged += (_, _) => {
            if (loadingControls || refreshingEmailPrograms) return;
            int selected = emailPrograms.SelectedIndex;
            if (selected == 0) emailMailProgram.Clear();
            else if (selected > 0 && selected <= emailDetectedClients.Count) emailMailProgram.Text = emailDetectedClients[selected - 1].Path;
        };
        emailMailProgram.TextChanged += (_, _) => RefreshEmailProgramSelection();
        foreach (var field in new[] { emailName, emailServer, emailUsername, emailWebmail, emailMailProgram }) field.TextChanged += (_, _) => EmailControlsChanged();
        emailPassword.TextChanged += (_, _) => {
            if (loadingControls) return;
            try { emailCipher = emailPassword.Text.Length==0 ? emailDraft.Account.ProtectedPassword : WindowsSecretProtector.Protect(emailPassword.Text); }
            catch (Exception) { emailCipher = ""; emailTestKey = "EmailStorageOrNetworkFailed"; emailTestStatus.Text = Localization.Get(emailTestKey, previewLanguageCode); }
            EmailControlsChanged();
        };
        foreach (var box in new[] { emailEnabled, emailLocked, emailPreviewEnabled, emailFullscreen }) box.CheckedChanged += (_, _) => EmailControlsChanged();
        foreach (var box in new[] { emailMode, emailStyle, emailSound, emailSecurity }) box.SelectedIndexChanged += (_, _) => EmailControlsChanged();
        foreach (var input in new[] { emailPort, emailPoll, emailSeconds, emailHeight }) input.ValueChanged += (_, _) => EmailControlsChanged();
        emailRulesList.ItemCheck += (_, e) => { if (loadingControls) return; emailRules[e.Index] = emailRules[e.Index] with { Enabled = e.NewValue == CheckState.Checked }; EmailControlsChanged(); };
        emailTest.Click += async (_, _) => await TestEmailConnectionAsync();
        emailSoundPreview.Click += (_, _) => {
            if (emailSound.SelectedIndex > 0 && Enum.IsDefined((EmailWatcherSound)emailSound.SelectedIndex))
                (EmailPreviewSoundPlayer ?? emailPreviewSound.PlayEmail)((EmailWatcherSound)emailSound.SelectedIndex);
        };
        emailStatusTimer.Tick += (_, _) => { if (!IsDisposed && EmailStatusSource != null) emailLiveStatus.Text = Localization.Get(EmailStatusSource(), previewLanguageCode); }; emailStatusTimer.Start();
    }
    private void EmailControlsChanged()
    { if (loadingControls) return; emailTestCancellation?.Cancel(); UpdateEmailPreview(); NotifyWidgetPreviewChanged(); }
    private void RefreshEmailChoices()
    {
        void Choices(ComboBox box, string[] keys) { int selected = box.SelectedIndex; box.Items.Clear(); foreach (string key in keys) box.Items.Add(Localization.Get(key, previewLanguageCode)); box.SelectedIndex = Math.Max(0, selected); }
        Choices(emailMode, ["EmailUntilClosed", "EmailAutomatic"]); Choices(emailSound, ["EmailNoSound", "EmailMagical", "EmailPremium", "EmailFanfare"]);
        Choices(emailSecurity, ["EmailTls", "EmailStartTls"]); RefreshWidgetStyleChoices(emailStyle, (SystemWidgetStyle)Math.Max(0, emailStyle.SelectedIndex));
        RefreshEmailProgramChoices(); RefreshEmailRules(); emailTestStatus.Text = Localization.Get(emailTestKey, previewLanguageCode); UpdateEmailPreview();
    }
    private void RefreshEmailProgramChoices()
    {
        refreshingEmailPrograms = true;
        try {
            emailPrograms.Items.Clear();
            emailPrograms.Items.Add(Localization.Get("EmailNoProgram", previewLanguageCode));
            foreach (var client in emailDetectedClients)
                emailPrograms.Items.Add(client.Name + (client.IsDefault ? " (" + Localization.Get("EmailWindowsDefault", previewLanguageCode) + ")" : ""));
            emailPrograms.Items.Add(Localization.Get("EmailCustomProgram", previewLanguageCode));
        }
        finally { refreshingEmailPrograms = false; }
        RefreshEmailProgramSelection();
    }
    private void RefreshEmailProgramSelection()
    {
        if (emailPrograms.Items.Count == 0) return;
        refreshingEmailPrograms = true;
        try {
            int match = -1;
            for (int i = 0; i < emailDetectedClients.Count; i++)
                if (string.Equals(emailDetectedClients[i].Path, emailMailProgram.Text.Trim(), StringComparison.OrdinalIgnoreCase)) { match = i; break; }
            emailPrograms.SelectedIndex = string.IsNullOrWhiteSpace(emailMailProgram.Text) ? 0 : match >= 0 ? match + 1 : emailPrograms.Items.Count - 1;
        }
        finally { refreshingEmailPrograms = false; }
    }
    private void LoadEmailControls(WidgetSettings settings)
    {
        emailTestCancellation?.Cancel(); emailDraft = settings.EmailWatcher.Clone(); emailCipher = emailDraft.Account.ProtectedPassword;
        emailName.Text = emailDraft.Account.DisplayName; emailServer.Text = emailDraft.Account.Server; emailUsername.Text = emailDraft.Account.Username; emailWebmail.Text = emailDraft.Account.WebmailUrl; emailMailProgram.Text = emailDraft.Account.MailProgramPath; emailPassword.Clear();
        emailPort.Value = Math.Clamp(emailDraft.Account.Port, 1, 65535); emailPoll.Value = Math.Clamp(emailDraft.Account.PollingSeconds, 15, 3600);
        emailEnabled.Checked = emailDraft.Enabled; emailLocked.Checked = emailDraft.Locked; emailPreviewEnabled.Checked = emailDraft.ShowPreview; emailFullscreen.Checked = emailDraft.HoldDuringFullscreen;
        emailSeconds.Value = Math.Clamp(emailDraft.AutoHideSeconds, 1, 3600); emailHeight.Value = CalendarViewport.NormalizeMaximum(emailDraft.MaximumHeight);
        emailRules.Clear(); emailRules.AddRange(emailDraft.Rules); RefreshEmailChoices();
        emailMode.SelectedIndex = (int)emailDraft.DisplayMode; emailSecurity.SelectedIndex = (int)emailDraft.Account.Security; emailStyle.SelectedIndex = (int)emailDraft.Style;
        emailSound.SelectedIndex = emailDraft.SoundEnabled ? (int)emailDraft.Sound : 0; UpdateEmailPreview();
    }
    private EmailWatcherSettings ReadEmailControls()
    {
        var value = emailDraft.Clone(); value.Account.DisplayName = emailName.Text.Trim(); value.Account.Server = emailServer.Text.Trim(); value.Account.Port = (int)emailPort.Value;
        value.Account.Username = emailUsername.Text.Trim(); value.Account.WebmailUrl = emailWebmail.Text.Trim(); value.Account.MailProgramPath = emailMailProgram.Text.Trim(); value.Account.ProtectedPassword = emailCipher;
        value.Account.Security = (EmailTransportSecurity)Math.Max(0, emailSecurity.SelectedIndex); value.Account.PollingSeconds = (int)emailPoll.Value;
        value.Enabled = emailEnabled.Checked; value.Locked = emailLocked.Checked; value.ShowPreview = emailPreviewEnabled.Checked; value.HoldDuringFullscreen = emailFullscreen.Checked;
        value.DisplayMode = (EmailWatcherDisplayMode)Math.Max(0, emailMode.SelectedIndex); value.Style = (SystemWidgetStyle)Math.Max(0, emailStyle.SelectedIndex);
        value.AutoHideSeconds = (int)emailSeconds.Value; value.MaximumHeight = (int)emailHeight.Value; value.Sound = (EmailWatcherSound)Math.Max(0, emailSound.SelectedIndex); value.SoundEnabled = value.Sound != EmailWatcherSound.None;
        value.Rules = emailRules.ToList(); return value;
    }
    private void RefreshEmailRules()
    {
        int selected = emailRulesList.SelectedIndex; emailRulesList.Items.Clear();
        foreach (var rule in emailRules) {
            string sender = rule.SenderCondition == EmailSenderCondition.None ? "" : Localization.Get(rule.SenderCondition == EmailSenderCondition.Address ? "EmailAddressCondition" : "EmailDomainCondition", previewLanguageCode) + ": " + rule.Sender;
            string subject = rule.SubjectContains.Length == 0 ? "" : Localization.Get("EmailSubjectCondition", previewLanguageCode) + ": " + rule.SubjectContains;
            string separator = sender.Length != 0 && subject.Length != 0 ? " · " + Localization.Get("EmailAnd", previewLanguageCode) + " · " : "";
            emailRulesList.Items.Add(sender + separator + subject, rule.Enabled);
        }
        if (selected >= 0 && selected < emailRules.Count) emailRulesList.SelectedIndex = selected;
    }
    private void EditEmailRule(bool edit)
    {
        int index = emailRulesList.SelectedIndex; if (edit && index < 0) return;
        using var dialog = new EmailRuleEditorForm(edit ? emailRules[index] : null, previewLanguageCode, darkMode);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK || dialog.Result == null) return;
        if (edit) emailRules[index] = dialog.Result; else emailRules.Add(dialog.Result);
        bool previous = loadingControls; loadingControls = true; try { RefreshEmailRules(); } finally { loadingControls = previous; } EmailControlsChanged();
    }
    private void DeleteEmailRule()
    {
        int selected = emailRulesList.SelectedIndex; if (selected < 0) return;
        emailRules.RemoveAt(selected); bool previous = loadingControls; loadingControls = true; try { RefreshEmailRules(); } finally { loadingControls = previous; } EmailControlsChanged();
    }
    private async Task TestEmailConnectionAsync()
    {
        var account = ReadEmailControls().Account;
        if (!account.IsConfigured) { emailTestKey = "EmailInvalidConfiguration"; emailTestStatus.Text = Localization.Get(emailTestKey, previewLanguageCode); return; }
        emailTestCancellation?.Cancel(); var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(35)); emailTestCancellation = cancellation; emailTest.Enabled = false;
        emailTestKey = "EmailConnecting"; emailTestStatus.Text = Localization.Get(emailTestKey, previewLanguageCode);
        try {
            string result = await Task.Run(() => EmailConnectionTest(account, cancellation.Token));
            if(cancellation.IsCancellationRequested)result="EmailCancelled";
            if (!IsDisposed && emailTestCancellation == cancellation) { emailTestKey = result; emailTestStatus.Text = Localization.Get(result, previewLanguageCode); }
        } finally { if (!IsDisposed) emailTest.Enabled = true; if (emailTestCancellation == cancellation) emailTestCancellation = null; cancellation.Dispose(); }
    }
    private void UpdateEmailPreview()
    {
        emailSoundPreview.Enabled = emailSound.SelectedIndex > 0;
        using var widget = new EmailWatcherWidgetForm(ReadEmailControls(), previewLanguageCode, _ => { }, () => { }, () => { }); widget.ApplyTheme(darkMode);
        widget.SetNotices([new(EmailMessageKey.Inbox(Guid.Empty, 1, 1), Localization.Get("EmailSampleSender", previewLanguageCode), "sample@example.invalid", Localization.Get("EmailSampleSubject", previewLanguageCode), DateTimeOffset.UtcNow, Localization.Get("EmailSamplePreview", previewLanguageCode))]);
        var image = widget.RenderBitmap(1200, 96); var previous = emailPreview.Image; emailPreview.Image = image; previous?.Dispose();
    }
    private void DisposeEmailControls() { loadingControls=true; emailCipher=""; emailTestCancellation?.Cancel(); emailStatusTimer.Dispose(); emailPreviewSound.Dispose(); emailPassword.Clear(); emailPreview.Image?.Dispose(); emailPreview.Image = null; }
}
