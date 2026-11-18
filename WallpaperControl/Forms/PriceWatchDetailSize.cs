namespace WallpaperControl;

internal static class PriceWatchDetailSize
{
    internal const int StandardWidth = 700;
    internal const int StandardHeight = 580;
    private const int MinimumWidth = 440;
    private const int MaximumWidth = 1600;
    private const double HeightPerWidth = (double)StandardHeight / StandardWidth;

    internal static bool IsValidWidth(int width) => width is >= MinimumWidth and <= MaximumWidth;

    internal static int ToLogical(int pixels, int dpi) => (int)Math.Round(pixels * 96d / dpi);

    internal static Size Constrain(double width, int dpi, Size workArea)
    {
        int maximum = Math.Min((int)Math.Round(MaximumWidth * dpi / 96d),
            Math.Min(workArea.Width, (int)Math.Floor(workArea.Height / HeightPerWidth)));
        int minimum = Math.Min(maximum, (int)Math.Round(MinimumWidth * dpi / 96d));
        int resultWidth = (int)Math.Round(Math.Clamp(width, minimum, maximum));
        return new(resultWidth, (int)Math.Round(resultWidth * HeightPerWidth));
    }

    // Native WMSZ values indicate which sides move. Keep the opposite sides anchored.
    internal static Rectangle Resize(Rectangle proposed, int edge, int dpi, Rectangle workArea)
    {
        double width = edge switch
        {
            1 or 2 => proposed.Width,
            3 or 6 => proposed.Height / HeightPerWidth,
            _ => (proposed.Width + proposed.Height * HeightPerWidth) / (1 + HeightPerWidth * HeightPerWidth)
        };
        bool leftEdge = edge is 1 or 4 or 7, topEdge = edge is 3 or 4 or 5;
        int availableWidth = edge is 3 or 6 ? workArea.Width
            : leftEdge ? proposed.Right - workArea.Left : workArea.Right - proposed.Left;
        int availableHeight = edge is 1 or 2 ? workArea.Height
            : topEdge ? proposed.Bottom - workArea.Top : workArea.Bottom - proposed.Top;
        Size minimum = Constrain(0, dpi, workArea.Size);
        Size size = Constrain(width, dpi, new(
            Math.Clamp(availableWidth, minimum.Width, workArea.Width),
            Math.Clamp(availableHeight, minimum.Height, workArea.Height)));
        int left = leftEdge ? proposed.Right - size.Width : proposed.Left;
        int top = topEdge ? proposed.Bottom - size.Height : proposed.Top;
        return PriceWatchDetailForm.FitBounds(new(new Point(left, top), size), workArea);
    }
}
