using System.Net;
using WallpaperControl;

internal static class CalendarTests
{
    /// <summary>
    /// Runs the calendar regression checks using the supplied assertion callback.
    /// </summary>
    /// <param name="check">The assertion callback that records a passing check or throws on failure.</param>
    internal static void Run(Action<bool, string> check)
    {
        using var handler = new FeedHandler();
        using var client = new HttpClient(handler);
        using var provider = new IcsCalendarProvider(client);
        provider.SetSource("https://calendar.test/a\nhttps://calendar.test/b");
        // Refreshes the calendar fixture synchronously for the regression check.
        void Refresh() => provider.RefreshAsync().GetAwaiter().GetResult();
        // Reads the event titles from the calendar fixture.
        string[] Titles() => provider.GetUpcoming(DateTime.Today, 10, "en").Select(e => e.Title).Order().ToArray();

        handler.Fail = true;
        Refresh();
        check(Titles().Length == 0 && provider.StatusResourceKey == "CalendarStatusError" && provider.LastRefresh == null,
            "Calendar first failure reports error without cached data");
        handler.Fail = false;
        Refresh();
        var lastSuccess = provider.LastRefresh;
        check(Titles().SequenceEqual(new[] { "a", "b" }) && provider.StatusResourceKey == "CalendarStatusConnected",
            "Calendar successful refresh loads both feeds");
        handler.Fail = true;
        Refresh();
        check(Titles().SequenceEqual(new[] { "a", "b" }) && provider.StatusResourceKey == "CalendarStatusStale" &&
            provider.LastRefresh == lastSuccess, "Calendar outage retains events and successful refresh time");
        Refresh();
        check(Titles().Length == 2 && provider.StatusResourceKey == "CalendarStatusStale",
            "Repeated calendar failures retain cached events");
        handler.Fail = false;
        handler.FailB = true;
        handler.Suffix = " updated";
        Refresh();
        check(Titles().SequenceEqual(new[] { "a updated", "b" }) && provider.StatusResourceKey == "CalendarStatusStale",
            "Partial calendar outage updates working feed and preserves failed feed");
        handler.FailB = false;
        Refresh();
        check(Titles().SequenceEqual(new[] { "a updated", "b updated" }) && provider.StatusResourceKey == "CalendarStatusConnected",
            "Calendar recovery replaces stale entries and clears warning");
        handler.Empty = true;
        Refresh();
        check(Titles().Length == 0 && provider.StatusResourceKey == "CalendarStatusConnected",
            "Successful empty calendars remove obsolete entries");
        handler.Empty = false;
        Refresh();
        provider.SetSource("https://calendar.test/c");
        handler.Fail = true;
        Refresh();
        check(Titles().Length == 0 && provider.StatusResourceKey == "CalendarStatusError",
            "Changed calendar source does not reuse unrelated cached events");
        provider.SetSource(null);
        Refresh();
        check(Titles().Length == 0 && provider.StatusResourceKey == "CalendarStatusNoSource" && provider.LastRefresh == null,
            "Removing calendar sources clears cached data");
    }

    private sealed class FeedHandler : HttpMessageHandler
    {
        internal bool Fail, FailB, Empty;
        internal string Suffix = "";
        /// <summary>
        /// Returns the configured feed response or simulated failure without contacting a real server.
        /// </summary>
        /// <param name="request">The HTTP request to handle.</param>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        /// <returns>A task containing the simulated response, or a simulated request failure.</returns>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string name = request.RequestUri!.AbsolutePath.Trim('/');
            if (Fail || (FailB && name == "b"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            string day = DateTime.Today.AddDays(1).ToString("yyyyMMdd");
            string nextDay = DateTime.Today.AddDays(2).ToString("yyyyMMdd");
            string entry = Empty ? "" : $"BEGIN:VEVENT\r\nUID:{name}\r\nDTSTAMP:{day}T000000Z\r\nDTSTART;VALUE=DATE:{day}\r\nDTEND;VALUE=DATE:{nextDay}\r\nSUMMARY:{name}{Suffix}\r\nEND:VEVENT\r\n";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//WallpaperControl Tests//EN\r\n" + entry + "END:VCALENDAR\r\n")
            });
        }
    }
}
