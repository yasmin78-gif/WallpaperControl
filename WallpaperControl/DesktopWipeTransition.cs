using System.IO;

namespace WallpaperControl
{
    internal sealed class DesktopWipeTransition : IWallpaperTransition
    {
        private static PersistentDesktopWallpaperHost? persistentHost;

        public static async Task<bool> InitializeHostAsync(
            string? currentWallpaperPath,
            CancellationToken cancellationToken = default)
        {
            if (persistentHost != null &&
                !persistentHost.IsDisposed)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(currentWallpaperPath) ||
                !File.Exists(currentWallpaperPath))
            {
                return false;
            }

            persistentHost =
                new PersistentDesktopWallpaperHost();

            if (!persistentHost.Initialize(currentWallpaperPath))
            {
                persistentHost.Dispose();
                persistentHost = null;
                return false;
            }

            // Give Explorer/DWM time to finish the one-time desktop/taskbar
            // recomposition caused by inserting the persistent child layer.
            await Task.Delay(1500, cancellationToken);

            // Explorer/DWM may reorder the child once during startup.
            // Put the already-created host back directly below the desktop icons.
            persistentHost.EnsureDesktopPlacement();

            return true;
        }

        public async Task ApplyAsync(
            string? currentWallpaperPath,
            string nextWallpaperPath,
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(nextWallpaperPath))
            {
                return;
            }

            if (!await InitializeHostAsync(
                    currentWallpaperPath,
                    cancellationToken))
            {
                return;
            }

            persistentHost!.EnsureDesktopPlacement();

            await persistentHost.WipeToAsync(
                nextWallpaperPath,
                2000,
                cancellationToken);

            persistentHost.CommitCurrentPath(
                nextWallpaperPath);

            // Deliberately NO IDesktopWallpaper.SetWallpaper() here.
            // Windows/WorkerW stays untouched between slideshow changes.
        }

        public static string? GetDisplayedWallpaperPath()
        {
            return persistentHost?.CurrentWallpaperPath;
        }

        public static void Shutdown()
        {
            if (persistentHost == null)
            {
                return;
            }

            persistentHost.Dispose();
            persistentHost = null;
        }
    }
}
