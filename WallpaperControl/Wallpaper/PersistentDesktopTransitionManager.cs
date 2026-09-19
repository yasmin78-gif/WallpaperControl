using System.IO;
using System.Drawing;
using System.Windows.Forms;

namespace WallpaperControl
{
    internal static class PersistentDesktopTransitionManager
    {
        private static IPersistentWallpaperHost? persistentHost;
        private static readonly object initializationLock = new();
        private static Task compositionReady = Task.CompletedTask;
        private static bool initializing;
        private static int generation;
        private static bool activitySuspended;
        private static string? nativeWallpaperAtStart;

        internal static bool SupportsConfiguration(int monitorCount, DesktopWallpaperPosition position) =>
            monitorCount == 1 && position != DesktopWallpaperPosition.Span;
        private static DesktopWallpaperPosition wallpaperPosition =
            DesktopWallpaperPosition.Fill;

        /// <summary>
        /// Acquires a ready renderer before the caller disables native scheduling.
        /// </summary>
        /// <param name="currentWallpaperPath">The currently displayed wallpaper path, when known.</param>
        /// <param name="takeOwnership">The native scheduling ownership action.</param>
        /// <param name="createHost">Optional native-host factory for isolated failure-path verification.</param>
        /// <returns>A task whose result is true when a usable desktop host is available.</returns>
        // All production callers run on the UI thread. The lock also serializes
        // concurrent requests; the guard rejects native message-loop reentrancy.
        internal static bool TryStartSession(string currentWallpaperPath, Action takeOwnership,
            Func<IPersistentWallpaperHost>? createHost = null)
        {
            lock (initializationLock)
            {
                if (!TryInitializeHost(currentWallpaperPath, createHost)) return false;
                try
                {
                    takeOwnership();
                    nativeWallpaperAtStart = currentWallpaperPath;
                    return true;
                }
                catch { Shutdown(); throw; }
            }
        }

        private static bool TryInitializeHost(string? path, Func<IPersistentWallpaperHost>? createHost = null)
        {
            if (initializing) return false;
            if (persistentHost is { IsDisposed: false } && persistentHost.EnsureDesktopPlacement()) return true;
            persistentHost?.Dispose();
            persistentHost = null;
            if (createHost == null && !SupportsConfiguration(Screen.AllScreens.Length, wallpaperPosition)) return false;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;

            initializing = true;
            int initialGeneration = generation;
            IPersistentWallpaperHost? candidate = null;
            try
            {
                candidate = createHost != null ? createHost() : new PersistentDesktopWallpaperHost();
                candidate.SetActivitySuspended(activitySuspended);
                candidate.SetWallpaperPosition(wallpaperPosition);
                if (!candidate.Initialize(path) || !candidate.EnsureDesktopPlacement()) return false;
                if (initialGeneration != generation) return false;
                candidate.SetActivitySuspended(activitySuspended);
                candidate.SetWallpaperPosition(wallpaperPosition);
                // Publish only after attachment, image decoding and final placement succeed.
                persistentHost = candidate;
                nativeWallpaperAtStart ??= path;
                candidate = null;
                compositionReady = Task.Delay(1500);
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Warning("Could not initialize the desktop renderer; retaining native scheduling.", ex);
                return false;
            }
            finally
            {
                candidate?.Dispose();
                initializing = false;
            }
        }

        public static async Task<bool> InitializeHostAsync(
            string? currentWallpaperPath,
            CancellationToken cancellationToken = default)
        {
            IPersistentWallpaperHost? host;
            Task ready;
            lock (initializationLock)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryInitializeHost(currentWallpaperPath)) return false;
                host = persistentHost;
                ready = compositionReady;
            }
            await ready.WaitAsync(cancellationToken);
            lock (initializationLock)
            {
                return ReferenceEquals(host, persistentHost) &&
                    host is { IsDisposed: false } && host.EnsureDesktopPlacement();
            }
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
        public static Task ApplyAsync(
            string? currentWallpaperPath, string nextWallpaperPath, WallpaperTransitionKind transitionKind,
            int durationMilliseconds, WallpaperTransitionDirection direction, WallpaperZoomMode zoomMode,
            CancellationToken cancellationToken = default) =>
            ApplyCoreAsync(currentWallpaperPath, nextWallpaperPath, transitionKind, durationMilliseconds,
                direction, zoomMode, null, cancellationToken);

        internal static async Task ApplyCoreAsync(
            string? currentWallpaperPath,
            string nextWallpaperPath,
            WallpaperTransitionKind transitionKind,
            int durationMilliseconds,
            WallpaperTransitionDirection direction,
            WallpaperZoomMode zoomMode,
            Func<string, CancellationToken, Task>? directFallback,
            CancellationToken cancellationToken)
        {
            int requestGeneration = generation;
            if (!File.Exists(nextWallpaperPath))
            {
                throw new FileNotFoundException("Wallpaper file not found.", nextWallpaperPath);
            }

            if (!await InitializeHostAsync(
                    currentWallpaperPath,
                    cancellationToken))
            {
                if (requestGeneration != generation) throw new OperationCanceledException();
                // The shell may disappear after startup. Remove a stale overlay and
                // retain a working direct rendering path for the custom scheduler.
                Shutdown();
                if (directFallback != null) await directFallback(nextWallpaperPath, cancellationToken);
                else await new DirectWallpaperTransition().ApplyAsync(currentWallpaperPath,
                    nextWallpaperPath, durationMilliseconds, direction, zoomMode, cancellationToken);
                return;
            }

            IPersistentWallpaperHost host = persistentHost!;
            if (requestGeneration != generation) throw new OperationCanceledException();

            await host.TransitionToAsync(
                nextWallpaperPath,
                transitionKind,
                Math.Clamp(
                    durationMilliseconds,
                    100,
                    10000),
                direction,
                zoomMode,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            if (requestGeneration != generation || host.IsDisposed) throw new OperationCanceledException();
            host.CommitCurrentPath(
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
            return persistentHost is { IsDisposed: false } ? persistentHost.CurrentWallpaperPath : null;
        }

        // Animated images deliberately differ from the native image retained at startup.
        internal static bool HasExternalWallpaperChange(string? nativePath) =>
            GetDisplayedWallpaperPath() != null && !string.IsNullOrWhiteSpace(nativePath) &&
            !string.Equals(nativePath, nativeWallpaperAtStart, StringComparison.OrdinalIgnoreCase);

        internal static void ApplyNativeSelection(Action applyNative)
        {
            applyNative();
            Shutdown();
        }

        /// <summary>
        /// Disposes the persistent desktop host and clears its shared reference.
        /// </summary>
        public static void Shutdown()
        {
            lock (initializationLock)
            {
                generation++;
                nativeWallpaperAtStart = null;
                persistentHost?.Dispose();
                persistentHost = null;
                compositionReady = Task.CompletedTask;
            }
        }
    }
}
