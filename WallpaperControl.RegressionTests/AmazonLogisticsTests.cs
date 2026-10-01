extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using System.Drawing;
using System.Reflection;
using System.Text.Json.Nodes;

internal static class AmazonLogisticsTests
{
    private sealed class Provider : App.ITrackingProvider
    {
        public string ProviderId => "ship24";
        internal int Creates, Gets;
        internal bool MissingCredential;
        public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default) => throw new Exception("Unexpected connection test");
        public Task<App.TrackedShipment> CreateTrackerAsync(string trackingNumber, string? carrierCode = null, CancellationToken cancellationToken = default)
        {
            Creates++;
            if (MissingCredential) throw new App.TrackingProviderException(App.TrackingProviderFailure.MissingCredential);
            return Task.FromResult(new App.TrackedShipment { Provider = ProviderId, ProviderTrackerId = trackingNumber, TrackingNumber = trackingNumber });
        }
        public Task<App.TrackedShipment> GetTrackingAsync(string providerTrackerId, CancellationToken cancellationToken = default)
        { Gets++; return Task.FromResult(new App.TrackedShipment { Provider = ProviderId, ProviderTrackerId = providerTrackerId, TrackingNumber = providerTrackerId, LastSuccessfulRefresh = DateTimeOffset.UtcNow }); }
    }
    internal static void Run(Action<bool, string> check)
    {
        string root = Path.Combine(AppContext.BaseDirectory, "test-data", "amazon-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        Task.Run(() => Logic(check, root)).GetAwaiter().GetResult();
        using Task work = new(() => Ui(check, root)); var thread = new Thread(() => work.RunSynchronously(TaskScheduler.Default));
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); work.GetAwaiter().GetResult();
    }
    private static async Task Logic(Action<bool, string> check, string root)
    {
        foreach (string number in new[] { "DE5897460159", " de5897460159 ", "TBA123456789012", "tba000000000000" })
            check(App.AmazonLogistics.Recognizes(number), "Amazon accepts narrow format " + number.Trim());
        foreach (string number in new[] { "DE123456789", "DE12345678901", "DE12345X7890", "DE１２３４５６７８９０", "RR123456789DE", "00340433911523226765", "TBA12345678901", "TBA1234567890123", "TBA12345678901X", "TBC123456789012", "DE12345 7890", "" })
            check(!App.AmazonLogistics.Recognizes(number), "Amazon rejects unrelated or incomplete format " + number);
        string path = Path.Combine(root, "tracking.json"); var store = new App.TrackingStore(path); var provider = new Provider { MissingCredential = true };
        using var service = new App.PackageTrackingService(store, provider);
        check((await service.AddAsync(" de5897460159 ", " Amazon ")).Outcome == App.PackageOperation.Success && provider.Creates == 0,
            "Amazon local create bypasses provider even without credentials");
        var local = new App.TrackingStore(path).Shipments.Single();
        check(local.TrackingMode == "manual" && local.StatusSource == "local" && local.ProviderTrackerId == null && local.CarrierCode == App.AmazonLogistics.Carrier
            && local.DisplayName == "Amazon" && local.LastSuccessfulRefresh == null, "Amazon explicit local semantics and name survive reload");
        check((await service.AddAsync("DE5897460159", null)).Outcome == App.PackageOperation.Duplicate && provider.Creates == 0, "Amazon duplicate detection remains case insensitive");
        int notifications = 0; service.Changed += () => notifications++;
        check(service.SetManualDelivered(local.Id, true).Outcome == App.PackageOperation.Success && notifications == 1, "Amazon manual delivery notifies UI immediately");
        local = new App.TrackingStore(path).Shipments.Single();
        check(local.StatusMilestone == "delivered" && local.StatusSource == "user" && local.DeliveredAt != null, "Amazon manual delivered status and source survive reload");
        var deliveredAt = local.DeliveredAt;
        service.SetManualDelivered(local.Id, true);
        check(service.Shipments.Single().DeliveredAt == deliveredAt, "Amazon repeated delivery does not overwrite confirmation time");
        check(service.SetManualDelivered(local.Id, false).Outcome == App.PackageOperation.Success && notifications == 2, "Amazon undo notifies immediately");
        local = new App.TrackingStore(path).Shipments.Single();
        check(local.StatusMilestone == "unknown" && local.DeliveredAt == null && local.StatusSource == "local", "Amazon undo clears only manual delivered state persistently");
        check((await service.RefreshAsync()).Outcome == App.PackageOperation.Success && provider.Gets == 0 && service.RefreshFailures.Count == 0, "Amazon refresh skips local shipment without misleading failures");
        provider.MissingCredential = false;
        await service.AddAsync("00340433911523226765", "DHL"); await service.RefreshAsync();
        check(provider.Creates == 1 && provider.Gets == 1, "Amazon mixed list still creates and refreshes normal Ship24 shipment");
        var remote = service.Shipments.Single(s => s.TrackingMode == "provider");
        check(service.SetManualDelivered(remote.Id, true).Outcome == App.PackageOperation.Missing, "Amazon manual action cannot change provider status");
        store.Save(remote with { TrackingNumber = "DE0123456789" }); await service.RefreshAsync();
        check(provider.Gets == 1 && service.Shipments.Single(s => s.Id == remote.Id).TrackingMode == "provider", "Amazon recognized legacy shipment skips provider without silently rewriting metadata");
        var json = JsonNode.Parse(File.ReadAllText(path))!;
        var remoteNode = json["Shipments"]!.AsArray().Single(s => s!["TrackingMode"]!.GetValue<string>() == "provider")!.DeepClone();
        remoteNode.AsObject().Remove("TrackingMode"); remoteNode.AsObject().Remove("StatusSource");
        json["Shipments"] = new JsonArray(remoteNode); string oldPath = Path.Combine(root, "legacy.json"); File.WriteAllText(oldPath, json.ToJsonString());
        var legacy = new App.TrackingStore(oldPath);
        check(legacy.CanWrite && legacy.Shipments.Single().TrackingMode == "provider" && legacy.Shipments.Single().StatusSource == "provider", "Amazon legacy schema defaults to provider tracking");
        foreach (string field in new[] { "TrackingMode", "StatusSource" })
        foreach (JsonNode unknown in new JsonNode[] { JsonValue.Create("future-mode")!, JsonValue.Create(999)! })
        {
            var future = json.DeepClone(); future["Shipments"]![0]![field] = unknown.DeepClone();
            string futurePath = Path.Combine(root, field + unknown.ToJsonString().Length + ".json"); string original = future.ToJsonString();
            File.WriteAllText(futurePath, original); File.Copy(oldPath, futurePath + ".bak");
            var guarded = new App.TrackingStore(futurePath);
            check(!guarded.CanWrite && !guarded.RecoveredFromBackup && !guarded.Save(remote) && File.ReadAllText(futurePath) == original,
                "Amazon future semantics remain untouched and never fall back " + field + unknown.ToJsonString());
        }
    }
    private static IEnumerable<Control> Controls(Control c) => c.Controls.Cast<Control>().SelectMany(x => new[] { x }.Concat(Controls(x)));
    private static void Ui(Action<bool, string> check, string root)
    {
        var store = new App.TrackingStore(Path.Combine(root, "ui.json"));
        var local = new App.TrackedShipment { Provider = "local", TrackingMode = "manual", StatusSource = "local", CarrierCode = App.AmazonLogistics.Carrier, TrackingNumber = "DE5897460159" };
        store.Save(local); using var service = new App.PackageTrackingService(store, new Provider { MissingCredential = true });
        foreach (string lang in new[] { "de", "en", "fr", "es", "ja" })
        {
            foreach (string key in new[] { "PackageAmazonNotice", "PackageAmazonOnly", "PackageManualDelivered", "PackageMarkDelivered", "PackageMarkNotDelivered" })
                check(App.Localization.Get(key, lang) != key, "Amazon translation " + lang + "/" + key);
            check(App.PackagePresentation.ShipmentStatus(local, lang) == "Amazon Logistics" && App.PackagePresentation.ShipmentStatus(local with { StatusMilestone = "delivered", StatusSource = "user" }, lang) == App.Localization.Get("PackageManualDelivered", lang), "Amazon manual status source localized " + lang);
            foreach (int dpi in new[] { 96, 120, 144, 192 })
            foreach (bool dark in new[] { false, true })
            {
                using var editor = new App.PackageEditorForm(service, null, lang);
                var number = (TextBox)typeof(App.PackageEditorForm).GetField("number", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
                number.Text = local.TrackingNumber;
                App.NotesDialogStyle.Apply(editor, App.SystemWidgetStyle.Minimal, dark);
                using var editorFont = new Font(editor.Font.FontFamily, editor.Font.Size * dpi / 96f);
                editor.Font = editorFont; editor.Scale(new SizeF(dpi / 96f, dpi / 96f)); editor.Opacity = 0; editor.Show(); Application.DoEvents();
                check(Controls(editor).OfType<Label>().Any(l => l.Text == App.Localization.Get("PackageAmazonNotice", lang)) && !Controls(editor).OfType<ComboBox>().Any(), $"Amazon {lang}/{dpi}/{dark} detection notice before saving without carrier selector");
                var notice = Controls(editor).OfType<Label>().Single(l => l.Text == App.Localization.Get("PackageAmazonNotice", lang));
                check(notice.Bottom <= notice.Parent!.ClientSize.Height && notice.Right <= notice.Parent.ClientSize.Width, $"Amazon {lang}/{dpi}/{dark} full notice fits before save");
                if (lang == "de" && dpi == 96 && dark) { using var shot = new Bitmap(editor.Width, editor.Height); editor.DrawToBitmap(shot, new Rectangle(Point.Empty, shot.Size)); shot.Save(Path.Combine(root, "amazon-editor.png")); }
                editor.Close();
                using var details = new App.PackageDetailsForm(service, local.Id, lang);
                App.NotesDialogStyle.Apply(details, App.SystemWidgetStyle.Minimal, dark);
                using var font = new Font(details.Font.FontFamily, details.Font.Size * dpi / 96f);
                details.Font = font; details.Scale(new SizeF(dpi / 96f, dpi / 96f)); details.Opacity = 0; details.Show(); Application.DoEvents();
                var button = Controls(details).OfType<Button>().Single(b => b.Text == App.Localization.Get("PackageMarkDelivered", lang));
                check(button.Visible && button.Right <= button.Parent!.ClientSize.Width && button.Bottom <= button.Parent.ClientSize.Height, $"Amazon {lang}/{dpi}/{dark} manual action fits footer");
                button.PerformClick();
                check(button.Text == App.Localization.Get("PackageMarkNotDelivered", lang), $"Amazon {lang}/{dpi}/{dark} details update immediately after delivery");
                button.PerformClick(); details.Close();
                using var widget = new App.PackageTrackingWidgetForm(service, new App.WidgetSettings { ClockLanguageCode = lang }, _ => { }, _ => { }); widget.ApplyTheme(dark);
                using var bitmap = widget.RenderBitmap(2400, dpi);
                check(bitmap.Height <= 500 * dpi / 96 && service.Shipments.Single().LastSuccessfulRefresh == null, $"Amazon {lang}/{dpi}/{dark} local widget has no invented refresh time");
            }
        }
    }
}
