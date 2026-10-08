using System.Text.Json;
namespace WallpaperControl;
internal enum FeedPeriod { All, Day, Week, Month }
internal enum FeedDisplayMode { List, FocusFirst, Titles }
internal sealed record FeedSource
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Title { get; init; } = "";
    public string Url { get; init; } = "";
    public int RefreshMinutes { get; init; } = 15;
    public bool Notifications { get; init; }
    internal static readonly int[] Intervals = [15, 30, 60, 120, 360, 720, 1440, 10080, 0];
}
internal sealed class FeedWidgetSettings
{
    public bool Enabled { get; set; }
    public bool Locked { get; set; }
    public SystemWidgetStyle Style { get; set; } = SystemWidgetStyle.Minimal;
    public int MaximumHeight { get; set; } = 500;
    public Point Location { get; set; } = new(700, 40);
    public FeedDisplayMode Display { get; set; }
    public FeedPeriod Period { get; set; }
    public Guid? Selected { get; set; }
    public List<FeedSource> Sources { get; set; } = [];
    internal FeedWidgetSettings Clone() => Parse(JsonSerializer.Serialize(this));
    internal static FeedWidgetSettings Parse(string json)
    {
        try
        {
            var result = JsonSerializer.Deserialize<FeedWidgetSettings>(json) ?? new();
            result.Sources = (result.Sources ?? []).Where(s => s != null && FeedParser.WebUri(s.Url) != null)
                .DistinctBy(s => s.Id).Take(20).Select(s => s with { RefreshMinutes = FeedSource.Intervals.Contains(s.RefreshMinutes) ? s.RefreshMinutes : 15 }).ToList();
            result.MaximumHeight = CalendarViewport.NormalizeMaximum(result.MaximumHeight);
            if (!Enum.IsDefined(result.Period)) result.Period = FeedPeriod.All;
            if (!Enum.IsDefined(result.Display)) result.Display = FeedDisplayMode.List;
            if (result.Selected != null && !result.Sources.Any(s => s.Id == result.Selected)) result.Selected = null;
            return result;
        }
        catch (JsonException) { return new(); }
    }
}
