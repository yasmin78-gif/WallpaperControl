using System.Globalization;
using System.IO;
using System.Text;
using Ical.Net.DataTypes;
using Ical.Net;

namespace WallpaperControl;

internal static class CalendarFeedLimits
{
    // Desktop preview budgets, not iCalendar format limits. See TESTING.md.
    internal const int MaxResponseBytes = 2 * 1024 * 1024;
    internal const int MaxSources = 16;
    internal const int MaxEvents = 2048;
    internal const int MaxOccurrences = 4096;
    internal const int HorizonDays = 90;
    internal const int MaxDisplayRows = 40;
    internal const int MaxWidgetHeight = 1800;

    internal static async Task<string> ReadResponseAsync(HttpContent content, CancellationToken token)
    {
        if (content.Headers.ContentLength > MaxResponseBytes) throw LimitExceeded();
        using Stream input = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using MemoryStream buffer = new();
        byte[] chunk = new byte[8192];
        while (true)
        {
            int count = await input.ReadAsync(chunk, token).ConfigureAwait(false);
            if (count == 0) break;
            if (buffer.Length + count > MaxResponseBytes) throw LimitExceeded();
            await buffer.WriteAsync(chunk.AsMemory(0, count), token).ConfigureAwait(false);
        }
        buffer.Position = 0;
        string? charset = content.Headers.ContentType?.CharSet?.Trim('"');
        Encoding encoding = string.IsNullOrWhiteSpace(charset) ? Encoding.UTF8 : Encoding.GetEncoding(charset);
        using StreamReader reader = new(buffer, encoding, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(token).ConfigureAwait(false);
    }

    // Preflight before Calendar.Load: bound nesting, parsed components, folded
    // properties and recurrence work (including EXRULE and VTIMEZONE rules).
    // Clamp uncounted rules before the library can search beyond our horizon.
    internal static string Prepare(string text, DateTime until, CancellationToken token)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaxResponseBytes) throw LimitExceeded();
        List<string> lines = new();
        using StringReader reader = new(text);
        while (reader.ReadLine() is string line)
        {
            token.ThrowIfCancellationRequested();
            if (line.Length > 16384 || lines.Count >= 50000) throw LimitExceeded();
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t') && lines.Count > 0)
            {
                if (lines[^1].Length + line.Length > 16384) throw LimitExceeded();
                lines[^1] += line[1..];
            }
            else lines.Add(line);
        }

        Stack<(string Name, DateTime Start, bool HasTime, List<int> Rules)> components = new();
        int events = 0, totalComponents = 0;
        double totalWork = 0;
        for (int index = 0; index < lines.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            string line = lines[index];
            int colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0) continue;
            string name = line[..colon].Split(';')[0].ToUpperInvariant();
            string value = line[(colon + 1)..];
            if (name == "BEGIN")
            {
                if (++totalComponents > 4096 || components.Count >= 8) throw LimitExceeded();
                if (value.Equals("VEVENT", StringComparison.OrdinalIgnoreCase) && ++events > MaxEvents) throw LimitExceeded();
                components.Push((value, DateTime.MinValue, true, new List<int>()));
            }
            else if (components.Count > 0 && name == "DTSTART")
            {
                var component = components.Pop();
                if (value.Length >= 8 && DateTime.TryParseExact(value[..8], "yyyyMMdd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime start)) component.Start = start;
                component.HasTime = value.Length > 8;
                components.Push(component);
            }
            else if (components.Count > 0 && name is "RRULE" or "EXRULE")
            {
                var component = components.Peek();
                if (component.Rules.Count >= 4) throw LimitExceeded();
                component.Rules.Add(index);
            }
            else if (components.Count > 0 && name == "END")
            {
                var component = components.Pop();
                if (!component.Name.Equals(value, StringComparison.OrdinalIgnoreCase)) throw LimitExceeded();
                foreach (int ruleIndex in component.Rules)
                {
                    string ruleLine = lines[ruleIndex];
                    int ruleColon = ruleLine.IndexOf(':', StringComparison.Ordinal);
                    RecurrenceRule rule = new(ruleLine[(ruleColon + 1)..]);
                    if (rule.Interval < 1 || rule.Count is <= 0 or > 10000) throw LimitExceeded();
                    double periodDays = rule.Frequency switch
                    {
                        FrequencyType.Secondly => 1d / 86400,
                        FrequencyType.Minutely => 1d / 1440,
                        FrequencyType.Hourly => 1d / 24,
                        FrequencyType.Daily => 1,
                        FrequencyType.Weekly => 7,
                        FrequencyType.Monthly => 28,
                        _ => 365
                    };
                    double combinations = 1;
                    foreach (int count in new[] { rule.BySecond.Count, rule.ByMinute.Count, rule.ByHour.Count,
                        rule.ByDay.Count, rule.ByMonthDay.Count, rule.ByYearDay.Count, rule.ByWeekNo.Count, rule.ByMonth.Count })
                        combinations *= Math.Max(1, count);
                    if (combinations > 4096) throw LimitExceeded();
                    DateTime end = rule.Until?.Value < until ? rule.Until.Value : until;
                    double increments = Math.Max(1, (end - component.Start).TotalDays / periodDays / rule.Interval + 2);
                    // COUNT does not bound unmatched search; EvaluationOptions also caps that.
                    if (rule.Count is int countLimit) increments = Math.Min(increments, countLimit * 129d);
                    // A yearly/monthly/weekly interval can expand implicit days
                    // even when no corresponding BY-list contains those dates.
                    int daysPerPeriod = rule.Frequency switch
                    {
                        FrequencyType.Yearly => 366,
                        FrequencyType.Monthly => 31,
                        FrequencyType.Weekly => 7,
                        _ => 1
                    };
                    double work = increments * combinations * daysPerPeriod;
                    if (work > 250000 || (totalWork += work) > 2000000) throw LimitExceeded();
                    if (rule.Count == null && (rule.Until == null || rule.Until.Value > until))
                    {
                        string boundedRule = string.Join(";", ruleLine[(ruleColon + 1)..].Split(';')
                            .Where(part => !part.StartsWith("UNTIL=", StringComparison.OrdinalIgnoreCase)));
                        lines[ruleIndex] = ruleLine[..(ruleColon + 1)] + boundedRule + ";UNTIL=" +
                            until.AddDays(1).ToString(component.HasTime ? "yyyyMMdd'T'HHmmss'Z'" : "yyyyMMdd", CultureInfo.InvariantCulture);
                    }
                }
            }
        }
        if (components.Count != 0) throw LimitExceeded();
        return string.Join("\r\n", lines);
    }

    private static InvalidDataException LimitExceeded() => new("Calendar feed exceeds the widget processing limits.");
}
