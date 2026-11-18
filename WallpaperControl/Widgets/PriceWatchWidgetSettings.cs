using System.Text.Json;

namespace WallpaperControl;

internal sealed class PriceWatchWidgetSettings
{
    public bool Enabled { get; set; }
    public bool Locked { get; set; }
    public bool DetailSizeLocked { get; set; }
    public bool Live { get; set; }
    public int[] ShopIds { get; set; } = PriceWatchShops.Defaults;
    public SystemWidgetStyle Style { get; set; }
    public Point Location { get; set; } = new(35, 330);
    public PriceWatchGame? TestSelection { get; set; }
    public PriceWatchGame? LiveSelection { get; set; }

    internal PriceWatchGame? Selection => Live ? LiveSelection : TestSelection;
    internal PriceWatchWidgetSettings Clone()
    {
        var result = (PriceWatchWidgetSettings)MemberwiseClone();
        result.ShopIds = PriceWatchShops.Normalize(ShopIds);
        return result;
    }

    internal static PriceWatchWidgetSettings Parse(string json)
    {
        try
        {
            var result = JsonSerializer.Deserialize<PriceWatchWidgetSettings>(json) ?? new();
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return new();
            result.ShopIds = document.RootElement.TryGetProperty(nameof(ShopIds), out _)
                ? PriceWatchShops.Normalize(result.ShopIds) : PriceWatchShops.Legacy;
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
