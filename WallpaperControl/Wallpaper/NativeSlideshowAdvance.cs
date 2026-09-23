using System.Runtime.InteropServices;

namespace WallpaperControl;

// Windows owns the automatic timer in native fallback mode. We cannot lock it;
// only acknowledge a concurrent change if the same monitors actually changed image.
internal sealed class NativeSlideshowAdvance
{
    internal const int Unexpected = unchecked((int)0x8000FFFF);
    internal bool IsRunning { get; private set; }

    internal async Task<bool> TryAdvanceAsync(Action advance,
        Func<IReadOnlyDictionary<string, string>?> readImages, Func<Task> settle)
    {
        if (IsRunning)
        {
            AppLogger.Info("Native slideshow: duplicate manual request skipped");
            return false;
        }
        IsRunning = true;
        try
        {
            var before = readImages();
            try
            {
                advance();
                AppLogger.Info("Native slideshow: AdvanceSlideshow succeeded");
                return true;
            }
            catch (COMException ex) when (ex.HResult == Unexpected)
            {
                AppLogger.Info("Native slideshow: AdvanceSlideshow returned 0x8000FFFF; checking concurrent image change; retry=false");
                await settle();
                var after = readImages();
                if (ChangedImages(before, after))
                {
                    AppLogger.Info("Native slideshow: concurrent image change confirmed; request satisfied without retry");
                    return true;
                }
                AppLogger.Info("Native slideshow: no concurrent image change confirmed; preserving original failure");
                throw;
            }
        }
        finally { IsRunning = false; }
    }

    private static bool ChangedImages(IReadOnlyDictionary<string, string>? before, IReadOnlyDictionary<string, string>? after)
    {
        if (before == null || after == null || before.Count == 0 || before.Count != after.Count) return false;
        bool changed = false;
        foreach (var image in before)
        {
            if (string.IsNullOrWhiteSpace(image.Value) || !after.TryGetValue(image.Key, out string? next)
                || string.IsNullOrWhiteSpace(next)) return false;
            changed |= !string.Equals(image.Value, next, StringComparison.OrdinalIgnoreCase);
        }
        return changed;
    }

    internal static IReadOnlyDictionary<string, string>? ReadImages(IDesktopWallpaper wallpaper)
    {
        try
        {
            Dictionary<string, string> images = new(StringComparer.OrdinalIgnoreCase);
            uint count = wallpaper.GetMonitorDevicePathCount();
            for (uint i = 0; i < count; i++)
            {
                string monitor = wallpaper.GetMonitorDevicePathAt(i);
                images.Add(monitor, wallpaper.GetWallpaper(monitor));
            }
            return images;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            AppLogger.Info($"Native slideshow: image snapshot unavailable; exceptionType={ex.GetType().Name}; hresult=0x{ex.HResult:X8}");
            return null;
        }
    }
}
