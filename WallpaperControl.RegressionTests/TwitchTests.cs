extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using Microsoft.Win32;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Resources;
using System.Globalization;

internal static class TwitchTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private sealed class Store : App.ITwitchTokenStore
    {
        internal App.TwitchTokens? Value; internal int Saves;
        public App.TwitchTokens? Load() => Value;
        public void Save(App.TwitchTokens value) { Value = value; Saves++; }
        public void Clear() => Value = null;
    }
    private static App.TwitchTokens Pair(string suffix = "") => new() { AccessToken = "access-marker" + suffix, RefreshToken = "refresh-marker" + suffix };
    private static App.TwitchStream Stream(int index) => new(index.ToString(), "channel_" + index, "Channel " + index, "Category", "A stream title", 12481, DateTimeOffset.UtcNow.AddMinutes(-102), "https://example.invalid/image.jpg");
    private sealed class Api : App.ITwitchApi
    {
        internal int Validations, Calls, Polls, Refreshes;
        internal App.TwitchError Failure;
        internal bool Expired, Revoked, SlowDown;
        internal TaskCompletionSource? Hold;
        internal IReadOnlyList<App.TwitchStream> Items = [];
        public Task<App.TwitchDeviceCode> StartDeviceAsync(CancellationToken token) => Task.FromResult(new App.TwitchDeviceCode("device", "CODE123", new Uri("https://www.twitch.tv/activate"), 30, 5));
        public Task<App.TwitchTokens?> PollDeviceAsync(string code, CancellationToken token)
        {
            Polls++; if (SlowDown && Polls == 1) throw new App.TwitchApi.SlowDownException();
            return Task.FromResult<App.TwitchTokens?>(Polls == 1 ? null : Pair());
        }
        public Task<App.TwitchTokens> RefreshTokenAsync(string value, CancellationToken token)
        { Refreshes++; if (Revoked) throw new App.TwitchException(App.TwitchError.Reconnect); Expired = false; return Task.FromResult(Pair("rotated")); }
        public Task<App.TwitchIdentity> ValidateAsync(string value, CancellationToken token)
        { Validations++; if (Expired || Revoked) throw new App.TwitchException(App.TwitchError.Reconnect); return Task.FromResult(new App.TwitchIdentity("123", "tester", "tester", 14400)); }
        public Task<string> GetDisplayNameAsync(string value, string user, CancellationToken token) => Task.FromResult("Test User");
        public async Task<IReadOnlyList<App.TwitchStream>> GetStreamsAsync(string value, string user, CancellationToken token)
        { Calls++; if (Hold != null) await Hold.Task.WaitAsync(token); if (Failure != App.TwitchError.None) throw new App.TwitchException(Failure); return Items; }
        public void Dispose() { }
    }
    private sealed class Handler : HttpMessageHandler
    {
        internal Func<HttpRequestMessage, string, HttpResponseMessage> Respond = (_, _) => throw new Exception("Unexpected request");
        internal int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; return Respond(request, request.Content == null ? "" : await request.Content.ReadAsStringAsync(token)); }
    }
    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static string TokenJson => "{\"access_token\":\"access\",\"refresh_token\":\"refresh\",\"scope\":[\"user:read:follows\"],\"token_type\":\"bearer\"}";
    internal static void Run(Action<bool, string> check, string? previews)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { RunAsync(check, previews).GetAwaiter().GetResult(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if (failure != null) throw new Exception("Twitch checks failed", failure);
    }
    private static async Task RunAsync(Action<bool, string> check, string? previews)
    {
        var time = DateTimeOffset.UtcNow;
        var api = new Api(); var store = new Store(); var intervals = new List<TimeSpan>();
        using var service = new App.TwitchService(api, store, () => time, (duration, token) => { token.ThrowIfCancellationRequested(); intervals.Add(duration); time += duration; return Task.CompletedTask; });
        check(await service.InitializeAsync() && !service.Connected && api.Validations == 0, "Twitch disconnected performs no API calls");
        string? code = null;
        check(await service.ConnectAsync(value => code = value.UserCode) && code == "CODE123" && service.DisplayName == "Test User", "Device-code login publishes code and connected display name");
        check(intervals.Count == 2 && intervals.All(i => i == TimeSpan.FromSeconds(5)) && store.Saves == 1, "Device polling respects interval and persists token pair");
        check(await service.InitializeAsync() && api.Validations == 1, "Validated session does not over-validate");
        time += TimeSpan.FromHours(1);
        check(await service.InitializeAsync() && api.Validations == 2, "Session validates hourly");
        check(await service.RefreshAsync() && service.Streams.Count == 0, "Followed streams with zero results");
        api.Items = [Stream(1)]; time += TimeSpan.FromSeconds(11);
        check(await service.RefreshAsync() && service.Streams.Count == 1, "Single live channel");
        api.Items = Enumerable.Range(1, 8).Select(Stream).ToArray(); time += TimeSpan.FromSeconds(11);
        check(await service.RefreshAsync() && service.Streams.Count == 8, "Multiple live channels");
        var last = service.Streams; var updated = service.LastUpdated;
        api.Failure = App.TwitchError.Offline; time += TimeSpan.FromSeconds(11);
        check(!await service.RefreshAsync() && ReferenceEquals(last, service.Streams) && service.LastUpdated == updated && service.Error == App.TwitchError.Offline, "Temporary network failure preserves last good snapshot and timestamp");
        int attempts = api.Calls; check(!await service.RefreshAsync() && api.Calls == attempts, "Network failure backs off without request spam");
        api.Failure = App.TwitchError.None; time += TimeSpan.FromMinutes(5); api.Hold = new();
        var pending = service.RefreshAsync();
        check(service.Busy && !await service.RefreshAsync() && api.Calls == attempts + 1, "Manual refresh prevents parallel requests");
        api.Hold.SetResult(); check(await pending && !service.Busy, "Refresh gate released on completion"); api.Hold = null;
        api.Expired = true; time += TimeSpan.FromHours(1);
        check(await service.InitializeAsync() && api.Refreshes == 1 && store.Value!.RefreshToken.EndsWith("rotated", StringComparison.Ordinal), "Expired public-client token refresh rotates and persists pair");
        api.Revoked = true; time += TimeSpan.FromHours(1);
        check(!await service.InitializeAsync() && !service.Connected && store.Value == null && service.Error == App.TwitchError.Reconnect && service.Streams.Count == 0, "Revoked token clears session and requires reconnect");
        int validates = api.Validations; await service.InitializeAsync(); check(api.Validations == validates, "Invalid session produces no validation loop");
        api.Revoked = false; api.SlowDown = true; api.Polls = 0; intervals.Clear();
        check(await service.ConnectAsync(_ => { }) && intervals.SequenceEqual(new[] { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) }), "Device slow_down increases polling interval");
        time += TimeSpan.FromSeconds(11); api.Hold = new(); var cancelled = service.RefreshAsync(); service.Disconnect();
        check(!await cancelled && !service.Connected && store.Value == null && service.Streams.Count == 0, "Disconnect cancels stale refresh without restoring session"); api.Hold = null;
        using (var cancelledService = new App.TwitchService(new Api(), new Store(), delay: (_, ct) => Task.Delay(Timeout.Infinite, ct)))
        {
            using var cancel = new CancellationTokenSource(); var login = cancelledService.ConnectAsync(_ => { }, cancel.Token); cancel.Cancel();
            check(!await login && cancelledService.Error == App.TwitchError.Cancelled && !cancelledService.Busy, "Closing login cancels polling and releases gate");
        }
        var expiryApi = new Api(); using (var expiredService = new App.TwitchService(expiryApi, new Store(), () => time, (d, _) => { time += TimeSpan.FromMinutes(1); return Task.CompletedTask; }))
            check(!await expiredService.ConnectAsync(_ => { }) && expiredService.Error == App.TwitchError.Expired && expiryApi.Polls == 0, "Device expiration terminates polling");

        string path = @"Software\WallpaperControl.TwitchTests\" + Guid.NewGuid().ToString("N");
        try
        {
            var secure = new App.TwitchTokenStore(path); secure.Save(Pair());
            using var key = Registry.CurrentUser.OpenSubKey(path);
            string encrypted = (string)key!.GetValue("Tokens")!;
            check(!encrypted.Contains("access-marker", StringComparison.Ordinal) && !encrypted.Contains("refresh-marker", StringComparison.Ordinal) && secure.Load()!.AccessToken == "access-marker", "DPAPI ciphertext roundtrip contains no plaintext tokens");
            var reloadApi = new Api(); using var reload = new App.TwitchService(reloadApi, secure);
            check(await reload.InitializeAsync() && reload.Connected && reloadApi.Validations == 1, "Saved token is validated when reloaded");
            secure.Clear(); check(secure.Load() == null, "Disconnect removes stored tokens");
            var settings = new App.WidgetSettings { TwitchEnabled = true, TwitchLocked = true, TwitchStyle = App.SystemWidgetStyle.Glow, TwitchMaximumHeight = 350, TwitchLocation = new(87, 99) };
            settings.Save(path); var restored = App.WidgetSettings.Load(path);
            check(restored.TwitchEnabled && restored.TwitchLocked && restored.TwitchStyle == App.SystemWidgetStyle.Glow && restored.TwitchMaximumHeight == 350 && restored.TwitchLocation == settings.TwitchLocation && settings.Clone().TwitchLocation == settings.TwitchLocation, "Twitch settings persist and clone without credentials");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, false); }

        check(App.TwitchLinks.Channel("channel_1")?.AbsoluteUri == "https://www.twitch.tv/channel_1", "Channel URL uses canonical Twitch HTTPS address");
        check(new[] { "bad/name", "x?query", "x#fragment", "../x", "", "ümlaut", new string('a', 26) }.All(x => App.TwitchLinks.Channel(x) == null), "Invalid channel logins cannot construct URLs");

        using var handler = new Handler(); using var realApi = new App.TwitchApi(handler);
        handler.Respond = (request, body) =>
        {
            check(request.RequestUri!.AbsoluteUri == "https://id.twitch.tv/oauth2/device" && body.Contains("scopes=user%3Aread%3Afollows", StringComparison.Ordinal) && !body.Contains("secret", StringComparison.Ordinal), "Device request uses only follows scope and no secret");
            return Json("{\"device_code\":\"dc\",\"user_code\":\"UC\",\"verification_uri\":\"https://www.twitch.tv/activate?public=true\",\"expires_in\":1800,\"interval\":5}");
        };
        check((await realApi.StartDeviceAsync(default)).UserCode == "UC", "Device response decoded");
        handler.Respond = (_, _) => Json("{\"message\":\"authorization_pending\"}", HttpStatusCode.BadRequest);
        check(await realApi.PollDeviceAsync("dc", default) == null, "Twitch authorization_pending respected");
        handler.Respond = (request, body) => { check(body.Contains("grant_type=refresh_token", StringComparison.Ordinal) && body.Contains("refresh_token=a%2Bb%26c", StringComparison.Ordinal) && !body.Contains("secret", StringComparison.Ordinal), "Public refresh encodes rotating token without client secret"); return Json(TokenJson); };
        check((await realApi.RefreshTokenAsync("a+b&c", default)).RefreshToken == "refresh", "Public refresh response decoded");
        handler.Respond = (request, _) =>
        {
            check(request.Headers.Authorization?.Scheme == "OAuth" && request.RequestUri!.AbsolutePath == "/oauth2/validate", "Official validation endpoint and OAuth header");
            return Json(JsonSerializer.Serialize(new { client_id = App.TwitchConfiguration.ClientId, user_id = "123", login = "tester", scopes = new[] { "user:read:follows" }, expires_in = 14400 }));
        };
        check((await realApi.ValidateAsync("access", default)).UserId == "123", "Validation verifies client scope and user");
        int pages = 0;
        handler.Respond = (request, _) =>
        {
            pages++; check(request.Headers.Authorization?.Scheme == "Bearer" && request.Headers.GetValues("Client-Id").Single() == App.TwitchConfiguration.ClientId && request.RequestUri!.Query.Contains("user_id=123", StringComparison.Ordinal), "Helix request carries Client-Id Bearer token and user ID");
            if (pages == 2) check(request.RequestUri!.Query.Contains("after=cursor1", StringComparison.Ordinal), "Pagination uses next cursor");
            return Json(JsonSerializer.Serialize(new { data = new[] { Stream(pages) }, pagination = pages == 1 ? new { cursor = "cursor1" } : (object)new { } }));
        };
        check((await realApi.GetStreamsAsync("access", "123", default)).Count == 2 && pages == 2, "All followed-stream pages assembled");
        foreach (var (status, json, error) in new[] {
            (HttpStatusCode.Unauthorized, "{}", App.TwitchError.Reconnect), (HttpStatusCode.ServiceUnavailable, "{}", App.TwitchError.Unavailable),
            (HttpStatusCode.TooManyRequests, "{}", App.TwitchError.RateLimit), (HttpStatusCode.OK, "broken", App.TwitchError.InvalidResponse),
            (HttpStatusCode.OK, "{\"data\":[{}],\"pagination\":{}}", App.TwitchError.InvalidResponse) })
        {
            handler.Respond = (_, _) => Json(json, status); App.TwitchError? caught = null;
            try { await realApi.GetStreamsAsync("access", "123", default); } catch (App.TwitchException ex) { caught = ex.Error; }
            check(caught == error, "API safely classifies " + error);
        }
        handler.Respond = (_, _) => Json(JsonSerializer.Serialize(new { data = new[] { Stream(1) }, pagination = new { cursor = "repeat" } }));
        bool bounded = false; try { await realApi.GetStreamsAsync("access", "123", default); } catch (App.TwitchException ex) { bounded = ex.Error == App.TwitchError.InvalidResponse; }
        check(bounded, "Repeated pagination cursor terminates safely");

        var scheduleApi = new Api { Items = [Stream(1)] }; var scheduleStore = new Store { Value = Pair() };
        using var scheduleService = new App.TwitchService(scheduleApi, scheduleStore, () => time);
        using var scheduler = new App.TwitchRefreshScheduler(scheduleService, () => time, false);
        await scheduler.EvaluateAsync(); int calls = scheduleApi.Calls; await scheduler.EvaluateAsync();
        check(calls == 1 && scheduleApi.Calls == calls, "Automatic refresh scheduled at five minutes without duplicates");
        scheduler.SetSuspended(true); time += TimeSpan.FromMinutes(30); await scheduler.EvaluateAsync();
        check(scheduleApi.Calls == calls, "Fullscreen defers Twitch requests");
        scheduler.SetSuspended(false); await scheduler.EvaluateAsync(); await scheduler.EvaluateAsync();
        check(scheduleApi.Calls == calls + 1, "Fullscreen resume catches up exactly once");
        scheduler.SetPowerSuspended(true); time += TimeSpan.FromMinutes(10); await scheduler.EvaluateAsync();
        check(scheduleApi.Calls == calls + 1, "Sleep defers Twitch requests");
        scheduler.SetPowerSuspended(false); await scheduler.EvaluateAsync();
        check(scheduleApi.Calls == calls + 2, "Sleep resume catches up once");
        scheduler.Configure(false); time += TimeSpan.FromHours(1); int hourly = scheduleApi.Validations;
        await scheduler.EvaluateAsync(); await scheduler.EvaluateAsync();
        check(scheduleApi.Calls == calls + 2 && scheduleApi.Validations == hourly + 1, "Disabled widget validates connected session hourly without streams requests");
        scheduler.Configure(true);
        scheduleApi.Items = Enumerable.Range(1, 20).Select(Stream).ToArray(); time += TimeSpan.FromMinutes(5); await scheduler.EvaluateAsync();
        if (previews != null) Directory.CreateDirectory(previews);
        // UI checks run in a separate STA because asynchronous transport tests may resume on a pool thread.
        Exception? renderingError = null;
        var ui = new Thread(() =>
        {
            try
            {
                foreach (var style in Enum.GetValues<App.SystemWidgetStyle>())
                {
                    Uri? opened = null;
                    using var widget = new App.TwitchWidgetForm(scheduleService, new App.WidgetSettings { TwitchStyle = style, TwitchMaximumHeight = 300, ClockLanguageCode = "de" }, _ => { }, uri => opened = uri);
                    using var bmp = widget.RenderBitmap();
                    if (previews != null) bmp.Save(Path.Combine(previews, "Twitch-" + style + ".png"));
                    var view = (App.CalendarViewport)typeof(App.TwitchWidgetForm).GetField("viewport", Flags)!.GetValue(widget)!;
                    check(bmp.Height == 300 && view.CanScroll, style + " bounded rendering and scroll viewport");
                    var point = new Point(25, 66); var args = new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0);
                    typeof(App.TwitchWidgetForm).GetMethod("OnMouseDown", Flags)!.Invoke(widget, [args]); typeof(App.TwitchWidgetForm).GetMethod("OnMouseUp", Flags)!.Invoke(widget, [args]);
                    check(opened?.AbsoluteUri == "https://www.twitch.tv/channel_1", style + " row click opens expected URL");
                    check(widget.ScrollWheel(new Point(30, 100), -120, 3) && view.ScrollOffset > 0, style + " wheel scrolls list");
                    using var dpi = widget.RenderBitmap(dpi: 144); check(dpi.Height == 450 && dpi.Width == 585, style + " DPI scales bounded layout");
                }
                using var emptyService = new App.TwitchService(new Api(), new Store());
                using var emptyWidget = new App.TwitchWidgetForm(emptyService, new App.WidgetSettings(), _ => { });
                using var empty = emptyWidget.RenderBitmap(); check(empty.Height <= 140, "Disconnected widget remains compact");
                using var noneService = new App.TwitchService(new Api(), new Store { Value = Pair() }); noneService.RefreshAsync().GetAwaiter().GetResult();
                using var noneWidget = new App.TwitchWidgetForm(noneService, new App.WidgetSettings(), _ => { });
                using var none = noneWidget.RenderBitmap(); check(noneService.Connected && none.Height <= 140 && noneWidget.HitTest(new Point(25, 66)) == null, "No live followed channels renders compact localized empty state");
                using var editor = new App.WidgetSettingsEditor(new App.WidgetSettings { TwitchEnabled = true, TwitchStyle = App.SystemWidgetStyle.Clean });
                editor.ConfigureTwitch(scheduleService, (_, _, _) => { }); editor.SelectWidget("TwitchTitle");
                check(editor.SelectedKey == "TwitchTitle" && editor.ReadWidgetSettings(false).TwitchEnabled && editor.ReadWidgetSettings(false).TwitchStyle == App.SystemWidgetStyle.Clean, "Twitch settings page integrates navigation and values");
                var resources = new ResourceManager("WallpaperControl.Strings", typeof(App.WidgetSettings).Assembly);
                foreach (string lang in new[] { "de", "en", "fr", "es", "ja" })
                {
                    var set = resources.GetResourceSet(lang == "de" ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(lang), true, false)!;
                    check(set.GetString("TwitchEmpty") != null && set.GetString("TwitchPrivacy") != null && set.GetString("TwitchViewers")!.Contains("{1}", StringComparison.Ordinal)
                        && Enum.GetValues<App.TwitchError>().Where(e => e != App.TwitchError.None).All(e => set.GetString("TwitchError" + e) != null), "Complete Twitch localization " + lang);
                }
            }
            catch (Exception ex) { renderingError = ex; }
        });
        ui.SetApartmentState(ApartmentState.STA); ui.Start(); ui.Join(); if (renderingError != null) throw renderingError;
    }
}
