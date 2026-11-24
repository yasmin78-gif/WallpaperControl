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
        private readonly CheckBox popup = new() { AutoSize = true };
        private readonly ComboBox popupLead = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
        private readonly ComboBox repeat = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
        private readonly Label repeatHint = new() { AutoSize = true, MaximumSize = new Size(500, 0) };
        private readonly FlowLayoutPanel customRow = new() { AutoSize = true, WrapContents = false };
        private readonly TextBox interval = new() { Text = "1", Width = 65, MaxLength = 10 };
        private readonly ComboBox unit = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
        private readonly Label customSummary = new() { AutoSize = true };
        private bool completionChanged;
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
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            MaximizeBox = MinimizeBox = false;
            AutoSize = true;
            AutoScroll = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            TableLayoutPanel layout = new() { AutoSize = true, ColumnCount = 1, Padding = new Padding(18), Width = 550 };
            Controls.Add(layout);
            AddLabel(layout, "NotesEntryTitle"); layout.Controls.Add(titleBox);
            AddLabel(layout, "NotesDescription"); layout.Controls.Add(descriptionBox);
            AddLabel(layout, "NotesRepeat"); layout.Controls.Add(repeat);
            repeat.Items.AddRange(new object[] { Localization.Get("NotesRepeatNone", language), Localization.Get("NotesRepeatDaily", language), Localization.Get("NotesRepeatMonthly", language), Localization.Get("NotesRepeatYearly", language), Localization.Get("NotesRepeatCustom", language) });
            repeat.SelectedIndex = (int)original.EffectiveRepeat;
            customRow.Controls.Add(new Label { Text = Localization.Get("NotesRepeatEvery", language), AutoSize = true, Anchor = AnchorStyles.Left });
            customRow.Controls.Add(interval); customRow.Controls.Add(unit);
            unit.Items.AddRange(new object[] { Localization.Get("NotesUnitDays", language), Localization.Get("NotesUnitWeeks", language), Localization.Get("NotesUnitMonths", language), Localization.Get("NotesUnitYears", language) });
            interval.Text = original.RepeatInterval.ToString(System.Globalization.CultureInfo.InvariantCulture);
            unit.SelectedIndex = (int)original.RepeatUnit;
            layout.Controls.Add(customRow); layout.Controls.Add(customSummary);
            interval.TextChanged += (_, _) => UpdateCustomSummary();
            unit.SelectedIndexChanged += (_, _) => UpdateCustomSummary();
            repeatHint.Text = Localization.Get("NotesDailyHint", language); layout.Controls.Add(repeatHint);
            reminder.Text = Localization.Get("NotesReminder", language); layout.Controls.Add(reminder);
            AddLabel(layout, "NotesDate"); layout.Controls.Add(date);
            timed.Text = Localization.Get("NotesUseTime", language); layout.Controls.Add(timed); layout.Controls.Add(time);
            popup.Text = Localization.Get("NotesPopup", language); layout.Controls.Add(popup);
            popupLead.Items.AddRange(new object[] { Localization.Get("NotesPopupAtDue", language),
                Localization.Get("NotesPopupFiveBefore", language), Localization.Get("NotesPopupTenBefore", language) });
            popupLead.SelectedIndex = original.PopupLeadMinutes / 5;
            popup.Checked = original.PopupReminder;
            layout.Controls.Add(popupLead);
            popup.CheckedChanged += (_, _) => UpdateDateControls();
            completed.Text = Localization.Get("NotesCompleted", language); completed.Visible = existing; layout.Controls.Add(completed);
            layout.Controls.Add(error);
            FlowLayoutPanel actions = new() { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            Button delete = ActionButton("NotesDelete"); delete.Visible = existing; delete.Enabled = store.CanWrite;
            delete.Click += (_, _) => DeleteEntry(); actions.Controls.Add(delete);
            Button cancel = ActionButton("SettingsCancel"); cancel.DialogResult = DialogResult.Cancel; actions.Controls.Add(cancel); CancelButton = cancel;
            Button save = ActionButton("SettingsSave"); save.Enabled = store.CanWrite; save.Click += (_, _) => SaveEntry(); actions.Controls.Add(save);
            layout.Controls.Add(actions);
            // Explicit cells keep hidden custom rows from shifting native combo row styles.
            layout.RowCount = layout.Controls.Count;
            for (int row = 0; row < layout.Controls.Count; row++)
            {
                Control control = layout.Controls[row];
                control.TabIndex = row;
                layout.SetCellPosition(control, new TableLayoutPanelCellPosition(0, row));
                RowStyle rowStyle = new(SizeType.AutoSize);
                layout.RowStyles.Add(rowStyle);
                if (control is ComboBox combo)
                {
                    rowStyle.SizeType = SizeType.Absolute;
                    rowStyle.Height = combo.Height + combo.Margin.Vertical;
                    combo.SizeChanged += (_, _) => rowStyle.Height = combo.Height + combo.Margin.Vertical;
                }
            }
            // No AcceptButton: Enter belongs to the multiline description; Tab/Space activates Save.
            titleBox.Text = original.Title; descriptionBox.Text = original.Description;
            reminder.Checked = original.HasReminder; timed.Checked = original.DueTime.HasValue;
            if (original.DueDate is DateOnly d) date.Value = new DateTime(Math.Clamp(d.ToDateTime(TimeOnly.MinValue).Ticks, date.MinDate.Ticks, date.MaxDate.Ticks));
            if (original.DueTime is TimeOnly t) time.Value = DateTime.Today.Add(t.ToTimeSpan());
            date.Format = DateTimePickerFormat.Custom;
            date.CustomFormat = System.Globalization.CultureInfo.GetCultureInfo(language).DateTimeFormat.ShortDatePattern;
            time.Format = DateTimePickerFormat.Custom;
            time.CustomFormat = System.Globalization.CultureInfo.GetCultureInfo(language).DateTimeFormat.ShortTimePattern;
            completed.Checked = original.IsCompletedOn(store.Now);
            repeat.SelectedIndexChanged += (_, _) =>
            {
                completed.Checked = (original with { RepeatsDaily = repeat.SelectedIndex == 1, Repeat = (NoteRepeat)repeat.SelectedIndex }).IsCompletedOn(store.Now);
                completionChanged = false;
                UpdateDateControls();
            };
            completed.CheckedChanged += (_, _) => completionChanged = true;
            reminder.CheckedChanged += (_, _) => UpdateDateControls(); timed.CheckedChanged += (_, _) => UpdateDateControls();
            UpdateDateControls();
            NotesDialogStyle.Apply(this, style, darkMode);
            error.ForeColor = (darkMode ?? NotesDialogStyle.ResolveDarkMode()) ? Color.FromArgb(245, 160, 145) : Color.Firebrick;
        }

        private void AddLabel(TableLayoutPanel layout, string key) => layout.Controls.Add(new Label { Text = Localization.Get(key, language), AutoSize = true, Margin = new Padding(3, 10, 3, 3) });
        protected override void OnShown(EventArgs e)
        {
            MaximumSize = Screen.FromControl(this).WorkingArea.Size;
            base.OnShown(e);
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            MaximumSize = Screen.FromControl(this).WorkingArea.Size;
        }
        private Button ActionButton(string key) => new() { Text = Localization.Get(key, language), AutoSize = true, MinimumSize = new Size(105, 32), Margin = new Padding(3, 12, 3, 3) };
        private void UpdateDateControls()
        {
            bool daily = repeat.SelectedIndex == 1;
            bool recurring = repeat.SelectedIndex > 0;
            customRow.Visible = customSummary.Visible = repeat.SelectedIndex == 4;
            UpdateCustomSummary();
            if (recurring && !reminder.Checked) reminder.Checked = true;
            reminder.Enabled = !recurring;
            repeatHint.Visible = daily;
            date.Enabled = timed.Enabled = reminder.Checked;
            time.Enabled = reminder.Checked && timed.Checked;
            popup.Enabled = time.Enabled;
            popupLead.Enabled = popup.Enabled && popup.Checked;
            completed.Text = Localization.Get(daily ? "NotesCompletedToday" : "NotesCompleted", language);
        }

        private bool TryInterval(out int value) => int.TryParse(interval.Text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out value) && value > 0;

        private void UpdateCustomSummary()
        {
            customSummary.Text = TryInterval(out int value) ? NoteRecurrence.Summary(new NoteEntry { Repeat = NoteRepeat.Custom, RepeatInterval = value, RepeatUnit = (NoteRepeatUnit)Math.Max(0, unit.SelectedIndex) }, language)
                : Localization.Get("NotesIntervalInvalid", language);
        }

        internal bool SaveEntry()
        {
            if (string.IsNullOrWhiteSpace(titleBox.Text)) { error.Text = Localization.Get("NotesTitleRequired", language); titleBox.Focus(); return false; }
            if (repeat.SelectedIndex == 4 && !TryInterval(out _)) { error.Text = customSummary.Text; interval.Focus(); return false; }
            DateOnly selectedDate = DateOnly.FromDateTime(date.Value);
            bool sameRhythm = original.EffectiveRepeat == (NoteRepeat)repeat.SelectedIndex && original.DueDate == (reminder.Checked ? selectedDate : (DateOnly?)null)
                && (repeat.SelectedIndex != 4 || (TryInterval(out int count) && count == original.RepeatInterval && unit.SelectedIndex == (int)original.RepeatUnit));
            NoteEntry draft = original with
            {
                Title = titleBox.Text.Trim(), Description = descriptionBox.Text, HasReminder = reminder.Checked,
                DueDate = reminder.Checked ? DateOnly.FromDateTime(date.Value) : null,
                DueTime = reminder.Checked && timed.Checked ? new TimeOnly(time.Value.Hour, time.Value.Minute) : null,
                RepeatsDaily = repeat.SelectedIndex == 1,
                Repeat = (NoteRepeat)repeat.SelectedIndex,
                RepeatInterval = TryInterval(out int intervalValue) ? intervalValue : 1,
                RepeatUnit = (NoteRepeatUnit)Math.Max(0, unit.SelectedIndex),
                RepeatAnchor = sameRhythm ? original.RepeatAnchor : selectedDate,
                RepeatCompletedThrough = completionChanged && completed.Checked ? sameRhythm ? original.EffectiveDueDate(store.Now) : selectedDate : sameRhythm ? original.RepeatCompletedThrough : null,
                PopupReminder = popup.Enabled && popup.Checked,
                PopupLeadMinutes = Math.Max(0, popupLead.SelectedIndex) * 5,
                // Merely editing across midnight must not check off the new day's task.
                IsCompleted = completionChanged || !sameRhythm ? completed.Checked : original.IsCompleted,
                CompletedAt = completionChanged || !sameRhythm ? completed.Checked ? store.Now : null : original.CompletedAt
            };
            if (draft.ExtendedRepeat && !draft.IsValid) { error.Text = Localization.Get("NotesIntervalTooLarge", language); return false; }
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
