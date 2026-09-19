using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WallpaperControl
{
    internal static class CalendarSourceName
    {
        private static readonly char[] PropertySeparators = { ':', ';' };
        /// <summary>
        /// Reads optional top-level X-WR-CALNAME (a generic property in Ical.Net) or RFC 7986 NAME.
        /// Remove these optional lines before Ical.Net parsing so broken name metadata cannot
        /// invalidate otherwise usable events. Input has already passed the bounded download.
        /// </summary>
        internal static string Extract(ref string text)
        {
            List<string> lines = new();
            using StringReader reader = new(text);
            while (reader.ReadLine() is { } line)
            {
                if ((line.StartsWith(' ') || line.StartsWith('\t')) && lines.Count > 0)
                    lines[^1] += line[1..];
                else lines.Add(line);
            }
            StringBuilder remaining = new();
            int depth = 0;
            string name = string.Empty;
            foreach (string line in lines)
            {
                if (line.StartsWith("BEGIN:", StringComparison.OrdinalIgnoreCase)) depth++;
                string property = line.Split(PropertySeparators, 2)[0];
                if (depth == 1 && (property.Equals("X-WR-CALNAME", StringComparison.OrdinalIgnoreCase)
                    || property.Equals("NAME", StringComparison.OrdinalIgnoreCase)))
                {
                    int colon = line.IndexOf(':', StringComparison.Ordinal);
                    string candidate = colon < 0 ? "" : line[(colon + 1)..].Trim()
                        .Replace("\\,", ",", StringComparison.Ordinal).Replace("\\;", ";", StringComparison.Ordinal);
                    if (name.Length == 0 && candidate.Length is > 0 and <= 160
                        && !candidate.Contains("://", StringComparison.Ordinal)
                        && !candidate.Contains('\\', StringComparison.Ordinal) && !Array.Exists(candidate.ToCharArray(), char.IsControl)) name = candidate;
                }
                else remaining.AppendLine(line);
                if (line.StartsWith("END:", StringComparison.OrdinalIgnoreCase)) depth--;
            }
            text = remaining.ToString();
            return name;
        }
    }
}
