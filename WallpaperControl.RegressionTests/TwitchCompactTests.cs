extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;

internal static class TwitchCompactTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private sealed class Handler : HttpMessageHandler
    {
        internal int Calls;
        internal Func<HttpRequestMessage, HttpResponseMessage> Respond = _ => throw new Exception("Unexpected image/API request");
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; return Task.FromResult(Respond(request)); }
    }
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    private sealed class Images : App.ITwitchAvatarDownloader
    {
        internal List<Bitmap> Created = new(); internal int Calls; internal bool Fail, Disposed;
        internal TaskCompletionSource? Hold;
        public async Task<Bitmap?> DownloadAsync(Uri uri, CancellationToken token)
        {
            Calls++; if (Hold != null) await Hold.Task; // Deliberately ignore cancellation to exercise late-completion disposal.
            if (Fail) return null;
            Bitmap bitmap = new(128, 128); using (var g = Graphics.FromImage(bitmap)) g.Clear(Color.SteelBlue); Created.Add(bitmap); return bitmap;
        }
        public void Dispose() => Disposed = true;
    }
    private static bool Disposed(Bitmap bitmap) { try { _ = bitmap.Width; return false; } catch (ArgumentException) { return true; } }
    private sealed class Store : App.ITwitchTokenStore
    {
        public App.TwitchTokens? Load() => new() { AccessToken = "test-access", RefreshToken = "test-refresh" };
        public void Save(App.TwitchTokens pair) { }
        public void Clear() { }
    }
    private sealed class Streams : App.ITwitchApi, App.ITwitchProfileApi
    {
        internal IReadOnlyList<App.TwitchStream> Items = [];
        internal int StreamCalls, ProfileCalls;
        internal bool ProfileFailure;
        internal TaskCompletionSource? HoldProfiles;
        public Task<App.TwitchDeviceCode> StartDeviceAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<App.TwitchTokens?> PollDeviceAsync(string code, CancellationToken token) => throw new NotSupportedException();
        public Task<App.TwitchTokens> RefreshTokenAsync(string refresh, CancellationToken token) => throw new NotSupportedException();
        public Task<App.TwitchIdentity> ValidateAsync(string value, CancellationToken token) => Task.FromResult(new App.TwitchIdentity("123", "tester", "Test", 14400));
        public Task<string> GetDisplayNameAsync(string value, string id, CancellationToken token) => Task.FromResult("Test");
        public Task<IReadOnlyList<App.TwitchStream>> GetStreamsAsync(string value, string id, CancellationToken token) { StreamCalls++; return Task.FromResult(Items); }
        public async Task<IReadOnlyDictionary<string, Uri>> GetProfileImagesAsync(string value, IReadOnlyList<string> ids, CancellationToken token)
        {
            ProfileCalls++; if (HoldProfiles != null) await HoldProfiles.Task;
            if (ProfileFailure) throw new App.TwitchException(App.TwitchError.Unavailable);
            return ids.ToDictionary(id => id, id => new Uri("https://images.example.invalid/" + id + ".png"));
        }
        public void Dispose() { }
    }
    private static App.TwitchStream Stream(int id, DateTimeOffset time, string title = "REMOVED STREAM TITLE") => new(id.ToString(), "channel_" + id,
        id == 1 ? "A very long channel display name" : "Channel " + id, "A very long game or category name", title, id == 1 ? 19908 : id == 2 ? 620 : 334, time.AddSeconds(-6777), "");
    private static byte[] Png(Bitmap bitmap) { using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png); return stream.ToArray(); }
    internal static void Run(Action<bool, string> check, string? previews)
    { RunAsync(check).GetAwaiter().GetResult(); Layout(check, previews); }
    private static async Task RunAsync(Action<bool, string> check)
    {
        using var http = new Handler(); using var api = new App.TwitchApi(http);
        var batches = new List<string[]>();
        http.Respond = request =>
        {
            check(request.RequestUri!.AbsolutePath == "/helix/users" && request.Headers.GetValues("Client-Id").Single() == App.TwitchConfiguration.ClientId && request.Headers.Authorization?.Scheme == "Bearer", "Get Users uses existing client ID and token");
            var ids = request.RequestUri.Query.TrimStart('?').Split('&').Select(part => Uri.UnescapeDataString(part.Split('=')[1])).ToArray(); batches.Add(ids);
            return Json(new { data = ids.Reverse().Select(id => new { id, profile_image_url = id == "2" ? "" : "https://images.example.invalid/" + id + ".png" }).ToArray() });
        };
        var urls = await api.GetProfileImagesAsync("test-access", Enumerable.Range(1, 205).Select(i => i.ToString()).ToArray(), default);
        check(batches.Select(b => b.Length).SequenceEqual(new[] { 100, 100, 5 }), "Profile requests batch up to 100 users instead of one request per channel");
        check(urls["1"].AbsolutePath == "/1.png" && urls["205"].AbsolutePath == "/205.png" && !urls.ContainsKey("2"), "Profile URL maps to user ID independent of response order; missing URL omitted");
        http.Respond = _ => Json(new { data = new[] { new { id = "1", profile_image_url = "https://images.example.invalid/1.png" }, new { id = "2", profile_image_url = "http://images.example.invalid/2.png" }, new { id = "999", profile_image_url = "https://images.example.invalid/999.png" } } });
        urls = await api.GetProfileImagesAsync("test-access", ["1", "2"], default);
        check(urls.Count == 1 && urls.ContainsKey("1"), "Only requested HTTPS profile URLs from Get Users are accepted");

        byte[] bytes;
        using (var wide = new Bitmap(200, 100))
        {
            using var g = Graphics.FromImage(wide); g.Clear(Color.Red); using var center = new SolidBrush(Color.Lime); using var side = new SolidBrush(Color.Blue);
            g.FillRectangle(center, 50, 0, 100, 100); g.FillRectangle(side, 150, 0, 50, 100); bytes = Png(wide);
        }
        using var imageHttp = new Handler();
        imageHttp.Respond = request =>
        {
            check(request.Headers.Authorization == null && !request.Headers.Contains("Client-Id"), "Image HTTP never forwards OAuth or Client-Id headers");
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        };
        using (var downloader = new App.TwitchAvatarDownloader(imageHttp))
        {
            using var bitmap = await downloader.DownloadAsync(new Uri("https://images.example.invalid/test.png"), default);
            check(bitmap is { Width: 128, Height: 128 } && bitmap.GetPixel(64, 64).G > 240 && bitmap.GetPixel(8, 64).G > 240, "Image is detached and center-cropped without aspect distortion");
            imageHttp.Respond = _ => new(HttpStatusCode.ServiceUnavailable);
            check(await downloader.DownloadAsync(new Uri("https://images.example.invalid/fail.png"), default) == null, "Image HTTP failure returns fallback");
            imageHttp.Respond = _ => new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
            check(await downloader.DownloadAsync(new Uri("https://images.example.invalid/corrupt.png"), default) == null, "Corrupt image returns fallback");
            imageHttp.Respond = _ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[2 * 1024 * 1024 + 1]) };
            check(await downloader.DownloadAsync(new Uri("https://images.example.invalid/large.png"), default) == null, "Image payload size is bounded");
            int calls = imageHttp.Calls;
            check(await downloader.DownloadAsync(new Uri("file:///C:/test.png"), default) == null && imageHttp.Calls == calls, "Non-HTTPS avatar input does not issue requests");
        }

        var time = DateTimeOffset.UtcNow; int lookups = 0; bool changedUrl = false;
        Task<IReadOnlyDictionary<string, Uri>> Profiles(IReadOnlyList<string> ids, CancellationToken token)
        { lookups++; return Task.FromResult<IReadOnlyDictionary<string, Uri>>(ids.Where(id => id != "missing").ToDictionary(id => id, id => new Uri("https://images.example.invalid/" + id + (changedUrl ? "-changed" : "") + ".png"))); }
        var images = new Images(); var cache = new App.TwitchAvatarCache(Profiles, images, () => time, capacity: 2);
        using var target = new Bitmap(48, 48); using var graphics = Graphics.FromImage(target);
        await cache.EnsureAsync(["1", "missing"]);
        check(cache.BitmapCount == 1 && !cache.Draw(graphics, "missing", new RectangleF(0, 0, 46, 46)), "Missing profile uses neutral fallback while retaining stream entry");
        await cache.EnsureAsync(["1", "missing"]);
        check(lookups == 1 && images.Calls == 1, "Cache prevents repeated Get Users calls and image downloads");
        await cache.EnsureAsync(["2"]); await cache.EnsureAsync(["3"]);
        check(cache.Count == 2 && cache.BitmapCount == 2 && Disposed(images.Created[0]), "Bounded LRU evicts and disposes old bitmap");
        time += TimeSpan.FromHours(6) + TimeSpan.FromSeconds(1); int downloads = images.Calls;
        await cache.EnsureAsync(["2", "3"]);
        check(images.Calls == downloads && lookups == 4, "Six-hour profile revalidation reuses unchanged image URLs");
        changedUrl = true; time += TimeSpan.FromHours(6) + TimeSpan.FromSeconds(1); await cache.EnsureAsync(["2"]);
        check(images.Calls == downloads + 1 && Disposed(images.Created[1]), "Changed profile image replaces and disposes previous bitmap");
        cache.Clear(); check(cache.Count == 0 && images.Created.All(Disposed), "Clearing cache disposes every owned bitmap");
        await cache.EnsureAsync(["1"]); cache.Dispose(); cache.Dispose();
        check(cache.Count == 0 && images.Disposed && images.Created.All(Disposed), "Cache shutdown is idempotent and releases downloader and bitmaps");

        var failedImages = new Images { Fail = true };
        using (var failedCache = new App.TwitchAvatarCache(Profiles, failedImages, () => time))
        {
            await failedCache.EnsureAsync(["1"]); int calls = failedImages.Calls; await failedCache.EnsureAsync(["1"]);
            check(failedCache.Count == 1 && failedCache.BitmapCount == 0 && failedImages.Calls == calls, "Image failure caches fallback with retry backoff");
            time += TimeSpan.FromMinutes(5); failedImages.Fail = false; await failedCache.EnsureAsync(["1"]);
            check(failedCache.BitmapCount == 1 && failedImages.Calls == calls + 1, "Failed avatar can recover after five-minute backoff");
        }
        var slowImages = new Images { Hold = new() };
        using (var staleCache = new App.TwitchAvatarCache(Profiles, slowImages))
        {
            var old = staleCache.EnsureAsync(["1"]);
            // Wait only for this small mocked operation to reach its simulated download.
            for (int i = 0; i < 100 && slowImages.Calls == 0; i++) await Task.Delay(10);
            staleCache.Clear(); slowImages.Hold.SetResult(); await old;
            check(slowImages.Calls == 1 && staleCache.Count == 0 && slowImages.Created.All(Disposed), "Cancelled stale image completion cannot repopulate cache and disposes bitmap");
        }
        var source = new Streams { Items = [Stream(1, time), Stream(2, time)], ProfileFailure = true };
        using var service = new App.TwitchService(source, new Store(), () => time); await service.RefreshAsync(); var snapshot = service.Streams;
        using (var presentationCache = new App.TwitchAvatarCache(service.GetProfileImagesAsync, new Images()))
        {
            await presentationCache.EnsureAsync(["1", "2"]);
            check(service.Streams.Count == 2 && ReferenceEquals(snapshot, service.Streams) && service.Error == App.TwitchError.None && service.Connected, "Optional Get Users failure does not fail Twitch refresh or alter authentication");
        }
        source.ProfileFailure = false; source.HoldProfiles = new();
        var late = service.GetProfileImagesAsync(["1"], default); service.Disconnect(); source.HoldProfiles.SetResult();
        bool cancelled = false; try { await late; } catch (OperationCanceledException) { cancelled = true; }
        check(cancelled, "Old profile lookup is rejected after existing session generation changes");

        check(App.TwitchRowPresentation.Viewers(334, "de") == "334" && App.TwitchRowPresentation.Viewers(620, "de") == "620", "Small viewer counts remain compact integers");
        foreach (string lang in new[] { "de", "en", "fr", "es", "ja" })
        {
            string separator = System.Globalization.CultureInfo.GetCultureInfo(lang).NumberFormat.NumberDecimalSeparator;
            check(App.TwitchRowPresentation.Viewers(19908, lang) == "19" + separator + "9k" && App.TwitchRowPresentation.Viewers(125000, lang) == "125k", "Compact viewer counts follow culture " + lang);
        }
        check(App.TwitchRowPresentation.Duration(time.AddSeconds(-6777), time) == "01:52:57" && App.TwitchRowPresentation.Duration(time.AddHours(-29), time) == "29:00:00", "Live duration uses total hours and current seconds");
        check(App.TwitchRowPresentation.Duration(time.AddSeconds(-6777), time.AddSeconds(1)) == "01:52:58" && App.TwitchRowPresentation.Duration(time.AddMinutes(1), time) == "00:00:00", "Duration advances locally and clamps future start time");
        foreach (int minutes in new[] { 1, 5, 10, 15 })
        {
            var clock = new DateTimeOffset(2026, 10, 2, 12, 2, 17, TimeSpan.Zero);
            var streams = new Streams { Items = [Stream(1, clock)] };
            using var scheduled = new App.TwitchService(streams, new Store(), () => clock);
            using var scheduler = new App.TwitchRefreshScheduler(scheduled, () => clock, useTimer: false);
            scheduler.Configure(true, minutes); await scheduler.EvaluateAsync();
            clock = App.TwitchRefreshScheduler.NextBoundary(clock, minutes).AddSeconds(-1); await scheduler.EvaluateAsync();
            check(streams.StreamCalls == 1 && scheduler.IntervalMinutes == minutes, minutes + " minute refresh waits until configured deadline");
            clock += TimeSpan.FromSeconds(1); await scheduler.EvaluateAsync(); await scheduler.EvaluateAsync();
            check(streams.StreamCalls == 2, minutes + " minute refresh runs once at deadline");
            scheduler.SetSuspended(true); clock += TimeSpan.FromHours(1); await scheduler.EvaluateAsync();
            scheduler.SetSuspended(false); await scheduler.EvaluateAsync(); await scheduler.EvaluateAsync();
            check(streams.StreamCalls == 3, minutes + " minute fullscreen deferral catches up once");
        }
        check(App.TwitchRefreshScheduler.NormalizeInterval(2) == 5 && new App.WidgetSettings().TwitchRefreshMinutes == 5, "Default and unsupported refresh intervals fall back to five minutes");
        string registry = @"Software\WallpaperControl.TwitchCompactTests\" + Guid.NewGuid().ToString("N");
        try
        {
            foreach (int minutes in new[] { 1, 5, 10, 15 })
            {
                var settings = new App.WidgetSettings { TwitchRefreshMinutes = minutes }; settings.Save(registry);
                check(App.WidgetSettings.Load(registry).TwitchRefreshMinutes == minutes && settings.Clone().TwitchRefreshMinutes == minutes, minutes + " minute preference persists and clones");
            }
        }
        finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(registry, false); }
    }
    private static void Layout(Action<bool, string> check, string? previews)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (previews != null) Directory.CreateDirectory(previews);
                DateTimeOffset time = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
                foreach (var style in Enum.GetValues<App.SystemWidgetStyle>())
                foreach (int count in new[] { 0, 1, 3, 20 })
                {
                    var source = new Streams { Items = Enumerable.Range(1, count).Select(i => Stream(i, time)).ToArray() };
                    using var service = new App.TwitchService(source, new Store(), () => time); service.RefreshAsync().GetAwaiter().GetResult();
                    using var widget = new App.TwitchWidgetForm(service, new App.WidgetSettings { TwitchStyle = style, TwitchMaximumHeight = 300, ClockLanguageCode = "de" }, _ => { }, utcNow: () => time);
                    using var bitmap = widget.RenderBitmap(); if (previews != null) bitmap.Save(Path.Combine(previews, style + "-" + count + ".png"));
                    int expected = count == 0 ? 134 : count == 1 ? 137 : count == 3 ? 253 : 300;
                    check(bitmap.Height == expected, style + " compact layout for " + count + " streams");
                    var viewport = (App.CalendarViewport)typeof(App.TwitchWidgetForm).GetField("viewport", Flags)!.GetValue(widget)!;
                    check(viewport.CanScroll == (count == 20) && viewport.ContentBounds.Bottom + App.CalendarViewport.FooterHeight <= viewport.LogicalHeight + 0.01, style + " header/footer and height bounds for " + count);
                    if (count == 20)
                    {
                        check(widget.ScrollWheel(new Point(25, 100), -120, 3) && viewport.ScrollOffset > 0, style + " compact list retains scrolling");
                        using var dpi = widget.RenderBitmap(dpi: 144); check(dpi.Width == 585 && dpi.Height == 450, style + " avatar and row layout scale with DPI");
                    }
                }
                // Images are prepared before creating the widget; Draw/paint performs no HTTP.
                var images = new Images();
                using var loaded = new App.TwitchAvatarCache((ids, _) => Task.FromResult<IReadOnlyDictionary<string, Uri>>(ids.ToDictionary(id => id, id => new Uri("https://images.example.invalid/" + id))), images);
                // No message pump is running in this isolated rendering check. Preload outside WinForms' UI context.
                var context = SynchronizationContext.Current; SynchronizationContext.SetSynchronizationContext(null);
                try { loaded.EnsureAsync(["1", "2", "3"]).GetAwaiter().GetResult(); }
                finally { SynchronizationContext.SetSynchronizationContext(context); }
                var avatarSource = new Streams { Items = Enumerable.Range(1, 3).Select(i => Stream(i, time)).ToArray() };
                using var avatarService = new App.TwitchService(avatarSource, new Store(), () => time); avatarService.RefreshAsync().GetAwaiter().GetResult();
                using (var widget = new App.TwitchWidgetForm(avatarService, new App.WidgetSettings { TwitchStyle = App.SystemWidgetStyle.Glow, ClockLanguageCode = "de" }, _ => { }, avatars: loaded, utcNow: () => time))
                {
                    using var bitmap = widget.RenderBitmap(); if (previews != null) bitmap.Save(Path.Combine(previews, "Glow-avatars.png"));
                    check(bitmap.GetPixel(37, 82).B > 100 && bitmap.GetPixel(15, 60) != Color.SteelBlue && images.Calls == 3, "Avatar renders inside circular 46-pixel bounds");
                    check(bitmap.GetPixel(370, 67).R > 180 && bitmap.GetPixel(370, 67).G < 100, "Live indicator is a drawn red dot");
                    Uri? clicked = null;
                    using var clickWidget = new App.TwitchWidgetForm(avatarService, new App.WidgetSettings(), _ => { }, url => clicked = url, utcNow: () => time);
                    using var clickBitmap = clickWidget.RenderBitmap(); var args = new MouseEventArgs(MouseButtons.Left, 1, 24, 80, 0);
                    typeof(App.TwitchWidgetForm).GetMethod("OnMouseDown", Flags)!.Invoke(clickWidget, [args]); typeof(App.TwitchWidgetForm).GetMethod("OnMouseUp", Flags)!.Invoke(clickWidget, [args]);
                    check(clicked?.AbsoluteUri == "https://www.twitch.tv/channel_1", "Whole compact entry including avatar retains channel click");
                    int streamCalls = avatarSource.StreamCalls, profileCalls = avatarSource.ProfileCalls;
                    for (int i = 0; i < 3; i++) widget.RefreshDisplayTick();
                    check(avatarSource.StreamCalls == streamCalls && avatarSource.ProfileCalls == profileCalls && images.Calls == 3, "Display-only duration tick does not request streams or images");
                }
                check(loaded.Count == 0 && images.Created.All(Disposed) && images.Disposed, "Removing widget disposes its avatar cache");
                byte[] RenderTitle(string title)
                {
                    var source = new Streams { Items = [Stream(1, time, title)] };
                    using var service = new App.TwitchService(source, new Store(), () => time); service.RefreshAsync().GetAwaiter().GetResult();
                    using var widget = new App.TwitchWidgetForm(service, new App.WidgetSettings(), _ => { }, utcNow: () => time);
                    using var bitmap = widget.RenderBitmap(); return Png(bitmap);
                }
                check(RenderTitle("First title").SequenceEqual(RenderTitle("Completely different title")), "Changing stream title produces identical widget pixels");
                using var editor = new App.WidgetSettingsEditor(new App.WidgetSettings { TwitchRefreshMinutes = 10 });
                var interval = (ComboBox)typeof(App.WidgetSettingsEditor).GetField("twitchInterval", Flags)!.GetValue(editor)!;
                check(interval.Items.Count == 4 && editor.ReadWidgetSettings(false).TwitchRefreshMinutes == 10, "Settings expose exactly four refresh choices and load saved interval");
                for (int i = 0; i < 4; i++)
                { interval.SelectedIndex = i; check(editor.ReadWidgetSettings(false).TwitchRefreshMinutes == new[] { 1, 5, 10, 15 }[i], "Refresh choice " + interval.Items[i] + " applies to preview/save"); }
                foreach (string lang in new[] { "de", "en", "fr", "es", "ja" })
                { editor.ApplyPresentation(false, lang); check(interval.Items.Count == 4 && editor.ReadWidgetSettings(false).TwitchRefreshMinutes == 15 && interval.Items[0]!.ToString()!.Contains('1'), "Refresh choices localize and preserve selection " + lang); }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if (failure != null) throw failure;
    }
}
