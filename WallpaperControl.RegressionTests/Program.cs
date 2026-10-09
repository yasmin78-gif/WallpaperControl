using System.Diagnostics;
using System.Collections.Concurrent;
using WallpaperControl;
using System.Text;
using System.IO.Pipes;

try
{
    if(args.Length==1&&args[0]=="--launcher-child")return 0;
    if (args.Contains("--fail-test")) throw new Exception("Intentional test failure.");
    if (args.Length == 5 && args[0] == "--mpv-application-loop")
    { MpvApplicationLoopProbe.Run(args[1], args[2], args[3], int.Parse(args[4])); return 0; }
    if (args.Length == 5 && args[0] == "--mpv-production")
    { MpvProductionNativeChecks.Run(args[1], args[2], args[3], int.Parse(args[4])); return 0; }
    if (args.Length == 3 && args[0] == "--libmpv-audio-handoff-checks")
    {
        LibMpvAudioHandoffChecks.Run(args[1],args[2]); return 0;
    }
    if (args.Length == 3 && args[0] == "--libmpv-fill-checks")
    {
        LibMpvPreparationChecks.Fill(args[1],args[2]); return 0;
    }
    if (args.Length == 3 && args[0] == "--libmpv-test-surface")
    {
        LibMpvDesktopChecks.Surface(args[1]=="full",int.Parse(args[2])); return 0;
    }
    if (args.Length == 3 && args[0] == "--libmpv-preparation-checks")
    {
        LibMpvPreparationChecks.Run(args[1],args[2]); return 0;
    }
    if (args.Length == 3 && args[0] == "--libmpv-desktop-checks")
    {
        LibMpvDesktopChecks.Run(args[1],args[2]); return 0;
    }
    if (args.Length is 5 or 6 && args[0] == "--libmpv-switch")
    {
        LibMpvSwitchDiagnostics.Run(args[1], args[2], args[3], int.Parse(args[4]),args.Length==6 && args[5]=="idle");
        return 0;
    }
    if (args.Length == 4 && args[0] == "--libmpv-desktop-loop")
    {
        LibMpvLoopDiagnostics.Run(args[1], args[2], int.Parse(args[3]), desktop: true);
        return 0;
    }
    if (args.Length == 4 && args[0] == "--libmpv-desktop")
    {
        LibMpvDesktopDiagnostics.Run(args[1], args[2], int.Parse(args[3]));
        return 0;
    }
    if (args.Length == 2 && args[0] == "--audio-background-check")
    {
        LoopAudioProbe.MeasureBackground(int.Parse(args[1]));
        return 0;
    }
    if (args.Length is 4 or 5 && args[0] == "--libmpv-loop")
    {
        LibMpvLoopDiagnostics.Run(args[1], args[2], int.Parse(args[3]), args.Length == 5 ? double.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture) : 21.54434690031884);
        return 0;
    }
    if(args.Length>0 && args[0].StartsWith("--libmpv",StringComparison.Ordinal))
        throw new ArgumentException("Invalid isolated libmpv diagnostic arguments");
    if (args.Length == 3 && args[0] == "--media-engine-loop")
    {
        MediaEngineLoopDiagnostics.Run(args[1], int.Parse(args[2]));
        return 0;
    }
    if (args.Length is 3 or 4 && args[0] == "--video-loop-timing")
    {
        VideoLoopTimingDiagnostics.Run(args[1], int.Parse(args[2]), args.Length == 4 ? args[3] : null);
        return 0;
    }
    if (args.Length == 2 && args[0] == "--video-handle-count-pid")
    {
        Console.WriteLine($"Kernel handles for pid={args[1]}: {NativeHandleSnapshot.Capture(int.Parse(args[1])).Total}");
        return 0;
    }
    if (args.Length == 5 && args[0] == "--video-handle-stress")
    {
        VideoHandleStressDiagnostics.Run(args[1], args[2], int.Parse(args[3]), args[4]);
        return 0;
    }

    if (args.Contains("--probe"))
    {
        using var probe = new SingleInstanceGuard(args[1]);
        return probe.IsPrimary ? 10 : 20;
    }

    string instanceName = @"Local\WallpaperControl.Tests." + Guid.NewGuid().ToString("N");
    int passed = 0;
    if(args.Length==1&&args[0]=="--shared-ui-checks") { SharedUiTests.Run(Check); Console.WriteLine($"All {passed} shared UI checks passed."); return 0; }
    if(args.Length==1&&args[0]=="--widget-snapping-checks") { WidgetSnappingTests.Run(Check); Console.WriteLine($"All {passed} widget snapping checks passed."); return 0; }
    // Records an assertion result and reports the named regression check.
    void Check(bool ok, string name)
    {
        if (!ok) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
        passed++;
    }
    if (args.Length == 1 && args[0] == "--mpv-controller-checks") { MpvControllerIntegrationTests.Run(Check); Console.WriteLine($"All {passed} mpv controller checks passed."); return 0; }
    if (args.Length >= 2 && args[0] == "--video-native") { VideoNativeSmokeTests.Run(args[1], Check); Console.WriteLine($"All {passed} native video checks passed."); return 0; }
    if (args.Length >= 2 && args[0] == "--video-native-phase2") { VideoNativeSmokeTests.Run(args[1], Check, phase2Only: true); Console.WriteLine($"All {passed} Phase 2 native video checks passed."); return 0; }
    if (args.Length >= 2 && args[0] == "--video-desktop") { VideoDesktopSmokeTests.Run(args[1], Check); Console.WriteLine($"All {passed} desktop video checks passed."); return 0; }
    if (args.Length >= 2 && args[0] == "--video-desktop-phase2") { VideoDesktopSmokeTests.Run(args[1], Check, phase2Only: true); Console.WriteLine($"All {passed} Phase 2 desktop video checks passed."); return 0; }
    if (args.Length == 1 && args[0] == "--production-video-checks")
    { ProductionVideoIntegrationChecks.Run(Check); MpvProfileChecks.Run(); CandidatePackageChecks.Run(Path.Combine(AppContext.BaseDirectory, "video-runtime", "package")); Console.WriteLine($"All {passed} production integration checks passed."); return 0; }
    if (args.Length == 2 && args[0] == "--production-video-native")
    { ProductionVideoIntegrationChecks.Native(args[1], Check); Console.WriteLine($"All {passed} production native checks passed."); return 0; }
    if (args.Length is 1 or 2 && args[0] == "--package-delivered-checks")
    { PackageDeliveredGroupTests.Run(Check, args.Length == 2 ? args[1] : null); Console.WriteLine($"All {passed} delivered package widget checks passed."); return 0; }
    if (args.Length is 1 or 2 && args[0] == "--twitch-compact-checks")
    { TwitchCompactTests.Run(Check, args.Length == 2 ? args[1] : null); Console.WriteLine("All focused Twitch compact/avatar checks passed."); return 0; }
    if (args.Length == 1 && args[0] == "--twitch-notification-checks")
    { TwitchNotificationTests.Run(Check); Console.WriteLine($"All {passed} focused Twitch startup/notification checks passed."); return 0; }
    if (args.Length == 1 && args[0] == "--wallpaper-widget-mode-checks")
    { WallpaperWidgetModeTests.Run(Check); Console.WriteLine($"All {passed} focused wallpaper widget mode checks passed."); return 0; }
    if(args.Length==1&&args[0]=="--launcher-widget-checks") { LauncherWidgetTests.Run(Check); Console.WriteLine($"All {passed} launcher widget checks passed."); return 0; }
    if (args.Length == 1 && args[0] == "--clipboard-widget-checks") { ClipboardWidgetTests.Run(Check); Console.WriteLine($"All {passed} clipboard widget checks passed."); return 0; }
    if (args.Length == 1 && args[0] == "--system-drive-checks") { SystemDriveTests.Run(Check); Console.WriteLine($"All {passed} system drive checks passed."); return 0; }
    if (args.Length == 1 && args[0] == "--now-playing-checks") { NowPlayingTests.Run(Check); Console.WriteLine($"All {passed} Now Playing checks passed."); return 0; }
    if (args.Length == 1 && args[0] == "--now-playing-native-checks") { NowPlayingTests.Native(Check); Console.WriteLine($"All {passed} native Now Playing checks passed."); return 0; }
    if (args.Length == 1 && args[0] == "--feed-notification-checks")
    { FeedNotificationTests.Run(Check); Console.WriteLine($"All {passed} focused feed notification and tone checks passed."); return 0; }
    if (args.Length == 1 && args[0] == "--widget-navigation-checks")
    { WidgetNavigationTests.Run(Check); Console.WriteLine($"All {passed} focused widget navigation checks passed."); return 0; }
    if (args.Length is 1 or 2 && args[0] == "--feed-checks")
    { FeedTests.Run(Check, args.Length == 2 ? args[1] : null); Console.WriteLine($"All {passed} focused feed checks passed."); return 0; }
    if (args.Length == 1 && args[0] == "--notification-feature-checks")
    { NotificationFeatureTests.Run(Check); Console.WriteLine($"All {passed} focused sound/package notification checks passed."); return 0; }
    if (args.Length is 1 or 2 && args[0] == "--twitch-checks")
    { TwitchTests.Run(Check, args.Length == 2 ? args[1] : null); Console.WriteLine("All focused Twitch checks passed."); return 0; }
    LoopAudioProbeTests.Run(Check);
    MpvControllerIntegrationTests.Run(Check);
    VideoAudioTests.Run(Check);
    VideoPresentationTests.Run(Check);
    RuntimeResourceTelemetryTests.Run(Check);
    VideoPhase2Tests.Run(Check);
    if (args.Contains("--video")) { VideoWallpaperTests.Run(Check); Console.WriteLine($"All {passed} video checks passed."); return 0; }
    VideoWallpaperTests.Run(Check);
    if (args.Contains("--wallpaper-layout"))
    {
        WallpaperLayoutTests.Run(Check);
        Console.WriteLine($"All {passed} wallpaper layout checks passed.");
        return 0;
    }
    if (args.Contains("--status-ui"))
    {
        SlideshowStatusUiTests.Run(Check);
        Console.WriteLine($"All {passed} status UI checks passed.");
        return 0;
    }
    if (args.Contains("--web")) { WebWidgetTests.Run(Check); Console.WriteLine($"All {passed} Web checks passed."); return 0; }
    if (args.Contains("--notes")) { NotesTests.Run(Check); DailyNotesTests.Run(Check); Console.WriteLine($"All {passed} notes checks passed."); return 0; }
    if (args.Contains("--daily-notes")) { DailyNotesTests.Run(Check); Console.WriteLine($"All {passed} daily notes checks passed."); return 0; }
    if (args.Contains("--scheduler")) { SchedulerDiagnosticsTests.Run(Check); Console.WriteLine($"All {passed} scheduler checks passed."); return 0; }
    if (args.Contains("--note-reminders")) { NoteReminderTests.Run(Check); Console.WriteLine($"All {passed} reminder checks passed."); return 0; }
    if (args.Contains("--widget-navigation")) { WidgetNavigationTests.Run(Check); Console.WriteLine($"All {passed} navigation checks passed."); return 0; }
    if (args.Contains("--ship24")) { Ship24Tests.Run(Check); Console.WriteLine($"All {passed} Ship24 checks passed."); return 0; }
    if (args.Contains("--packages")) { PackageTrackingTests.Run(Check); Console.WriteLine($"All {passed} package checks passed."); return 0; }
    if (args.Contains("--amazon")) { AmazonLogisticsTests.Run(Check); Console.WriteLine($"All {passed} Amazon checks passed."); return 0; }
    if (args.Contains("--package-refresh")) { PackageRefreshTests.Run(Check); Console.WriteLine($"All {passed} package refresh checks passed."); return 0; }
    PostRefreshTests.Run(Check);
    PackageRefreshTests.Run(Check);
    AmazonLogisticsTests.Run(Check);
    PackageTrackingTests.Run(Check);
    NoteReminderTests.Run(Check);
    TrackingTests.Run(Check);
    Ship24Tests.Run(Check);
    SchedulerDiagnosticsTests.Run(Check);
    NativeSlideshowAdvanceTests.Run(Check);
    LayoutEngineActivationTests.Run(Check);
    WebWidgetTests.Run(Check);
    CalendarTests.Run(Check);
    StatisticsTests.Run(Check);
    HotkeyTests.Run(Check);
    SettingsTests.Run(Check);
    FullscreenTests.Run(Check);
    SharedUiTests.Run(Check);
    StabilizationTests.Run(Check);
    CalendarSourceTests.Run(Check);
    CalendarScrollTests.Run(Check);
    WallpaperInfoTests.Run(Check);
    NotesTests.Run(Check);
    DailyNotesTests.Run(Check);
    WidgetNavigationTests.Run(Check);
    SlideshowStatusUiTests.Run(Check);
    WallpaperLayoutTests.Run(Check);
    var root = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var path = Path.Combine(root, "statistics.json");
    // Creates a statistics fixture containing one wallpaper with the requested view count.
    PersistentStatisticsData Data(int views) => new()
    {
        Wallpapers = new() { new() { Path = @"C:\wallpapers\test.jpg", Views = views } }
    };
    Check(StatisticsStorage.Load(path).Wallpapers.Count == 0, "Missing files start empty");
    StatisticsStorage.SaveData(path, Data(1));
    Check(StatisticsStorage.Load(path).Wallpapers.Single().Views == 1, "First save round-trips");
    StatisticsStorage.SaveData(path, Data(2));
    Check(StatisticsStorage.Load(path).Wallpapers.Single().Views == 2 &&
          StatisticsStorage.Load(path + ".bak").Wallpapers.Single().Views == 1, "Replacement retains previous version");
    File.WriteAllText(path, "broken json");
    Check(StatisticsStorage.Load(path).Wallpapers.Single().Views == 1, "Corrupt primary recovers backup");
    Check(Directory.GetFiles(root, "statistics.json.corrupt-*").Any(p => File.ReadAllText(p) == "broken json"), "Corrupt bytes preserved");
    StatisticsStorage.SaveData(path, Data(3));
    Check(StatisticsStorage.Load(path).Wallpapers.Single().Views == 3 &&
          StatisticsStorage.Load(path + ".bak").Wallpapers.Single().Views == 1, "Save after recovery preserves good backup");
    File.WriteAllText(path, "null");
    StatisticsStorage.SaveData(path, Data(4));
    Check(StatisticsStorage.Load(path + ".bak").Wallpapers.Single().Views == 1, "Save never backs up corrupt data");
    File.WriteAllText(path, "{\"Wallpapers\":[null]}");
    File.WriteAllText(path + ".bak", "invalid backup");
    Check(StatisticsStorage.Load(path).Wallpapers.Count == 0 &&
          Directory.GetFiles(root, "*.corrupt-*").Length == 4, "Both invalid files preserved before empty fallback");
    StatisticsStorage.SaveData(path, Data(5));
    using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
    {
        bool failed = false;
        try { StatisticsStorage.SaveData(path, Data(6)); }
        catch (IOException) { failed = true; }
        Check(failed, "Locked primary is not replaced");
    }
    Check(StatisticsStorage.Load(path).Wallpapers.Single().Views == 5 && Directory.GetFiles(root, "*.tmp").Length == 0,
        "Failed save retains original and cleans temporary file");
    // Runs a child process to verify single-instance ownership and returns its exit code.
    int Probe()
    {
        using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { ArgumentList = { "--probe", instanceName }, UseShellExecute = false })!;
        if (!child.WaitForExit(5000)) { child.Kill(); throw new Exception("Probe timed out"); }
        return child.ExitCode;
    }
    using (var primary = new SingleInstanceGuard(instanceName))
    {
        Check(primary.IsPrimary && Probe() == 20, "Second process cannot own instance lock");
    }
    Check(Probe() == 10, "Lock released after primary exits");
    // Parses a command from an in-memory UTF-8 stream.
    string? Parse(string text)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        return RemoteCommandServer.ReadCommandAsync(stream, CancellationToken.None).GetAwaiter().GetResult();
    }
    Check(Parse("next\n") == "next" && Parse("show\r\n") == "show", "LF and CRLF accepted");
    Check(Parse(new string('x', 64) + "\n")?.Length == 64, "Length limit accepted");
    Check(Parse(new string('x', 65) + "\n") == null, "Oversized command rejected");
    Check(Parse("next") == null, "Incomplete command rejected");
    using (var silent = new SilentStream())
    {
        var watch = Stopwatch.StartNew();
        Check(RemoteCommandServer.ReadCommandAsync(silent, CancellationToken.None).GetAwaiter().GetResult() == null &&
            watch.Elapsed < TimeSpan.FromSeconds(5), "Silent input times out");
    }
    using (var silent = new SilentStream())
    using (var stop = new CancellationTokenSource(50))
    {
        bool cancelled = false;
        try { RemoteCommandServer.ReadCommandAsync(silent, stop.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "Shutdown cancellation propagates");
    }
    using (var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
    {
        ArgumentList = { "--fail-test" }, UseShellExecute = false, RedirectStandardError = true
    })!)
    {
        if (!child.WaitForExit(5000)) { child.Kill(); throw new Exception("Failure test timed out"); }
        Check(child.ExitCode == 1 && child.StandardError.ReadToEnd().Contains("Intentional test failure"),
            "Test failure returns exit code 1 and console diagnostic");
    }
    if (!args.Contains("--skip-pipe"))
    {
    string pipeName = "WallpaperControl.Tests." + Guid.NewGuid().ToString("N");
    var received = new BlockingCollection<string>();
    using (var server = new RemoteCommandServer(received.Add, pipeName))
    {
        server.Start();
        // Verifies that the command pipe delivers the expected next-wallpaper request.
        void ExpectNext(string label)
        {
            bool sent = false;
            for (int i = 0; i < 30 && !sent; i++)
            {
                sent = RemoteCommandServer.TrySend("next", pipeName);
                if (!sent) Thread.Sleep(50);
            }
            Check(sent && received.TryTake(out var command, 3000) && command == "next", label);
        }
        // Connects a test client to the command pipe with a bounded timeout.
        NamedPipeClientStream Connect()
        {
            var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            client.Connect(5000);
            return client;
        }
        using (var idle = Connect())
        {
            Thread.Sleep(RemoteCommandServer.CommandTimeout + TimeSpan.FromMilliseconds(200));
            ExpectNext("Listener continues after idle timeout");
        }
        using (var oversized = Connect())
        {
            oversized.Write(Encoding.UTF8.GetBytes(new string('x', RemoteCommandServer.MaxCommandLength + 1)));
            oversized.Flush();
            ExpectNext("Listener continues after oversized command");
        }
        using (var incomplete = Connect())
            incomplete.Write(Encoding.UTF8.GetBytes("show"));
        ExpectNext("Incomplete command ignored and listener continues");
        foreach (var command in new[] { "show", "next" })
        {
            bool sent = false;
            for (int i = 0; i < 20 && !sent; i++) { sent = RemoteCommandServer.TrySend(command, pipeName); if (!sent) Thread.Sleep(50); }
            Check(sent && received.TryTake(out var actual, 3000) && actual == command, "Remote command forwarded: " + command);
        }
    }
    }
    Console.WriteLine($"All {passed} checks passed. Test data: {root}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("FAIL: " + ex);
    return 1;
}

internal sealed class SilentStream : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    /// <summary>
    /// Simulates a client that sends no data until the read is canceled.
    /// </summary>
    /// <param name="buffer">The destination or source buffer for the stream operation.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A pending read task that completes by cancellation rather than receiving data.</returns>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return 0;
    }
    /// <summary>
    /// Rejects synchronous reads because this fixture models an asynchronous silent connection.
    /// </summary>
    /// <param name="buffer">The destination or source buffer for the stream operation.</param>
    /// <param name="offset">The offset required by the stream operation.</param>
    /// <param name="count">The maximum number of bytes or characters to process.</param>
    /// <returns>No value; synchronous reads are not supported by this test fixture.</returns>
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    /// <summary>
    /// Performs no work because the fixture has no output buffer.
    /// </summary>
    public override void Flush() => throw new NotSupportedException();
    /// <summary>
    /// Rejects seeking on the non-seekable test stream.
    /// </summary>
    /// <param name="offset">The offset required by the stream operation.</param>
    /// <param name="origin">The reference point for the requested seek operation.</param>
    /// <returns>No value; seeking is not supported by this test fixture.</returns>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    /// <summary>
    /// Rejects length changes on the read-only test stream.
    /// </summary>
    /// <param name="value">The requested stream length required by the stream contract.</param>
    public override void SetLength(long value) => throw new NotSupportedException();
    /// <summary>
    /// Rejects writes on the read-only test stream.
    /// </summary>
    /// <param name="buffer">The destination or source buffer for the stream operation.</param>
    /// <param name="offset">The offset required by the stream operation.</param>
    /// <param name="count">The maximum number of bytes or characters to process.</param>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

namespace WallpaperControl
{
    internal static class AppLogger
    {
        /// <summary>
        /// Records a recoverable problem with its context and exception details.
        /// </summary>
        /// <param name="message">The warning message to append to the application log.</param>
        /// <param name="ex">The exception associated with the failure.</param>
        internal static void Warning(string message, Exception ex) => Console.WriteLine("Diagnostic: " + message + " " + ex.Message);
    }
}
