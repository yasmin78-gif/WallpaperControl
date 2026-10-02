using System.Globalization;

namespace WallpaperControl;

internal static class TwitchRowPresentation
{
    internal const int RowHeight = 58;
    internal const int AvatarSize = 46;
    internal static string Viewers(int viewers, string language)
    {
        var culture = CultureInfo.GetCultureInfo(language); int count = Math.Max(0, viewers);
        if (count < 1000) return count.ToString(culture);
        decimal thousands = Math.Round(count / 1000m, 1, MidpointRounding.AwayFromZero);
        return thousands.ToString("0.#", culture) + "k";
    }
    internal static string Duration(DateTimeOffset startedAt, DateTimeOffset now)
    {
        long seconds = (long)Math.Max(0, (now - startedAt).TotalSeconds);
        return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", seconds / 3600, seconds / 60 % 60, seconds % 60);
    }
}
