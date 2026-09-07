namespace WallpaperControl
{
    internal sealed class DesktopCurtainTransition : IWallpaperTransition
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
                WallpaperTransitionKind.DesktopCurtain,
                durationMilliseconds,
                direction,
                zoomMode,
                cancellationToken);
        }
    }
}
