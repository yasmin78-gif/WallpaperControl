namespace WallpaperControl;

internal readonly record struct NoteReminder(Guid Id, DateTime Due);

/// <summary>Local-time occurrences; missed daily tasks do not accumulate a backlog.</summary>
internal static class NoteReminderSchedule
{
    internal static DateTime? Occurrence(NoteEntry entry, DateTime now)
    {
        if (!entry.PopupReminder || !entry.HasReminder || entry.DueDate is not DateOnly start
            || entry.DueTime is not TimeOnly time) return null;
        if (entry.PopupSnoozedUntil is DateTime snooze && entry.PopupOccurrence is DateTime snoozedDue
            && (!entry.RepeatsDaily || now.Date <= snooze.Date) && !Completed(entry, snoozedDue)) return snoozedDue;
        DateTime due = start.ToDateTime(time);
        if (entry.RepeatsDaily && now.Date >= due.Date)
        {
            due = now.Date.Add(time.ToTimeSpan());
            bool tomorrowInLeadWindow = due.Date < DateTime.MaxValue.Date
                && now >= due.AddDays(1).AddMinutes(-entry.PopupLeadMinutes);
            if (entry.IsCompletedOn(now) || now < due.AddMinutes(-entry.PopupLeadMinutes) || tomorrowInLeadWindow)
            {
                if (due.Date == DateTime.MaxValue.Date) return null;
                due = due.AddDays(1);
            }
        }
        else if (entry.IsCompletedOn(now)) return null;
        if (Completed(entry, due)) return null;
        return now >= due.AddMinutes(-entry.PopupLeadMinutes) ? due : null;
    }

    internal static bool IsPending(NoteEntry entry, DateTime due, DateTime now) =>
        entry.PopupOccurrence != due || (entry.PopupSnoozedUntil is DateTime snooze && now >= snooze);

    internal static bool Completed(NoteEntry entry, DateTime due) =>
        entry.IsCompleted && (!entry.RepeatsDaily || entry.CompletedAt?.Date == due.Date);

    internal static bool IsCurrent(NoteEntry entry, DateTime due) =>
        entry.PopupReminder && entry.PopupOccurrence == due && !Completed(entry, due);
}

/// <summary>UI-thread service, independent of widget visibility and wallpaper auto-pause settings.</summary>
internal sealed class NoteReminderService : IDisposable
{
    private readonly NotesStore store;
    private readonly Func<string> language;
    private readonly Func<bool?> fullscreen;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 500 };
    private NoteReminderPopup? popup;
    private NoteReminder? current;
    private bool ticking;
    private bool disposed;

    internal NoteReminderService(NotesStore store, Func<string> language, Func<bool?>? fullscreen = null)
    {
        this.store = store;
        this.language = language;
        this.fullscreen = fullscreen ?? FullscreenActivityDetector.GetFullscreenState;
        timer.Tick += (_, _) => Tick();
    }

    internal void Start() => timer.Start();

    internal void Tick()
    {
        if (disposed || ticking) return;
        ticking = true;
        try
        {
            DateTime now = store.Now;
            // An unknown foreground state is not permission to cover a fullscreen app.
            bool blocked = fullscreen() != false;
            if (popup != null && current is NoteReminder active)
            {
                NoteEntry? entry = store.Entries.FirstOrDefault(e => e.Id == active.Id);
                if (entry == null || !NoteReminderSchedule.IsCurrent(entry, active.Due))
                    ClosePopup();
                else
                {
                    if (blocked) popup.Hide();
                    else { popup.UpdateContent(entry, active.Due, now); if (!popup.Visible) popup.Show(); }
                    return;
                }
            }
            if (blocked || !store.CanWrite) return;
            var pending = store.Entries.Select(e => (Entry: e, Due: NoteReminderSchedule.Occurrence(e, now)))
                .Where(x => x.Due.HasValue && NoteReminderSchedule.IsPending(x.Entry, x.Due.Value, now))
                .OrderBy(x => x.Due).ThenBy(x => x.Entry.CreatedAt).FirstOrDefault();
            if (pending.Entry == null) return;
            var reminder = new NoteReminder(pending.Entry.Id, pending.Due!.Value);
            // Persist before display so restarting does not show a dismissed occurrence again.
            if (!store.RecordPopup(reminder.Id, reminder.Due, null)) return;
            current = reminder;
            popup = new NoteReminderPopup(language(), action => HandleAction(action));
            popup.UpdateContent(pending.Entry, reminder.Due, now);
            popup.Show();
        }
        finally { ticking = false; }
    }

    private void HandleAction(NoteReminderAction action)
    {
        if (current is not NoteReminder reminder) return;
        NoteEntry? latest = store.Entries.FirstOrDefault(e => e.Id == reminder.Id);
        if (latest == null || !NoteReminderSchedule.IsCurrent(latest, reminder.Due))
        { ClosePopup(); return; }
        // A click queued just before the deadline must not dismiss a now-due alert.
        if (action is NoteReminderAction.AtDue or NoteReminderAction.Snooze
            && !NoteReminderPopup.LaterOptions(reminder.Due, store.Now).Contains(action))
        { popup?.UpdateContent(latest, reminder.Due, store.Now); return; }
        // A daily popup displayed before midnight belongs to tomorrow, not today.
        bool saved = action switch
        {
            NoteReminderAction.Complete => CompleteOccurrence(reminder),
            NoteReminderAction.Snooze => store.RecordPopup(reminder.Id, reminder.Due, store.Now.AddMinutes(5)),
            NoteReminderAction.AtDue => store.RecordPopup(reminder.Id, reminder.Due, reminder.Due),
            _ => true
        };
        if (saved) ClosePopup();
        else popup?.ShowSaveError();
    }

    private bool CompleteOccurrence(NoteReminder reminder)
    {
        NoteEntry? entry = store.Entries.FirstOrDefault(e => e.Id == reminder.Id);
        if (entry == null) return true;
        if (!NoteReminderSchedule.IsCurrent(entry, reminder.Due)) return true;
        return store.SaveEntry(entry with { IsCompleted = true,
            CompletedAt = entry.RepeatsDaily ? reminder.Due.Date.Add(store.Now.TimeOfDay) : store.Now });
    }

    private void ClosePopup()
    {
        NoteReminderPopup? old = popup;
        popup = null; current = null;
        old?.Dispose();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        timer.Stop(); timer.Dispose(); ClosePopup();
    }
}
