namespace WallpaperControl
{
    internal sealed class WallpaperTransitionService
    {
        private readonly IReadOnlyDictionary<
            WallpaperTransitionKind,
            IWallpaperTransition> transitions;

        public WallpaperTransitionService()
        {
            transitions =
                new Dictionary<
                    WallpaperTransitionKind,
                    IWallpaperTransition>
                {
                    {
                        WallpaperTransitionKind.Direct,
                        new DirectWallpaperTransition()
                    },
                    {
                        WallpaperTransitionKind.DesktopWipe,
                        new DesktopWipeTransition()
                    },
                    {
                        WallpaperTransitionKind.DesktopSlide,
                        new DesktopSlideTransition()
                    },
                    {
                        WallpaperTransitionKind.DesktopFade,
                        new DesktopFadeTransition()
                    },
                    {
                        WallpaperTransitionKind.DesktopWipeRight,
                        new DesktopWipeRightTransition()
                    },
                    {
                        WallpaperTransitionKind.DesktopSlideRandom,
                        new DesktopSlideRandomTransition()
                    },
                    {
                        WallpaperTransitionKind.DesktopZoomFade,
                        new DesktopZoomFadeTransition()
                    }
                };
        }

        public Task ApplyAsync(
            string? currentWallpaperPath,
            string nextWallpaperPath,
            WallpaperTransitionKind transitionKind,
            int durationMilliseconds,
            CancellationToken cancellationToken = default)
        {
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
                cancellationToken);
        }
    }
}
