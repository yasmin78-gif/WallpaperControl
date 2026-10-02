namespace WallpaperControl.Video;

internal readonly record struct VideoBackendSelection(IVideoDesktop Desktop, VideoEngine Effective, bool Fallback, string? Reason);
internal static class VideoBackendFactory
{
    internal static IVideoDesktop Create(Control dispatcher, IEnumerable<string>? arguments = null)
        => SelectAsync(dispatcher, VideoEngine.Standard, arguments).GetAwaiter().GetResult().Desktop;
    internal static async Task<VideoBackendSelection> SelectAsync(Control dispatcher, VideoEngine preferred, IEnumerable<string>? arguments = null)
    {
        var args = (arguments ?? Environment.GetCommandLineArgs()).ToArray();
        string? backend = args.FirstOrDefault(a => a.StartsWith("--video-backend=", StringComparison.Ordinal))?[16..];
        string? path = args.FirstOrDefault(a => a.StartsWith("--mpv-runtime=", StringComparison.Ordinal))?[14..];
        VideoEngine requested = preferred;
        if (backend != null)
        {
            requested = backend switch { "libmpv" => VideoEngine.Standard, "mfplay" => VideoEngine.Compatibility,
                _ => throw new ArgumentException("Unknown development video backend") };
            AppLogger.Info($"Video development override active; requested={backend}; preferred={preferred}");
        }
        else if (path != null) throw new ArgumentException("A development runtime path requires an explicit backend override");
        if (requested == VideoEngine.Compatibility)
        {
            AppLogger.Info("Video backend selected: MFPlay; Compatibility");
            return new(new MediaFoundationVideoDesktop(dispatcher), VideoEngine.Compatibility, false, null);
        }
        try
        {
            var runtime = MpvRuntime.Open(path ?? Path.Combine(AppContext.BaseDirectory, "video-runtime", "package", "libmpv-2.dll"));
            await runtime.VerifyIdentityAsync().ConfigureAwait(false);
            AppLogger.Info($"Video backend selected: libmpv/D3D11; Standard; pinned={runtime.ExpectedBuild}");
            return new(new MpvVideoDesktop(dispatcher, runtime), VideoEngine.Standard, false, null);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or BadImageFormatException or NotSupportedException or System.Security.SecurityException or
            DllNotFoundException or EntryPointNotFoundException)
        {
            string reason = $"{ex.GetType().Name}: {ex.Message}; hr=0x{ex.HResult:X8}";
            AppLogger.Info($"Video backend fallback: Standard runtime unavailable; MFPlay Compatibility; reason={reason}");
            return new(new MediaFoundationVideoDesktop(dispatcher), VideoEngine.Compatibility, true, reason);
        }
    }
}
