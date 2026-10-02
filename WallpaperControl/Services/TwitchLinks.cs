using System.Diagnostics;

namespace WallpaperControl;

internal static class TwitchLinks
{
    internal static Uri? Channel(string? login) => login is { Length: >= 1 and <= 25 }
        && login.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? new Uri("https://www.twitch.tv/" + login) : null;
    internal static bool Open(Uri uri)
    {
        if (uri.Scheme != "https" || uri.Host != "www.twitch.tv" || !string.IsNullOrEmpty(uri.UserInfo)) return false;
        try { using var process = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); return true; }
        catch { AppLogger.Info("Twitch browser launch failed."); return false; }
    }
}
