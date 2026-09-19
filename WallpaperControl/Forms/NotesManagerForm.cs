using System.Globalization;

namespace WallpaperControl
{
    internal sealed class NotesManagerForm : Form
    {
        private readonly NotesStore store;
        private readonly string language;
        private readonly ListView list = new() { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, Dock = DockStyle.Fill };
        private readonly Label status = new() { AutoSize = true, Dock = DockStyle.Top, MaximumSize = new Size(800, 0) };

        internal NotesManagerForm(NotesStore store, string language)
        {
            this.store = store; this.language = language;
            Text = Localization.Get("NotesManage", language);
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(840, 450); MinimumSize = new Size(680, 350);
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            foreach (var (key, width) in new[] { ("NotesStatus", 140), ("NotesEntryTitle", 390), ("NotesDate", 140), ("NotesTime", 130) })
                list.Columns.Add(Localization.Get(key, language), width);
            FlowLayoutPanel actions = new() { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8) };
            AddAction(actions, "NotesNew", () => Edit(null));
            AddAction(actions, "NotesEdit", () => { if (Selected is NoteEntry e) Edit(e); });
            AddAction(actions, "NotesToggle", () => ToggleSelected());
            AddAction(actions, "NotesDelete", () => DeleteSelected());
            Controls.Add(list); Controls.Add(status); Controls.Add(actions);
            list.DoubleClick += (_, _) => { if (Selected is NoteEntry e) Edit(e); };
            list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter && Selected is NoteEntry entry) { Edit(entry); e.Handled = true; } };
            store.Changed += RefreshEntries;
            RefreshEntries();
            if (store.LoadIssue) status.Text = Localization.Get("NotesLoadIssue", language);
        }

        private NoteEntry? Selected => list.SelectedItems.Count == 1 ? list.SelectedItems[0].Tag as NoteEntry : null;
        private void AddAction(FlowLayoutPanel panel, string key, Action action)
        {
            Button button = new() { Text = Localization.Get(key, language), AutoSize = true, MinimumSize = new Size(110, 32), Enabled = store.CanWrite };
            button.Click += (_, _) => action(); panel.Controls.Add(button);
        }
        private void Edit(NoteEntry? entry) { using NoteEditorForm editor = new(store, entry, language); editor.ShowDialog(this); }
        internal bool ToggleSelected() => Report(Selected is NoteEntry e && store.Complete(e.Id, !e.IsCompleted));
        internal bool DeleteSelected() => Report(Selected is NoteEntry e && store.Delete(e.Id));
        private bool Report(bool success) { if (!success) status.Text = Localization.Get("NotesSaveError", language); return success; }

        private void RefreshEntries()
        {
            Guid? selected = Selected?.Id;
            list.BeginUpdate(); list.Items.Clear();
            CultureInfo culture = CultureInfo.GetCultureInfo(language);
            foreach (NoteEntry entry in store.Entries.OrderBy(e => e.IsCompleted).ThenBy(e => e.DueDate).ThenBy(e => e.DueTime).ThenBy(e => e.CreatedAt).ThenBy(e => e.Id))
            {
                ListViewItem item = new(new[] { Localization.Get(entry.IsCompleted ? "NotesCompleted" : "NotesOpen", language), entry.Title,
                    entry.DueDate?.ToString("d", culture) ?? "", entry.DueTime?.ToString("t", culture) ?? "" }) { Tag = entry };
                list.Items.Add(item); item.Selected = entry.Id == selected;
            }
            list.EndUpdate();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { store.Changed -= RefreshEntries; list.Dispose(); status.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
