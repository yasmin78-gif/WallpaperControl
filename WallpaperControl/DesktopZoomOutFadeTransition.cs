namespace WallpaperControl
{
    internal sealed class DesktopZoomOutFadeTransition : IWallpaperTransition
    {
        public Task ApplyAsync(
            string? currentWallpaperPath,
            string nextWallpaperPath,
            int durationMilliseconds,
            WallpaperTransitionDirection direction,
            WallpaperZoomMode zoomMode,
            CancellationToken cancellationToken = default)
        {
            return PersistentDesktopTransitionManager.ApplyAsync(
                currentWallpaperPath,
                nextWallpaperPath,
                WallpaperTransitionKind.DesktopZoomOutFade,
                durationMilliseconds,
                direction,
                zoomMode,
                cancellationToken);
        }
    }
}
