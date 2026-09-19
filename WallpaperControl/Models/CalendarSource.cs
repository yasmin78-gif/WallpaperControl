using System;
using System.Drawing;
using System.Globalization;
using System.Text.Json.Serialization;

namespace WallpaperControl
{
    internal enum CalendarSourceType { Calendar, Holiday }

    /// <summary>An immutable settings snapshot. The private address is never used as display identity.</summary>
    internal sealed record CalendarSource
    {
        [JsonRequired] public Guid Id { get; init; } = Guid.NewGuid();
        public string Name { get; init; } = string.Empty;
        [JsonRequired] public string Url { get; init; } = string.Empty;
        public CalendarSourceType Type { get; init; }
        public bool Enabled { get; init; } = true;
        public int ColorArgb { get; init; } = DefaultColor(0);
        public int FallbackNumber { get; init; } = 1;
        [JsonIgnore] public bool IsHoliday => Type == CalendarSourceType.Holiday;

        public string DisplayName(string language) => string.IsNullOrWhiteSpace(Name)
            ? string.Format(CultureInfo.GetCultureInfo(language), Localization.Get(
                IsHoliday ? "CalendarSourceHolidayFallback" : "CalendarSourceFallback", language), FallbackNumber)
            : Name;

        public static int DefaultColor(int index)
        {
            int[] colors = { 0x79CFFF, 0xFFD580, 0xA6E3A1, 0xE5ADFF, 0xFFABA8, 0x8BE3D5 };
            return unchecked((int)0xFF000000) | colors[Math.Abs(index % colors.Length)];
        }

        public static int ValidateColor(int argb, int index = 0) => Color.FromArgb(argb).A == 255
            ? argb : DefaultColor(index);

        public static bool IsValidUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && !string.IsNullOrEmpty(uri.Host) && !value.Contains('\r', StringComparison.Ordinal) && !value.Contains('\n', StringComparison.Ordinal);

        // Records otherwise generate a ToString containing every property, including secrets.
        public override string ToString() => $"CalendarSource {Id}";
    }
}
