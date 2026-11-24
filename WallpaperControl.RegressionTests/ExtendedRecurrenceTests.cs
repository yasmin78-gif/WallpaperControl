extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using System.Reflection;

internal static class ExtendedRecurrenceTests
{
    // A reproducible, unsaved acceptance draft. Never opens the production notes file.
    internal static void Preview()
    {
        Console.WriteLine("Preparing recurrence acceptance preview.");
        Thread thread = new(() =>
        {
            Console.WriteLine("Initializing preview UI.");
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            App.Localization.RefreshAvailableLanguages();
            Console.WriteLine("Creating unsaved editor.");
            var store = new App.NotesStore(Path.Combine(Path.GetTempPath(), "WallpaperControl-RecurrencePreview", Guid.NewGuid().ToString("N"), "notes.json"));
            using var editor = new App.NoteEditorForm(store, null, "de", App.SystemWidgetStyle.Glow, darkMode: true);
            editor.ShowInTaskbar = true;
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            ((ComboBox)typeof(App.NoteEditorForm).GetField("repeat", flags)!.GetValue(editor)!).SelectedIndex = 4;
            ((TextBox)typeof(App.NoteEditorForm).GetField("interval", flags)!.GetValue(editor)!).Text = "2";
            ((ComboBox)typeof(App.NoteEditorForm).GetField("unit", flags)!.GetValue(editor)!).SelectedIndex = 1;
            Console.WriteLine("Showing recurrence acceptance preview.");
            Application.Run(editor);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
    }

    internal static void Run(Action<bool, string> check)
    {
        App.Localization.RefreshAvailableLanguages();
        DateTime now = new(2027, 1, 31, 9, 0, 0);
        App.NoteEntry monthly = new() { Title = "Monthly", HasReminder = true, DueDate = new(2027, 1, 31), DueTime = new(9, 0), Repeat = App.NoteRepeat.Monthly, CreatedAt = now, PopupReminder = true, PopupLeadMinutes = 10 };
        int[] days = [31, 28, 31, 30, 31];
        for (int i = 0; i < days.Length; i++) check(App.NoteRecurrence.At(monthly, i) == new DateOnly(2027, i + 1, days[i]), $"Monthly anchor retains day 31 at index {i}");
        var leap = monthly with { DueDate = new(2028, 2, 29), Repeat = App.NoteRepeat.Yearly };
        for (int i = 0; i <= 4; i++) check(App.NoteRecurrence.At(leap, i) == new DateOnly(2028 + i, 2, i % 4 == 0 ? 29 : 28), $"Leap year anchor at index {i}");
        foreach (var unit in Enum.GetValues<App.NoteRepeatUnit>())
        {
            var entry = monthly with { Repeat = App.NoteRepeat.Custom, RepeatInterval = 2, RepeatUnit = unit };
            var expected = unit switch { App.NoteRepeatUnit.Days => monthly.DueDate!.Value.AddDays(2), App.NoteRepeatUnit.Weeks => monthly.DueDate!.Value.AddDays(14), App.NoteRepeatUnit.Months => monthly.DueDate!.Value.AddMonths(2), _ => monthly.DueDate!.Value.AddYears(2) };
            check(entry.IsValid && App.NoteRecurrence.Next(entry, monthly.DueDate!.Value) == expected, $"Custom {unit} uses original rhythm");
            check(!(entry with { RepeatInterval = 0 }).IsValid && !(entry with { RepeatInterval = -1 }).IsValid && !(entry with { RepeatInterval = int.MaxValue }).IsValid, $"Invalid {unit} intervals rejected without overflow");
        }
        check(App.NoteRecurrence.At(monthly with { DueDate = new(2028, 1, 31) }, 1) == new DateOnly(2028, 2, 29), "Monthly leap February");
        check(App.NoteRecurrence.Next(monthly, new(2027, 12, 31)) == new DateOnly(2028, 1, 31), "Year rollover");
        var late = monthly with { IsCompleted = true, CompletedAt = new DateTime(2027, 4, 12, 15, 0, 0) };
        check(late.EffectiveDueDate(now) == new DateOnly(2027, 4, 30), "Late completion skips missed months");
        check(App.NoteReminderSchedule.Occurrence(monthly, now.AddMinutes(-11)) == null && App.NoteReminderSchedule.Occurrence(monthly, now.AddMinutes(-10)) == now, "Monthly popup lead window");
        check(App.NoteReminderSchedule.Occurrence(late, new(2027, 4, 30, 8, 50, 0)) == new DateTime(2027, 4, 30, 9, 0, 0), "Next popup retains wall clock time");
        check(!App.NoteReminderSchedule.IsPending(monthly with { PopupOccurrence = now }, now, now), "Popup duplicate suppressed");
        var zone = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
        foreach (var day in new[] { new DateOnly(2027, 3, 27), new DateOnly(2027, 10, 30) })
        {
            var e = monthly with { DueDate = day, Repeat = App.NoteRepeat.Custom, RepeatUnit = App.NoteRepeatUnit.Days };
            var first = App.NoteRecurrence.At(e, 0)!.Value.ToDateTime(new TimeOnly(9, 0));
            var second = App.NoteRecurrence.At(e, 1)!.Value.ToDateTime(new TimeOnly(9, 0));
            check(first.Hour == second.Hour && zone.GetUtcOffset(first) != zone.GetUtcOffset(second), "DST transition retains local 09:00");
        }
        var beforeToday = monthly with { IsCompleted = true, CompletedAt = new DateTime(2027, 2, 28, 8, 0, 0) };
        check(beforeToday.EffectiveDueDate(now) == new DateOnly(2027, 2, 28), "Late completion retains a still-future occurrence today");
        var early = monthly with { IsCompleted = true, CompletedAt = new DateTime(2027, 2, 27, 23, 55, 0), RepeatCompletedThrough = new DateOnly(2027, 2, 28) };
        check(early.EffectiveDueDate(now) == new DateOnly(2027, 3, 31) && App.NoteReminderSchedule.Completed(early, new(2027, 2, 28, 0, 0, 0)), "Early completion consumes the exact future occurrence");
        string path = Path.Combine(AppContext.BaseDirectory, "test-data", "extended-" + Guid.NewGuid(), "notes.json");
        var store = new App.NotesStore(path, () => now);
        check(store.SaveEntry(monthly) && store.SaveEntry(monthly with { Title = "Edited" }), "Create and edit extended series");
        var loaded = new App.NotesStore(path, () => now).Entries.Single();
        check(loaded.Repeat == monthly.Repeat && loaded.DueDate == monthly.DueDate && loaded.Title == "Edited", "Extended series round trips");
        check(File.Exists(path + ".bak") && File.ReadAllText(path).Contains("\"Version\": 4"), "Version 4 and atomic backup");
        check(store.Complete(monthly.Id, true) && store.Entries.Single().EffectiveDueDate(now) == new DateOnly(2027, 2, 28), "Completion advances monthly series");
        now = new DateTime(2027, 4, 12, 15, 0, 0);
        check(store.Complete(monthly.Id, true) && new App.NotesStore(path).Entries.Single().EffectiveDueDate(now) == new DateOnly(2027, 4, 30), "Repeated late completion and cutoff survive restart");
        var completedSeries = store.Entries.Single();
        check(store.SaveEntry(completedSeries with { Description = "Edited details" }) && store.Entries.Single().EffectiveDueDate(now) == new DateOnly(2027, 4, 30), "Editing details preserves current occurrence");
        Exception? failure = null;
        Thread thread = new(() => { try { Ui(check, path); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static void Ui(Action<bool, string> check, string path)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        T Field<T>(object o, string name) => (T)o.GetType().GetField(name, flags)!.GetValue(o)!;
        var store = new App.NotesStore(path);
        using var editor = new App.NoteEditorForm(store, null, "de", darkMode: true);
        editor.Show(); Application.DoEvents();
        var repeat = Field<ComboBox>(editor, "repeat"); var interval = Field<TextBox>(editor, "interval"); var unit = Field<ComboBox>(editor, "unit");
        Field<TextBox>(editor, "titleBox").Text = "Custom test";
        repeat.SelectedIndex = 4; Application.DoEvents();
        check(Field<FlowLayoutPanel>(editor, "customRow").Visible && interval.Text == "1" && unit.SelectedIndex == 0, "Custom controls show with defaults");
        int expanded = editor.Height;
        foreach (string invalid in new[] { "", "0", "-1", "1.5", "abc", "2147483648", "9999999999" }) { interval.Text = invalid; check(!editor.SaveEntry(), $"Editor rejects '{invalid}'"); }
        interval.Text = "2"; unit.SelectedIndex = 1;
        check(Field<Label>(editor, "customSummary").Text == "Alle 2 Wochen", "Dynamic custom summary");
        repeat.SelectedIndex = 2; Application.DoEvents();
        check(!Field<FlowLayoutPanel>(editor, "customRow").Visible && editor.Height < expanded, "Hidden custom row releases height");
        repeat.SelectedIndex = 4;
        check(editor.SaveEntry(), "Valid custom draft saves");
        var saved = new App.NotesStore(path).Entries.Single(e => e.Title == "Custom test");
        check(saved.RepeatInterval == 2 && saved.RepeatUnit == App.NoteRepeatUnit.Weeks, "Custom settings round trip");
        foreach (string language in new[] { "de", "en", "fr", "es", "ja" })
        foreach (bool dark in new[] { false, true })
        foreach (int dpi in new[] { 96, 144, 192 })
        {
            using Font font = new("Segoe UI", 9f * dpi / 96f);
            using var preview = new App.NoteEditorForm(store, saved, language, darkMode: dark);
            preview.Scale(new SizeF(dpi / 96f, dpi / 96f));
            preview.Font = font; preview.Opacity = 0; preview.Show(); preview.PerformLayout();
            var panel = preview.Controls[0]; panel.PerformLayout();
            var controls = panel.Controls.Cast<Control>().Where(c => c.Visible).ToArray();
            check(controls.All(c => c.Bottom <= panel.Height && c.Right <= panel.Width)
                && controls.Zip(controls.Skip(1)).All(pair => pair.First.Bottom <= pair.Second.Top), $"Custom layout {language}/{dpi}/{dark} fits without overlap");
            var row = Field<FlowLayoutPanel>(preview, "customRow");
            check(row.Controls.Cast<Control>().All(c => c.Right <= row.Width && c.Bottom <= row.Height), $"Custom interval controls {language}/{dpi}/{dark} fit");
            check(Field<CheckBox>(preview, "timed").ForeColor == preview.ForeColor && Field<CheckBox>(preview, "popup").ForeColor == preview.ForeColor,
                $"Checkbox palette {language}/{dpi}/{dark} matches dialog");
            if (dark && dpi == 96)
            {
                var disabled = Field<CheckBox>(preview, "popup");
                using Bitmap image = new(disabled.Width, disabled.Height);
                disabled.DrawToBitmap(image, disabled.ClientRectangle);
                int readablePixels = 0;
                for (int y = 0; y < image.Height; y++)
                for (int x = 20; x < image.Width; x++)
                {
                    Color pixel = image.GetPixel(x, y);
                    if (pixel.R > 150 && pixel.G > 150 && pixel.B > 150) readablePixels++;
                }
                check(!disabled.Enabled && readablePixels > 10, $"Disabled popup label {language} renders readable light text");
            }
        }
        using var editing = new App.NoteEditorForm(store, saved, "de", darkMode: false);
        check(Field<ComboBox>(editing, "repeat").SelectedIndex == 4 && Field<TextBox>(editing, "interval").Text == "2", "Editor restores custom values");
        DateTime widgetNow = new(2026, 11, 24, 12, 0, 0);
        var widgetStore = new App.NotesStore(Path.Combine(Path.GetDirectoryName(path)!, "visibility.json"), () => widgetNow);
        widgetStore.SaveEntry(new App.NoteEntry { Title = "Undated" });
        widgetStore.SaveEntry(new App.NoteEntry { Title = "Future one-off", HasReminder = true, DueDate = new(2027, 3, 19) });
        foreach (var kind in new[] { App.NoteRepeat.Daily, App.NoteRepeat.Monthly, App.NoteRepeat.Yearly, App.NoteRepeat.Custom })
            widgetStore.SaveEntry(new App.NoteEntry { Title = "Serie", HasReminder = true, DueDate = new(2026, 11, 25), DueTime = new(23, 0), Repeat = kind, RepeatsDaily = kind == App.NoteRepeat.Daily });
        using var widget = new App.NotesWidgetForm(widgetStore, new() { ClockLanguageCode = "de" }, _ => { }, _ => { }, () => widgetNow);
        using (var image = widget.RenderBitmap())
        {
            check(widget.OpenCount == 2 && Field<System.Collections.IList>(widget, "rows").Count == 2,
                "Widget hides future recurring rows and excludes them from count; one-offs remain visible");
        }
        widgetNow = widgetNow.Date.AddDays(1);
        using (var image = widget.RenderBitmap())
            check(widget.OpenCount == 6 && Field<System.Collections.IList>(widget, "rows").Count == 6,
                "All recurrence kinds appear on their due day before the configured time");
        var monthly = widgetStore.Entries.Single(e => e.Repeat == App.NoteRepeat.Monthly);
        string title = (string)typeof(App.NotesWidgetForm).GetMethod("DisplayTitle", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { monthly, widgetNow, System.Globalization.CultureInfo.GetCultureInfo("de") })!;
        check(title == "23:00  Serie", "Recurring widget title retains time without recurrence label");
        widgetStore.Complete(monthly.Id, true);
        using (var image = widget.RenderBitmap())
            check(widget.OpenCount == 5 && Field<System.Collections.IList>(widget, "rows").Count == 5,
                "Completing a series hides its next future occurrence immediately");
        widgetNow = widgetNow.AddDays(1);
        using (var image = widget.RenderBitmap())
            check(widget.OpenCount == 5, "Overdue recurring entries remain visible until completion");
    }
}
