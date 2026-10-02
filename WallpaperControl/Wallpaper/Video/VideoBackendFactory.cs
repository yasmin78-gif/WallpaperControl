namespace WallpaperControl.Video;

internal static class VideoBackendFactory
{
    internal static IVideoDesktop Create(Control dispatcher, IEnumerable<string>? arguments = null)
    {
        var args = (arguments ?? Environment.GetCommandLineArgs()).ToArray();
        string? backend = args.FirstOrDefault(a => a.StartsWith("--video-backend=", StringComparison.Ordinal))?[16..];
        if (backend is null or "mfplay")
        { AppLogger.Info("Video backend selected: MFPlay; internal default/reference"); return new MediaFoundationVideoDesktop(dispatcher); }
        if (backend != "libmpv") throw new ArgumentException("Unknown internal video backend");
        string? path = args.FirstOrDefault(a => a.StartsWith("--mpv-runtime=", StringComparison.Ordinal))?[14..];
        try
        {
            var runtime = MpvRuntime.Open(path ?? Path.Combine(AppContext.BaseDirectory, "video-runtime", "libmpv-2.dll"));
            AppLogger.Info($"Video backend selected: libmpv/D3D11; pinned={MpvRuntime.Build}");
            return new MpvVideoDesktop(dispatcher, runtime);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or BadImageFormatException or NotSupportedException or System.Security.SecurityException or DllNotFoundException or EntryPointNotFoundException)
        {
            AppLogger.Info($"Video backend selected: MFPlay; pinned runtime unavailable/incompatible; type={ex.GetType().Name}");
            return new MediaFoundationVideoDesktop(dispatcher);
        }
    }
}
