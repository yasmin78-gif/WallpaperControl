namespace WallpaperControl
{
    /// <summary>Edits an independent draft; only explicit save/delete writes to the shared store.</summary>
    internal sealed class NoteEditorForm : Form
    {
        private readonly NotesStore store;
        private readonly NoteEntry original;
        private readonly bool existing;
        private readonly string language;
        private readonly TextBox titleBox = new() { MaxLength = 200, Dock = DockStyle.Fill };
        private readonly TextBox descriptionBox = new() { Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, MaxLength = 8000, Height = 100, Dock = DockStyle.Fill };
        private readonly CheckBox reminder = new() { AutoSize = true };
        private readonly CheckBox timed = new() { AutoSize = true };
        private readonly CheckBox completed = new() { AutoSize = true };
        private readonly DateTimePicker date = new() { Format = DateTimePickerFormat.Short, Width = 190 };
        private readonly DateTimePicker time = new() { Format = DateTimePickerFormat.Time, ShowUpDown = true, Width = 160 };
        private readonly Label error = new() { AutoSize = true, ForeColor = Color.Firebrick, MaximumSize = new Size(500, 0) };

        internal NoteEditorForm(NotesStore store, NoteEntry? entry, string language, SystemWidgetStyle style = SystemWidgetStyle.Minimal, bool? darkMode = null)
        {
            this.store = store;
            this.language = language;
            existing = entry != null;
            original = entry ?? new NoteEntry();
            Text = Localization.Get(existing ? "NotesEdit" : "NotesNew", language);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MaximizeBox = MinimizeBox = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            TableLayoutPanel layout = new() { AutoSize = true, ColumnCount = 1, Padding = new Padding(18), Width = 550 };
            Controls.Add(layout);
            AddLabel(layout, "NotesEntryTitle"); layout.Controls.Add(titleBox);
            AddLabel(layout, "NotesDescription"); layout.Controls.Add(descriptionBox);
            reminder.Text = Localization.Get("NotesReminder", language); layout.Controls.Add(reminder);
            AddLabel(layout, "NotesDate"); layout.Controls.Add(date);
            timed.Text = Localization.Get("NotesUseTime", language); layout.Controls.Add(timed); layout.Controls.Add(time);
            completed.Text = Localization.Get("NotesCompleted", language); completed.Visible = existing; layout.Controls.Add(completed);
            layout.Controls.Add(error);
            FlowLayoutPanel actions = new() { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            Button delete = ActionButton("NotesDelete"); delete.Visible = existing; delete.Enabled = store.CanWrite;
            delete.Click += (_, _) => DeleteEntry(); actions.Controls.Add(delete);
            Button cancel = ActionButton("SettingsCancel"); cancel.DialogResult = DialogResult.Cancel; actions.Controls.Add(cancel); CancelButton = cancel;
            Button save = ActionButton("SettingsSave"); save.Enabled = store.CanWrite; save.Click += (_, _) => SaveEntry(); actions.Controls.Add(save);
            layout.Controls.Add(actions);
            // No AcceptButton: Enter belongs to the multiline description; Tab/Space activates Save.
            titleBox.Text = original.Title; descriptionBox.Text = original.Description;
            reminder.Checked = original.HasReminder; timed.Checked = original.DueTime.HasValue;
            if (original.DueDate is DateOnly d) date.Value = new DateTime(Math.Clamp(d.ToDateTime(TimeOnly.MinValue).Ticks, date.MinDate.Ticks, date.MaxDate.Ticks));
            if (original.DueTime is TimeOnly t) time.Value = DateTime.Today.Add(t.ToTimeSpan());
            date.Format = DateTimePickerFormat.Custom;
            date.CustomFormat = System.Globalization.CultureInfo.GetCultureInfo(language).DateTimeFormat.ShortDatePattern;
            time.Format = DateTimePickerFormat.Custom;
            time.CustomFormat = System.Globalization.CultureInfo.GetCultureInfo(language).DateTimeFormat.ShortTimePattern;
            completed.Checked = original.IsCompleted;
            reminder.CheckedChanged += (_, _) => UpdateDateControls(); timed.CheckedChanged += (_, _) => UpdateDateControls();
            UpdateDateControls();
            NotesDialogStyle.Apply(this, style, darkMode);
            error.ForeColor = (darkMode ?? NotesDialogStyle.ResolveDarkMode()) ? Color.FromArgb(245, 160, 145) : Color.Firebrick;
        }

        private void AddLabel(TableLayoutPanel layout, string key) => layout.Controls.Add(new Label { Text = Localization.Get(key, language), AutoSize = true, Margin = new Padding(3, 10, 3, 3) });
        private Button ActionButton(string key) => new() { Text = Localization.Get(key, language), AutoSize = true, MinimumSize = new Size(105, 32), Margin = new Padding(3, 12, 3, 3) };
        private void UpdateDateControls() { date.Enabled = timed.Enabled = reminder.Checked; time.Enabled = reminder.Checked && timed.Checked; }

        internal bool SaveEntry()
        {
            if (string.IsNullOrWhiteSpace(titleBox.Text)) { error.Text = Localization.Get("NotesTitleRequired", language); titleBox.Focus(); return false; }
            NoteEntry draft = original with
            {
                Title = titleBox.Text.Trim(), Description = descriptionBox.Text, HasReminder = reminder.Checked,
                DueDate = reminder.Checked ? DateOnly.FromDateTime(date.Value) : null,
                DueTime = reminder.Checked && timed.Checked ? new TimeOnly(time.Value.Hour, time.Value.Minute) : null,
                IsCompleted = completed.Checked,
                CompletedAt = completed.Checked ? original.CompletedAt ?? DateTime.Now : null
            };
            if (!store.SaveEntry(draft)) { error.Text = Localization.Get("NotesSaveError", language); return false; }
            DialogResult = DialogResult.OK; Close(); return true;
        }

        internal bool DeleteEntry()
        {
            if (!existing || !store.Delete(original.Id)) { error.Text = Localization.Get("NotesSaveError", language); return false; }
            DialogResult = DialogResult.OK; Close(); return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                titleBox.Dispose(); descriptionBox.Dispose(); reminder.Dispose(); timed.Dispose(); completed.Dispose(); date.Dispose(); time.Dispose(); error.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
