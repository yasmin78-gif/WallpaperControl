namespace WallpaperControl;

internal enum NoteReminderAction { Complete, Snooze, AtDue, Dismiss }

/// <summary>A normal topmost tool window, never parented to Explorer's desktop band.</summary>
internal sealed class NoteReminderPopup : Form
{
    private readonly string language;
    private readonly Button later = new() { AutoSize = true, MinimumSize = new Size(100, 32) };
    private readonly ContextMenuStrip laterMenu = new();
    private NoteReminderAction[] laterOptions = Array.Empty<NoteReminderAction>();
    internal static NoteReminderAction[] LaterOptions(DateTime due, DateTime now) => now >= due
        ? new[] { NoteReminderAction.Snooze }
        : due - now > TimeSpan.FromMinutes(5)
            ? new[] { NoteReminderAction.Snooze, NoteReminderAction.AtDue }
            : new[] { NoteReminderAction.AtDue };
    private readonly Label title = new() { AutoSize = true, MaximumSize = new Size(420, 0) };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(420, 0) };
    private readonly TextBox description = new()
    {
        Multiline = true, ReadOnly = true, WordWrap = true, ScrollBars = ScrollBars.Vertical,
        BorderStyle = BorderStyle.None, Size = new Size(420, 120), TabStop = false,
        Margin = new Padding(3, 8, 3, 8)
    };
    private readonly Label error = new() { AutoSize = true, MaximumSize = new Size(420, 0) };
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams value = base.CreateParams;
            value.ExStyle |= 0x08000000 | 0x00000080; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
            return value;
        }
    }

    internal NoteReminderPopup(string language, Action<NoteReminderAction> action)
    {
        this.language = language;
        Text = Localization.Get("NotesReminder", language);
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        ShowInTaskbar = false; MaximizeBox = MinimizeBox = false; TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        TableLayoutPanel layout = new() { AutoSize = true, ColumnCount = 1, Padding = new Padding(16) };
        title.UseMnemonic = status.UseMnemonic = false;
        layout.Controls.Add(title); layout.Controls.Add(status); layout.Controls.Add(description); layout.Controls.Add(error);
        FlowLayoutPanel actions = new() { AutoSize = true, MaximumSize = new Size(440, 0), WrapContents = true };
        later.Click += (_, _) =>
        {
            if (laterOptions.Length == 1) action(laterOptions[0]);
            else if (laterOptions.Length > 1) laterMenu.Show(later, new Point(0, later.Height));
        };
        foreach (var item in new[] { ("NotesCompleted", NoteReminderAction.Complete), ("NotesPopupClose", NoteReminderAction.Dismiss) })
        {
            Button button = new() { AutoSize = true, MinimumSize = new Size(100, 32), Text = Localization.Get(item.Item1, language) };
            button.Click += (_, _) => action(item.Item2);
            actions.Controls.Add(button);
            if (item.Item2 == NoteReminderAction.Complete) actions.Controls.Add(later);
        }
        foreach (var item in new[] { ("NotesPopupSnooze", NoteReminderAction.Snooze), ("NotesPopupAtDueAction", NoteReminderAction.AtDue) })
        {
            var menuItem = laterMenu.Items.Add(Localization.Get(item.Item1, language));
            menuItem.Click += (_, _) => action(item.Item2);
        }
        layout.Controls.Add(actions); Controls.Add(layout);
        NotesDialogStyle.Apply(this, SystemWidgetStyle.Minimal, null);
        description.BackColor = BackColor;
        description.ForeColor = ForeColor;
        description.AccessibleName = Localization.Get("NotesDescription", language);
        error.ForeColor = Color.IndianRed;
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; action(NoteReminderAction.Dismiss); } };
    }

    internal void UpdateContent(NoteEntry entry, DateTime due, DateTime now)
    {
        title.Text = entry.Title;
        var options = LaterOptions(due, now);
        if (!laterOptions.SequenceEqual(options))
        {
            laterMenu.Close();
            laterOptions = options;
            later.Text = options.Length > 1 ? Localization.Get("NotesPopupLater", language) + " ▾"
                : Localization.Get(options[0] == NoteReminderAction.AtDue ? "NotesPopupAtDueAction" : "NotesPopupSnooze", language);
        }
        // The timer refreshes the due status; leave the reading/scroll position alone.
        if (description.Text != entry.Description) description.Text = entry.Description;
        description.Visible = !string.IsNullOrWhiteSpace(entry.Description);
        var culture = System.Globalization.CultureInfo.GetCultureInfo(language);
        status.Text = now < due
            ? string.Format(culture, Localization.Get("NotesPopupUpcoming", language), Math.Ceiling((due - now).TotalMinutes))
            : string.Format(culture, Localization.Get("NotesPopupDue", language), due.ToString("g", culture));
        PerformLayout();
        Rectangle area = (Visible ? Screen.FromControl(this) : Screen.FromPoint(Cursor.Position)).WorkingArea;
        int gap = 16 * DeviceDpi / 96;
        Location = new Point(Math.Max(area.Left, area.Right - Width - gap), Math.Max(area.Top, area.Bottom - Height - gap));
    }

    internal void ShowSaveError() => error.Text = Localization.Get("NotesSaveError", language);
    protected override void OnVisibleChanged(EventArgs e)
    {
        if (!Visible) laterMenu.Close();
        base.OnVisibleChanged(e);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) laterMenu.Dispose();
        base.Dispose(disposing);
    }
}

