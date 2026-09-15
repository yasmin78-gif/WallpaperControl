using WallpaperControl;

internal static class StatisticsTests
{
    internal static void Run(Action<bool, string> check)
    {
        DateTime now = new(2026, 9, 15, 23, 59, 0);
        int saves = 0;
        var statistics = new WallpaperStatistics(() => now, () => saves++);
        statistics.Load(new PersistentStatisticsData());
        var liveCounts = statistics.ViewCounts;
        const string a = @"C:\Images\A.jpg";
        const string b = @"C:\Images\B.jpg";
        statistics.RecordView(a);
        check(statistics.ViewCounts[a] == 1 && statistics.LastShown[a] == now &&
            statistics.DailyViewCounts[a]["2026-09-15"] == 1 && saves == 1,
            "Statistics records first view and saves it");
        statistics.RecordView(a.ToLowerInvariant());
        check(statistics.ViewCounts[a] == 1 && saves == 1,
            "Statistics ignores consecutive duplicate paths regardless of case");
        now = now.AddSeconds(30);
        statistics.RecordView(b);
        now = now.AddSeconds(60);
        statistics.RecordView(a);
        check(statistics.ViewCounts[a] == 2 && statistics.RecurrenceCounts[a] == 1 &&
            statistics.RecurrenceSeconds[a] == 90 && statistics.DailyViewCounts[a]["2026-09-16"] == 1,
            "Statistics tracks recurrence and local-day rollover");
        statistics.Remove(a.ToLowerInvariant());
        check(!statistics.ViewCounts.ContainsKey(a) && !statistics.LastShown.ContainsKey(a) &&
            !statistics.DailyViewCounts.ContainsKey(a) && !statistics.RecurrenceCounts.ContainsKey(a) &&
            !statistics.RecurrenceSeconds.ContainsKey(a) && saves == 4,
            "Removing a wallpaper clears all its statistics and saves");
        statistics.RecordView(a);
        check(statistics.ViewCounts[a] == 1 && saves == 5,
            "Removed current wallpaper can be counted again");
        statistics.Reset(a);
        statistics.RecordView(a);
        check(liveCounts.Count == 0 && statistics.LastShown.Count == 0 && statistics.DailyViewCounts.Count == 0 &&
            statistics.RecurrenceCounts.Count == 0 && statistics.RecurrenceSeconds.Count == 0 && saves == 6 &&
            statistics.StartedAt == now && statistics.DailyStartedAt == now && statistics.RecurrenceStartedAt == now,
            "Reset clears live data and leaves current wallpaper uncounted");
        statistics.RecordView(b);
        statistics.RecordView(a);
        check(liveCounts[a] == 1 && liveCounts[b] == 1,
            "Real wallpaper changes resume counting after reset");

        var restored = new PersistentStatisticsData
        {
            StartedAt = now.AddDays(-10), DailyTrackingStartedAt = now.AddDays(-5),
            RecurrenceTrackingStartedAt = now, LastCountedWallpaperPath = a,
            Wallpapers = new()
            {
                new() { Path = a, Views = 7, LastShown = now.AddDays(-1),
                    DailyViews = new() { ["2026-09-15"] = 3, ["bad"] = 0 },
                    RecurrenceCount = 2, TotalRecurrenceSeconds = 120 },
                new() { Path = b, Views = 0 }, new() { Path = "", Views = 2 }
            }
        };
        statistics.Load(restored);
        statistics.RecordView(a);
        check(liveCounts.Count == 1 && liveCounts[a] == 7 && statistics.DailyViewCounts[a].Count == 1 &&
            statistics.RecurrenceCounts[a] == 2 && statistics.StartedAt == restored.StartedAt,
            "Load restores valid data without recounting current wallpaper");
        statistics.RecordView(b);
        statistics.RecordView(a);
        check(statistics.ViewCounts[a] == 8 && statistics.RecurrenceCounts[a] == 2,
            "Views before recurrence tracking began do not create recurrence intervals");
        check(restored.Wallpapers[0].DailyViews.Count == 2,
            "Loaded daily counters do not mutate the storage input");
    }
}
