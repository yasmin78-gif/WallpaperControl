using System.Buffers.Binary;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace WallpaperControl;

// Share one instance per image source. Only overlapping reads share data.
internal sealed class PriceWatchCoverSource(Func<CancellationToken, Task<Stream>> open)
{
    private const int MaxBytes = 2 * 1024 * 1024;
    private const int MaxSide = 2048;
    private readonly object gate = new();
    private ReadOperation? pending;

    private sealed class ReadOperation
    {
        internal readonly CancellationTokenSource Cancellation = new();
        internal Task<byte[]> Bytes = null!;
        internal int Readers;
    }

    internal async Task<Bitmap?> LoadAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ReadOperation operation;
        lock (gate)
        {
            if (pending == null)
            {
                var created = new ReadOperation();
                created.Bytes = Task.Run(() => ReadAsync(created.Cancellation.Token));
                pending = created;
            }
            operation = pending;
            operation.Readers++;
        }

        try
        {
            var bytes = await operation.Bytes.WaitAsync(token).ConfigureAwait(false);
            return await Task.Run(() => Decode(bytes), token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException
            or ExternalException or HttpRequestException or TimeoutException
            || (error is OperationCanceledException && !token.IsCancellationRequested))
        {
            return null;
        }
        finally
        {
            bool lastReader;
            lock (gate)
            {
                lastReader = --operation.Readers == 0;
                if (lastReader) pending = null;
            }
            if (lastReader) _ = FinishReadAsync(operation);
        }
    }

    private static async Task FinishReadAsync(ReadOperation operation)
    {
        try
        {
            await operation.Cancellation.CancelAsync().ConfigureAwait(false);
            await operation.Bytes.ConfigureAwait(false);
        }
        catch (Exception) { /* The load already observed the error or was cancelled. */ }
        finally { operation.Cancellation.Dispose(); }
    }

    private async Task<byte[]> ReadAsync(CancellationToken token)
    {
        await using var stream = await open(token).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            if (output.Length + count > MaxBytes) throw new InvalidDataException("Cover exceeds the byte limit.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    private static Bitmap? Decode(byte[] bytes)
    {
        Size size = ReadDimensions(bytes);
        if (size.Width <= 0 || size.Height <= 0 || size.Width > MaxSide || size.Height > MaxSide) return null;
        using var stream = new MemoryStream(bytes, writable: false);
        using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
        if (image.Size != size || (!image.RawFormat.Equals(ImageFormat.Jpeg) && !image.RawFormat.Equals(ImageFormat.Png)))
            return null;
        return new Bitmap(image);
    }

    private static Size ReadDimensions(ReadOnlySpan<byte> data)
    {
        ReadOnlySpan<byte> png = [137, 80, 78, 71, 13, 10, 26, 10];
        if (data.Length >= 24 && data.StartsWith(png) && data[12..16].SequenceEqual("IHDR"u8))
            return new(BinaryPrimitives.ReadInt32BigEndian(data[16..]), BinaryPrimitives.ReadInt32BigEndian(data[20..]));
        if (data.Length < 2 || data[0] != 0xff || data[1] != 0xd8) return Size.Empty;

        // Read JPEG frame dimensions before giving the compressed image to GDI+.
        int offset = 2;
        while (offset < data.Length && data[offset++] == 0xff)
        {
            while (offset < data.Length && data[offset] == 0xff) offset++;
            if (data.Length - offset < 3) break;
            byte marker = data[offset++];
            if (marker is 0xda or 0xd9) break;
            int length = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
            if (length < 2 || length > data.Length - offset) break;
            if (length >= 8 && marker is >= 0xc0 and <= 0xcf and not (0xc4 or 0xc8 or 0xcc))
                return new(BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 5)..]),
                    BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 3)..]));
            offset += length;
        }
        return Size.Empty;
    }
}

internal sealed class PriceWatchCover : Panel
{
    private Bitmap? image;
    private CancellationTokenSource? loading;

    internal bool HasImage => image != null;

    internal async Task LoadAsync(PriceWatchCoverSource source)
    {
        CancelLoad();
        image?.Dispose();
        image = null;
        Invalidate();
        using var operation = new CancellationTokenSource();
        loading = operation;
        try
        {
            var loaded = await source.LoadAsync(operation.Token);
            if (operation.IsCancellationRequested || IsDisposed) loaded?.Dispose();
            else
            {
                image = loaded;
                Invalidate();
            }
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        finally { if (loading == operation) loading = null; }
    }

    internal void CancelLoad() => loading?.Cancel();

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (image == null || Width <= 2 || Height <= 2) return;
        float scale = Math.Min((Width - 2f) / image.Width, (Height - 2f) / image.Height);
        float width = image.Width * scale, height = image.Height * scale;
        e.Graphics.DrawImage(image, new RectangleF((Width - width) / 2, (Height - height) / 2, width, height));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CancelLoad();
            image?.Dispose();
            image = null;
        }
        base.Dispose(disposing);
    }
}
