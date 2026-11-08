using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace WallpaperControl;

internal sealed class VisitorModeWidgetForm : Form
{
    private readonly VisitorModeService service;
    private readonly WidgetDragHandler drag;
    private readonly ToolTip tooltip = new();
    private VisitorWidgetSettings settings = new();
    private string language = Localization.CurrentLanguage;
    private Rectangle switchBounds;
    private bool switchPressed;
    private Point pressPoint;
    private bool rendering;

    internal VisitorModeWidgetForm(VisitorModeService service, Action<Point> moved)
    {
        this.service = service;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(220, 48);
        MouseDown += (_, e) => { switchPressed = e.Button == MouseButtons.Left && switchBounds.Contains(e.Location); pressPoint = e.Location; };
        drag = new WidgetDragHandler(this, () => settings.Locked || switchPressed, RenderLayeredWindow, moved);
        MouseUp += async (_, e) =>
        {
            bool toggle = switchPressed && e.Button == MouseButtons.Left && switchBounds.Contains(e.Location)
                && Math.Abs(e.X - pressPoint.X) < 4 && Math.Abs(e.Y - pressPoint.Y) < 4;
            switchPressed = false;
            if (toggle && !service.Busy) await ToggleAsync();
        };
        service.Changed += RefreshState;
        AccessibleRole = AccessibleRole.CheckButton;
        RefreshState();
    }
    internal Task ToggleAsync() => service.ToggleAsync();
    internal void Apply(VisitorWidgetSettings value, string languageCode)
    { settings = value.Clone(); language = languageCode; RefreshState(); }
    internal void RefreshState()
    {
        if (IsDisposed) return;
        AccessibleName = Localization.Get("VisitorTitle", language);
        AccessibleDescription = Localization.Get(service.Error ?? (service.Active ? "VisitorActive" : "VisitorInactive"), language);
        tooltip.SetToolTip(this, AccessibleDescription);
        RenderLayeredWindow();
    }
    protected override bool ShowWithoutActivation => true;
    protected override void OnShown(EventArgs e) { base.OnShown(e); RenderLayeredWindow(); }
    protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); RenderLayeredWindow(); }
    internal Bitmap RenderBitmap(int dpi = 96)
    {
        float scale = Math.Clamp(dpi / 96f, 1, 8) * settings.ScalePercent / 100f;
        float fontScale = settings.FontSize / 11f;
        string title = Localization.Get("VisitorTitle", language);
        float titleWidth;
        using (Bitmap measure = new(1, 1))
        using (Graphics graphics = Graphics.FromImage(measure))
        using (Font font = new("Segoe UI Semibold", settings.FontSize, FontStyle.Bold))
            titleWidth = graphics.MeasureString(title.ToUpper(System.Globalization.CultureInfo.GetCultureInfo(language)), font).Width;
        int width = (int)Math.Ceiling(Math.Max(210, titleWidth + 106));
        int height = (int)Math.Ceiling(Math.Max(48, 32 * fontScale + 16));
        var palette = WidgetDrawing.GetPalette(settings.Style);
        Color panel = settings.BackgroundArgb is int background ? Color.FromArgb(background) : palette.panel;
        panel = Color.FromArgb(settings.BackgroundOpacityPercent is int opacity ? opacity * 255 / 100 : palette.panel.A, panel);
        Color text = settings.TextArgb is int foreground ? Color.FromArgb(255, Color.FromArgb(foreground)) : palette.title;
        Bitmap bitmap = new((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale), PixelFormat.Format32bppPArgb);
        using Graphics g = Graphics.FromImage(bitmap);
        g.Clear(Color.Transparent); g.ScaleTransform(scale, scale); g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = WidgetDrawing.RoundedRectangle(new RectangleF(.5f, .5f, width - 1, height - 1), 12))
        using (var fill = new SolidBrush(panel))
        using (var border = new Pen(palette.border)) { g.FillPath(fill, path); g.DrawPath(border, path); }
        WidgetHeader.Draw(g, title, language, settings.Style, width, 70, text, settings.TextArgb.HasValue ? text : palette.accent, settings.FontSize);
        Rectangle toggle = new(width - 66, 12, 50, 24);
        VisitorToggleSwitch.DrawSwitch(g, toggle, service.Active, !service.Busy);
        switchBounds = Rectangle.Round(new RectangleF(toggle.X * scale, toggle.Y * scale, toggle.Width * scale, toggle.Height * scale));
        return bitmap;
    }
    private void RenderLayeredWindow()
    {
        if (rendering || !IsHandleCreated || IsDisposed) return;
        rendering = true;
        try { using Bitmap image = RenderBitmap(DeviceDpi); ClientSize = image.Size; LayeredWidgetBitmap.Update(Handle, Location, image); }
        finally { rendering = false; }
    }
    protected override void WndProc(ref Message m)
    { if (!DesktopWidgetNative.HandleMouseActivation(ref m)) base.WndProc(ref m); }
    protected override CreateParams CreateParams
    { get { var cp = base.CreateParams; cp.ExStyle |= 0x80 | 0x08000000 | 0x00080000; return cp; } }
    protected override void Dispose(bool disposing)
    { if (disposing) { service.Changed -= RefreshState; drag.Dispose(); tooltip.Dispose(); } base.Dispose(disposing); }
}
