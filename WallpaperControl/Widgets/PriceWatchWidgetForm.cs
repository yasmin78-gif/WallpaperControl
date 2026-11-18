using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace WallpaperControl;

internal sealed class PriceWatchWidgetForm : Form
{
    private readonly Func<bool, IPriceWatchSource> source;
    private readonly PriceWatchStore store;
    private readonly Action<Uri> openLink;
    private (int Row, int Shop)? hoveredMerchant;
    private int pressedMerchant = -1;
    private Uri? pressedUrl;
    private readonly CalendarViewport viewport = new(PriceWatchWidgetRenderer.Width);
    private PriceWatchEntry[] entries = [];
    private int selectedIndex = -1;
    private Guid? pressedRow;
    private float? thumbGrab;
    private readonly WidgetDragHandler drag;
    private readonly ToolTip tips = new();
    private PriceWatchWidgetLayout layout;
    private int hovered, pressed;
    private bool rendering;
    private PriceWatchWidgetSettings settings;
    private string language;
    private bool preview, suspended, dialogOpen;
    private bool dark = NotesDialogStyle.ResolveDarkMode();
    private CancellationTokenSource? active;
    private PriceWatchSearchForm? searchDialog;
    private PriceWatchDetailForm? detailDialog;
    private PriceWatchItadCovers? itadCovers;
    private readonly Dictionary<(bool Live, Guid Id), Bitmap> rowCovers = new();
    private readonly Dictionary<(bool Live, Guid Id), string> rowCoverUrls = new();
    private readonly Dictionary<(bool Live, Guid Id), CancellationTokenSource> coverLoads = new();
    private readonly HashSet<(bool Live, Guid Id, string Url)> failedCovers = new();
    private readonly HashSet<(bool Live, Guid Id)> autoInfoAttempted = [];
    private bool storageError;
    private readonly Dictionary<(bool Live, Guid Id), CancellationTokenSource> initialLoads = new();
    private readonly SemaphoreSlim initialLoadQueue = new(1, 1);
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly CancellationToken lifetimeToken;
    private int selectionVersion;
    private bool selectionRefreshPending, selectionRefreshing;
    private int[] lastRequestedShops;
    private readonly System.Windows.Forms.Timer titleTimer = new() { Interval = 33 };
    private readonly System.Diagnostics.Stopwatch titleClock = new();
    private int scrollingTitle = -1;
    private bool titleFrame;
    private PriceWatchException? failure;

    internal PriceWatchWidgetForm(Func<bool, IPriceWatchSource> source, PriceWatchWidgetSettings value,
        string language, Action<Point> moved, PriceWatchStore store, Action<Uri>? openLink = null)
    {
        this.source = source;
        lifetimeToken = lifetimeCancellation.Token;
        this.store = store;
        this.openLink = openLink ?? PriceWatchShopLinks.Open;
        settings = value.Clone();
        lastRequestedShops = settings.ShopIds.ToArray();
        this.language = language;
        titleTimer.Tick += (_, _) =>
        {
            titleFrame = true;
            try { Render(); }
            finally { titleFrame = false; }
        };
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;
        drag = new(this, () => settings.Locked, Render, moved);
        ReadEntries();
        Apply(value, language, false);
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var value = base.CreateParams; value.ExStyle |= 0x00080000 | 0x00000080 | 0x08000000; return value; }
    }

    protected override void WndProc(ref Message message)
    {
        if (DesktopWidgetNative.HandleMouseActivation(ref message)) return;
        if (message.Msg == 0x020A)
        {
            StopTitleScroll();
            if (viewport.Wheel((short)((message.WParam.ToInt64() >> 16) & 0xffff), SystemInformation.MouseWheelScrollLines))
                Render();
            message.Result = 0;
            return;
        }
        base.WndProc(ref message);
    }

    private bool CanOperate => !preview && !suspended && active == null;
    private bool CanSearch => CanOperate && !dialogOpen && !storageError;
    private bool CanRefresh => CanSearch && SelectedEntry != null
        && !initialLoads.ContainsKey((settings.Live, SelectedEntry.Game.Id));
    private PriceWatchEntry? SelectedEntry => selectedIndex >= 0 ? entries[selectedIndex] : null;
    internal string ModeCaption => T(settings.Live ? "PriceWatchLive" : "PriceWatchTest");
    private string StatusCaption => preview ? T("PriceWatchPreview")
        : active is { IsCancellationRequested: false } ? T("PriceWatchLoading")
        : storageError ? T("PriceWatchStorageError")
        : failure != null ? PriceWatchSearchForm.ErrorText(failure, language)
        : initialLoads.Keys.Any(key => key.Live == settings.Live) ? T("PriceWatchLoading") : "";

    internal void FitToScreen(Point point)
    {
        using var bitmap = RenderBitmap(Screen.FromPoint(point).WorkingArea.Size, DeviceDpi, out layout);
        ClientSize = bitmap.Size;
        Location = WidgetSettings.EnsureVisible(point, bitmap.Size);
        Render();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Render();
        ScheduleSelectionRefresh();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        StopTitleScroll();
        base.OnDpiChanged(e);
        FitToScreen(Location);
    }

    private PriceWatchOffer[] DisplayOffers(PriceWatchEntry entry) =>
        PriceWatchShops.WidgetOffers(entry, settings.ShopIds, DateTimeOffset.UtcNow);

    private Uri? MerchantLink(int row, int shop)
    {
        if (!CanSearch || !settings.Live || row < 0 || shop < 0) return null;
        var offers = DisplayOffers(entries[row]);
        return shop < offers.Length ? PriceWatchShopLinks.Validate(offers[shop].Url) : null;
    }

    internal Bitmap RenderBitmap(Size area, int dpi, out PriceWatchWidgetLayout result)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        return PriceWatchWidgetRenderer.Draw(area, dpi, settings.Style, dark, language, ModeCaption,
            T("PriceWatchWatchlistEmpty"), StatusCaption, T("PriceWatchSearch"), T("PriceWatchRefresh"),
            CanSearch, CanRefresh, hovered, pressed, viewport, entries.Length, index =>
            {
                var entry = entries[index];
                var offers = DisplayOffers(entry);
                var prices = offers.Select(offer => PriceWatchOfferDisplay.Columns(
                    PriceWatchShops.Supported.Single(shop => shop.Id == offer.Shop.Id), offer, culture, T("PriceWatchNoOffer"))).ToArray();
                var reached = offers.Select(offer =>
                    PriceWatchTargetPrice.IsReached(entry, offer, DateTimeOffset.UtcNow, settings.ShopIds)).ToArray();
                return new(entry.Game.Title, T(PriceWatchSearchForm.EditionKey(entry.Game.Type)), prices,
                    rowCovers.GetValueOrDefault((settings.Live, entry.Game.Id)),
                    entry.TargetCents is int target ? string.Format(T("PriceWatchTargetCaption"), PriceWatchTargetPrice.Format(target)) : null,
                    reached, T("PriceWatchNoOffer"));
            }, out result, CanSearch && settings.Live ? hoveredMerchant : null, Focused && ShowFocusCues,
                scrollingTitle, titleClock.Elapsed.TotalSeconds);
    }

    private string GameCaption(PriceWatchGame game) =>
        $"{game.Title} · {T(PriceWatchSearchForm.EditionKey(game.Type))}";

    internal void Apply(PriceWatchWidgetSettings value, string lang, bool editing)
    {
        StopTitleScroll();
        bool modeChanged = value.Live != settings.Live;
        bool shopsChanged = !PriceWatchShops.Normalize(value.ShopIds).SequenceEqual(settings.ShopIds);
        if (shopsChanged || modeChanged)
        {
            selectionVersion++;
            active?.Cancel();
            foreach (var load in initialLoads.Values.ToArray()) load.Cancel();
            if (shopsChanged) selectionRefreshPending = true;
        }
        if (shopsChanged || modeChanged || editing && !preview)
        {
            active?.Cancel();
            searchDialog?.Close();
            detailDialog?.Close();
        }
        settings = value.Clone();
        language = lang;
        preview = editing;
        if (modeChanged)
        {
            selectedIndex = -1;
            entries = [];
            failure = null;
            viewport.SetOffset(0);
            ReadEntries();
        }
        Render();
        ScheduleSelectionRefresh();
    }

    private bool UseStore(Action work)
    {
        try
        {
            work();
            storageError = false;
            return true;
        }
        catch (Exception error) when (error is SqliteException or IOException or UnauthorizedAccessException or JsonException or FormatException)
        {
            storageError = true;
            return false;
        }
    }

    private void ReadEntries(Guid? preferred = null)
    {
        StopTitleScroll();
        Guid? id = preferred ?? SelectedEntry?.Game.Id;
        UseStore(() =>
        {
            store.Migrate(settings.TestSelection, settings.LiveSelection);
            entries = store.Read(settings.Live);
            selectedIndex = Array.FindIndex(entries, entry => entry.Game.Id == id);
            if (selectedIndex < 0 && entries.Length > 0) selectedIndex = 0;
        });
    }

    internal void ApplyTheme(bool value)
    {
        dark = value;
        Render();
    }

    internal void SetSuspended(bool value)
    {
        StopTitleScroll();
        suspended = value;
        if (value)
        {
            active?.Cancel();
            searchDialog?.Close();
            detailDialog?.Close();
        }
        Render();
        if (!value) ScheduleSelectionRefresh();
    }

    private async void ScheduleSelectionRefresh()
    {
        if (selectionRefreshing || !selectionRefreshPending || preview || suspended || active != null
            || initialLoads.Count != 0 || IsDisposed || !IsHandleCreated) return;
        if (lastRequestedShops.SequenceEqual(settings.ShopIds))
        {
            selectionRefreshPending = false;
            return;
        }
        selectionRefreshing = true;
        selectionRefreshPending = false;
        lastRequestedShops = settings.ShopIds.ToArray();
        int version = selectionVersion;
        bool live = settings.Live;
        try
        {
            var snapshot = entries.ToArray();
            foreach (var entry in snapshot)
            {
                bool proceed = false;
                await OnUiAsync(() => proceed = !preview && !suspended && version == selectionVersion);
                if (!proceed) break;
                await LoadNewEntryAsync(live, entry.Game.Id, includeInfo: false);
            }
        }
        finally
        {
            await OnUiAsync(() =>
            {
                selectionRefreshing = false;
                ScheduleSelectionRefresh();
            });
        }
    }

    internal int[] SelectedShops => settings.ShopIds.ToArray();

    internal void RestoreShopRefreshBaseline(int[] previous)
    {
        lastRequestedShops = previous.ToArray();
        selectionRefreshPending = !lastRequestedShops.SequenceEqual(settings.ShopIds);
        ScheduleSelectionRefresh();
    }

    private string T(string key) => Localization.Get(key, language);

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Render();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Render();
    }

    protected override void OnChangeUICues(UICuesEventArgs e)
    {
        base.OnChangeUICues(e);
        if (e.ChangeFocus) Render();
    }

    private void Render()
    {
        if (IsDisposed || Disposing || rendering || !IsHandleCreated) return;
        rendering = true;
        try
        {
            using var bitmap = RenderBitmap(Screen.FromControl(this).WorkingArea.Size, DeviceDpi, out layout);
            if (ClientSize != bitmap.Size)
            {
                ClientSize = bitmap.Size;
                Location = WidgetSettings.EnsureVisible(Location, bitmap.Size);
            }
            LayeredWidgetBitmap.Update(Handle, Location, bitmap);
            if (!titleFrame) UpdateVisibleCovers();
        }
        finally { rendering = false; }
    }

    private void UpdateVisibleCovers()
    {
        bool clearedImage = false;
        var visibleEntries = layout.VisibleRows().Select(index => entries[index]).ToArray();
        var visible = visibleEntries.Select(entry => (Live: settings.Live, Id: entry.Game.Id)).ToHashSet();
        foreach (var key in coverLoads.Keys.Where(key => !visible.Contains(key) || preview || suspended).ToArray())
            coverLoads[key].Cancel();
        foreach (var key in rowCovers.Keys.Where(key => !visible.Contains(key)).ToArray())
        {
            rowCovers[key].Dispose();
            rowCovers.Remove(key);
            rowCoverUrls.Remove(key);
        }
        if (preview || suspended) return;
        foreach (var entry in visibleEntries)
        {
            var key = (Live: settings.Live, Id: entry.Game.Id);
            if (key.Live && rowCovers.ContainsKey(key))
            {
                string? current = null;
                TryInfoStore(() => current = store.ReadInfo(true, key.Id)?.Data.Boxart?.AbsoluteUri);
                if (rowCoverUrls.GetValueOrDefault(key) != current)
                {
                    rowCovers[key].Dispose();
                    rowCovers.Remove(key);
                    rowCoverUrls.Remove(key);
                    clearedImage = true;
                }
            }
            if (!rowCovers.ContainsKey(key) && !coverLoads.ContainsKey(key)) LoadRowCover(entry, key);
        }
        if (clearedImage) BeginInvoke(new Action(Render));
    }

    private async void LoadRowCover(PriceWatchEntry entry, (bool Live, Guid Id) key)
    {
        Uri? url = null;
        PriceWatchCoverSource? local = null;
        if (key.Live)
        {
            if (!TryInfoStore(() => url = store.ReadInfo(true, key.Id)?.Data.Boxart)
                || !PriceWatchItadCovers.IsAllowed(url)) return;
        }
        else local = PriceWatchTestData.Details(entry.Game).Cover;
        if (local == null && url == null) return;
        string identity = url?.AbsoluteUri ?? "local";
        if (failedCovers.Contains((key.Live, key.Id, identity))) return;
        using var cancellation = new CancellationTokenSource();
        coverLoads.Add(key, cancellation);
        Bitmap? image = null;
        try
        {
            image = key.Live
                ? await (itadCovers ??= new()).LoadAsync(url, cancellation.Token)
                : await local!.LoadAsync(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        if (IsDisposed || Disposing || !IsHandleCreated)
        {
            image?.Dispose();
            return;
        }
        try
        {
            await this.InvokeAsync(() =>
            {
                coverLoads.Remove(key);
                if (cancellation.IsCancellationRequested || IsDisposed || Disposing) image?.Dispose();
                else if (image != null)
                {
                    rowCovers[key] = image;
                    rowCoverUrls[key] = identity;
                }
                else failedCovers.Add((key.Live, key.Id, identity));
                Render();
            });
        }
        catch (InvalidOperationException) when (IsDisposed || Disposing || !IsHandleCreated) { image?.Dispose(); }
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new SurfaceAccessibility(this);

    private sealed class SurfaceAccessibility(PriceWatchWidgetForm owner) : ControlAccessibleObject(owner)
    {
        public override string? Name { get => $"PriceWatch · {owner.ModeCaption}"; set => base.Name = value; }
        public override int GetChildCount() => 2;
        public override AccessibleObject? GetChild(int index) =>
            index is 0 or 1 ? new ActionAccessibility(owner, this, index + 1) : null;
    }

    private sealed class ActionAccessibility(PriceWatchWidgetForm owner, AccessibleObject parent, int action) : AccessibleObject
    {
        public override string? Name { get => owner.T(action == 1 ? "PriceWatchSearch" : "PriceWatchRefresh"); set { } }
        public override AccessibleObject Parent => parent;
        public override AccessibleRole Role => AccessibleRole.PushButton;
        public override AccessibleStates State => owner.ActionEnabled(action) ? AccessibleStates.None : AccessibleStates.Unavailable;
        public override Rectangle Bounds
        {
            get
            {
                var layout = owner.layout;
                var bounds = action == 1 ? layout.Search : layout.Refresh;
                return owner.RectangleToScreen(Rectangle.Round(new RectangleF(bounds.X * layout.Scale,
                    bounds.Y * layout.Scale, bounds.Width * layout.Scale, bounds.Height * layout.Scale)));
            }
        }

        public override void DoDefaultAction()
        {
            if (!owner.ActionEnabled(action) || !owner.IsHandleCreated) return;
            owner.BeginInvoke(new Action(async () =>
            {
                if (action == 1) owner.ShowSearch();
                else await owner.RefreshPricesAsync();
            }));
        }
    }

    private bool ActionEnabled(int action) => action == 1 ? CanSearch : action == 2 && CanRefresh;

    private void StopTitleScroll()
    {
        bool changed = scrollingTitle >= 0;
        titleTimer.Stop();
        titleClock.Reset();
        scrollingTitle = -1;
        if (changed) Render();
    }

    private void UpdateTitleHover(Point mouse)
    {
        int row = layout.Scale > 0 && CanSearch ? layout.RowAt(mouse) : -1;
        if (row >= 0)
        {
            float top = layout.RowTop(row);
            var bounds = PriceWatchWidgetRenderer.TitleBounds(layout.Content, top);
            if (!bounds.Contains(layout.ToLogical(mouse))) row = -1;
        }
        if (row == scrollingTitle) return;
        StopTitleScroll();
        if (row < 0 || !PriceWatchWidgetRenderer.TitleOverflows(entries[row].Game.Title,
            PriceWatchWidgetRenderer.TitleBounds(layout.Content, 0).Width)) return;
        scrollingTitle = row;
        titleClock.Restart();
        titleTimer.Start();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        StopTitleScroll();
        if (e.Button == MouseButtons.Left)
        {
            int action = layout.ActionAt(e.Location);
            if (action != 0)
            {
                if (ActionEnabled(action))
                {
                    pressed = action;
                    Capture = true;
                    Render();
                }
                return;
            }
        }
        if (e.Button == MouseButtons.Left && layout.Scale > 0)
        {
            PointF point = layout.ToLogical(e.Location);
            if (viewport.Track.Contains(point))
            {
                if (viewport.Thumb.Contains(point))
                {
                    thumbGrab = point.Y - viewport.Thumb.Top;
                    Capture = true;
                }
                else
                {
                    viewport.SetOffset(viewport.ScrollOffset + (point.Y < viewport.Thumb.Top ? -1 : 1) * viewport.ViewportHeight);
                    Render();
                }
                return;
            }
            int row = layout.RowAt(e.Location);
            if (row >= 0)
            {
                int shop = layout.MerchantAt(e.Location, row);
                var url = MerchantLink(row, shop);
                if (CanSearch && (shop < 0 || url != null))
                {
                    pressedRow = entries[row].Game.Id;
                    pressedMerchant = shop;
                    pressedUrl = url;
                    Capture = true;
                }
                return;
            }
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (thumbGrab is { } grab)
        {
            if (viewport.DragThumb(layout.ToLogical(e.Location).Y, grab)) Render();
            return;
        }
        UpdateTitleHover(e.Location);
        int action = layout.ActionAt(e.Location);
        int row = layout.RowAt(e.Location);
        int shop = layout.MerchantAt(e.Location, row);
        var url = MerchantLink(row, shop);
        (int, int)? merchant = url == null ? null : (row, shop);
        int next = ActionEnabled(action) ? action : CanSearch && row >= 0 && (shop < 0 || url != null) ? 3 : 0;
        if (hovered != next || hoveredMerchant != merchant)
        {
            hovered = next;
            hoveredMerchant = merchant;
            Cursor = hovered == 0 ? Cursors.Default : Cursors.Hand;
            Render();
        }
        if (layout.Scale > 0)
        {
            var point = new PointF(e.X / layout.Scale, e.Y / layout.Scale);
            string? text = action == 1 ? T("PriceWatchSearch")
                : action == 2 && SelectedEntry is { } target ? $"{T("PriceWatchRefresh")} · {target.Game.Title}"
                : row >= 0 ? GameCaption(entries[row].Game) : layout.Status.Contains(point) ? StatusCaption
                : layout.Mode.Contains(point) ? ModeCaption : null;
            if (tips.GetToolTip(this) != (text ?? "")) tips.SetToolTip(this, text);
        }
        if (pressed == 0 && pressedRow == null) base.OnMouseMove(e);
    }

    protected override async void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && pressed != 0)
        {
            int action = pressed;
            pressed = 0;
            Capture = false;
            Render();
            if (layout.ActionAt(e.Location) != action || !ActionEnabled(action)) return;
            if (action == 1) ShowSearch();
            else await RefreshPricesAsync();
            return;
        }
        if (e.Button == MouseButtons.Left && thumbGrab != null)
        {
            thumbGrab = null;
            Capture = false;
            return;
        }
        if (e.Button == MouseButtons.Left && pressedRow is { } id)
        {
            int buttonShop = pressedMerchant;
            var buttonUrl = pressedUrl;
            ClearPressedRow();
            Capture = false;
            int row = layout.RowAt(e.Location);
            int shop = layout.MerchantAt(e.Location, row);
            if (row >= 0 && entries[row].Game.Id == id && CanSearch && shop == buttonShop)
            {
                if (shop < 0) ShowDetails(row);
                else if (buttonUrl != null && MerchantLink(row, shop)?.OriginalString == buttonUrl.OriginalString)
                    openLink(buttonUrl);
            }
            return;
        }
        base.OnMouseUp(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        StopTitleScroll();
        hovered = 0;
        hoveredMerchant = null;
        Cursor = Cursors.Default;
        Render();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        if (!Capture && (pressed != 0 || pressedRow != null || thumbGrab != null))
        {
            pressed = 0;
            ClearPressedRow();
            thumbGrab = null;
            Render();
        }
        base.OnMouseCaptureChanged(e);
    }

    private void ClearPressedRow()
    {
        pressedRow = null;
        pressedMerchant = -1;
        pressedUrl = null;
    }

    private async void ShowSearch()
    {
        if (!CanSearch) return;
        PriceWatchGame? added = null;
        bool live = settings.Live;
        dialogOpen = true;
        Render();
        try
        {
            using var dialog = new PriceWatchSearchForm(source(settings.Live), settings.Live, language, settings.Style);
            searchDialog = dialog;
            bool inserted = false;
            if (dialog.ShowDialog(this) == DialogResult.OK && dialog.SelectedGame is { } choice && !IsDisposed
                && UseStore(() => inserted = store.Add(live, choice)))
            {
                if (inserted) added = choice;
                ReadEntries(choice.Id);
                Render();
                if (selectedIndex >= 0) viewport.SetOffset(layout.RowOffset(selectedIndex));
            }
        }
        catch (PriceWatchLimitException)
        {
            MessageBox.Show(this, string.Format(T("PriceWatchLimitReached"), PriceWatchStore.MaximumGames),
                T("PriceWatchTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally
        {
            searchDialog = null;
            dialogOpen = false;
            Render();
        }
        if (added != null && !IsDisposed) await LoadNewEntryAsync(live, added.Id);
    }

    private async Task OnUiAsync(Action action)
    {
        if (IsDisposed || Disposing || !IsHandleCreated || lifetimeToken.IsCancellationRequested) return;
        if (!InvokeRequired)
        {
            action();
            return;
        }
        try
        {
            await this.InvokeAsync(() =>
            {
                if (!IsDisposed && !Disposing) action();
            }, lifetimeToken);
        }
        catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested) { }
        catch (InvalidOperationException) when (IsDisposed || Disposing || !IsHandleCreated) { }
    }

    internal async Task LoadNewEntryAsync(bool live, Guid id, bool includeInfo = true)
    {
        Task? load = null;
        await OnUiAsync(() => load = LoadEntryCoreAsync(live, id, includeInfo));
        if (load != null) await load;
    }

    private async Task LoadEntryCoreAsync(bool live, Guid id, bool includeInfo)
    {
        var key = (live, id);
        if (initialLoads.ContainsKey(key) || IsDisposed) return;
        using var operation = new CancellationTokenSource();
        initialLoads.Add(key, operation);
        int version = selectionVersion;
        int[] shops = settings.ShopIds.ToArray();
        autoInfoAttempted.Add(key);
        failure = null;
        Render();
        bool acquired = false;
        PriceWatchException? requestFailure = null;
        try
        {
            await initialLoadQueue.WaitAsync(operation.Token);
            acquired = true;
            var provider = source(live);
            if (includeInfo) try
            {
                var info = await provider.InfoAsync(id, operation.Token);
                await OnUiAsync(() =>
                {
                    if (operation.IsCancellationRequested) return;
                    UseStore(() => store.SaveInfo(live, id, info));
                    if (settings.Live == live && SelectedEntry?.Game.Id == id
                        && detailDialog is { IsDisposed: false } dialog)
                        UseStore(() => dialog.PresentInfo(store.ReadInfo(live, id)?.Data));
                    Render();
                });
            }
            catch (PriceWatchException error) when (!operation.IsCancellationRequested) { requestFailure = error; }
            operation.Token.ThrowIfCancellationRequested();
            try
            {
                var prices = await provider.PricesAsync(id, shops, operation.Token);
                await OnUiAsync(() =>
                {
                    if (operation.IsCancellationRequested || version != selectionVersion) return;
                    UseStore(() => store.SaveSnapshot(live, id, prices));
                    if (settings.Live == live)
                    {
                        ReadEntries();
                        if (SelectedEntry?.Game.Id == id && detailDialog is { IsDisposed: false } dialog)
                            dialog.Present(SelectedEntry, "");
                    }
                });
            }
            catch (PriceWatchException error) when (!operation.IsCancellationRequested) { requestFailure = error; }
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (PriceWatchException error) { requestFailure = error; }
        finally
        {
            if (acquired) initialLoadQueue.Release();
            await OnUiAsync(() =>
            {
                if (!operation.IsCancellationRequested && version == selectionVersion && requestFailure != null)
                    failure = requestFailure;
                initialLoads.Remove(key);
                Render();
                ScheduleSelectionRefresh();
            });
        }
    }

    private void CancelInitialLoad(bool live, Guid id)
    {
        if (initialLoads.TryGetValue((live, id), out var load)) load.Cancel();
    }

    private void ShowDetails(int index)
    {
        StopTitleScroll();
        var entry = entries[index];
        bool live = settings.Live;
        PriceWatchReply<PriceWatchGameInfo>? cachedInfo = null;
        bool cacheRead = TryInfoStore(() => cachedInfo = store.ReadInfo(live, entry.Game.Id));
        bool fetchOnOpen = cacheRead && cachedInfo == null && !initialLoads.ContainsKey((live, entry.Game.Id))
            && autoInfoAttempted.Add((live, entry.Game.Id));
        selectedIndex = index;
        dialogOpen = true;
        failure = null;
        Render();
        try
        {
            var presentation = live ? default : PriceWatchTestData.Details(entry.Game);
            using var dialog = new PriceWatchDetailForm(entry, ModeCaption, language, settings.Style,
                () => RefreshPricesAsync(fromDetails: true), () =>
                {
                    if (!CanOperate || !UseStore(() => store.Remove(settings.Live, entry.Game.Id))) return false;
                    CancelInitialLoad(live, entry.Game.Id);
                    autoInfoAttempted.Remove((live, entry.Game.Id));
                    ReadEntries();
                    return true;
                }, shopLinksEnabled: live, openLink: openLink, info: cachedInfo?.Data,
                refreshInfo: () => RefreshInfoAsync(live, entry.Game.Id), fetchInfoOnOpen: fetchOnOpen,
                description: presentation.Description, coverSource: presentation.Cover, sizeSettings: new AppSettingsStore(),
                sizeLocked: settings.DetailSizeLocked, itadCovers: live ? itadCovers ??= new() : null,
                saveTarget: value =>
                {
                    if (!UseStore(() => store.SaveTarget(live, entry.Game.Id, value))) return false;
                    ReadEntries(entry.Game.Id);
                    Render();
                    return true;
                }, shops: settings.ShopIds);
            detailDialog = dialog;
            dialog.ShowDialog(this);
        }
        finally
        {
            detailDialog = null;
            dialogOpen = false;
            active?.Cancel();
            Render();
        }
    }

    private static bool TryInfoStore(Action work)
    {
        try
        {
            work();
            return true;
        }
        catch (Exception error) when (error is SqliteException or IOException or UnauthorizedAccessException or JsonException or FormatException)
        {
            return false;
        }
    }

    private async Task RefreshInfoAsync(bool live, Guid id)
    {
        if (!CanOperate || initialLoads.ContainsKey((live, id)) || detailDialog is not { } dialog) return;
        using var operation = new CancellationTokenSource();
        active = operation;
        bool acquired = false;
        try
        {
            await initialLoadQueue.WaitAsync(operation.Token);
            acquired = true;
            var info = await source(live).InfoAsync(id, operation.Token);
            await OnUiAsync(() =>
            {
                if (operation.IsCancellationRequested || dialog.IsDisposed) return;
                if (!TryInfoStore(() =>
                {
                    store.SaveInfo(live, id, info);
                    dialog.PresentInfo(store.ReadInfo(live, id)?.Data);
                })) dialog.PresentInfo(null, T("PriceWatchStorageError"));
            });
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (PriceWatchException error)
        {
            await OnUiAsync(() =>
            {
                if (!operation.IsCancellationRequested && !dialog.IsDisposed)
                    dialog.PresentInfo(null, PriceWatchSearchForm.ErrorText(error, language));
            });
        }
        finally
        {
            if (acquired) initialLoadQueue.Release();
            await OnUiAsync(() =>
            {
                active = null;
                Render();
                ScheduleSelectionRefresh();
            });
        }
    }

    private async Task RefreshPricesAsync(bool fromDetails = false)
    {
        if (!CanOperate || (dialogOpen != fromDetails) || SelectedEntry is not { } entry || storageError
            || initialLoads.ContainsKey((settings.Live, entry.Game.Id))) return;
        using var operation = new CancellationTokenSource();
        active = operation;
        failure = null;
        bool live = settings.Live;
        int version = selectionVersion;
        int[] shops = settings.ShopIds.ToArray();
        bool acquired = false;
        Render();
        try
        {
            await initialLoadQueue.WaitAsync(operation.Token);
            acquired = true;
            var reply = await source(live).PricesAsync(entry.Game.Id, shops, operation.Token);
            await OnUiAsync(() =>
            {
                if (!operation.IsCancellationRequested && version == selectionVersion
                    && UseStore(() => store.SaveSnapshot(live, entry.Game.Id, reply)))
                    ReadEntries();
            });
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (PriceWatchException error)
        {
            await OnUiAsync(() =>
            {
                if (!operation.IsCancellationRequested) failure = error;
            });
        }
        finally
        {
            if (acquired) initialLoadQueue.Release();
            await OnUiAsync(() =>
            {
                active = null;
                if (detailDialog is { IsDisposed: false } dialog && SelectedEntry is { } updated)
                    dialog.Present(updated, StatusCaption);
                Render();
                ScheduleSelectionRefresh();
            });
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !lifetimeToken.IsCancellationRequested)
        {
            lifetimeCancellation.Cancel();
            lifetimeCancellation.Dispose();
            active?.Cancel();
            foreach (var load in initialLoads.Values.ToArray()) load.Cancel();
            searchDialog?.Close();
            detailDialog?.Close();
            drag.Dispose();
            titleTimer.Dispose();
            tips.Dispose();
            itadCovers?.Dispose();
            foreach (var load in coverLoads.Values) load.Cancel();
            foreach (var image in rowCovers.Values) image.Dispose();
            rowCovers.Clear();
        }
        base.Dispose(disposing);
    }
}
