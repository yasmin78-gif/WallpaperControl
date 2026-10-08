using System.Net;
using System.Net.Http.Headers;
namespace WallpaperControl;
internal sealed class FeedService : IDisposable
{
    internal sealed class Snapshot
    {
        internal ParsedFeed? Feed;
        internal DateTimeOffset? Updated;
        internal DateTimeOffset Next;
        internal string? ETag, Modified, Error;
        internal string Url = "";
    }
    private readonly HttpClient http;
    private readonly FeedNotifications notifications;
    internal event Action<FeedSource,int>? NewEntries;
    internal Func<bool?> Fullscreen { get; set; } = FullscreenActivityDetector.GetFullscreenState;
    private readonly CancellationTokenSource lifetime = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 30000 };
    private FeedWidgetSettings configuration = new();
    internal readonly Dictionary<Guid, Snapshot> Snapshots = [];
    internal event Action? Changed;
    internal bool Busy { get; private set; }
    private bool disposed, suspended;
    internal Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.UtcNow;
    private int generation;
    internal FeedService(HttpMessageHandler? handler = null, bool startTimer = true, string? notificationHistoryPath = null)
    {
        notifications = new(notificationHistoryPath ?? (handler == null ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"WallpaperControl","Feeds","notifications.json") : null));
        notifications.NewEntries += (source,count) => NewEntries?.Invoke(source,count);
        http = handler == null ? new() : new(handler); http.Timeout = TimeSpan.FromSeconds(20);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("WallpaperControl-Feed/1.0");
        timer.Tick += async (_, _) => await RefreshAsync(); if (startTimer) timer.Start();
    }
    internal void Configure(FeedWidgetSettings value)
    {
        if (System.Text.Json.JsonSerializer.Serialize(configuration) != System.Text.Json.JsonSerializer.Serialize(value)) generation++;
        var previous = configuration;
        configuration = value.Clone();
        notifications.Configure(configuration.Sources);
        if (!previous.Enabled && configuration.Enabled) notifications.Rebaseline();
        foreach (var key in Snapshots.Keys.Where(k => !configuration.Sources.Any(s => s.Id == k)).ToArray()) Snapshots.Remove(key);
        foreach (var source in configuration.Sources)
            if (!Snapshots.TryGetValue(source.Id, out var state) || state.Url != source.Url) Snapshots[source.Id] = new() { Url = source.Url };
            else if (previous.Sources.FirstOrDefault(s => s.Id == source.Id)?.RefreshMinutes != source.RefreshMinutes) state.Next = DateTimeOffset.MinValue;
    }
    internal void SetSuspended(bool value) { suspended = value; if (value) notifications.Rebaseline(); if (!value) _ = RefreshAsync(); }
    internal static bool WithinPeriod(DateTimeOffset? published, FeedPeriod period, DateTimeOffset now)
    {
        if (published == null || period == FeedPeriod.All) return true;
        var cutoff = period switch { FeedPeriod.Day => now.AddHours(-24), FeedPeriod.Week => now.AddDays(-7), FeedPeriod.Month => now.AddMonths(-1), _ => DateTimeOffset.MinValue };
        return published >= cutoff;
    }
    internal IEnumerable<(FeedSource Source, FeedEntry Entry)> Items(Guid? selected, FeedPeriod? period = null)
    {
        var current = Now();
        return configuration.Sources.Where(s => selected == null || s.Id == selected).SelectMany(s => Snapshots.TryGetValue(s.Id, out var state) && state.Feed != null ? state.Feed.Entries.Select(e => (s, e)) : [])
            .Where(pair => WithinPeriod(pair.Item2.Published, period ?? configuration.Period, current))
            .OrderByDescending(pair => pair.Item2.Published).Take(200);
    }
    internal static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, int limit, CancellationToken cancellation)
    {
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Response too large");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var input = await response.Content.ReadAsStreamAsync(timeout.Token); using var output = new MemoryStream();
        var buffer = new byte[8192]; int read;
        while ((read = await input.ReadAsync(buffer, timeout.Token)) != 0) { if (output.Length + read > limit) throw new InvalidDataException("Response too large"); output.Write(buffer, 0, read); }
        return output.ToArray();
    }
    internal async Task<ParsedFeed> ProbeAsync(string address, CancellationToken token)
    {
        var uri = FeedParser.WebUri(address) ?? throw new ArgumentException("Feed URL");
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token); response.EnsureSuccessStatusCode();
        using var stream = new MemoryStream(await ReadBoundedAsync(response, 2_000_000, token));
        return FeedParser.Parse(stream, response.RequestMessage?.RequestUri ?? uri);
    }
    internal async Task RefreshAsync(bool manual = false)
    {
        if (disposed || Busy || suspended || !configuration.Enabled) return;
        Busy = true; var lease = generation; Changed?.Invoke();
        try
        {
            foreach (var source in configuration.Sources.ToArray())
            {
                if (disposed || suspended || lease != generation) break;
                var state = Snapshots[source.Id];
                if (!manual && (source.RefreshMinutes == 0 && state.Updated != null || Now() < state.Next)) continue;
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, source.Url);
                    if (state.ETag != null) request.Headers.TryAddWithoutValidation("If-None-Match", state.ETag);
                    if (state.Modified != null) request.Headers.TryAddWithoutValidation("If-Modified-Since", state.Modified);
                    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, lifetime.Token);
                    ParsedFeed? feed = state.Feed;
                    if (response.StatusCode != HttpStatusCode.NotModified)
                    {
                        response.EnsureSuccessStatusCode(); using var stream = new MemoryStream(await ReadBoundedAsync(response, 2_000_000, lifetime.Token));
                        feed = FeedParser.Parse(stream, response.RequestMessage?.RequestUri ?? new Uri(source.Url));
                    }
                    if (feed == null) throw new InvalidDataException("304 without cached feed");
                    if (disposed || lease != generation) break;
                    state.Feed = feed; state.Updated = Now(); state.Error = null;
                    notifications.Observe(source,feed,suspended || Fullscreen() != false);
                    state.ETag = response.Headers.ETag?.ToString() ?? state.ETag; state.Modified = response.Content.Headers.LastModified?.ToString("R") ?? state.Modified;
                    AppLogger.Info($"Feed refreshed: source={source.Id}; entries={feed?.Entries.Count ?? 0}; http={(int)response.StatusCode}");
                }
                catch (OperationCanceledException) when (disposed) { break; }
                catch (Exception ex) { if (lease == generation) state.Error = "FeedRefreshFailed"; AppLogger.Info($"Feed refresh failed: source={source.Id}; type={ex.GetType().Name}"); }
                state.Next = source.RefreshMinutes == 0 ? DateTimeOffset.MaxValue : Now().AddMinutes(source.RefreshMinutes);
            }
        }
        finally { Busy = false; if (!disposed) Changed?.Invoke(); }
    }
    public void Dispose() { if (disposed) return; disposed = true; timer.Dispose(); lifetime.Cancel(); http.Dispose(); lifetime.Dispose(); Changed = null; NewEntries = null; }
}
