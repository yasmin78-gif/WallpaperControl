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
    private bool storageError;
    private PriceWatchException? failure;

    internal PriceWatchWidgetForm(Func<bool, IPriceWatchSource> source, PriceWatchWidgetSettings value,
        string language, Action<Point> moved, PriceWatchStore store, Action<Uri>? openLink = null)
    {
        this.source = source;
        this.store = store;
        this.openLink = openLink ?? PriceWatchShopLinks.Open;
        settings = value.Clone();
        this.language = language;
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
            if (viewport.Wheel((short)((message.WParam.ToInt64() >> 16) & 0xffff), SystemInformation.MouseWheelScrollLines))
                Render();
            message.Result = 0;
            return;
        }
        base.WndProc(ref message);
    }

    private bool CanOperate => !preview && !suspended && active == null;
    private bool CanSearch => CanOperate && !dialogOpen && !storageError;
    private bool CanRefresh => CanSearch && SelectedEntry != null;
    private PriceWatchEntry? SelectedEntry => selectedIndex >= 0 ? entries[selectedIndex] : null;
    internal string ModeCaption => T(settings.Live ? "PriceWatchLive" : "PriceWatchTest");
    private string StatusCaption => preview ? T("PriceWatchPreview")
        : active is { IsCancellationRequested: false } ? T("PriceWatchLoading")
        : storageError ? T("PriceWatchStorageError")
        : failure != null ? PriceWatchSearchForm.ErrorText(failure, language)
        : "";

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
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        FitToScreen(Location);
    }

    private static PriceWatchOffer? CheapestOffer(PriceWatchEntry entry, int shop) =>
        PriceWatchOfferDisplay.ForShop(entry, PriceWatchOfferDisplay.Shops[shop].Id).MinBy(offer => offer.Price.AmountInt);

    private Uri? MerchantLink(int row, int shop) => CanSearch && settings.Live && row >= 0 && shop >= 0
        ? PriceWatchShopLinks.Validate(CheapestOffer(entries[row], shop)?.Url) : null;

    internal Bitmap RenderBitmap(Size area, int dpi, out PriceWatchWidgetLayout result)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        return PriceWatchWidgetRenderer.Draw(area, dpi, settings.Style, dark, language, ModeCaption,
            T("PriceWatchWatchlistEmpty"), StatusCaption, T("PriceWatchSearch"), T("PriceWatchRefresh"),
            CanSearch, CanRefresh, hovered, pressed, viewport, entries.Length, index =>
            {
                var entry = entries[index];
                var prices = PriceWatchOfferDisplay.Shops.Select((shop, shopIndex) =>
                    PriceWatchOfferDisplay.Columns(shop, CheapestOffer(entry, shopIndex),
                        culture, T("PriceWatchNoOffer"))).ToArray();
                return new(entry.Game.Title, T(PriceWatchSearchForm.EditionKey(entry.Game.Type)), prices);
            }, out result, CanSearch && settings.Live ? hoveredMerchant : null, Focused && ShowFocusCues);
    }

    private string GameCaption(PriceWatchGame game) =>
        $"{game.Title} · {T(PriceWatchSearchForm.EditionKey(game.Type))}";

    internal void Apply(PriceWatchWidgetSettings value, string lang, bool editing)
    {
        bool modeChanged = value.Live != settings.Live;
        if (modeChanged || editing && !preview)
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
        suspended = value;
        if (value)
        {
            active?.Cancel();
            searchDialog?.Close();
            detailDialog?.Close();
        }
        Render();
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
        }
        finally { rendering = false; }
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

    protected override void OnMouseDown(MouseEventArgs e)
    {
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

    private void ShowSearch()
    {
        if (!CanSearch) return;
        dialogOpen = true;
        Render();
        try
        {
            using var dialog = new PriceWatchSearchForm(source(settings.Live), settings.Live, language, settings.Style);
            searchDialog = dialog;
            if (dialog.ShowDialog(this) == DialogResult.OK && dialog.SelectedGame is { } choice && !IsDisposed
                && UseStore(() => store.Add(settings.Live, choice)))
            {
                ReadEntries(choice.Id);
                Render();
                if (selectedIndex >= 0) viewport.SetOffset(selectedIndex * (float)PriceWatchWidgetRenderer.RowHeight);
            }
        }
        finally
        {
            searchDialog = null;
            dialogOpen = false;
            Render();
        }
    }

    private void ShowDetails(int index)
    {
        var entry = entries[index];
        selectedIndex = index;
        dialogOpen = true;
        failure = null;
        Render();
        try
        {
            using var dialog = new PriceWatchDetailForm(entry, ModeCaption, language, settings.Style,
                () => RefreshPricesAsync(fromDetails: true), () =>
                {
                    if (!CanOperate || !UseStore(() => store.Remove(settings.Live, entry.Game.Id))) return false;
                    ReadEntries();
                    return true;
                }, shopLinksEnabled: settings.Live, openLink: openLink);
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

    private async Task RefreshPricesAsync(bool fromDetails = false)
    {
        if (!CanOperate || (dialogOpen != fromDetails) || SelectedEntry is not { } entry || storageError) return;
        using var operation = new CancellationTokenSource();
        active = operation;
        failure = null;
        bool live = settings.Live;
        Render();
        try
        {
            var reply = await source(live).PricesAsync(entry.Game.Id, operation.Token);
            if (!operation.IsCancellationRequested && !IsDisposed
                && UseStore(() => store.SaveSnapshot(live, entry.Game.Id, reply)))
                ReadEntries();
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (PriceWatchException error) when (!operation.IsCancellationRequested && !IsDisposed) { failure = error; }
        catch (PriceWatchException) { }
        finally
        {
            active = null;
            if (detailDialog is { IsDisposed: false } dialog && SelectedEntry is { } updated)
                dialog.Present(updated, StatusCaption);
            Render();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            active?.Cancel();
            searchDialog?.Close();
            detailDialog?.Close();
            drag.Dispose();
            tips.Dispose();
        }
        base.Dispose(disposing);
    }
}
