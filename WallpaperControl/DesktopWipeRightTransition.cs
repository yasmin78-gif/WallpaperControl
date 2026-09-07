namespace WallpaperControl
{
    internal sealed class DesktopWipeRightTransition : IWallpaperTransition
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
                WallpaperTransitionKind.DesktopWipeRight,
                durationMilliseconds,
                cancellationToken);
        }
    }
}
