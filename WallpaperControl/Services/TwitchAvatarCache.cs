namespace WallpaperControl;

/// <summary>Widget-owned bounded LRU bitmap cache; only the latest lookup may publish results.</summary>
internal sealed class TwitchAvatarCache : IDisposable
{
    internal const int DefaultCapacity = 128;
    private sealed class Entry(Uri? uri, Bitmap? bitmap, DateTimeOffset expires, long used)
    {
        internal Uri? Uri = uri;
        internal Bitmap? Bitmap = bitmap;
        internal DateTimeOffset Expires = expires;
        internal long Used = used;
    }
    private readonly object gate = new();
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyDictionary<string, Uri>>> profiles;
    private readonly ITwitchAvatarDownloader downloader;
    private readonly Func<DateTimeOffset> now;
    private readonly int capacity;
    private CancellationTokenSource? request;
    private int generation;
    private long used;
    private bool disposed;
    internal event Action? Changed;
    internal int Count { get { lock (gate) return entries.Count; } }
    internal int BitmapCount { get { lock (gate) return entries.Values.Count(e => e.Bitmap != null); } }
    internal TwitchAvatarCache(Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyDictionary<string, Uri>>> profiles,
        ITwitchAvatarDownloader? downloader = null, Func<DateTimeOffset>? now = null, int capacity = DefaultCapacity)
    { this.profiles = profiles; this.downloader = downloader ?? new TwitchAvatarDownloader(); this.now = now ?? (() => DateTimeOffset.UtcNow); this.capacity = Math.Clamp(capacity, 1, DefaultCapacity); }

    internal bool Draw(Graphics g, string id, RectangleF bounds)
    {
        lock (gate)
        {
            if (disposed || !entries.TryGetValue(id, out var entry) || entry.Bitmap == null) return false;
            entry.Used = ++used; g.DrawImage(entry.Bitmap, bounds); return true;
        }
    }
    internal void CancelPending()
    {
        CancellationTokenSource? previous;
        lock (gate) { generation++; previous = request; request = null; }
        try { previous?.Cancel(); } catch (ObjectDisposedException) { } // EnsureAsync owns its CTS; completion may race cancellation.
    }
    internal async Task EnsureAsync(IReadOnlyList<string> ids, CancellationToken token = default)
    {
        CancelPending();
        string[] needed; int current;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
        lock (gate)
        {
            if (disposed) return;
            needed = ids.Distinct(StringComparer.Ordinal).Take(capacity).Where(id => !entries.TryGetValue(id, out var entry) || now() >= entry.Expires).ToArray();
            if (needed.Length == 0) return;
            current = generation; request = linked;
        }
        try
        {
            // A brief debounce avoids issuing lookups for every intermediate mouse-wheel position.
            await Task.Delay(150, linked.Token);
            IReadOnlyDictionary<string, Uri> urls;
            try { urls = await profiles(needed, linked.Token); }
            catch (OperationCanceledException) when (linked.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                linked.Token.ThrowIfCancellationRequested();
                AppLogger.Info($"Twitch profile-image lookup failed: {ex.GetType().Name}.");
                foreach (string id in needed) Publish(id, null, null, false, current);
                return;
            }
            using var parallel = new SemaphoreSlim(2);
            await Task.WhenAll(needed.Select(async id =>
            {
                await parallel.WaitAsync(linked.Token);
                Bitmap? bitmap = null;
                try
                {
                    linked.Token.ThrowIfCancellationRequested(); urls.TryGetValue(id, out var uri);
                    bool reuse;
                    lock (gate) reuse = uri != null && entries.TryGetValue(id, out var entry) && entry.Uri == uri && entry.Bitmap != null;
                    if (!reuse && uri != null) bitmap = await downloader.DownloadAsync(uri, linked.Token);
                    linked.Token.ThrowIfCancellationRequested();
                    Publish(id, uri, bitmap, reuse || uri == null || bitmap != null, current, reuse);
                    bitmap = null; // Publish consumes/disposes ownership even when the operation is stale.
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
                catch { if (!linked.IsCancellationRequested) Publish(id, null, null, false, current); }
                finally { bitmap?.Dispose(); parallel.Release(); }
            }));
        }
        catch (OperationCanceledException) { }
        finally { lock (gate) { if (ReferenceEquals(request, linked)) request = null; } }
    }
    private void Publish(string id, Uri? uri, Bitmap? bitmap, bool success, int current, bool reuse = false)
    {
        lock (gate)
        {
            if (disposed || current != generation) { bitmap?.Dispose(); return; }
            entries.TryGetValue(id, out var old);
            if (reuse || !success)
            {
                bitmap?.Dispose(); bitmap = old?.Bitmap; uri = old?.Uri;
            }
            else old?.Bitmap?.Dispose();
            entries[id] = new(uri, bitmap, now().Add(success ? TimeSpan.FromHours(6) : TimeSpan.FromMinutes(5)), ++used);
            while (entries.Count > capacity)
            {
                var oldest = entries.MinBy(pair => pair.Value.Used);
                entries.Remove(oldest.Key); oldest.Value.Bitmap?.Dispose();
            }
        }
        try { Changed?.Invoke(); } catch (Exception ex) { AppLogger.Info($"Twitch avatar repaint failed: {ex.GetType().Name}."); }
    }
    internal void Clear()
    {
        CancelPending();
        lock (gate) { foreach (var entry in entries.Values) entry.Bitmap?.Dispose(); entries.Clear(); }
    }
    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; Changed = null; }
        Clear(); downloader.Dispose();
    }
}
