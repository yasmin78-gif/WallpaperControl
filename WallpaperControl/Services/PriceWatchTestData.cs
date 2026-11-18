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

    private static readonly PriceWatchGame[] DetailExamples =
    [
        new(new("10000000-0000-4000-8000-000000000004"), "PriceWatch Demo – English", "game"),
        new(new("10000000-0000-4000-8000-000000000005"), "PriceWatch Demo – Langer Text", "game"),
        new(new("10000000-0000-4000-8000-000000000006"), "PriceWatch Demo – Edition ohne Zuordnung", "package")
    ];

    private static readonly PriceWatchCoverSource LocalCover = new(token =>
    {
        token.ThrowIfCancellationRequested();
        using var image = new Bitmap(120, 168);
        using (var graphics = Graphics.FromImage(image))
        {
            graphics.Clear(Color.FromArgb(16, 49, 67));
            graphics.FillEllipse(Brushes.DarkCyan, 16, 24, 88, 88);
            graphics.DrawString("DEMO", SystemFonts.MessageBoxFont!, Brushes.White, 36, 127);
        }
        using var encoded = new MemoryStream();
        image.Save(encoded, System.Drawing.Imaging.ImageFormat.Png);
        return Task.FromResult<Stream>(new MemoryStream(encoded.ToArray(), writable: false));
    });

    private static readonly PriceWatchCoverSource BrokenCover = new(_ =>
        Task.FromResult<Stream>(new MemoryStream([0x50, 0x57])));

    internal static (PriceWatchDescription? Description, PriceWatchCoverSource? Cover) Details(PriceWatchGame game)
    {
        const string source = "PriceWatch Offline";
        const string german = "Dieser lokale Beispieltext zeigt die deutsche Beschreibung im Detailfenster. "
            + "Der Inhalt dient der Prüfung von Zeilenumbrüchen und Quellenangaben.";
        var candidates = new List<PriceWatchDescription>();
        PriceWatchCoverSource? cover = null;
        if (game.Id == Games[0].Id || game.Id == Games[1].Id)
        {
            bool fallback = game.Id == Games[1].Id;
            candidates.Add(new(game.Id, german, "de", source, fallback, fallback ? Games[0].Id : null));
            if (!fallback) cover = LocalCover;
        }
        else if (game.Id == DetailExamples[0].Id)
        {
            candidates.Add(new(game.Id, "This local example tests the English description fallback. "
                + "It has no German translation.", "en", source));
            cover = BrokenCover;
        }
        else if (game.Id == DetailExamples[1].Id)
        {
            candidates.Add(new(game.Id, string.Join("\n\n", Enumerable.Repeat(german, 20)), "de-DE", source));
        }
        return (PriceWatchDescription.Select(game, candidates, DateTimeOffset.UtcNow), cover);
    }

    public Task<PriceWatchReply<PriceWatchGame[]>> SearchAsync(string title, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(new PriceWatchReply<PriceWatchGame[]>(DateTimeOffset.UtcNow,
            Games.Concat(DetailExamples).Where(game => game.Title.Contains(title, StringComparison.OrdinalIgnoreCase)).ToArray()));
    }

    public Task<PriceWatchReply<PriceWatchGameInfo>> InfoAsync(Guid id, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var game = Games.Concat(DetailExamples).FirstOrDefault(item => item.Id == id)
            ?? throw new PriceWatchException(PriceWatchFailure.GameNotFound);
        return Task.FromResult(new PriceWatchReply<PriceWatchGameInfo>(DateTimeOffset.UtcNow,
            new(id, game.Title, new DateOnly(2020, 12, 10), ["Example developer"], ["Example publisher"], null)));
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
