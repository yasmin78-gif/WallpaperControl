extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class NoteReminderTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Members)!.GetValue(target)!;
    private static string TestPath() => Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"), "notes.json");
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

    internal static void Run(Action<bool, string> check)
    {
        DateTime due = new(2026, 9, 27, 12, 0, 0);
        foreach (var sample in new[] { (-301, 2), (-300, 1), (-299, 1), (-1, 1), (0, 1), (1, 1) })
        {
            var options = App.NoteReminderPopup.LaterOptions(due, due.AddSeconds(sample.Item1));
            check(options.Length == sample.Item2 && options.Contains(App.NoteReminderAction.Snooze) == (sample.Item1 < -300 || sample.Item1 >= 0)
                && options.Contains(App.NoteReminderAction.AtDue) == (sample.Item1 < 0), $"Later options at {sample.Item1} seconds");
        }
        App.NoteEntry note = new() { Title = "Reminder", HasReminder = true, DueDate = DateOnly.FromDateTime(due),
            DueTime = TimeOnly.FromDateTime(due), PopupReminder = true, CreatedAt = due.AddDays(-1) };
        foreach (int lead in new[] { 0, 5, 10 })
        {
            var entry = note with { PopupLeadMinutes = lead };
            check(entry.IsValid && App.NoteReminderSchedule.Occurrence(entry, due.AddMinutes(-lead).AddSeconds(-1)) == null,
                $"Popup {lead}: no early delivery");
            check(App.NoteReminderSchedule.Occurrence(entry, due.AddMinutes(-lead)) == due,
                $"Popup {lead}: exact lead-time boundary");
        }
        check(!(note with { DueTime = null }).IsValid && !(note with { PopupLeadMinutes = 3 }).IsValid,
            "Popup rejects untimed reminders and unsupported lead times");
        check(App.NoteReminderSchedule.Occurrence(note with { PopupReminder = false }, due) == null,
            "Existing notes do not opt into popups automatically");
        check(App.NoteReminderSchedule.Occurrence(note with { IsCompleted = true, CompletedAt = due }, due) == null,
            "Completed notes never deliver");
        check(App.NoteReminderSchedule.Occurrence(note, due.AddDays(3)) == due,
            "Missed one-off reminder remains available after sleep or restart");
        var daily = note with { RepeatsDaily = true };
        check(App.NoteReminderSchedule.Occurrence(daily, due.AddDays(4)) == due.AddDays(4),
            "Daily restart selects only current day's occurrence");
        var midnight = daily with { DueTime = new TimeOnly(0, 3), PopupLeadMinutes = 5 };
        DateTime next = due.Date.AddDays(1).AddMinutes(3);
        midnight = midnight with { PopupOccurrence = next.AddDays(-1) };
        check(App.NoteReminderSchedule.Occurrence(midnight, next.AddMinutes(-5)) == next,
            "Daily lead time can cross midnight after today's reminder was delivered");
        check(App.NoteReminderSchedule.Occurrence(midnight with { IsCompleted = true, CompletedAt = next.Date }, next.AddMinutes(-5)) == null,
            "Completing tomorrow's early popup prevents another delivery before midnight");
        check(App.NoteReminderSchedule.Occurrence(midnight with { IsCompleted = true, CompletedAt = next.Date, PopupOccurrence = next }, next.AddMinutes(-4)) == null,
            "Completing tomorrow's popup does not resurrect today's occurrence");
        var snoozedDaily = daily with { DueTime = new TimeOnly(23, 59), PopupOccurrence = due.Date.AddHours(23).AddMinutes(59),
            PopupSnoozedUntil = due.Date.AddDays(1).AddMinutes(4) };
        check(App.NoteReminderSchedule.Occurrence(snoozedDaily, due.Date.AddDays(1)) == snoozedDaily.PopupOccurrence
            && !App.NoteReminderSchedule.IsPending(snoozedDaily, snoozedDaily.PopupOccurrence.Value, due.Date.AddDays(1)),
            "Daily snooze crosses midnight without losing its occurrence or firing early");
        string path = TestPath();
        DateTime now = due;
        App.NotesStore store = new(path, () => now);
        check(store.SaveEntry(note) && store.RecordPopup(note.Id, due, null), "Popup delivery state persists");
        store = new(path, () => now);
        check(!App.NoteReminderSchedule.IsPending(store.Entries.Single(), due, now), "Restart does not duplicate delivered popup");
        check(store.RecordPopup(note.Id, due, due.AddMinutes(5)), "Snooze persists");
        store = new(path, () => now);
        check(!App.NoteReminderSchedule.IsPending(store.Entries.Single(), due, due.AddMinutes(4))
            && App.NoteReminderSchedule.IsPending(store.Entries.Single(), due, due.AddMinutes(5)), "Snooze survives restart and waits five minutes");
        check(store.SaveEntry(note with { Title = "Edited from stale draft" }) && store.Entries.Single().PopupSnoozedUntil == due.AddMinutes(5),
            "Stale editor cannot erase delivery or snooze state");
        check(store.SaveEntry(note with { DueTime = new TimeOnly(13, 0) }) && store.Entries.Single().PopupOccurrence == null,
            "Changing the schedule resets its delivery state");
        check(File.ReadAllText(path).Contains("\"Version\": 3"), "Popup format protects data from older versions");
        using Task work = new(() => Ui(check, note, due));
        Thread thread = new(() => work.RunSynchronously(TaskScheduler.Default));
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); work.GetAwaiter().GetResult();
    }

    private static void Ui(Action<bool, string> check, App.NoteEntry note, DateTime due)
    {
        App.Localization.RefreshAvailableLanguages();
        DateTime now = due;
        bool? fullscreen = true;
        App.NotesStore store = new(TestPath(), () => now); store.SaveEntry(note);
        using App.NoteReminderService service = new(store, () => "de", () => fullscreen);
        service.Tick();
        check(Field<App.NoteReminderPopup?>(service, "popup") == null && store.Entries.Single().PopupOccurrence == null,
            "Fullscreen defers delivery without consuming reminder");
        fullscreen = null; service.Tick();
        check(Field<App.NoteReminderPopup?>(service, "popup") == null, "Unknown fullscreen state also defers");
        fullscreen = false;
        IntPtr foreground = GetForegroundWindow();
        service.Tick();
        var popup = Field<App.NoteReminderPopup>(service, "popup");
        check(popup.Visible && popup.TopMost && popup.Owner == null && !popup.ShowInTaskbar
            && GetForegroundWindow() == foreground, "Popup is independent, topmost and does not steal foreground focus");
        fullscreen = true; service.Tick();
        check(!popup.Visible, "Already visible popup hides when fullscreen begins");
        fullscreen = false; service.Tick();
        check(popup.Visible && ReferenceEquals(popup, Field<App.NoteReminderPopup>(service, "popup")), "Fullscreen exit restores the same pending popup");
        typeof(App.NoteReminderService).GetMethod("HandleAction", Members)!.Invoke(service, new object[] { App.NoteReminderAction.Snooze });
        service.Tick();
        check(Field<App.NoteReminderPopup?>(service, "popup") == null, "Snooze closes popup without immediate redisplay");
        now = now.AddMinutes(5); service.Tick();
        check(Field<App.NoteReminderPopup?>(service, "popup") != null, "Snoozed popup returns after five minutes");
        typeof(App.NoteReminderService).GetMethod("HandleAction", Members)!.Invoke(service, new object[] { App.NoteReminderAction.Complete });
        service.Tick();
        check(store.Entries.Single().IsCompletedOn(now) && Field<App.NoteReminderPopup?>(service, "popup") == null,
            "Complete action updates store and closes popup");
        store.SaveEntry(note with { IsCompleted = false, CompletedAt = null, PopupLeadMinutes = 5 });
        service.Tick();
        check(Field<App.NoteReminderPopup?>(service, "popup") != null, "Edited schedule can deliver again");
        store.Delete(note.Id); service.Tick();
        check(Field<App.NoteReminderPopup?>(service, "popup") == null, "Deleting note removes its active popup");
        store.SaveEntry(note); service.Tick();
        store.SaveEntry(store.Entries.Single() with { PopupReminder = false }); service.Tick();
        check(Field<App.NoteReminderPopup?>(service, "popup") == null, "Disabling reminders closes active popup");
        store.SaveEntry(store.Entries.Single() with { PopupReminder = true }); service.Tick();
        typeof(App.NoteReminderService).GetMethod("HandleAction", Members)!.Invoke(service, new object[] { App.NoteReminderAction.Dismiss });
        service.Tick();
        check(Field<App.NoteReminderPopup?>(service, "popup") == null && !store.Entries.Single().IsCompleted,
            "Dismiss consumes only the reminder, not the task");
        store.SaveEntry(note with { Id = Guid.NewGuid(), Title = "Second" });
        store.SaveEntry(note with { Id = Guid.NewGuid(), Title = "Third" }); service.Tick();
        var queuedPopup = Field<App.NoteReminderPopup>(service, "popup");
        service.Tick();
        check(ReferenceEquals(queuedPopup, Field<App.NoteReminderPopup>(service, "popup")), "Simultaneous reminders show one popup at a time");
        typeof(App.NoteReminderService).GetMethod("HandleAction", Members)!.Invoke(service, new object[] { App.NoteReminderAction.Dismiss });
        service.Tick();
        check(Field<App.NoteReminderPopup?>(service, "popup") != null, "Next queued reminder appears after dismissal");
        service.Dispose();
        check(Field<App.NoteReminderPopup?>(service, "popup") == null, "Shutdown disposes open popup");
        string atDuePath = TestPath();
        now = due.AddMinutes(-5);
        var atDueStore = new App.NotesStore(atDuePath, () => now);
        atDueStore.SaveEntry(note with { PopupLeadMinutes = 5 });
        using (var earlyService = new App.NoteReminderService(atDueStore, () => "de", () => false))
        {
            earlyService.Tick();
            Field<Button>(Field<App.NoteReminderPopup>(earlyService, "popup"), "later").PerformClick();
            check(Field<App.NoteReminderPopup?>(earlyService, "popup") == null && atDueStore.Entries.Single().PopupSnoozedUntil == due,
                "At-due button persists exact deadline and closes early popup");
        }
        atDueStore = new App.NotesStore(atDuePath, () => now);
        using (var restarted = new App.NoteReminderService(atDueStore, () => "de", () => fullscreen))
        {
            now = due.AddSeconds(-1); restarted.Tick();
            check(Field<App.NoteReminderPopup?>(restarted, "popup") == null, "At-due reminder survives restart without early delivery");
            fullscreen = true; now = due; restarted.Tick();
            check(Field<App.NoteReminderPopup?>(restarted, "popup") == null, "At-due reminder still respects fullscreen");
            fullscreen = false; restarted.Tick();
            check(Field<App.NoteReminderPopup?>(restarted, "popup") is { Visible: true } duePopup && Field<Button>(duePopup, "later").Text == App.Localization.Get("NotesPopupSnooze", "de"),
                "Deadline delivers popup and hides at-due action");
            typeof(App.NoteReminderService).GetMethod("HandleAction", Members)!.Invoke(restarted, new object[] { App.NoteReminderAction.AtDue });
            check(Field<App.NoteReminderPopup?>(restarted, "popup") != null,
                "Stale at-due click cannot dismiss an already due reminder");
        }
        foreach (string language in new[] { "de", "en", "fr", "es", "ja" })
        {
            using App.NoteEditorForm editor = new(store, note, language);
            check(Field<CheckBox>(editor, "popup").Checked && Field<ComboBox>(editor, "popupLead").Items.Count == 3,
                $"Popup {language}: editor restores popup and all lead choices");
            Field<CheckBox>(editor, "timed").Checked = false;
            check(!Field<CheckBox>(editor, "popup").Enabled && !Field<ComboBox>(editor, "popupLead").Enabled,
                $"Popup {language}: no popup options for date-only note");
            Field<CheckBox>(editor, "timed").Checked = true;
            string? output = Environment.GetEnvironmentVariable("REMINDER_TEST_OUTPUT");
            if (output != null)
            {
                Directory.CreateDirectory(output);
                editor.Show(); editor.PerformLayout();
                using Bitmap bitmap = new(editor.Width, editor.Height);
                editor.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(output, $"editor-{language}.png"));
                editor.Hide();
            }
            foreach (float scale in new[] { 1f, 1.5f, 2f })
            {
                using App.NoteReminderPopup form = new(language, _ => { });
                form.UpdateContent(note, due, due.AddMinutes(-5));
                using Font scaledFont = new(form.Font.FontFamily, form.Font.Size * scale);
                form.Font = scaledFont;
                form.Scale(new SizeF(scale, scale)); form.PerformLayout();
                form.Show(); form.PerformLayout();
                form.UpdateContent(note, due, due.AddMinutes(-10));
                var later = Field<Button>(form, "later");
                var menu = Field<ContextMenuStrip>(form, "laterMenu");
                later.PerformClick();
                check(menu.Visible && menu.Items.Count == 2, $"Popup {language}/{scale}: early Later button opens both choices");
                form.UpdateContent(note, due, due.AddMinutes(-9));
                check(menu.Visible, $"Popup {language}/{scale}: regular refresh preserves open choices");
                form.UpdateContent(note, due, due.AddMinutes(-5));
                check(!menu.Visible && later.Text == App.Localization.Get("NotesPopupAtDueAction", language),
                    $"Popup {language}/{scale}: five-minute boundary switches to direct at-due button");
                check(Field<Button>(form, "later").Visible, $"Popup {language}/{scale}: at-due action available before deadline");
                var description = Field<TextBox>(form, "description");
                check(!description.Visible, $"Popup {language}/{scale}: empty description takes no space");
                string reminderText = "First line & details\r\n" + string.Join("\r\n", Enumerable.Repeat("Long reminder text 日本語", 80));
                form.UpdateContent(note with { Description = reminderText }, due, due.AddMinutes(-5));
                check(description.Visible && description.ReadOnly && description.Text == reminderText
                    && description.ScrollBars == ScrollBars.Vertical,
                    $"Popup {language}/{scale}: complete multiline description remains scrollable");
                description.Select(description.TextLength, 0); description.ScrollToCaret();
                form.UpdateContent(note with { Description = reminderText }, due, due.AddMinutes(-4));
                check(description.SelectionStart == description.TextLength,
                    $"Popup {language}/{scale}: status refresh preserves reading position");
                check(form.Controls[0].Controls.Cast<Control>().All(c => form.Controls[0].ClientRectangle.Contains(c.Bounds)),
                    $"Popup {language}/{scale}: content fits layout");
                check(!Field<Label>(form, "status").Text.Contains("NotesPopup"), $"Popup {language}/{scale}: localized status");
                var actionPanel = Field<Button>(form, "later").Parent!;
                check(actionPanel.Controls.Cast<Control>().Where(c => c.Visible).All(c => actionPanel.ClientRectangle.Contains(c.Bounds)),
                    $"Popup {language}/{scale}: all three action buttons fit");
                if (output != null)
                {
                    using Bitmap bitmap = new(form.Width, form.Height);
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(Path.Combine(output, $"popup-{language}-{scale}.png"));
                }
                form.UpdateContent(note, due, due);
                check(Field<Button>(form, "later").Text == App.Localization.Get("NotesPopupSnooze", language), $"Popup {language}/{scale}: at-due action disappears at deadline");
            }
        }
    }
}
