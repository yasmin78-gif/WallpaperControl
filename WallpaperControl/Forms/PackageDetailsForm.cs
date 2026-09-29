namespace WallpaperControl;

internal sealed class PackageDetailsForm : Form
{
    private readonly PackageTrackingService service;
    private readonly Guid id;
    private readonly string language;
    private readonly TextBox details = new SelectableDetailsTextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true, Cursor = Cursors.Default };
    private readonly FlowLayoutPanel actions = new() { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(10), FlowDirection = FlowDirection.RightToLeft };
    private readonly Button edit, delete;
    internal PackageDetailsForm(PackageTrackingService service, Guid id, string language)
    {
        this.service = service; this.id = id; this.language = language;
        Text = Localization.Get("PackageDetails", language); AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new(96, 96);
        ClientSize = new(650, 530); MinimumSize = new(440, 350); ShowInTaskbar = false; StartPosition = FormStartPosition.CenterScreen;
        Button Button(string key) { var b = new Button { Text = Localization.Get(key, language), AutoSize = true, MinimumSize = new(100, 32) }; actions.Controls.Add(b); return b; }
        var close = Button("AboutClose"); close.DialogResult = DialogResult.Cancel; CancelButton = close;
        edit = Button("PackageEdit"); delete = Button("NotesDelete");
        edit.Click += (_, _) => { var s = service.Shipments.FirstOrDefault(s => s.Id == id); if (s != null) { using var editor = new PackageEditorForm(service, s, language); editor.ShowDialog(this); } };
        delete.Click += (_, _) =>
        {
            if (MessageBox.Show(this, Localization.Get("PackageDeleteConfirm", language), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var result = service.Delete(id);
            if (result.Outcome == PackageOperation.Success) Close();
            else MessageBox.Show(this, PackagePresentation.Error(result, language), Text);
        };
        Controls.Add(details); Controls.Add(actions); service.Changed += RefreshData; RefreshData(); NotesDialogStyle.Apply(this, SystemWidgetStyle.Minimal);
        ActiveControl = close;
        Shown += (_, _) => details.Select(0, 0);
    }
    private void RefreshData()
    {
        if (IsDisposed) return;
        edit.Enabled = delete.Enabled = !service.Busy && service.CanWrite;
        var s = service.Shipments.FirstOrDefault(s => s.Id == id); if (s == null) return;
        var lines = new List<string> { PackagePresentation.Name(s, language), PackagePresentation.CarrierNumber(s, false), PackagePresentation.Status(s.StatusMilestone, language) };
        if (s.EstimatedDelivery != null) lines.Add(PackagePresentation.Format("PackageEta", PackagePresentation.Date(s.EstimatedDelivery, language), language));
        if (s.LastSuccessfulRefresh != null) lines.Add(PackagePresentation.Format("PackageUpdated", PackagePresentation.Date(s.LastSuccessfulRefresh, language), language));
        if (service.RefreshFailures.ContainsKey(id)) lines.Add(Localization.Get("PackageRefreshError", language));
        lines.Add(""); lines.Add(Localization.Get("PackageHistory", language));
        foreach (var e in PackagePresentation.NewestEventsFirst(s.Events))
        {
            lines.Add(PackagePresentation.EventDate(e, language));
            if (!string.IsNullOrWhiteSpace(e.Location)) lines.Add(e.Location);
            if (!string.IsNullOrWhiteSpace(e.Description)) lines.Add(e.Description);
            lines.Add("");
        }
        details.Text = string.Join(Environment.NewLine, lines);
    }
    protected override void Dispose(bool disposing) { if (disposing) service.Changed -= RefreshData; base.Dispose(disposing); }

    /// <summary>Keep native selection, scrolling and copying, but do not imply editable text with a caret.</summary>
    private sealed class SelectableDetailsTextBox : TextBox
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool HideCaret(IntPtr window);

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            // The native edit control creates its caret while processing WM_SETFOCUS.
            if (m.Msg == 0x0007) HideCaret(Handle);
        }
    }
}
