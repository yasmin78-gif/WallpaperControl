using System.Text.Json;
namespace WallpaperControl;
internal sealed class ClipboardWidgetSettings
{
    public bool Enabled { get; set; }
    public bool Locked { get; set; }
    public SystemWidgetStyle Style { get; set; }
    public int Capacity { get; set; } = 10;
    public bool CaptureScreenshots { get; set; } = true;
    public int ScreenshotLifetimeHours { get; set; } = 24;
    public int ScreenshotCapacity { get; set; } = 20;
    public bool KeepScreenshots { get; set; } = true;
    internal static readonly int[] ScreenshotCapacities=[10,20,50];
    internal static readonly int[] ScreenshotLifetimes=[1,24,168,0];
    public int MaximumHeight { get; set; } = 500;
    public Point Location { get; set; } = new(700, 300);
    internal static readonly int[] Capacities = [5,10,20,50];
    internal ClipboardWidgetSettings Clone() => (ClipboardWidgetSettings)MemberwiseClone();
    internal static ClipboardWidgetSettings Parse(string json)
    {
        try
        {
            var result = JsonSerializer.Deserialize<ClipboardWidgetSettings>(json) ?? new();
            if (!Capacities.Contains(result.Capacity)) result.Capacity = 10;
            if(!ScreenshotCapacities.Contains(result.ScreenshotCapacity))result.ScreenshotCapacity=20;
            if(!ScreenshotLifetimes.Contains(result.ScreenshotLifetimeHours))result.ScreenshotLifetimeHours=24;
            result.MaximumHeight = CalendarViewport.NormalizeMaximum(result.MaximumHeight);
            if (!Enum.IsDefined(result.Style)) result.Style = SystemWidgetStyle.Minimal;
            return result;
        }
        catch (JsonException) { return new(); }
    }
}
internal sealed record ClipboardEntry(Guid Id,string Text,DateTimeOffset Copied,ClipboardScreenshot? Screenshot=null)
{ internal bool IsImage=>Screenshot!=null; }
/// <summary>Mixed view of RAM-only text and screenshot metadata. Never serialize or log the text entries.</summary>
internal sealed class ClipboardHistory
{
    internal const int MaximumTextCharacters = 524288;
    private readonly List<ClipboardEntry> entries = [];
    internal IReadOnlyList<ClipboardEntry> Entries => entries;
    internal bool Paused { get; set; }
    private int capacity = 10;
    internal void Configure(int value) { capacity = ClipboardWidgetSettings.Capacities.Contains(value) ? value : 10; TrimTexts(); }
    private void TrimTexts() { foreach(var item in entries.Where(e=>!e.IsImage).Skip(capacity).ToArray())entries.Remove(item); }
    internal bool Observe(string? text,DateTimeOffset copied)
    {
        if (Paused || string.IsNullOrWhiteSpace(text) || text.Length > MaximumTextCharacters) return false;
        var existing = entries.FirstOrDefault(e=>!e.IsImage&&e.Text == text);
        if (existing != null) entries.Remove(existing);
        entries.Insert(0,new(existing?.Id ?? Guid.NewGuid(),text,copied));
        TrimTexts();
        return true;
    }
    internal void Remove(Guid id) => entries.RemoveAll(e=>e.Id==id);
    internal void ReplaceScreenshots(IEnumerable<ClipboardScreenshot> images)
    { entries.RemoveAll(e=>e.IsImage); entries.AddRange(images.Select(image=>new ClipboardEntry(image.Id,"",image.Copied,image))); var ordered=entries.OrderByDescending(e=>e.Copied).ToArray(); entries.Clear(); entries.AddRange(ordered); }
    internal void Clear() => entries.Clear();
}
