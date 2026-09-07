namespace WallpaperControl
{
    internal sealed class DesktopSlideRandomTransition : IWallpaperTransition
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
                WallpaperTransitionKind.DesktopSlide,
                durationMilliseconds,
                WallpaperTransitionDirection.Random,
                zoomMode,
                cancellationToken);
        }
    }
}
