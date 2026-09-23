namespace WallpaperControl;

public partial class MainForm
{
    private string? lastSchedulerState;
    private DateTime nextSchedulerAnomalyLogUtc;
    private long schedulerCallbackUtcTicks;
    private long schedulerDispatchUtcTicks;
    private bool schedulerIntervalRetryLogged;
    private DateTime schedulerLastArmedDeadline;

    private string SchedulerState =>
        $"active={customSlideshowEngineActive}; manualPause={slideshowPaused}; " +
        $"fullscreenPause={fullscreenPolicy.IsPaused}; changing={customSlideshowChangeRunning}; " +
        $"intervalMs={customSlideshowLastInterval}; deadline={customSlideshowNextChange:O}";

    private void LogScheduler(string decision) => AppLogger.Info($"Slideshow: {decision}; {SchedulerState}");

    // Observe using the existing UI poll. Never re-arm or change scheduling here.
    private void ObserveScheduler()
    {
        string state = SchedulerState;
        if (state != lastSchedulerState)
        {
            lastSchedulerState = state;
            LogScheduler("state changed");
        }
        bool eligible = customSlideshowEngineActive && fullscreenPolicy.AllowsSlideshow(slideshowPaused);
        bool invalid = customSlideshowNextChange == DateTime.MaxValue;
        bool overdue = !invalid && DateTime.Now - customSlideshowNextChange > TimeSpan.FromSeconds(10);
        if (!eligible || (!invalid && !overdue)) { nextSchedulerAnomalyLogUtc = default; return; }
        DateTime now = DateTime.UtcNow;
        if (now < nextSchedulerAnomalyLogUtc) return;
        nextSchedulerAnomalyLogUtc = now.AddMinutes(1);
        LogScheduler($"anomaly={(invalid ? "invalid-deadline" : "overdue-deadline")}; " +
            $"lastCallbackUtc={new DateTime(Interlocked.Read(ref schedulerCallbackUtcTicks), DateTimeKind.Utc):O}; " +
            $"lastDispatchUtc={new DateTime(Interlocked.Read(ref schedulerDispatchUtcTicks), DateTimeKind.Utc):O}; recovery=none");
    }

    // Existing completion policy, shared by automatic and manual changes.
    private void CompleteCustomSlideshowSchedule(bool automatic)
    {
        bool overdue = customSlideshowNextChange <= DateTime.Now && customSlideshowLastInterval > 0;
        if (overdue)
            customSlideshowNextChange = GetNextAlignedChange(DateTime.Now, customSlideshowLastInterval);
        ArmCustomSlideshowPreciseTimer();
        LogScheduler($"{(automatic ? "automatic" : "manual")} completion; deadlineRecalculated={overdue}; timer re-arm requested");
    }
}
