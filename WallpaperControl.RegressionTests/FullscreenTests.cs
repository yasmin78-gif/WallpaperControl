using System.Drawing;
using WallpaperControl;

internal static class FullscreenTests
{
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
        check(!policy.Update(true, false, now.AddSeconds(5)) && policy.IsPaused,
            "Resume waits for two clear seconds");
        check(policy.Update(true, false, now.AddSeconds(6)) && policy.AllowsSlideshow(false) && !policy.AllowsSlideshow(true),
            "Automatic resume preserves a manual pause");
        policy.Update(true, true, now.AddSeconds(7));
        check(policy.Update(false, true, now.AddSeconds(8)) && !policy.IsPaused && !policy.AllowsSlideshow(true),
            "Disabling fullscreen protection releases only the automatic pause");
        check(!policy.Update(false, true, now.AddSeconds(9)), "Disabled protection ignores fullscreen activity");
    }
}
