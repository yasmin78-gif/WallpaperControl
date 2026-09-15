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

        public static PersistentStatisticsData Load() => Load(StatisticsFilePath);

        internal static PersistentStatisticsData Load(string path)
        {
            foreach (string candidate in new[] { path, path + ".bak" })
            {
                if (!File.Exists(candidate)) continue;
                try { return ReadData(candidate); }
                catch (Exception ex)
                {
                    AppLogger.Warning("Could not load persistent statistics: " + candidate, ex);
                    if (ex is JsonException)
                    {
                        try { PreserveDamagedFile(candidate); }
                        catch (Exception backupError)
                        {
                            AppLogger.Warning("Could not preserve damaged statistics.", backupError);
                        }
                    }
                }
            }
            return CreateEmpty();
        }

        private static PersistentStatisticsData ReadData(string path)
        {
            var data = JsonSerializer.Deserialize<PersistentStatisticsData>(
                File.ReadAllText(path), JsonOptions)
                ?? throw new JsonException("Statistics contain null instead of an object.");
            if (data.StartedAt == default) data.StartedAt = DateTime.Now;
            if (data.DailyTrackingStartedAt == default) data.DailyTrackingStartedAt = DateTime.Now;
            if (data.RecurrenceTrackingStartedAt == default) data.RecurrenceTrackingStartedAt = DateTime.Now;
            data.Wallpapers ??= new();
            foreach (var wallpaper in data.Wallpapers)
            {
                if (wallpaper == null) throw new JsonException("Statistics contain a null wallpaper.");
                wallpaper.DailyViews ??= new();
            }
            return data;
        }

        private static void PreserveDamagedFile(string path)
        {
            File.Move(path, path + ".corrupt-" +
                DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff") + "-" + Guid.NewGuid().ToString("N"));
        }

        internal static void SaveData(string path, PersistentStatisticsData data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            string tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                // Flush the complete new document before replacing the current file.
                using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, data, JsonOptions);
                    stream.Flush(true);
                }
                if (File.Exists(path))
                {
                    try { ReadData(path); }
                    catch (JsonException) { PreserveDamagedFile(path); }
                }
                if (File.Exists(path))
                {
                    // Stage the backup as well, so interruption cannot leave a
                    // partially overwritten backup file.
                    string backupTemp = tempPath + ".bak";
                    try
                    {
                        File.Copy(path, backupTemp);
                        File.Move(backupTemp, path + ".bak", true);
                    }
                    finally
                    {
                        if (File.Exists(backupTemp)) File.Delete(backupTemp);
                    }
                    File.Move(tempPath, path, true);
                }
                else
                    File.Move(tempPath, path);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
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

                SaveData(StatisticsFilePath, data);
            }
            catch (Exception ex)
            {
                // Statistics are useful, but they must never be able
                // to break wallpaper switching or application exit.
                AppLogger.Warning("Could not save persistent statistics.", ex);
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
