namespace WallpaperControl
{
    internal sealed class DesktopWipeRightTransition : IWallpaperTransition
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
                WallpaperTransitionKind.DesktopWipe,
                durationMilliseconds,
                WallpaperTransitionDirection.Right,
                zoomMode,
                cancellationToken);
        }
    }
}
