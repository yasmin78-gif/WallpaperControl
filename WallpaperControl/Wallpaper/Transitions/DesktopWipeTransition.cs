namespace WallpaperControl
{
    internal sealed class DesktopWipeTransition : IWallpaperTransition
    {
        /// <summary>
        /// Applies the wipe wallpaper transition using the supplied animation settings.
        /// </summary>
        /// <param name="currentWallpaperPath">The currently displayed wallpaper path, when known.</param>
        /// <param name="nextWallpaperPath">The image path to display next.</param>
        /// <param name="durationMilliseconds">The requested transition duration in milliseconds.</param>
        /// <param name="direction">The requested direction of the animated wallpaper transition.</param>
        /// <param name="zoomMode">The requested direction of the zoom effect.</param>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        /// <returns>A task representing completion of the asynchronous operation.</returns>
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
                direction,
                zoomMode,
                cancellationToken);
        }
    }
}
