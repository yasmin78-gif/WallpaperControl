using System;

namespace WallpaperControl
{
    internal sealed record CalendarEvent(
        DateTime Start,
        DateTime End,
        bool IsAllDay,
        string Title,
        string Location,
        string SourceName = "");
}
