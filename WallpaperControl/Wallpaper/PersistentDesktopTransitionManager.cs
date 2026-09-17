using System.IO;
using System.Drawing;
using System.Windows.Forms;

namespace WallpaperControl
{
    internal static class PersistentDesktopTransitionManager
    {
        private static PersistentDesktopWallpaperHost? persistentHost;
        private static bool activitySuspended;
        private static DesktopWallpaperPosition wallpaperPosition =
            DesktopWallpaperPosition.Fill;

        /// <summary>
        /// Creates and places the persistent wallpaper host before allowing Explorer&apos;s initial composition to settle.
        /// </summary>
        /// <param name="currentWallpaperPath">The currently displayed wallpaper path, when known.</param>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        /// <returns>A task whose result is true when a usable desktop host is available.</returns>
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
            persistentHost.SetActivitySuspended(activitySuspended);
            persistentHost.SetWallpaperPosition(wallpaperPosition);

            if (!persistentHost.Initialize(
                    currentWallpaperPath))
            {
                persistentHost.Dispose();
                persistentHost = null;
                return false;
            }

            // Explorer/DWM needs a short settling period when the desktop layer
            // is first inserted and the desktop composition is rebuilt.
            await Task.Delay(
                1500,
                cancellationToken);

            persistentHost.EnsureDesktopPlacement();

            return true;
        }

        /// <summary>
        /// Initializes the desktop host when needed, applies the selected transition, and commits the displayed path.
        /// </summary>
        /// <param name="currentWallpaperPath">The currently displayed wallpaper path, when known.</param>
        /// <param name="nextWallpaperPath">The image path to display next.</param>
        /// <param name="transitionKind">The selected wallpaper transition effect.</param>
        /// <param name="durationMilliseconds">The requested transition duration in milliseconds.</param>
        /// <param name="direction">The requested direction of the animated wallpaper transition.</param>
        /// <param name="zoomMode">The requested direction of the zoom effect.</param>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        /// <returns>A task representing completion of the asynchronous operation.</returns>
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

            // Avoid SetWallpaper() while the custom renderer owns the session.
            // This prevents the native Windows fade from overlapping our transition.
        }

        /// <summary>
        /// Stores the suspension state and forwards it to an existing desktop host.
        /// </summary>
        /// <param name="suspended">True to pause background activity; false to resume it.</param>
        internal static void SetActivitySuspended(bool suspended)
        {
            activitySuspended = suspended;
            persistentHost?.SetActivitySuspended(suspended);
        }
        /// <summary>
        /// Stores the image layout and applies it to an existing desktop host.
        /// </summary>
        /// <param name="position">The Windows wallpaper scaling and placement mode.</param>
        public static void SetWallpaperPosition(DesktopWallpaperPosition position)
        {
            wallpaperPosition = position;

            if (persistentHost != null &&
                !persistentHost.IsDisposed)
            {
                persistentHost.SetWallpaperPosition(position);
            }
        }

        /// <summary>
        /// Reads the path currently displayed by the persistent desktop host.
        /// </summary>
        /// <returns>The hosted wallpaper path, or null when no host exists.</returns>
        public static string? GetDisplayedWallpaperPath()
        {
            return persistentHost?.CurrentWallpaperPath;
        }

        /// <summary>
        /// Disposes the persistent desktop host and clears its shared reference.
        /// </summary>
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
