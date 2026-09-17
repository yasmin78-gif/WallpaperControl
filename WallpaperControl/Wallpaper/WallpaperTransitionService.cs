namespace WallpaperControl
{
    internal sealed class WallpaperTransitionService
    {
        private static readonly WallpaperTransitionKind[] RandomTransitions =
        {
            WallpaperTransitionKind.DesktopWipe,
            WallpaperTransitionKind.DesktopSlide,
            WallpaperTransitionKind.DesktopFade,
            WallpaperTransitionKind.DesktopZoomFade,
            WallpaperTransitionKind.DesktopSplit,
            WallpaperTransitionKind.DesktopCurtain
        };

        private readonly IReadOnlyDictionary<
            WallpaperTransitionKind,
            IWallpaperTransition> transitions;

        /// <summary>
        /// Registers the direct and animated transition implementations.
        /// </summary>
        public WallpaperTransitionService()
        {
            transitions =
                new Dictionary<WallpaperTransitionKind, IWallpaperTransition>
                {
                    { WallpaperTransitionKind.Direct, new DirectWallpaperTransition() },
                    { WallpaperTransitionKind.DesktopWipe, new DesktopWipeTransition() },
                    { WallpaperTransitionKind.DesktopSlide, new DesktopSlideTransition() },
                    { WallpaperTransitionKind.DesktopFade, new DesktopFadeTransition() },
                    { WallpaperTransitionKind.DesktopZoomFade, new DesktopZoomFadeTransition() },
                    { WallpaperTransitionKind.DesktopSplit, new DesktopSplitTransition() },
                    { WallpaperTransitionKind.DesktopCurtain, new DesktopCurtainTransition() },
                };
        }

        /// <summary>
        /// Resolves random effect options and dispatches the requested wallpaper transition.
        /// </summary>
        /// <param name="currentWallpaperPath">The currently displayed wallpaper path, when known.</param>
        /// <param name="nextWallpaperPath">The image path to display next.</param>
        /// <param name="transitionKind">The selected wallpaper transition effect.</param>
        /// <param name="durationMilliseconds">The requested transition duration in milliseconds.</param>
        /// <param name="direction">The requested direction of the animated wallpaper transition.</param>
        /// <param name="zoomMode">The requested direction of the zoom effect.</param>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        /// <returns>A task representing completion of the asynchronous operation.</returns>
        public Task ApplyAsync(
            string? currentWallpaperPath,
            string nextWallpaperPath,
            WallpaperTransitionKind transitionKind,
            int durationMilliseconds,
            WallpaperTransitionDirection direction,
            WallpaperZoomMode zoomMode,
            CancellationToken cancellationToken = default)
        {
            if (transitionKind == WallpaperTransitionKind.DesktopRandom)
            {
                transitionKind =
                    RandomTransitions[
                        Random.Shared.Next(
                            RandomTransitions.Length)];

                switch (transitionKind)
                {
                    case WallpaperTransitionKind.DesktopWipe:
                    case WallpaperTransitionKind.DesktopSlide:
                        direction =
                            Random.Shared.Next(4) switch
                            {
                                0 => WallpaperTransitionDirection.Left,
                                1 => WallpaperTransitionDirection.Right,
                                2 => WallpaperTransitionDirection.Up,
                                _ => WallpaperTransitionDirection.Down
                            };
                        break;

                    case WallpaperTransitionKind.DesktopZoomFade:
                        zoomMode =
                            Random.Shared.Next(2) == 0
                                ? WallpaperZoomMode.In
                                : WallpaperZoomMode.Out;
                        break;
                }
            }

            if (!transitions.TryGetValue(
                    transitionKind,
                    out IWallpaperTransition? transition))
            {
                throw new InvalidOperationException(
                    $"Unknown wallpaper transition: {transitionKind}");
            }

            return transition.ApplyAsync(
                currentWallpaperPath,
                nextWallpaperPath,
                durationMilliseconds,
                direction,
                zoomMode,
                cancellationToken);
        }
    }
}
