using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace WallpaperControl;

internal interface ITwitchAvatarDownloader : IDisposable
{
    /// <summary>The caller owns the returned standalone normalized bitmap.</summary>
    Task<Bitmap?> DownloadAsync(Uri uri, CancellationToken token);
}

/// <summary>Unauthenticated, bounded CDN fetches. Never forwards Twitch OAuth headers to image servers.</summary>
internal sealed class TwitchAvatarDownloader : ITwitchAvatarDownloader
{
    internal const int BitmapSize = 128;
    private const int MaximumBytes = 2 * 1024 * 1024;
    private readonly HttpClient client;
    internal TwitchAvatarDownloader(HttpMessageHandler? handler = null)
    {
        client = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false });
        client.Timeout = TimeSpan.FromSeconds(30);
    }
    public async Task<Bitmap?> DownloadAsync(Uri uri, CancellationToken token)
    {
        if (uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo)) return null;
        try
        {
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaximumBytes) return null;
            using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var buffer = new MemoryStream(); byte[] bytes = new byte[8192]; int read;
            while ((read = await stream.ReadAsync(bytes, token).ConfigureAwait(false)) > 0)
            { if (buffer.Length + read > MaximumBytes) return null; buffer.Write(bytes, 0, read); }
            byte[] payload = buffer.ToArray();
            return await Task.Run(() => Decode(payload, token), token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { return null; } // Optional image failures do not affect the Twitch stream refresh.
    }
    private static Bitmap? Decode(byte[] bytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var stream = new MemoryStream(bytes);
        using var source = Image.FromStream(stream, false, false);
        if (source.Width > 2048 || source.Height > 2048 || source.Width < 1 || source.Height < 1) return null;
        var bitmap = new Bitmap(BitmapSize, BitmapSize, PixelFormat.Format32bppPArgb);
        try
        {
            using var g = Graphics.FromImage(bitmap); g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            // Center crop to a square while preserving aspect ratio, then the widget clips to a circle.
            float side = Math.Min(source.Width, source.Height);
            g.DrawImage(source, new RectangleF(0, 0, BitmapSize, BitmapSize), new RectangleF((source.Width - side) / 2, (source.Height - side) / 2, side, side), GraphicsUnit.Pixel);
            token.ThrowIfCancellationRequested(); return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }
    public void Dispose() => client.Dispose();
}
