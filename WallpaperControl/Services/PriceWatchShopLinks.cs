using System.ComponentModel;
using System.Diagnostics;

namespace WallpaperControl;

internal static class PriceWatchShopLinks
{
    internal static Uri? Validate(Uri? url) =>
        url is { IsAbsoluteUri: true, Scheme: "http" or "https", Host.Length: > 0, UserInfo.Length: 0 }
        && url.OriginalString.StartsWith(url.Scheme + "://", StringComparison.OrdinalIgnoreCase)
        && !url.OriginalString.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))
        ? url : null;

    internal static void Open(Uri url)
    {
        if (Validate(url) == null) return;
        try { using var process = Process.Start(new ProcessStartInfo(url.OriginalString) { UseShellExecute = true }); }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            AppLogger.Info("PriceWatch could not open the default browser.");
        }
    }
}
