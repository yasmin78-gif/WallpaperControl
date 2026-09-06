namespace WallpaperControl
{
    internal interface IWallpaperTransition
    {
        Task ApplyAsync(
            string? currentWallpaperPath,
            string nextWallpaperPath,
            CancellationToken cancellationToken = default);
    }
}
