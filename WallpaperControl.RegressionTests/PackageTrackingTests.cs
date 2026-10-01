extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using Microsoft.Win32;
using System.Drawing;
using System.Reflection;
using System.Resources;
using System.Globalization;

internal static class PackageTrackingTests
{
    private sealed class Provider : App.ITrackingProvider
    {
        public string ProviderId => "ship24";
        internal int Created, Fetched, Tested;
        internal readonly List<string> Ids = new();
        internal Func<string, Task<App.TrackedShipment>>? Get;
        internal bool FailCreate, FailTest;
        public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
        { Tested++; if (FailTest) throw new App.TrackingProviderException(App.TrackingProviderFailure.Unauthorized); return Task.FromResult(true); }
        public Task<App.TrackedShipment> CreateTrackerAsync(string trackingNumber, string? carrierCode = null, CancellationToken cancellationToken = default)
        { Created++; if (FailCreate) throw new App.TrackingProviderException(App.TrackingProviderFailure.NetworkError); return Task.FromResult(Snapshot(trackingNumber)); }
        public Task<App.TrackedShipment> GetTrackingAsync(string providerTrackerId, CancellationToken cancellationToken = default)
        { Fetched++; Ids.Add(providerTrackerId); return Get?.Invoke(providerTrackerId) ?? Task.FromResult(Snapshot(providerTrackerId)); }
    }
    private static App.TrackedShipment Snapshot(string number) => new()
    {
        Provider = "ship24", ProviderTrackerId = number, TrackingNumber = number, StatusMilestone = "in_transit",
        LastSuccessfulRefresh = DateTimeOffset.UtcNow, LastRelevantEvent = new() { EventId = "event", OccurredAt = DateTimeOffset.UtcNow, Location = "Paris" }
    };
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static IEnumerable<Control> Controls(Control root) => root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Controls(c)));
    internal static void Run(Action<bool, string> check)
    {
        string root = Path.Combine(AppContext.BaseDirectory, "test-data", "packages-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        Task.Run(() => Service(check, root)).GetAwaiter().GetResult();
        using Task ui = new(() => Ui(check, root));
        Thread thread = new(() => ui.RunSynchronously(TaskScheduler.Default)); thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); ui.GetAwaiter().GetResult();
    }
    private static async Task Service(Action<bool, string> check, string root)
    {
        var oldUpdate = new DateTimeOffset(2026, 10, 1, 0, 20, 0, TimeSpan.FromHours(2));
        var lastUpdate = oldUpdate.AddHours(8).AddMinutes(31);
        var delivered = Snapshot("delivered") with { StatusMilestone = "delivered", LastSuccessfulRefresh = oldUpdate };
        var active = Snapshot("active") with { LastSuccessfulRefresh = lastUpdate };
        var unrefreshed = Snapshot("pending") with { LastSuccessfulRefresh = null };
        var manual = Snapshot("manual") with { TrackingMode = "manual", LastSuccessfulRefresh = lastUpdate.AddHours(1) };
        check(App.PackagePresentation.LastUpdate(new[] { delivered, active, unrefreshed, manual }) == lastUpdate,
            "Packages footer shows latest successful update despite older delivered and never-refreshed shipments");
        check(App.PackagePresentation.LastUpdate(new[] { delivered }) == oldUpdate,
            "Packages footer retains last actual update when all shipments are delivered");
        check(App.PackagePresentation.LastUpdate(new[] { unrefreshed, manual }) == null && App.PackagePresentation.LastUpdate(Array.Empty<App.TrackedShipment>()) == null,
            "Packages footer does not invent updates for manual, unrefreshed or empty lists");
        var rawEvents = new[] { "2026-09-28T01:55:00", "2026-09-29T13:40:00", "2026-09-26T10:11:00", "2026-09-26T16:29:00", "2026-09-29T10:36:00" }
            .Select((time, index) => new App.TrackingEvent { EventId = index.ToString(), RawOccurredAt = time }).ToArray();
        check(App.PackagePresentation.NewestEventsFirst(rawEvents).Select(e => e.EventId).SequenceEqual(new[] { "1", "4", "0", "3", "2" }),
            "Packages history sorts timezone-less courier timestamps newest first");
        check(rawEvents.All(e => e.OccurredAt == null), "Packages display sorting never invents stored UTC instants");
        foreach (string language in new[] { "de", "en", "fr", "es", "ja" })
        {
            var culture = CultureInfo.GetCultureInfo(language);
            var entry = new App.TrackingEvent { RawOccurredAt = "2026-09-29T13:40:00" };
            check(App.PackagePresentation.EventDate(entry, language) == new DateTime(2026, 9, 29, 13, 40, 0).ToString("g", culture),
                "Packages localized source time without timezone conversion " + language);
            check(App.PackagePresentation.EventDate(entry with { RawOccurredAt = "2026-09-29" }, language) == new DateTime(2026, 9, 29).ToString("d", culture),
                "Packages date-only events do not invent a time " + language);
        }
        check(App.PackagePresentation.EventDate(rawEvents[1], "de") == "29.09.2026 13:40", "Packages German event time uses readable date and minutes");
        var mixedEvents = new[] {
            new App.TrackingEvent { EventId = "invalid", RawOccurredAt = "invalid" },
            new App.TrackingEvent { EventId = "date", RawOccurredAt = "2026-09-28" },
            new App.TrackingEvent { EventId = "offset", OccurredAt = DateTimeOffset.Parse("2026-09-29T12:00:00-04:00") },
            new App.TrackingEvent { EventId = "utc", OccurredAt = DateTimeOffset.Parse("2026-09-29T15:00:00Z") },
            new App.TrackingEvent { EventId = "missing" } };
        check(App.PackagePresentation.NewestEventsFirst(mixedEvents).Select(e => e.EventId).SequenceEqual(new[] { "offset", "utc", "date", "invalid", "missing" }),
            "Packages history compares known offsets correctly and keeps undated events last");
        string path = Path.Combine(root, "tracking.json"); var store = new App.TrackingStore(path); var provider = new Provider();
        using var service = new App.PackageTrackingService(store, provider);
        check((await service.AddAsync("  Abcde123  ", "  Headphones  ")).Outcome == App.PackageOperation.Success && provider.Created == 1, "Packages add creates exactly one tracker");
        var initial = service.Shipments.Single();
        check(initial.TrackingNumber == "Abcde123" && initial.DisplayName == "Headphones", "Packages trim number and display name");
        check((await service.AddAsync("abcde123", null)).Outcome == App.PackageOperation.Duplicate && provider.Created == 1, "Packages duplicates ignore surrounding spaces and case");
        check((await service.AddAsync("!", null)).Outcome == App.PackageOperation.InvalidNumber && provider.Created == 1, "Packages invalid number never reaches provider");
        check(service.Rename(initial.Id, " Renamed ").Outcome == App.PackageOperation.Success && service.Shipments.Single().ProviderTrackerId == initial.ProviderTrackerId
            && service.Shipments.Single().DisplayName == "Renamed" && provider.Created == 1, "Packages rename preserves remote tracker and local identity");
        provider.FailCreate = true;
        check((await service.AddAsync("SECOND", null)).Outcome == App.PackageOperation.ProviderError && service.Shipments.Count == 1, "Packages failed create leaves no zombie");
        provider.FailCreate = false; await service.AddAsync("SECOND", null);
        var second = service.Shipments.Single(s => s.TrackingNumber == "SECOND");
        store.Save(initial with { DisplayName = "Renamed", NotificationState = new() { NotifiedEventIds = new() { "keep" } } });
        store.Save(second); // TrackingStore replaces an entry at the end; make request ordering explicit.
        var pending = new TaskCompletionSource<App.TrackedShipment>(); provider.Get = _ => pending.Task;
        var running = service.RefreshAsync();
        check(service.Busy && (await service.RefreshAsync()).Outcome == App.PackageOperation.Busy && provider.Fetched == 1, "Packages refresh storm is suppressed");
        check(service.Delete(initial.Id).Outcome == App.PackageOperation.Busy && service.Rename(initial.Id, "other").Outcome == App.PackageOperation.Busy
            && (await service.AddAsync("THIRD", null)).Outcome == App.PackageOperation.Busy, "Packages mutations cannot race refresh");
        provider.Get = _ => throw new App.TrackingProviderException(App.TrackingProviderFailure.NetworkError);
        pending.SetResult(Snapshot(initial.TrackingNumber) with { StatusMilestone = "delivered", DeliveredAt = DateTimeOffset.UtcNow });
        check((await running).Outcome == App.PackageOperation.ProviderError && !service.Busy, "Packages partial refresh completes with error state");
        var refreshed = service.Shipments.Single(s => s.Id == initial.Id);
        check(refreshed.StatusMilestone == "delivered" && refreshed.DeliveredAt != null && refreshed.DisplayName == "Renamed"
            && refreshed.NotificationState.NotifiedEventIds.Contains("keep"), "Packages refresh preserves name identity and notification metadata");
        check(service.Shipments.Single(s => s.Id == second.Id).LastSuccessfulRefresh == second.LastSuccessfulRefresh
            && service.RefreshFailures.ContainsKey(second.Id), "Packages offline retains last known shipment and timestamp");
        check(provider.Created == 3 && provider.Ids.SequenceEqual(new[] { initial.ProviderTrackerId!, second.ProviderTrackerId! }), "Packages refresh uses only existing tracker IDs and no POST");
        check(new App.TrackingStore(path).Shipments.Any(s => s.Id == initial.Id && s.DeliveredAt != null), "Packages delivered state survives restart");
        provider.Get = id => Task.FromResult(Snapshot(id)); await service.RefreshAsync();
        check(service.RefreshFailures.Count == 0, "Packages successful retry clears per-shipment errors");
        check(service.Delete(initial.Id).Outcome == App.PackageOperation.Success && new App.TrackingStore(path).Shipments.Count == 1, "Packages delete persists locally");
        provider.Get = _ => throw new OperationCanceledException();
        try { await service.RefreshAsync(); check(false, "Packages cancellation throws"); }
        catch (OperationCanceledException) { check(!service.Busy && service.Shipments.Count == 1, "Packages cancellation clears busy without deleting cached data"); }
        string invalidPath = Path.Combine(root, "future.json"); File.WriteAllText(invalidPath, "{\"Version\":999,\"Shipments\":[]}");
        using var protectedService = new App.PackageTrackingService(new App.TrackingStore(invalidPath), provider);
        int created = provider.Created;
        check((await protectedService.AddAsync("BLOCKED", null)).Outcome == App.PackageOperation.StorageError && provider.Created == created, "Packages unreadable store prevents remote creation");
    }
    private static void Ui(Action<bool, string> check, string root)
    {
        string registry = @"Software\WallpaperControl.PackageTests-" + Guid.NewGuid().ToString("N");
        var credentials = new App.TrackingCredentialStore(registry);
        try
        {
            var store = new App.TrackingStore(Path.Combine(root, "ui.json")); var provider = new Provider();
            for (int i = 0; i < 15; i++) store.Save(Snapshot("LONGNUMBER0123456789" + i) with { DisplayName = new string('W', 90), EstimatedDelivery = DateTimeOffset.UtcNow.AddDays(1), Events = new() { new() { EventId = "a", Description = new string('長', 300), RawOccurredAt = "2026-09-29" } } });
            using var service = new App.PackageTrackingService(store, provider);
            var resources = new ResourceManager("WallpaperControl.Strings", typeof(App.WidgetSettings).Assembly);
            var keys = resources.GetResourceSet(CultureInfo.InvariantCulture, true, false)!.Cast<System.Collections.DictionaryEntry>().Select(e => (string)e.Key).Where(k => k.StartsWith("Package", StringComparison.Ordinal)).ToArray();
            foreach (string language in new[] { "de", "en", "fr", "es", "ja" })
            {
                var set = resources.GetResourceSet(language == "de" ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(language), true, false)!;
                check(keys.Length >= 53 && keys.All(k => !string.IsNullOrWhiteSpace(set.GetString(k))), "Packages complete localization " + language);
                foreach (string milestone in new[] { "info_received", "in_transit", "out_for_delivery", "available_for_pickup", "delivered", "exception" })
                    check(App.PackagePresentation.Status(milestone, language) != App.PackagePresentation.Status("future-value", language), $"Packages localized milestone {language}/{milestone}");
                check(App.PackagePresentation.Status(null, language) == App.PackagePresentation.Status("future-value", language), "Packages unknown milestone fallback " + language);
                foreach (int dpi in new[] { 96, 120, 144, 192 })
                foreach (bool dark in new[] { false, true })
                {
                    var settings = new App.WidgetSettings { PackageMaximumHeight = 500, ClockLanguageCode = language };
                    using var widget = new App.PackageTrackingWidgetForm(service, settings, _ => { }, _ => { }); widget.ApplyTheme(dark);
                    using var image = widget.RenderBitmap(2400, dpi);
                    var viewport = Field<App.CalendarViewport>(widget, "viewport");
                    check(widget.UpdatedBounds.Top >= viewport.LogicalHeight, $"Packages {language}/{dpi}/{dark} update footer follows all packages offscreen");
                    viewport.SetOffset(float.MaxValue);
                    using (var bottom = widget.RenderBitmap(2400, dpi))
                        check(Math.Abs(widget.UpdatedBounds.Bottom - (viewport.LogicalHeight - 3)) < 0.01f && !viewport.SetOffset(float.MaxValue),
                            $"Packages {language}/{dpi}/{dark} scrolling ends with update footer at bottom");
                    viewport.SetOffset(0);
                    using (var reset = widget.RenderBitmap(2400, dpi)) { }
                    check(image.Width == (int)Math.Ceiling(390 * dpi / 96f) && image.Height <= 500 * dpi / 96 && widget.Controls.Count == 0, $"Packages {language}/{dpi}/{dark} bounded layered list");
                    check(widget.HitTest(new Point(350 * dpi / 96, 20 * dpi / 96)) == Guid.Empty, $"Packages {language}/{dpi}/{dark} fixed refresh header hit target");
                    check(widget.HitTestAdd(new Point(320 * dpi / 96, 20 * dpi / 96)) && !widget.HitTestAdd(new Point(350 * dpi / 96, 20 * dpi / 96)),
                        $"Packages {language}/{dpi}/{dark} add target stays separate from refresh");
                    check(widget.ScrollWheel(new Point(40 * dpi / 96, 100 * dpi / 96), -120, 3), $"Packages {language}/{dpi}/{dark} list mouse wheel");
                    check(!widget.ScrollWheel(new Point(40 * dpi / 96, 20 * dpi / 96), -120, 3), $"Packages {language}/{dpi}/{dark} header never scrolls");
                    widget.SetActivitySuspended(true);
                    check(!widget.ScrollWheel(new(40, 100), -120, 3), $"Packages {language}/{dpi}/{dark} fullscreen input suspended");
                    widget.SetActivitySuspended(false);
                    if (language == "de" && dpi == 96) image.Save(Path.Combine(root, dark ? "widget-dark.png" : "widget-light.png"));
                }
                using var editor = new App.PackageEditorForm(service, service.Shipments[0], language);
                check(Field<TextBox>(editor, "number").ReadOnly, "Packages tracking number is read-only " + language);
                using var setup = new App.Ship24SetupForm(credentials, language, () => provider);
                check(Field<TextBox>(setup, "keyInput").UseSystemPasswordChar && Field<TextBox>(setup, "keyInput").Text.Length == 0, "Packages missing key never exposed in UI " + language);
                using var detail = new App.PackageDetailsForm(service, service.Shipments[0].Id, language);
                check(Field<TextBox>(detail, "details").Text.Contains(new string('長', 300)), "Packages details retain full carrier history " + language);
                foreach (int dpi in new[] { 96, 120, 144, 192 })
                foreach (bool dark in new[] { false, true })
                {
                    foreach (Form form in new Form[] { new App.Ship24SetupForm(credentials, language, () => provider),
                        new App.PackageEditorForm(service, service.Shipments[0], language), new App.PackageManagerForm(service, language), new App.PackageDetailsForm(service, service.Shipments[0].Id, language) })
                    {
                        using (form)
                        {
                            App.NotesDialogStyle.Apply(form, App.SystemWidgetStyle.Minimal, dark);
                            using var font = new Font(form.Font.FontFamily, form.Font.Size * dpi / 96f);
                            form.Font = font; form.Scale(new SizeF(dpi / 96f, dpi / 96f)); form.Opacity = 0; form.Show(); form.PerformLayout(); Application.DoEvents();
                            if (form is App.PackageDetailsForm)
                            {
                                var text = Field<TextBox>(form, "details");
                                check(text.SelectionLength == 0 && !text.Focused, $"Packages details {language}/{dpi}/{dark} open without text selection or input focus");
                                text.Focus(); text.Select(0, 5);
                                check(text.ReadOnly && text.SelectedText == text.Text[..5] && text.ShortcutsEnabled && text.Cursor == Cursors.Default,
                                    $"Packages details {language}/{dpi}/{dark} retain selectable copyable read-only text");
                                text.Select(0, 0);
                            }
                            check(Controls(form).OfType<Button>().All(b => b.Height >= b.Font.Height && b.Width >= TextRenderer.MeasureText(b.Text, b.Font).Width),
                                $"Packages dialog {form.GetType().Name}/{language}/{dpi}/{dark} buttons fit translated text");
                            check(Controls(form).OfType<TextBox>().All(t => t.Width >= 100 && t.Height >= t.Font.Height),
                                $"Packages dialog {form.GetType().Name}/{language}/{dpi}/{dark} inputs retain usable bounds");
                            if (language == "de" && dpi == 96 && dark)
                            { using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(root, form.GetType().Name + ".png")); }
                            form.Close();
                        }
                    }
                }
            }
            check(provider.Created == 0 && provider.Fetched == 0 && provider.Tested == 0, "Packages startup render fullscreen and dialogs never request tracking");
            check(App.PackagePresentation.CarrierNumber(Snapshot("ABCDE"), false) == "ABCDE"
                && App.PackagePresentation.CarrierNumber(Snapshot("ABCDE") with { CarrierCode = "dhl" }, false) == "dhl · ABCDE", "Packages carrier display never invents a carrier");
            credentials.Save("ship24", new App.TrackingCredential("secret-only-for-offline-ui-tests"));
            foreach (bool fail in new[] { false, true })
            {
                provider.FailTest = fail;
                using var setup = new App.Ship24SetupForm(credentials, "de", () => provider);
                setup.Opacity = 0; setup.Show(); Application.DoEvents();
                check(Field<TextBox>(setup, "keyInput").Text == "" && !Controls(setup).Any(c => c.Text.Contains("secret-only-for-offline-ui-tests")), "Packages saved key not reinserted into controls");
                Controls(setup).OfType<Button>().Single(b => b.Text == App.Localization.Get("PackageTest", "de")).PerformClick(); Application.DoEvents();
                check(Controls(setup).OfType<Label>().Any(l => l.Text == App.Localization.Get(fail ? "PackageCredentialError" : "PackageConnected", "de")), "Packages connection test UI " + (fail ? "failure" : "success"));
                setup.Close();
            }
            var prefs = new App.WidgetSettings { PackageEnabled = false, PackageLocked = true, PackageMaximumHeight = 625, PackageLocation = new(80, 120) }; prefs.Save(registry);
            var loaded = App.WidgetSettings.Load(registry);
            check(loaded.PackageLocked && loaded.PackageMaximumHeight == 625 && loaded.Clone().PackageLocation == prefs.PackageLocation, "Packages widget configuration and clone persist");
            using var manager = new App.WidgetManager(() => { }, registryPath: registry, notesStore: new App.NotesStore(Path.Combine(root, "notes.json")), packageTrackingService: service, packageCredentials: credentials);
            App.WidgetEditSession? session = null;
            using var settingsEditor = new App.WidgetSettingsEditor(manager.Settings, preview: s => session!.Preview(s));
            session = new(manager, settingsEditor.ReadWidgetSettings, settingsEditor.LoadSettings); session.Begin();
            var styleChoice = Field<ComboBox>(settingsEditor, "packageStyle");
            styleChoice.SelectedIndex = (int)App.SystemWidgetStyle.Glow;
            check(manager.Settings.PackageStyle == App.SystemWidgetStyle.Glow && App.WidgetSettings.Load(registry).PackageStyle == App.SystemWidgetStyle.Minimal,
                "Packages style preview changes runtime without saving");
            session.Discard();
            check(styleChoice.SelectedIndex == 0 && manager.Settings.PackageStyle == App.SystemWidgetStyle.Minimal, "Packages discard restores widget style");
            styleChoice.SelectedIndex = (int)App.SystemWidgetStyle.Clean;
            check(session.Save() && App.WidgetSettings.Load(registry).PackageStyle == App.SystemWidgetStyle.Clean && manager.Settings.Clone().PackageStyle == App.SystemWidgetStyle.Clean,
                "Packages style persists and survives cloning");
            foreach (string lang in new[] { "de", "en", "fr", "es", "ja" })
            {
                settingsEditor.ApplyPresentation(false, lang);
                check(styleChoice.Items.Count == 3 && styleChoice.SelectedIndex == 1 && styleChoice.Items[2]!.ToString() == App.Localization.Get("ClockStyleGlow", lang),
                    "Packages style localization preserves selection " + lang);
            }
            foreach (bool dark in new[] { false, true })
            foreach (int dpi in new[] { 96, 120, 144, 192 })
            {
                var hashes = new HashSet<string>();
                foreach (var style in Enum.GetValues<App.SystemWidgetStyle>())
                {
                    using var widget = new App.PackageTrackingWidgetForm(service, new() { PackageStyle = style }, _ => { }, _ => { });
                    widget.ApplyTheme(dark); using var bitmap = widget.RenderBitmap(2400, dpi); using var bytes = new MemoryStream();
                    bitmap.Save(bytes, System.Drawing.Imaging.ImageFormat.Png);
                    hashes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes.ToArray())));
                }
                check(hashes.Count == 3, $"Packages three distinct styles at {dpi} DPI dark={dark}");
            }
            Field<NumericUpDown>(settingsEditor, "packageMaximum").Value = 800;
            check(session.IsDirty && manager.Settings.PackageMaximumHeight == 800 && App.WidgetSettings.Load(registry).PackageMaximumHeight == 625, "Packages live preview remains uncommitted");
            session.Discard(); check(manager.Settings.PackageMaximumHeight == 625, "Packages discard restores maximum height");
            Field<NumericUpDown>(settingsEditor, "packageMaximum").Value = 700;
            check(session.Save() && App.WidgetSettings.Load(registry).PackageMaximumHeight == 700, "Packages save commits maximum height");
            settingsEditor.ResetDefaults(); check(manager.Settings.PackageMaximumHeight == 500 && !manager.Settings.PackageEnabled && manager.Settings.PackageStyle == App.SystemWidgetStyle.Minimal, "Packages restore defaults is disabled with 500 logical pixels and Minimal style");
            session.Discard();
            int requests = provider.Fetched;
            Field<CheckBox>(settingsEditor, "packageEnabled").Checked = true;
            var activeWidget = Field<App.PackageTrackingWidgetForm>(manager, "packageWidget");
            check(Field<HashSet<Form>>(manager, "desktopWidgets").Contains(activeWidget), "Packages register in shared desktop widget collection");
            typeof(App.WidgetManager).GetMethod("RestoreDesktopWidgetBand", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(manager, null);
            manager.SetActivitySuspended(true); manager.SetActivitySuspended(false);
            check(provider.Fetched == requests && !activeWidget.IsDisposed, "Packages Win+D repair and fullscreen resume retain widget without networking");
            session.Discard();
            check(activeWidget.IsDisposed && !Field<HashSet<Form>>(manager, "desktopWidgets").Contains(activeWidget), "Packages discard closes preview widget and unregisters lifecycle");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(registry, false); }
    }
}
