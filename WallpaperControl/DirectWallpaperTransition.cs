using System.Runtime.InteropServices;

namespace WallpaperControl
{
    internal sealed class DirectWallpaperTransition : IWallpaperTransition
    {
        public Task ApplyAsync(
            string? currentWallpaperPath,
            string nextWallpaperPath,
            int durationMilliseconds,
            WallpaperTransitionDirection direction,
            WallpaperZoomMode zoomMode,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IDesktopWallpaper? wallpaper = null;

            try
            {
                wallpaper =
                    (IDesktopWallpaper)
                    new DesktopWallpaper();

                wallpaper.SetWallpaper(
                    null,
                    nextWallpaperPath);
            }
            finally
            {
                if (wallpaper != null &&
                    Marshal.IsComObject(wallpaper))
                {
                    Marshal.FinalReleaseComObject(wallpaper);
                }
            }

            return Task.CompletedTask;
        }
    }
}
