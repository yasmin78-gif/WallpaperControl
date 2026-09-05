using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace WallpaperControl
{
    internal sealed class PersistentStatisticsData
    {
        public DateTime StartedAt { get; set; }
        public DateTime DailyTrackingStartedAt { get; set; }
        public DateTime RecurrenceTrackingStartedAt { get; set; }
        public string? LastCountedWallpaperPath { get; set; }
        public List<PersistentWallpaperStatistics> Wallpapers { get; set; } = new();
    }

    internal sealed class PersistentWallpaperStatistics
    {
        public string Path { get; set; } = "";
        public int Views { get; set; }
        public DateTime LastShown { get; set; }
        public Dictionary<string, int> DailyViews { get; set; } = new();
        public int RecurrenceCount { get; set; }
        public double TotalRecurrenceSeconds { get; set; }
    }

    internal static class StatisticsStorage
    {
        private static readonly JsonSerializerOptions JsonOptions =
            new()
            {
                WriteIndented = true
            };

        private static string StatisticsDirectory =>
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                "WallpaperControl");

        internal static string StatisticsFilePath =>
            Path.Combine(
                StatisticsDirectory,
                "statistics.json");

        public static PersistentStatisticsData Load()
        {
            try
            {
                if (!File.Exists(StatisticsFilePath))
                {
                    return CreateEmpty();
                }

                string json =
                    File.ReadAllText(
                        StatisticsFilePath);

                PersistentStatisticsData? data =
                    JsonSerializer.Deserialize<PersistentStatisticsData>(
                        json,
                        JsonOptions);

                if (data == null)
                {
                    return CreateEmpty();
                }

                if (data.StartedAt == default)
                {
                    data.StartedAt = DateTime.Now;
                }

                if (data.DailyTrackingStartedAt == default)
                {
                    // Existing statistics.json files from before Stage 3
                    // have no historical day buckets. Time-based tracking
                    // therefore starts when this version first loads them.
                    data.DailyTrackingStartedAt = DateTime.Now;
                }

                if (data.RecurrenceTrackingStartedAt == default)
                {
                    // Existing statistics cannot tell us historical
                    // recurrence intervals reliably. Stage 5 therefore
                    // starts measuring them from this first load.
                    data.RecurrenceTrackingStartedAt = DateTime.Now;
                }

                data.Wallpapers ??= new();

                foreach (PersistentWallpaperStatistics wallpaper
                    in data.Wallpapers)
                {
                    wallpaper.DailyViews ??= new();
                }

                return data;
            }
            catch
            {
                // A damaged statistics file must never prevent
                // Wallpaper Control from starting.
                return CreateEmpty();
            }
        }

        public static void Save(
            DateTime startedAt,
            DateTime dailyTrackingStartedAt,
            IReadOnlyDictionary<string, int> viewCounts,
            IReadOnlyDictionary<string, DateTime> lastShown,
            IReadOnlyDictionary<string, Dictionary<string, int>> dailyViews,
            IReadOnlyDictionary<string, int> recurrenceCounts,
            IReadOnlyDictionary<string, double> recurrenceSeconds,
            DateTime recurrenceTrackingStartedAt,
            string? lastCountedWallpaperPath)
        {
            try
            {
                Directory.CreateDirectory(
                    StatisticsDirectory);

                PersistentStatisticsData data =
                    new()
                    {
                        StartedAt =
                            startedAt == default
                                ? DateTime.Now
                                : startedAt,
                        DailyTrackingStartedAt =
                            dailyTrackingStartedAt == default
                                ? DateTime.Now
                                : dailyTrackingStartedAt,
                        RecurrenceTrackingStartedAt =
                            recurrenceTrackingStartedAt == default
                                ? DateTime.Now
                                : recurrenceTrackingStartedAt,
                        LastCountedWallpaperPath =
                            lastCountedWallpaperPath,
                        Wallpapers =
                            viewCounts
                                .Where(
                                    item =>
                                        !string.IsNullOrWhiteSpace(
                                            item.Key) &&
                                        item.Value > 0)
                                .Select(
                                    item =>
                                    {
                                        lastShown.TryGetValue(
                                            item.Key,
                                            out DateTime shown);

                                        return new PersistentWallpaperStatistics
                                        {
                                            Path = item.Key,
                                            Views = item.Value,
                                            LastShown = shown,
                                            DailyViews =
                                                dailyViews.TryGetValue(
                                                    item.Key,
                                                    out Dictionary<string, int>? days)
                                                    ? days
                                                        .Where(
                                                            entry =>
                                                                !string.IsNullOrWhiteSpace(
                                                                    entry.Key) &&
                                                                entry.Value > 0)
                                                        .OrderBy(
                                                            entry => entry.Key,
                                                            StringComparer.Ordinal)
                                                        .ToDictionary(
                                                            entry => entry.Key,
                                                            entry => entry.Value,
                                                            StringComparer.Ordinal)
                                                    : new Dictionary<string, int>(
                                                        StringComparer.Ordinal),
                                            RecurrenceCount =
                                                recurrenceCounts.TryGetValue(
                                                    item.Key,
                                                    out int recurrenceCount)
                                                    ? Math.Max(0, recurrenceCount)
                                                    : 0,
                                            TotalRecurrenceSeconds =
                                                recurrenceSeconds.TryGetValue(
                                                    item.Key,
                                                    out double totalSeconds)
                                                    ? Math.Max(0, totalSeconds)
                                                    : 0
                                        };
                                    })
                                .OrderBy(
                                    item => item.Path,
                                    StringComparer.OrdinalIgnoreCase)
                                .ToList()
                    };

                string json =
                    JsonSerializer.Serialize(
                        data,
                        JsonOptions);

                string tempPath =
                    StatisticsFilePath + ".tmp";

                File.WriteAllText(
                    tempPath,
                    json);

                File.Move(
                    tempPath,
                    StatisticsFilePath,
                    true);
            }
            catch
            {
                // Statistics are useful, but they must never be able
                // to break wallpaper switching or application exit.
            }
        }

        private static PersistentStatisticsData CreateEmpty()
        {
            return new PersistentStatisticsData
            {
                StartedAt = DateTime.Now,
                DailyTrackingStartedAt = DateTime.Now,
                RecurrenceTrackingStartedAt = DateTime.Now
            };
        }
    }
}
