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
        /// <summary>
        /// Refreshes the provider&apos;s cached calendar data while honoring cancellation.
        /// </summary>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        /// <returns>A task representing completion of the asynchronous operation.</returns>
        Task RefreshAsync(CancellationToken cancellationToken = default);
        /// <summary>
        /// Selects upcoming calendar entries for the requested display window.
        /// </summary>
        /// <param name="from">The reference time used to select upcoming events.</param>
        /// <param name="maxEntries">The requested calendar display limit, interpreted by the provider.</param>
        /// <param name="languageCode">The language code used for localized text.</param>
        /// <returns>The upcoming entries selected for the requested display limit.</returns>
        IReadOnlyList<CalendarEvent> GetUpcoming(DateTime from, int maxEntries, string languageCode);
    }
}
