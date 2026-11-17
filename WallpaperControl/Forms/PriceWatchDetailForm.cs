using System.Globalization;
using System.Drawing.Drawing2D;

namespace WallpaperControl;

internal sealed class PriceWatchDetailForm : Form
{
    private readonly Label heading = new() { AutoSize = true, UseMnemonic = false, Margin = new(0, 0, 0, 3) };
    private readonly Label edition = new() { AutoSize = true, Margin = new(0, 0, 0, 8) };
    private readonly Label updated = new() { AutoSize = true, Margin = new(0, 6, 0, 0) };
    private readonly Label status = new() { AutoSize = true, Margin = new(0, 4, 0, 0) };
    private readonly Label descriptionText = new() { AutoSize = true, UseMnemonic = false, Margin = Padding.Empty };
    private readonly TableLayoutPanel header = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2,
        Padding = new(10), Margin = Padding.Empty };
    private readonly TableLayoutPanel about = new() { Name = "PriceWatchAbout", Dock = DockStyle.Top, AutoSize = true,
        ColumnCount = 1, Margin = new(0, 8, 0, 0) };
    private readonly Panel cover = new() { Size = new(48, 66), Margin = new(0, 0, 10, 0) };
    private readonly TableLayoutPanel offers = new() { Name = "PriceWatchOffers", Dock = DockStyle.Top,
        AutoSize = true, ColumnCount = 3, Margin = new(0, 8, 0, 0) };
    private readonly Panel scroll = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly TableLayoutPanel content = new() { Dock = DockStyle.Top, AutoSize = true,
        ColumnCount = 1, Padding = new(14) };
    private readonly FlowLayoutPanel actions = new() { Dock = DockStyle.Top, AutoSize = true, Margin = new(0, 10, 0, 0) };
    private readonly Button chromeClose = new() { Name = "PriceWatchChromeClose", Text = "×", UseMnemonic = false };
    private readonly bool shopLinksEnabled;
    private readonly Action<Uri> openLink;
    private readonly string language;
    private readonly CultureInfo culture;
    private readonly Color accent, muted, stripe, foreground;
    private readonly Font titleFont, columnFont, offerInfoFont;
    private readonly Color chromeBackground, chromeBorder;
    private Size resizeStart;
    private bool userSized;

    internal PriceWatchDetailForm(PriceWatchEntry entry, string mode, string language, SystemWidgetStyle style,
        Func<Task> refresh, Func<bool> remove, string? description = null,
        bool shopLinksEnabled = false, Action<Uri>? openLink = null)
    {
        this.language = language;
        this.shopLinksEnabled = shopLinksEnabled;
        this.openLink = openLink ?? PriceWatchShopLinks.Open;
        culture = CultureInfo.GetCultureInfo(language);
        Text = $"{T("PriceWatchDetails")} · {mode}";
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = MinimizeBox = false;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
        AutoScaleDimensions = new(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new(440, 245);
        MinimumSize = new(300, 150);
        var dark = NotesDialogStyle.ResolveDarkMode();
        var palette = PriceWatchWidgetRenderer.ContentColors(dark, style);
        accent = palette.Accent;
        muted = dark ? palette.Muted : AppTheme.TextSecondary(false);
        stripe = AppTheme.PanelBackground(dark);
        foreground = palette.Text;
        chromeBackground = AppTheme.PanelBackground(dark);
        chromeBorder = style == SystemWidgetStyle.Minimal ? AppTheme.Border(dark) : accent;
        titleFont = new Font("Segoe UI Semibold", 13f, FontStyle.Bold);
        columnFont = new Font("Segoe UI", 9f, FontStyle.Bold);
        offerInfoFont = new Font("Segoe UI", 8.5f);
        content.ColumnStyles.Add(new(SizeType.Percent, 100));
        header.ColumnStyles.Add(new(SizeType.AutoSize));
        header.ColumnStyles.Add(new(SizeType.Percent, 100));
        header.Controls.Add(cover, 0, 0);
        header.SetRowSpan(cover, 2);
        header.Controls.Add(heading, 1, 0);
        header.Controls.Add(edition, 1, 1);
        header.Paint += (_, e) => DrawHeader(e.Graphics, dark, style);
        cover.Paint += (_, e) => PriceWatchWidgetRenderer.DrawCoverPlaceholder(e.Graphics,
            new RectangleF(1, 1, Math.Max(1, cover.Width - 2), Math.Max(1, cover.Height - 2)), dark, muted);
        content.Controls.Add(header, 0, 0);
        // Future release date, developer and publisher belong below the game header.
        about.ColumnStyles.Add(new(SizeType.Percent, 100));
        var aboutHeading = new Label { AutoSize = true, Text = T("PriceWatchAboutGame"), Margin = new(0, 0, 0, 3) };
        about.Controls.Add(aboutHeading, 0, 0);
        about.Controls.Add(descriptionText, 0, 1);
        descriptionText.Text = description ?? "";
        about.Visible = !string.IsNullOrWhiteSpace(description);
        content.Controls.Add(about, 0, 1);
        offers.ColumnStyles.Add(new(SizeType.Percent, 38));
        offers.ColumnStyles.Add(new(SizeType.Percent, 35));
        offers.ColumnStyles.Add(new(SizeType.Percent, 27));
        content.Controls.Add(offers, 0, 2);
        // A history section can be placed after the offers when a chart is available.
        content.Controls.Add(updated, 0, 3);
        content.Controls.Add(status, 0, 4);
        var fetch = new Button { Text = T("PriceWatchRefresh"), AutoSize = true };
        var delete = new Button { Text = T("PriceWatchRemove"), AutoSize = true };
        var close = new Button { Text = T("AboutClose"), AutoSize = true, DialogResult = DialogResult.Cancel };
        actions.Controls.AddRange([fetch, delete, close]);
        content.Controls.Add(actions, 0, 5);
        scroll.Controls.Add(content);
        Controls.Add(scroll);
        scroll.TabIndex = 1;
        chromeClose.AccessibleName = T("AboutClose");
        chromeClose.Click += (_, _) => Close();
        Controls.Add(chromeClose);
        CancelButton = close;
        ConfigureChrome();
        NotesDialogStyle.Apply(this, style, dark);
        heading.Font = titleFont;
        heading.BackColor = edition.BackColor = cover.BackColor = Color.Transparent;
        heading.ForeColor = foreground;
        edition.ForeColor = accent;
        aboutHeading.Font = columnFont;
        aboutHeading.ForeColor = accent;
        updated.ForeColor = muted;
        delete.Click += (_, _) =>
        {
            if (MessageBox.Show(this, T("PriceWatchConfirmRemove"), Text, MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            if (remove()) Close();
            else SetStatus(T("PriceWatchStorageError"));
        };
        fetch.Click += async (_, _) =>
        {
            fetch.Enabled = delete.Enabled = false;
            SetStatus(T("PriceWatchLoading"));
            try { await refresh(); }
            finally
            {
                if (!IsDisposed) fetch.Enabled = delete.Enabled = true;
            }
        };
        scroll.Resize += (_, _) => FitText();
        FitText();
        Present(entry, "");
    }

    private void DrawHeader(Graphics graphics, bool dark, SystemWidgetStyle style)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = WidgetDrawing.RoundedRectangle(new(2, 2,
            Math.Max(1, header.Width - 4), Math.Max(1, header.Height - 4)), 8 * DeviceDpi / 96f);
        using var fill = new SolidBrush(AppTheme.PanelBackground(dark));
        using var border = new Pen(style == SystemWidgetStyle.Minimal ? AppTheme.Border(dark) : accent);
        if (style == SystemWidgetStyle.Glow)
        {
            using var glow = new Pen(Color.FromArgb(65, accent), 3);
            graphics.DrawPath(glow, shape);
        }
        graphics.FillPath(fill, shape);
        graphics.DrawPath(border, shape);
    }

    internal void Present(PriceWatchEntry entry, string message)
    {
        heading.Text = entry.Game.Title;
        edition.Text = T(PriceWatchSearchForm.EditionKey(entry.Game.Type));
        offers.SuspendLayout();
        try
        {
            foreach (Control cell in offers.Controls.Cast<Control>().ToArray()) cell.Dispose();
            offers.RowStyles.Clear();
            offers.RowCount = 0;
            AddRow(T("PriceWatchMerchant"), T("PriceWatchPrice"), T("PriceWatchDiscount"), stripe);
            int offerIndex = 0;
            foreach (var shop in PriceWatchOfferDisplay.Shops)
            {
                var available = PriceWatchOfferDisplay.ForShop(entry, shop.Id).OrderBy(offer => offer.Price.AmountInt).ToArray();
                if (available.Length == 0)
                    AddRow(shop.Name, T("PriceWatchNoOffer"), "—", (offerIndex++ % 2 == 0) ? BackColor : stripe);
                foreach (var offer in available)
                {
                    Color background = offerIndex++ % 2 == 0 ? BackColor : stripe;
                    AddRow(shop.Name, PriceWatchOfferDisplay.Price(offer, culture),
                        PriceWatchOfferDisplay.Discount(offer, culture), background, offer.Url);
                    AddOfferInfo(offer, background);
                }
            }
        }
        finally { offers.ResumeLayout(true); }
        updated.Text = entry.Snapshot == null ? T("PriceWatchNotChecked") :
            $"{T("PriceWatchUpdated")} {entry.Snapshot.FetchedAt.ToLocalTime().ToString("g", culture)}";
        SetStatus(message);
    }

    private void SetStatus(string message)
    {
        status.Text = message;
        status.Visible = message.Length != 0;
        if (Visible) FitToMonitor(fitContents: !userSized);
    }

    private void AddRow(string shop, string price, string discount, Color background, Uri? offerUrl = null)
    {
        int row = offers.RowCount++;
        offers.RowStyles.Add(new(SizeType.AutoSize));
        string[] values = [shop, price, discount];
        for (int column = 0; column < values.Length; column++)
        {
            Label cell = new();
            if (column == 0 && shopLinksEnabled && PriceWatchShopLinks.Validate(offerUrl) is { } url)
            {
                var link = new LinkLabel
                {
                    LinkBehavior = LinkBehavior.HoverUnderline,
                    LinkColor = accent, ActiveLinkColor = foreground, VisitedLinkColor = accent,
                    TabStop = true, TabIndex = row
                };
                link.LinkClicked += (_, _) => openLink(url);
                cell = link;
            }
            cell.Text = values[column];
            cell.UseMnemonic = false;
            cell.AutoSize = true;
            cell.Dock = DockStyle.Fill;
            cell.Margin = Padding.Empty;
            cell.Padding = new(5, 4, 5, 4);
            cell.Font = row == 0 ? columnFont : Font;
            cell.ForeColor = row == 0 ? accent : foreground;
            cell.BackColor = background;
            cell.TextAlign = column == 0 ? ContentAlignment.MiddleLeft : ContentAlignment.MiddleRight;
            offers.Controls.Add(cell, column, row);
        }
    }

    private void AddOfferInfo(PriceWatchOffer offer, Color background)
    {
        string platforms = PriceWatchOfferDisplay.TagNames(offer.Platforms);
        string drm = PriceWatchOfferDisplay.TagNames(offer.Drm?.OfType<PriceWatchOfferTag>().Select(tag =>
            string.Equals(tag.Name, "DRM Free", StringComparison.OrdinalIgnoreCase)
                ? tag with { Name = T("PriceWatchDrmFree") }
                : tag).ToArray());
        if (platforms.Length == 0 && drm.Length == 0) return;

        string text = string.Join(" · ", new[] { platforms, drm }.Where(value => value.Length > 0));
        int row = offers.RowCount++;
        offers.RowStyles.Add(new(SizeType.AutoSize));
        var info = new Label
        {
            Name = "PriceWatchOfferInfo", AutoSize = true, UseMnemonic = false, Dock = DockStyle.Fill,
            Text = text, Margin = Padding.Empty, Padding = new(13, 0, 5, 6),
            Font = offerInfoFont, ForeColor = muted, BackColor = background,
            MaximumSize = new(AvailableContentWidth, 0)
        };
        offers.Controls.Add(info, 0, row);
        offers.SetColumnSpan(info, 3);
    }

    private int AvailableContentWidth => Math.Max(1, scroll.ClientSize.Width - content.Padding.Horizontal);

    private void FitText()
    {
        int width = AvailableContentWidth;
        heading.MaximumSize = edition.MaximumSize = new(Math.Max(1, width - header.Padding.Horizontal
            - cover.Width - cover.Margin.Horizontal), 0);
        descriptionText.MaximumSize = status.MaximumSize = updated.MaximumSize = new(width, 0);
        foreach (Control cell in offers.Controls)
            if (cell.Name == "PriceWatchOfferInfo") cell.MaximumSize = new(width, 0);
        actions.MaximumSize = new(width, 0);
    }

    private int ChromePixels(int logical) => Math.Max(1, logical * DeviceDpi / 96);

    private void ConfigureChrome()
    {
        int edge = ChromePixels(6);
        Padding = new(edge, ChromePixels(35), edge, edge);
        PerformLayout();
        Invalidate();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (chromeClose == null) return;
        int edge = ChromePixels(6);
        int width = ChromePixels(30);
        var bounds = new Rectangle(Math.Max(edge, ClientSize.Width - edge - width), edge, width, ChromePixels(23));
        if (chromeClose.Bounds != bounds) chromeClose.Bounds = bounds;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int thickFrame = 0x00040000;
            var parameters = base.CreateParams;
            parameters.Style |= thickFrame;
            return parameters;
        }
    }

    protected override void WndProc(ref Message message)
    {
        const int calculateClientArea = 0x0083;
        const int hitTest = 0x0084;
        const int paintNonClient = 0x0085;
        const int activateNonClient = 0x0086;
        if (message.Msg is calculateClientArea or paintNonClient)
        {
            // The entire frame is painted by the client while Windows handles sizing.
            message.Result = IntPtr.Zero;
            return;
        }
        if (message.Msg == hitTest)
        {
            long coordinates = message.LParam.ToInt64();
            Point local = PointToClient(new Point((short)coordinates, (short)(coordinates >> 16)));
            message.Result = HitZone(ClientSize, local, ChromePixels(6), Padding.Top, chromeClose.Bounds);
            return;
        }
        if (message.Msg == activateNonClient)
        {
            // Preserve default activation handling without repainting the system frame.
            message.LParam = new IntPtr(-1);
        }
        base.WndProc(ref message);
    }

    internal static int HitZone(Size area, Point point, int edge, int captionHeight, Rectangle closeButton)
    {
        bool left = point.X < edge, right = point.X >= area.Width - edge;
        bool top = point.Y < edge, bottom = point.Y >= area.Height - edge;
        if (top) return left ? 13 : right ? 14 : 12;
        if (bottom) return left ? 16 : right ? 17 : 15;
        if (left) return 10;
        if (right) return 11;
        return point.Y < captionHeight && !closeButton.Contains(point) ? 2 : 1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var fill = new SolidBrush(chromeBackground);
        using var edge = new Pen(chromeBorder);
        e.Graphics.FillRectangle(fill, 0, 0, ClientSize.Width, Padding.Top);
        e.Graphics.DrawLine(edge, Padding.Left, Padding.Top - 1, ClientSize.Width - Padding.Right, Padding.Top - 1);
        e.Graphics.DrawRectangle(edge, 0, 0, Math.Max(0, ClientSize.Width - 1), Math.Max(0, ClientSize.Height - 1));
        var caption = new Rectangle(ChromePixels(12), 0, Math.Max(0, chromeClose.Left - ChromePixels(20)), Padding.Top);
        TextRenderer.DrawText(e.Graphics, Text, columnFont, caption, foreground,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowsTheme.ApplyCustomFrame(this);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        FitToMonitor(fitContents: true);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ConfigureChrome();
        FitToMonitor(fitContents: false);
    }

    protected override void OnResizeBegin(EventArgs e)
    {
        resizeStart = Size;
        base.OnResizeBegin(e);
    }

    protected override void OnResizeEnd(EventArgs e)
    {
        userSized |= Size != resizeStart;
        FitToMonitor(fitContents: false);
        base.OnResizeEnd(e);
    }

    private void FitToMonitor(bool fitContents)
    {
        var area = Screen.FromRectangle(Bounds).WorkingArea;
        MinimumSize = new(Math.Min(ChromePixels(300), area.Width), Math.Min(ChromePixels(150), area.Height));
        Width = Math.Min(Width, area.Width);
        FitText();
        content.PerformLayout();
        if (fitContents) Height = content.Height + Padding.Vertical + Height - ClientSize.Height;
        Bounds = FitBounds(Bounds, area);
    }

    internal static Rectangle FitBounds(Rectangle bounds, Rectangle area)
    {
        int width = Math.Min(bounds.Width, area.Width), height = Math.Min(bounds.Height, area.Height);
        return new(Math.Clamp(bounds.X, area.Left, area.Right - width),
            Math.Clamp(bounds.Y, area.Top, area.Bottom - height), width, height);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            titleFont.Dispose();
            columnFont.Dispose();
            offerInfoFont.Dispose();
        }
    }

    private string T(string key) => Localization.Get(key, language);
}
