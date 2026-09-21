using System.Text.Json;

namespace WallpaperControl;

/// <summary>Configuration for the single Web instance; geometry is stored in logical pixels.</summary>
internal sealed class WebWidgetSettings
{
    public bool Enabled { get; set; }
    public string DisplayName { get; set; } = "";
    public string Url { get; set; } = "";
    public int Zoom { get; set; } = 100;
    public bool AllowInteraction { get; set; } = true;
    public bool ReloadOnStartup { get; set; } = true;
    public bool Locked { get; set; }
    public bool Collapsed { get; set; }
    public int X { get; set; } = 100;
    public int Y { get; set; } = 100;
    public int Width { get; set; } = 640;
    public int Height { get; set; } = 420;
    public SystemWidgetStyle Style { get; set; } = SystemWidgetStyle.Clean;

    internal WebWidgetSettings Clone() => (WebWidgetSettings)MemberwiseClone();
    internal void CopyGeometry(WebWidgetSettings source)
    {
        X = source.X; Y = source.Y; Width = source.Width; Height = source.Height; Collapsed = source.Collapsed;
    }
    internal static bool IsValidUrl(string? value) => !string.IsNullOrWhiteSpace(value) &&
        Uri.IsWellFormedUriString(value.Trim(), UriKind.Absolute) &&
        Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
        !string.IsNullOrEmpty(uri.Host) && string.IsNullOrEmpty(uri.UserInfo);
    internal void Normalize()
    {
        DisplayName = DisplayName?.Trim() ?? ""; Url = Url?.Trim() ?? "";
        Zoom = Math.Clamp(Zoom, 50, 200); Width = Math.Clamp(Width, 320, 16384); Height = Math.Clamp(Height, 200, 16384);
        if (!Enum.IsDefined(Style)) Style = SystemWidgetStyle.Clean;
    }
    internal static WebWidgetSettings Parse(string json)
    {
        WebWidgetSettings value = JsonSerializer.Deserialize<WebWidgetSettings>(json) ?? new();
        value.Normalize(); return value;
    }
    internal static Rectangle ClampBounds(Point position, Size logicalSize, Rectangle area, float scale, bool collapsed)
    {
        int width = Math.Min(area.Width, (int)Math.Round(Math.Max(320, logicalSize.Width) * scale));
        int height = Math.Min(area.Height, (int)Math.Round((collapsed ? 36 : Math.Max(200, logicalSize.Height)) * scale));
        return new Rectangle(Math.Clamp(position.X, area.Left, area.Right - width),
            Math.Clamp(position.Y, area.Top, area.Bottom - height), width, height);
    }
}
