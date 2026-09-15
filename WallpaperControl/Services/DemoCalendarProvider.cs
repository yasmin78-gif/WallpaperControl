using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WallpaperControl
{
    internal sealed class DemoCalendarProvider : ICalendarProvider
    {
        public string ProviderName => "Demo";
        public string StatusResourceKey => "CalendarStatusDemo";
        public DateTime? LastRefresh => DateTime.Now;

        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public IReadOnlyList<CalendarEvent> GetUpcoming(DateTime from, int maxEntries, string languageCode)
        {
            DateTime today = from.Date;
            var entries = new List<CalendarEvent>
            {
                New(today.AddHours(9).AddMinutes(30), false, Localization.Get("CalendarDemoMeeting", languageCode), "Microsoft Teams"),
                New(today.AddHours(13), false, Localization.Get("CalendarDemoLunch", languageCode), Localization.Get("CalendarDemoCafeteria", languageCode)),
                New(today.AddHours(16).AddMinutes(30), false, Localization.Get("CalendarDemoAppointment", languageCode), Localization.Get("CalendarDemoCityCenter", languageCode)),
                New(today.AddDays(1).AddHours(8), false, Localization.Get("CalendarDemoShift", languageCode), Localization.Get("CalendarDemoHomeOffice", languageCode)),
                New(today.AddDays(1).AddHours(14), false, Localization.Get("CalendarDemoProject", languageCode), "Microsoft Teams"),
                New(today.AddDays(2), true, Localization.Get("CalendarDemoBirthday", languageCode), Localization.Get("CalendarDemoPrivate", languageCode)),
                New(today.AddDays(2).AddHours(18), false, Localization.Get("CalendarDemoDinner", languageCode), Localization.Get("CalendarDemoRestaurant", languageCode))
            };

            List<CalendarEvent> upcoming = entries
                .Where(e => e.IsAllDay ? e.Start.Date >= from.Date : e.End > from)
                .OrderBy(e => e.Start)
                .ToList();

            HashSet<DateTime> selectedDays = upcoming
                .Select(e => e.Start.Date)
                .Distinct()
                .Take(Math.Max(1, maxEntries))
                .ToHashSet();

            return upcoming.Where(e => selectedDays.Contains(e.Start.Date)).ToList();
        }

        private static CalendarEvent New(DateTime start, bool allDay, string title, string location)
        {
            DateTime end = allDay ? start.Date.AddDays(1) : start.AddHours(1);
            return new CalendarEvent(start, end, allDay, title, location, "Demo");
        }
    }
}
