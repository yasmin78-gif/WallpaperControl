namespace WallpaperControl;
internal sealed class FeedEditorForm : Form
{
    private readonly TextBox title = new() { Width = 420, MaxLength = 512 }, address = new() { Width = 420, MaxLength = 2048 };
    private readonly ComboBox interval = new() { Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox notifications = new() { AutoSize = true };
    private readonly ListBox preview = new() { Width = 540, Height = 170 };
    private readonly Button save = new() { AutoSize = true, Enabled = false }, probe = new() { AutoSize = true };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new(540, 0) };
    private readonly CancellationTokenSource lifetime = new();
    private readonly FeedService service;
    private readonly FeedSource original;
    private readonly string language;
    private bool busy, disposed;
    private string verified = "";
    internal FeedSource? Result { get; private set; }
    internal FeedEditorForm(FeedService service, FeedSource? source, string language)
    {
        this.service = service; original = source ?? new(); this.language = language;
        Text = Localization.Get(source == null ? "FeedAdd" : "FeedEdit", language); ClientSize = new(590, 480);
        StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new(18) };
        Controls.Add(layout);
        foreach (var pair in new[] { ("FeedName", (Control)title), ("FeedAddress", address), ("TwitchRefreshInterval", interval) })
        { layout.Controls.Add(new Label { AutoSize = true, Text = Localization.Get(pair.Item1, language) }); layout.Controls.Add(pair.Item2); }
        foreach (int minutes in FeedSource.Intervals) interval.Items.Add(Localization.Get("FeedInterval" + minutes, language));
        interval.SelectedIndex = Math.Max(0, Array.IndexOf(FeedSource.Intervals, original.RefreshMinutes));
        title.Text = original.Title; address.Text = original.Url;
        probe.Text = Localization.Get("FeedCheck", language); save.Text = Localization.Get("SettingsSave", language);
        var buttons = new FlowLayoutPanel { AutoSize = true }; var cancel = new Button { AutoSize = true, Text = Localization.Get("SettingsCancel", language), DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(probe); buttons.Controls.Add(save); buttons.Controls.Add(cancel);
        notifications.Text = Localization.Get("FeedNotifications",language); notifications.Checked = original.Notifications; layout.Controls.Add(notifications);
        layout.Controls.Add(status); layout.Controls.Add(preview); layout.Controls.Add(buttons); CancelButton = cancel;
        address.TextChanged += (_, _) => { verified = ""; save.Enabled = false; };
        address.Leave += async (_, _) => { if (address.Text.Trim() != verified) await Probe(); };
        probe.Click += async (_, _) => await Probe();
        save.Click += (_, _) => { if (verified != address.Text.Trim()) return; Result = original with { Title = title.Text.Trim().Length == 0 ? new Uri(verified).Host : title.Text.Trim(), Url = verified, RefreshMinutes = FeedSource.Intervals[interval.SelectedIndex], Notifications = notifications.Checked }; DialogResult = DialogResult.OK; Close(); };
        Shown += async (_, _) => { if (address.Text.Length > 0) await Probe(); };
        NotesDialogStyle.Apply(this, SystemWidgetStyle.Minimal);
    }
    private async Task Probe()
    {
        if (busy || disposed) return; string url = address.Text.Trim(); busy = true; probe.Enabled = false;
        status.Text = Localization.Get("FeedChecking", language);
        try
        {
            var result = await service.ProbeAsync(url, lifetime.Token);
            if (disposed || address.Text.Trim() != url) return;
            verified = url; if (title.Text.Trim().Length == 0) title.Text = result.Title;
            preview.Items.Clear(); foreach (var entry in result.Entries.Take(8)) preview.Items.Add(entry.Title);
            status.Text = Localization.Get("FeedReady", language); save.Enabled = true;
        }
        catch (OperationCanceledException) when (disposed) { }
        catch (Exception) { if (!disposed) { status.Text = Localization.Get("FeedRefreshFailed", language); save.Enabled = false; } }
        finally { busy = false; if (!disposed) probe.Enabled = true; }
    }
    protected override void Dispose(bool disposing) { if (disposing && !disposed) { disposed = true; lifetime.Cancel(); lifetime.Dispose(); } base.Dispose(disposing); }
}
internal sealed class FeedManagerForm : Form
{
    private readonly ListBox list = new() { Dock = DockStyle.Fill, DisplayMember = "Title", FormattingEnabled = true };
    internal FeedManagerForm(FeedService service, FeedWidgetSettings configuration, Action<FeedWidgetSettings> changed, string language)
    {
        Text = Localization.Get("FeedManage", language); ClientSize = new(590, 340); StartPosition = FormStartPosition.CenterParent;
        var value = configuration.Clone(); Controls.Add(list);
        list.Format += (_, e) =>
        {
            if (e.ListItem is FeedSource source)
                e.Value = source.Notifications ? "🔔 " + source.Title : source.Title;
        };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true }; Controls.Add(actions);
        void Reload() { list.Items.Clear(); foreach (var source in value.Sources) list.Items.Add(source); }
        void Edit(bool add)
        {
            var selected = add ? null : list.SelectedItem as FeedSource; if (!add && selected == null || add && value.Sources.Count >= 20) return;
            using var dialog = new FeedEditorForm(service, selected, language);
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result == null) return;
            if (selected != null) value.Sources.RemoveAll(s => s.Id == selected.Id); value.Sources.Add(dialog.Result); changed(value.Clone()); Reload();
        }
        foreach (var pair in new[] { ("FeedAdd", (Action)(() => Edit(true))), ("FeedEdit", () => Edit(false)), ("NotesDelete", () => { if (list.SelectedItem is FeedSource source) { value.Sources.Remove(source); if (value.Selected == source.Id) value.Selected = null; changed(value.Clone()); Reload(); } }) })
        { var button = new Button { Text = Localization.Get(pair.Item1, language), AutoSize = true }; button.Click += (_, _) => pair.Item2(); actions.Controls.Add(button); }
        var close = new Button { Text = Localization.Get("AboutClose", language), AutoSize = true }; close.Click += (_, _) => Close(); actions.Controls.Add(close);
        Reload(); NotesDialogStyle.Apply(this, SystemWidgetStyle.Minimal);
    }
}
