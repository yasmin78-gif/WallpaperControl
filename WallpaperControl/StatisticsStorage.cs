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
        public string? LastCountedWallpaperPath { get; set; }
        public List<PersistentWallpaperStatistics> Wallpapers { get; set; } = new();
    }

    internal sealed class PersistentWallpaperStatistics
    {
        public string Path { get; set; } = "";
        public int Views { get; set; }
        public DateTime LastShown { get; set; }
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

                data.Wallpapers ??= new();

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
            IReadOnlyDictionary<string, int> viewCounts,
            IReadOnlyDictionary<string, DateTime> lastShown,
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
                                            LastShown = shown
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
                StartedAt = DateTime.Now
            };
        }
    }
}
