using System.Text.Json;

namespace WallpaperControl;

internal sealed class VisitorWidgetSettings
{
    public bool Enabled { get; set; }
    public bool Locked { get; set; }
    public Point Location { get; set; } = new(40, 140);
    public SystemWidgetStyle Style { get; set; }
    public int ScalePercent { get; set; } = 100;
    public int FontSize { get; set; } = 11;
    public int? BackgroundArgb { get; set; }
    public int? TextArgb { get; set; }
    public int? BackgroundOpacityPercent { get; set; }
    internal VisitorWidgetSettings Clone() => (VisitorWidgetSettings)MemberwiseClone();
    internal static VisitorWidgetSettings Parse(string json)
    {
        try
        {
            var value = JsonSerializer.Deserialize<VisitorWidgetSettings>(json) ?? new();
            value.ScalePercent = Math.Clamp(value.ScalePercent, 50, 200);
            value.FontSize = Math.Clamp(value.FontSize, 8, 24);
            if (value.BackgroundOpacityPercent is int opacity) value.BackgroundOpacityPercent = Math.Clamp(opacity, 0, 100);
            if (!Enum.IsDefined(value.Style)) value.Style = SystemWidgetStyle.Minimal;
            return value;
        }
        catch (JsonException) { return new(); }
    }
    internal static VisitorWidgetSettings Load(Microsoft.Win32.RegistryKey key)
    {
        if (key.GetValue("VisitorWidgetConfiguration") is string json) return Parse(json);
        // Read the old widget values once as defaults, without modifying visitor media or activation.
        try
        {
            var legacy = JsonSerializer.Deserialize<VisitorModeSettings>(key.GetValue("VisitorMode") as string ?? "{}");
            return new() { Enabled = legacy?.WidgetEnabled ?? false, Locked = legacy?.WidgetLocked ?? false,
                Location = new(legacy?.WidgetX ?? 40, legacy?.WidgetY ?? 140) };
        }
        catch (JsonException) { return new(); }
    }
}
