namespace WallpaperControl
{
    internal sealed class DesktopZoomFadeTransition : IWallpaperTransition
    {
        public Task ApplyAsync(
            string? currentWallpaperPath,
            string nextWallpaperPath,
            int durationMilliseconds,
            CancellationToken cancellationToken = default)
        {
            return PersistentDesktopTransitionManager.ApplyAsync(
                currentWallpaperPath,
                nextWallpaperPath,
                WallpaperTransitionKind.DesktopZoomFade,
                durationMilliseconds,
                cancellationToken);
        }
    }
}
