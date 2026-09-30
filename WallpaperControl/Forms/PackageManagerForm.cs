namespace WallpaperControl;

internal sealed class PackageManagerForm : Form
{
    private readonly PackageTrackingService service;
    private readonly string language;
    private readonly CancellationTokenSource lifetime = new();
    private readonly ListView list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, ShowItemToolTips = true };
    private readonly Label status = new() { Dock = DockStyle.Top, AutoSize = true, MaximumSize = new(780, 0) };
    private readonly FlowLayoutPanel actions = new() { Dock = DockStyle.Fill, AutoSize = true };
    internal PackageManagerForm(PackageTrackingService service, string language)
    {
        this.service = service; this.language = language;
        Text = Localization.Get("PackageManage", language);
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new(96, 96); ClientSize = new(860, 500); MinimumSize = new(650, 360);
        ShowInTaskbar = false; StartPosition = FormStartPosition.CenterParent;
        foreach (var (key, width) in new[] { ("PackageName", 210), ("PackageNumber", 220), ("NotesStatus", 160), ("PackageUpdatedTitle", 200) }) list.Columns.Add(Localization.Get(key, language), width);
        var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 2, Padding = new Padding(8) };
        footer.ColumnStyles.Add(new(SizeType.Percent, 100)); footer.ColumnStyles.Add(new(SizeType.AutoSize));
        var close = new Button { Text = Localization.Get("AboutClose", language), AutoSize = true, MinimumSize = new(110, 32), DialogResult = DialogResult.Cancel };
        footer.Controls.Add(actions, 0, 0); footer.Controls.Add(close, 1, 0); CancelButton = close;
        Button Button(string key, Action action) { var b = new Button { Text = Localization.Get(key, language), AutoSize = true, MinimumSize = new(100, 32) }; b.Click += (_, _) => action(); actions.Controls.Add(b); return b; }
        Button("PackageAdd", () => { using var editor = new PackageEditorForm(service, null, language); editor.ShowDialog(this); });
        Button("PackageDetails", Details);
        var refresh = Button("PackageRefresh", () => { });
        refresh.Click += async (_, _) =>
        {
            try { var result = await service.RefreshAsync(lifetime.Token); if (!IsDisposed && result.Outcome != PackageOperation.Success) status.Text = PackagePresentation.Error(result, language); }
            catch (OperationCanceledException) { }
        };
        list.DoubleClick += (_, _) => Details();
        Controls.Add(list); Controls.Add(status); Controls.Add(footer);
        service.Changed += RefreshList; RefreshList();
        FormClosing += (_, _) => lifetime.Cancel(); NotesDialogStyle.Apply(this, SystemWidgetStyle.Minimal);
    }
    private void Details()
    { if (list.SelectedItems.Count > 0 && list.SelectedItems[0].Tag is Guid id) { using var detail = new PackageDetailsForm(service, id, language); detail.ShowDialog(this); } }
    private void RefreshList()
    {
        if (IsDisposed) return;
        Guid? selected = list.SelectedItems.Count > 0 ? list.SelectedItems[0].Tag as Guid? : null;
        actions.Enabled = !service.Busy && service.CanWrite;
        status.Text = Localization.Get(service.Busy ? "PackageBusy" : service.LoadIssue ? "PackageLoadIssue" : service.RefreshFailures.Count > 0 ? "PackageRefreshError" : "PackageLocalActions", language);
        list.BeginUpdate(); list.Items.Clear();
        foreach (var s in service.Shipments)
        {
            var row = new ListViewItem(new[] { PackagePresentation.Name(s, language), PackagePresentation.CarrierNumber(s, false), PackagePresentation.ShipmentStatus(s, language),
                AmazonLogistics.IsLocal(s) ? Localization.Get("PackageAmazonOnly", language) : PackagePresentation.Date(s.LastSuccessfulRefresh, language) }) { Tag = s.Id };
            row.ToolTipText = string.Join(" · ", row.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(i => i.Text));
            list.Items.Add(row); row.Selected = selected == s.Id;
        }
        list.EndUpdate();
    }
    protected override void Dispose(bool disposing) { if (disposing && !IsDisposed) { service.Changed -= RefreshList; lifetime.Cancel(); lifetime.Dispose(); } base.Dispose(disposing); }
}
