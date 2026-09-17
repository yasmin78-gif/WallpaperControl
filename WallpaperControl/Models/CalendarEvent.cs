using System;

namespace WallpaperControl
{
    /// <summary>
    /// Represents a calendar occurrence using local times or date-only boundaries for all-day entries.
    /// </summary>
    /// <param name="Start">The occurrence start time or inclusive all-day start date.</param>
    /// <param name="End">The occurrence end time or exclusive all-day end date.</param>
    /// <param name="IsAllDay">True when the occurrence occupies whole calendar days.</param>
    /// <param name="Title">The event title displayed by the widget.</param>
    /// <param name="Location">The optional event location text.</param>
    /// <param name="SourceName">The source label, with a Holiday: prefix for holiday entries.</param>
    internal sealed record CalendarEvent(
        DateTime Start,
        DateTime End,
        bool IsAllDay,
        string Title,
        string Location,
        string SourceName = "");
}
