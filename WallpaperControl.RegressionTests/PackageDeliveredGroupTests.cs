extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using System.Globalization;
using System.Reflection;
using System.Resources;

internal static class PackageDeliveredGroupTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, Flags)!.GetValue(obj)!;
    private static Guid[] Rows(App.PackageTrackingWidgetForm widget) => Field<List<(Guid Id, RectangleF Bounds)>>(widget, "rows").Select(r => r.Id).ToArray();
    private static Point GroupPoint(App.PackageTrackingWidgetForm widget, bool arrow = false)
    {
        var bounds = Field<RectangleF>(widget, "deliveredBounds");
        var view = Field<App.CalendarViewport>(widget, "viewport");
        return new((int)((bounds.Left + (arrow ? 5 : bounds.Width / 2)) * view.Scale), (int)((bounds.Top + bounds.Height / 2) * view.Scale));
    }
    private static void Click(App.PackageTrackingWidgetForm widget, Point point)
    {
        var args = new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0);
        typeof(App.PackageTrackingWidgetForm).GetMethod("OnMouseDown", Flags)!.Invoke(widget, [args]);
        typeof(App.PackageTrackingWidgetForm).GetMethod("OnMouseUp", Flags)!.Invoke(widget, [args]);
    }
    private static App.TrackedShipment Shipment(string number, bool delivered) => new()
    {
        Provider = "ship24", ProviderTrackerId = number, TrackingNumber = number, DisplayName = number,
        StatusMilestone = delivered ? "delivered" : "in_transit", LastSuccessfulRefresh = DateTimeOffset.UtcNow
    };
    private sealed class Provider : App.ITrackingProvider
    {
        internal int Calls;
        internal string? Deliver;
        public string ProviderId => "ship24";
        public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<App.TrackedShipment> CreateTrackerAsync(string number, string? carrierCode = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<App.TrackedShipment> GetTrackingAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<App.TrackedShipment> RefreshTrackingAsync(App.TrackedShipment item, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(item with { StatusMilestone = item.TrackingNumber == Deliver ? "delivered" : item.StatusMilestone }); }
    }
    internal static void Run(Action<bool, string> check, string? previews)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { Ui(check, previews); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error != null) throw new Exception("Delivered group tests failed", error);
    }
    private static void Ui(Action<bool, string> check, string? previews)
    {
        string basePath = Path.GetFullPath(AppContext.BaseDirectory);
        string root = Path.Combine(basePath, "test-data", "package-delivered-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        if (previews != null) Directory.CreateDirectory(previews);
        try
        {
            var d1 = Shipment("delivered-one", true); var a1 = Shipment("active-one", false);
            var d2 = Shipment("delivered-two", true); var a2 = Shipment("active-two", false); var d3 = Shipment("delivered-three", true);
            string path = Path.Combine(root, "mixed.json"); var store = new App.TrackingStore(path);
            foreach (var item in new[] { d1, a1, d2, a2, d3 }) if (!store.Save(item)) throw new Exception("Fixture save failed");
            byte[] original = File.ReadAllBytes(path);
            var provider = new Provider(); using var service = new App.PackageTrackingService(store, provider);
            var settings = new App.WidgetSettings { PackageMaximumHeight = 700, PackageLocked = true, ClockLanguageCode = "de" };
            int details = 0;
            using var widget = new App.PackageTrackingWidgetForm(service, settings, _ => { }, _ => details++);
            using var collapsed = widget.RenderBitmap();
            check(Rows(widget).SequenceEqual(new[] { a1.Id, a2.Id }), "Two active rows remain ordered; three delivered rows hidden");
            check(!Field<bool>(widget, "deliveredExpanded") && widget.HitTestDeliveredGroup(GroupPoint(widget)), "Delivered defaults collapsed; full row is clickable");
            check(Field<RectangleF>(widget, "deliveredBounds").Top >= Field<List<(Guid Id, RectangleF Bounds)>>(widget, "rows").Last().Bounds.Bottom, "Section follows active rows");
            Click(widget, GroupPoint(widget)); using var expanded = widget.RenderBitmap();
            check(Rows(widget).SequenceEqual(new[] { a1.Id, a2.Id, d1.Id, d2.Id, d3.Id }), "Expand shows all delivered rows in unchanged group order");
            var last = Field<List<(Guid Id, RectangleF Bounds)>>(widget, "rows").Last().Bounds;
            Click(widget, new((int)last.Left + 10, (int)last.Top + 10));
            check(details == 1, "Expanded shipment still opens its details");
            Click(widget, GroupPoint(widget, true)); using var closed = widget.RenderBitmap();
            check(Rows(widget).Length == 2 && closed.Height == collapsed.Height && expanded.Height > collapsed.Height, "Arrow collapses and frees excess height");
            check(provider.Calls == 0 && original.SequenceEqual(File.ReadAllBytes(path)) && service.Shipments.Count == 5, "Toggling never tracks, persists or deletes shipments");
            foreach (var style in Enum.GetValues<App.SystemWidgetStyle>())
            {
                settings.PackageStyle = style; settings.PackageMaximumHeight = 300; widget.Apply(settings);
                using var compact = widget.RenderBitmap();
                Click(widget, GroupPoint(widget)); using var full = widget.RenderBitmap();
                var view = Field<App.CalendarViewport>(widget, "viewport");
                check(full.Height == 300 && view.CanScroll, style + ": expansion respects maximum and enables scroll");
                check(widget.ScrollWheel(new(30, 100), -120, 3), style + ": mouse wheel scrolls existing viewport");
                view.SetOffset(float.MaxValue); using var bottom = widget.RenderBitmap();
                check(Math.Abs(widget.UpdatedBounds.Bottom - (view.LogicalHeight - 3)) < 0.01f, style + ": footer reaches existing bottom position");
                check(widget.HitTest(new(30, 20)) == null && !widget.HitTestDeliveredGroup(new(30, 20)), style + ": fixed header cannot hit scrolled rows");
                if (previews != null) bottom.Save(Path.Combine(previews, style + "-expanded.png"));
                view.SetOffset(120); using (var middle = widget.RenderBitmap()) Click(widget, GroupPoint(widget));
                using var shortImage = widget.RenderBitmap();
                check(shortImage.Height == compact.Height && !view.CanScroll && view.ScrollOffset == 0, style + ": collapse shrinks and clamps scroll offset");
                if (previews != null) shortImage.Save(Path.Combine(previews, style + "-collapsed.png"));
            }
            var resources = new ResourceManager("WallpaperControl.Strings", typeof(App.WidgetSettings).Assembly);
            foreach (var pair in new[] { ("de", "Zugestellt (3)"), ("en", "Delivered (3)"), ("fr", "Livré (3)"), ("es", "Entregado (3)"), ("ja", "配達済み (3)") })
            {
                var culture = CultureInfo.GetCultureInfo(pair.Item1);
                var set = resources.GetResourceSet(pair.Item1 == "de" ? CultureInfo.InvariantCulture : culture, true, false)!;
                check(string.Format(culture, set.GetString("PackageDeliveredGroup")!, 3) == pair.Item2, "Localized section/count " + pair.Item1);
            }
            settings.PackageMaximumHeight = 700; widget.Apply(settings);
            provider.Deliver = a1.TrackingNumber; service.RefreshAsync().GetAwaiter().GetResult();
            using (var changed = widget.RenderBitmap())
                check(Rows(widget).SequenceEqual(new[] { a2.Id }) && service.Shipments.Count(s => s.StatusMilestone == "delivered") == 4, "Refresh moves delivered item out of active group");
            Click(widget, GroupPoint(widget)); using (var changed = widget.RenderBitmap())
                // Existing store updates append refreshed entries; preserve that order too.
                check(Rows(widget).SequenceEqual(new[] { a2.Id, d1.Id, d2.Id, d3.Id, a1.Id }), "Newly delivered group retains existing refreshed store order");
            using (var recreated = new App.PackageTrackingWidgetForm(service, settings, _ => { }, _ => { }))
            using (var image = recreated.RenderBitmap())
                check(!Field<bool>(recreated, "deliveredExpanded") && Rows(recreated).SequenceEqual(new[] { a2.Id }), "Widget recreation resets expanded state");
            foreach (bool delivered in new[] { false, true })
            {
                var edgeStore = new App.TrackingStore(Path.Combine(root, "edge-" + delivered + ".json"));
                edgeStore.Save(Shipment("edge-one", delivered)); edgeStore.Save(Shipment("edge-two", delivered));
                using var edgeService = new App.PackageTrackingService(edgeStore, new Provider());
                using var edge = new App.PackageTrackingWidgetForm(edgeService, settings, _ => { }, _ => { });
                using var image = edge.RenderBitmap();
                check(delivered ? !Field<RectangleF>(edge, "deliveredBounds").IsEmpty && Rows(edge).Length == 0
                    : Field<RectangleF>(edge, "deliveredBounds").IsEmpty && Rows(edge).Length == 2,
                    delivered ? "Delivered-only list has compact section and no active-empty placeholder" : "No delivered shipments means no section");
                if (delivered) { Click(edge, GroupPoint(edge)); using var open = edge.RenderBitmap(); check(Rows(edge).Length == 2, "Delivered-only section expands"); }
            }
            var amazonStore = new App.TrackingStore(Path.Combine(root, "amazon.json"));
            var amazon = Shipment("DE5897460159", false) with { Provider = "local", TrackingMode = "manual", StatusSource = "local", CarrierCode = App.AmazonLogistics.Carrier };
            amazonStore.Save(amazon);
            using var amazonService = new App.PackageTrackingService(amazonStore, new Provider());
            using var amazonWidget = new App.PackageTrackingWidgetForm(amazonService, settings, _ => { }, _ => { });
            amazonService.SetManualDelivered(amazon.Id, true); using (var image = amazonWidget.RenderBitmap())
                check(Rows(amazonWidget).Length == 0 && !Field<RectangleF>(amazonWidget, "deliveredBounds").IsEmpty, "Manual Amazon delivery enters delivered group");
            amazonService.SetManualDelivered(amazon.Id, false); using (var image = amazonWidget.RenderBitmap())
                check(Rows(amazonWidget).SequenceEqual(new[] { amazon.Id }) && Field<RectangleF>(amazonWidget, "deliveredBounds").IsEmpty, "Undo Amazon delivery returns item to active group");
        }
        finally
        {
            if (!Path.GetFullPath(root).StartsWith(basePath, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected test path");
            Directory.Delete(root, true);
        }
    }
}
