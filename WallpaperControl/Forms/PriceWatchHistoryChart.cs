using System.Drawing.Drawing2D;
using System.Globalization;

namespace WallpaperControl;

internal sealed class PriceWatchHistoryChart : Control
{
    private readonly ToolTip tips = new();
    private readonly string language;
    private readonly CultureInfo culture;
    private readonly SystemWidgetStyle style;
    private PriceWatchHistorySeries[] series = [];
    private PriceWatchLocalPoint[] local = [];
    private DateTimeOffset start, end;
    private int? target;
    private PriceWatchHistory? hovered;
    private PriceWatchLocalPoint? hoveredLocal;
    internal PriceWatchHistoryChart(string language, SystemWidgetStyle style = SystemWidgetStyle.Clean)
    {
        this.language = language; this.style = style; culture = CultureInfo.GetCultureInfo(language);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw, true);
        Name = "PriceWatchHistoryChart"; TabStop = true;
        AccessibleName = Localization.Get("PriceWatchHistory", language);
    }
    internal static Color ShopColor(int id) => id switch
    {
        35 => Color.FromArgb(177, 130, 234), 61 => Color.FromArgb(64, 180, 238),
        16 => Color.FromArgb(237, 173, 64), 27 => Color.FromArgb(78, 183, 109),
        62 => Color.FromArgb(230, 117, 171), 48 => Color.FromArgb(75, 190, 186),
        _ => Color.FromArgb(230, 120, 70)
    };
    internal void Present(PriceWatchHistorySeries[] data, DateTimeOffset from, DateTimeOffset to, int? cents,
        PriceWatchLocalPoint[]? observations = null)
    { series = data; local = observations ?? []; start = from; end = to; target = cents;
        hovered = null; hoveredLocal = null; tips.SetToolTip(this, null); Invalidate(); }

    internal static RectangleF PlotArea(Size size, int dpi)
    {
        float scale = dpi / 96f;
        return new(78 * scale, 12 * scale, Math.Max(1, size.Width - 94 * scale), Math.Max(1, size.Height - 52 * scale));
    }
    private double Maximum => Math.Max(100, Math.Max(series.SelectMany(s => s.Points)
        .Where(p => p.Deal != null).Select(p => (double)p.Deal!.Price.AmountInt)
        .Concat(local.Select(p => (double)p.Observation.Cents)).DefaultIfEmpty(0).Max(), target ?? 0) * 1.1);
    internal PointF Position(PriceWatchHistory p, RectangleF area, double? maximum = null) => new(
        area.Left + (float)((p.Timestamp - start).TotalSeconds / Math.Max(1, (end - start).TotalSeconds)) * area.Width,
        area.Bottom - (float)(p.Deal!.Price.AmountInt / (maximum ?? Maximum)) * area.Height);
    internal PriceWatchHistory? Hit(Point point)
    {
        var area = PlotArea(ClientSize, DeviceDpi);
        double radius = 9d * DeviceDpi / 96;
        if (!RectangleF.Inflate(area, (float)radius, (float)radius).Contains(point)) return null;
        double maximum = Maximum;
        return series.SelectMany(s => s.Points).Where(p => p.Deal != null)
            .Select(p => (Point: p, Distance: Math.Pow(Position(p, area, maximum).X - point.X, 2) + Math.Pow(Position(p, area, maximum).Y - point.Y, 2)))
            .Where(p => p.Distance <= radius * radius).OrderBy(p => p.Distance).Select(p => p.Point).FirstOrDefault();
    }
    internal string PointText(PriceWatchHistory point) =>
        Localization.Get("PriceWatchHistoryITAD", language) + "\n"
        + $"{point.Timestamp.ToLocalTime():dd.MM.yyyy HH:mm:ss zzz}\n{point.Shop.Name}: {point.Deal!.Price.Amount.ToString("N2", culture)} €\n"
        + $"{Localization.Get("PriceWatchHistoryRegular", language)}: {point.Deal.Regular.Amount.ToString("N2", culture)} €";

    internal PointF LocalPosition(PriceWatchLocalPoint point, RectangleF area, double? maximum = null) => new(
        area.Left + (float)((point.Observation.CapturedAt - start).TotalSeconds / Math.Max(1, (end - start).TotalSeconds)) * area.Width,
        area.Bottom - (float)(point.Observation.Cents / (maximum ?? Maximum)) * area.Height);
    internal string LocalText(PriceWatchLocalPoint point)
    {
        var p = point.Observation;
        return Localization.Get(point.IsLast ? "PriceWatchHistoryLast" : "PriceWatchHistoryLocal", language)
            + " · " + Localization.Get("PriceWatchHistoryLocalSource", language) + "\n"
            + $"{p.CapturedAt.ToLocalTime():dd.MM.yyyy HH:mm:ss zzz}\n"
            + $"{PriceWatchShops.Supported.Single(s => s.Id == p.Shop).Name}: {(p.Cents / 100m).ToString("N2", culture)} €"
            + (p.RegularCents is { } regular ? "\n" + Localization.Get("PriceWatchHistoryRegular", language)
                + $": {(regular / 100m).ToString("N2", culture)} €" : "");
    }
    internal PriceWatchLocalPoint? HitLocal(Point point)
    {
        var area = PlotArea(ClientSize, DeviceDpi); double maximum = Maximum;
        float radius = 9f * DeviceDpi / 96;
        if (!RectangleF.Inflate(area, radius, radius).Contains(point)) return null;
        return local.Select(p => (Point: p, Position: LocalPosition(p, area, maximum)))
            .Select(p => (p.Point, Distance: Math.Pow(p.Position.X - point.X, 2) + Math.Pow(p.Position.Y - point.Y, 2)))
            .Where(p => p.Distance <= Math.Pow(9d * DeviceDpi / 96, 2)).OrderBy(p => p.Distance)
            .ThenByDescending(p => p.Point.IsLast).Select(p => p.Point).FirstOrDefault();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var localPoint = HitLocal(e.Location);
        var point = Hit(e.Location);
        if (point == hovered && localPoint == hoveredLocal) return;
        hovered = point; hoveredLocal = localPoint;
        tips.SetToolTip(this, localPoint != null ? LocalText(localPoint) : point == null ? null : PointText(point));
    }
    protected override void OnMouseLeave(EventArgs e)
    { base.OnMouseLeave(e); hovered = null; hoveredLocal = null; tips.SetToolTip(this, null); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is not (Keys.Left or Keys.Right)) return;
        var points = series.SelectMany(s => s.Points).Where(p => p.Deal != null)
            .Select(p => (Time: p.Timestamp, History: (PriceWatchHistory?)p, Local: (PriceWatchLocalPoint?)null))
            .Concat(local.Select(p => (Time: p.Observation.CapturedAt, History: (PriceWatchHistory?)null, Local: (PriceWatchLocalPoint?)p)))
            .OrderBy(p => p.Time).ToArray();
        if (points.Length == 0) return;
        int index = Array.FindIndex(points, p => hoveredLocal != null ? p.Local == hoveredLocal : p.History != null && p.History == hovered);
        var selected = points[(index + (e.KeyCode == Keys.Left ? -1 : 1) + points.Length) % points.Length];
        hovered = selected.History; hoveredLocal = selected.Local;
        string text = hoveredLocal != null ? LocalText(hoveredLocal) : PointText(hovered!);
        tips.Show(text, this, Width / 2, Height / 2, 5000);
        AccessibleDescription = text; e.Handled = true;
    }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics; var area = PlotArea(ClientSize, DeviceDpi);
        float scale = DeviceDpi / 96f;
        double maximum = Maximum;
        using var text = new SolidBrush(ForeColor);
        using var grid = new Pen(Color.FromArgb(55, ForeColor));
        using var format = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
        for (int i = 0; i <= 4; i++)
        {
            float y = area.Bottom - area.Height * i / 4;
            g.DrawLine(grid, area.Left, y, area.Right, y);
            g.DrawString((maximum * i / 400).ToString("N2", culture) + " €", Font, text,
                new RectangleF(0, y - Font.Height, area.Left - 5 * scale, Font.Height * 2), format);
        }
        if (end <= start) return;
        using var dateFormat = new StringFormat { Alignment = StringAlignment.Center };
        int labels = area.Width < 350 * scale ? 2 : 4;
        for (int i = 0; i <= labels; i++)
        {
            var date = start + (end - start) * (i / (double)labels);
            g.DrawString(date.ToLocalTime().ToString("dd.MM.", culture), Font, text,
                new PointF(area.Left + area.Width * i / labels, area.Bottom + 6 * scale), dateFormat);
        }
        var saved = g.Save(); g.SetClip(area);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (target is { } cents)
        {
            float y = area.Bottom - (float)(cents / maximum) * area.Height;
            using var pen = new Pen(ForeColor, scale) { DashStyle = DashStyle.Dash };
            g.DrawLine(pen, area.Left, y, area.Right, y);
            g.DrawString(Localization.Get("PriceWatchTarget", language) + $" {cents / 100m:N2} €", Font, text, area.Left + 4, y);
        }
        foreach (var line in series)
        {
            using var pen = new Pen(ShopColor(line.Shop), 2.5f * scale)
                { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            using var brush = new SolidBrush(pen.Color);
            var curves = PriceWatchCurves.History(line);
            if (style == SystemWidgetStyle.Glow)
            {
                using var glow = new Pen(Color.FromArgb(45, pen.Color), 6 * scale);
                foreach (var curve in curves) DrawCurve(curve, glow);
            }
            foreach (var curve in curves) DrawCurve(curve, pen);
            using var connection = new Pen(Color.FromArgb(215, pen.Color), 2 * scale) { DashStyle = DashStyle.Dash };
            foreach (var curve in PriceWatchCurves.Connections(line, local)) DrawCurve(curve, connection);
            foreach (var point in line.Points.Where(p => p.Deal != null))
            {
                var p = Position(point, area, maximum);
                g.FillEllipse(brush, p.X - 2.6f * scale, p.Y - 2.6f * scale, 5.2f * scale, 5.2f * scale);
            }
        }
        void DrawCurve(PriceWatchCurveSegment curve, Pen pen)
        {
            PointF Project(PriceWatchCurveKnot point) => new(
                area.Left + (float)((point.Time - start).TotalSeconds / Math.Max(1, (end - start).TotalSeconds)) * area.Width,
                area.Bottom - (float)(point.Cents / maximum) * area.Height);
            g.DrawBezier(pen, Project(curve.From), Project(curve.Control1), Project(curve.Control2), Project(curve.To));
        }
        g.Restore(saved);
        foreach (var point in local)
        {
            var p = LocalPosition(point, area, maximum);
            using var pen = new Pen(ShopColor(point.Observation.Shop), 1.8f * scale);
            if (point.IsLast)
            {
                float r = 5 * scale;
                using var fill = new SolidBrush(pen.Color);
                g.FillPolygon(fill, new PointF[] { new(p.X, p.Y - r), new(p.X + r, p.Y), new(p.X, p.Y + r), new(p.X - r, p.Y) });
            }
            else g.DrawEllipse(pen, p.X - 3 * scale, p.Y - 3 * scale, 6 * scale, 6 * scale);
        }
        if (Focused) ControlPaint.DrawFocusRectangle(g, ClientRectangle);
    }
    protected override void Dispose(bool disposing) { if (disposing) tips.Dispose(); base.Dispose(disposing); }
}
