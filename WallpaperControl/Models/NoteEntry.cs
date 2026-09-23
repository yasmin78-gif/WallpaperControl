namespace WallpaperControl
{
    /// <summary>Local calendar dates and optional wall-clock times; no implicit midnight alarm.</summary>
    internal sealed record NoteEntry
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public string Title { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public bool HasReminder { get; init; }
        public DateOnly? DueDate { get; init; }
        public TimeOnly? DueTime { get; init; }
        public bool IsCompleted { get; init; }
        public bool RepeatsDaily { get; init; }
        public DateTime CreatedAt { get; init; } = DateTime.Now;
        public DateTime? CompletedAt { get; init; }

        internal bool IsValid => Id != Guid.Empty && !string.IsNullOrWhiteSpace(Title) && Title.Length <= 200
            && Description != null && Description.Length <= 8000 && CreatedAt != default
            && HasReminder == DueDate.HasValue && (HasReminder || !DueTime.HasValue)
            && IsCompleted == CompletedAt.HasValue
            && (!RepeatsDaily || HasReminder)
            && (!DueDate.HasValue || (DueDate.Value >= new DateOnly(1753, 1, 1) && DueDate.Value <= new DateOnly(9998, 12, 31)));

        internal bool IsCompletedOn(DateTime now) => IsCompleted && (!RepeatsDaily || CompletedAt?.Date == now.Date);
        internal DateOnly? EffectiveDueDate(DateTime now) => RepeatsDaily && DueDate <= DateOnly.FromDateTime(now)
            ? DateOnly.FromDateTime(now) : DueDate;

        internal bool IsOverdue(DateTime now) => HasReminder && !IsCompletedOn(now) && EffectiveDueDate(now) is DateOnly date
            && (date < DateOnly.FromDateTime(now) || (date == DateOnly.FromDateTime(now) && DueTime is TimeOnly time && time < TimeOnly.FromDateTime(now)));

        internal bool IsDueToday(DateTime now) => HasReminder && !IsCompletedOn(now) && EffectiveDueDate(now) == DateOnly.FromDateTime(now)
            && (!DueTime.HasValue || DueTime.Value <= TimeOnly.FromDateTime(now));
    }

    internal sealed record NoteGroup(string Key, DateOnly? Date, IReadOnlyList<NoteEntry> Entries);

    internal static class NoteGrouping
    {
        internal static IReadOnlyList<NoteGroup> Build(IEnumerable<NoteEntry> entries, DateTime now)
        {
            DateOnly today = DateOnly.FromDateTime(now);
            // Undated first, overdue next, then ascending days. Date-only precedes timed items.
            return entries.Where(e => !e.IsCompletedOn(now))
                .GroupBy(e => !e.HasReminder ? (0, (DateOnly?)null) : !e.RepeatsDaily && e.IsOverdue(now) ? (1, (DateOnly?)null) : (2, e.EffectiveDueDate(now)))
                .OrderBy(g => g.Key.Item1).ThenBy(g => g.Key.Item2)
                .Select(g => new NoteGroup(g.Key.Item1 == 0 ? "NotesUndated" : g.Key.Item1 == 1 ? "NotesOverdue"
                    : g.Key.Item2 == today ? "NotesToday" : g.Key.Item2 == today.AddDays(1) ? "NotesTomorrow" : "",
                    g.Key.Item2, g.OrderBy(e => e.EffectiveDueDate(now)).ThenBy(e => e.DueTime.HasValue).ThenBy(e => e.DueTime)
                        .ThenBy(e => e.CreatedAt).ThenBy(e => e.Id).ToArray())).ToArray();
        }
    }
}
