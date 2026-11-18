namespace WallpaperControl;

internal static class PriceWatchTitleScroll
{
    internal static float Offset(double elapsedSeconds, float overflow, float dpiScale)
    {
        if (overflow <= 0 || dpiScale <= 0) return 0;
        const double speed = 24;
        double travel = overflow / speed;
        double time = elapsedSeconds % (0.5 + travel + 1 + travel);
        double offset = time < 0.5 ? 0 : time < 0.5 + travel ? (time - 0.5) * speed
            : time < 1.5 + travel ? overflow : overflow - (time - 1.5 - travel) * speed;
        return Math.Clamp((float)(Math.Round(offset * dpiScale) / dpiScale), 0, overflow);
    }
}
