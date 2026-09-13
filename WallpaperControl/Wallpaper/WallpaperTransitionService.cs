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
