extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

internal static class PriceWatchShopTests
{
    private static readonly Guid Id = new("10000000-0000-4000-8000-000000000001");
    private static int[] All => App.PriceWatchShops.Supported.Select(shop => shop.Id).ToArray();

    internal static async Task RunAsync(Action<bool, string> check)
    {
        var defaults = new App.PriceWatchWidgetSettings();
        var legacy = App.PriceWatchWidgetSettings.Parse("{\"Enabled\":true}");
        check(defaults.ShopIds.SequenceEqual(new[] { 35, 61, 16 }) && legacy.ShopIds.SequenceEqual(new[] { 35, 61 }),
            "new installations use three shops and existing configurations retain their two shops");
        var normalized = App.PriceWatchWidgetSettings.Parse("{\"ShopIds\":[62,62,999,35]}");
        check(normalized.ShopIds.SequenceEqual(new[] { 62, 35 })
            && App.PriceWatchWidgetSettings.Parse("{\"ShopIds\":[]}").ShopIds.Length > 0
            && App.PriceWatchWidgetSettings.Parse("null").ShopIds.Length > 0,
            "shop settings normalize unsupported and duplicate IDs and always retain a shop");
        var clone = normalized.Clone();
        clone.ShopIds[0] = 16;
        check(normalized.ShopIds[0] == 62, "shop settings clones own their ID arrays");

        var data = new App.PriceWatchTestData();
        var reply = await data.PricesAsync(Id, All, default);
        check(reply.Data.Single().Deals.Select(offer => offer.Shop.Id).SequenceEqual(All),
            "offline data supplies all seven shops without a network source");
        var entry = new App.PriceWatchEntry(new(Id, "Fixture", "game"), reply, 2000);
        int[] order = [61, 27, 35, 16, 62, 48, 52];
        var top = App.PriceWatchShops.WidgetOffers(entry, order, DateTimeOffset.UtcNow);
        check(top.Select(offer => offer.Shop.Id).SequenceEqual(new[] { 16, 27, 35 }),
            "widget selects the cheapest three shops with configured tie order");
        var two = App.PriceWatchShops.WidgetOffers(entry, [35, 61], DateTimeOffset.UtcNow);
        check(two.Length == 2 && App.PriceWatchShops.WidgetOffers(entry, [52], DateTimeOffset.UtcNow).Length == 1,
            "widget omits inactive shops and supports fewer than three offers");
        var epic = reply.Data.Single().Deals.Single(offer => offer.Shop.Id == 16);
        check(App.PriceWatchTargetPrice.IsReached(entry, epic, DateTimeOffset.UtcNow, [16])
            && !App.PriceWatchTargetPrice.IsReached(entry, epic, DateTimeOffset.UtcNow, [61]),
            "only active merchants can meet the shared target");
        var invalid = reply.Data.Single().Deals.Select(offer => offer with { Expiry = DateTimeOffset.UtcNow.AddMinutes(-1) }).ToArray();
        invalid[0] = invalid[0] with { Expiry = null, Price = invalid[0].Price with { Currency = "USD" } };
        invalid[1] = invalid[1] with { Expiry = null, Price = invalid[1].Price with { AmountInt = -1 } };
        var invalidEntry = entry with { Snapshot = reply with { Data = [new(Id, invalid)] } };
        check(App.PriceWatchShops.WidgetOffers(invalidEntry, All, DateTimeOffset.UtcNow).Length == 0,
            "expired, foreign-currency and invalid offers are absent from the widget");

        using var handler = new ShopHandler();
        using var http = new HttpClient(handler);
        var client = new App.PriceWatchClient(http, () => new App.TrackingCredential("fixture:fixture"));
        handler.Data = reply.Data;
        var live = await client.PricesAsync(Id, All, default);
        check(handler.Selected.SequenceEqual(All) && live.Data.Single().Deals.Length == 7,
            "typed prices client passes the selected seven-shop list unchanged");
        handler.Data = new[] { new App.PriceWatchHistory(new(16, "Epic Game Store"), DateTimeOffset.UtcNow, null) };
        await client.HistoryAsync(Id, 30, [16], default);
        check(handler.Selected.SequenceEqual(new[] { 16 }), "history client uses the current shop selection");
        int calls = handler.Calls;
        foreach (int[] ids in new[] { new[] { 999 }, Array.Empty<int>() })
        {
            bool rejected = false;
            try { await client.PricesAsync(Id, ids, default); }
            catch (App.PriceWatchException e) { rejected = e.Failure == App.PriceWatchFailure.InvalidRequest; }
            check(rejected && handler.Calls == calls, "invalid shop selections are rejected before HTTP");
        }
        handler.Data = reply.Data;
        bool extraRejected = false;
        try { await client.PricesAsync(Id, [16], default); }
        catch (App.PriceWatchException e) { extraRejected = e.Failure == App.PriceWatchFailure.InvalidResponse; }
        check(extraRejected, "client rejects unrequested merchants in the upstream response");
    }

    internal static void RunUi(Action<bool, string> check)
    {
        string path = $@"Software\WallpaperControl\Regression\Phase13-{Guid.NewGuid():N}";
        string directory = Path.Combine(Path.GetTempPath(), $"pricewatch-shops-{Guid.NewGuid():N}");
        try
        {
            check(App.WidgetSettings.Load(path).PriceWatch.ShopIds.SequenceEqual(new[] { 35, 61, 16 }),
                "a missing registry configuration preserves the new-install defaults");
            using (var key = Registry.CurrentUser.CreateSubKey(path))
                key.SetValue("PriceWatchWidgetConfiguration", "{\"Enabled\":true}");
            check(App.WidgetSettings.Load(path).PriceWatch.ShopIds.SequenceEqual(new[] { 35, 61 }),
                "registry loading migrates existing two-shop installations without enabling Epic");
            var settings = App.WidgetSettings.Load(path);
            settings.PriceWatch.ShopIds = [62, 35];
            settings.Save(path);
            check(App.WidgetSettings.Load(path).PriceWatch.ShopIds.SequenceEqual(new[] { 62, 35 }),
                "shop selection and order persist through the existing settings store");
            using (var editor = new App.WidgetSettingsEditor(settings, dark: true, language: "de"))
            {
                editor.SelectWidget("PriceWatchTitle");
                var boxes = All.Select(id => (CheckBox)editor.Controls.Find($"PriceWatchShop{id}", true).Single()).ToArray();
                check(boxes.Count(box => box.Checked) == 2, "all seven shop checkboxes load the saved selection");
                foreach (var box in boxes) box.Checked = false;
                check(boxes.Count(box => box.Checked) == 1 && editor.ReadWidgetSettings(false).PriceWatch.ShopIds.Length == 1,
                    "settings UI prevents disabling the final selected shop");
            }
            var source = new DeferredSource();
            var store = new App.PriceWatchStore(Path.Combine(directory, "watchlist.sqlite"));
            var game = new App.PriceWatchGame(Id, "Cyberpunk 2077", "game");
            store.Add(false, game);
            store.SaveTarget(false, Id, 2000);
            var data = new App.PriceWatchTestData();
            store.SaveSnapshot(false, Id, data.PricesAsync(Id, All, default).Result);
            using var widget = new App.PriceWatchWidgetForm(_ => source,
                new() { Enabled = true, ShopIds = [35, 61] }, "de", _ => { }, store);
            widget.Show();
            Application.DoEvents();
            check(source.Calls == 0, "starting a widget with a stored selection makes no automatic price request");
            source.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var initial = widget.LoadNewEntryAsync(false, Id);
            Pump(() => source.Calls == 1);
            var before = store.Read(false).Single().Snapshot!.FetchedAt;
            widget.Apply(new() { Enabled = true, ShopIds = [16] }, "de", true);
            check(source.Calls == 1 && store.Read(false).Single().Snapshot!.FetchedAt == before,
                "editing shop selection filters preview without clearing snapshots or issuing requests");
            widget.Apply(new() { Enabled = true, ShopIds = [16] }, "de", false);
            source.Gate.SetResult();
            Pump(() => source.Calls >= 2 && store.Read(false).Single().Snapshot!.Data.Single().Deals.All(offer => offer.Shop.Id == 16));
            initial.GetAwaiter().GetResult();
            check(source.Requests[0].SequenceEqual(new[] { 35, 61 }) && source.Requests[^1].SequenceEqual(new[] { 16 })
                && source.MaximumConcurrent == 1 && store.Read(false).Single().TargetCents == 2000,
                "selection changes replace cancelled responses without concurrent duplicate loads or losing targets");
            CheckActionSurface(widget, check, "after initial data and selection refresh");
            CheckGrowingActionSurface(check, directory);
            CheckAddingThroughSearch(check, directory);
            int priorCalls = source.Calls;
            source.Gate = null;
            source.Fail = true;
            var priorSnapshot = store.Read(false).Single().Snapshot;
            widget.Apply(new() { Enabled = true, ShopIds = [62] }, "de", false);
            Pump(() => source.Calls > priorCalls);
            check(store.Read(false).Single().Snapshot!.FetchedAt == priorSnapshot!.FetchedAt,
                "failed selection refresh preserves the old snapshot while the display filters inactive shops");
            source.Fail = false;
            priorCalls = source.Calls;
            widget.Apply(new() { Enabled = true, ShopIds = [35] }, "de", true);
            widget.Apply(new() { Enabled = true, ShopIds = [62] }, "de", false);
            Application.DoEvents();
            check(source.Calls == priorCalls, "cancelling a preview selection does not request the saved selection again");

            using (var recreated = new App.PriceWatchWidgetForm(_ => source,
                new() { Enabled = true, ShopIds = [52] }, "de", _ => { }, store))
            {
                recreated.RestoreShopRefreshBaseline([62]);
                recreated.Show();
                Pump(() => source.Calls > priorCalls && store.Read(false).Single().Snapshot!.Data.Single().Deals
                    .All(offer => offer.Shop.Id == 52));
                check(source.Requests[^1].SequenceEqual(new[] { 52 }),
                    "a selection changed while the widget was disabled refreshes on reactivation");
            }

            var full = data.PricesAsync(Id, All, default).Result;
            foreach (var style in Enum.GetValues<App.SystemWidgetStyle>())
            {
                using var details = new App.PriceWatchDetailForm(new(game, full, 2000), "Testdaten", "de", style,
                    () => Task.CompletedTask, () => false, shops: All, saveTarget: _ => true) { Opacity = 0 };
                details.Show();
                details.Size = details.MinimumSize;
                Application.DoEvents();
                var table = (TableLayoutPanel)details.Controls.Find("PriceWatchOffers", true).Single();
                check(table.RowCount == 8 && All.Select((id, i) => table.GetControlFromPosition(0, i + 1)!.Text)
                    .SequenceEqual(App.PriceWatchShops.Supported.Select(shop => shop.Name))
                    && details.Controls.OfType<Panel>().Single().VerticalScroll.Visible,
                    $"seven active shops remain available in a scrollable {style} detail window");
            }
            foreach (int dpi in new[] { 96, 144, 192 })
            foreach (var style in Enum.GetValues<App.SystemWidgetStyle>())
            {
                App.PriceWatchRowDisplay Row(int row) => new("Long title for shop layout", "Grundspiel",
                    Enumerable.Range(0, row + 1).Select(i => new App.PriceWatchOfferColumns($"Shop {i}", "29,99 €", "−50 %")).ToArray(),
                    Target: "Wunsch: 29,99 €");
                using var bitmap = App.PriceWatchWidgetRenderer.Draw(new(1200, 900), dpi, style, true, "de",
                    "Testdaten", "", "", "Suchen", "Preise abrufen", true, true, 0, 0,
                    new App.CalendarViewport(390), 3, Row, out var layout);
                bool mapped = true;
                for (int row = 0; row < 3; row++)
                for (int shop = 0; shop <= row; shop++)
                {
                    var bounds = App.PriceWatchWidgetRenderer.RowOfferBounds(layout.Content, layout.RowTop(row), shop).Merchant;
                    var point = new Point((int)((bounds.Left + 3) * layout.Scale), (int)((bounds.Top + 8) * layout.Scale));
                    mapped &= layout.RowAt(point) == row && layout.MerchantAt(point, row) == shop;
                }
                check(mapped && layout.RowOffset(1) == 85 && layout.RowOffset(2) == 179,
                    $"variable row height and merchant hit testing agree in {style} at {dpi} DPI");
                var compactViewport = new App.CalendarViewport(390);
                using var compact = App.PriceWatchWidgetRenderer.Draw(new(1200, 900), dpi, style, true, "de",
                    "Testdaten", "", "", "Suchen", "Preise abrufen", true, true, 0, 0,
                    compactViewport, 1, Row, out _);
                check(!compactViewport.CanScroll, $"one-offer widget avoids a spare scrollbar in {style} at {dpi} DPI");
            }
            var current = data.PricesAsync(Id, All, default).Result;
            current = current with
            {
                Data = [new(Id, current.Data.Single().Deals.Select(offer =>
                    offer with { Url = new($"https://fixture.invalid/offer/{offer.Shop.Id}") }).ToArray())]
            };
            store.SaveSnapshot(false, Id, current);
            store.Add(true, game);
            store.SaveSnapshot(true, Id, current);
            using (var links = new App.PriceWatchWidgetForm(_ => source,
                new() { Enabled = true, Live = true, ShopIds = [61, 27, 35, 16] }, "de", _ => { }, store))
            {
                using var bitmap = links.RenderBitmap(new(1200, 900), 96, out var layout);
                var merchantLink = typeof(App.PriceWatchWidgetForm).GetMethod("MerchantLink",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                var urls = Enumerable.Range(0, 3).Select(index =>
                    ((Uri)merchantLink.Invoke(links, [0, index])!).AbsolutePath).ToArray();
                check(layout.OfferCounts!.Single() == 3 && urls.SequenceEqual(new[] { "/offer/16", "/offer/27", "/offer/35" }),
                    "sorted merchant rows open their exact URLs while the widget keeps at most three offers");
            }
            check(true, "all phase 13 UI and source tests operate on local fixtures");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, false);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static void Pump(Func<bool> done)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && timer.Elapsed < TimeSpan.FromSeconds(5))
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }
        if (!done()) throw new Exception("Shop selection did not settle.");
    }

    private static void CheckActionSurface(App.PriceWatchWidgetForm widget, Action<bool, string> check, string stage)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var field = typeof(App.PriceWatchWidgetForm).GetField("layout", flags)!;
        var before = (App.PriceWatchWidgetLayout)field.GetValue(widget)!;
        foreach (var bounds in new[] { before.Search, before.Refresh })
        foreach (float y in new[] { .1f, .5f, .9f })
        {
            var point = new Point((int)((bounds.Left + bounds.Width / 2) * before.Scale),
                (int)((bounds.Top + bounds.Height * y) * before.Scale));
            nint coordinates = (point.X & 0xffff) | ((point.Y & 0xffff) << 16);
            SendMessage(widget.Handle, 0x0200, 0, coordinates);
            var after = (App.PriceWatchWidgetLayout)field.GetValue(widget)!;
            GetClientRect(widget.Handle, out var client);
            var screen = widget.PointToScreen(point);
            nint screenCoordinates = (screen.X & 0xffff) | ((screen.Y & 0xffff) << 16);
            int hit = (int)SendMessage(widget.Handle, 0x0084, 0, screenCoordinates);
            SendMessage(widget.Handle, 0x0201, 1, coordinates);
            int pressed = (int)typeof(App.PriceWatchWidgetForm).GetField("pressed", flags)!.GetValue(widget)!;
            widget.Capture = false;
            check(after.ActionAt(point) == before.ActionAt(point) && point.Y < client.Bottom && hit == 1
                && pressed == before.ActionAt(point),
                $"action geometry is stable across hover at {stage} / {y} before {before.Search.Y} after {after.Search.Y} pressed {pressed} expected {before.ActionAt(point)} hit {hit} clientBottom {client.Bottom} point {point} active {typeof(App.PriceWatchWidgetForm).GetField("active", flags)!.GetValue(widget)}");
        }
    }

    private static void CheckAddingThroughSearch(Action<bool, string> check, string directory)
    {
        var store = new App.PriceWatchStore(Path.Combine(directory, "search-add.sqlite"));
        var source = new DelayedLocalSource();
        using var widget = new App.PriceWatchWidgetForm(_ => source, new() { Enabled = true, ShopIds = All },
            "de", _ => { }, store);
        widget.Show();
        App.DesktopWidgetNative.AttachToDesktop(widget, new(30, 30));
        var choices = new[] { ("Cyberpunk", 0), ("Cyberpunk", 1), ("Cyberpunk", 2), ("PriceWatch Demo", 0),
            ("PriceWatch Demo", 1), ("PriceWatch Demo", 2), ("Duke Nukem", 0), ("Baldur", 0) };
        foreach (var (query, index) in choices)
        {
            widget.BeginInvoke(new Action(() =>
            {
                var search = Application.OpenForms.OfType<App.PriceWatchSearchForm>().Single();
                var table = search.Controls[0];
                table.Controls.OfType<TextBox>().Single().Text = query;
                table.Controls.OfType<Button>().Single().PerformClick();
                table.Controls.OfType<ListBox>().Single().SelectedIndex = index;
                table.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>()
                    .Single(button => button.DialogResult != DialogResult.Cancel).PerformClick();
            }));
            typeof(App.PriceWatchWidgetForm).GetMethod("ShowSearch", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(widget, null);
            Pump(() => store.Read(false).All(entry => entry.Snapshot != null)
                && ((System.Collections.IDictionary)typeof(App.PriceWatchWidgetForm).GetField("initialLoads",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(widget)!).Count == 0);
            Application.DoEvents();
            CheckActionSurface(widget, check, $"after the search dialog adds {query}/{index}");
        }
        foreach (float fraction in new[] { .1f, .5f, .9f })
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var layout = (App.PriceWatchWidgetLayout)typeof(App.PriceWatchWidgetForm).GetField("layout", flags)!.GetValue(widget)!;
            var bounds = layout.Refresh;
            var point = new Point((int)((bounds.Left + bounds.Width / 2) * layout.Scale),
                (int)((bounds.Top + bounds.Height * fraction) * layout.Scale));
            var before = store.Read(false).Last().Snapshot!.FetchedAt;
            nint coordinates = (point.X & 0xffff) | ((point.Y & 0xffff) << 16);
            SendMessage(widget.Handle, 0x0201, 1, coordinates);
            SendMessage(widget.Handle, 0x0202, 0, coordinates);
            Pump(() => store.Read(false).Last().Snapshot!.FetchedAt > before
                && typeof(App.PriceWatchWidgetForm).GetField("active", flags)!.GetValue(widget) == null);
            CheckActionSurface(widget, check, $"after a manual price click at {fraction}");
        }
    }

    private sealed class DelayedLocalSource : App.IPriceWatchSource
    {
        private readonly App.PriceWatchTestData data = new();
        public Task<App.PriceWatchReply<App.PriceWatchGame[]>> SearchAsync(string title, CancellationToken token) =>
            data.SearchAsync(title, token);
        public async Task<App.PriceWatchReply<App.PriceWatchGameInfo>> InfoAsync(Guid id, CancellationToken token)
        {
            await Task.Delay(20, token);
            return await data.InfoAsync(id, token);
        }
        public Task<App.PriceWatchReply<App.PriceWatchPrices[]>> PricesAsync(Guid id, CancellationToken token) =>
            PricesAsync(id, App.PriceWatchShops.Legacy, token);
        public async Task<App.PriceWatchReply<App.PriceWatchPrices[]>> PricesAsync(Guid id, IReadOnlyList<int> shops,
            CancellationToken token)
        {
            await Task.Delay(40, token);
            return await data.PricesAsync(id, shops, token);
        }
    }

    private static void CheckGrowingActionSurface(Action<bool, string> check, string directory)
    {
        var store = new App.PriceWatchStore(Path.Combine(directory, "growing.sqlite"));
        using var widget = new App.PriceWatchWidgetForm(_ => throw new Exception("Unexpected source"),
            new() { Enabled = true, ShopIds = All }, "de", _ => { }, store);
        widget.Show();
        App.DesktopWidgetNative.AttachToDesktop(widget, new(30, 30));
        CheckActionSurface(widget, check, "empty desktop widget");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var data = new App.PriceWatchTestData();
        var fixture = data.PricesAsync(Id, All, default).Result;
        for (int count = 1; count <= 20; count++)
        {
            var id = Guid.NewGuid();
            store.Add(false, new(id, $"Growing fixture {count}", "game"));
            typeof(App.PriceWatchWidgetForm).GetMethod("ReadEntries", flags)!.Invoke(widget, [id]);
            typeof(App.PriceWatchWidgetForm).GetMethod("Render", flags)!.Invoke(widget, null);
            CheckActionSurface(widget, check, $"after adding game {count}");
            store.SaveSnapshot(false, id, fixture with { Data = [new(id, fixture.Data.Single().Deals.Take(count % 7 + 1).ToArray())] });
            typeof(App.PriceWatchWidgetForm).GetMethod("ReadEntries", flags)!.Invoke(widget, [id]);
            typeof(App.PriceWatchWidgetForm).GetMethod("Render", flags)!.Invoke(widget, null);
            CheckActionSurface(widget, check, $"after prices for game {count}");
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect { internal int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetClientRect(nint window, out NativeRect rect);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);

    private sealed class ShopHandler : HttpMessageHandler
    {
        internal object? Data;
        internal int[] Selected = [];
        internal int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Selected = body.RootElement.GetProperty("shops").EnumerateArray().Select(value => value.GetInt32()).ToArray();
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    source = "IsThereAnyDeal", fetchedAt = DateTimeOffset.UtcNow, data = Data
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class DeferredSource : App.IPriceWatchSource
    {
        private readonly App.PriceWatchTestData local = new();
        internal TaskCompletionSource? Gate;
        internal int Calls, Active, MaximumConcurrent;
        internal bool Fail;
        internal readonly List<int[]> Requests = new();
        public Task<App.PriceWatchReply<App.PriceWatchGame[]>> SearchAsync(string title, CancellationToken token) =>
            local.SearchAsync(title, token);
        public Task<App.PriceWatchReply<App.PriceWatchGameInfo>> InfoAsync(Guid id, CancellationToken token) =>
            local.InfoAsync(id, token);
        public Task<App.PriceWatchReply<App.PriceWatchPrices[]>> PricesAsync(Guid id, CancellationToken token) =>
            PricesAsync(id, App.PriceWatchShops.Legacy, token);
        public async Task<App.PriceWatchReply<App.PriceWatchPrices[]>> PricesAsync(Guid id, IReadOnlyList<int> shops,
            CancellationToken token)
        {
            Calls++;
            Requests.Add(shops.ToArray());
            MaximumConcurrent = Math.Max(MaximumConcurrent, ++Active);
            try
            {
                if (Gate != null) await Gate.Task; // Return even after cancellation to test late replies.
                if (Fail) throw new App.PriceWatchException(App.PriceWatchFailure.Connection);
                return await local.PricesAsync(id, shops, default);
            }
            finally { Active--; }
        }
    }
}
