extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using System.Globalization;

internal static class RuntimeResourceTelemetryTests
{
    private sealed class Owner : IDisposable
    {
        internal int Disposals;
        public void Dispose() => Disposals++;
    }
    internal static void Run(Action<bool, string> check)
    {
        var time = TimeSpan.Zero;
        var stamp = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        var sample = new App.RuntimeResourceSample(42, TimeSpan.Zero, 128L * 1048576, 256L * 1048576);
        var context = new App.RuntimeResourceContext(App.WallpaperOperatingMode.ImageSlideshow, App.VideoWallpaperState.Stopped, App.VideoPauseReason.None, false);
        int reads = 0;
        var lines = new List<string>();
        var owner = new Owner();
        using var telemetry = new App.RuntimeResourceTelemetry(() => { reads++; return sample; }, () => time, () => stamp, lines.Add, 4, owner);
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new("de-DE");
            telemetry.Observe(context);
            check(reads == 1 && lines[0].Contains("cpuPercentMachine=n/a") && lines[0].Contains("workingSetMiB=128.00") && lines[0].Contains("privateMemoryMiB=256.00"),
                "Telemetry: first process memory sample uses MiB and invariant decimals; CPU has explicit baseline");
            check(lines[0].Contains("pid=42") && lines[0].Contains("sampleUtc=2026-10-01T08:00:00") && lines[0].Contains("mode=ImageSlideshow"),
                "Telemetry: process, timestamp and image mode identify each sample");
            for (int i = 1; i < 600; i++) { time = TimeSpan.FromMilliseconds(i * 100); telemetry.Observe(context); }
            check(reads == 1 && lines.Count == 1, "Telemetry: frequent UI polls perform no additional process reads or logging");
            time = TimeSpan.FromSeconds(60); stamp = stamp.AddMinutes(1);
            sample = sample with { CpuTime = TimeSpan.FromSeconds(1.2), WorkingSetBytes = 130L * 1048576 };
            telemetry.Observe(context with { Mode = App.WallpaperOperatingMode.VideoWallpaper, VideoState = App.VideoWallpaperState.Playing });
            check(lines[^1].Contains("cpuPercentMachine=0.500") && lines[^1].Contains("cpuWindowSeconds=60.000") && lines[^1].Contains("workingSetMiB=130.00"),
                "Telemetry: CPU and RAM share the same real measurement window and line");
            check(lines[^1].Contains("mode=VideoWallpaper") && lines[^1].Contains("videoState=Playing"), "Telemetry: running video is identified");
            time = TimeSpan.FromSeconds(180); stamp = stamp.AddMinutes(2);
            telemetry.Observe(context with { Mode = App.WallpaperOperatingMode.VideoWallpaper, VideoState = App.VideoWallpaperState.Paused,
                PauseReasons = App.VideoPauseReason.Manual | App.VideoPauseReason.Fullscreen, ChangePending = true });
            check(reads == 3 && lines.Count == 3 && lines[^1].Contains("videoState=Paused") && lines[^1].Contains("pauseReasons=Manual, Fullscreen") &&
                lines[^1].Contains("changePending=True") && lines[^1].Contains("cpuWindowSeconds=120.000"),
                "Telemetry: paused video and combined reasons are recorded; delayed polls do not catch up in bursts");
            telemetry.Dispose(); telemetry.Dispose();
            time = TimeSpan.FromHours(8); telemetry.Observe(context);
            check(reads == 3 && owner.Disposals == 1, "Telemetry: deterministic idempotent disposal stops all sampling");
        }
        finally { CultureInfo.CurrentCulture = saved; }
        time = TimeSpan.Zero; reads = 0; lines.Clear();
        using var unavailable = new App.RuntimeResourceTelemetry(() => { reads++; throw new IOException("unavailable"); }, () => time, () => stamp, lines.Add, 4);
        unavailable.Observe(context);
        for (int i = 0; i < 100; i++) unavailable.Observe(context);
        check(reads == 1 && lines.Count == 1 && lines[0].Contains("sample unavailable") && lines[0].Contains("hr=0x"),
            "Telemetry: sampling failure cannot crash or flood the UI poll");
        time = TimeSpan.FromMinutes(1); unavailable.Observe(context);
        check(reads == 2 && lines.Count == 2, "Telemetry: failed process read retries at the normal interval");
        lines.Clear();
        using var native = App.RuntimeResourceTelemetry.Create(lines.Add);
        native.Observe(context);
        check(lines.Count == 1 && lines[0].Contains("workingSetMiB=") && lines[0].Contains("privateMemoryMiB=") && !lines[0].Contains("unavailable"),
            "Telemetry: real process memory counters available without extra workers");
    }
}
