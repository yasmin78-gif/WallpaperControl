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

        internal static void SetActivitySuspended(bool suspended)
        {
            activitySuspended = suspended;
            persistentHost?.SetActivitySuspended(suspended);
        }
        public static void SetWallpaperPosition(DesktopWallpaperPosition position)
        {
            wallpaperPosition = position;

            if (persistentHost != null &&
                !persistentHost.IsDisposed)
            {
                persistentHost.SetWallpaperPosition(position);
            }
        }

        public static string? GetDisplayedWallpaperPath()
        {
            return persistentHost?.CurrentWallpaperPath;
        }

        public static bool TryAttachWidget(Control widget, Point location)
        {
            if (persistentHost == null ||
                persistentHost.IsDisposed ||
                !persistentHost.IsHandleCreated ||
                widget.IsDisposed)
            {
                return false;
            }

            Point safeLocation =
                WidgetSettings.EnsureVisible(location, widget.Size);

            if (widget.Parent != persistentHost)
            {
                widget.Parent?.Controls.Remove(widget);
                persistentHost.Controls.Add(widget);
            }

            widget.Location = safeLocation;
            widget.Visible = true;
            widget.BringToFront();
            widget.Invalidate();

            return true;
        }

        public static void DetachWidget(Control widget)
        {
            if (widget.IsDisposed)
                return;

            widget.Parent?.Controls.Remove(widget);
            widget.Visible = false;
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
