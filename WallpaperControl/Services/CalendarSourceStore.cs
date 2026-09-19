using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using System.Security;

namespace WallpaperControl
{
    /// <summary>Stores the complete versioned source list inside one user-bound DPAPI envelope.</summary>
    internal static class CalendarSourceStore
    {
        internal const string ValueName = "CalendarWidgetSourcesProtectedV1";
        private static readonly char[] LineSeparators = { '\r', '\n' };
        private sealed class Envelope
        {
            [JsonRequired] public int Version { get; set; } = 1;
            [JsonRequired] public List<CalendarSource> Sources { get; set; } = new();
        }

        internal static List<CalendarSource> Migrate(string normal, string holiday)
        {
            List<CalendarSource> result = new();
            foreach ((string text, CalendarSourceType type) in new[]
            {
                (normal, CalendarSourceType.Calendar), (holiday, CalendarSourceType.Holiday)
            })
            {
                int number = 0;
                // Preserve every nonempty legacy entry, including duplicates and currently invalid URLs.
                // Validation applies to new edits; migration must never silently discard old data.
                foreach (string url in text.Split(LineSeparators, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    result.Add(new CalendarSource { Url = url, Type = type, FallbackNumber = ++number,
                        ColorArgb = CalendarSource.DefaultColor(result.Count) });
            }
            return result;
        }

        internal static List<CalendarSource> Load(RegistryKey key, string registryPath)
        {
            string saved = key.GetValue(ValueName) as string ?? string.Empty;
            if (TryDecode(saved, out List<CalendarSource> sources)) return sources;
            string recovery = key.GetValue(ValueName + "Recovery") as string ?? string.Empty;
            if (saved.Length > 0 && TryDecode(recovery, out sources)) return sources;
            // Keep both the legacy ciphertext and any unreadable new envelope for recovery.
            string normal = key.GetValue("CalendarWidgetIcsUrlProtected") as string ?? string.Empty;
            string holiday = key.GetValue("CalendarWidgetHolidayIcsUrlProtected") as string ?? string.Empty;
            string plainNormal = WindowsSecretProtector.Unprotect(normal);
            string plainHoliday = WindowsSecretProtector.Unprotect(holiday);
            sources = Migrate(plainNormal, plainHoliday);
            bool fullyReadable = (normal.Length == 0 || plainNormal.Length > 0)
                && (holiday.Length == 0 || plainHoliday.Length > 0);
            if (saved.Length == 0 && fullyReadable && sources.Count > 0)
            {
                try
                {
                    using RegistryKey? writable = Registry.CurrentUser.OpenSubKey(registryPath, writable: true);
                    if (writable != null) Save(writable, sources);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or InvalidOperationException)
                { LogFailure(ex); }
            }
            return sources;
        }

        internal static bool TryDecode(string protectedValue, out List<CalendarSource> sources)
        {
            sources = new();
            try
            {
                string json = WindowsSecretProtector.Unprotect(protectedValue);
                if (json.Length == 0) return false;
                Envelope? envelope = JsonSerializer.Deserialize<Envelope>(json);
                if (envelope?.Version != 1 || envelope.Sources == null) return false;
                HashSet<Guid> ids = new();
                foreach (CalendarSource source in envelope.Sources)
                {
                    if (source == null || source.Id == Guid.Empty || !ids.Add(source.Id)
                        || source.Url == null || source.Name == null || !Enum.IsDefined(source.Type)) return false;
                    sources.Add(source with { ColorArgb = CalendarSource.ValidateColor(source.ColorArgb, sources.Count),
                        FallbackNumber = Math.Max(1, source.FallbackNumber) });
                }
                return true;
            }
            catch (JsonException) { sources.Clear(); return false; }
        }

        internal static void Save(RegistryKey key, IEnumerable<CalendarSource> sources,
            Func<string, string>? protect = null)
        {
            string json = JsonSerializer.Serialize(new Envelope { Sources = sources.ToList() });
            string encrypted = (protect ?? WindowsSecretProtector.Protect)(json);
            if (!TryDecode(encrypted, out _)) throw new InvalidOperationException("Calendar source encryption verification failed.");
            object? previous = key.GetValue(ValueName);
            if (previous is string prior && prior.Length > 0)
                key.SetValue(ValueName + (TryDecode(prior, out _) ? "Recovery" : "Unreadable"), prior, RegistryValueKind.String);
            try
            {
                key.SetValue(ValueName, encrypted, RegistryValueKind.String);
                if (!string.Equals(key.GetValue(ValueName) as string, encrypted, StringComparison.Ordinal))
                    throw new InvalidOperationException("Calendar source persistence verification failed.");
            }
            catch
            {
                if (previous != null) key.SetValue(ValueName, previous);
                else key.DeleteValue(ValueName, throwOnMissingValue: false);
                throw;
            }
            // Deliberately never delete/overwrite the old encrypted multiline values.
            // A successful verified write makes migration idempotent, including an empty saved list.
        }

        private static void LogFailure(Exception ex) => AppLogger.Warning(
            "Calendar source migration could not be saved.", new InvalidOperationException(ex.GetType().Name));
    }
}
