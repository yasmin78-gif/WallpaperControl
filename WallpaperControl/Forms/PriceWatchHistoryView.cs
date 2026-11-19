namespace WallpaperControl;

internal sealed class PriceWatchHistoryView : UserControl
{
    private readonly Button toggle = new() { AutoSize = true, Name = "PriceWatchHistoryToggle" };
    private readonly ComboBox period = new() { Name = "PriceWatchHistoryPeriod", DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button refresh = new() { AutoSize = true, Name = "PriceWatchHistoryRefresh" };
    private readonly FlowLayoutPanel options = new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
    private readonly FlowLayoutPanel legend = new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
    private readonly TableLayoutPanel body = new() { Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true, Visible = false };
    private readonly Label summary = new() { AutoSize = true, UseMnemonic = false };
    private readonly Label state = new() { AutoSize = true, UseMnemonic = false };
    private readonly Label pointLegend = new() { AutoSize = true, UseMnemonic = false };
    private readonly PriceWatchHistoryChart chart;
    private readonly Func<int, CancellationToken, Task<PriceWatchReply<PriceWatchHistory[]>>> load;
    private readonly string language;
    private readonly int[] shops;
    private readonly bool testData;
    private readonly List<CheckBox> checks = new();
    private CancellationTokenSource? active;
    private PriceWatchReply<PriceWatchHistory[]>? reply;
    private PriceWatchEntry? entry;
    private PriceWatchObservation[] observations = [];
    private DateTimeOffset from, to;
    private int? target;
    private int version;
    private bool closed;

    internal PriceWatchHistoryView(string language, int[] shops,
        Func<int, CancellationToken, Task<PriceWatchReply<PriceWatchHistory[]>>> load, bool testData = false,
        SystemWidgetStyle style = SystemWidgetStyle.Clean)
    {
        this.language = language; this.shops = shops.ToArray(); this.load = load; this.testData = testData;
        Name = "PriceWatchHistoryView"; Dock = DockStyle.Top; AutoSize = true;
        Margin = new(0, 12, 0, 0);
        chart = new(language, style) { Dock = DockStyle.Top, Height = 220 };
        var root = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Margin = Padding.Empty };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        body.ColumnStyles.Add(new(SizeType.Percent, 100));
        toggle.Text = "▸ " + T("PriceWatchHistory");
        period.Items.AddRange(new object[] { T("PriceWatchHistory7"), T("PriceWatchHistory30"), T("PriceWatchHistory90") });
        period.SelectedIndex = 1; period.AccessibleName = T("PriceWatchHistoryPeriodLabel");
        refresh.Text = T("PriceWatchRefresh"); options.Controls.AddRange([period, refresh]);
        foreach (int id in shops)
        {
            var shop = PriceWatchShops.Supported.Single(s => s.Id == id);
            var check = new CheckBox { AutoSize = true, Checked = true, Text = shop.Name, Tag = id };
            check.CheckedChanged += (_, _) => RenderData();
            legend.Controls.Add(check); checks.Add(check);
        }
        body.Controls.Add(options, 0, 0); body.Controls.Add(legend, 0, 1);
        pointLegend.Text = "● " + T("PriceWatchHistoryITAD") + " · ○ " + T("PriceWatchHistoryLocal")
            + " · ◇ " + T("PriceWatchHistoryLast") + "\n┄┄ " + T("PriceWatchHistoryConnection");
        body.Controls.Add(chart, 0, 2); body.Controls.Add(pointLegend, 0, 3);
        body.Controls.Add(summary, 0, 4); body.Controls.Add(state, 0, 5);
        root.Controls.Add(toggle, 0, 0); root.Controls.Add(body, 0, 1); Controls.Add(root);
        toggle.Click += async (_, _) =>
        {
            body.Visible = !body.Visible;
            toggle.Text = (body.Visible ? "▾ " : "▸ ") + T("PriceWatchHistory");
            if (body.Visible && reply == null) await LoadAsync();
            if (!body.Visible) CancelLoad();
        };
        period.SelectedIndexChanged += async (_, _) => { if (body.Visible) await LoadAsync(); };
        refresh.Click += async (_, _) => await LoadAsync();
    }
    private string T(string key) => Localization.Get(key, language);
    internal void SetTarget(int? cents) { target = cents; RenderData(); }
    internal void SetPrices(PriceWatchEntry value, PriceWatchObservation[] local)
    {
        entry = value; observations = local;
        if (to != default)
        {
            to = DateTimeOffset.UtcNow;
            from = to.AddDays(-(period.SelectedIndex switch { 0 => 7, 2 => 90, _ => 30 }));
        }
        RenderData();
    }
    internal void ApplyScale(int chartHeight)
    {
        chart.Height = chartHeight;
        period.Width = Math.Max(110 * DeviceDpi / 96, TextRenderer.MeasureText(T("PriceWatchHistory90"), Font).Width + 40 * DeviceDpi / 96);
        foreach (var check in checks) check.ForeColor = PriceWatchHistoryChart.ShopColor((int)check.Tag!);
    }
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (summary != null) summary.MaximumSize = state.MaximumSize = new(Math.Max(1, ClientSize.Width - 12), 0);
        if (pointLegend != null) pointLegend.MaximumSize = new(Math.Max(1, ClientSize.Width - 12), 0);
        foreach (var check in checks) check.MaximumSize = new(Math.Max(1, ClientSize.Width - 12), 0);
    }
    private Task Ui(Action action)
    {
        if (closed || IsDisposed || !IsHandleCreated) return Task.CompletedTask;
        if (!InvokeRequired) { action(); return Task.CompletedTask; }
        return Dispatch();
        async Task Dispatch()
        {
            try { await this.InvokeAsync(() => { if (!closed && !IsDisposed) action(); }); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) when (closed || IsDisposed) { }
        }
    }
    internal async Task LoadAsync()
    {
        if (closed || !IsHandleCreated) return;
        CancelLoad(); int generation = version;
        using var operation = new CancellationTokenSource(); active = operation;
        int days = period.SelectedIndex switch { 0 => 7, 2 => 90, _ => 30 };
        to = DateTimeOffset.UtcNow; from = to.AddDays(-days);
        // Cache ownership belongs to the requested window. Never relabel an earlier reply as this window.
        reply = null;
        state.Text = T("PriceWatchHistoryLoading"); RenderData();
        try
        {
            var result = await load(days, operation.Token);
            await Ui(() =>
            {
                if (operation.IsCancellationRequested || generation != version) return;
                reply = result; RenderData();
                state.Text = (testData ? T("PriceWatchTest") + " · " : "")
                    + T("PriceWatchHistorySource") + " · " + result.FetchedAt.ToLocalTime().ToString("g");
            });
        }
        catch (OperationCanceledException)
        {
            await Ui(() => { if (generation == version) state.Text = T("PriceWatchCancel"); });
        }
        catch (PriceWatchException error)
        {
            await Ui(() => { if (generation == version) state.Text = PriceWatchSearchForm.ErrorText(error, language); });
        }
        catch (Exception)
        {
            await Ui(() => { if (generation == version) state.Text = T("PriceWatchConnection"); });
        }
        finally { await Ui(() => { if (active == operation) active = null; }); }
    }
    private void RenderData()
    {
        if (closed) return;
        var all = PriceWatchHistorySeries.Create(reply?.Data ?? [], shops, from, to);
        foreach (var check in checks)
        {
            int id = (int)check.Tag!;
            string name = PriceWatchShops.Supported.Single(s => s.Id == id).Name;
            check.Text = name + (reply != null && !all.Single(s => s.Shop == id).Points.Any(p => p.Deal != null)
                ? " · " + T("PriceWatchHistoryNoData") : "");
        }
        var visible = all.Where(s => checks.Any(c => (int)c.Tag! == s.Shop && c.Checked)).ToArray();
        int[] selected = visible.Select(s => s.Shop).ToArray();
        var local = entry == null ? [] : PriceWatchLocalPrices.Create(entry, !testData, observations, selected,
            from, to, DateTimeOffset.UtcNow);
        chart.Present(visible, from, to, target, local);
        var low = PriceWatchHistorySeries.Minimum(visible);
        summary.Text = T("PriceWatchHistoryMinimum") + ": " + (low == null ? "—" :
            $"{low.Deal!.Price.Amount:N2} € · {low.Shop.Name} · {low.Timestamp.ToLocalTime():dd.MM.yyyy HH:mm}");
    }
    internal void CancelLoad()
    {
        version++;
        var pending = active; active = null;
        pending?.Cancel();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !closed) { closed = true; CancelLoad(); }
        base.Dispose(disposing);
    }
}
