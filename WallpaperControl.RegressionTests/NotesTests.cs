extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Win32;

internal static class NotesTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly string[] ExpectedGroupKeys = { "NotesUndated", "NotesOverdue", "NotesToday", "NotesTomorrow", "" };
    private static readonly DateTime Now = new(2026, 9, 20, 12, 0, 0);
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, Members)!.GetValue(value)!;
    private static object? Invoke(object value, string name, params object[] args) => value.GetType().GetMethod(name, Members)!.Invoke(value, args);
    private static string FixturePath() => Path.Combine(AppContext.BaseDirectory, "test-data", "notes-" + Guid.NewGuid().ToString("N"), "notes.json");
    private static App.NoteEntry Note(string title, int? day = null, int? hour = null) => new()
    {
        Title = title, CreatedAt = Now, HasReminder = day.HasValue,
        DueDate = day.HasValue ? DateOnly.FromDateTime(Now.AddDays(day.Value)) : null,
        DueTime = hour.HasValue ? new TimeOnly(hour.Value, 0) : null
    };

    internal static void Run(Action<bool, string> check)
    {
        ModelAndTime(check); Storage(check);
        // Run synchronously on an STA thread; Task captures and rethrows assertion failures to the runner.
        using Task work = new(() => { Dialogs(check); DialogAppearance(check); Widget(check); Settings(check); Manager(check); });
        Thread thread = new(() => work.RunSynchronously(TaskScheduler.Default));
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); work.GetAwaiter().GetResult();
    }

    private static void ModelAndTime(Action<bool, string> check)
    {
        var note = Note("Undated"); var today = Note("Date only", 0); var timed = Note("Timed", 0, 14);
        check(note.IsValid && !note.HasReminder && note.DueDate == null && note.DueTime == null, "Notes undated model is valid");
        check(today.IsValid && today.DueTime == null && today.IsDueToday(Now) && !today.IsOverdue(Now), "Notes date-only reminder is due today without hidden time");
        check(timed.IsValid && !timed.IsDueToday(Now) && !timed.IsOverdue(Now), "Notes timed reminder is pending before its local time");
        check(timed.IsDueToday(Now.AddHours(2)) && !timed.IsOverdue(Now.AddHours(2)), "Notes timed reminder becomes due exactly at its time");
        check(timed.IsOverdue(Now.AddHours(2).AddSeconds(1)), "Notes timed reminder becomes overdue after due time");
        check(today.IsOverdue(Now.AddDays(1)) && !today.IsOverdue(Now.Date.AddHours(23)), "Notes date-only overdue status changes at next local day");
        check(Note("Yesterday", -1).IsOverdue(Now) && !Note("Tomorrow", 1).IsDueToday(Now), "Notes startup recognizes past and future dates");
        foreach (App.NoteEntry invalid in new[] { note with { Id = Guid.Empty }, note with { Title = " " }, note with { DueTime = new TimeOnly(9, 0) },
            note with { HasReminder = true }, note with { DueDate = DateOnly.FromDateTime(Now) }, note with { IsCompleted = true },
            note with { CreatedAt = default }, today with { DueDate = new DateOnly(1000, 1, 1) }, note with { Description = new string('a', 8001) } })
            check(!invalid.IsValid, "Notes reject invalid identity, content or date/status combination");
        var done = today with { IsCompleted = true, CompletedAt = Now };
        check(done.IsValid && !done.IsOverdue(Now.AddDays(2)), "Notes completed entries have completion timestamp and no overdue status");
        App.NoteEntry[] entries = { Note("Future", 3), Note("Tomorrow late", 1, 18), note, Note("Yesterday", -1), timed, today, Note("Tomorrow early", 1, 9), done };
        var groups = App.NoteGrouping.Build(entries, Now);
        check(groups.Select(g => g.Key).SequenceEqual(ExpectedGroupKeys), "Notes group order is undated, overdue, today, tomorrow, future date");
        check(groups[2].Entries[0].Id == today.Id && groups[2].Entries[1].Id == timed.Id, "Notes date-only entries precede timed entries consistently");
        check(groups[3].Entries.Select(e => e.DueTime).SequenceEqual(new TimeOnly?[] { new(9, 0), new(18, 0) }), "Notes date groups sort times chronologically");
        check(groups.Sum(g => g.Entries.Count) == 7 && groups.All(g => g.Entries.Count > 0), "Notes completed entries and empty groups are excluded");
        check(App.NoteGrouping.Build(new[] { done }, Now).Count == 0, "Notes no open entries means no empty groups");
        check(App.NoteGrouping.Build(new[] { Note("Tomorrow", 1) }, Now.AddDays(1))[0].Key == "NotesToday", "Notes tomorrow-to-today transition is deterministic");
    }

    private static void Storage(Action<bool, string> check)
    {
        string path = FixturePath(); App.NotesStore store = new(path, () => Now); int changes = 0;
        store.Changed += () => changes++;
        var note = Note("Fixture");
        check(store.Entries.Count == 0 && store.CanWrite && !store.LoadIssue, "Notes missing document starts empty and writable");
        check(store.SaveEntry(note) && store.Entries.Single().Id == note.Id, "Notes save creates a stable ID");
        check(store.SaveEntry(note with { Description = "Description" }) && store.Entries.Count == 1 && store.Entries[0].Id == note.Id, "Notes edit keeps ID and replaces only its entry");
        check(store.Complete(note.Id, true) && store.Entries[0].CompletedAt == Now, "Notes complete records injected local time");
        check(store.Complete(note.Id, false) && store.Entries[0].CompletedAt == null, "Notes reopen clears completion timestamp");
        check(changes == 4, "Notes successful edits publish exactly one change each");
        check(!store.SaveEntry(note with { Title = "" }) && store.Entries[0].Title == "Fixture", "Notes invalid edit preserves current data");
        var dated = Note("Dated", 1); var timed = Note("Timed", 2, 10);
        store.SaveEntry(dated); store.SaveEntry(timed);
        App.NotesStore loaded = new(path);
        check(loaded.Entries.Count == 3 && loaded.Entries.Single(e => e.Id == timed.Id).DueTime == new TimeOnly(10, 0), "Notes JSON round-trips note, date and optional local time");
        check(File.Exists(path + ".bak") && File.ReadAllText(path).Contains("\"Version\": 1", StringComparison.Ordinal), "Notes persistence retains backup and explicit version");
        using (FileStream locked = new(path, FileMode.Open, FileAccess.Read, FileShare.None))
            check(!store.Delete(note.Id) && store.Entries.Count == 3, "Notes failed atomic save leaves memory and existing entries untouched");
        check(store.Delete(note.Id) && !store.Entries.Any(e => e.Id == note.Id), "Notes delete removes only the requested identity");
        check(new App.NotesStore(path).Entries.Count == 2, "Notes deletion persists without losing unrelated entries");
        File.WriteAllText(path, "invalid json");
        loaded = new(path);
        check(loaded.LoadIssue && loaded.CanWrite && loaded.Entries.Count == 3 && Directory.GetFiles(Path.GetDirectoryName(path)!, "notes.json.corrupt-*").Length == 1,
            "Notes corrupt primary is preserved and valid backup recovered");
        check(loaded.SaveEntry(Note("Recovered edit")) && new App.NotesStore(path).Entries.Count == 4, "Notes recovered data remains safely editable");
        File.WriteAllText(path, "{\"Version\":99,\"Entries\":[]}");
        loaded = new(path);
        check(!loaded.CanWrite && loaded.LoadIssue && !loaded.SaveEntry(note) && File.ReadAllText(path).Contains("99", StringComparison.Ordinal), "Notes unknown version is never overwritten or silently downgraded");
        string empty = FixturePath(); Directory.CreateDirectory(Path.GetDirectoryName(empty)!); File.WriteAllText(empty, "");
        loaded = new(empty);
        check(loaded.LoadIssue && loaded.Entries.Count == 0 && loaded.CanWrite, "Notes empty damaged file is preserved without blocking startup");
    }

    private static void Dialogs(Action<bool, string> check)
    {
        App.NotesStore store = new(FixturePath(), () => Now);
        using (App.NoteEditorForm editor = new(store, null, "de"))
        {
            check(!editor.SaveEntry() && store.Entries.Count == 0, "Notes editor requires a title");
            Field<TextBox>(editor, "titleBox").Text = Note("Entry").Title;
            Field<TextBox>(editor, "descriptionBox").Text = Note("Multiline\ntext").Title;
            check(editor.AcceptButton == null && Field<TextBox>(editor, "descriptionBox").AcceptsReturn, "Notes multiline Enter cannot accidentally save the editor");
            check(editor.SaveEntry() && store.Entries.Count == 1, "Notes editor creates a new undated entry");
        }
        var original = store.Entries.Single();
        using (App.NoteEditorForm editor = new(store, original, "de"))
        {
            Field<TextBox>(editor, "titleBox").Text = Note("Cancelled").Title;
            editor.DialogResult = DialogResult.Cancel; editor.Close();
            check(store.Entries.Single() == original, "Notes editor Cancel never mutates stored entry");
        }
        using (App.NoteEditorForm editor = new(store, original, "de"))
        {
            Field<CheckBox>(editor, "reminder").Checked = true;
            Field<CheckBox>(editor, "timed").Checked = true;
            Field<DateTimePicker>(editor, "date").Value = Now;
            Field<DateTimePicker>(editor, "time").Value = Now.AddHours(2).AddSeconds(37);
            check(editor.SaveEntry() && store.Entries.Single().DueTime == new TimeOnly(14, 0) && store.Entries.Single().Id == original.Id,
                "Notes editor saves optional minute-precision time without hidden seconds or changed ID");
        }
        using (App.NotesManagerForm manager = new(store, "de"))
        {
            manager.Opacity = 0; manager.Show();
            ListView list = Field<ListView>(manager, "list"); list.Items[0].Selected = true;
            check(manager.ToggleSelected() && store.Entries.Single().IsCompleted, "Notes manager marks a selected entry complete");
            list.Items[0].Selected = true;
            check(manager.ToggleSelected() && !store.Entries.Single().IsCompleted, "Notes manager reopens completed entries");
            list.Items[0].Selected = true;
            check(manager.DeleteSelected() && store.Entries.Count == 0, "Notes manager deletes selected entry");
        }
        store.SaveEntry(original);
        using (App.NoteEditorForm editor = new(store, original, "de"))
            check(editor.DeleteEntry() && store.Entries.Count == 0, "Notes editor delete is available for existing entries");
        store.SaveEntry(Note("Release vorbereiten"));
        var completedFixture = Note("Wallpaper sortieren"); store.SaveEntry(completedFixture); store.Complete(completedFixture.Id, true);
        string? output = Environment.GetEnvironmentVariable("NOTES_TEST_OUTPUT");
        foreach (string language in new[] { "de", "en", "fr", "es", "ja" })
        foreach (float scale in new[] { 1f, 1.5f, 2f })
        {
            using App.NoteEditorForm editor = new(store, original, language, darkMode: true);
            editor.Scale(new SizeF(scale, scale)); editor.PerformLayout();
            Control panel = editor.Controls[0]; panel.PerformLayout();
            Control[] controls = panel.Controls.Cast<Control>().Where(c => c.Visible).ToArray();
            check(controls.All(c => c.Right <= panel.Width && c.Bottom <= panel.Height)
                && controls.Zip(controls.Skip(1)).All(p => p.First.Bottom <= p.Second.Top), $"Notes editor {language}/{scale} controls fit vertically without overlap");
            using App.NotesManagerForm manager = new(store, language, darkMode: true);
            manager.Scale(new SizeF(scale, scale)); manager.PerformLayout();
            ListView managerList = Field<ListView>(manager, "list");
            TableLayoutPanel footer = manager.Controls.OfType<TableLayoutPanel>().Single(); footer.PerformLayout();
            FlowLayoutPanel actions = footer.Controls.OfType<FlowLayoutPanel>().Single(); actions.PerformLayout();
            check(actions.Controls.Cast<Control>().All(c => c.Bottom <= actions.Height && c.Right <= actions.Width)
                && managerList.Bottom <= footer.Top, $"Notes manager {language}/{scale} action buttons and list do not overlap");
            Button close = (Button)manager.CancelButton!;
            check(close.Text == App.Localization.Get("AboutClose", language) && close.Enabled
                && close.Right <= footer.ClientSize.Width && close.Left >= actions.Right
                && close.Bottom <= footer.ClientSize.Height,
                $"Notes manager {language}/{scale} localized Close stays separate at bottom right");
            check(editor.BackColor == Color.FromArgb(255, App.WidgetDrawing.GetPalette(App.SystemWidgetStyle.Minimal).panel)
                && manager.BackColor == editor.BackColor && managerList.BackColor != SystemColors.Window,
                $"Notes dialogs {language}/{scale} inherit the widget palette");
            if (output != null && scale == 1)
            {
                Directory.CreateDirectory(output); editor.Opacity = 0; editor.Show();
                using Bitmap bitmap = new(editor.Width, editor.Height); editor.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(output, "editor-" + language + ".png"));
                manager.Opacity = 0; manager.Show(); using Bitmap managerImage = new(manager.Width, manager.Height);
                manager.DrawToBitmap(managerImage, new Rectangle(Point.Empty, managerImage.Size));
                managerImage.Save(Path.Combine(output, "manager-" + language + ".png"));
            }
        }
    }

    private static void DialogAppearance(Action<bool, string> check)
    {
        App.NotesStore store = new(FixturePath());
        foreach (App.SystemWidgetStyle style in Enum.GetValues<App.SystemWidgetStyle>())
        {
            using App.NoteEditorForm editor = new(store, null, "de", style, true);
            using App.NotesManagerForm manager = new(store, "de", style, true);
            Color expected = Color.FromArgb(255, App.WidgetDrawing.GetPalette(style).panel);
            check(editor.BackColor == expected && manager.BackColor == expected,
                $"Notes dialogs use selected {style} widget background");
            manager.Opacity = 0; manager.Show();
            ((Button)manager.CancelButton!).PerformClick();
            check(!manager.Visible && store.Entries.Count == 0, $"Notes manager {style} Close dismisses without changing entries");
        }
    }

    private static void Widget(Action<bool, string> check)
    {
        App.NotesStore store = new(FixturePath(), () => Now); var first = Note("A title that must remain constrained to at most two lines even when it contains lots of words and more words and more words");
        store.SaveEntry(first);
        int edits = 0, adds = 0;
        App.WidgetSettings settings = new() { NotesLocked = true, NotesMaximumHeight = 300 };
        using App.NotesWidgetForm widget = new(store, settings, _ => { }, entry => { if (entry == null) adds++; else edits++; }, () => Now);
        using Bitmap compact = widget.RenderBitmap();
        check(widget.OpenCount == 1 && compact.Height < 300, "Notes widget grows from compact content and counts open entries");
        for (int i = 0; i < 30; i++) store.SaveEntry(Note("Fixture " + i.ToString(CultureInfo.InvariantCulture), i % 4 - 1, i % 24));
        foreach (int dpi in new[] { 96, 144, 192 })
        {
            using Bitmap image = widget.RenderBitmap(2000, dpi);
            check(image.Width == 360 * dpi / 96 && image.Height == 300 * dpi / 96, $"Notes {dpi} DPI bitmap respects width and maximum height");
        }
        widget.Opacity = 0; widget.Show(); Invoke(widget, "OnShown", EventArgs.Empty);
        var viewport = Field<App.CalendarViewport>(widget, "viewport");
        check(widget.ScrollWheel(new Point(100, 100), -120, 3) && viewport.ScrollOffset > 0, "Notes wheel scroll works while locked");
        viewport.SetOffset(float.MaxValue); check(viewport.ScrollOffset == viewport.MaxScrollOffset, "Notes scroll offset clamps to bottom");
        viewport.SetOffset(0); widget.RefreshData();
        Point checkbox = new(24, 86); Point title = new(100, 86); Point plus = new(325, 22);
        check(widget.HitTest(checkbox) is { Checkbox: true } && widget.HitTest(title) is { Checkbox: false, Add: false }, "Notes checkbox and entry hit areas are distinct");
        Point position = widget.Location;
        Click(widget, plus); Click(widget, title);
        check(adds == 1 && edits == 1 && widget.Location == position, "Notes locked plus and edit remain interactive without dragging");
        Click(widget, checkbox);
        check(store.Entries.Single(e => e.Id == first.Id).IsCompleted && widget.Location == position, "Notes locked checkbox completes immediately without dragging");
        viewport.SetOffset(0); widget.RefreshData();
        Point track = new((int)viewport.Track.X + 2, (int)viewport.Track.Bottom - 2);
        Click(widget, track);
        check(viewport.ScrollOffset > 0, "Notes scrollbar track pages through content");
        viewport.SetOffset(0); widget.RefreshData();
        Invoke(widget, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, (int)viewport.Thumb.X + 2, (int)viewport.Thumb.Y + 2, 0));
        Invoke(widget, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, (int)viewport.Track.X + 2, (int)viewport.Track.Bottom, 0));
        Invoke(widget, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 325, (int)viewport.Track.Bottom, 0));
        check(viewport.ScrollOffset == viewport.MaxScrollOffset, "Notes scrollbar thumb dragging reaches bottom");
        Point cursor = new(100, 100); object drag = Field<object>(widget, "drag");
        drag.GetType().GetField("cursorPosition", Members)!.SetValue(drag, (Func<Point>)(() => cursor));
        Invoke(widget, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 10, 10, 0)); cursor.Offset(20, 15);
        Invoke(widget, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 30, 25, 0)); Invoke(widget, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 30, 25, 0));
        check(widget.Location == position, "Notes lock blocks free-surface movement");
        settings.NotesLocked = false; widget.Apply(settings);
        Invoke(widget, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 10, 10, 0)); cursor.Offset(20, 15);
        Invoke(widget, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 30, 25, 0)); Invoke(widget, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 30, 25, 0));
        check(widget.Location == new Point(position.X + 20, position.Y + 15), "Notes unlocked free surface uses shared drag handler");
        widget.SetActivitySuspended(true);
        check(!Field<System.Windows.Forms.Timer>(widget, "timer").Enabled && !widget.ScrollWheel(new Point(100, 100), 120, 3), "Notes fullscreen suspension stops timer and input");
        widget.SetActivitySuspended(false);
        check(Field<System.Windows.Forms.Timer>(widget, "timer").Enabled && Field<System.Windows.Forms.Timer>(widget, "timer").Interval == 30000, "Notes resume starts one lightweight 30-second timer");
        string? output = Environment.GetEnvironmentVariable("NOTES_TEST_OUTPUT");
        foreach (string language in new[] { "de", "en", "fr", "es", "ja" })
        foreach (int dpi in new[] { 96, 144, 192 })
        {
            settings.ClockLanguageCode = language; settings.NotesStyle = (App.SystemWidgetStyle)(dpi == 96 ? 0 : dpi == 144 ? 1 : 2); widget.Apply(settings);
            viewport.SetOffset(0); using Bitmap bitmap = widget.RenderBitmap(2000, dpi);
            check(bitmap.Height <= 300 * dpi / 96 && widget.Controls.Count == 0, $"Notes {language}/{dpi} localized layered viewport is bounded and has no child controls");
            if (output != null) bitmap.Save(Path.Combine(output, $"widget-{language}-{dpi}.png"));
        }
        using (Bitmap measure = new(1, 1))
        using (Graphics graphics = Graphics.FromImage(measure))
        using (Font font = new("Segoe UI", 13, FontStyle.Regular, GraphicsUnit.Pixel))
        {
            foreach (string longTitle in new[] { first.Title, new string('長', 180), string.Concat(Enumerable.Repeat("😀", 90)) })
            {
                string fitted = App.NotesWidgetForm.FitTitle(graphics, longTitle, font, 280, 38);
                check(fitted.EndsWith('…') && fitted.Length < longTitle.Length,
                    "Notes long title explicitly ellipsizes after at most two lines without splitting Unicode text elements");
            }
        }
        widget.Dispose(); check(!Field<System.Windows.Forms.Timer>(widget, "timer").Enabled, "Notes dispose stops its timer");
        if (output != null)
        {
            App.NotesStore demo = new(FixturePath());
            demo.SaveEntry(Note("Release vorbereiten und alle Änderungen vor der Veröffentlichung sorgfältig prüfen, einschließlich der Dokumentation und der Regressionstests"));
            demo.SaveEntry(Note("Wallpaper sortieren")); demo.SaveEntry(Note("Zahnarzt", 0, 14));
            demo.SaveEntry(Note("Film aufnehmen", 0, 20)); demo.SaveEntry(Note("Rechnung bezahlen", 1, 9));
            using App.NotesWidgetForm preview = new(demo, new() { NotesStyle = App.SystemWidgetStyle.Glow, NotesMaximumHeight = 500, ClockLanguageCode = "de" }, _ => { }, _ => { }, () => Now);
            using Bitmap bitmap = preview.RenderBitmap(); bitmap.Save(Path.Combine(output, "notes-preview.png"));
        }
    }

    private static void Click(Form form, Point point)
    {
        Invoke(form, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0));
        Invoke(form, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0));
    }
    private static App.WidgetSettingsEditor NewSettings(App.WidgetSettings settings) => new(settings, preview: null);

    private static void Settings(Action<bool, string> check)
    {
        string key = @"Software\WallpaperControl.RegressionTests-" + Guid.NewGuid().ToString("N");
        try
        {
            App.WidgetSettings settings = App.WidgetSettings.Load(key);
            check(settings.WallpaperInfoFontSize == 13 && !settings.NotesEnabled && settings.NotesMaximumHeight == 500, "Notes and wallpaper font defaults retain previous appearance");
            settings.NotesEnabled = settings.NotesLocked = true; settings.NotesMaximumHeight = 650; settings.NotesStyle = App.SystemWidgetStyle.Clean; settings.WallpaperInfoFontSize = 22; settings.Save(key);
            var loaded = App.WidgetSettings.Load(key);
            check(loaded.NotesEnabled && loaded.NotesLocked && loaded.NotesMaximumHeight == 650 && loaded.NotesStyle == App.SystemWidgetStyle.Clean && loaded.WallpaperInfoFontSize == 22,
                "Notes settings and wallpaper font persist with their existing identities");
            using (RegistryKey registry = Registry.CurrentUser.OpenSubKey(key, true)!) { registry.SetValue("WallpaperInfoWidgetFontSize", "bad"); registry.SetValue("NotesWidgetMaximumHeight", -10); }
            loaded = App.WidgetSettings.Load(key);
            check(loaded.WallpaperInfoFontSize == 13 && loaded.NotesMaximumHeight == 300 && loaded.NotesEnabled, "Notes invalid height and font cannot prevent independent settings loading");
            using (RegistryKey registry = Registry.CurrentUser.OpenSubKey(key, true)!) registry.SetValue("WallpaperInfoWidgetFontSize", -500);
            check(App.WidgetSettings.Load(key).WallpaperInfoFontSize == 10, "Notes persisted font below range clamps to minimum");
            using (RegistryKey registry = Registry.CurrentUser.OpenSubKey(key, true)!) registry.SetValue("WallpaperInfoWidgetFontSize", 500);
            check(App.WidgetSettings.Load(key).WallpaperInfoFontSize == 24, "Notes persisted font above range clamps to maximum");
            using (App.WidgetSettingsEditor form = NewSettings(settings))
            {
                Field<NumericUpDown>(form, "wallpaperInfoFontSize").Value = 18; Field<NumericUpDown>(form, "notesMaximumHeight").Value = 700;
                var draft = (App.WidgetSettings)Invoke(form, "ReadWidgetSettings", false)!;
                check(draft.WallpaperInfoFontSize == 18 && draft.NotesMaximumHeight == 700 && settings.WallpaperInfoFontSize == 22, "Notes height and wallpaper font preview use independent drafts");
                var acceptedSettings = form.ReadWidgetSettings(true); check(acceptedSettings.WallpaperInfoFontSize == 18 && acceptedSettings.NotesMaximumHeight == 700, "Notes height and wallpaper font Save accept draft values");
            }
            using (App.WidgetSettingsEditor form = NewSettings(settings))
            {
                form.ResetDefaults(); var draft = (App.WidgetSettings)Invoke(form, "ReadWidgetSettings", false)!;
                check(!draft.NotesEnabled && draft.NotesMaximumHeight == 500 && draft.WallpaperInfoFontSize == 13, "Notes Reset restores height, disabled state and default font");
            }
            foreach (string language in new[] { "de", "en", "fr", "es", "ja" })
            {
                using App.WidgetSettingsEditor form = NewSettings(settings);
                form.ApplyPresentation(false, language);
                int managed = 0;
                form.ConfigureNotesManager((owner, selectedLanguage) => { if (owner == form && selectedLanguage == language) managed++; });
                Invoke(Field<Button>(form, "notesManageButton"), "OnClick", EventArgs.Empty);
                check(managed == 1, $"Notes {language} settings manager button forwards owner and preview language");
                check(form.WidgetKeys.Count == 8, $"Notes {language} widget editor exposes all eight sections");
                Button[] buttons = Field<FlowLayoutPanel>(form, "navigation").Controls.OfType<Button>().OrderBy(b => b.TabIndex).ToArray();
                var comparer = StringComparer.Create(CultureInfo.GetCultureInfo(language), true);
                string[] labels = form.WidgetKeys.Select(key => App.Localization.Get(key, language)).ToArray();
                check(labels.SequenceEqual(labels.OrderBy(n => n, comparer)) && buttons.All(b => b.TabStop), $"Notes {language} widget sections sort by localized labels and support keyboard navigation");
                check(Field<TabControl>(form, "pages").TabPages.Cast<TabPage>().Select(p => (string)p.Tag!).Order().SequenceEqual(form.WidgetKeys.Order()), $"Notes {language} sorting preserves widget page identities");
                form.SelectWidget(form.WidgetKeys[0]);
                check(form.SelectedKey == form.WidgetKeys[0], $"Notes {language} opening a widget selects exactly its page");
                form.SelectWidget(form.WidgetKeys[0]);
                check(form.SelectedKey == form.WidgetKeys[0], $"Notes {language} repeated selection retains its editor");
                form.ApplyPresentation(false, language);
                check(form.SelectedKey == form.WidgetKeys[0], $"Notes {language} sorting does not change the selected widget");
            }
            foreach (string language in new[] { "de", "en", "fr", "es", "ja" })
            foreach (float scale in new[] { 1f, 1.5f, 2f })
            {
                using App.WidgetSettingsEditor form = NewSettings(settings); form.ApplyPresentation(false, language);
                form.SelectWidget("NotesTitle");
                var tabs = Field<TabControl>(form, "pages");
                form.ConfigureNotesManager((_, _) => { });
                Control panel = Field<NumericUpDown>(form, "notesMaximumHeight").Parent!;
                panel.Scale(new SizeF(scale, scale)); panel.PerformLayout();
                Control[] controls = panel.Controls.Cast<Control>().ToArray();
                check(controls.All(c => c.Right <= panel.Width && c.Bottom <= panel.Height)
                    && controls.Zip(controls.Skip(1)).All(pair => pair.First.Bottom <= pair.Second.Top), $"Notes settings {language}/{scale} localized controls fit without overlap");
                string? output = Environment.GetEnvironmentVariable("NOTES_TEST_OUTPUT");
                if (output != null && scale == 1)
                {
                     form.Show(); form.SelectWidget("NotesTitle");
                    using Bitmap bitmap = new(form.Width, form.Height); form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(Path.Combine(output, "settings-" + language + ".png"));
                }
            }
            foreach (string language in new[] { "de", "en", "fr", "es", "ja" })
            foreach (int fontSize in new[] { 10, 13, 24 })
            foreach (int dpi in new[] { 96, 144, 192 })
            foreach (bool advanced in new[] { false, true })
            {
                using App.WallpaperInfoWidgetForm form = new(new() { ClockLanguageCode = language, WallpaperInfoFontSize = fontSize, WallpaperInfoShowAdvanced = advanced }, _ => { });
                form.SetData(new(new string('W', 1000), 7, 305, new(2162, 7.28, 19)));
                using Bitmap bitmap = form.RenderBitmap(dpi, 4000);
                check(bitmap.Height >= (10 + (advanced ? 2 : 1) * Math.Max(24, fontSize + 6)) * dpi / 96,
                    $"Notes wallpaper font {language}/{fontSize}/{dpi}/{advanced} keeps both rows within dynamic height");
                string? output = Environment.GetEnvironmentVariable("NOTES_TEST_OUTPUT");
                if (output != null && fontSize == 24 && dpi == 192 && advanced)
                    bitmap.Save(Path.Combine(output, "info-large-" + language + ".png"));
            }
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(key, false); }
    }

    private static void Manager(Action<bool, string> check)
    {
        string key = @"Software\WallpaperControl.RegressionTests-" + Guid.NewGuid().ToString("N");
        try
        {
            App.NotesStore store = new(FixturePath());
            for (int i = 0; i < 15; i++) store.SaveEntry(Note("Fixture " + i.ToString(CultureInfo.InvariantCulture)));
            using App.WidgetManager manager = new(() => { }, registryPath: key, notesStore: store);
            var original = manager.Settings; var draft = original.Clone(); draft.NotesEnabled = true;
            manager.Preview(draft); var widget = Field<App.NotesWidgetForm>(manager, "notesWidget"); widget.Opacity = 0; Invoke(widget, "OnShown", EventArgs.Empty);
            var active = Field<HashSet<Form>>(manager, "desktopWidgets");
            check(active.Contains(widget), "Notes manager registers active widget centrally");
            Invoke(manager, "RestoreDesktopWidgetBand"); check(active.Contains(widget), "Notes shared Show Desktop repair retains registration");
            manager.SetActivitySuspended(true); check(!Field<System.Windows.Forms.Timer>(widget, "timer").Enabled, "Notes manager forwards fullscreen suspension");
            manager.SetActivitySuspended(false); check(Field<System.Windows.Forms.Timer>(widget, "timer").Enabled, "Notes manager resumes time-dependent display");
            var activeSettings = manager.Settings;
            int originalHeight = widget.Height; draft.NotesMaximumHeight = 300; manager.Preview(draft);
            check(widget.Height < originalHeight, "Notes maximum-height preview shrinks an existing scrolled window");
            manager.CancelPreview(activeSettings);
            check(widget.Height == originalHeight && manager.Settings.NotesMaximumHeight == 500, "Notes Cancel restores maximum-height setting and geometry");
            manager.CommitPreview(activeSettings);
            check(App.WidgetSettings.Load(key).NotesEnabled && App.WidgetSettings.Load(key).NotesMaximumHeight == 500, "Notes accepted widget settings persist through manager Commit");
            manager.CancelPreview(original); check(widget.IsDisposed && active.Count == 0, "Notes settings Cancel disposes preview-only widget and deregisters");
            draft.WallpaperInfoEnabled = true; manager.Preview(draft); var info = Field<App.WallpaperInfoWidgetForm>(manager, "wallpaperInfoWidget"); info.Opacity = 0;
            int height = info.Height; var originalPreview = manager.Settings; draft.WallpaperInfoFontSize = 24; manager.Preview(draft);
            check(info.Height > height, "Notes wallpaper font preview immediately grows existing window");
            manager.CancelPreview(originalPreview); check(info.Height == height && manager.Settings.WallpaperInfoFontSize == 13, "Notes Cancel restores wallpaper font and geometry");
            draft.NotesEnabled = false; manager.Preview(draft); check(Field<App.NotesWidgetForm?>(manager, "notesWidget") == null, "Notes deactivation removes its widget");
            manager.Dispose(); check(active.Count == 0, "Notes application exit releases central registrations");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(key, false); }
    }
}
