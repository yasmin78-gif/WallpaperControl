extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using Microsoft.Win32;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

internal static class PriceWatchTests
{
    private const string OfferUrl = "https://offers.example.invalid/A%2fb?ref=one&ref=two";
    private static readonly Guid Id = new("10000000-0000-4000-8000-000000000002");

    internal static void Run(Action<bool, string> check)
    {
        RunClient(check).GetAwaiter().GetResult();
        CheckOfflineCoverAndDescriptions(check).GetAwaiter().GetResult();
        CheckItadCovers(check).GetAwaiter().GetResult();
#if DEBUG
        App.Localization.Initialize();
        check(true, "startup localization audit passes for all supported languages");
#endif
        CheckStorageAndLayout(check);
    }

    private static async Task CheckItadCovers(Action<bool, string> check)
    {
        Uri Url(int id) => new($"https://assets.isthereanydeal.com/fixture/boxart.png?t={id}");
        using var native = App.PriceWatchItadCovers.CreateHandler();
        check(!native.AllowAutoRedirect && !native.UseCookies && !native.UseDefaultCredentials
            && native.Credentials == null, "CDN transport disables redirects, cookies and credentials");
        check(App.PriceWatchItadCovers.IsAllowed(Url(1))
            && new[] { "http://assets.isthereanydeal.com/a", "https://assets.isthereanydeal.com.evil.test/a",
                "https://assets.isthereanydeal.com:444/a", "https://user@assets.isthereanydeal.com/a",
                "https://example.invalid/a", "file:///C:/cover.png", "https://assets.isthereanydeal.com/a#fragment" }
                .All(value => !App.PriceWatchItadCovers.IsAllowed(new(value))),
            "CDN validation permits only the exact HTTPS host without credentials or custom port");
        using var handler = new CoverHandler();
        using var covers = new App.PriceWatchItadCovers(handler);
        using var first = await covers.LoadAsync(Url(1), default);
        using var second = await covers.LoadAsync(Url(1), default);
        check(first != null && second != null && handler.Calls == 1 && handler.Safe,
            "successful covers reopen from RAM without authorization headers");
        for (int id = 2; id <= 9; id++)
        {
            using var image = await covers.LoadAsync(Url(id), default);
        }
        using var evicted = await covers.LoadAsync(Url(1), default);
        check(handler.Calls == 10, "cover RAM cache evicts the least recently used entry at eight images");
        using var invalid = await covers.LoadAsync(new("https://example.invalid/a"), default);
        check(handler.Calls == 10 && invalid == null, "invalid cover URLs never reach HTTP");
        handler.Status = HttpStatusCode.Redirect;
        using var redirect = await covers.LoadAsync(Url(10), default);
        handler.Status = HttpStatusCode.OK;
        handler.Bytes = [1, 2, 3];
        using var corrupt = await covers.LoadAsync(Url(10), default);
        handler.Bytes = CoverHandler.Png();
        using var recovered = await covers.LoadAsync(Url(10), default);
        check(redirect == null && corrupt == null && recovered != null && handler.Calls == 13,
            "redirects and corrupt images retain the placeholder and are not cached");

        using var delayed = new CoverHandler { Wait = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var shared = new App.PriceWatchItadCovers(delayed);
        using var cancel = new CancellationTokenSource();
        var cancelled = shared.LoadAsync(Url(1), cancel.Token);
        var remaining = shared.LoadAsync(Url(1), default);
        await Task.Delay(100);
        cancel.Cancel();
        bool stopped = false;
        try { using var discarded = await cancelled; }
        catch (OperationCanceledException) { stopped = true; }
        delayed.Wait.SetResult();
        using var result = await remaining;
        check(stopped && result != null && delayed.Calls == 1,
            "simultaneous CDN readers share one request and cancel independently");
        using var pendingHandler = new CoverHandler { Wait = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var shutdown = new App.PriceWatchItadCovers(pendingHandler);
        var request = shutdown.LoadAsync(Url(1), default);
        await Task.Delay(100);
        shutdown.Dispose();
        using var ended = await request.WaitAsync(TimeSpan.FromSeconds(5));
        check(ended == null, "disposing the cover cache cancels active downloads");
    }

    private sealed class CoverHandler : HttpMessageHandler
    {
        internal int Calls;
        internal bool Safe = true;
        internal byte[] Bytes = Png();
        internal HttpStatusCode Status = HttpStatusCode.OK;
        internal TaskCompletionSource? Wait;
        internal static byte[] Png()
        {
            using var image = new Bitmap(12, 17);
            using var stream = new MemoryStream();
            image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            return stream.ToArray();
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            Safe &= request.Method == HttpMethod.Get && request.Headers.Authorization == null
                && !request.Headers.Contains("Cookie") && !request.Headers.Contains("ITAD-API-Key");
            if (Wait != null) await Wait.Task.WaitAsync(token);
            return new(Status) { Content = new ByteArrayContent(Bytes) };
        }
    }

    private static async Task CheckOfflineCoverAndDescriptions(Action<bool, string> check)
    {
        var fixtures = new App.PriceWatchTestData();
        var games = (await fixtures.SearchAsync("Cyberpunk", default)).Data;
        var examples = (await fixtures.SearchAsync("PriceWatch Demo", default)).Data;
        var german = App.PriceWatchTestData.Details(games[0]);
        var edition = App.PriceWatchTestData.Details(games[1]);
        var english = App.PriceWatchTestData.Details(examples[0]);
        var longText = App.PriceWatchTestData.Details(examples[1]);
        check(german.Description?.Language == "de" && english.Description?.Language == "en"
            && longText.Description?.Text.Length > 2000, "local description fixtures cover German, English and long text");
        check(edition.Description is { IsBaseGameFallback: true } fallback
            && fallback.ItadId == games[1].Id && fallback.BaseGameId == games[0].Id
            && App.PriceWatchTestData.Details(examples[2]).Description == null,
            "only a confirmed parent supplies an edition fallback");
        var now = DateTimeOffset.UtcNow;
        var candidate = german.Description!;
        var en = candidate with { Language = "en-GB" };
        var de = candidate with { Language = "de-DE" };
        check(App.PriceWatchDescription.Select(games[0], [en, de], now) == de
            && App.PriceWatchDescription.Select(games[0], [de with { ExpiresAt = now }, en], now) == en
            && App.PriceWatchDescription.Select(games[0], [candidate with { Language = "fr" }], now) == null,
            "selection uses declared language and ignores expired and unsupported texts");
        check(App.PriceWatchDescription.Select(games[1], [edition.Description!, en with { ItadId = games[1].Id }], now)?.Language == "en-GB"
            && App.PriceWatchDescription.Select(games[1], [edition.Description! with { BaseGameId = null }], now) == null
            && App.PriceWatchDescription.Select(games[2], [edition.Description! with { ItadId = games[2].Id }], now) == null
            && App.PriceWatchTestData.Details(games[2]).Description == null,
            "an edition's own text wins and a DLC cannot inherit base-game text");

        using var png = await german.Cover!.LoadAsync(default);
        using var broken = await english.Cover!.LoadAsync(default);
        check(png?.Size == new Size(120, 168) && broken == null && edition.Cover == null,
            "local PNG loads and missing or corrupt cover keeps the placeholder");

        using var original = new Bitmap(32, 64);
        using var encoded = new MemoryStream();
        original.Save(encoded, System.Drawing.Imaging.ImageFormat.Jpeg);
        byte[] bytes = encoded.ToArray();
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int reads = 0;
        var shared = new App.PriceWatchCoverSource(async token =>
        {
            Interlocked.Increment(ref reads);
            opened.TrySetResult();
            try { await release.Task.WaitAsync(token); }
            finally { finished.TrySetResult(); }
            return new MemoryStream(bytes);
        });
        using var cancel = new CancellationTokenSource();
        var first = shared.LoadAsync(cancel.Token);
        await opened.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = shared.LoadAsync(default);
        cancel.Cancel();
        bool cancelled = false;
        try { using var discarded = await first; }
        catch (OperationCanceledException) { cancelled = true; }
        bool stillReading = !finished.Task.IsCompleted;
        release.SetResult();
        using var jpeg = await second.WaitAsync(TimeSpan.FromSeconds(5));
        check(cancelled && stillReading && reads == 1 && jpeg?.Size == new Size(32, 64),
            "parallel JPEG loads share one read and cancellation preserves the remaining reader");

        var oversize = new App.PriceWatchCoverSource(_ =>
            Task.FromResult<Stream>(new MemoryStream(new byte[2 * 1024 * 1024 + 1])));
        using var oversized = await oversize.LoadAsync(default);
        using var wide = new Bitmap(2049, 1);
        using var wideEncoded = new MemoryStream();
        wide.Save(wideEncoded, System.Drawing.Imaging.ImageFormat.Png);
        var dimensions = new App.PriceWatchCoverSource(_ =>
            Task.FromResult<Stream>(new MemoryStream(wideEncoded.ToArray())));
        using var tooWide = await dimensions.LoadAsync(default);
        check(oversized == null && tooWide == null, "cover loader enforces byte and dimension limits");
    }

    private static async Task RunClient(Action<bool, string> check)
    {
        var fixtures = new App.PriceWatchTestData();
        var titles = await fixtures.SearchAsync("Cyberpunk", default);
        check(titles.Data.Select(x => x.Type).SequenceEqual(new[] { "game", "package", "dlc" })
            && titles.Data.Select(x => x.Id).Distinct().Count() == 3, "offline search distinguishes the three editions");
        var sample = await fixtures.PricesAsync(Id, default);
        check(sample.Data.Single().Deals.Select(x => x.Shop.Id).SequenceEqual(new[] { 35, 61 }),
            "offline prices have both required shops");
        check((await fixtures.PricesAsync(Guid.Empty, default)).Data.Length == 0, "offline unknown game is empty");

        using var handler = new Handler();
        using var http = new HttpClient(handler);
        var client = new App.PriceWatchClient(http, () => new App.TrackingCredential("fixture-user:fixture-value"));
        handler.Data = titles.Data;
        var search = await client.SearchAsync("Cyberpunk", default);
        check(search.Data[1].Id == Id && search.Data[1].Type == "package" && handler.Path == "/search",
            "typed client posts search and keeps the selected package identity");
        check(handler.SecureOrigin && handler.BasicOnly && handler.Body.GetProperty("title").GetString() == "Cyberpunk",
            "authorization stays on the fixed HTTPS origin and credentials are absent from JSON");

        handler.Data = new Dictionary<string, Guid?> { ["bundle/32470"] = Id };
        var mapping = await client.LookupAsync(new("https://store.steampowered.com/bundle/32470/"), default);
        check(mapping.Data["bundle/32470"] == Id && handler.Path == "/lookup", "typed Steam bundle lookup");
        var annotated = sample.Data[0].Deals[0] with { Url = new(OfferUrl), Platforms = [new(1, "Windows")], Drm = [new(1000, "DRM Free")] };
        handler.Data = new[] { new App.PriceWatchPrices(Id, [annotated]) };
        var offers = await client.PricesAsync(Id, default);
        check(offers.Data[0].Id == Id && offers.Data[0].Deals[0].Price.Amount == 40
            && handler.Body.GetProperty("shops").EnumerateArray().Select(x => x.GetInt32()).SequenceEqual(new[] { 35, 61 }),
            "typed EUR prices are scoped to the selection and the two shops");
        check(offers.Data[0].Deals[0].Platforms?.Single().Name == "Windows"
            && offers.Data[0].Deals[0].Drm?.Single().Name == "DRM Free",
            "existing price response preserves optional platform and activation fields");
        check(offers.Data[0].Deals[0].Url.OriginalString == OfferUrl,
            "original offer URL including query and escape spelling reaches the C# model");
        handler.Data = new[] { new App.PriceWatchHistory(new(35, "GOG"), DateTimeOffset.UtcNow, null) };
        var history = await client.HistoryAsync(Id, 30, default);
        check(history.Data[0].Deal == null && handler.Body.GetProperty("days").GetInt32() == 30 && handler.Path == "/history",
            "typed history preserves a point without a price");
        handler.Data = Array.Empty<App.PriceWatchPrices>();
        check((await client.PricesAsync(Id, default)).Data.Length == 0, "valid empty price response");
        var metadata = new App.PriceWatchGameInfo(Id, "Detailed fixture title", new(2024, 2, 29),
            ["First developer", "Second developer"], ["Publisher"], new("https://assets.example.invalid/cover.jpg?t=12"));
        handler.Data = metadata;
        var gameInfo = await client.InfoAsync(Id, default);
        check(handler.Path == "/info" && handler.Body.GetProperty("id").GetGuid() == Id &&
            gameInfo.Data.ReleaseDate == new DateOnly(2024, 2, 29)
            && gameInfo.Data.Developers.SequenceEqual(metadata.Developers)
            && gameInfo.Data.Boxart?.OriginalString == "https://assets.example.invalid/cover.jpg?t=12",
            "game info decodes the fixed request, date, all companies and cover address");
        await Fails(() => client.InfoAsync(Guid.NewGuid(), default), App.PriceWatchFailure.InvalidResponse,
            "game info rejects the wrong ITAD identifier", check);
        handler.Status = HttpStatusCode.NotFound;
        await Fails(() => client.InfoAsync(Id, default), App.PriceWatchFailure.GameNotFound,
            "missing game info reports unavailable", check);
        handler.Status = HttpStatusCode.OK;
        handler.Data = sample.Data;
        await Fails(() => client.PricesAsync(Guid.NewGuid(), default), App.PriceWatchFailure.InvalidResponse,
            "mismatched product identity rejected", check);
        handler.Invalid = true;
        await Fails(() => client.PricesAsync(Id, default), App.PriceWatchFailure.InvalidResponse, "malformed response is sanitized", check);
        handler.Invalid = false;
        handler.Status = HttpStatusCode.Unauthorized;
        await Fails(() => client.PricesAsync(Id, default), App.PriceWatchFailure.Credentials, "401 is a credential error", check);
        handler.Status = HttpStatusCode.BadRequest;
        await Fails(() => client.PricesAsync(Id, default), App.PriceWatchFailure.InvalidRequest, "400 is an input error", check);
        handler.Status = HttpStatusCode.ServiceUnavailable;
        handler.Retry = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(1));
        await Fails(() => client.PricesAsync(Id, default), App.PriceWatchFailure.Busy, "503 with HTTP-date Retry-After", check, true);
        int calls = handler.Calls;
        await Fails(() => client.PricesAsync(Id, default), App.PriceWatchFailure.Busy, "manual repeat respects cooldown", check, true);
        check(handler.Calls == calls, "cooldown performs no extra HTTP call");
        handler.Status = HttpStatusCode.TooManyRequests;
        handler.Retry = new RetryConditionHeaderValue(TimeSpan.FromSeconds(12));
        var fresh = new App.PriceWatchClient(http, () => new App.TrackingCredential("fixture-user:fixture-value"));
        await Fails(() => fresh.PricesAsync(Id, default), App.PriceWatchFailure.Busy, "429 with seconds Retry-After", check, true);
        handler.Delay = true;
        var quick = new App.PriceWatchClient(http, () => new App.TrackingCredential("fixture-user:fixture-value"), TimeSpan.FromMilliseconds(150));
        await Fails(() => quick.PricesAsync(Id, default), App.PriceWatchFailure.Timeout, "deadline cancels mocked HTTP", check);
        using var cancelled = new CancellationTokenSource();
        var awaiting = quick.PricesAsync(Id, cancelled.Token);
        cancelled.Cancel();
        try { await awaiting; check(false, "user cancellation propagates"); }
        catch (OperationCanceledException) { check(true, "user cancellation propagates"); }
        calls = handler.Calls;
        var missing = new App.PriceWatchClient(http, () => null);
        await Fails(() => missing.PricesAsync(Id, default), App.PriceWatchFailure.Credentials, "missing credential is local", check);
        check(handler.Calls == calls, "missing credential causes no HTTP call");
    }

    private static async Task Fails(Func<Task> call, App.PriceWatchFailure failure, string name,
        Action<bool, string> check, bool retry = false)
    {
        try { await call(); check(false, name); }
        catch (App.PriceWatchException error)
        {
            check(error.Failure == failure && (!retry || error.RetryAfter > TimeSpan.Zero)
                && error.InnerException == null && error.Message == failure.ToString(), name);
        }
    }

    private static void CheckStorageAndLayout(Action<bool, string> check)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var defaults = App.PriceWatchWidgetSettings.Parse("{}");
                check(!defaults.Live && !defaults.Enabled, "legacy settings default to disabled and offline");
                var path = @"Software\WallpaperControl\Regression\PriceWatch-" + Guid.NewGuid().ToString("N");
                try
                {
                    var credentials = new App.PriceWatchCredentials(path);
                    const string fixturePassword = "fixture-password-only";
                    check(credentials.Save("fixture-user", fixturePassword), "pilot credentials save through protected store");
                    using (var values = Registry.CurrentUser.OpenSubKey(path))
                    {
                        string? value = values?.GetValue("pricewatch") as string;
                        check(!string.IsNullOrEmpty(value) && !value.Contains(fixturePassword, StringComparison.Ordinal),
                            "credential registry value contains ciphertext");
                    }
                    check(credentials.Load() != null && credentials.Delete() && credentials.Load() == null,
                        "protected credential load and delete");
                }
                finally { Registry.CurrentUser.DeleteSubKeyTree(path, false); }

                using var editor = new App.WidgetSettingsEditor(new App.WidgetSettings());
                check(editor.WidgetKeys.Contains("PriceWatchTitle") && !editor.ReadWidgetSettings(false).PriceWatch.Enabled,
                    "settings editor exposes an opt-in PriceWatch page");
                int sourceCalls = 0;
                using var storage = new TemporaryWatchlist();
                CheckWatchlist(check, storage.Store);
                var reopened = storage.Store;
                reopened.Migrate(new App.PriceWatchGame(Id, "Legacy", "package"), null);
                check(reopened.Read(false).Length == 1102 && reopened.Read(false).All(entry => entry.Game.Id != Id),
                    "reopening SQLite preserves the watchlist and does not re-import a removed legacy selection");
                using var widget = new App.PriceWatchWidgetForm(_ => { sourceCalls++; return new App.PriceWatchTestData(); },
                    defaults, "de", _ => { }, new App.PriceWatchStore(Path.Combine(storage.DirectoryPath, "widget.sqlite")));
                widget.Apply(defaults, "de", true);
                widget.PerformLayout();
                using var dialog = new App.PriceWatchSearchForm(new App.PriceWatchTestData(), false, "de", defaults.Style);
                dialog.PerformLayout();
                check(sourceCalls == 0,
                    "constructing the widget and entering preview do not request data");
                check(dialog.Text.Contains("Testdaten") && widget.ModeCaption.Contains("Testdaten"), "both surfaces label offline test data");
                dialog.Opacity = 0;
                dialog.Shown += (_, _) => dialog.BeginInvoke(new Action(() =>
                {
                    var table = dialog.Controls[0];
                    var query = table.Controls.Cast<Control>().OfType<TextBox>().Single();
                    var list = table.Controls.Cast<Control>().OfType<ListBox>().Single();
                    query.Text = "Cyberpunk 2077";
                    table.Controls.Cast<Control>().OfType<Button>().Single().PerformClick();
                    check(list.Items.Count == 3, "search dialog shows three local edition rows");
                    list.SelectedIndex = 1;
                    var actions = table.Controls.Cast<Control>().OfType<FlowLayoutPanel>().Single();
                    actions.Controls.Cast<Control>().OfType<Button>()
                        .Single(button => button.DialogResult != DialogResult.Cancel).PerformClick();
                }));
                check(dialog.ShowDialog() == DialogResult.OK && dialog.SelectedGame?.Id == Id,
                    "search dialog confirms the exact selected offline edition");
                foreach (int dpi in new[] { 96, 144, 192 })
                {
                    var area = new Size(480, 300);
                    Size dialogSize = new(Math.Min(area.Width, 460 * dpi / 96), Math.Min(area.Height, 295 * dpi / 96));
                    dialog.Size = dialogSize;
                    dialog.PerformLayout();
                    var results = dialog.Controls[0].Controls.Cast<Control>().OfType<ListBox>().Single();
                    check(results.Width > 0 && results.Height > 0
                        && dialog.Controls[0].ClientRectangle.Contains(results.Bounds),
                        $"search result viewport fits at simulated {dpi} DPI");
                    using var image = widget.RenderBitmap(area, dpi, out var layout);
                    check(ValidLayout(image, area, layout), $"widget actions fit and hit-test at simulated {dpi} DPI");
                }
                CheckDesign(check, widget, defaults);
                CheckSettingsDesign(check);
                CheckGameInfoStorageAndPresentation(check);
                CheckOfflineDetailLayout(check);
                CheckCoverCredit(check);
                CheckDetailSizing(check);
                CheckOfferPresentation(check);
                CheckShopLinksAndChrome(check);
                CheckDesktopTrayLifecycle(check);

            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw new InvalidOperationException("PriceWatch UI checks failed.", failure);
    }

    private static void CheckOfflineDetailLayout(Action<bool, string> check)
    {
        var fixtures = new App.PriceWatchTestData();
        var examples = fixtures.SearchAsync("PriceWatch Demo", default).GetAwaiter().GetResult().Data;
        var cyberpunk = fixtures.SearchAsync("Cyberpunk", default).GetAwaiter().GetResult().Data;
        var edition = cyberpunk[1];
        foreach (var style in new[] { App.SystemWidgetStyle.Minimal, App.SystemWidgetStyle.Clean, App.SystemWidgetStyle.Glow })
        {
            var game = cyberpunk[0];
            var fixture = App.PriceWatchTestData.Details(game);
            var prices = fixtures.PricesAsync(game.Id, default).GetAwaiter().GetResult();
            var info = fixtures.InfoAsync(game.Id, default).GetAwaiter().GetResult();
            using (var standard = new App.PriceWatchDetailForm(new(game, prices), "Testdaten", "de", style,
                () => Task.CompletedTask, () => false, info: info.Data, refreshInfo: () => Task.CompletedTask,
                description: fixture.Description) { Opacity = 0 })
            {
                standard.Show();
                Application.DoEvents();
                var cover = standard.Controls.Find("PriceWatchCover", true).Single();
                var title = cover.Parent!.Parent!.Controls.OfType<TableLayoutPanel>()
                    .Single(panel => panel != cover.Parent).Controls.OfType<Label>().First();
                var actions = standard.Controls.Find("PriceWatchRefreshInfo", true).Single().Parent!;
                var buttons = actions.Controls.OfType<Button>().ToArray();
                var work = Screen.FromControl(standard).WorkingArea;
                int Logical(int value) => (int)Math.Round(value * 96d / standard.DeviceDpi);
                check(work.Contains(standard.Bounds) && Logical(cover.Width) == 120 && Logical(cover.Height) == 170
                    && title.Font.SizeInPoints == 16.5f && standard.Font.SizeInPoints == 11.25f
                    && (Logical(work.Width) < 710 || (Logical(standard.ClientSize.Width) >= 695
                        && buttons.Select(button => button.Top).Distinct().Count() == 1))
                    && (Logical(work.Height) < 590 || Logical(standard.ClientSize.Height) >= 575)
                    && buttons.Length == 4 && buttons.All(button => actions.ClientRectangle.Contains(button.Bounds)
                        && button.Size.Width >= button.GetPreferredSize(Size.Empty).Width),
                    $"expanded detail sizes and uncut actions in {style}");
                var viewport = standard.Controls.OfType<Panel>().Single();
                if (Logical(work.Width) >= 710 && Logical(work.Height) >= 590)
                    check(!viewport.VerticalScroll.Visible && !viewport.HorizontalScroll.Visible,
                        $"standard Cyberpunk details need no scrollbar in {style} DPI {standard.DeviceDpi} viewport {viewport.ClientSize} content {viewport.Controls[0].Size} scroll V{viewport.VerticalScroll.Visible} H{viewport.HorizontalScroll.Visible} rows {string.Join(",", ((TableLayoutPanel)viewport.Controls[0]).GetRowHeights())}");
            }
            var longText = App.PriceWatchTestData.Details(examples[1]).Description;
            using var dialog = new App.PriceWatchDetailForm(new(examples[1], null), "Testdaten", "de", style,
                () => Task.CompletedTask, () => false, description: longText) { Opacity = 0 };
            dialog.Show();
            dialog.Size = dialog.MinimumSize;
            Application.DoEvents();
            var description = dialog.Controls.Find("PriceWatchDescription", true).Single();
            var source = dialog.Controls.Find("PriceWatchDescriptionSource", true).Single();
            var scroll = dialog.Controls.OfType<Panel>().Single();
            var offers = dialog.Controls.Find("PriceWatchOffers", true).Single();
            scroll.ScrollControlIntoView(offers);
            check(description.Visible && description.Height > description.Font.Height * 4
                && description.Width <= description.MaximumSize.Width
                && source.Text == "Quelle: PriceWatch Offline" && scroll.VerticalScroll.Visible
                && scroll.VerticalScroll.Value > 0 && !scroll.HorizontalScroll.Visible,
                $"long description wraps and the price table remains reachable in {style}");
        }

        using var fallback = new App.PriceWatchDetailForm(new(edition, null), "Testdaten", "de", App.SystemWidgetStyle.Clean,
            () => Task.CompletedTask, () => false, description: App.PriceWatchTestData.Details(edition).Description)
            { Opacity = 0 };
        fallback.Show();
        var about = fallback.Controls.Find("PriceWatchAbout", true).Single();
        check(about.Visible && about.Controls.Cast<Control>().Any(control => control.Text.Contains("Beschreibung des Hauptspiels")),
            "edition fallback text is explicitly labeled");

        var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingCover = new App.PriceWatchCoverSource(async token =>
        {
            readStarted.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { readStopped.TrySetResult(); }
            return Stream.Null;
        });
        using var empty = new App.PriceWatchDetailForm(new(examples[2], null), "Testdaten", "de", App.SystemWidgetStyle.Clean,
            () => Task.CompletedTask, () => false, coverSource: pendingCover) { Opacity = 0 };
        empty.Show();
        Application.DoEvents();
        check(!empty.Controls.Find("PriceWatchAbout", true).Single().Visible, "missing description hides the entire section");
        bool started = readStarted.Task.Wait(TimeSpan.FromSeconds(5));
        empty.Close();
        check(started && readStopped.Task.Wait(TimeSpan.FromSeconds(5)), "closing the detail cancels its cover read");
        Application.DoEvents();
    }

    private static void CheckCoverCredit(Action<bool, string> check)
    {
        using var handler = new CoverHandler();
        using var covers = new App.PriceWatchItadCovers(handler);
        var game = new App.PriceWatchGame(Id, "Cover fixture", "package");
        var info = new App.PriceWatchGameInfo(Id, game.Title, null, [], [],
            new("https://assets.isthereanydeal.com/fixture/cover.png?t=1"));
        using var dialog = new App.PriceWatchDetailForm(new(game, null), "Live", "de", App.SystemWidgetStyle.Clean,
            () => Task.CompletedTask, () => false, info: info, itadCovers: covers) { Opacity = 0 };
        var credit = dialog.Controls.Find("PriceWatchCoverCredit", true).Single();
        var image = (App.PriceWatchCover)dialog.Controls.Find("PriceWatchCover", true).Single();
        bool Pump(Func<bool> done)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (!done() && timer.Elapsed < TimeSpan.FromSeconds(5))
            {
                Application.DoEvents();
                Thread.Sleep(5);
            }
            return done();
        }
        dialog.Show();
        check(Pump(() => credit.Visible) && image.HasImage && credit.Text == "Cover: IsThereAnyDeal",
            "external cover alone enables its visible source credit");
        dialog.PresentInfo(info with { Boxart = new("https://example.invalid/cover.png") });
        check(!credit.Visible && !image.HasImage && handler.Calls == 1,
            "replacing a cover with an invalid URL immediately clears image and attribution");
        handler.Status = HttpStatusCode.NotFound;
        dialog.PresentInfo(info with { Boxart = new("https://assets.isthereanydeal.com/fixture/missing.png") });
        check(Pump(() => handler.Calls == 2) && !credit.Visible && !image.HasImage,
            "missing external cover keeps the local placeholder without attribution");
        dialog.Close();
        var offline = new App.PriceWatchGame(new("10000000-0000-4000-8000-000000000001"), "Cyberpunk 2077", "game");
        var fixture = App.PriceWatchTestData.Details(offline);
        using var local = new App.PriceWatchDetailForm(new(offline, null), "Testdaten", "de", App.SystemWidgetStyle.Glow,
            () => Task.CompletedTask, () => false, info: info, coverSource: fixture.Cover) { Opacity = 0 };
        local.Show();
        var localCover = (App.PriceWatchCover)local.Controls.Find("PriceWatchCover", true).Single();
        check(Pump(() => localCover.HasImage)
            && !local.Controls.Find("PriceWatchCoverCredit", true).Single().Visible && handler.Calls == 2,
            "offline demo cover stays local even when metadata contains an external URL");
    }

    private static void CheckDetailSizing(Action<bool, string> check)
    {
        static bool Proportional(Size size) => Math.Abs(size.Height - size.Width * 580d / 700) <= 0.5;
        var area = new Rectangle(-1800, 70, 1700, 1200);
        foreach (int dpi in new[] { 96, 144, 192 })
        {
            var standard = App.PriceWatchDetailSize.Constrain(700 * dpi / 96d, dpi, new(2000, 1500));
            var small = App.PriceWatchDetailSize.Constrain(1200 * dpi / 96d, dpi, new(670, 420));
            check(standard == new Size(700 * dpi / 96, 580 * dpi / 96)
                && App.PriceWatchDetailSize.ToLogical(standard.Width, dpi) == 700
                && Proportional(small) && small.Width <= 670 && small.Height <= 420,
                $"logical detail size restores and clamps proportionally at {dpi} DPI");
            var proposed = new Rectangle(-1550, 170, 1000, 775);
            for (int edge = 1; edge <= 8; edge++)
            {
                var result = App.PriceWatchDetailSize.Resize(proposed, edge, dpi, area);
                check(Proportional(result.Size) && area.Contains(result)
                    && (edge is 1 or 4 or 7 ? result.Right == proposed.Right : result.Left == proposed.Left)
                    && (edge is 3 or 4 or 5 ? result.Bottom == proposed.Bottom : result.Top == proposed.Top)
                    && (edge is not (1 or 2) || result.Width == proposed.Width)
                    && (edge is not (3 or 6) || result.Height == proposed.Height),
                    $"detail sizing anchors native edge {edge} at {dpi} DPI");
            }
        }

        string registryPath = $@"Software\WallpaperControl.PriceWatchSizingTests\{Guid.NewGuid():N}";
        try
        {
            var fixtures = new App.PriceWatchTestData();
            var game = fixtures.SearchAsync("Ultimate Edition", default).GetAwaiter().GetResult().Data.Single();
            var fixture = App.PriceWatchTestData.Details(game);
            var settings = new App.AppSettingsStore(registryPath);
            settings.SavePriceWatchDetailWidth(-1);
            App.PriceWatchDetailForm Open(App.AppSettingsStore store, bool locked = false)
            {
                var dialog = new App.PriceWatchDetailForm(new(game, null), "Testdaten", "de", App.SystemWidgetStyle.Clean,
                    () => Task.CompletedTask, () => false, refreshInfo: () => Task.CompletedTask,
                    description: fixture.Description, sizeSettings: store, sizeLocked: locked) { Opacity = 0 };
                dialog.Show();
                Application.DoEvents();
                return dialog;
            }
            using var first = Open(settings);
            var work = Screen.FromControl(first).WorkingArea;
            Size standardSize = App.PriceWatchDetailSize.Constrain(700 * first.DeviceDpi / 96d, first.DeviceDpi, work.Size);
            check(first.Size == standardSize && settings.LoadPriceWatchDetailWidth() == -1,
                "invalid saved width falls back without overwriting the stored setting");
            var cover = first.Controls.Find("PriceWatchCover", true).Single();
            int originalWidth = cover.Width;
            float originalFont = first.Font.SizeInPoints;
            SendMessage(first.Handle, 0x0231, 0, 0);
            var rect = Marshal.AllocHGlobal(sizeof(int) * 4);
            try
            {
                Marshal.Copy(new[] { first.Left, first.Top, first.Left + 500 * first.DeviceDpi / 96, first.Bottom }, 0, rect, 4);
                nint handled = SendMessage(first.Handle, 0x0214, 2, rect);
                var values = new int[4];
                Marshal.Copy(rect, values, 0, values.Length);
                first.Bounds = Rectangle.FromLTRB(values[0], values[1], values[2], values[3]);
                SendMessage(first.Handle, 0x0232, 0, 0);
                int actualWidth = App.PriceWatchDetailSize.ToLogical(first.Width, first.DeviceDpi);
                check(handled == 1 && Proportional(first.Size) && settings.LoadPriceWatchDetailWidth() == actualWidth
                    && cover.Width < originalWidth && first.Font.SizeInPoints < originalFont && first.Font.SizeInPoints >= 9,
                    "native resizing scales cover and fonts and saves logical width");
                using var reopened = Open(new App.AppSettingsStore(registryPath));
                check(reopened.Size == first.Size && reopened.Font.SizeInPoints == first.Font.SizeInPoints,
                    "a new detail window restores size from the registry");
                reopened.Width += 10;
                reopened.Close();
                check(settings.LoadPriceWatchDetailWidth() == actualWidth, "programmatic resizing does not save a preference");

                var preferences = App.WidgetSettings.Load(registryPath);
                check(!preferences.PriceWatch.DetailSizeLocked && !preferences.PriceWatch.Locked,
                    "existing settings default to allowing detail resizing");
                preferences.PriceWatch.DetailSizeLocked = true;
                preferences.Save(registryPath);
                var restored = App.WidgetSettings.Load(registryPath);
                check(restored.PriceWatch.DetailSizeLocked && !restored.PriceWatch.Locked
                    && settings.LoadPriceWatchDetailWidth() == actualWidth,
                    "detail size lock persists separately from widget position and preferred width");

                using var locked = Open(settings, restored.PriceWatch.DetailSizeLocked);
                int Hit(Point point)
                {
                    var screen = locked.PointToScreen(point);
                    nint coordinates = (screen.X & 0xffff) | ((screen.Y & 0xffff) << 16);
                    return (int)SendMessage(locked.Handle, 0x0084, 0, coordinates);
                }
                var close = locked.Controls.Find("PriceWatchChromeClose", true).Single();
                check(locked.Size == first.Size && Hit(Point.Empty) == 2
                    && Hit(new(locked.ClientSize.Width - 1, locked.ClientSize.Height - 1)) == 1
                    && Hit(new(close.Left + 2, close.Top + 2)) == 1,
                    "locked detail suppresses edge and corner hits while retaining caption dragging and close");
                Rectangle before = locked.Bounds;
                SendMessage(locked.Handle, 0x0231, 0, 0);
                Marshal.Copy(new[] { before.Left, before.Top, before.Right + 100, before.Bottom + 100 }, 0, rect, 4);
                handled = SendMessage(locked.Handle, 0x0214, 8, rect);
                Marshal.Copy(rect, values, 0, values.Length);
                SendMessage(locked.Handle, 0x0232, 0, 0);
                check(handled == 1 && Rectangle.FromLTRB(values[0], values[1], values[2], values[3]) == before
                    && locked.Bounds == before && settings.LoadPriceWatchDetailWidth() == actualWidth,
                    "locked detail rejects native sizing and preserves the stored width");

                var lockedWorkArea = Screen.FromControl(locked).WorkingArea;
                SendMessage(locked.Handle, 0x0231, 0, 0);
                locked.Width = lockedWorkArea.Width + 100;
                SendMessage(locked.Handle, 0x0232, 0, 0);
                check(lockedWorkArea.Contains(locked.Bounds) && Proportional(locked.Size)
                    && settings.LoadPriceWatchDetailWidth() == actualWidth,
                    "locked detail still fits the work area without replacing the preferred width");
            }
            finally { Marshal.FreeHGlobal(rect); }
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(registryPath, throwOnMissingSubKey: false); }
    }

    private static bool ValidLayout(Bitmap image, Size area, App.PriceWatchWidgetLayout layout)
    {
        var bounds = new RectangleF(0, 0, image.Width / layout.Scale, image.Height / layout.Scale);
        Point Center(RectangleF rect) => new((int)((rect.Left + rect.Width / 2) * layout.Scale),
            (int)((rect.Top + rect.Height / 2) * layout.Scale));
        return image.Width <= area.Width && image.Height <= area.Height && layout.CaptionsFit
            && bounds.Contains(layout.Search) && bounds.Contains(layout.Refresh)
            && !layout.Search.IntersectsWith(layout.Refresh)
            && layout.ActionAt(Center(layout.Search)) == 1 && layout.ActionAt(Center(layout.Refresh)) == 2
            && layout.ActionAt(Point.Empty) == 0 && image.GetPixel(0, 0).A == 0;
    }

    private static void CheckDesign(Action<bool, string> check, App.PriceWatchWidgetForm widget,
        App.PriceWatchWidgetSettings settings)
    {
        foreach (bool dark in new[] { false, true })
        {
            foreach (var state in new[] { (false, false, false), (true, false, false), (true, true, false), (true, true, true) })
            {
                var (text, background) = App.PriceWatchWidgetRenderer.ActionColors(dark, state.Item1, state.Item2, state.Item3);
                check(Contrast(text, background) >= 4.5, $"button caption contrast {dark}/{state}");
            }

            foreach (var style in Enum.GetValues<App.SystemWidgetStyle>())
            {
                settings.Style = style;
                widget.ApplyTheme(dark);
                var (panel, text, muted, accent) = App.PriceWatchWidgetRenderer.ContentColors(dark, style);
                foreach (Color wallpaper in new[] { Color.Black, Color.White })
                {
                    var background = Blend(panel, wallpaper);
                    check(new[] { text, muted, accent }.All(color => Contrast(color, background) >= 4.5),
                        $"widget text keeps contrast over wallpaper {dark}/{style}/{wallpaper.Name}");
                }

                foreach (var language in new[] { "de", "en", "fr", "es", "ja" })
                {
                    bool fits = true;
                    foreach (bool live in new[] { false, true })
                    {
                        settings.Live = live;
                        widget.Apply(settings, language, true);
                        App.PriceWatchRowDisplay BlankRow(int _) => new("Testspiel", "Edition",
            [new("GOG", "", ""), new("Steam", "", "")]);
        Bitmap DrawRows(bool focused, out App.PriceWatchWidgetLayout result) =>
            App.PriceWatchWidgetRenderer.Draw(new(480, 520), 96, App.SystemWidgetStyle.Clean, true, "de",
                "Testdaten", "", "", "Suchen", "Preise abrufen", true, true, 0, 0,
                new App.CalendarViewport(390), 2, BlankRow, out result, keyboardFocus: focused);
        using var resting = DrawRows(false, out var restingLayout);
        using var focused = DrawRows(true, out _);
        int sampleX = (int)restingLayout.Content.Right - 2, sampleY = (int)restingLayout.Content.Top + 2;
        check(resting.GetPixel(sampleX, sampleY) == resting.GetPixel(sampleX, sampleY + App.PriceWatchWidgetRenderer.RowHeight),
            "rows share the same background without persistent selection");
        check(Enumerable.Range(20, 100).Any(x => resting.GetPixel(x, 4) != focused.GetPixel(x, 4)),
            "keyboard focus has a visible outline separate from game rows");

        foreach (int dpi in new[] { 96, 144, 192 })
                        {
                            var area = new Size(480, 300);
                            using var preview = widget.RenderBitmap(area, dpi, out var layout);
                            fits &= ValidLayout(preview, area, layout);
                        }
                    }
                    check(fits, $"all action captions fit at 100%, 150%, 200% in both modes {dark}/{style}/{language}");
                }
            }
        }

        settings.Live = false;
        widget.Apply(settings, "de", false);
        using var compact = widget.RenderBitmap(new(480, 300), 96, out _);
        check(compact.Width == 390 && compact.Height < 176, "watchlist shares the Feed width and collapses when empty");
        check(widget.AccessibilityObject.GetChild(0)?.Role == AccessibleRole.PushButton
            && widget.AccessibilityObject.GetChild(1)?.State == AccessibleStates.Unavailable,
            "canvas actions retain accessible names and disabled state");
        foreach (int dpi in new[] { 96, 144, 192 })
        {
            var area = new Size(480, 300);
            var viewport = new App.CalendarViewport(390);
            const int count = 1200;
            int rowsDrawn = 0;
            App.PriceWatchRowDisplay Row(int index)
            {
                rowsDrawn++;
                return new($"Cyberpunk 2077: Ultimate Edition · {index}", "Paket / Edition",
                    [new("GOG", "40,00 €", "−50 %"), new("Steam", "80,00 €", "0 %")]);
            }
            Bitmap Draw(out App.PriceWatchWidgetLayout layout) =>
                App.PriceWatchWidgetRenderer.Draw(area, dpi, App.SystemWidgetStyle.Glow, true, "de",
                    "Testdaten", "Keine Spiele", "Die Abfrage hat zu lange gedauert.", "Suchen", "Preise abrufen",
                    true, true, 0, 0, viewport, count, Row, out layout);
            using var top = Draw(out var first);
            check(ValidLayout(top, area, first) && viewport.CanScroll && rowsDrawn < 12,
                $"virtualized large list and actions fit at {dpi} DPI");
            check(viewport.Wheel(-120, 3) && viewport.ScrollOffset > 0,
                $"wheel scroll advances the watchlist at {dpi} DPI");
            using var scrolled = Draw(out var middle);
            check(middle.Content.Height % App.PriceWatchWidgetRenderer.RowHeight == 0
                && middle.ScrollOffset % App.PriceWatchWidgetRenderer.RowHeight == 0,
                $"scrolling keeps both offer lines of every visible row at {dpi} DPI");
            check(viewport.DragThumb(viewport.Track.Bottom, 0), $"thumb can reach list end at {dpi} DPI");
            rowsDrawn = 0;
            using var bottom = Draw(out var end);
            Point endPoint = new((int)((end.Content.Left + 8) * end.Scale),
                (int)((end.Content.Bottom - 2) * end.Scale));
            check(ValidLayout(bottom, area, end) && end.RowAt(endPoint) == count - 1 && rowsDrawn < 12,
                $"bottom row is visible and clickable at {dpi} DPI");
            using var small = App.PriceWatchWidgetRenderer.Draw(area, dpi, App.SystemWidgetStyle.Clean, true, "de",
                "Testdaten", "Keine Spiele", "", "Suchen", "Preise abrufen", true, true, 0, 0,
                new App.CalendarViewport(390), 1, Row, out _);
            check(first.Content.Height > App.PriceWatchWidgetRenderer.RowHeight ? small.Height < top.Height : small.Height == top.Height,
                $"watchlist grows only when another whole row fits at {dpi} DPI");
        }
    }

    private sealed class TemporaryWatchlist : IDisposable
    {
        internal string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), $"pricewatch-list-{Guid.NewGuid():N}");
        internal string FilePath => Path.Combine(DirectoryPath, "watchlist.sqlite");
        internal App.PriceWatchStore Store => new(FilePath);
        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true);
        }
    }

    private static void CheckWatchlist(Action<bool, string> check, App.PriceWatchStore store)
    {
        var variants = new App.PriceWatchTestData().SearchAsync("Cyberpunk", default).Result.Data;
        var edition = variants[1];
        store.Migrate(edition, edition with { Title = "Live edition" });
        check(store.Read(false).Single().Game == edition && store.Read(true).Single().Game.Title == "Live edition",
            "legacy test and live selections migrate independently even with identical ITAD UUIDs");
        check(!store.Add(false, edition with { Title = "Duplicate" }) && store.Read(false).Single().Game == edition,
            "duplicate ITAD ID does not replace a saved edition");
        check(store.Add(false, variants[0]) && store.Add(false, variants[2]) && store.Read(false).Length == 3,
            "base game, package and DLC coexist independently");
        var offers = new App.PriceWatchTestData().PricesAsync(edition.Id, default).Result;
        check(store.SaveSnapshot(false, edition.Id, offers)
            && !store.SaveSnapshot(false, edition.Id, new(offers.FetchedAt.AddHours(-1), []))
            && store.Read(false).Single(entry => entry.Game == edition).Snapshot?.Data[0].Deals.Length == 2,
            "SQLite keeps last accepted price snapshot when older data arrives");
        store.Remove(false, edition.Id);
        check(!store.SaveSnapshot(false, edition.Id, offers) && store.Read(true).Single().Game.Id == edition.Id,
            "deleting one mode cannot recreate or remove the corresponding entry in the other mode");
        for (int i = 0; i < 1100; i++)
            store.Add(false, new(Guid.NewGuid(), $"Test game {i}", "game"));
        check(store.Read(false).Length == 1102, "watchlist stores more than a thousand games without an application cap");
    }

    private static void CheckSettingsDesign(Action<bool, string> check)
    {
        foreach (float scale in new[] { 1f, 1.5f, 2f })
        {
            using var editor = new App.WidgetSettingsEditor(new App.WidgetSettings(), dark: true, language: "de");
            editor.SelectWidget("PriceWatchTitle");
            editor.Scale(new SizeF(scale, scale));
            editor.Size = new(900, 520);
            editor.PerformLayout();
            var page = editor.Controls.Cast<Control>().OfType<TabControl>().Single().SelectedTab!;
            var layout = page.Controls.Cast<Control>().OfType<FlowLayoutPanel>().Single();
            layout.PerformLayout();
            var detailLock = (CheckBox)layout.Controls.Find("PriceWatchDetailSizeLocked", false).Single();
            var positionLock = (CheckBox)layout.Controls[layout.Controls.GetChildIndex(detailLock) - 1];
            check(!detailLock.Checked && positionLock.Text == "Position fixieren"
                && detailLock.Text == "Größe des Detailfensters fixieren"
                && detailLock.Top >= positionLock.Bottom && detailLock.Width >= detailLock.PreferredSize.Width,
                $"detail size lock appears below widget position lock at {scale * 100:0}%");
            detailLock.Checked = true;
            var edited = editor.ReadWidgetSettings(applySaveDefaults: true).PriceWatch;
            check(edited.DetailSizeLocked && !edited.Locked, "settings editor reads the independent detail size lock");
            var actions = layout.Controls.Cast<Control>().OfType<Button>().ToArray();
            check(page.AutoScroll && actions.Length == 2
                && actions.All(button => button.GetPreferredSize(Size.Empty).Width <= button.Width
                    && layout.ClientRectangle.Contains(button.Bounds)
                    && Contrast(button.ForeColor, button.BackColor) >= 4.5)
                && !actions[0].Bounds.IntersectsWith(actions[1].Bounds),
                $"settings actions fit and use shared dark control contrast at {scale * 100:0}%");
        }
    }

    private static void CheckOfferPresentation(Action<bool, string> check)
    {
        var data = new App.PriceWatchTestData();
        var edition = data.SearchAsync("Cyberpunk", default).Result.Data[1];
        var snapshot = data.PricesAsync(edition.Id, default).Result;
        var entry = new App.PriceWatchEntry(edition, snapshot);
        var culture = System.Globalization.CultureInfo.GetCultureInfo("de");
        var shops = App.PriceWatchOfferDisplay.Shops;
        check(shops.Select(shop => App.PriceWatchOfferDisplay.Columns(shop,
                App.PriceWatchOfferDisplay.ForShop(entry, shop.Id).Single(), culture, "Kein Angebot"))
            .SequenceEqual(new App.PriceWatchOfferColumns[] { new("GOG", "40,00 €", "−50 %"), new("Steam", "80,00 €", "0 %") }),
            "widget offer cells preserve seller, amount and signed discount");
        check(App.PriceWatchOfferDisplay.Columns(shops[1], null, culture, "Kein Angebot") == new App.PriceWatchOfferColumns("Steam", "Kein Angebot", ""),
            "missing retailer has its own no-offer row");
        var (merchant, price, discount) = App.PriceWatchWidgetRenderer.OfferBounds(47, 90, 155);
        check(merchant.Right < price.Left && price.Right < discount.Left && discount.Right == 202,
            "widget offer cells have independent fixed columns");
        var gog = App.PriceWatchOfferDisplay.ForShop(entry, 35).Single() with
        {
            Platforms = [new(1, "Windows"), new(3, "Mac")],
            Drm = [new(1000, "Drm Free")]
        };
        var steam = App.PriceWatchOfferDisplay.ForShop(entry, 61).Single() with
        {
            Platforms = [new(1, "Windows"), new(3, "Mac")],
            Drm = null
        };
        var enriched = new App.PriceWatchEntry(edition, new(snapshot.FetchedAt, [new(edition.Id, [gog, steam])]));
        using (var details = new App.PriceWatchDetailForm(enriched, "Testdaten", "de", App.SystemWidgetStyle.Clean,
            () => Task.CompletedTask, () => false, shopLinksEnabled: true))
        {
            var table = (TableLayoutPanel)details.Controls.Find("PriceWatchOffers", true).Single();
            var gogInfo = (Label)table.GetControlFromPosition(0, 2)!;
            var steamInfo = (Label)table.GetControlFromPosition(0, 4)!;
            check(table.GetControlFromPosition(0, 1) is LinkLabel { Text: "GOG" }
                && table.GetControlFromPosition(0, 3) is LinkLabel { Text: "Steam" }
                && gogInfo.Text == "Windows, Mac · DRM-frei" && steamInfo.Text == "Windows, Mac"
                && table.GetColumnSpan(gogInfo) == 3
                && table.GetColumnSpan(steamInfo) == 3, "metadata stays below its exact offer and merchant links are kept");
            check(!details.Controls.Find("PriceWatchAbout", true).Single().Visible,
                "unavailable game description has no visible section");
            var drmOnly = gog with { Platforms = null, Drm = [new(500, "Custom DRM")] };
            details.Present(new(edition, new(snapshot.FetchedAt, [new(edition.Id, [drmOnly])])), "");
            check(table.GetControlFromPosition(0, 2)?.Text == "Custom DRM"
                && details.Controls.Find("PriceWatchOfferInfo", true).Length == 1,
                "unknown DRM names remain unchanged and missing metadata adds no labels or separators");
            var longOffer = gog with
            {
                Platforms = Enumerable.Range(1, 20).Select(i => new App.PriceWatchOfferTag(i, $"Plattform {i}")).ToArray(),
                Drm = null
            };
            details.Present(new(edition, new(snapshot.FetchedAt, [new(edition.Id, [longOffer])])), "");
            details.Size = new(305, 170);
            details.PerformLayout();
            table.PerformLayout();
            var longInfo = (Label)table.GetControlFromPosition(0, 2)!;
            check(!longInfo.Text.Contains("Aktivierung") && longInfo.Text.Contains("Plattform 20")
                && longInfo.GetPreferredSize(new(longInfo.MaximumSize.Width, 0)).Height > longInfo.Font.Height * 2
                && details.Controls.OfType<Panel>().Any(panel => panel.AutoScroll),
                "long per-offer metadata wraps and the outer dialog can scroll");
        }

        foreach (var style in Enum.GetValues<App.SystemWidgetStyle>())
        foreach (float scale in new[] { 1f, 1.5f, 2f })
        {
            var onlyGog = new App.PriceWatchReply<App.PriceWatchPrices[]>(snapshot.FetchedAt,
                [new(edition.Id, [App.PriceWatchOfferDisplay.ForShop(entry, 35).Single()])]);
            using var details = new App.PriceWatchDetailForm(new(
                    edition with { Title = string.Concat(Enumerable.Repeat(edition.Title + " ", 4)).TrimEnd() }, onlyGog),
                "Testdaten", "de", style, () => Task.CompletedTask, () => false);
            details.Scale(new SizeF(scale, scale));
            details.Size = new Size((int)(330 * scale), (int)(245 * scale));
            details.PerformLayout();
            var table = (TableLayoutPanel)details.Controls.Find("PriceWatchOffers", true).Single();
            table.PerformLayout();
            check(table.RowCount == 3 && table.GetControlFromPosition(0, 1)?.Text == "GOG"
                && table.GetControlFromPosition(1, 1)?.Text == "40,00 €"
                && table.GetControlFromPosition(2, 1)?.Text == "−50 %"
                && table.GetControlFromPosition(0, 2)?.Text == "Steam"
                && table.GetControlFromPosition(1, 2)?.Text == "Kein Angebot"
                && table.Controls.Cast<Control>().All(cell => table.ClientRectangle.Contains(cell.Bounds)),
                $"detail offer columns and empty offer fit in {style} at {scale * 100:0}%");
        }
    }

    private static void CheckShopLinksAndChrome(Action<bool, string> check)
    {
        check(App.PriceWatchShopLinks.Validate(new Uri(OfferUrl))?.OriginalString == OfferUrl
            && App.PriceWatchShopLinks.Validate(new Uri("http://offers.example.invalid/game")) != null
            && App.PriceWatchShopLinks.Validate(new Uri("/game", UriKind.Relative)) == null
            && App.PriceWatchShopLinks.Validate(new Uri("file:///C:/game.exe")) == null
            && App.PriceWatchShopLinks.Validate(new Uri("javascript:alert(1)")) == null
            && App.PriceWatchShopLinks.Validate(new Uri("https://name@example.invalid/")) == null,
            "offer links accept only absolute HTTP(S) without credentials and keep the original URL");

        foreach (int scale in new[] { 1, 2 })
        {
            var area = new Size(400 * scale, 250 * scale);
            var close = new Rectangle(362 * scale, 6 * scale, 30 * scale, 23 * scale);
            int Hit(int x, int y) => App.PriceWatchDetailForm.HitZone(area, new(x * scale, y * scale),
                6 * scale, 35 * scale, close);
            check(Hit(2, 2) == 13 && Hit(200, 2) == 12 && Hit(398, 2) == 14
                && Hit(2, 100) == 10 && Hit(398, 100) == 11
                && Hit(2, 248) == 16 && Hit(200, 248) == 15 && Hit(398, 248) == 17
                && Hit(60, 16) == 2 && Hit(376, 18) == 1 && Hit(200, 110) == 1,
                $"borderless frame hits all edges, caption and close control at {scale * 100}%");
        }
        var work = new Rectangle(-1920, -100, 1280, 720);
        check(App.PriceWatchDetailForm.FitBounds(new(-2400, -200, 1700, 900), work) == work
            && work.Contains(App.PriceWatchDetailForm.FitBounds(new(-700, 600, 500, 300), work)),
            "borderless window bounds stay in a small work area with a negative monitor origin");

        var fixtures = new App.PriceWatchTestData();
        var game = fixtures.SearchAsync("Cyberpunk", default).Result.Data[1];
        var sample = fixtures.PricesAsync(game.Id, default).Result;
        var offer = sample.Data[0].Deals[0] with { Url = new(OfferUrl) };
        var snapshot = new App.PriceWatchReply<App.PriceWatchPrices[]>(sample.FetchedAt, [new(game.Id, [offer])]);
        var entry = new App.PriceWatchEntry(game, snapshot);
        var opened = new List<string>();
        using var details = new App.PriceWatchDetailForm(entry, "Live", "de", App.SystemWidgetStyle.Glow,
            () => Task.CompletedTask, () => false, shopLinksEnabled: true, openLink: u => opened.Add(u.OriginalString));
        var table = (TableLayoutPanel)details.Controls.Find("PriceWatchOffers", true).Single();
        var link = (LinkLabel)table.GetControlFromPosition(0, 1)!;
        typeof(LinkLabel).GetMethod("OnLinkClicked", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(link, [new LinkLabelLinkClickedEventArgs(link.Links[0])]);
        check(details.FormBorderStyle == FormBorderStyle.None && details.CancelButton != null
            && details.Controls.Find("PriceWatchChromeClose", true).Single() is Button
            && link.TabStop && table.GetControlFromPosition(0, 2) is not LinkLabel
            && opened.SequenceEqual([OfferUrl]),
            "borderless detail keeps Esc, top close, keyboard link and a plain missing-offer merchant");

        using var storage = new TemporaryWatchlist();
        storage.Store.Add(true, game);
        storage.Store.SaveSnapshot(true, game.Id, snapshot);
        check(storage.Store.Read(true).Single().Snapshot!.Data[0].Deals[0].Url.OriginalString == OfferUrl,
            "cached SQLite snapshot retains original offer URL");
        var settings = new App.PriceWatchWidgetSettings { Live = true, Locked = true };
        using var widget = new App.PriceWatchWidgetForm(_ => throw new InvalidOperationException("Price source was called"),
            settings, "de", _ => { }, storage.Store, u => opened.Add(u.OriginalString));
        widget.FitToScreen(new(30, 30));
        widget.Show();
        const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
        var layout = (App.PriceWatchWidgetLayout)typeof(App.PriceWatchWidgetForm).GetField("layout", members)!.GetValue(widget)!;
        bool missingIsPlain = ClickMerchant(1) == Cursors.Default;
        bool activeUsesHand = ClickMerchant(0) == Cursors.Hand;
        check(missingIsPlain && activeUsesHand && opened.SequenceEqual([OfferUrl, OfferUrl]),
            "only the widget merchant with an offer uses the hand cursor and opens its exact URL");
        widget.Apply(settings, "de", true);
        ClickMerchant(0);
        check(opened.Count == 2, "widget preview blocks browser links");

        Cursor ClickMerchant(int shop)
        {
            var bounds = App.PriceWatchWidgetRenderer.RowOfferBounds(layout.Content, layout.Content.Top, shop).Merchant;
            var point = new Point((int)((bounds.X + 4) * layout.Scale), (int)((bounds.Y + 8) * layout.Scale));
            nint coordinates = (point.X & 0xffff) | ((point.Y & 0xffff) << 16);
            SendMessage(widget.Handle, 0x0200, 0, coordinates);
            var cursor = widget.Cursor;
            SendMessage(widget.Handle, 0x0201, 1, coordinates);
            SendMessage(widget.Handle, 0x0202, 0, coordinates);
            return cursor;
        }
    }

    private static void CheckGameInfoStorageAndPresentation(Action<bool, string> check)
    {
        using var storage = new TemporaryWatchlist();
        Directory.CreateDirectory(storage.DirectoryPath);
        using (var old = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={storage.FilePath};Pooling=False"))
        {
            old.Open();
            using var command = old.CreateCommand();
            command.CommandText = """
                CREATE TABLE watched (live INTEGER NOT NULL, id TEXT NOT NULL, title TEXT NOT NULL,
                    kind TEXT, snapshot TEXT, fetched_ticks INTEGER, PRIMARY KEY (live, id));
                INSERT INTO watched (live, id, title, kind) VALUES (0, $id, 'Legacy fixture title', 'package');
                """;
            command.Parameters.AddWithValue("$id", Id.ToString("D"));
            command.ExecuteNonQuery();
        }

        var store = storage.Store;
        var entry = store.Read(false).Single();
        check(entry.Game.Title == "Legacy fixture title" && store.ReadInfo(false, Id) == null,
            "opening the pre-metadata SQLite schema preserves the watchlist");
        var info = new App.PriceWatchGameInfo(Id, "Full fixture title", new(2024, 2, 29),
            ["CD PROJEKT RED", "CD Project Red", "CD Projekt Red", "Second developer"],
            ["CD Project Red", "CD PROJEKT RED"], new("https://assets.example.invalid/example.jpg"));
        var fetched = new App.PriceWatchReply<App.PriceWatchGameInfo>(DateTimeOffset.UtcNow, info);
        check(store.SaveInfo(false, Id, fetched) && !store.SaveInfo(true, Id, fetched)
            && !store.SaveInfo(false, Id, fetched with { FetchedAt = fetched.FetchedAt.AddHours(-1) })
            && storage.Store.ReadInfo(false, Id)?.Data.Developers.SequenceEqual(info.Developers) == true,
            "metadata persists by mode and older records cannot overwrite it");
        using var details = new App.PriceWatchDetailForm(entry, "Testdaten", "de", App.SystemWidgetStyle.Glow,
            () => Task.CompletedTask, () => false, info: info, refreshInfo: () => Task.CompletedTask);
        var group = details.Controls.Find("PriceWatchGameInfo", true).Single();
        var labelText = group.Text;
        details.PresentInfo(null, "fixture connection error");
        check(group.Text == labelText && labelText == string.Join(Environment.NewLine,
            "Veröffentlicht: 29.02.2024", "Entwickler: CD PROJEKT RED, Second developer", "Publisher: CD Project Red"),
            "detail formats a compact date and deduplicates names independently in their original order");
        check(store.ReadInfo(false, Id)?.Data.Developers.SequenceEqual(info.Developers) == true
            && store.ReadInfo(false, Id)?.Data.Publishers.SequenceEqual(info.Publishers) == true,
            "display formatting leaves stored company arrays intact");
        details.Opacity = 0;
        details.Show();
        Application.DoEvents();
        var updateButton = details.Controls.Find("PriceWatchRefreshInfo", true).Single();
        var actions = updateButton.Parent!;
        actions.PerformLayout();
        var buttons = actions.Controls.OfType<Button>().ToArray();
        check(buttons.Select(button => button.Text).SequenceEqual(
                ["Preise abrufen", "Spielinfos aktualisieren", "Entfernen", "Schließen"])
            && buttons.Select(button => button.Top).Distinct().Count() == 1
            && buttons.All(button => button.Width >= button.GetPreferredSize(Size.Empty).Width
                && actions.ClientRectangle.Contains(button.Bounds)),
            "shown detail fits all four unabridged German action buttons on one row");
        details.Hide();
        details.PresentInfo(info with { ReleaseDate = null, Publishers = [] });
        check(group.Text == "Entwickler: CD PROJEKT RED, Second developer", "missing game info fields are omitted");
        store.Remove(false, Id);
        check(storage.Store.ReadInfo(false, Id) == null, "removing the watchlist entry removes related metadata");
    }

    private static void CheckDesktopTrayLifecycle(Action<bool, string> check)
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
        static T Get<T>(object target, string name) =>
            (T)target.GetType().GetField(name, members)!.GetValue(target)!;
        static object? Invoke(object target, string name, params object[] args) =>
            target.GetType().GetMethod(name, members)!.Invoke(target, args);

        var path = @"Software\WallpaperControl\Regression\PriceWatchTray-" + Guid.NewGuid().ToString("N");
        try
        {
            var settings = new App.WidgetSettings { ClockLanguageCode = "de" };
            settings.PriceWatch.Enabled = true;
            settings.Save(path);
            using var storage = new TemporaryWatchlist();
            using var manager = new App.WidgetManager(() => { }, registryPath: path);
            typeof(App.WidgetManager).GetField("priceWatchStore", members)!.SetValue(manager, storage.Store);
            using var main = new App.MainForm(manager);
            manager.Start();
            main.Opacity = 0;
            main.Show();
            var widget = Get<App.PriceWatchWidgetForm>(manager, "priceWatchWidget");
            var session = Get<App.WidgetEditSession>(main, "widgetEditSession");
            var editor = Get<App.WidgetSettingsEditor>(main, "widgetEditor");
            var search = widget.AccessibilityObject.GetChild(0)!;
            var prices = widget.AccessibilityObject.GetChild(1)!;
            bool Enabled(AccessibleObject control) => !control.State.HasFlag(AccessibleStates.Unavailable);
            check(widget.Enabled && Enabled(search) && !Enabled(prices), "desktop startup enables search only");

            Invoke(main, "SelectMainSection", true);
            check(session.Active && !Enabled(search) && !Enabled(prices), "visible editor suppresses price and search actions");
            check(ClickSearch(select: false) == null, "preview click opens no dialog");
            check(session.Save() && !Enabled(search), "save keeps preview active while editor is visible");

            // The real close-to-tray path resolves the session before hiding the main form.
            check(Invoke(main, "ResolveWidgetNavigation") is true, "clean editor exit needs no prompt");
            Invoke(main, "HideMainWindowToTray");
            check(!main.Visible && !session.Active && widget.Enabled && Enabled(search) && !Enabled(prices),
                "close to tray resumes desktop search with no selected edition");
            check(ClickSearch(select: true) == Id, "desktop mouse down/up opens offline search and confirms the edition");
            check(Enabled(prices) && storage.Store.Read(false).Single().Game.Id == Id,
                "desktop price action enables and stores only after explicit edition selection");
            Click(prices);
            check(storage.Store.Read(false).Single().Snapshot?.Data.Single().Deals.Length == 2,
                "desktop price click persists both offline offers");
            bool detailOpened = false, nativeChrome = false, activationHandled = false, escapeHandled = false;
            widget.BeginInvoke(new Action(() =>
            {
                var details = Application.OpenForms.OfType<App.PriceWatchDetailForm>().SingleOrDefault();
                if (details == null) return;
                var offers = (TableLayoutPanel)details.Controls.Find("PriceWatchOffers", true).Single();
                detailOpened = offers.RowCount == 3 && offers.ColumnCount == 3
                    && offers.GetControlFromPosition(0, 1)?.Text == "GOG"
                    && offers.GetControlFromPosition(0, 2)?.Text == "Steam"
                    && offers.GetControlFromPosition(2, 1)?.Text == "−50 %";
                var close = (Button)details.Controls.Find("PriceWatchChromeClose", true).Single();
                int NativeHit(Point local)
                {
                    var point = details.PointToScreen(local);
                    nint coordinates = (point.X & 0xffff) | ((point.Y & 0xffff) << 16);
                    return (int)SendMessage(details.Handle, 0x0084, 0, coordinates);
                }
                activationHandled = SendMessage(details.Handle, 0x0086, 0, 0) != 0;
                SendMessage(details.Handle, 0x0086, 1, 0);
                nativeChrome = NativeHit(new(0, 0)) == 13
                    && NativeHit(new(close.Left / 2, close.Top + 2)) == 2
                    && NativeHit(new(close.Left + 2, close.Top + 2)) == 1;
                escapeHandled = (bool)typeof(Form).GetMethod("ProcessDialogKey", members)!
                    .Invoke(details, [Keys.Escape])!;
                if (!escapeHandled) details.Close();
            }));
            var widgetLayout = Get<App.PriceWatchWidgetLayout>(widget, "layout");
            ClickAt(new((int)((widgetLayout.Content.Left + 14) * widgetLayout.Scale),
                (int)((widgetLayout.Content.Top + 12) * widgetLayout.Scale)));
            Application.DoEvents();
            check(detailOpened && storage.Store.ReadInfo(false, Id)?.Data.Developers.Length == 1,
                "opening details fetches and caches offline game info without another price request");
            check(nativeChrome && activationHandled && escapeHandled,
                "shown details handle focus loss and return, native resize and drag, and Escape");
            var cachedAt = storage.Store.ReadInfo(false, Id)?.FetchedAt;
            bool cachedInfoVisible = false;
            widget.BeginInvoke(new Action(() =>
            {
                var details = Application.OpenForms.OfType<App.PriceWatchDetailForm>().SingleOrDefault();
                if (details == null) return;
                cachedInfoVisible = details.Controls.Find("PriceWatchGameInfo", true).Single().Text.Contains("Example developer");
                details.Close();
            }));
            ClickAt(new((int)((widgetLayout.Content.Left + 14) * widgetLayout.Scale),
                (int)((widgetLayout.Content.Top + 12) * widgetLayout.Scale)));
            Application.DoEvents();
            check(cachedInfoVisible && cachedAt != null && storage.Store.ReadInfo(false, Id)?.FetchedAt == cachedAt,
                "reopening details uses SQLite game info without fetching it again");


            Invoke(main, "RestoreFromTray");
            Application.DoEvents();
            check(main.Visible && session.Active && !Enabled(search) && !Enabled(prices),
                "tray restore resumes the visible widget editor preview");
            main.WindowState = FormWindowState.Minimized;
            Application.DoEvents();
            check(!main.Visible && !session.Active && Enabled(search) && Enabled(prices),
                "minimizing a saved editor also restores desktop interaction");

            Invoke(main, "RestoreFromTray");
            Application.DoEvents();
            Get<CheckBox>(editor, "priceWatchLive").Checked = true;
            main.WindowState = FormWindowState.Minimized;
            Application.DoEvents();
            check(session.Active && session.IsDirty && !Enabled(search) && !Enabled(prices)
                && !App.WidgetSettings.Load(path).PriceWatch.Live,
                "minimizing an unsaved live-mode draft keeps the draft and blocks requests");

            Guid? ClickSearch(bool select)
            {
                Guid? found = null;
                bool dialogOpened = false;
                widget.BeginInvoke(new Action(() =>
                {
                    var dialog = Application.OpenForms.OfType<App.PriceWatchSearchForm>().SingleOrDefault();
                    if (dialog == null) return;
                    dialogOpened = dialog.Visible && dialog.Text.EndsWith(widget.ModeCaption, StringComparison.Ordinal);
                    if (select)
                    {
                        Control table = dialog.Controls[0];
                        table.Controls.OfType<TextBox>().Single().Text = "Cyberpunk 2077";
                        table.Controls.OfType<Button>().Single().PerformClick();
                        table.Controls.OfType<ListBox>().Single().SelectedIndex = 1;
                        table.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>()
                            .Single(button => button.DialogResult != DialogResult.Cancel).PerformClick();
                        found = dialog.SelectedGame?.Id;
                    }
                    else dialog.Close();
                }));
                Click(search);
                Application.DoEvents();
                return select && dialogOpened ? found : dialogOpened ? Guid.Empty : null;
            }

            void Click(AccessibleObject action)
            {
                var bounds = action.Bounds;
                Point point = widget.PointToClient(new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2));
                ClickAt(point);
            }

            void ClickAt(Point point)
            {
                nint coordinates = (point.X & 0xffff) | ((point.Y & 0xffff) << 16);
                SendMessage(widget.Handle, 0x0201, 1, coordinates);
                SendMessage(widget.Handle, 0x0202, 0, coordinates);
            }
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, false); }
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nint state, nint coordinates);

    private static Color Blend(Color fore, Color background)
    {
        int Mix(int a, int b) => (a * fore.A + b * (255 - fore.A)) / 255;
        return Color.FromArgb(Mix(fore.R, background.R), Mix(fore.G, background.G), Mix(fore.B, background.B));
    }

    private static double Contrast(Color left, Color right)
    {
        static double Luminance(Color color)
        {
            static double Linear(byte channel)
            {
                double value = channel / 255d;
                return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        }

        double a = Luminance(left) + 0.05, b = Luminance(right) + 0.05;
        return Math.Max(a, b) / Math.Min(a, b);
    }

    private sealed class Handler : HttpMessageHandler
    {
        internal object? Data;
        internal bool Invalid, Delay, SecureOrigin, BasicOnly;
        internal int Calls;
        internal string? Path;
        internal JsonElement Body;
        internal HttpStatusCode Status = HttpStatusCode.OK;
        internal RetryConditionHeaderValue? Retry;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            Path = request.RequestUri!.AbsolutePath;
            SecureOrigin = request.Method == HttpMethod.Post && request.RequestUri is { Scheme: "https", Host: "api.nightmahr.de" };
            BasicOnly = request.Headers.Authorization?.Scheme == "Basic" && !request.Headers.Contains("ITAD-API-Key");
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Body = json.RootElement.Clone();
            if (Delay) await Task.Delay(Timeout.Infinite, token);
            string response = Invalid ? "invalid-json" : JsonSerializer.Serialize(new
            {
                source = "IsThereAnyDeal", fetchedAt = DateTimeOffset.UtcNow, data = Data
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return new HttpResponseMessage(Status)
            {
                Headers = { RetryAfter = Retry }, Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
    }
}
