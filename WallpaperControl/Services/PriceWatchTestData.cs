namespace WallpaperControl;

// The widget test mode never creates or calls an HTTP client.
internal sealed class PriceWatchTestData : IPriceWatchSource
{
    private static readonly PriceWatchGame[] Games =
    [
        new(new("10000000-0000-4000-8000-000000000001"), "Cyberpunk 2077", "game"),
        new(new("10000000-0000-4000-8000-000000000002"), "Cyberpunk 2077: Ultimate Edition", "package"),
        new(new("10000000-0000-4000-8000-000000000003"), "Cyberpunk 2077: Phantom Liberty", "dlc")
    ];

    public Task<PriceWatchReply<PriceWatchGame[]>> SearchAsync(string title, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(new PriceWatchReply<PriceWatchGame[]>(DateTimeOffset.UtcNow,
            Games.Where(game => game.Title.Contains(title, StringComparison.OrdinalIgnoreCase)).ToArray()));
    }

    public Task<PriceWatchReply<PriceWatchPrices[]>> PricesAsync(Guid id, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        if (!Games.Any(game => game.Id == id))
            return Task.FromResult(new PriceWatchReply<PriceWatchPrices[]>(now, []));
        decimal regular = id == Games[1].Id ? 80m : 40m;
        return Task.FromResult(new PriceWatchReply<PriceWatchPrices[]>(now, [new(id,
        [
            Offer(35, "GOG", regular, 50, now),
            Offer(61, "Steam", regular, 0, now)
        ])]));
    }

    private static PriceWatchOffer Offer(int id, string name, decimal regular, decimal cut, DateTimeOffset now)
    {
        decimal amount = regular * (1 - cut / 100);
        return new(new(id, name), new(amount, (int)(amount * 100), "EUR"),
            new(regular, (int)(regular * 100), "EUR"), cut, new("https://fixture.invalid/"), now, null);
    }
}
