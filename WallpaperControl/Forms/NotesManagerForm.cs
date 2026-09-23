using System.Globalization;

namespace WallpaperControl
{
    internal sealed class NotesManagerForm : Form
    {
        private readonly NotesStore store;
        private readonly string language;
        private readonly SystemWidgetStyle style;
        private readonly bool darkMode;
        private readonly System.Windows.Forms.Timer dayTimer = new() { Interval = 30000 };
        private readonly ListView list = new() { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, ShowItemToolTips = true, Dock = DockStyle.Fill };
        private readonly Label status = new() { AutoSize = true, Dock = DockStyle.Top, MaximumSize = new Size(800, 0) };

        internal NotesManagerForm(NotesStore store, string language, SystemWidgetStyle style = SystemWidgetStyle.Minimal, bool? darkMode = null)
        {
            this.store = store; this.language = language; this.style = style; this.darkMode = darkMode ?? NotesDialogStyle.ResolveDarkMode();
            Text = Localization.Get("NotesManage", language);
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(840, 450); MinimumSize = new Size(680, 350);
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            foreach (var (key, width) in new[] { ("NotesStatus", 230), ("NotesEntryTitle", 300), ("NotesDate", 140), ("NotesTime", 130) })
                list.Columns.Add(Localization.Get(key, language), width);
            TableLayoutPanel footer = new() { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 2, RowCount = 1, Padding = new Padding(8) };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            FlowLayoutPanel actions = new() { Dock = DockStyle.Fill, AutoSize = true, Margin = Padding.Empty };
            Button close = new() { Text = Localization.Get("AboutClose", language), AutoSize = true, MinimumSize = new Size(110, 32), Anchor = AnchorStyles.Right | AnchorStyles.Bottom, DialogResult = DialogResult.Cancel };
            close.Click += (_, _) => Close();
            CancelButton = close;
            footer.Controls.Add(actions, 0, 0); footer.Controls.Add(close, 1, 0);
            AddAction(actions, "NotesNew", () => Edit(null));
            AddAction(actions, "NotesEdit", () => { if (Selected is NoteEntry e) Edit(e); });
            AddAction(actions, "NotesToggle", () => ToggleSelected());
            AddAction(actions, "NotesDelete", () => DeleteSelected());
            Controls.Add(list); Controls.Add(status); Controls.Add(footer);
            list.DoubleClick += (_, _) => { if (Selected is NoteEntry e) Edit(e); };
            list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter && Selected is NoteEntry entry) { Edit(entry); e.Handled = true; } };
            store.Changed += RefreshEntries;
            dayTimer.Tick += (_, _) => RefreshEntries(); dayTimer.Start();
            RefreshEntries();
            NotesDialogStyle.Apply(this, style, this.darkMode);
            list.ClientSizeChanged += (_, _) => ResizeLastColumn();
            ResizeLastColumn();
            if (store.LoadIssue) status.Text = Localization.Get("NotesLoadIssue", language);
        }

        private void ResizeLastColumn()
        {
            int fixedWidth = list.Columns[0].Width + list.Columns[2].Width + list.Columns[3].Width;
            list.Columns[1].Width = Math.Max(100 * list.DeviceDpi / 96, list.ClientSize.Width - fixedWidth);
        }

        private NoteEntry? Selected => list.SelectedItems.Count == 1 ? list.SelectedItems[0].Tag as NoteEntry : null;
        private void AddAction(FlowLayoutPanel panel, string key, Action action)
        {
            Button button = new() { Text = Localization.Get(key, language), AutoSize = true, MinimumSize = new Size(110, 32), Enabled = store.CanWrite };
            button.Click += (_, _) => action(); panel.Controls.Add(button);
        }
        private void Edit(NoteEntry? entry) { using NoteEditorForm editor = new(store, entry, language, style, darkMode); editor.ShowDialog(this); }
        internal bool ToggleSelected() => Report(Selected is NoteEntry e && store.Complete(e.Id, !e.IsCompletedOn(store.Now)));
        internal bool DeleteSelected() => Report(Selected is NoteEntry e && store.Delete(e.Id));
        private bool Report(bool success) { if (!success) status.Text = Localization.Get("NotesSaveError", language); return success; }

        private void RefreshEntries()
        {
            Guid? selected = Selected?.Id;
            list.BeginUpdate(); list.Items.Clear();
            CultureInfo culture = CultureInfo.GetCultureInfo(language);
            DateTime now = store.Now;
            foreach (NoteEntry entry in store.Entries.OrderBy(e => e.IsCompletedOn(now)).ThenBy(e => e.EffectiveDueDate(now)).ThenBy(e => e.DueTime).ThenBy(e => e.CreatedAt).ThenBy(e => e.Id))
            {
                string state = entry.IsCompletedOn(now) && entry.CompletedAt?.Date == now.Date
                    ? string.Format(culture, Localization.Get("NotesDoneAt", language), entry.CompletedAt.Value.ToString("t", culture))
                    : Localization.Get(entry.IsCompletedOn(now) ? "NotesCompleted" : "NotesOpen", language);
                ListViewItem item = new(new[] { state, entry.Title,
                    entry.RepeatsDaily ? Localization.Get("NotesRepeatDaily", language) : entry.DueDate?.ToString("d", culture) ?? "",
                    entry.DueTime?.ToString("t", culture) ?? "" }) { Tag = entry, ToolTipText = state };
                list.Items.Add(item); item.Selected = entry.Id == selected;
            }
            list.EndUpdate();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { dayTimer.Dispose(); store.Changed -= RefreshEntries; list.Dispose(); status.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
