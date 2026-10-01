using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace WallpaperControl;

internal readonly record struct RuntimeResourceSample(int ProcessId, TimeSpan CpuTime, long WorkingSetBytes, long PrivateMemoryBytes, int HandleCount = 0);
internal readonly record struct RuntimeResourceContext(WallpaperOperatingMode Mode, VideoWallpaperState VideoState, VideoPauseReason PauseReasons, bool ChangePending);

// Driven by the existing UI poll. One native process refresh and one existing
// log entry per minute; no timer, history buffer, per-frame work or GC forcing.
internal sealed class RuntimeResourceTelemetry : IDisposable
{
    [DllImport("kernel32.dll")] private static extern bool GetProcessHandleCount(nint process, out uint count);
    internal static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private readonly Func<RuntimeResourceSample> read;
    private readonly Func<TimeSpan> elapsed;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly Action<string> log;
    private readonly int processors;
    private readonly IDisposable? owner;
    private TimeSpan nextSample, previousElapsed;
    private RuntimeResourceSample? previous;
    private bool disposed;

    internal RuntimeResourceTelemetry(Func<RuntimeResourceSample> read, Func<TimeSpan> elapsed, Func<DateTimeOffset> utcNow,
        Action<string> log, int processors, IDisposable? owner = null)
    {
        this.read = read; this.elapsed = elapsed; this.utcNow = utcNow; this.log = log;
        this.processors = Math.Max(1, processors); this.owner = owner;
    }
    internal static RuntimeResourceTelemetry Create(Action<string> log)
    {
        var process = Process.GetCurrentProcess();
        int pid = process.Id;
        long start = Stopwatch.GetTimestamp();
        return new(() =>
        {
            process.Refresh();
            return new(pid, process.TotalProcessorTime, process.WorkingSet64, process.PrivateMemorySize64,
                GetProcessHandleCount(process.Handle, out uint handles) ? checked((int)handles) : -1);
        }, () => Stopwatch.GetElapsedTime(start), () => DateTimeOffset.UtcNow, log, Environment.ProcessorCount, process);
    }
    internal void Observe(RuntimeResourceContext context)
    {
        if (disposed) return;
        var time = elapsed();
        if (time < nextSample) return;
        nextSample = time + Interval; // Never catch up missed samples in a burst.
        try
        {
            var stamp = utcNow();
            var sample = read();
            double seconds = (time - previousElapsed).TotalSeconds;
            string cpu = previous is { } last && last.ProcessId == sample.ProcessId && seconds > 0
                ? Math.Clamp((sample.CpuTime - last.CpuTime).TotalSeconds / seconds / processors * 100, 0, 100).ToString("F3", CultureInfo.InvariantCulture)
                : "n/a";
            log(FormattableString.Invariant($"Runtime resources: sampleUtc={stamp:O}; pid={sample.ProcessId}; mode={context.Mode}; videoState={context.VideoState}; pauseReasons={context.PauseReasons}; changePending={context.ChangePending}; cpuPercentMachine={cpu}; cpuWindowSeconds={(previous != null ? seconds : 0):F3}; workingSetMiB={sample.WorkingSetBytes / 1048576.0:F2}; privateMemoryMiB={sample.PrivateMemoryBytes / 1048576.0:F2}; handleCount={sample.HandleCount}; intervalSeconds={Interval.TotalSeconds:F0}"));
            previous = sample; previousElapsed = time;
        }
        catch (Exception ex)
        {
            log($"Runtime resources: sample unavailable; type={ex.GetType().Name}; hr=0x{ex.HResult:X8}; retrySeconds=60");
        }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; owner?.Dispose();
    }
}
