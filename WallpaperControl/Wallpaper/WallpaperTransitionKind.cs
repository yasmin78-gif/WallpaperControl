namespace WallpaperControl
{
    /// <summary>
    /// Identifies direct wallpaper changes and animated desktop effects; settings also support legacy selector indices.
    /// </summary>
    internal enum WallpaperTransitionKind
    {
        Direct,
        DesktopWipe,
        DesktopSlide,
        DesktopFade,
        DesktopZoomFade,
        DesktopSplit,
        DesktopCurtain,
        DesktopRandom
    }
}
