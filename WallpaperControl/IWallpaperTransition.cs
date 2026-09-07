namespace WallpaperControl
{
    internal interface IWallpaperTransition
    {
        Task ApplyAsync(
            string? currentWallpaperPath,
            string nextWallpaperPath,
            int durationMilliseconds,
            WallpaperTransitionDirection direction,
            WallpaperZoomMode zoomMode,
            CancellationToken cancellationToken = default);
    }
}
