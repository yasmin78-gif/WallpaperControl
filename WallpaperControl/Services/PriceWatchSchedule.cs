namespace WallpaperControl;

internal static class PriceWatchSchedule
{
    internal static DateTimeOffset Slot(DateTimeOffset now, TimeZoneInfo zone, bool next)
    {
        var local = TimeZoneInfo.ConvertTime(now, zone).DateTime;
        DateTime slot;
        if (next)
            slot = local.Hour < 8 ? local.Date.AddHours(8) : local.Hour < 20 ? local.Date.AddHours(20)
                : local.Date.AddDays(1).AddHours(8);
        else
            slot = local.Hour >= 20 ? local.Date.AddHours(20) : local.Hour >= 8 ? local.Date.AddHours(8)
                : local.Date.AddDays(-1).AddHours(20);
        return new(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(slot, DateTimeKind.Unspecified), zone));
    }

    internal static bool Due(DateTimeOffset now, TimeZoneInfo zone, PriceWatchMonitorState state, bool startup)
    {
        if (state.Success is not { } success) return true;
        // A catch-up just before the next fixed slot must not produce a second immediate cycle.
        if (now - success < TimeSpan.FromHours(1)) return false;
        return startup ? now - success >= TimeSpan.FromHours(12) : success < Slot(now, zone, next: false);
    }
}
