namespace WallpaperControl;

internal enum NoteRepeat { None = 0, Daily = 1, Monthly = 2, Yearly = 3, Custom = 4 }
internal enum NoteRepeatUnit { Days = 0, Weeks = 1, Months = 2, Years = 3 }

/// <summary>All occurrences are computed from the original local calendar anchor, never chained.</summary>
internal static class NoteRecurrence
{
    internal static DateOnly? At(NoteEntry entry, int index)
    {
        if (entry.DueDate is not DateOnly start || index < 0) return null;
        DateOnly anchor = entry.RepeatAnchor ?? start;
        int interval = entry.EffectiveRepeat == NoteRepeat.Custom ? entry.RepeatInterval : 1;
        var unit = entry.EffectiveRepeat switch { NoteRepeat.Monthly => NoteRepeatUnit.Months, NoteRepeat.Yearly => NoteRepeatUnit.Years, _ => entry.EffectiveRepeat == NoteRepeat.Custom ? entry.RepeatUnit : NoteRepeatUnit.Days };
        if (interval <= 0) return null;
        try
        {
            int amount = checked(index * interval);
            return unit switch { NoteRepeatUnit.Days => anchor.AddDays(amount), NoteRepeatUnit.Weeks => anchor.AddDays(checked(amount * 7)), NoteRepeatUnit.Months => anchor.AddMonths(amount), NoteRepeatUnit.Years => anchor.AddYears(amount), _ => null };
        }
        catch (ArgumentOutOfRangeException) { return null; }
        catch (OverflowException) { return null; }
    }

    // Binary search keeps even multi-millennial overdue series bounded.
    internal static DateOnly? Next(NoteEntry entry, DateOnly after)
    {
        int low = 0, high = 3652059;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            DateOnly? candidate = At(entry, mid);
            if (candidate == null || candidate > after) high = mid; else low = mid + 1;
        }
        return At(entry, low);
    }

    internal static DateOnly? Current(NoteEntry entry, DateTime now)
    {
        DateOnly start = entry.DueDate!.Value;
        if (!entry.IsCompleted || entry.CompletedAt is not DateTime completed) return start;
        DateOnly cutoff = entry.RepeatCompletedThrough ?? start;
        DateOnly completedDate = DateOnly.FromDateTime(completed);
        // A missed older occurrence may be completed before today's scheduled time.
        if (entry.DueTime is TimeOnly time && time > TimeOnly.FromDateTime(completed) && completedDate > DateOnly.MinValue)
            completedDate = completedDate.AddDays(-1);
        return Next(entry, completedDate > cutoff ? completedDate : cutoff);
    }

    internal static string Summary(NoteEntry entry, string language)
    {
        if (entry.EffectiveRepeat != NoteRepeat.Custom)
            return Localization.Get(entry.EffectiveRepeat switch { NoteRepeat.None => "NotesRepeatNone", NoteRepeat.Daily => "NotesRepeatDaily", NoteRepeat.Monthly => "NotesRepeatMonthly", _ => "NotesRepeatYearly" }, language);
        string[] singular = ["NotesEveryDay", "NotesEveryWeek", "NotesEveryMonth", "NotesEveryYear"];
        string[] plural = ["NotesUnitDays", "NotesUnitWeeks", "NotesUnitMonths", "NotesUnitYears"];
        return entry.RepeatInterval == 1 ? Localization.Get(singular[(int)entry.RepeatUnit], language)
            : string.Format(System.Globalization.CultureInfo.GetCultureInfo(language), Localization.Get("NotesEveryFormat", language), entry.RepeatInterval, Localization.Get(plural[(int)entry.RepeatUnit], language));
    }
}
