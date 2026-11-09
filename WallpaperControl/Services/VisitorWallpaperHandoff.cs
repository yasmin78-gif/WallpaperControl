using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace WallpaperControl;

// Only callers with visitor media ownership may publish here. No normal source lookup.
internal sealed class VisitorWallpaperHandoff(string directory, Action<string> publish,
    Func<string, string, CancellationToken, Task<bool>>? extract = null, Func<string?>? readNative = null,
    Action<string>? publishCovered = null)
{
    private static readonly string[] Slots = ["safe-0.bmp", "safe-1.bmp", "safe-2.bmp"];
    internal string StaticPath
    {
        get
        {
            try
            {
                string slot = File.ReadAllText(Path.Combine(directory, "last-safe.txt"));
                if (Slots.Contains(slot)) return Path.Combine(directory, slot);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return Path.Combine(directory, Slots[0]);
        }
    }
    internal bool HasStaticImage => ValidImage(StaticPath);
    internal bool CanPublishCovered => publishCovered != null;

    internal bool PublishExistingOrNeutral() => HasStaticImage ? TryPublish(StaticPath) : PublishNeutral();
    internal bool PublishImage(string source, bool rendererReady = false)
    {
        string temporary = Path.Combine(directory, "still-" + Guid.NewGuid().ToString("N") + ".tmp");
        string pointer = Path.Combine(directory, "pointer-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Directory.CreateDirectory(directory);
            using Image image = Image.FromFile(source);
            using Bitmap bitmap = new(image.Width, image.Height, PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(bitmap)) graphics.DrawImage(image, 0, 0, bitmap.Width, bitmap.Height);
            bitmap.Save(temporary, ImageFormat.Bmp);
            string previous = StaticPath;
            string? native = null;
            try { native = readNative?.Invoke(); } catch { /* Keep the last committed slot protected regardless. */ }
            string target = Slots.Select(slot => Path.Combine(directory, slot)).First(path =>
                !string.Equals(path, previous, StringComparison.OrdinalIgnoreCase) && !string.Equals(path, native, StringComparison.OrdinalIgnoreCase));
            // Do not overwrite the image Windows is reading. Three bounded slots retain
            // the committed image, the native predecessor and one staging destination.
            File.Move(temporary, target, overwrite: true);
            File.WriteAllText(pointer, Path.GetFileName(target));
            File.Move(pointer, Path.Combine(directory, "last-safe.txt"), overwrite: true);
            return TryPublish(target, rendererReady);
        }
        catch (Exception ex) { AppLogger.Warning("Could not persist visitor wallpaper handoff.", ex); return false; }
        finally { DeleteTemporary(temporary); DeleteTemporary(pointer); }
    }
    internal bool PublishNeutral()
    {
        string temporary = Path.Combine(directory, "neutral-" + Guid.NewGuid().ToString("N") + ".bmp");
        try
        {
            Directory.CreateDirectory(directory);
            using (Bitmap bitmap = new(32, 32))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.FromArgb(32, 38, 44));
                bitmap.Save(temporary, ImageFormat.Bmp);
            }
            return PublishImage(temporary);
        }
        catch (Exception ex) { AppLogger.Warning("Could not prepare neutral visitor handoff.", ex); return false; }
        finally { DeleteTemporary(temporary); }
    }
    internal async Task<bool> PublishVideoAsync(string video,
        Func<string, CancellationToken, Task<bool>>? capture, Func<bool> stillCurrent, CancellationToken cancellation)
    {
        Directory.CreateDirectory(directory);
        string captured = Path.Combine(directory, "capture-" + Guid.NewGuid().ToString("N") + ".png");
        string thumbnail = Path.Combine(directory, "thumbnail-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            if (capture != null && await TryPrepareAsync(capture, captured, cancellation) && stillCurrent())
                if (PublishImage(captured)) return true;
            cancellation.ThrowIfCancellationRequested();
            if (File.Exists(video) && await TryPrepareAsync((path, token) =>
                    (extract ?? ExtractVideoThumbnailAsync)(video, path, token), thumbnail, cancellation) && stillCurrent())
                return PublishImage(thumbnail);
            return false;
        }
        finally { DeleteTemporary(captured); DeleteTemporary(thumbnail); }
    }
    private static async Task<bool> TryPrepareAsync(Func<string, CancellationToken, Task<bool>> prepare, string path, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        Task<bool>? operation = null;
        try
        {
            operation = prepare(path, timeout.Token);
            return await operation.WaitAsync(timeout.Token) && ValidImage(path);
        }
        catch (Exception ex) when (!cancellation.IsCancellationRequested)
        { AppLogger.Warning("Visitor frame capture unavailable; trying safe fallback.", ex); return false; }
        finally
        {
            // A blocked shell provider or native encoder can complete after the deadline.
            // It only owns this scratch file and can never publish the native image slots itself.
            if (operation is { IsCompleted: false }) _ = CleanupLatePreparationAsync(operation, path);
            else if (operation?.IsFaulted == true) _ = operation.Exception;
        }
    }
    private static async Task CleanupLatePreparationAsync(Task<bool> operation, string path)
    { try { await operation.ConfigureAwait(false); } catch { } finally { DeleteTemporary(path); } }
    private bool TryPublish(string path, bool rendererReady = false)
    {
        try { (rendererReady ? publishCovered ?? throw new InvalidOperationException("No covered handoff configured.") : publish)(path); return true; }
        catch (Exception ex) { AppLogger.Warning("Could not hand visitor wallpaper to Windows.", ex); return false; }
    }
    private static bool ValidImage(string path)
    { try { using Image image = Image.FromFile(path); return image.Width > 0 && image.Height > 0; } catch { return false; } }
    private static void DeleteTemporary(string path)
    { try { if (File.Exists(path)) File.Delete(path); } catch (Exception ex) { AppLogger.Warning("Visitor scratch image cleanup failed.", ex); } }

    // Shell thumbnail extraction uses installed Windows codecs, with no fallback to file icons.
    // It runs on its own STA worker because GetImage may perform blocking disk/codec work.
    private static Task<bool> ExtractVideoThumbnailAsync(string video, string destination, CancellationToken cancellation)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread worker = new(() =>
        {
            IShellItemImageFactory? factory = null; nint bitmapHandle = 0;
            try
            {
                cancellation.ThrowIfCancellationRequested();
                Guid iid = typeof(IShellItemImageFactory).GUID;
                Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(video, 0, ref iid, out factory));
                factory.GetImage(new NativeSize { Width = 1920, Height = 1080 }, 0x1 | 0x8, out bitmapHandle);
                cancellation.ThrowIfCancellationRequested();
                using Bitmap bitmap = Image.FromHbitmap(bitmapHandle);
                bitmap.Save(destination, ImageFormat.Png);
                completion.TrySetResult(true);
            }
            catch (Exception ex) { completion.TrySetException(ex); }
            finally
            {
                try
                {
                    if (bitmapHandle != 0) DeleteObject(bitmapHandle);
                    if (factory != null && Marshal.IsComObject(factory)) Marshal.FinalReleaseComObject(factory);
                }
                catch (Exception ex) { AppLogger.Warning("Visitor thumbnail resources could not be released.", ex); }
            }
        }) { IsBackground = true, Name = "Visitor video still" };
        worker.SetApartmentState(ApartmentState.STA); worker.Start();
        return completion.Task;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeSize { internal int Width, Height; }
    [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    { void GetImage(NativeSize size, uint flags, out nint bitmap); }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, nint bindContext, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint instance);
}
