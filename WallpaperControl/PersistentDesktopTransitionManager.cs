using System.IO;

namespace WallpaperControl
{
    internal static class PersistentDesktopTransitionManager
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

            if (!persistentHost.Initialize(
                    currentWallpaperPath))
            {
                persistentHost.Dispose();
                persistentHost = null;
                return false;
            }

            // Explorer/DWM braucht beim ersten Einfügen der Desktop-Ebene
            // kurz Zeit für seine einmalige Neuzusammensetzung.
            await Task.Delay(
                1500,
                cancellationToken);

            persistentHost.EnsureDesktopPlacement();

            return true;
        }

        public static async Task ApplyAsync(
            string? currentWallpaperPath,
            string nextWallpaperPath,
            WallpaperTransitionKind transitionKind,
            int durationMilliseconds,
            WallpaperTransitionDirection direction,
            WallpaperZoomMode zoomMode,
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

            await persistentHost.TransitionToAsync(
                nextWallpaperPath,
                transitionKind,
                Math.Clamp(
                    durationMilliseconds,
                    100,
                    10000),
                direction,
                zoomMode,
                cancellationToken);

            persistentHost.CommitCurrentPath(
                nextWallpaperPath);

            // Während der Sitzung kein SetWallpaper().
            // So bleibt Windows' eigener Fade vollständig außen vor.
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
