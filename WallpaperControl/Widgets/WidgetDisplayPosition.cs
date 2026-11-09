namespace WallpaperControl;

// Display/DPI recovery may need a visible fallback, but only a deliberate move
// changes the preferred position. Never learn a temporary Windows relocation.
internal sealed class WidgetDisplayPosition(Point preferred)
{
    internal Point Preferred { get; private set; } = preferred;
    internal Func<Rectangle, Rectangle> WorkingAreaForBounds { get; set; } = bounds => Screen.FromRectangle(bounds).WorkingArea;

    internal void Remember(Point location) => Preferred = location;
    internal Rectangle WorkingArea(Size size) => WorkingAreaForBounds(new Rectangle(Preferred, size));
    internal Point Resolve(Size size, Rectangle area) => new(
        Math.Clamp(Preferred.X, area.Left, Math.Max(area.Left, area.Right - size.Width)),
        Math.Clamp(Preferred.Y, area.Top, Math.Max(area.Top, area.Bottom - size.Height)));
}
