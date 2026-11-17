using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace WallpaperControl;

internal readonly record struct PriceWatchWidgetLayout(float Scale, RectangleF Search, RectangleF Refresh,
    RectangleF Mode, RectangleF Content, RectangleF Status, float ScrollOffset, int Count, bool CaptionsFit)
{
    internal PointF ToLogical(Point physicalPoint) => new(physicalPoint.X / Scale, physicalPoint.Y / Scale);

    internal int ActionAt(Point physicalPoint)
    {
        var point = ToLogical(physicalPoint);
        return Search.Contains(point) ? 1 : Refresh.Contains(point) ? 2 : 0;
    }

    internal int RowAt(Point physicalPoint)
    {
        var point = ToLogical(physicalPoint);
        if (!Content.Contains(point)) return -1;
        int index = (int)((point.Y - Content.Top + ScrollOffset) / PriceWatchWidgetRenderer.RowHeight);
        return index < Count ? index : -1;
    }

    internal int MerchantAt(Point physicalPoint, int row)
    {
        if (row < 0) return -1;
        var point = ToLogical(physicalPoint);
        float top = Content.Top + row * PriceWatchWidgetRenderer.RowHeight - ScrollOffset;
        for (int shop = 0; shop < PriceWatchOfferDisplay.ShopCount; shop++)
            if (PriceWatchWidgetRenderer.RowOfferBounds(Content, top, shop).Merchant.Contains(point)) return shop;
        return -1;
    }

}

internal readonly record struct PriceWatchRowDisplay(string Title, string Edition,
    IReadOnlyList<PriceWatchOfferColumns> Prices);

internal static class PriceWatchWidgetRenderer
{
    internal const int Width = 390;
    private const int PriceLineHeight = 17;
    private const int PriceTop = 36;
    internal const int RowHeight = PriceTop + PriceWatchOfferDisplay.ShopCount * PriceLineHeight + 5;
    private const int Inset = 16;
    private const int Gap = 6;
    private const int ActionHeight = 21;

    internal static (Color Text, Color Background) ActionColors(bool dark, bool enabled, bool hovered, bool pressed)
    {
        if (!enabled)
            return (AppTheme.TextSecondary(dark), AppTheme.PanelBackground(dark));

        return (AppTheme.TextPrimary(dark), pressed ? AppTheme.ControlPressed(dark)
            : hovered ? AppTheme.ControlHover(dark) : AppTheme.ControlBackground(dark));
    }

    internal static (Color Panel, Color Text, Color Muted, Color Accent) ContentColors(bool dark, SystemWidgetStyle style)
    {
        var colors = WidgetDrawing.GetPalette(style);
        return dark
            ? (Color.FromArgb(225, colors.panel), Color.FromArgb(255, colors.text),
                Color.FromArgb(255, colors.muted), Color.FromArgb(255, colors.accent))
            : (Color.FromArgb(240, AppTheme.WindowBackground(false)), AppTheme.TextPrimary(false),
                Color.FromArgb(75, 85, 95), Color.FromArgb(24, 85, 140));
    }

    internal static Bitmap Draw(Size workArea, int dpi, SystemWidgetStyle style, bool dark, string language,
        string mode, string empty, string status, string search, string refresh, bool canSearch, bool canRefresh,
        int hovered, int pressed, CalendarViewport viewport, int count, Func<int, PriceWatchRowDisplay> rowAt,
        out PriceWatchWidgetLayout layout, (int Row, int Shop)? hoveredMerchant = null, bool keyboardFocus = false)
    {
        var area = new Size(Math.Max(1, workArea.Width), Math.Max(1, workArea.Height));
        const int chromeHeight = CalendarViewport.HeaderHeight + CalendarViewport.FooterHeight;
        float scale = Math.Min(Math.Clamp(dpi, 48, 768) / 96f,
            Math.Min(area.Width / 255f, area.Height / (float)(chromeHeight + RowHeight)));
        float width = Math.Min(Width, area.Width / scale);
        // Use a whole number of rows so both shop lines stay visible while scrolling.
        int heightLimit = Math.Min(CalendarViewport.DefaultMaximumHeight, (int)Math.Floor(area.Height / scale));
        int visibleRows = Math.Max(1, (heightLimit - chromeHeight) / RowHeight);
        viewport.Update(Math.Max(RowHeight, (float)count * RowHeight), CalendarViewport.DefaultMaximumHeight,
            chromeHeight + visibleRows * RowHeight, 96, width);
        float offset = Math.Clamp(MathF.Round(viewport.ScrollOffset / RowHeight, MidpointRounding.AwayFromZero) * RowHeight,
            0, viewport.MaxScrollOffset);
        float height = viewport.LogicalHeight;
        float available = width - 2 * Inset;

        using var measureImage = new Bitmap(1, 1);
        measureImage.SetResolution(96, 96);
        using var measure = Graphics.FromImage(measureImage);
        using var font = new Font("Segoe UI", 13, FontStyle.Regular, GraphicsUnit.Pixel);
        using var small = new Font("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel);
        using var linkFont = new Font(small, FontStyle.Underline);
        using var figures = new Font("Consolas", 12, FontStyle.Regular, GraphicsUnit.Pixel);
        using var titleFont = new Font("Segoe UI", 13, FontStyle.Bold, GraphicsUnit.Pixel);
        float searchWidth = (float)Math.Ceiling(measure.MeasureString(search, small).Width) + 14;
        float refreshWidth = (float)Math.Ceiling(measure.MeasureString(refresh, small).Width) + 14;
        float modeWidth = Math.Min(available - 90, (float)Math.Ceiling(measure.MeasureString(mode, small).Width) + 2);
        var modeBounds = new RectangleF(width - Inset - modeWidth, 13, modeWidth, 20);
        var statusBounds = new RectangleF(Inset, 41, available, 13);
        float searchSize = Math.Min(Math.Max(searchWidth, (available - Gap) / 2),
            Math.Max(0, available - Gap - refreshWidth));
        var searchBounds = new RectangleF(Inset, height - 24, searchSize, ActionHeight);
        var refreshBounds = new RectangleF(searchBounds.Right + Gap, searchBounds.Y,
            available - searchSize - Gap, ActionHeight);
        layout = new(scale, searchBounds, refreshBounds, modeBounds, viewport.ContentBounds, statusBounds,
            offset, count, searchWidth <= searchBounds.Width + .01f && refreshWidth <= refreshBounds.Width + .01f);

        var bitmap = new Bitmap(Math.Min(area.Width, (int)Math.Ceiling(width * scale)),
            Math.Min(area.Height, (int)Math.Ceiling(height * scale)), PixelFormat.Format32bppPArgb);
        try
        {
            bitmap.SetResolution(96, 96);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Transparent);
            graphics.ScaleTransform(scale, scale);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var palette = WidgetDrawing.GetPalette(style);
            var colors = ContentColors(dark, style);
            using var panel = WidgetDrawing.RoundedRectangle(new(2, 2, width - 4, height - 4), 12);
            using var fill = new SolidBrush(colors.Panel);
            using var border = new Pen(dark ? palette.border : AppTheme.Border(false));
            if (style == SystemWidgetStyle.Glow)
            {
                using var glow = new Pen(Color.FromArgb(54, colors.Accent), 4);
                graphics.DrawPath(glow, panel);
            }
            graphics.FillPath(fill, panel);
            graphics.DrawPath(border, panel);
            if (keyboardFocus)
            {
                using var focus = new Pen(colors.Accent) { DashStyle = DashStyle.Dot };
                using var focusOutline = WidgetDrawing.RoundedRectangle(new(4, 4, width - 8, height - 8), 10);
                graphics.DrawPath(focus, focusOutline);
            }
            WidgetHeader.Draw(graphics, "PriceWatch", language, style, width, modeWidth + 8, colors.Text, colors.Accent);

            using var text = new SolidBrush(colors.Text);
            using var muted = new SolidBrush(colors.Muted);
            using var accent = new SolidBrush(colors.Accent);
            using var line = new StringFormat { FormatFlags = StringFormatFlags.NoWrap,
                Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center };
            using var centered = new StringFormat(line) { Alignment = StringAlignment.Center };
            using var right = new StringFormat(line) { Alignment = StringAlignment.Far };
            graphics.DrawString(mode, small, accent, modeBounds, line);
            graphics.DrawString(status, small, text, statusBounds, line);

            var content = viewport.ContentBounds;
            var clip = graphics.Save();
            graphics.SetClip(content);
            if (count == 0) graphics.DrawString(empty, font, muted, content, centered);
            int first = (int)(offset / RowHeight);
            float y = content.Top + first * (float)RowHeight - offset;
            for (int index = first; index < count && y < content.Bottom; index++, y += RowHeight)
            {
                var row = rowAt(index);
                var cover = new RectangleF(content.X + 5, y + (RowHeight - 46) / 2f, 33, 46);
                DrawCoverPlaceholder(graphics, cover, dark, colors.Muted);
                float left = cover.Right + 8;
                float rowWidth = content.Right - left - 4;
                graphics.DrawString(row.Title, titleFont, text, new RectangleF(left, y + 3, rowWidth, 18), line);
                graphics.DrawString(row.Edition, small, muted, new RectangleF(left, y + 20, rowWidth, 15), line);
                for (int shop = 0; shop < row.Prices.Count; shop++)
                {
                    PriceWatchOfferColumns offer = row.Prices[shop];
                    var (merchant, price, discount) = RowOfferBounds(content, y, shop);
                    bool linkHovered = hoveredMerchant == (index, shop);
                    graphics.DrawString(offer.Merchant, linkHovered ? linkFont : small,
                        linkHovered ? accent : muted, merchant, line);
                    if (offer.Discount.Length == 0)
                        graphics.DrawString(offer.Price, small, muted, RectangleF.Union(price, discount), right);
                    else
                    {
                        graphics.DrawString(offer.Price, figures, text, price, right);
                        graphics.DrawString(offer.Discount, figures, muted, discount, right);
                    }
                }
                graphics.DrawLine(border, content.Left, y + RowHeight - 1, content.Right, y + RowHeight - 1);
            }
            graphics.Restore(clip);
            WidgetScrollbar.Draw(graphics, viewport, dark);
            DrawAction(search, searchBounds, canSearch, 1);
            DrawAction(refresh, refreshBounds, canRefresh, 2);
            return bitmap;

            void DrawAction(string caption, RectangleF bounds, bool enabled, int id)
            {
                var button = ActionColors(dark, enabled, id == hovered, id == pressed);
                using var shape = WidgetDrawing.RoundedRectangle(bounds, 5);
                using var background = new SolidBrush(button.Background);
                using var fore = new SolidBrush(button.Text);
                using var outline = new Pen(enabled && id == hovered ? colors.Accent : AppTheme.Border(dark));
                graphics.FillPath(background, shape);
                graphics.DrawPath(outline, shape);
                graphics.DrawString(caption, small, fore, bounds, centered);
            }
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    internal static (RectangleF Merchant, RectangleF Price, RectangleF Discount) RowOfferBounds(
        RectangleF content, float rowTop, int shop)
    {
        const float textInset = 5 + 33 + 8;
        return OfferBounds(content.Left + textInset, rowTop + PriceTop + shop * PriceLineHeight,
            content.Width - textInset - 4);
    }

    internal static (RectangleF Merchant, RectangleF Price, RectangleF Discount) OfferBounds(float left, float top, float width)
    {
        float merchant = width * .31f;
        float price = width * .43f;
        return (new(left, top, merchant - 3, PriceLineHeight),
            new(left + merchant, top, price - 3, PriceLineHeight),
            new(left + merchant + price, top, width - merchant - price, PriceLineHeight));
    }

    internal static void DrawCoverPlaceholder(Graphics graphics, RectangleF bounds, bool dark, Color foreground)
    {
        using var shape = WidgetDrawing.RoundedRectangle(bounds, 5);
        using var fill = new SolidBrush(AppTheme.ControlBackground(dark));
        using var border = new Pen(AppTheme.Border(dark));
        using var text = new SolidBrush(foreground);
        using var font = new Font("Segoe UI", 13, FontStyle.Regular, GraphicsUnit.Pixel);
        using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.FillPath(fill, shape);
        graphics.DrawPath(border, shape);
        graphics.DrawString("◇", font, text, bounds, center);
    }
}
