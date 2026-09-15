using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WallpaperControl
{
    internal interface ICalendarProvider
    {
        string ProviderName { get; }
        string StatusResourceKey { get; }
        DateTime? LastRefresh { get; }
        Task RefreshAsync(CancellationToken cancellationToken = default);
        IReadOnlyList<CalendarEvent> GetUpcoming(DateTime from, int maxEntries, string languageCode);
    }
}
