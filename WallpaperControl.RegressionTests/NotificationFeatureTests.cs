extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using Microsoft.Win32;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class NotificationFeatureTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private sealed class Provider : App.ITrackingProvider
    {
        public string ProviderId => "ship24";
        internal int Calls;
        internal string Milestone = "info_received";
        internal bool Fail, WrongId, Held;
        internal DateTimeOffset? EventTime;
        public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<App.TrackedShipment> CreateTrackerAsync(string number, string? carrierCode = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Shipment(number));
        public Task<App.TrackedShipment> GetTrackingAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async Task<App.TrackedShipment> RefreshTrackingAsync(App.TrackedShipment shipment, CancellationToken cancellationToken = default)
        {
            Calls++; if (Held) await Task.Delay(Timeout.Infinite, cancellationToken);
            if (Fail) throw new App.TrackingProviderException(App.TrackingProviderFailure.NetworkError);
            return shipment with { StatusMilestone = Milestone, ProviderTrackerId = WrongId ? "wrong" : shipment.ProviderTrackerId,
                LastSuccessfulRefresh = DateTimeOffset.UtcNow, LastRelevantEvent = EventTime == null ? null
                    : new App.TrackingEvent { EventId = EventTime.Value.ToString("O"), OccurredAt = EventTime, StatusMilestone = Milestone } };
        }
    }
    private static App.TrackedShipment Shipment(string number = "TEST12345") => new()
    { Provider = "ship24", ProviderTrackerId = number, TrackingNumber = number, DisplayName = "Test package", StatusMilestone = "info_received" };
    internal static void Run(Action<bool, string> check)
    {
        string directory = Path.Combine(Path.GetTempPath(), "WallpaperControl.NotificationTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            PackageAsync(check, directory).GetAwaiter().GetResult();
            Exception? failure = null;
            var thread = new Thread(() => { try { Ui(check, directory); } catch (Exception ex) { failure = ex; } });
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if (failure != null) throw failure;
        }
        finally { Directory.Delete(directory, true); }
    }
    private static async Task PackageAsync(Action<bool, string> check, string directory)
    {
        string path = Path.Combine(directory, "tracking.json"); var store = new App.TrackingStore(path); var provider = new Provider();
        var shipment = Shipment(); check(store.Save(shipment), "Package notification fixture persists");
        using var service = new App.PackageTrackingService(store, provider);
        var alerts = new List<App.TrackedShipment>(); bool fullscreen = false;
        using var notifications = new App.PackageStatusNotifications(service, alerts.Add, () => fullscreen);
        notifications.Configure(true);
        check(alerts.Count == 0, "Loading existing shipments does not notify");
        await service.RefreshAsync(); check(alerts.Count == 0 && provider.Calls == 1, "Unchanged first refresh has no notification or extra provider request");
        provider.Milestone = "in_transit"; await service.RefreshAsync();
        check(alerts.Count == 1 && alerts[0].StatusMilestone == "in_transit" && new App.TrackingStore(path).Shipments.Single().StatusMilestone == "in_transit",
            "Status notification fires only after durable successful refresh");
        await service.RefreshAsync(); check(alerts.Count == 1, "Repeated refresh does not repeat package notification");
        service.Rename(shipment.Id, "Renamed"); check(alerts.Count == 1, "Renaming shipment does not notify");
        provider.Fail = true; provider.Milestone = "out_for_delivery"; await service.RefreshAsync();
        check(alerts.Count == 1 && service.Shipments.Single().StatusMilestone == "in_transit", "Provider failure preserves status and does not notify");
        provider.Fail = false; provider.Milestone = "in_transit"; await service.RefreshAsync();
        check(alerts.Count == 1, "Recovery to same status does not duplicate notification");
        provider.WrongId = true; provider.Milestone = "out_for_delivery"; await service.RefreshAsync();
        check(alerts.Count == 1, "Invalid tracker response does not notify"); provider.WrongId = false;
        using (var fileLock = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = await service.RefreshAsync();
            check(result.Outcome == App.PackageOperation.StorageError && alerts.Count == 1, "Failed atomic save never emits status notification");
        }
        provider.Held = true;
        using (var cancellation = new CancellationTokenSource())
        {
            var refresh = service.RefreshAsync(cancellation.Token); cancellation.Cancel();
            try { await refresh; } catch (OperationCanceledException) { }
            check(alerts.Count == 1 && !service.Busy, "Cancelled provider refresh does not notify and releases operation gate");
        }
        provider.Held = false;
        notifications.Configure(false); await service.RefreshAsync(); check(alerts.Count == 1, "Disabled package notifications stay quiet");
        notifications.Configure(true); await service.RefreshAsync(); check(alerts.Count == 1, "Re-enabling does not notify existing package status");
        fullscreen = true; provider.Milestone = "in_transit"; await service.RefreshAsync();
        check(alerts.Count == 1, "Fullscreen suppresses package alert"); fullscreen = false; await service.RefreshAsync();
        check(alerts.Count == 1, "Suppressed package update is not queued for resume");
        notifications.SetSuspended(true); provider.Milestone = "out_for_delivery"; await service.RefreshAsync(); notifications.SetSuspended(false);
        await service.RefreshAsync(); check(alerts.Count == 1, "Activity suspension suppresses package alert without delayed duplicate");
        provider.EventTime = DateTimeOffset.UtcNow; await service.RefreshAsync(); provider.EventTime -= TimeSpan.FromHours(1); provider.Milestone = "in_transit";
        await service.RefreshAsync(); check(alerts.Count == 1, "Older carrier event does not produce an out-of-order alert");
        provider.EventTime = DateTimeOffset.UtcNow; provider.Milestone = "delivered";
        await service.RefreshAutomaticAsync(_ => true, _ => { }, default);
        check(alerts.Count == 2 && alerts[^1].StatusMilestone == "delivered", "Automatic refresh emits delivered notification through same path");
        await service.AddAsync("TEST67890", "New"); check(alerts.Count == 2, "Adding an existing-status package does not notify");
        notifications.Dispose(); provider.Milestone = "in_transit"; await service.RefreshAsync();
        check(alerts.Count == 2, "Disposed package observer releases subscription");
        using var restarted = new App.PackageTrackingService(new App.TrackingStore(path), new Provider { Milestone = "in_transit" });
        using var reloaded = new App.PackageStatusNotifications(restarted, alerts.Add, () => false); reloaded.Configure(true); await restarted.RefreshAsync();
        check(alerts.Count == 2, "Restart uses persisted current status and does not repeat old notification");
    }
    private static void Ui(Action<bool, string> check, string directory)
    {
        string registry = @"Software\WallpaperControl.NotificationFeatureTests\" + Guid.NewGuid().ToString("N");
        try
        {
            check(new App.WidgetSettings().NotificationSound == App.NotificationSoundKind.Chime && !new App.WidgetSettings().PackageStatusNotifications,
                "Default sound is chime; package notifications are opt-in");
            foreach (var sound in Enum.GetValues<App.NotificationSoundKind>())
            {
                var settings = new App.WidgetSettings { NotificationSound = sound, PackageStatusNotifications = true }; settings.Save(registry);
                check(App.WidgetSettings.Load(registry).NotificationSound == sound && settings.Clone().NotificationSound == sound,
                    "Sound preference persists and clones " + sound);
            }
            check(App.NotificationSounds.Normalize(99) == App.NotificationSoundKind.Chime, "Unsupported sound value falls back safely");
            using var editor = new App.WidgetSettingsEditor(new App.WidgetSettings { NotificationSound = App.NotificationSoundKind.Off, PackageStatusNotifications = true });
            check(editor.ReadWidgetSettings(false).NotificationSound == App.NotificationSoundKind.Off && editor.ReadWidgetSettings(false).PackageStatusNotifications,
                "Editor loads sound and package notification preferences");
            check(!editor.WidgetKeys.Contains("NotificationSettings"), "Global notification page is absent from widget navigation");
            editor.ResetDefaults();
            check(editor.ReadWidgetSettings(false).NotificationSound == App.NotificationSoundKind.Off, "Widget defaults preserve global sound preference");
            using var dialog = new App.SettingsForm(false, "system", 3, 0x27, 3, 0x50, 3, 0x45, 7, 0x52, "", true, false, true, true, true, 80, App.NotificationSoundKind.Off);
            var combo = (ComboBox)typeof(App.SettingsForm).GetField("notificationSound", Flags)!.GetValue(dialog)!;
            var tabs = (TabControl)typeof(App.SettingsForm).GetField("settingsTabControl", Flags)!.GetValue(dialog)!;
            check(tabs.TabPages.Cast<TabPage>().Any(page => (string?)page.Tag == "NotificationSettings"), "Global settings contain notification page");
            check(combo.SelectedIndex == 0 && dialog.NotificationSound == App.NotificationSoundKind.Off, "Global settings load saved sound");
            check(combo.Items.Count == 3, "Sound UI exposes exactly three choices");
            combo.SelectedIndex = 2;
            foreach (var language in new[] { "de", "en", "fr", "es", "ja" })
            {
                typeof(App.SettingsForm).GetMethod("ApplyPreviewLocalization", Flags)!.Invoke(dialog, new object[] { language });
                check(combo.SelectedIndex == 2, "Language change preserves selected tone " + language);
                foreach (var key in new[] { "NotificationSettings", "NotificationSoundPreview", "NotificationSoundChime", "PackageStatusNotifications", "PackageStatusNotificationTitle", "PackageStatusNotificationBody" })
                    check(App.Localization.Get(key, language) != key, "Notification localization " + language + "/" + key);
            }
            check(dialog.NotificationSound == App.NotificationSoundKind.Off, "Editing and localization do not accept preference before saving");
            typeof(App.SettingsForm).GetMethod("SaveAndClose", Flags)!.Invoke(dialog, null);
            check(dialog.DialogResult == DialogResult.OK && dialog.NotificationSound == App.NotificationSoundKind.Chime, "Global save captures selected sound");
            using var wav = typeof(App.NotificationSounds).Assembly.GetManifestResourceStream("WallpaperControl.NotificationChime.wav")!;
            using var binary = new BinaryReader(wav);
            check(new string(binary.ReadChars(4)) == "RIFF" && wav.Length == 44144, "Embedded chime is a complete half-second mono PCM WAV");
            using var quiet = new App.NotificationSounds(); quiet.Play(App.NotificationSoundKind.Off);
            check(typeof(App.NotificationSounds).GetField("player", Flags)!.GetValue(quiet) == null, "Off does not allocate or play audio resources");
            quiet.Dispose(); quiet.Play(App.NotificationSoundKind.Chime);
            check(typeof(App.NotificationSounds).GetField("player", Flags)!.GetValue(quiet) == null, "Disposed sound owner cannot restart playback");

            using var icon = new NotifyIcon { Icon = SystemIcons.Application, Visible = true };
            App.NativeNotificationBridge.Data? sent = null; int shown = 0;
            using (var bridge = new App.NativeNotificationBridge(icon, () => shown++, data => { sent = data; return true; }))
            {
                check(bridge.Show("Test", "Silent", true) && sent is { Window: not 0 } && sent.Value.Id > 0,
                    "Native .NET 10 NotifyIcon identity bridge obtains actual tray HWND and ID");
                check(sent!.Value.InfoFlags == 0x91 && sent.Value.Flags == 0x10 && sent.Value.Size == Marshal.SizeOf<App.NativeNotificationBridge.Data>(),
                    "Custom/off native notifications request NIIF_NOSOUND and quiet-time respect");
                check(Marshal.SizeOf<App.NativeNotificationBridge.Data>() == (IntPtr.Size == 8 ? 976 : 956)
                    && Marshal.OffsetOf<App.NativeNotificationBridge.Data>("InfoFlags").ToInt32() == (IntPtr.Size == 8 ? 948 : 932),
                    "NOTIFYICONDATAW native size and sound-flag offset match Windows ABI");
                SendMessage(sent.Value.Window, 0x800, 0, 0x403); check(shown == 0, "Native balloon HIDE does not trigger sound callback");
                SendMessage(sent.Value.Window, 0x800, 0, 0x402); check(shown == 1, "Actual native balloon SHOW triggers sound callback");
                bridge.Show("Standard", "Audible", false); check((sent.Value.InfoFlags & 0x10) == 0, "Windows-standard native mode leaves OS sound enabled");
                bridge.Dispose(); check(!bridge.Show("Disposed", "Ignored", true), "Disposed native bridge releases subclass and cannot display");
            }
            var displayed = new List<string>(); int sounds = 0, packageClicks = 0; App.NotificationSoundKind tone = App.NotificationSoundKind.Chime;
            using var notifications = new App.DesktopNotifications(icon, () => false, (_, body) => displayed.Add(body), visible: () => true,
                sound: () => tone, playSound: _ => sounds++);
            notifications.Show("Package", "first", () => { packageClicks++; notifications.OnClosed(); }, "package");
            notifications.Show("Twitch", "second", () => { }, "twitch"); notifications.OnShown(); notifications.OnShown();
            check(sounds == 1, "Own tone plays once after display confirmation, never twice");
            notifications.OnClicked(); check(packageClicks == 1 && displayed.SequenceEqual(new[] { "first", "second" }),
                "Package click retains its action even with nested close callback");
            notifications.Clear("twitch"); notifications.OnClicked(); tone = App.NotificationSoundKind.Off;
            notifications.Show("Quiet", "silent", () => { }, "package"); notifications.OnShown(); check(sounds == 1, "Off mode never plays custom tone");
            notifications.OnClosed(); tone = App.NotificationSoundKind.WindowsStandard;
            notifications.Show("Windows", "standard", () => { }, "package"); notifications.OnShown(); check(sounds == 1, "Windows sound is never duplicated by custom player");
            notifications.OnClosed();
            notifications.Show("P", "kept", () => packageClicks++, "package"); notifications.Show("T", "discarded", () => { }, "twitch");
            notifications.Clear("twitch"); notifications.OnClicked(); check(packageClicks == 2 && !displayed.Contains("discarded"), "Clearing Twitch alerts does not clear package click target");

            DateTime now = new(2026, 10, 2, 12, 0, 0); bool fullscreen = true; int reminderSounds = 0;
            var notes = new App.NotesStore(Path.Combine(directory, "notes.json"), () => now);
            check(notes.SaveEntry(new App.NoteEntry { Title = "Sound test", HasReminder = true, DueDate = DateOnly.FromDateTime(now), DueTime = TimeOnly.FromDateTime(now), PopupReminder = true }),
                "Reminder sound fixture persists valid reminder");
            using var reminders = new App.NoteReminderService(notes, () => "de", () => fullscreen, () => reminderSounds++);
            reminders.Tick(); check(reminderSounds == 0, "Fullscreen reminder deferral stays silent");
            fullscreen = false; reminders.Tick(); reminders.Tick(); check(reminderSounds == 1, "New reminder plays once, repeated ticks stay silent");
            fullscreen = true; reminders.Tick(); fullscreen = false; reminders.Tick();
            check(reminderSounds == 1, "Restoring same reminder after fullscreen does not replay tone");
            QueueScenarios(check, icon);
            editor.Dispose(); editor.Dispose(); check(editor.IsDisposed, "Repeated settings editor disposal safely releases sound and Twitch resources");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(registry, false); }
    }
    private static void QueueScenarios(Action<bool, string> check, NotifyIcon icon)
    {
        DateTimeOffset clock = new(2026, 10, 2, 18, 0, 0, TimeSpan.Zero);
        var displayed = new List<string>(); var clicks = new List<string>(); int sounds = 0;
        using (var queue = new App.DesktopNotifications(icon, () => false, (_, body) => displayed.Add(body), visible: () => true,
            sound: () => App.NotificationSoundKind.Chime, playSound: _ => sounds++, now: () => clock))
        {
            queue.Show("Package", "package", () => clicks.Add("package"), "package");
            queue.Show("Twitch", "Gronkh", () => clicks.Add("Gronkh"), "twitch"); queue.OnShown();
            check(displayed.SequenceEqual(new[] { "package" }) && sounds == 1, "Simultaneous package and Gronkh alerts show only package first");
            queue.OnHidden();
            check(displayed.SequenceEqual(new[] { "package", "Gronkh" }), "Dismissal via native HIDE advances to Gronkh instead of blocking queue");
            queue.OnHidden(); queue.OnShown(); queue.OnClicked();
            check(clicks.SequenceEqual(new[] { "Gronkh" }) && sounds == 2, "Queued Gronkh alert retains channel action and plays its own sound once");
            clock += TimeSpan.FromMinutes(2);
            queue.Show("Twitch", "xPandorya", () => clicks.Add("xPandorya"), "twitch"); queue.OnShown(); queue.OnClicked();
            check(displayed[^1] == "xPandorya" && clicks[^1] == "xPandorya" && sounds == 3, "xPandorya two minutes later displays normally with its own click and sound");
            queue.Show("P", "active", () => { }, "package"); queue.Show("T", "old queued", () => { }, "twitch");
            clock += TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(1);
            queue.Show("T", "fresh queued", () => { }, "twitch"); queue.OnClosed();
            check(!displayed.Contains("old queued") && displayed[^1] == "fresh queued", "Two-minute expiry drops only old waiting notices, not newly arriving alerts");
        }
        displayed.Clear();
        using (var bounded = new App.DesktopNotifications(icon, () => false, (_, body) => displayed.Add(body), visible: () => true, now: () => clock))
        {
            for (int i = 0; i < 40; i++) bounded.Show("Burst", i.ToString(), () => { }, "package");
            for (int i = 0; i < 40; i++) bounded.OnClosed();
            check(displayed.Count == 33 && displayed.Distinct().Count() == 33, "Burst queue is bounded to active plus 32 waiting notices without duplicates");
        }
        bool fail = true;
        using (var resilient = new App.DesktopNotifications(icon, () => false, (_, body) => { if (fail) throw new InvalidOperationException(); displayed.Add(body); }, visible: () => true))
        {
            resilient.Show("Failure", "unavailable", () => { }, "package"); fail = false;
            resilient.Show("Recovery", "available", () => { }, "package");
            check(displayed[^1] == "available", "Native display failure cannot crash or permanently block subsequent notifications");
        }
        displayed.Clear();
        using (var suppressed = new App.DesktopNotifications(icon, () => false, (_, body) => displayed.Add(body), visible: () => true, now: () => clock))
        {
            suppressed.Show("No callback", "old", () => { }, "package");
            clock += TimeSpan.FromMinutes(2);
            suppressed.Show("Fresh", "xPandorya", () => { }, "twitch");
            check(displayed.SequenceEqual(new[] { "old", "xPandorya" }), "Missing native close callback cannot block a fresh alert two minutes later");
        }
    }
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
