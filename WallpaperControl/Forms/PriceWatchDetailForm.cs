using System.Globalization;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace WallpaperControl;

internal sealed class PriceWatchDetailForm : Form
{
    private readonly Label heading = new() { AutoSize = true, UseMnemonic = false, Margin = new(0, 0, 0, 3) };
    private readonly Label edition = new() { AutoSize = true, Margin = Padding.Empty };
    private readonly Label updated = new() { AutoSize = true, Margin = new(0, 10, 0, 0) };
    private readonly Label status = new() { AutoSize = true, Margin = new(0, 4, 0, 0) };
    private readonly Label gameInfo = new() { Name = "PriceWatchGameInfo", AutoSize = true, UseMnemonic = false,
        Margin = new(0, 8, 0, 0) };
    private PriceWatchGameInfo? currentInfo;
    private readonly Label descriptionText = new() { Name = "PriceWatchDescription", AutoSize = true, UseMnemonic = false,
        Margin = Padding.Empty };
    private readonly Label descriptionSource = new() { Name = "PriceWatchDescriptionSource", AutoSize = true,
        UseMnemonic = false, Margin = new(0, 3, 0, 0) };
    private readonly TableLayoutPanel header = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2,
        Padding = new(16, 12, 16, 12), Margin = Padding.Empty };
    private readonly TableLayoutPanel about = new() { Name = "PriceWatchAbout", Dock = DockStyle.Top, AutoSize = true,
        ColumnCount = 1, Margin = new(0, 12, 0, 0) };
    private readonly PriceWatchCover cover = new() { Name = "PriceWatchCover", Size = new(120, 170), Margin = new(0, 0, 18, 0) };
    private readonly Label coverCredit = new() { Name = "PriceWatchCoverCredit", AutoSize = true, Visible = false,
        UseMnemonic = false, Margin = new(0, 4, 18, 0) };
    private readonly FlowLayoutPanel targetArea = new() { Name = "PriceWatchTargetArea", Dock = DockStyle.Top,
        AutoSize = true, Margin = Padding.Empty };
    private readonly TextBox targetInput = new() { Name = "PriceWatchTargetInput", Width = 82, MaxLength = 24 };
    private readonly Button targetSave = new() { Name = "PriceWatchTargetSave", AutoSize = true,
        Margin = new(3, 0, 3, 0) };
    private PriceWatchEntry currentEntry;
    private readonly int[] selectedShops;
    private readonly TableLayoutPanel offers = new() { Name = "PriceWatchOffers", Dock = DockStyle.Top,
        AutoSize = true, ColumnCount = 3, Margin = new(0, 12, 0, 0) };
    private readonly Panel scroll = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly TableLayoutPanel content = new() { Dock = DockStyle.Top, AutoSize = true,
        ColumnCount = 1, Padding = new(20, 16, 20, 4) };
    private readonly FlowLayoutPanel actions = new() { Dock = DockStyle.Top, AutoSize = true, Margin = new(0, 10, 0, 0) };
    private readonly Button chromeClose = new() { Name = "PriceWatchChromeClose", Text = "×", UseMnemonic = false };
    private readonly bool shopLinksEnabled;
    private readonly Action<Uri> openLink;
    private readonly string language;
    private readonly CultureInfo culture;
    private readonly Color accent, muted, stripe, foreground;
    private Font bodyFont, titleFont, columnFont, offerInfoFont;
    private readonly Color chromeBackground, chromeBorder;
    private readonly AppSettingsStore? sizeSettings;
    private readonly bool sizeLocked;
    private readonly PriceWatchItadCovers? itadCovers;
    private bool coverReady;
    private string? displayedCoverUrl;
    private bool scaleContents;
    private bool sizing;
    private Size resizeStart;
    private double layoutScale = 1;
    private (int Width, int Dpi) lastLayout;

    internal PriceWatchDetailForm(PriceWatchEntry entry, string mode, string language, SystemWidgetStyle style,
        Func<Task> refresh, Func<bool> remove,
        bool shopLinksEnabled = false, Action<Uri>? openLink = null, PriceWatchGameInfo? info = null,
        Func<Task>? refreshInfo = null, bool fetchInfoOnOpen = false,
        PriceWatchDescription? description = null, PriceWatchCoverSource? coverSource = null,
        AppSettingsStore? sizeSettings = null, bool sizeLocked = false, PriceWatchItadCovers? itadCovers = null,
        Func<int?, bool>? saveTarget = null, IReadOnlyList<int>? shops = null)
    {
        selectedShops = PriceWatchShops.Validate(shops ?? PriceWatchShops.Legacy);
        currentEntry = entry;
        this.sizeSettings = sizeSettings;
        this.sizeLocked = sizeLocked;
        this.itadCovers = itadCovers;
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
        ClientSize = new(PriceWatchDetailSize.StandardWidth, PriceWatchDetailSize.StandardHeight);
        MinimumSize = new(300, 150);
        var dark = NotesDialogStyle.ResolveDarkMode();
        var palette = PriceWatchWidgetRenderer.ContentColors(dark, style);
        accent = palette.Accent;
        muted = dark ? palette.Muted : AppTheme.TextSecondary(false);
        stripe = AppTheme.PanelBackground(dark);
        foreground = palette.Text;
        chromeBackground = AppTheme.PanelBackground(dark);
        chromeBorder = style == SystemWidgetStyle.Minimal ? AppTheme.Border(dark) : accent;
        bodyFont = new Font("Segoe UI", 11.25f);
        titleFont = new Font("Segoe UI Semibold", 16.5f);
        columnFont = new Font("Segoe UI", 11.25f, FontStyle.Bold);
        offerInfoFont = new Font("Segoe UI", 10.5f);
        content.ColumnStyles.Add(new(SizeType.Percent, 100));
        header.ColumnStyles.Add(new(SizeType.AutoSize));
        header.ColumnStyles.Add(new(SizeType.Percent, 100));
        var coverColumn = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Margin = Padding.Empty };
        coverColumn.Controls.Add(cover, 0, 0);
        coverColumn.Controls.Add(coverCredit, 0, 1);
        header.Controls.Add(coverColumn, 0, 0);
        var gameHeader = new TableLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right,
            ColumnCount = 1, Margin = Padding.Empty };
        gameHeader.ColumnStyles.Add(new(SizeType.Percent, 100));
        gameHeader.Controls.Add(heading, 0, 0);
        gameHeader.Controls.Add(edition, 0, 1);
        gameHeader.Controls.Add(gameInfo, 0, 2);
        header.Controls.Add(gameHeader, 1, 0);
        header.Paint += (_, e) => DrawHeader(e.Graphics, dark, style);
        cover.Paint += (_, e) =>
        {
            if (!cover.HasImage)
                PriceWatchWidgetRenderer.DrawCoverPlaceholder(e.Graphics,
                    new RectangleF(1, 1, Math.Max(1, cover.Width - 2), Math.Max(1, cover.Height - 2)), dark, muted);
        };
        if (coverSource != null) Shown += async (_, _) => await cover.LoadAsync(coverSource);
        if (itadCovers != null) Shown += (_, _) =>
        {
            coverReady = true;
            UpdateItadCover();
        };
        FormClosed += (_, _) =>
        {
            coverReady = false;
            cover.CancelLoad();
        };
        content.Controls.Add(header, 0, 0);
        about.ColumnStyles.Add(new(SizeType.Percent, 100));
        var aboutHeading = new Label { AutoSize = true, Text = T("PriceWatchAboutGame"), Margin = new(0, 0, 0, 6) };
        about.Controls.Add(aboutHeading, 0, 0);
        about.Controls.Add(descriptionText, 0, 1);
        about.Controls.Add(descriptionSource, 0, 2);
        if (description != null)
        {
            descriptionText.Text = description.Text;
            descriptionSource.Text = string.Format(culture, T("PriceWatchDescriptionSource"), description.Source);
            if (description.IsBaseGameFallback)
                aboutHeading.Text += $" · {T("PriceWatchBaseGameDescription")}";
        }
        about.Visible = description != null;
        content.Controls.Add(about, 0, 1);
        offers.ColumnStyles.Add(new(SizeType.Percent, 38));
        offers.ColumnStyles.Add(new(SizeType.Percent, 35));
        offers.ColumnStyles.Add(new(SizeType.Percent, 27));
        content.Controls.Add(offers, 0, 2);
        // A history section can be placed after the offers when a chart is available.
        if (saveTarget != null)
        {
            var targetLabel = new Label { Text = T("PriceWatchTarget"), AutoSize = true, Margin = new(0, 6, 6, 0) };
            targetInput.AccessibleName = T("PriceWatchTarget");
            targetInput.Text = entry.TargetCents is int cents ? PriceWatchTargetPrice.Format(cents) : "";
            targetSave.Text = T("PriceWatchTargetSave");
            targetSave.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            targetArea.Controls.AddRange([targetLabel, targetInput, targetSave, updated]);
            content.Controls.Add(targetArea, 0, 3);
            targetSave.Click += (_, _) =>
            {
                if (!PriceWatchTargetPrice.TryParse(targetInput.Text, out int? value))
                {
                    SetStatus(T("PriceWatchTargetInvalid"));
                    targetInput.Focus();
                    return;
                }
                if (!saveTarget(value))
                {
                    SetStatus(T("PriceWatchStorageError"));
                    return;
                }
                targetInput.Text = value is int saved ? PriceWatchTargetPrice.Format(saved) : "";
                Present(currentEntry with { TargetCents = value }, "");
            };
        }
        else content.Controls.Add(updated, 0, 3);
        content.Controls.Add(status, 0, 4);
        var fetch = new Button { Text = T("PriceWatchRefresh"), AutoSize = true };
        var delete = new Button { Text = T("PriceWatchRemove"), AutoSize = true };
        var close = new Button { Text = T("AboutClose"), AutoSize = true, DialogResult = DialogResult.Cancel };
        var fetchInfo = new Button { Name = "PriceWatchRefreshInfo", Text = T("PriceWatchRefreshInfo"), AutoSize = true };
        actions.Controls.Add(fetch);
        if (refreshInfo != null) actions.Controls.Add(fetchInfo);
        else fetchInfo.Dispose();
        actions.Controls.AddRange([delete, close]);
        foreach (Button button in actions.Controls)
        {
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.Margin = new Padding(0, 0, 8, 6);
            button.Padding = new Padding(10, 4, 10, 4);
        }
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
        Font = bodyFont;
        heading.Font = titleFont;
        gameHeader.BackColor = gameInfo.BackColor = heading.BackColor = edition.BackColor = cover.BackColor = Color.Transparent;
        coverColumn.BackColor = coverCredit.BackColor = Color.Transparent;
        coverCredit.Text = T("PriceWatchItadCoverCredit");
        coverCredit.ForeColor = muted;
        heading.ForeColor = foreground;
        edition.ForeColor = accent;
        aboutHeading.Font = columnFont;
        aboutHeading.ForeColor = accent;
        descriptionText.ForeColor = foreground;
        descriptionSource.ForeColor = muted;
        descriptionSource.Font = updated.Font = coverCredit.Font = offerInfoFont;
        updated.ForeColor = muted;
        delete.Click += (_, _) =>
        {
            if (MessageBox.Show(this, T("PriceWatchConfirmRemove"), Text, MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            if (remove()) Close();
            else SetStatus(T("PriceWatchStorageError"));
        };
        fetch.Click += async (_, _) => await Run(refresh);
        if (refreshInfo != null)
        {
            fetchInfo.Click += async (_, _) => await Run(refreshInfo);
            if (fetchInfoOnOpen) Load += async (_, _) => await Run(refreshInfo);
        }
        scroll.Resize += (_, _) => FitText();
        FitText();
        PresentInfo(info);
        Present(entry, "");

        async Task Run(Func<Task> action)
        {
            fetch.Enabled = delete.Enabled = false;
            if (!fetchInfo.IsDisposed) fetchInfo.Enabled = false;
            SetStatus(T("PriceWatchLoading"));
            try { await action(); }
            finally
            {
                if (!IsDisposed)
                {
                    fetch.Enabled = delete.Enabled = true;
                    if (!fetchInfo.IsDisposed) fetchInfo.Enabled = true;
                }
            }
        }
    }

    private void DrawHeader(Graphics graphics, bool dark, SystemWidgetStyle style)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = WidgetDrawing.RoundedRectangle(new(2, 2,
            Math.Max(1, header.Width - 4), Math.Max(1, header.Height - 4)), LayoutPixels(8));
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
        currentEntry = entry;
        heading.Text = currentInfo?.Title ?? entry.Game.Title;
        edition.Text = T(PriceWatchSearchForm.EditionKey(entry.Game.Type));
        offers.SuspendLayout();
        try
        {
            foreach (Control cell in offers.Controls.Cast<Control>().ToArray()) cell.Dispose();
            offers.RowStyles.Clear();
            offers.RowCount = 0;
            AddRow(T("PriceWatchMerchant"), T("PriceWatchPrice"), T("PriceWatchDiscount"), stripe);
            int offerIndex = 0;
            foreach (int shopId in selectedShops)
            {
                var shop = PriceWatchShops.Supported.Single(shop => shop.Id == shopId);
                var available = PriceWatchOfferDisplay.ForShop(entry, shop.Id)
                    .Where(offer => PriceWatchShops.ValidOffer(offer, DateTimeOffset.UtcNow))
                    .OrderBy(offer => offer.Price.AmountInt).ToArray();
                if (available.Length == 0)
                    AddRow(shop.Name, T("PriceWatchNoOffer"), "—", (offerIndex++ % 2 == 0) ? BackColor : stripe);
                foreach (var offer in available)
                {
                    Color background = offerIndex++ % 2 == 0 ? BackColor : stripe;
                    AddRow(shop.Name, PriceWatchOfferDisplay.Price(offer, culture),
                        PriceWatchOfferDisplay.Discount(offer, culture), background, offer.Url);
                    if (PriceWatchTargetPrice.IsReached(entry, offer, DateTimeOffset.UtcNow, selectedShops))
                        offers.GetControlFromPosition(1, offers.RowCount - 1)!.ForeColor =
                            foreground.GetBrightness() > .5 ? Color.LightGreen : Color.FromArgb(0, 100, 40);
                    AddOfferInfo(offer, background);
                }
            }
        }
        finally { offers.ResumeLayout(true); }
        updated.Text = entry.Snapshot == null ? T("PriceWatchNotChecked") :
            $"{T("PriceWatchUpdated")} {entry.Snapshot.FetchedAt.ToLocalTime().ToString("g", culture)}";
        SetStatus(message);
    }

    internal void PresentInfo(PriceWatchGameInfo? info, string message = "")
    {
        if (info != null) currentInfo = info;
        if (currentInfo is { } game)
        {
            heading.Text = game.Title;
            var fields = new List<string>();
            if (game.ReleaseDate is { } date)
                fields.Add($"{T("PriceWatchReleased")}: {date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}");
            if (game.Developers.Length > 0)
                fields.Add($"{T("PriceWatchDevelopers")}: {FormatCompanies(game.Developers)}");
            if (game.Publishers.Length > 0)
                fields.Add($"{T("PriceWatchPublishers")}: {FormatCompanies(game.Publishers)}");
            gameInfo.Text = string.Join(Environment.NewLine, fields);
        }
        gameInfo.Visible = gameInfo.Text.Length > 0;
        SetStatus(message);
        if (info != null && coverReady) UpdateItadCover();
    }

    private async void UpdateItadCover()
    {
        var url = currentInfo?.Boxart;
        string? key = PriceWatchItadCovers.IsAllowed(url) ? url!.AbsoluteUri : null;
        if (displayedCoverUrl == key) return;
        displayedCoverUrl = key;
        coverCredit.Visible = false;
        if (key == null)
        {
            cover.ClearImage();
            return;
        }
        if (await cover.LoadAsync(token => itadCovers!.LoadAsync(url, token))
            && coverReady && displayedCoverUrl == key)
            coverCredit.Visible = true;
    }

    private static string FormatCompanies(IEnumerable<string> names)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return string.Join(", ", names.Where(name => seen.Add(
            string.Equals(name, "CD Project Red", StringComparison.OrdinalIgnoreCase) ? "CD PROJEKT RED" : name)));
    }

    private void SetStatus(string message)
    {
        status.Text = message;
        status.Visible = message.Length != 0;
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
            cell.Padding = OfferPadding(false);
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
            Text = text, Margin = Padding.Empty, Padding = OfferPadding(true),
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
        heading.MaximumSize = edition.MaximumSize = gameInfo.MaximumSize = new(Math.Max(1, width - header.Padding.Horizontal
            - cover.Width - cover.Margin.Horizontal), 0);
        descriptionText.MaximumSize = status.MaximumSize = updated.MaximumSize = new(width, 0);
        descriptionSource.MaximumSize = new(width, 0);
        about.Controls[0].MaximumSize = new(width, 0);
        foreach (Control cell in offers.Controls)
            if (cell.Name == "PriceWatchOfferInfo") cell.MaximumSize = new(width, 0);
        actions.MaximumSize = new(width, 0);
        targetArea.MaximumSize = new(width, 0);
    }

    private int ChromePixels(int logical) => Math.Max(1, logical * DeviceDpi / 96);
    private int LayoutPixels(int logical) => Math.Max(1, (int)Math.Round(logical * layoutScale * DeviceDpi / 96));

    private Padding OfferPadding(bool metadata) => metadata
        ? new(LayoutPixels(16), 0, LayoutPixels(8), LayoutPixels(8))
        : new(LayoutPixels(8), LayoutPixels(5), LayoutPixels(8), LayoutPixels(5));

    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        if (scaleContents) ApplyContentScale();
    }

    private void ApplyContentScale()
    {
        if (lastLayout == (ClientSize.Width, DeviceDpi)) return;
        lastLayout = (ClientSize.Width, DeviceDpi);
        layoutScale = Math.Clamp(PriceWatchDetailSize.ToLogical(ClientSize.Width, DeviceDpi)
            / (double)PriceWatchDetailSize.StandardWidth, 0.65, 1.65);
        SuspendLayout();
        scroll.SuspendLayout();
        content.SuspendLayout();
        header.SuspendLayout();
        about.SuspendLayout();
        offers.SuspendLayout();
        actions.SuspendLayout();
        try
        {
            ApplyFonts();
            content.Padding = new(LayoutPixels(20), LayoutPixels(16), LayoutPixels(20), LayoutPixels(4));
            header.Padding = new(LayoutPixels(16), LayoutPixels(12), LayoutPixels(16), LayoutPixels(12));
            cover.Size = new(LayoutPixels(120), LayoutPixels(170));
            cover.Margin = new(0, 0, LayoutPixels(18), 0);
            coverCredit.Margin = new(0, LayoutPixels(4), LayoutPixels(18), 0);
            coverCredit.MaximumSize = new(LayoutPixels(190), 0);
            heading.Margin = new(0, 0, 0, LayoutPixels(3));
            descriptionSource.Margin = new(0, LayoutPixels(3), 0, 0);
            gameInfo.Margin = new(0, LayoutPixels(8), 0, 0);
            about.Margin = new(0, LayoutPixels(12), 0, 0);
            about.Controls[0].Margin = new(0, 0, 0, LayoutPixels(6));
            offers.Margin = new(0, LayoutPixels(12), 0, 0);
            updated.Margin = targetArea.Parent != null ? new(LayoutPixels(12), LayoutPixels(6), 0, 0)
                : new(0, LayoutPixels(10), 0, 0);
            targetInput.Width = LayoutPixels(82);
            targetArea.Margin = Padding.Empty;
            status.Margin = new(0, LayoutPixels(4), 0, 0);
            actions.Margin = new(0, LayoutPixels(10), 0, 0);
            foreach (Button button in actions.Controls)
            {
                button.Margin = new(0, 0, LayoutPixels(8), LayoutPixels(6));
                button.Padding = new(LayoutPixels(10), LayoutPixels(4), LayoutPixels(10), LayoutPixels(4));
            }
            foreach (Control cell in offers.Controls)
                cell.Padding = OfferPadding(cell.Name == "PriceWatchOfferInfo");
            ConfigureChrome();
        }
        finally
        {
            actions.ResumeLayout();
            offers.ResumeLayout();
            about.ResumeLayout();
            header.ResumeLayout();
            content.ResumeLayout();
            scroll.ResumeLayout();
            ResumeLayout();
        }
        FitText();
    }

    private void ApplyFonts()
    {
        float Points(double baseline, double minimum, double maximum) =>
            (float)(Math.Round(Math.Clamp(baseline * layoutScale, minimum, maximum) * 4) / 4);
        float body = Points(11.25, 9, 17), title = Points(16.5, 13, 25), small = Points(10.5, 8.5, 16);
        if (bodyFont.SizeInPoints == body && titleFont.SizeInPoints == title && offerInfoFont.SizeInPoints == small)
            return;
        Font[] previous = [bodyFont, titleFont, columnFont, offerInfoFont];
        bodyFont = new("Segoe UI", body);
        titleFont = new("Segoe UI Semibold", title, FontStyle.Bold);
        columnFont = new("Segoe UI", body, FontStyle.Bold);
        offerInfoFont = new("Segoe UI", small);
        Font = bodyFont;
        heading.Font = titleFont;
        about.Controls[0].Font = columnFont;
        descriptionSource.Font = updated.Font = coverCredit.Font = offerInfoFont;
        foreach (Control cell in offers.Controls)
            cell.Font = cell.Name == "PriceWatchOfferInfo" ? offerInfoFont
                : offers.GetRow(cell) == 0 ? columnFont : bodyFont;
        foreach (Font font in previous) font.Dispose();
    }

    private void ConfigureChrome()
    {
        int edge = ChromePixels(6);
        Padding = new(edge, Math.Max(ChromePixels(29), LayoutPixels(35)), edge, edge);
        PerformLayout();
        Invalidate();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (chromeClose == null) return;
        int edge = ChromePixels(6);
        int width = Math.Max(ChromePixels(27), LayoutPixels(30));
        var bounds = new Rectangle(Math.Max(edge, ClientSize.Width - edge - width), edge, width, Padding.Top - 2 * edge);
        if (chromeClose.Bounds != bounds) chromeClose.Bounds = bounds;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int thickFrame = 0x00040000;
            var parameters = base.CreateParams;
            parameters.Style = sizeLocked ? parameters.Style & ~thickFrame : parameters.Style | thickFrame;
            return parameters;
        }
    }

    protected override void WndProc(ref Message message)
    {
        const int calculateClientArea = 0x0083;
        const int hitTest = 0x0084;
        const int paintNonClient = 0x0085;
        const int activateNonClient = 0x0086;
        const int sizeWindow = 0x0214;
        if (message.Msg == sizeWindow && message.WParam.ToInt64() is >= 1 and <= 8 && message.LParam != IntPtr.Zero)
        {
            int edge = message.WParam.ToInt32();
            var native = Marshal.PtrToStructure<NativeRectangle>(message.LParam);
            var proposed = Rectangle.FromLTRB(native.Left, native.Top, native.Right, native.Bottom);
            var resized = sizeLocked ? Bounds
                : PriceWatchDetailSize.Resize(proposed, edge, DeviceDpi, Screen.FromRectangle(Bounds).WorkingArea);
            Marshal.StructureToPtr(new NativeRectangle
            {
                Left = resized.Left, Top = resized.Top, Right = resized.Right, Bottom = resized.Bottom
            }, message.LParam, false);
            sizing = !sizeLocked;
            message.Result = new IntPtr(1);
            return;
        }
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
            message.Result = HitZone(ClientSize, local, sizeLocked ? 0 : ChromePixels(6), Padding.Top, chromeClose.Bounds);
            return;
        }
        if (message.Msg == activateNonClient)
        {
            // Preserve default activation handling without repainting the system frame.
            message.LParam = new IntPtr(-1);
        }
        base.WndProc(ref message);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        internal int Left, Top, Right, Bottom;
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
        int logicalWidth = sizeSettings?.LoadPriceWatchDetailWidth() is int saved && PriceWatchDetailSize.IsValidWidth(saved)
            ? saved : PriceWatchDetailSize.StandardWidth;
        FitToMonitor(logicalWidth * DeviceDpi / 96d, center: true);
        scaleContents = true;
        ApplyContentScale();
        base.OnShown(e);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        FitToMonitor(e.SuggestedRectangle.Width);
        ApplyContentScale();
    }

    protected override void OnResizeBegin(EventArgs e)
    {
        resizeStart = Size;
        sizing = false;
        base.OnResizeBegin(e);
    }

    protected override void OnResizeEnd(EventArgs e)
    {
        FitToMonitor(Width);
        int logicalWidth = PriceWatchDetailSize.ToLogical(Width, DeviceDpi);
        if (sizing && Size != resizeStart && PriceWatchDetailSize.IsValidWidth(logicalWidth))
            sizeSettings?.SavePriceWatchDetailWidth(logicalWidth);
        sizing = false;
        base.OnResizeEnd(e);
    }

    private void FitToMonitor(double width, bool center = false)
    {
        var area = Screen.FromRectangle(Bounds).WorkingArea;
        MinimumSize = PriceWatchDetailSize.Constrain(0, DeviceDpi, area.Size);
        Size size = PriceWatchDetailSize.Constrain(width, DeviceDpi, area.Size);
        Point location = center ? new(Left + (Width - size.Width) / 2, Top + (Height - size.Height) / 2) : Location;
        var bounds = FitBounds(new(location, size), area);
        if (Bounds != bounds) Bounds = bounds;
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
            bodyFont.Dispose();
        }
    }

    private string T(string key) => Localization.Get(key, language);
}
