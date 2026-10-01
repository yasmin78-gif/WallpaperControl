namespace WallpaperControl;

internal enum WallpaperOperatingMode { ImageSlideshow, VideoWallpaper }

// Shared by every wallpaper producer. A lease captured before an await or dispatch
// cannot be used after either mode changes, even if the user switches back quickly.
internal sealed class WallpaperModeOwnership
{
    internal WallpaperOperatingMode Mode { get; private set; }
    internal int Generation { get; private set; }
    internal bool Closed { get; private set; }
    internal bool AllowsImages => !Closed && Mode == WallpaperOperatingMode.ImageSlideshow;
    internal bool IsCurrentImage(int lease) => AllowsImages && Generation == lease;
    internal void Switch(WallpaperOperatingMode mode) { Generation++; Mode = mode; }
    internal void Close() { Closed = true; Generation++; }
}
