using Ical.Net;
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

        public string ProviderName => "iCalendar";
        public string StatusResourceKey { get; private set; } = "CalendarStatusNoSource";
        public DateTime? LastRefresh { get; private set; }

        public IcsCalendarProvider() : this(new HttpClient { Timeout = TimeSpan.FromSeconds(15) })
        {
            ownsHttpClient = true;
        }

        internal IcsCalendarProvider(HttpClient httpClient)
        {
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public bool SetSource(string? urls) => SetSources(urls, null);

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

        public async Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            if (disposed) return;

            await refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                List<CalendarSource> currentSources;
                Dictionary<CalendarSource, IReadOnlyList<CalendarEvent>> refreshedCache;
                lock (sync)
                {
                    currentSources = sources.ToList();
                    if (currentSources.Count == 0) return;
                    refreshedCache = new(sourceCache);
                    StatusResourceKey = "CalendarStatusLoading";
                }

                int successCount = 0;
                int invalidCount = 0;
                bool hasStaleData = false;

                for (int index = 0; index < currentSources.Count; index++)
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
                    if (!sources.SequenceEqual(currentSources)) return;

                    sourceCache = refreshedCache;
                    cachedEvents = refreshedCache.Values.SelectMany(events => events).OrderBy(e => e.Start).ToList();

                    if (successCount > 0)
                    {
                        LastRefresh = DateTime.Now;
                        StatusResourceKey = successCount == currentSources.Count
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

        private async Task<IReadOnlyList<CalendarEvent>> LoadSourceAsync(
            Uri uri,
            string sourceName,
            CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            string icsText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            Calendar? calendar = Calendar.Load(icsText);
            if (calendar == null) throw new InvalidOperationException("The iCalendar feed could not be parsed.");

            DateTime from = DateTime.Now.Date.AddDays(-1);
            DateTime until = DateTime.Now.Date.AddDays(90);
            CalDateTime calFrom = new(DateTime.SpecifyKind(from, DateTimeKind.Unspecified), true);
            CalDateTime calUntil = new(DateTime.SpecifyKind(until, DateTimeKind.Unspecified), true);

            return calendar
                .GetOccurrences(calFrom)
                .TakeWhileBefore(calUntil)
                .Where(o => o.Source is IcalCalendarEvent)
                .Select(o => ToCalendarEvent(o, sourceName))
                .Where(e => e != null)
                .Select(e => e!)
                .OrderBy(e => e.Start)
                .ToList();
        }

        public IReadOnlyList<CalendarEvent> GetUpcoming(DateTime from, int maxEntries, string languageCode)
        {
            lock (sync)
            {
                List<CalendarEvent> upcoming = cachedEvents
                    .Where(e => e.IsAllDay ? e.End.Date > from.Date : e.End > from)
                    .SelectMany(ExpandForDailyDisplay)
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
                    .ToList();
            }
        }

        private static List<string> SplitSources(string? value)
        {
            return (value ?? string.Empty)
                .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private static IEnumerable<CalendarEvent> ExpandForDailyDisplay(CalendarEvent calendarEvent)
        {
            if (!calendarEvent.IsAllDay)
            {
                yield return calendarEvent;
                yield break;
            }

            DateTime firstDay = calendarEvent.Start.Date;
            DateTime exclusiveEnd = calendarEvent.End.Date;
            if (exclusiveEnd <= firstDay) exclusiveEnd = firstDay.AddDays(1);

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

        private static DateTime ToLocalDateTime(CalDateTime value, bool allDay)
        {
            if (allDay || !value.HasTime) return value.Value.Date;
            if (value.IsFloating) return DateTime.SpecifyKind(value.Value, DateTimeKind.Local);
            return value.AsUtc.ToLocalTime();
        }

        private readonly record struct CalendarSource(string Url, bool IsHoliday);

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (ownsHttpClient) httpClient.Dispose();
            refreshLock.Dispose();
        }
    }
}
