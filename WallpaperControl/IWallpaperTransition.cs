namespace WallpaperControl
{
    internal interface IWallpaperTransition
    {
        Task ApplyAsync(
            string? currentWallpaperPath,
            string nextWallpaperPath,
            int durationMilliseconds,
            CancellationToken cancellationToken = default);
    }
}
