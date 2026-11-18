using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace WallpaperControl;

internal readonly record struct PriceWatchWidgetLayout(float Scale, RectangleF Search, RectangleF Refresh,
    RectangleF Mode, RectangleF Content, RectangleF Status, float ScrollOffset, int Count, bool CaptionsFit,
    float[]? RowStarts = null, int[]? OfferCounts = null)
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
        int index = IndexAt(point.Y - Content.Top + ScrollOffset);
        return index < Count ? index : -1;
    }

    internal int IndexAt(float offset)
    {
        if (RowStarts == null) return Math.Max(0, (int)(offset / PriceWatchWidgetRenderer.RowHeight));
        int index = Array.BinarySearch(RowStarts, offset);
        return Math.Clamp(index >= 0 ? index : ~index - 1, 0, Math.Max(0, Count - 1));
    }

    internal float RowOffset(int row) => RowStarts?[row] ?? row * PriceWatchWidgetRenderer.RowHeight;
    internal float RowTop(int row) => Content.Top + RowOffset(row) - ScrollOffset;
    internal IEnumerable<int> VisibleRows()
    {
        if (Count == 0) yield break;
        for (int row = IndexAt(ScrollOffset); row < Count && RowTop(row) < Content.Bottom; row++) yield return row;
    }

    internal int MerchantAt(Point physicalPoint, int row)
    {
        if (row < 0) return -1;
        var point = ToLogical(physicalPoint);
        float top = RowTop(row);
        for (int shop = 0; shop < (OfferCounts?[row] ?? PriceWatchOfferDisplay.ShopCount); shop++)
            if (PriceWatchWidgetRenderer.RowOfferBounds(Content, top, shop).Merchant.Contains(point)) return shop;
        return -1;
    }

}

internal readonly record struct PriceWatchRowDisplay(string Title, string Edition,
    IReadOnlyList<PriceWatchOfferColumns> Prices, Bitmap? Cover = null, string? Target = null,
    IReadOnlyList<bool>? TargetReached = null, string NoOffer = "");

internal static class PriceWatchWidgetRenderer
{
    internal const int Width = 390;
    private const int PriceLineHeight = 17;
    private const int PriceTop = 36;
    internal const int RowHeight = 94;
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
        out PriceWatchWidgetLayout layout, (int Row, int Shop)? hoveredMerchant = null, bool keyboardFocus = false,
        int scrollingTitle = -1, double titleElapsed = 0)
    {
        var area = new Size(Math.Max(1, workArea.Width), Math.Max(1, workArea.Height));
        const int chromeHeight = CalendarViewport.HeaderHeight + CalendarViewport.FooterHeight;
        var rows = Enumerable.Range(0, count).Select(rowAt).ToArray();
        var heights = rows.Select(row => Math.Max(85, 60 + Math.Min(3, row.Prices.Count) * PriceLineHeight)).ToArray();
        float scale = Math.Min(Math.Clamp(dpi, 48, 768) / 96f,
            Math.Min(area.Width / 255f, area.Height / (float)(chromeHeight + heights.DefaultIfEmpty(85).Max())));
        float width = Math.Min(Width, area.Width / scale);
        var starts = new float[count];
        for (int row = 1; row < count; row++) starts[row] = starts[row - 1] + heights[row - 1];
        int heightLimit = Math.Min(CalendarViewport.DefaultMaximumHeight, (int)Math.Floor(area.Height / scale));
        int visibleHeight = 0;
        int firstCandidate = count == 0 ? 0 : Math.Max(0, Array.FindLastIndex(starts, start => start <= viewport.ScrollOffset));
        foreach (int rowHeight in heights.Skip(firstCandidate))
        {
            if (visibleHeight > 0 && visibleHeight + rowHeight > heightLimit - chromeHeight) break;
            visibleHeight += rowHeight;
        }
        viewport.Update(count == 0 ? 85 : heights.Sum(), CalendarViewport.DefaultMaximumHeight,
            Math.Min(heightLimit, chromeHeight + Math.Max(85, visibleHeight)), 96, width);
        float offset = viewport.ScrollOffset;
        if (offset < viewport.MaxScrollOffset && count > 0)
            offset = starts[Math.Max(0, Array.FindLastIndex(starts, start => start <= offset))];
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
            offset, count, searchWidth <= searchBounds.Width + .01f && refreshWidth <= refreshBounds.Width + .01f,
            starts, rows.Select(row => row.Prices.Count).ToArray());

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
            using var targetHit = new SolidBrush(dark ? Color.LightGreen : Color.FromArgb(0, 100, 40));
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
            int first = count == 0 ? 0 : Math.Max(0, Array.FindLastIndex(starts, start => start <= offset));
            float y = content.Top + (count == 0 ? 0 : starts[first]) - offset;
            for (int index = first; index < count && y < content.Bottom; y += heights[index], index++)
            {
                var row = rows[index];
                var cover = new RectangleF(content.X + 5, y + (heights[index] - 75) / 2f, 55, 75);
                if (row.Cover is { } image)
                {
                    float fit = Math.Min(cover.Width / image.Width, cover.Height / image.Height);
                    var target = new RectangleF(cover.X + (cover.Width - image.Width * fit) / 2,
                        cover.Y + (cover.Height - image.Height * fit) / 2, image.Width * fit, image.Height * fit);
                    graphics.DrawImage(image, target);
                }
                else DrawCoverPlaceholder(graphics, cover, dark, colors.Muted);
                float left = cover.Right + 8;
                float rowWidth = content.Right - left - 4;
                var titleBounds = TitleBounds(content, y);
                float titleWidth = measure.MeasureString(row.Title, titleFont, int.MaxValue, line).Width;
                if (index == scrollingTitle && titleElapsed >= .5 && titleWidth > titleBounds.Width)
                {
                    var titleClip = graphics.Save();
                    graphics.SetClip(titleBounds, CombineMode.Intersect);
                    using var untrimmed = new StringFormat(line) { Trimming = StringTrimming.None };
                    float shift = PriceWatchTitleScroll.Offset(titleElapsed, titleWidth - titleBounds.Width, scale);
                    graphics.DrawString(row.Title, titleFont, text,
                        new RectangleF(titleBounds.X - shift, titleBounds.Y, titleWidth + 1, titleBounds.Height), untrimmed);
                    graphics.Restore(titleClip);
                }
                else graphics.DrawString(row.Title, titleFont, text, titleBounds, line);
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
                        graphics.DrawString(offer.Price, figures, row.TargetReached?[shop] == true ? targetHit : text, price, right);
                        graphics.DrawString(offer.Discount, figures, muted, discount, right);
                    }
                }
                if (row.Prices.Count == 0)
                    graphics.DrawString(row.NoOffer, small, muted, new RectangleF(left, y + PriceTop, rowWidth, 17), line);
                if (row.Target != null)
                    graphics.DrawString(row.Target, small, muted,
                        new RectangleF(left, y + PriceTop + Math.Max(1, row.Prices.Count) * PriceLineHeight + 2, rowWidth, 17), line);
                graphics.DrawLine(border, content.Left, y + heights[index] - 1, content.Right, y + heights[index] - 1);
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
        const float textInset = 5 + 55 + 8;
        return OfferBounds(content.Left + textInset, rowTop + PriceTop + shop * PriceLineHeight,
            content.Width - textInset - 4);
    }

    internal static RectangleF TitleBounds(RectangleF content, float top) =>
        new(content.Left + 68, top + 3, Math.Max(1, content.Width - 72), 18);

    internal static bool TitleOverflows(string title, float width)
    {
        using var bitmap = new Bitmap(1, 1);
        bitmap.SetResolution(96, 96);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var font = new Font("Segoe UI", 13, FontStyle.Bold, GraphicsUnit.Pixel);
        using var format = new StringFormat { FormatFlags = StringFormatFlags.NoWrap };
        return graphics.MeasureString(title, font, int.MaxValue, format).Width > width;
    }

    internal static (RectangleF Merchant, RectangleF Price, RectangleF Discount) OfferBounds(float left, float top, float width)
    {
        float merchant = width * .40f;
        float price = width * .36f;
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
