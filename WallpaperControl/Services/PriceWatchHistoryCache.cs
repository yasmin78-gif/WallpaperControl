namespace WallpaperControl;

// Bounded process-memory cache. Last subscriber cancellation also stops the shared request.
internal sealed class PriceWatchHistoryCache : IDisposable
{
    private sealed class Work
    {
        internal readonly CancellationTokenSource Cancel = new();
        internal Task<PriceWatchReply<PriceWatchHistory[]>> Task = null!;
        internal int Users;
    }
    private readonly object gate = new();
    private readonly Dictionary<string, Work> pending = new();
    private readonly Dictionary<string, (PriceWatchReply<PriceWatchHistory[]> Reply, DateTimeOffset Until, long Order)> cache = new();
    private readonly Func<bool, Guid, int, int[], CancellationToken, Task<PriceWatchReply<PriceWatchHistory[]>>> fetch;
    private readonly Func<DateTimeOffset> clock;
    private long order;
    private bool disposed;
    internal PriceWatchHistoryCache(Func<bool, Guid, int, int[], CancellationToken,
        Task<PriceWatchReply<PriceWatchHistory[]>>> fetch, Func<DateTimeOffset>? clock = null)
    { this.fetch = fetch; this.clock = clock ?? (() => DateTimeOffset.UtcNow); }

    internal async Task<PriceWatchReply<PriceWatchHistory[]>> LoadAsync(bool live, Guid id, int days, int[] shops,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (id == Guid.Empty || days is not (7 or 30 or 90)) throw new PriceWatchException(PriceWatchFailure.InvalidRequest);
        int[] selected = PriceWatchShops.Validate(shops).Order().ToArray();
        string key = $"{live}/{id:D}/{days}/{string.Join(',', selected)}";
        Work work;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            foreach (var stale in cache.Where(p => p.Value.Until <= clock()).Select(p => p.Key).ToArray()) cache.Remove(stale);
            if (cache.TryGetValue(key, out var hit))
            { cache[key] = (hit.Reply, hit.Until, ++order); return hit.Reply; }
            if (!pending.TryGetValue(key, out work!))
            {
                work = new();
                pending.Add(key, work);
                work.Task = FetchAsync(work, live, id, days, selected);
            }
            work.Users++;
        }
        try
        {
            var reply = await work.Task.WaitAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            lock (gate)
            {
                if (!disposed && !work.Cancel.IsCancellationRequested)
                {
                    cache[key] = (reply, clock().AddMinutes(15), ++order);
                    // Conservative managed-memory estimate; never retain unbounded event arrays.
                    while (cache.Count > 16 || cache.Values.Sum(v => (long)v.Reply.Data.Length * 512) > 8 * 1024 * 1024)
                        cache.Remove(cache.MinBy(p => p.Value.Order).Key);
                }
            }
            return reply;
        }
        finally
        {
            lock (gate)
            {
                if (--work.Users == 0)
                {
                    pending.Remove(key);
                    work.Cancel.Cancel();
                    _ = work.Task.ContinueWith(t => { _ = t.Exception; work.Cancel.Dispose(); }, TaskScheduler.Default);
                }
            }
        }
    }

    private async Task<PriceWatchReply<PriceWatchHistory[]>> FetchAsync(Work work, bool live, Guid id, int days, int[] shops)
    {
        var reply = await fetch(live, id, days, shops, work.Cancel.Token).ConfigureAwait(false);
        if (reply.FetchedAt == default || reply.Data == null || reply.Data.Length > 10000)
            throw new PriceWatchException(PriceWatchFailure.InvalidResponse);
        return reply;
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true; cache.Clear();
            foreach (var work in pending.Values.ToArray()) work.Cancel.Cancel();
        }
    }
}
