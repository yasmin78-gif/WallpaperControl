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
                    }
                };
        }

        public Task ApplyAsync(
            string? currentWallpaperPath,
            string nextWallpaperPath,
            WallpaperTransitionKind transitionKind,
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
                cancellationToken);
        }
    }
}
