extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using System.Reflection;

internal static class DailyNotesTests
{
    private static readonly BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, Members)!.GetValue(value)!;
    private static string PathForTest() => Path.Combine(AppContext.BaseDirectory, "test-data", "daily-" + Guid.NewGuid().ToString("N"), "notes.json");
    internal static void Run(Action<bool, string> check)
    {
        App.Localization.RefreshAvailableLanguages();
        DateTime now = new(2026, 9, 23, 8, 15, 0);
        string path = PathForTest();
        App.NotesStore store = new(path, () => now);
        App.NoteEntry daily = new() { Title = "Daily task", RepeatsDaily = true, HasReminder = true, DueDate = DateOnly.FromDateTime(now), DueTime = new TimeOnly(8, 0), CreatedAt = now };
        check(daily.IsValid && !daily.IsCompletedOn(now) && daily.IsOverdue(now), "Daily task has an optional local due time");
        check(!(daily with { HasReminder = false, DueDate = null, DueTime = null }).IsValid, "Daily task requires a start date");
        check(App.NoteGrouping.Build(new[] { daily }, now).Single().Key == "NotesToday", "Daily overdue time stays in today's group");
        check(store.SaveEntry(daily) && store.Complete(daily.Id, true), "Daily completion persists successfully");
        check(File.ReadAllText(path).Contains("\"Version\": 2", StringComparison.Ordinal), "Recurring data uses version 2 to protect it from older applications");
        store = new(path, () => now);
        check(store.Entries.Single().IsCompletedOn(now) && store.Entries.Single().CompletedAt == now, "Restart retains today's completion and exact time");
        now = now.AddMinutes(5); store.Complete(daily.Id, true);
        check(store.Entries.Single().CompletedAt == now.AddMinutes(-5), "Repeated completion does not overwrite recorded completion time");
        now = now.Date.AddDays(1);
        check(!store.Entries.Single().IsCompletedOn(now) && App.NoteGrouping.Build(store.Entries, now).Single().Key == "NotesToday", "At local midnight daily task reopens without a write");
        check(!store.Entries.Single().IsOverdue(now), "New daily occurrence is not overdue before its time");
        now = now.AddDays(12).AddHours(9);
        store = new(path, () => now);
        check(store.Entries.Count == 1 && !store.Entries[0].IsCompletedOn(now), "Restart after missed days produces one current occurrence");
        check(store.Complete(daily.Id, true) && store.Entries[0].CompletedAt == now, "New occurrence records new completion timestamp");
        check(store.Complete(daily.Id, false) && !store.Entries[0].IsCompletedOn(now), "Today's completion can be undone");
        check(store.SaveEntry(store.Entries[0] with { Title = "Edited daily task" }) && store.Entries.Count == 1, "Editing a daily task updates the series without duplication");
        var future = daily with { DueDate = DateOnly.FromDateTime(now.AddDays(3)) };
        check(!future.IsDueToday(now) && !future.IsOverdue(now) && future.EffectiveDueDate(now) == future.DueDate, "Future daily start date is respected");
        var once = daily with { RepeatsDaily = false, IsCompleted = true, CompletedAt = now.AddDays(-30) };
        check(once.IsCompletedOn(now) && !once.IsOverdue(now), "One-off completed notes stay completed across days");
        check(store.Delete(daily.Id) && new App.NotesStore(path).Entries.Count == 0, "Deleting daily task removes the series persistently");
        string oldPath = PathForTest(); Directory.CreateDirectory(Path.GetDirectoryName(oldPath)!);
        File.WriteAllText(oldPath, "{\"Version\":1,\"Entries\":[{\"Id\":\"" + Guid.NewGuid() + "\",\"Title\":\"Legacy note\",\"CreatedAt\":\"2026-09-20T12:00:00\"}]}");
        var old = new App.NotesStore(oldPath);
        check(old.CanWrite && !old.LoadIssue && !old.Entries.Single().RepeatsDaily, "Actual version 1 JSON without repeat property remains readable");
        using Task ui = new(() => Ui(check));
        Thread thread = new(() => ui.RunSynchronously(TaskScheduler.Default));
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); ui.GetAwaiter().GetResult();
    }

    private static void Ui(Action<bool, string> check)
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        DateTime now = new(2026, 9, 23, 8, 15, 0);
        App.NotesStore store = new(PathForTest(), () => now);
        var daily = new App.NoteEntry { Title = "Tabletten nehmen", RepeatsDaily = true, HasReminder = true, DueDate = DateOnly.FromDateTime(now), CreatedAt = now };
        store.SaveEntry(daily);
        store.SaveEntry(new App.NoteEntry { Title = "Gerät im Schlafzimmer wegräumen", HasReminder = true, DueDate = daily.DueDate, CreatedAt = now.AddSeconds(1) });
        using App.NotesWidgetForm widget = new(store, new() { NotesLocked = true, ClockLanguageCode = "de" }, _ => { }, _ => { }, () => now);
        using (var image = widget.RenderBitmap())
            check(image.Height <= 165 && widget.OpenCount == 2, "Two short tasks occupy compact single-line rows");
        var viewport = Field<App.CalendarViewport>(widget, "viewport");
        var firstHit = widget.HitTest(new Point(24, 86));
        var secondHit = widget.HitTest(new Point(24, 114));
        check(firstHit?.Id == daily.Id && secondHit?.Id != daily.Id && secondHit?.Checkbox == true, "Compact adjacent rows retain independent hit targets");
        store.Complete(daily.Id, true);
        using (var image = widget.RenderBitmap())
        {
            check(widget.OpenCount == 1 && !Field<bool>(widget, "completedExpanded"), "Completed-today group is initially collapsed and excluded from count");
            var header = Field<RectangleF>(widget, "completedBounds");
            var point = new Point((int)header.X + 20, (int)header.Y + 10);
            check(widget.HitTest(point)?.CompletedGroup == true, "Completed-today header has its own hit target");
            Click(widget, point);
        }
        using (var image = widget.RenderBitmap())
            check(Field<bool>(widget, "completedExpanded") && image.Height > 165, "Completed group expands without toggling completion");
        var completedRow = Field<List<(App.NoteEntry Entry, RectangleF Bounds)>>(widget, "rows").Single(r => r.Entry.Id == daily.Id);
        Click(widget, new Point(24, (int)completedRow.Bounds.Y + 10));
        check(!store.Entries.First(e => e.Id == daily.Id).IsCompletedOn(now), "Expanded completed row checkbox reopens today's task while locked");
        store.Complete(daily.Id, true);
        using (App.NoteEditorForm editor = new(store, store.Entries.First(e => e.Id == daily.Id), "de"))
        {
            now = now.Date.AddDays(1);
            Field<TextBox>(editor, "titleBox").Text = "Changed title";
            check(editor.SaveEntry() && !store.Entries.First(e => e.Id == daily.Id).IsCompletedOn(now), "Editing over midnight cannot accidentally complete the next day");
        }
        using (var image = widget.RenderBitmap())
            check(widget.OpenCount == 2 && Field<RectangleF>(widget, "completedBounds").IsEmpty, "Midnight refresh reopens daily task and removes yesterday's completed group");
        using (App.NotesManagerForm manager = new(store, "de"))
        {
            manager.Opacity = 0; manager.Show();
            var list = Field<ListView>(manager, "list");
            list.Items.Cast<ListViewItem>().Single(i => ((App.NoteEntry)i.Tag!).Id == daily.Id).Selected = true;
            check(manager.ToggleSelected() && store.Entries.First(e => e.Id == daily.Id).IsCompletedOn(now), "Manager toggles today's state rather than stored previous-day flag");
        }
        foreach (string lang in new[] { "de", "en", "fr", "es", "ja" })
        foreach (int dpi in new[] { 96, 144, 192 })
        {
            check(App.Localization.IsLanguageAvailable(lang) && (lang == "de" || App.Localization.Get("NotesDailyHint", lang) != App.Localization.Get("NotesDailyHint", "de")), $"Daily notes {lang} uses its own translation rather than fallback");
            foreach (string key in new[] { "NotesRepeat", "NotesRepeatNone", "NotesRepeatDaily", "NotesDailyHint", "NotesCompletedToday", "NotesDoneGroup", "NotesDoneAt" })
                check(App.Localization.Get(key, lang) != key, $"Daily notes {lang} resource {key} exists");
            Font sourceFont = SystemFonts.MessageBoxFont ?? Control.DefaultFont;
            using Font scaledFont = new(sourceFont.FontFamily, sourceFont.Size * dpi / 96f, sourceFont.Style, sourceFont.Unit);
            using App.NoteEditorForm editor = new(store, store.Entries.First(e => e.Id == daily.Id), lang);
            editor.Scale(new SizeF(dpi / 96f, dpi / 96f)); editor.Font = scaledFont;
            editor.Opacity = 0; editor.Show(); editor.PerformLayout();
            var repeat = Field<ComboBox>(editor, "repeat");
            check(repeat.SelectedIndex == 1 && Field<Label>(editor, "repeatHint").Visible, $"Daily editor {lang}/{dpi} restores recurrence and series explanation");
            var panel = editor.Controls[0]; panel.PerformLayout();
            var controls = panel.Controls.Cast<Control>().Where(c => c.Visible).ToArray();
            if (controls.Any(c => c.Bottom > panel.Height || c.Right > panel.Width) || controls.Zip(controls.Skip(1)).Any(pair => pair.First.Bottom > pair.Second.Top))
                Console.Error.WriteLine($"Layout {lang}/{dpi} panel={panel.Size}: " + string.Join("; ", controls.Select(c => $"{c.GetType().Name}={c.Bounds}")));
            check(controls.All(c => c.Bottom <= panel.Height && c.Right <= panel.Width)
                && controls.Zip(controls.Skip(1)).All(pair => pair.First.Bottom <= pair.Second.Top), $"Daily editor {lang}/{dpi} fields and actions do not overlap");
            widget.Apply(new() { ClockLanguageCode = lang, NotesLocked = true, NotesStyle = App.SystemWidgetStyle.Glow });
            using var bitmap = widget.RenderBitmap(2000, dpi);
            check(bitmap.Width == 360 * dpi / 96 && bitmap.Height <= 500 * dpi / 96, $"Daily widget {lang}/{dpi} stays within DPI bounds");
            string? output = Environment.GetEnvironmentVariable("DAILY_NOTES_TEST_OUTPUT");
            if (output != null)
            {
                Directory.CreateDirectory(output); bitmap.Save(Path.Combine(output, $"daily-{lang}-{dpi}.png"));
                using Bitmap dialog = new(editor.Width, editor.Height); editor.DrawToBitmap(dialog, new Rectangle(Point.Empty, dialog.Size));
                dialog.Save(Path.Combine(output, $"daily-editor-{lang}-{dpi}.png"));
            }
        }
        using (App.NoteEditorForm editor = new(store, null, "de"))
        {
            Field<TextBox>(editor, "titleBox").Text = "New daily task";
            Field<ComboBox>(editor, "repeat").SelectedIndex = 1;
            check(Field<CheckBox>(editor, "reminder").Checked && editor.SaveEntry(), "Editor creates daily task with optional time omitted");
            check(store.Entries.Last().RepeatsDaily && store.Entries.Last().DueTime == null, "Daily repeat and date-only behavior are persisted");
        }
        string? previewOutput = Environment.GetEnvironmentVariable("DAILY_NOTES_TEST_OUTPUT");
        if (previewOutput != null)
        {
            App.NotesStore previewStore = new(PathForTest(), () => now);
            var item = daily with { CreatedAt = now, DueDate = DateOnly.FromDateTime(now), DueTime = new TimeOnly(8, 0) };
            previewStore.SaveEntry(item);
            previewStore.SaveEntry(new App.NoteEntry { Title = "Gerät im Schlafzimmer wegräumen", HasReminder = true, DueDate = item.DueDate, CreatedAt = now.AddSeconds(1) });
            using App.NotesWidgetForm preview = new(previewStore, new() { NotesStyle = App.SystemWidgetStyle.Glow, ClockLanguageCode = "de" }, _ => { }, _ => { }, () => now);
            using (Bitmap image = preview.RenderBitmap()) image.Save(Path.Combine(previewOutput, "compact-preview.png"));
            now = now.Date.AddHours(8).AddMinutes(15); previewStore.Complete(item.Id, true);
            typeof(App.NotesWidgetForm).GetField("completedExpanded", Members)!.SetValue(preview, true);
            using (Bitmap image = preview.RenderBitmap()) image.Save(Path.Combine(previewOutput, "completed-preview.png"));
        }
    }

    private static void Click(Form form, Point p)
    {
        foreach (string name in new[] { "OnMouseDown", "OnMouseUp" })
            form.GetType().GetMethod(name, Members)!.Invoke(form, new object[] { new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0) });
    }
}
