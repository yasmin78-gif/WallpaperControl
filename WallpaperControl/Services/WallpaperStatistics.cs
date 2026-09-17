using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WallpaperControl
{
    // Owned by the UI thread. The dictionaries remain stable while the statistics
    // dialog is open so its existing read-only views keep seeing updates.
    internal sealed class WallpaperStatistics
    {
        private readonly Func<DateTime> clock;
        private readonly Action? saveOverride;

        /// <summary>
        /// Initializes in-memory tracking with optional clock and persistence substitutes for testing.
        /// </summary>
        /// <param name="clock">An optional clock used to make tracking timestamps deterministic.</param>
        /// <param name="saveOverride">An optional persistence callback used by tests.</param>
        internal WallpaperStatistics(Func<DateTime>? clock = null, Action? saveOverride = null)
        {
            this.clock = clock ?? (() => DateTime.Now);
            this.saveOverride = saveOverride;
        }

        internal IReadOnlyDictionary<string, int> ViewCounts => wallpaperViewCounts;
        internal IReadOnlyDictionary<string, DateTime> LastShown => wallpaperLastShown;
        internal IReadOnlyDictionary<string, Dictionary<string, int>> DailyViewCounts => wallpaperDailyViewCounts;
        internal IReadOnlyDictionary<string, int> RecurrenceCounts => wallpaperRecurrenceCounts;
        internal IReadOnlyDictionary<string, double> RecurrenceSeconds => wallpaperRecurrenceSeconds;
        internal DateTime StartedAt => statisticsStartedAt;
        internal DateTime DailyStartedAt => dailyStatisticsStartedAt;
        internal DateTime RecurrenceStartedAt => recurrenceStatisticsStartedAt;
        private readonly Dictionary<string, int>
            wallpaperViewCounts =
                new(StringComparer.OrdinalIgnoreCase);

        private DateTime statisticsStartedAt =
            DateTime.Now;

        private readonly Dictionary<string, DateTime>
            wallpaperLastShown =
                new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, Dictionary<string, int>>
            wallpaperDailyViewCounts =
                new(StringComparer.OrdinalIgnoreCase);

        private DateTime dailyStatisticsStartedAt =
            DateTime.Now;

        private readonly Dictionary<string, int>
            wallpaperRecurrenceCounts =
                new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, double>
            wallpaperRecurrenceSeconds =
                new(StringComparer.OrdinalIgnoreCase);

        private DateTime recurrenceStatisticsStartedAt =
            DateTime.Now;

        private string? lastCountedWallpaperPath;

        /// <summary>
        /// Restores counters and tracking timestamps from supplied or persisted statistics.
        /// </summary>
        /// <param name="loadedData">An optional snapshot to load instead of reading persistent storage.</param>
        internal void Load(PersistentStatisticsData? loadedData = null)
        {
            PersistentStatisticsData data =
                loadedData ?? StatisticsStorage.Load();

            statisticsStartedAt =
                data.StartedAt == default
                    ? clock()
                    : data.StartedAt;

            lastCountedWallpaperPath =
                string.IsNullOrWhiteSpace(
                    data.LastCountedWallpaperPath)
                    ? null
                    : data.LastCountedWallpaperPath;

            dailyStatisticsStartedAt =
                data.DailyTrackingStartedAt == default
                    ? clock()
                    : data.DailyTrackingStartedAt;

            recurrenceStatisticsStartedAt =
                data.RecurrenceTrackingStartedAt == default
                    ? clock()
                    : data.RecurrenceTrackingStartedAt;

            wallpaperViewCounts.Clear();
            wallpaperLastShown.Clear();
            wallpaperDailyViewCounts.Clear();
            wallpaperRecurrenceCounts.Clear();
            wallpaperRecurrenceSeconds.Clear();

            foreach (PersistentWallpaperStatistics item
                in data.Wallpapers)
            {
                if (string.IsNullOrWhiteSpace(item.Path) ||
                    item.Views <= 0)
                {
                    continue;
                }

                wallpaperViewCounts[item.Path] =
                    item.Views;

                if (item.LastShown != default)
                {
                    wallpaperLastShown[item.Path] =
                        item.LastShown;
                }

                Dictionary<string, int> validDailyCounts =
                    item.DailyViews
                        .Where(
                            entry =>
                                !string.IsNullOrWhiteSpace(
                                    entry.Key) &&
                                entry.Value > 0)
                        .ToDictionary(
                            entry => entry.Key,
                            entry => entry.Value,
                            StringComparer.Ordinal);

                if (validDailyCounts.Count > 0)
                {
                    wallpaperDailyViewCounts[item.Path] =
                        validDailyCounts;
                }

                if (item.RecurrenceCount > 0 &&
                    item.TotalRecurrenceSeconds > 0)
                {
                    wallpaperRecurrenceCounts[item.Path] =
                        item.RecurrenceCount;

                    wallpaperRecurrenceSeconds[item.Path] =
                        item.TotalRecurrenceSeconds;
                }
            }
        }

        /// <summary>
        /// Persists the current counters and duplicate-suppression state.
        /// </summary>
        internal void Save()
        {
            if (saveOverride != null)
            {
                saveOverride();
                return;
            }

            StatisticsStorage.Save(
                statisticsStartedAt,
                dailyStatisticsStartedAt,
                wallpaperViewCounts,
                wallpaperLastShown,
                wallpaperDailyViewCounts,
                wallpaperRecurrenceCounts,
                wallpaperRecurrenceSeconds,
                recurrenceStatisticsStartedAt,
                lastCountedWallpaperPath);
        }

        /// <summary>
        /// Removes a wallpaper from all counters and persists the updated tracking state.
        /// </summary>
        /// <param name="path">The image or folder path to process.</param>
        internal void Remove(
            string path)
        {
            wallpaperViewCounts.Remove(path);
            wallpaperLastShown.Remove(path);
            wallpaperDailyViewCounts.Remove(path);
            wallpaperRecurrenceCounts.Remove(path);
            wallpaperRecurrenceSeconds.Remove(path);

            if (string.Equals(
                path,
                lastCountedWallpaperPath,
                StringComparison.OrdinalIgnoreCase))
            {
                lastCountedWallpaperPath = null;
            }

            Save();
        }

        /// <summary>
        /// Clears tracking data while retaining the current image as already displayed until a real change occurs.
        /// </summary>
        /// <param name="currentWallpaperPath">The currently displayed wallpaper path, when known.</param>
        internal void Reset(string? currentWallpaperPath)
        {
            wallpaperViewCounts.Clear();
            wallpaperLastShown.Clear();
            wallpaperDailyViewCounts.Clear();
            wallpaperRecurrenceCounts.Clear();
            wallpaperRecurrenceSeconds.Clear();

            statisticsStartedAt =
                clock();

            dailyStatisticsStartedAt =
                statisticsStartedAt;

            recurrenceStatisticsStartedAt =
                statisticsStartedAt;

            // Reset must leave the displayed count at zero.
            // Treat the currently visible wallpaper as already present and count
            // it again only after an actual wallpaper change.
            lastCountedWallpaperPath =
                currentWallpaperPath;

            Save();
        }

        /// <summary>
        /// Records a genuine image change, updating daily and recurrence statistics while suppressing consecutive duplicates.
        /// </summary>
        /// <param name="path">The image or folder path to process.</param>
        internal void RecordView(
            string path)
        {
            if (string.Equals(
                path,
                lastCountedWallpaperPath,
                StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            DateTime now =
                clock();

            if (wallpaperLastShown.TryGetValue(
                    path,
                    out DateTime previousShown) &&
                previousShown >= recurrenceStatisticsStartedAt &&
                now > previousShown)
            {
                double recurrenceSeconds =
                    (now - previousShown).TotalSeconds;

                if (wallpaperRecurrenceCounts.TryGetValue(
                    path,
                    out int recurrenceCount))
                {
                    wallpaperRecurrenceCounts[path] =
                        recurrenceCount + 1;
                }
                else
                {
                    wallpaperRecurrenceCounts[path] = 1;
                }

                if (wallpaperRecurrenceSeconds.TryGetValue(
                    path,
                    out double totalSeconds))
                {
                    wallpaperRecurrenceSeconds[path] =
                        totalSeconds + recurrenceSeconds;
                }
                else
                {
                    wallpaperRecurrenceSeconds[path] =
                        recurrenceSeconds;
                }
            }

            lastCountedWallpaperPath =
                path;

            wallpaperLastShown[path] =
                now;

            if (wallpaperViewCounts.TryGetValue(
                path,
                out int count))
            {
                wallpaperViewCounts[path] =
                    count + 1;
            }
            else
            {
                wallpaperViewCounts[path] = 1;
            }

            string todayKey =
                now.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture);

            if (!wallpaperDailyViewCounts.TryGetValue(
                path,
                out Dictionary<string, int>? dailyCounts))
            {
                dailyCounts =
                    new Dictionary<string, int>(
                        StringComparer.Ordinal);

                wallpaperDailyViewCounts[path] =
                    dailyCounts;
            }

            if (dailyCounts.TryGetValue(
                todayKey,
                out int dailyCount))
            {
                dailyCounts[todayKey] =
                    dailyCount + 1;
            }
            else
            {
                dailyCounts[todayKey] = 1;
            }

            Save();
        }

    }
}
