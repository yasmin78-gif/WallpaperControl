using Ical.Net;
using Ical.Net.Evaluation;
using System.Diagnostics;
using IcalCalendarEvent = Ical.Net.CalendarComponents.CalendarEvent;
using Ical.Net.DataTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace WallpaperControl
{
    internal sealed class IcsCalendarProvider : ICalendarProvider, IDisposable
    {
        private readonly HttpClient httpClient;
        private readonly bool ownsHttpClient;
        private readonly SemaphoreSlim refreshLock = new(1, 1);
        private readonly object sync = new();
        private List<CalendarEvent> cachedEvents = new();
        private Dictionary<CalendarSource, IReadOnlyList<CalendarEvent>> sourceCache = new();
        private List<CalendarSource> sources = new();
        private bool disposed;
        private int activeRefreshes;

        public string ProviderName => "iCalendar";
        public string StatusResourceKey { get; private set; } = "CalendarStatusNoSource";
        public DateTime? LastRefresh { get; private set; }

        /// <summary>
        /// Initializes calendar fetching with an owned HTTP client and a 15-second request timeout.
        /// </summary>
        public IcsCalendarProvider() : this(new HttpClient { Timeout = TimeSpan.FromSeconds(15) })
        {
            ownsHttpClient = true;
        }

        /// <summary>
        /// Initializes calendar fetching with an HTTP client owned by the caller.
        /// </summary>
        /// <param name="httpClient">The HTTP client to use; the caller retains ownership.</param>
        internal IcsCalendarProvider(HttpClient httpClient)
        {
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        /// <summary>
        /// Updates the appointment feed configuration through the shared source-management path.
        /// </summary>
        /// <param name="urls">The configured calendar feed values.</param>
        /// <returns>True when the configured source set changed; otherwise, false.</returns>
        public bool SetSource(string? urls) => SetSources(urls, null);

        /// <summary>
        /// Updates appointment and holiday feeds and invalidates cached data when sources change.
        /// </summary>
        /// <param name="normalUrls">The appointment calendar feed values.</param>
        /// <param name="holidayUrls">The holiday calendar feed values.</param>
        /// <returns>True when appointment or holiday source configuration changed.</returns>
        public bool SetSources(string? normalUrls, string? holidayUrls)
        {
            List<CalendarSource> normalized = SplitSources(normalUrls)
                .Select(url => new CalendarSource(url, false))
                .Concat(SplitSources(holidayUrls).Select(url => new CalendarSource(url, true)))
                .Distinct()
                .ToList();

            lock (sync)
            {
                if (sources.SequenceEqual(normalized)) return false;
                sources = normalized;
                cachedEvents.Clear();
                sourceCache.Clear();
                LastRefresh = null;
                StatusResourceKey = normalized.Count == 0 ? "CalendarStatusNoSource" : "CalendarStatusLoading";
                return true;
            }
        }

        /// <summary>
        /// Refreshes configured feeds while retaining cached events for sources that temporarily fail.
        /// </summary>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        /// <returns>A task representing completion of the asynchronous operation.</returns>
        public async Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                if (disposed) return;
                activeRefreshes++;
            }
            try
            {
                await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                lock (sync)
                {
                    if (--activeRefreshes == 0 && disposed) refreshLock.Dispose();
                }
            }
        }

        private async Task RefreshCoreAsync(CancellationToken cancellationToken)
        {
            await refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                List<CalendarSource> currentSources;
                Dictionary<CalendarSource, IReadOnlyList<CalendarEvent>> refreshedCache;
                lock (sync)
                {
                    if (disposed) return;
                    currentSources = sources.ToList();
                    if (currentSources.Count == 0) return;
                    refreshedCache = new(sourceCache);
                    StatusResourceKey = "CalendarStatusLoading";
                }

                int successCount = 0;
                int invalidCount = 0;
                bool hasStaleData = false;

                for (int index = 0; index < Math.Min(currentSources.Count, CalendarFeedLimits.MaxSources); index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    CalendarSource calendarSource = currentSources[index];
                    string url = calendarSource.Url;

                    if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
                        (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                    {
                        invalidCount++;
                        hasStaleData |= refreshedCache.ContainsKey(calendarSource);
                        continue;
                    }

                    try
                    {
                        IReadOnlyList<CalendarEvent> events = await LoadSourceAsync(
                            uri,
                            calendarSource.IsHoliday ? $"Holiday:{index + 1}" : $"iCalendar {index + 1}",
                            cancellationToken).ConfigureAwait(false);
                        // A successful empty feed replaces old entries too.
                        refreshedCache[calendarSource] = events;
                        successCount++;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        hasStaleData |= refreshedCache.ContainsKey(calendarSource);
                        // Never log the private feed URL. Some networking exceptions include it.
                        AppLogger.Warning(
                            $"iCalendar feed {index + 1} could not be refreshed.",
                            new InvalidOperationException(ex.GetType().Name));
                    }
                }

                lock (sync)
                {
                    if (disposed || !sources.SequenceEqual(currentSources)) return;

                    sourceCache = refreshedCache;
                    bool limited = refreshedCache.Values.Sum(events => events.Count) > CalendarFeedLimits.MaxOccurrences;
                    cachedEvents = refreshedCache.Values.SelectMany(events => events).OrderBy(e => e.Start)
                        .Take(CalendarFeedLimits.MaxOccurrences).ToList();

                    if (successCount > 0)
                    {
                        LastRefresh = DateTime.Now;
                        StatusResourceKey = successCount == currentSources.Count && !limited
                            ? "CalendarStatusConnected"
                            : hasStaleData ? "CalendarStatusStale" : "CalendarStatusPartial";
                    }
                    else
                    {
                        StatusResourceKey = hasStaleData ? "CalendarStatusStale" : invalidCount == currentSources.Count
                            ? "CalendarStatusInvalidAddress"
                            : "CalendarStatusError";
                    }
                }
            }
            finally
            {
                refreshLock.Release();
            }
        }

        /// <summary>
        /// Downloads and expands one iCalendar feed into application calendar events.
        /// </summary>
        /// <param name="uri">The calendar feed URI to fetch.</param>
        /// <param name="sourceName">The source label attached to the resulting events.</param>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        /// <returns>A task whose result contains the events expanded from the feed.</returns>
        private async Task<IReadOnlyList<CalendarEvent>> LoadSourceAsync(
            Uri uri,
            string sourceName,
            CancellationToken cancellationToken)
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using HttpResponseMessage response = await httpClient.GetAsync(uri,
                HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            string text = await CalendarFeedLimits.ReadResponseAsync(response.Content, timeout.Token).ConfigureAwait(false);
            // Never parse a synchronous/cached response on the WinForms event thread.
            return await Task.Run(() => ParseSource(text, sourceName, DateTime.Today, timeout.Token),
                timeout.Token).ConfigureAwait(false);
        }

        internal static IReadOnlyList<CalendarEvent> ParseSource(string text, string sourceName,
            DateTime today, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Stopwatch budget = Stopwatch.StartNew();
            DateTime from = today.Date.AddDays(-1);
            DateTime until = today.Date.AddDays(CalendarFeedLimits.HorizonDays);
            text = CalendarFeedLimits.Prepare(text, until, cancellationToken);
            Calendar? calendar = Calendar.Load(text);
            if (calendar == null) throw new InvalidOperationException("The iCalendar feed could not be parsed.");
            cancellationToken.ThrowIfCancellationRequested();
            CalDateTime calFrom = new(DateTime.SpecifyKind(from, DateTimeKind.Unspecified), true);
            CalDateTime calUntil = new(DateTime.SpecifyKind(until, DateTimeKind.Unspecified), true);
            List<CalendarEvent> events = new();
            int examined = 0;
            foreach (Occurrence occurrence in calendar.GetOccurrences<IcalCalendarEvent>(calFrom,
                new EvaluationOptions { MaxUnmatchedIncrementsLimit = 128 }).TakeWhileBefore(calUntil))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (budget.Elapsed > TimeSpan.FromSeconds(2))
                    throw new InvalidOperationException("Calendar processing budget exceeded.");
                if (++examined > CalendarFeedLimits.MaxOccurrences)
                    throw new InvalidOperationException("Calendar occurrence limit exceeded.");
                CalendarEvent? item = ToCalendarEvent(occurrence, sourceName);
                if (item != null) events.Add(item);
            }
            return events;
        }

        /// <summary>
        /// Selects upcoming entries by occupied calendar days, including daily expansion of multi-day events.
        /// </summary>
        /// <param name="from">The reference time used to select upcoming events.</param>
        /// <param name="maxEntries">The maximum number of occupied calendar days to include.</param>
        /// <param name="languageCode">The language code used for localized text.</param>
        /// <returns>The entries belonging to the requested number of occupied upcoming calendar days.</returns>
        public IReadOnlyList<CalendarEvent> GetUpcoming(DateTime from, int maxEntries, string languageCode)
        {
            lock (sync)
            {
                List<CalendarEvent> upcoming = cachedEvents
                    .Where(e => e.Start < from.Date.AddDays(CalendarFeedLimits.HorizonDays) &&
                        (e.IsAllDay ? e.End.Date > from.Date : e.End > from))
                    .SelectMany(e => ExpandForDailyDisplay(e, from.Date, from.Date.AddDays(CalendarFeedLimits.HorizonDays))
                        .Take(Math.Clamp(maxEntries, 1, 9)))
                    .Where(e => e.IsAllDay ? e.Start.Date >= from.Date : e.End > from)
                    .OrderBy(e => e.Start.Date)
                    .ThenByDescending(e => e.SourceName.StartsWith("Holiday:", StringComparison.Ordinal))
                    .ThenBy(e => e.Start)
                    .ThenByDescending(e => e.IsAllDay)
                    .ToList();

                // maxEntries now represents occupied calendar days. Empty days do not
                // consume a slot; we continue forward until the requested number of
                // days containing at least one appointment has been collected.
                HashSet<DateTime> selectedDays = upcoming
                    .Select(e => e.Start.Date)
                    .Distinct()
                    .Take(Math.Max(1, maxEntries))
                    .ToHashSet();

                return upcoming
                    .Where(e => selectedDays.Contains(e.Start.Date))
                    .Take(CalendarFeedLimits.MaxDisplayRows + 1) // extra row signals overflow
                    .ToList();
            }
        }

        /// <summary>
        /// Splits configured calendar feed text into individual source values.
        /// </summary>
        /// <param name="value">The configured calendar source text to split into individual addresses or paths.</param>
        /// <returns>The individual configured feed values after splitting and normalization.</returns>
        private static List<string> SplitSources(string? value)
        {
            return (value ?? string.Empty)
                .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Expands a multi-day event into the day entries used by the calendar widget.
        /// </summary>
        /// <param name="calendarEvent">The event to expand into daily display entries.</param>
        /// <returns>The daily entries used to display the supplied event.</returns>
        private static IEnumerable<CalendarEvent> ExpandForDailyDisplay(CalendarEvent calendarEvent, DateTime from, DateTime until)
        {
            if (!calendarEvent.IsAllDay)
            {
                yield return calendarEvent;
                yield break;
            }

            DateTime firstDay = calendarEvent.Start.Date < from ? from : calendarEvent.Start.Date;
            DateTime exclusiveEnd = calendarEvent.End.Date;
            if (exclusiveEnd > until) exclusiveEnd = until;

            for (DateTime day = firstDay; day < exclusiveEnd; day = day.AddDays(1))
            {
                yield return new CalendarEvent(
                    day,
                    day.AddDays(1),
                    true,
                    calendarEvent.Title,
                    calendarEvent.Location,
                    calendarEvent.SourceName);
            }
        }

        /// <summary>
        /// Converts an iCalendar occurrence into the application&apos;s calendar event model.
        /// </summary>
        /// <param name="occurrence">The expanded iCalendar occurrence to convert.</param>
        /// <param name="sourceName">The source label attached to the resulting events.</param>
        /// <returns>The converted application event, or null when the occurrence cannot be represented.</returns>
        private static CalendarEvent? ToCalendarEvent(Occurrence occurrence, string sourceName)
        {
            if (occurrence.Source is not IcalCalendarEvent source) return null;
            if (string.Equals(source.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase)) return null;

            bool allDay = source.IsAllDay || !occurrence.Period.StartTime.HasTime;
            DateTime start = ToLocalDateTime(occurrence.Period.StartTime, allDay);
            CalDateTime? effectiveEnd = occurrence.Period.EffectiveEndTime;
            DateTime end = effectiveEnd != null
                ? ToLocalDateTime(effectiveEnd, allDay)
                : (allDay ? start.Date.AddDays(1) : start.AddHours(1));

            if (end <= start) end = allDay ? start.Date.AddDays(1) : start.AddHours(1);

            string title = string.IsNullOrWhiteSpace(source.Summary) ? "(ohne Titel)" : source.Summary.Trim();
            string location = source.Location?.Trim() ?? string.Empty;
            return new WallpaperControl.CalendarEvent(start, end, allDay, title, location, sourceName);
        }

        /// <summary>
        /// Converts a calendar date to local display time while retaining all-day date semantics.
        /// </summary>
        /// <param name="value">The calendar timestamp to convert to local time.</param>
        /// <param name="allDay">Whether the calendar value represents an all-day date rather than a timed event.</param>
        /// <returns>The local event time or the unchanged date for an all-day value.</returns>
        private static DateTime ToLocalDateTime(CalDateTime value, bool allDay)
        {
            if (allDay || !value.HasTime) return value.Value.Date;
            if (value.IsFloating) return DateTime.SpecifyKind(value.Value, DateTimeKind.Local);
            return value.AsUtc.ToLocalTime();
        }

        /// <summary>
        /// Identifies a configured feed and whether its entries should use holiday presentation.
        /// </summary>
        /// <param name="Url">The private calendar feed address used for retrieval and caching.</param>
        /// <param name="IsHoliday">True to mark entries from this feed as holidays.</param>
        private readonly record struct CalendarSource(string Url, bool IsHoliday);

        /// <summary>
        /// Releases refresh coordination resources and the HTTP client when this provider owns it.
        /// </summary>
        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                if (activeRefreshes == 0) refreshLock.Dispose();
            }
            if (ownsHttpClient) httpClient.Dispose();
        }
    }
}
