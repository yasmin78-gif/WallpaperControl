namespace WallpaperControl
{
    /// <summary>Shared all-time/period summary; the denominator is the number of tracked wallpapers.</summary>
    internal readonly record struct WallpaperViewSummary(int TotalViews, double AverageViews, int RecordViews)
    {
        internal static WallpaperViewSummary Calculate(IReadOnlyDictionary<string, int> counts)
        {
            int total = counts.Values.Sum();
            return new(total, counts.Count == 0 ? 0 : (double)total / counts.Count,
                counts.Count == 0 ? 0 : counts.Values.Max());
        }
    }
}
