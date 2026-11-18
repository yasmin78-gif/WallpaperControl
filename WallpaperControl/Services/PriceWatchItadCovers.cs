namespace WallpaperControl;

internal sealed class PriceWatchItadCovers : IDisposable
{
    private const int MaxEntries = 8;
    private const long MaxCacheBytes = 16 * 1024 * 1024;
    private readonly object gate = new();
    private readonly HttpClient http;
    private readonly CancellationTokenSource shutdown = new();
    private readonly LinkedList<(string Url, Bitmap Image)> cache = new();
    private readonly Dictionary<string, PendingDownload> downloads = new(StringComparer.Ordinal);
    private long cacheBytes;
    private bool disposed;

    private sealed class PendingDownload(PriceWatchCoverSource source)
    {
        internal readonly PriceWatchCoverSource Source = source;
        internal int Readers;
    }

    internal PriceWatchItadCovers(HttpMessageHandler? handler = null) =>
        http = new HttpClient(handler ?? CreateHandler());

    internal static HttpClientHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseDefaultCredentials = false
    };

    internal static bool IsAllowed(Uri? url) =>
        url is { IsAbsoluteUri: true, Scheme: "https", IsDefaultPort: true, UserInfo.Length: 0, Fragment.Length: 0 }
        && url.IdnHost.Equals("assets.isthereanydeal.com", StringComparison.OrdinalIgnoreCase);

    internal Task<Bitmap?> LoadAsync(Uri? url, CancellationToken token) =>
        Task.Run(() => LoadCoreAsync(url, token), token);

    private async Task<Bitmap?> LoadCoreAsync(Uri? url, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsAllowed(url)) return null;
        string key = url!.AbsoluteUri;
        PendingDownload pending;
        lock (gate)
        {
            if (disposed) return null;
            for (var node = cache.First; node != null; node = node.Next)
            {
                if (!string.Equals(node.Value.Url, key, StringComparison.Ordinal)) continue;
                cache.Remove(node);
                cache.AddFirst(node);
                return (Bitmap)node.Value.Image.Clone();
            }
            if (!downloads.TryGetValue(key, out pending!))
            {
                pending = new(new PriceWatchCoverSource(cancellation => DownloadAsync(url, cancellation)));
                downloads.Add(key, pending);
            }
            pending.Readers++;
        }

        try
        {
            var image = await pending.Source.LoadAsync(token).ConfigureAwait(false);
            if (image == null) return null;
            lock (gate)
            {
                if (!disposed && !token.IsCancellationRequested && !cache.Any(entry => entry.Url == key))
                {
                    long bytes = ImageBytes(image);
                    while (cache.Count >= MaxEntries || cacheBytes + bytes > MaxCacheBytes)
                        RemoveOldest();
                    cache.AddFirst((key, (Bitmap)image.Clone()));
                    cacheBytes += bytes;
                }
            }
            return image;
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref disposed)) { return null; }
        finally
        {
            lock (gate)
                if (--pending.Readers == 0) downloads.Remove(key);
        }
    }

    private async Task<byte[]> DownloadAsync(Uri url, CancellationToken token)
    {
        CancellationTokenSource timeout;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            timeout = CancellationTokenSource.CreateLinkedTokenSource(token, shutdown.Token);
        }
        using var deadline = timeout;
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > PriceWatchCoverSource.MaxBytes)
            throw new InvalidDataException("Cover exceeds the byte limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
        return await PriceWatchCoverSource.ReadBytesAsync(stream, deadline.Token).ConfigureAwait(false);
    }

    private static long ImageBytes(Bitmap image) => (long)image.Width * image.Height * 4;

    private void RemoveOldest()
    {
        var image = cache.Last!.Value.Image;
        cache.RemoveLast();
        cacheBytes -= ImageBytes(image);
        image.Dispose();
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            while (cache.Count != 0) RemoveOldest();
        }
        shutdown.Cancel();
        http.Dispose();
        shutdown.Dispose();
    }
}
