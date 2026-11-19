namespace WallpaperControl;

// The widget test mode never creates or calls an HTTP client.
internal sealed class PriceWatchTestData : IPriceWatchSource
{
    private readonly Func<DateTimeOffset> historyClock;
    private readonly DateTimeOffset historyAnchor;

    internal PriceWatchTestData(Func<DateTimeOffset>? historyClock = null)
    {
        this.historyClock = historyClock ?? (() => DateTimeOffset.UtcNow);
        // One calendar basis per offline source, independent of selected window and request time.
        historyAnchor = new DateTimeOffset(this.historyClock().UtcDateTime.Date, TimeSpan.Zero);
    }
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
        new(new("10000000-0000-4000-8000-000000000006"), "PriceWatch Demo – Edition ohne Zuordnung", "package"),
        new(new("10000000-0000-4000-8000-000000000007"), "Duke Nukem 3D: 20th Anniversary World Tour", "game"),
        new(new("10000000-0000-4000-8000-000000000008"), "Baldur's Gate 3", "game")
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

    public Task<PriceWatchReply<PriceWatchPrices[]>> PricesAsync(Guid id, CancellationToken token) =>
        PricesAsync(id, PriceWatchShops.Legacy, token);

    public Task<PriceWatchReply<PriceWatchPrices[]>> PricesAsync(Guid id, IReadOnlyList<int> shops, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        if (!Games.Concat(DetailExamples).Any(game => game.Id == id))
            return Task.FromResult(new PriceWatchReply<PriceWatchPrices[]>(now, []));
        decimal regular = id == Games[1].Id ? 80m : 40m;
        return Task.FromResult(new PriceWatchReply<PriceWatchPrices[]>(now, [new(id,
            PriceWatchShops.Validate(shops).Select(shopId =>
            {
                var shop = PriceWatchShops.Supported.Single(shop => shop.Id == shopId);
                decimal cut = shopId switch { 35 => 50, 16 => 60, 27 => 50, 62 => 25, 48 => 10, 52 => 30, _ => 0 };
                return Offer(shop.Id, shop.Name, regular, cut, now);
            }).ToArray())]));
    }

    private static PriceWatchOffer Offer(int id, string name, decimal regular, decimal cut, DateTimeOffset now)
    {
        decimal amount = regular * (1 - cut / 100);
        return new(new(id, name), new(amount, (int)(amount * 100), "EUR"),
            new(regular, (int)(regular * 100), "EUR"), cut, new("https://fixture.invalid/"), now, null);
    }

    public Task<PriceWatchReply<PriceWatchHistory[]>> HistoryAsync(Guid id, int days, IReadOnlyList<int> shops, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (days is not (7 or 30 or 90) || !Games.Concat(DetailExamples).Any(g => g.Id == id))
            throw new PriceWatchException(PriceWatchFailure.InvalidRequest);
        var now = historyClock();
        var points = new List<PriceWatchHistory>();
        foreach (int shop in PriceWatchShops.Validate(shops).Where(s => s != 52))
        {
            var merchant = PriceWatchShops.Supported.Single(s => s.Id == shop);
            int regular = (id == Games[1].Id ? 8000 : 4000)
                + Array.FindIndex(PriceWatchShops.Supported.ToArray(), s => s.Id == shop) * 200;
            (int Day, int Hour, decimal? Factor)[] events = shop switch
            {
                35 => [(-88, 16, 1m), (-64, 10, .75m), (-24, 9, .5m), (-6, 12, .75m),
                    (-5, 8, null), (-3, 10, .75m), (-1, 9, .5m), (-1, 16, .6m)],
                61 => [(-89, 10, 1m), (-65, 14, .8m), (-29, 10, 1m), (-12, 16, .6m),
                    (-6, 11, 1m), (-2, 8, .75m), (-1, 20, .5m)],
                16 => [(-80, 8, 1m), (-48, 12, .6m), (-22, 17, 1m), (-8, 14, .5m),
                    (-4, 18, .7m), (-2, 13, .4m)],
                27 => [(-85, 14, .9m), (-51, 9, .75m), (-20, 16, .8m), (-6, 15, .65m),
                    (-3, 12, .8m), (-1, 11, .55m)],
                62 => [(-82, 18, 1m), (-60, 10, .7m), (-43, 8, null), (-34, 11, 1m),
                    (-15, 15, .6m), (-5, 14, .8m), (-1, 18, 1m)],
                _ => [(-87, 12, 1m), (-55, 15, .8m), (-25, 10, .6m), (-7, 18, 1m),
                    (-4, 9, .9m), (-1, 14, .7m)]
            };
            foreach (var (day, hour, factor) in events)
            {
                int amount = (int)(regular * (factor ?? 1));
                points.Add(new(new(shop, merchant.Name), historyAnchor.AddDays(day).AddHours(hour), factor == null ? null :
                    new(new(amount / 100m, amount, "EUR"), new(regular / 100m, regular, "EUR"), (1 - factor.Value) * 100)));
            }
        }
        return Task.FromResult(new PriceWatchReply<PriceWatchHistory[]>(now, points
            .Where(p => p.Timestamp >= now.AddDays(-days) && p.Timestamp <= now)
            .OrderByDescending(p => p.Timestamp).ToArray()));
    }

    // Explicit offline presentation fixtures, never written to the user's observation table.
    internal static PriceWatchObservation[] ObservationExamples(Guid id, int[] shops)
    {
        var anchor = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        return shops.SelectMany(shop => new[] { 4, 2 }.Select(day => new PriceWatchObservation(id, false, shop,
            anchor.AddDays(-day).AddHours(12), 1800 + shop * 10 + day * 100, 4000))).ToArray();
    }
}
