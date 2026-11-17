using System.Text.Json;

namespace WallpaperControl;

internal sealed class PriceWatchWidgetSettings
{
    public bool Enabled { get; set; }
    public bool Locked { get; set; }
    public bool Live { get; set; }
    public SystemWidgetStyle Style { get; set; }
    public Point Location { get; set; } = new(35, 330);
    public PriceWatchGame? TestSelection { get; set; }
    public PriceWatchGame? LiveSelection { get; set; }

    internal PriceWatchGame? Selection => Live ? LiveSelection : TestSelection;
    internal PriceWatchWidgetSettings Clone() => (PriceWatchWidgetSettings)MemberwiseClone();

    internal static PriceWatchWidgetSettings Parse(string json)
    {
        try
        {
            var result = JsonSerializer.Deserialize<PriceWatchWidgetSettings>(json) ?? new();
            if (!Enum.IsDefined(result.Style))
                result.Style = SystemWidgetStyle.Minimal;
            return result;
        }
        catch (JsonException)
        {
            return new();
        }
    }
}
