using System.Text;

namespace WallpaperControl;

internal sealed class PriceWatchSearchForm : Form
{
    private readonly TextBox query = new() { Dock = DockStyle.Fill, MaxLength = 120 };
    private readonly SearchButton search = new() { AutoSize = true };
    private readonly SearchButton select = new() { AutoSize = true, Enabled = false };
    private readonly ListBox results = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true, IntegralHeight = false };
    private readonly Label status = new() { AutoSize = true, Dock = DockStyle.Fill };
    private CancellationTokenSource? active;
    internal PriceWatchGame? SelectedGame { get; private set; }

    internal PriceWatchSearchForm(IPriceWatchSource source, bool live, string language, SystemWidgetStyle style)
    {
        string T(string key) => Localization.Get(key, language);
        Text = T("PriceWatchSearch") + " · " + T(live ? "PriceWatchLive" : "PriceWatchTest");
        AutoScaleDimensions = new(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        ClientSize = new(460, 295);
        MinimumSize = new(290, 200);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(10), ColumnCount = 2, RowCount = 4 };
        layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new(SizeType.AutoSize));
        layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.RowStyles.Add(new(SizeType.AutoSize));
        search.Text = T("PriceWatchSearch");
        query.AccessibleName = search.Text;
        select.Text = T("PriceWatchSelect");
        var cancel = new Button { Text = T("PriceWatchCancel"), AutoSize = true, DialogResult = DialogResult.Cancel };
        layout.Controls.Add(query, 0, 0);
        layout.Controls.Add(search, 1, 0);
        layout.Controls.Add(results, 0, 1);
        layout.SetColumnSpan(results, 2);
        layout.Controls.Add(status, 0, 2);
        layout.SetColumnSpan(status, 2);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        actions.Controls.AddRange([cancel, select]);
        layout.Controls.Add(actions, 0, 3);
        layout.SetColumnSpan(actions, 2);
        Controls.Add(layout);
        AcceptButton = search;
        CancelButton = cancel;
        bool dark = NotesDialogStyle.ResolveDarkMode();
        search.SetDark(dark);
        select.SetDark(dark);
        NotesDialogStyle.Apply(this, style, dark);
        results.SelectedIndexChanged += (_, _) => select.Enabled = active == null && results.SelectedItem is SearchItem;
        select.Click += (_, _) => Choose();
        results.DoubleClick += (_, _) => Choose();
        search.Click += async (_, _) =>
        {
            string title = query.Text.Trim();
            if (active != null) return;
            if (Encoding.UTF8.GetByteCount(title) is < 2 or > 120 || title.Any(char.IsControl))
            {
                status.Text = T("PriceWatchInvalidRequest");
                return;
            }
            using var operation = new CancellationTokenSource();
            active = operation;
            results.Items.Clear();
            select.Enabled = search.Enabled = false;
            status.Text = T("PriceWatchLoading");
            try
            {
                var response = await source.SearchAsync(title, operation.Token);
                if (operation.IsCancellationRequested || IsDisposed) return;
                foreach (var game in response.Data)
                    results.Items.Add(new SearchItem(game, T(EditionKey(game.Type))));
                status.Text = results.Items.Count == 0 ? T("PriceWatchNoResults") : "";
            }
            catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
            catch (PriceWatchException error) when (!operation.IsCancellationRequested && !IsDisposed)
            {
                status.Text = ErrorText(error, language);
            }
            catch (PriceWatchException) { }
            finally
            {
                active = null;
                if (!IsDisposed)
                {
                    search.Enabled = true;
                    select.Enabled = results.SelectedItem is SearchItem;
                }
            }
        };
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var work = Screen.FromControl(this).WorkingArea;
        MinimumSize = new(Math.Min(MinimumSize.Width, work.Width), Math.Min(MinimumSize.Height, work.Height));
        Size = new(Math.Min(Width, work.Width), Math.Min(Height, work.Height));
        Location = WidgetSettings.EnsureVisible(Location, Size);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        active?.Cancel();
        base.OnFormClosed(e);
    }

    private void Choose()
    {
        if (active != null || results.SelectedItem is not SearchItem item) return;
        SelectedGame = item.Game;
        DialogResult = DialogResult.OK;
    }

    internal static string EditionKey(string? type) => type switch
    {
        "game" => "PriceWatchGame",
        "package" => "PriceWatchPackage",
        "dlc" => "PriceWatchDlc",
        _ => "PriceWatchUnknown"
    };

    internal static string ErrorText(PriceWatchException error, string language)
    {
        string text = Localization.Get("PriceWatch" + error.Failure, language);
        return error.RetryAfter is { } wait && wait > TimeSpan.Zero
            ? $"{text} {string.Format(Localization.Get("PriceWatchWait", language), Math.Ceiling(wait.TotalSeconds))}"
            : text;
    }

    // Keep the shared dialog theme for active buttons. WinForms uses system gray for
    // a disabled flat button, which does not account for our dark themed surface.
    private sealed class SearchButton : Button
    {
        private bool dark;
        internal void SetDark(bool value) => dark = value;
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Enabled)
            {
                base.OnPaint(e);
                return;
            }
            var colors = PriceWatchWidgetRenderer.ActionColors(dark, false, false, false);
            using var fill = new SolidBrush(colors.Background);
            using var border = new Pen(FlatAppearance.BorderColor);
            e.Graphics.FillRectangle(fill, ClientRectangle);
            e.Graphics.DrawRectangle(border, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, colors.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    private sealed record SearchItem(PriceWatchGame Game, string Edition)
    {
        public override string ToString() => $"{Game.Title} · {Edition}";
    }
}
