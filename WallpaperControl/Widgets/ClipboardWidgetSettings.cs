using System.Text.Json;
namespace WallpaperControl;
internal sealed class ClipboardWidgetSettings
{
    public bool Enabled { get; set; }
    public bool Locked { get; set; }
    public SystemWidgetStyle Style { get; set; }
    public int Capacity { get; set; } = 10;
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
            result.MaximumHeight = CalendarViewport.NormalizeMaximum(result.MaximumHeight);
            if (!Enum.IsDefined(result.Style)) result.Style = SystemWidgetStyle.Minimal;
            return result;
        }
        catch (JsonException) { return new(); }
    }
}
internal sealed record ClipboardEntry(Guid Id,string Text,DateTimeOffset Copied);
/// <summary>Only this process's RAM owns entries; never serialize or log them.</summary>
internal sealed class ClipboardHistory
{
    internal const int MaximumTextCharacters = 524288;
    private readonly List<ClipboardEntry> entries = [];
    internal IReadOnlyList<ClipboardEntry> Entries => entries;
    internal bool Paused { get; set; }
    private int capacity = 10;
    internal void Configure(int value) { capacity = ClipboardWidgetSettings.Capacities.Contains(value) ? value : 10; if(entries.Count > capacity) entries.RemoveRange(capacity,entries.Count-capacity); }
    internal bool Observe(string? text,DateTimeOffset copied)
    {
        if (Paused || string.IsNullOrWhiteSpace(text) || text.Length > MaximumTextCharacters) return false;
        var existing = entries.FirstOrDefault(e=>e.Text == text);
        if (existing != null) entries.Remove(existing);
        entries.Insert(0,new(existing?.Id ?? Guid.NewGuid(),text,copied));
        if(entries.Count > capacity) entries.RemoveAt(entries.Count-1);
        return true;
    }
    internal void Remove(Guid id) => entries.RemoveAll(e=>e.Id==id);
    internal void Clear() => entries.Clear();
}
