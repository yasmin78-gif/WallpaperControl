extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using Microsoft.Win32;
using System.Text.Json;

internal static class TrackingTests
{
    private sealed class FakeProvider : App.ITrackingProvider
    {
        public string ProviderId => "test";
        public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(true); }
        public Task<App.TrackedShipment> CreateTrackerAsync(string number, string? carrier = null, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(new App.TrackedShipment { Provider = ProviderId, TrackingNumber = number, CarrierCode = carrier }); }
        public Task<App.TrackedShipment> GetTrackingAsync(string id, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(new App.TrackedShipment { Provider = ProviderId, ProviderTrackerId = id }); }
    }
    internal static void Run(Action<bool, string> check)
    {
        string root = Path.Combine(AppContext.BaseDirectory, "test-data", "tracking-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "tracking.json");
        string registry = @"Software\WallpaperControl.TrackingTests-" + Guid.NewGuid().ToString("N");
        var now = new DateTimeOffset(2026, 9, 29, 10, 20, 30, TimeSpan.FromHours(2));
        App.TrackingEvent ev = new() { EventId = "event-1", OccurredAt = now, StatusCategory = "future-category",
            StatusMilestone = "future-milestone", Description = "Event description", Location = "City", CarrierCode = "carrier" };
        var shipment = new App.TrackedShipment { Provider = "test", ProviderTrackerId = "tracker-id", TrackingNumber = "synthetic-number",
            DisplayName = "Test shipment", CarrierCode = "carrier", StatusCategory = "future-category", StatusMilestone = "future-milestone",
            CreatedAt = now, EstimatedDelivery = now.AddDays(1), LastSuccessfulRefresh = now, DeliveredAt = now.AddDays(2),
            LastRelevantEvent = ev, Events = new() { ev }, NotificationState = new() { NotifiedEventIds = new() { "event-1", "event-older" },
                LastNotifiedStatusKey = "future-category/future-milestone", LastNotifiedAt = now } };
        string unrelated = Path.Combine(root, "notes.json");
        File.WriteAllText(unrelated, "unrelated feature sentinel");
        try
        {
            var store = new App.TrackingStore(path);
            check(store.CanWrite && !store.LoadIssue && store.Shipments.Count == 0 && !File.Exists(path), "Tracking missing file is empty without creating data");
            check(store.Save(shipment), "Tracking saves complete snapshot");
            var loaded = new App.TrackingStore(path).Shipments.Single();
            check(loaded.Id == shipment.Id && loaded.ProviderTrackerId == shipment.ProviderTrackerId && loaded.TrackingNumber == shipment.TrackingNumber
                && loaded.DisplayName == shipment.DisplayName && loaded.CarrierCode == shipment.CarrierCode, "Tracking identity roundtrip");
            check(loaded.CreatedAt == now && loaded.CreatedAt.Offset == TimeSpan.Zero && loaded.EstimatedDelivery == now.AddDays(1)
                && loaded.DeliveredAt == now.AddDays(2) && loaded.LastSuccessfulRefresh == now, "Tracking timestamps persist as unambiguous UTC instants");
            check(loaded.StatusCategory == "future-category" && loaded.StatusMilestone == "future-milestone"
                && loaded.Events.Single().StatusMilestone == "future-milestone", "Unknown future statuses survive roundtrip");
            check(loaded.LastRelevantEvent == loaded.Events.Single() && loaded.Events.Single().OccurredAt == now, "Tracking history and relevant event survive restart");
            check(loaded.NotificationState.NotifiedEventIds.SetEquals(new[] { "event-1", "event-older" })
                && loaded.NotificationState.LastNotifiedAt == now && loaded.NotificationState.LastNotifiedStatusKey == shipment.NotificationState.LastNotifiedStatusKey,
                "Notification identities and last notified status survive restart");
            shipment.Events.Clear(); loaded.NotificationState.NotifiedEventIds.Clear();
            check(store.Shipments.Single().Events.Count == 1 && store.Shipments.Single().NotificationState.NotifiedEventIds.Count == 2,
                "Store snapshots cannot be mutated through input or output collections");
            check(store.Save(store.Shipments.Single() with { DisplayName = "Updated" }) && File.Exists(path + ".bak"), "Atomic replacement retains previous backup");
            File.WriteAllText(path, "{broken");
            store = new App.TrackingStore(path);
            check(store.LoadIssue && store.RecoveredFromBackup && store.CanWrite && store.Shipments.Single().DisplayName == "Test shipment",
                "Corrupt main file recovers last valid backup");
            check(Directory.GetFiles(root, "tracking.json.corrupt-*").Length == 1, "Corrupt original is preserved for recovery");
            check(store.Save(store.Shipments.Single()) && new App.TrackingStore(path).Shipments.Count == 1, "Recovered data can be saved again");
            File.Delete(path);
            check(new App.TrackingStore(path).RecoveredFromBackup, "Missing primary can recover backup");
            File.WriteAllText(path, "{\"Version\":999,\"Shipments\":\"future format\"}");
            string future = File.ReadAllText(path);
            store = new App.TrackingStore(path);
            check(!store.CanWrite && store.LoadIssue && !store.Save(shipment) && File.ReadAllText(path) == future,
                "Future schema is preserved read-only even if its payload has a new shape");
            string damagedPath = Path.Combine(root, "damaged.json"); File.WriteAllText(damagedPath, "bad");
            File.WriteAllText(damagedPath + ".bak", "also bad");
            var damaged = new App.TrackingStore(damagedPath);
            check(damaged.LoadIssue && !damaged.CanWrite && damaged.Shipments.Count == 0, "Both damaged files fail safely without overwriting history");
            string emptyPath = Path.Combine(root, "empty.json"); File.WriteAllText(emptyPath, "{\"Version\":1,\"Shipments\":[]}");
            check(new App.TrackingStore(emptyPath).CanWrite && new App.TrackingStore(emptyPath).Shipments.Count == 0, "Empty document is valid");
            foreach (string invalid in new[] { "", "[]", "{}", "{\"Version\":\"1\",\"Shipments\":[]}",
                "{\"Version\":1,\"Shipments\":null}", "{\"Version\":1,\"Shipments\":[null]}",
                JsonSerializer.Serialize(new { Version = 1, Shipments = new[] { shipment, shipment } }),
                JsonSerializer.Serialize(new { Version = 1, Shipments = new[] { shipment } }).Replace("\"CreatedAt\":\"2026-09-29T10:20:30+02:00\"", "\"CreatedAt\":123") })
            {
                string invalidPath = Path.Combine(root, Guid.NewGuid() + ".json"); File.WriteAllText(invalidPath, invalid);
                var invalidStore = new App.TrackingStore(invalidPath);
                check(invalidStore.LoadIssue && !invalidStore.CanWrite, "Invalid JSON shape or duplicate identity cannot crash or overwrite storage");
            }
            string extensionPath = Path.Combine(root, "extension.json");
            string data = JsonSerializer.Serialize(new { Version = 1, Shipments = new[] { shipment }, FutureDocumentField = "keep" });
            using var extensionDoc = JsonDocument.Parse("{\"flag\":true}");
            shipment = shipment with { AdditionalData = new() { ["FutureShipmentField"] = extensionDoc.RootElement.Clone() } };
            File.WriteAllText(extensionPath, data);
            var extended = new App.TrackingStore(extensionPath); extended.Save(shipment);
            using (var saved = JsonDocument.Parse(File.ReadAllText(extensionPath)))
                check(saved.RootElement.GetProperty("FutureDocumentField").GetString() == "keep"
                    && saved.RootElement.GetProperty("Shipments")[0].GetProperty("FutureShipmentField").GetProperty("flag").GetBoolean(),
                    "Additive schema fields are preserved across writes");
            using (var locked = new FileStream(extensionPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                check(!extended.Save(shipment with { DisplayName = "must not commit" }) && extended.Shipments.Single().DisplayName == shipment.DisplayName,
                    "Failed atomic write preserves in-memory state");
            check(Directory.GetFiles(root, "*.tmp").Length == 0, "Failed write cleans temporary files");
            check(extended.Delete(shipment.Id) && new App.TrackingStore(extensionPath).Shipments.Count == 0, "Shipment deletion persists");
            string noOffset = Path.Combine(root, "offset.json");
            File.WriteAllText(noOffset, JsonSerializer.Serialize(new { Version = 1, Shipments = new[] { shipment } }).Replace("2026-09-29T10:20:30+02:00", "2026-09-29T10:20:30"));
            check(new App.TrackingStore(noOffset).LoadIssue, "Ambiguous timestamp without timezone is rejected");

            var credentials = new App.TrackingCredentialStore(registry);
            string secret = "test-secret-" + Guid.NewGuid().ToString("N");
            var credential = new App.TrackingCredential(secret);
            check(credentials.Load("test", out _) == App.TrackingCredentialLoadResult.Missing, "Missing credential is explicit");
            check(credentials.Save("test", credential), "Credential saved using existing user-bound DPAPI");
            credentials = new App.TrackingCredentialStore(registry);
            check(credentials.Load("test", out var restored) == App.TrackingCredentialLoadResult.Loaded && restored!.Reveal() == secret,
                "Credential loads after store restart");
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(registry, true)!)
            {
                string ciphertext = (string)key.GetValue("test")!;
                check(!ciphertext.Contains(secret) && !System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(ciphertext)).Contains(secret),
                    "Registry contains ciphertext rather than plaintext or base64 plaintext");
                check(App.WindowsSecretProtector.Unprotect(ciphertext).Contains(secret), "Credential uses existing CurrentUser DPAPI protector");
            }
            check(!credential.ToString().Contains(secret), "Credential ToString redacts its value");
            bool rejected = false;
            try { _ = JsonSerializer.Serialize(credential); }
            catch (NotSupportedException ex) { rejected = !ex.ToString().Contains(secret); }
            check(rejected, "Accidental credential JSON serialization fails without exposing secret");
            check(credentials.Save("second", new App.TrackingCredential("other-key")) && credentials.Save("test", new App.TrackingCredential("replacement-key")),
                "Independent provider credentials can be updated");
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(registry, true)!) key.SetValue("test", "invalid ciphertext");
            check(credentials.Load("test", out restored) == App.TrackingCredentialLoadResult.Recovered && restored!.Reveal() == secret,
                "Credential recovery restores previous valid protected value");
            check(credentials.Delete("test") && credentials.Load("test", out _) == App.TrackingCredentialLoadResult.Missing,
                "Credential deletion removes both current and recovery values");
            check(credentials.Load("second", out restored) == App.TrackingCredentialLoadResult.Loaded && restored!.Reveal() == "other-key",
                "Deleting one provider preserves others");
            check(credentials.Delete("test") && !credentials.Save("../bad", credential), "Credential deletion is idempotent and invalid provider IDs are rejected");
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(registry, true)!) key.SetValue("second", "broken");
            check(credentials.Load("second", out _) == App.TrackingCredentialLoadResult.Unreadable, "Broken credential safely reports unreadable");
            check(Directory.GetFiles(root, "*", SearchOption.AllDirectories).All(f => !File.ReadAllText(f).Contains(secret)),
                "No API key exists in tracking files, backups or temporary files");
            check(File.ReadAllText(unrelated) == "unrelated feature sentinel", "Unrelated feature data remains unchanged");
            if (File.Exists(App.AppLogger.LogFilePath)) check(!File.ReadAllText(App.AppLogger.LogFilePath).Contains(secret), "Error logs do not contain credential content");
            var provider = new FakeProvider();
            check(provider.TestConnectionAsync().GetAwaiter().GetResult() && provider.CreateTrackerAsync("number").Result.Provider == "test"
                && provider.GetTrackingAsync("id").Result.ProviderTrackerId == "id", "Provider contract supports all three operations without networking");
            foreach (Action operation in new Action[] { () => provider.TestConnectionAsync(new(true)).GetAwaiter().GetResult(),
                () => provider.CreateTrackerAsync("number", null, new(true)).GetAwaiter().GetResult(),
                () => provider.GetTrackingAsync("id", new(true)).GetAwaiter().GetResult() })
            {
                bool cancelled = false; try { operation(); } catch (OperationCanceledException) { cancelled = true; }
                check(cancelled, "Provider operation supports cancellation");
            }
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(registry, false); }
    }
}
