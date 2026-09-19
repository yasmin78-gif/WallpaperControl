using System.Drawing;
using WallpaperControl;

internal static class FullscreenTests
{
    /// <summary>
    /// Runs the fullscreen regression checks using the supplied assertion callback.
    /// </summary>
    /// <param name="check">The assertion callback that records a passing check or throws on failure.</param>
    internal static void Run(Action<bool, string> check)
    {
        var monitor = new Rectangle(0, 0, 1920, 1080);
        check(FullscreenActivityDetector.CoversMonitor(monitor, monitor), "Fullscreen monitor coverage recognized");
        check(!FullscreenActivityDetector.CoversMonitor(new Rectangle(0, 0, 1920, 1040), monitor),
            "Normal work-area sized window does not trigger fullscreen");
        check(FullscreenActivityDetector.CoversMonitor(new Rectangle(-2560, 0, 2560, 1440), new Rectangle(-2560, 0, 2560, 1440)),
            "Fullscreen recognized on a secondary monitor with negative coordinates");
        check(!FullscreenActivityDetector.CoversMonitor(new Rectangle(200, 200, 800, 600), monitor),
            "Windowed applications do not trigger fullscreen");
        check(!FullscreenActivityDetector.CoversMonitor(monitor, Rectangle.Empty), "Invalid monitor geometry is ignored");
        var policy = new FullscreenPausePolicy();
        DateTime now = new(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);
        check(policy.Update(true, true, now) && policy.IsPaused && !policy.AllowsSlideshow(false),
            "Fullscreen immediately suspends the slideshow");
        check(!policy.Update(true, true, now.AddSeconds(1)), "Steady fullscreen state does not repeatedly suspend");
        check(!policy.Update(true, false, now.AddSeconds(2)) && policy.IsPaused,
            "Leaving fullscreen starts a grace period");
        check(!policy.Update(true, true, now.AddSeconds(3)) && policy.IsPaused,
            "Quick Alt-Tab back to fullscreen cancels pending resume");
        policy.Update(true, false, now.AddSeconds(4));
        check(!policy.Update(true, false, now.AddSeconds(4.25)) && policy.IsPaused,
            "Resume still waits after one clear polling interval");
        check(policy.Update(true, false, now.AddSeconds(4.5)) && policy.AllowsSlideshow(false) && !policy.AllowsSlideshow(true),
            "Automatic resume preserves a manual pause");
        policy.Update(true, true, now.AddSeconds(7));
        check(policy.Update(false, true, now.AddSeconds(8)) && !policy.IsPaused && !policy.AllowsSlideshow(true),
            "Disabling fullscreen protection releases only the automatic pause");
        check(!policy.Update(false, true, now.AddSeconds(9)), "Disabled protection ignores fullscreen activity");
        ExitTiming(check, now);
    }

    private static void ExitTiming(Action<bool, string> check, DateTime now)
    {
        var policy = new FullscreenPausePolicy();
        check(policy.PollingIntervalMilliseconds == 1000, "Fullscreen entry retains the normal one-second polling interval");
        policy.Update(true, true, now);
        check(policy.PollingIntervalMilliseconds == 250, "Fullscreen pause accelerates exit polling to 250 ms");
        policy.Update(true, false, now.AddMilliseconds(250));
        check(!policy.Update(true, true, now.AddMilliseconds(500)) && policy.IsPaused,
            "One false-negative fullscreen sample cannot resume activity");
        policy.Update(true, false, now.AddMilliseconds(750));
        check(!policy.Update(true, false, now.AddMilliseconds(1249)) && policy.IsPaused,
            "Returning fullscreen resets the full 500 ms stability window");
        check(policy.Update(true, false, now.AddMilliseconds(1250)) && !policy.IsPaused,
            "Sustained fullscreen exit resumes exactly at the 500 ms stability boundary");
        check(policy.PollingIntervalMilliseconds == 1000 && !policy.AllowsSlideshow(true),
            "Resume restores normal polling and preserves manual pause");
        check(!policy.Update(true, false, now.AddMilliseconds(1500)), "Repeated clear samples do not notify resume again");
        policy.Update(true, true, now.AddMilliseconds(1750));
        check(policy.IsPaused && policy.PollingIntervalMilliseconds == 250,
            "Re-entering fullscreen pauses immediately without entry debounce");
        policy.Update(false, true, now.AddMilliseconds(1800));
        check(!policy.IsPaused && policy.PollingIntervalMilliseconds == 1000,
            "Disabling protection restores normal polling immediately");

        // Model every integer-millisecond phase of the periodic poll. No real
        // clock, Thread.Sleep, shell timing or scheduling jitter is involved.
        var oldLatencies = Enumerable.Range(0, 1000).Select(phase => phase + 2000).ToArray();
        var newLatencies = Enumerable.Range(0, 250).Select(phase =>
        {
            var sample = new FullscreenPausePolicy();
            sample.Update(true, true, now.AddMilliseconds(-1));
            for (int elapsed = phase; elapsed <= 1000; elapsed += sample.PollingIntervalMilliseconds)
                if (sample.Update(true, false, now.AddMilliseconds(elapsed))) return elapsed;
            throw new Exception("Sustained exit did not resume within a second.");
        }).ToArray();
        check(oldLatencies.Min() == 2000 && oldLatencies.Max() == 2999 && oldLatencies.Average() == 2499.5,
            "Previous 1000 ms polling plus 2000 ms grace explains roughly 2-3 seconds");
        check(newLatencies.Min() == 500 && newLatencies.Max() == 749 && newLatencies.Average() == 624.5,
            "All 250 poll phases resume within 500-749 ms, averaging 624.5 ms");
    }
}
