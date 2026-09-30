extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using Microsoft.Win32;
using System.Drawing;
using System.Reflection;

internal static class PackageRefreshTests
{
    private sealed class Clock
    {
        internal DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
        internal void Advance(int minutes) => Now = Now.AddMinutes(minutes);
    }
    private sealed class Provider(Clock clock) : App.ITrackingProvider
    {
        public string ProviderId => "ship24";
        internal int Creates;
        internal readonly List<string> Gets = new();
        internal Func<string, CancellationToken, Task<App.TrackedShipment>>? Get;
        public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default) => throw new Exception("Unexpected test connection");
        public Task<App.TrackedShipment> CreateTrackerAsync(string trackingNumber, string? carrierCode = null, CancellationToken cancellationToken = default)
        { Creates++; throw new Exception("Automatic tracking must never create a tracker"); }
        public Task<App.TrackedShipment> GetTrackingAsync(string providerTrackerId, CancellationToken cancellationToken = default)
        { Gets.Add(providerTrackerId); return Get?.Invoke(providerTrackerId, cancellationToken) ?? Task.FromResult(Shipment(providerTrackerId, clock.Now)); }
    }
    private static App.TrackedShipment Shipment(string id, DateTimeOffset? last) => new()
    {
        Provider = "ship24", ProviderTrackerId = id, TrackingNumber = "private-number-" + id, LastSuccessfulRefresh = last,
        StatusMilestone = "in_transit", Events = new() { new() { EventId = "keep", Description = "Keep cached event" } }
    };
    private sealed class Fixture : IDisposable
    {
        internal readonly Clock Clock = new();
        internal readonly App.TrackingStore Store;
        internal readonly string PathName;
        internal readonly Provider Provider;
        internal readonly App.PackageTrackingService Service;
        internal readonly App.PackageRefreshScheduler Scheduler;
        internal readonly List<string> Logs = new();
        internal Fixture(string root)
        {
            PathName = Path.Combine(root, Guid.NewGuid() + ".json");
            Store = new(PathName); Provider = new(Clock);
            Service = new(Store, Provider); Scheduler = new(Service, () => Clock.Now, Logs.Add, useTimer: false);
            Scheduler.Configure(true, 30);
        }
        public void Dispose() { Scheduler.Dispose(); Service.Dispose(); }
    }
    private static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj)!;
    internal static void Run(Action<bool, string> check)
    {
        string root = Path.Combine(AppContext.BaseDirectory, "test-data", "package-refresh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Task.Run(() => Logic(check, root)).GetAwaiter().GetResult();
        using var work = new Task(() => Ui(check, root));
        var thread = new Thread(() => work.RunSynchronously(TaskScheduler.Default));
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); work.GetAwaiter().GetResult();
    }
    private static async Task Logic(Action<bool, string> check, string root)
    {
        using (var f = new Fixture(root))
        {
            f.Store.Save(Shipment("fresh", f.Clock.Now.AddMinutes(-29)));
            f.Store.Save(Shipment("stale", f.Clock.Now.AddMinutes(-30)));
            f.Store.Save(Shipment("never", null));
            f.Store.Save(Shipment("delivered", null) with { StatusMilestone = "delivered" });
            f.Store.Save(new() { Provider = "local", TrackingMode = "manual", StatusSource = "local", TrackingNumber = "DE5897460159", CarrierCode = App.AmazonLogistics.Carrier });
            f.Store.Save(Shipment("missing-id", null) with { ProviderTrackerId = null });
            f.Store.Save(Shipment("foreign-provider", null) with { Provider = "future" });
            await f.Scheduler.EvaluateAsync("startup");
            check(f.Provider.Gets.SequenceEqual(new[] { "stale", "never" }), "Package refresh startup queries only stale existing eligible trackers");
            check(f.Provider.Creates == 0, "Package refresh startup creates no trackers");
            check(!f.Provider.Gets.Contains("fresh"), "Package refresh fresh shipment skipped");
            check(!f.Provider.Gets.Contains("delivered"), "Package refresh delivered provider skipped");
            check(f.Service.Shipments.Single(s => s.TrackingMode == "manual").LastSuccessfulRefresh == null, "Package refresh local Amazon untouched without timestamp");
            check(!f.Provider.Gets.Contains("missing-id"), "Package refresh missing ID never creates replacement tracker");
            check(!f.Provider.Gets.Contains("foreign-provider"), "Package refresh unsupported provider skipped");
            check(!f.Logs.Any(l => l.Contains("private-number") || l.Contains("DE5897460159")), "Package refresh diagnostics omit tracking numbers");
            int logs = f.Logs.Count; await f.Scheduler.EvaluateAsync();
            check(f.Logs.Count == logs && f.Provider.Gets.Count == 2, "Package refresh idle ticks are quiet");
            var restartProvider = new Provider(f.Clock);
            using (var restartService = new App.PackageTrackingService(new App.TrackingStore(f.PathName), restartProvider))
            using (var restarted = new App.PackageRefreshScheduler(restartService, () => f.Clock.Now, f.Logs.Add, false))
            {
                restarted.Configure(true, 30); await restarted.EvaluateAsync("startup");
                check(restartProvider.Gets.Count == 0 && restartProvider.Creates == 0, "Package refresh restart uses successful persisted timestamps");
            }
            f.Clock.Advance(240); f.Scheduler.SetPowerSuspended(true); await f.Scheduler.EvaluateAsync();
            check(f.Provider.Gets.Count == 2, "Package refresh suspended computer starts no requests");
            f.Scheduler.SetPowerSuspended(false); await f.Scheduler.EvaluateAsync("resume"); await f.Scheduler.EvaluateAsync("resume");
            check(f.Provider.Gets.Count == 5, "Package refresh four missed hours and duplicate resume signals produce one evaluation worth of requests");
            f.Clock.Advance(30); f.Scheduler.SetSuspended(true); await f.Scheduler.EvaluateAsync("timer");
            check(f.Provider.Gets.Count == 5, "Package refresh fullscreen blocks automatic requests");
            f.Scheduler.SetSuspended(false); await f.Scheduler.EvaluateAsync("fullscreen resume"); await f.Scheduler.EvaluateAsync("timer");
            check(f.Provider.Gets.Count == 8, "Package refresh fullscreen exit refreshes stale shipments once");
            check(f.Service.Shipments.Count == 7, "Package refresh lifecycle never deletes delivered shipments");
        }
        using (var f = new Fixture(root))
        {
            f.Store.Save(Shipment("one", null)); f.Scheduler.Configure(false, 30);
            await f.Scheduler.EvaluateAsync("startup");
            check(f.Provider.Gets.Count == 0, "Package refresh disabled automatic setting prevents requests");
            await f.Service.RefreshAsync();
            check(f.Provider.Gets.Count == 1, "Package refresh manual works with automatic disabled");
            f.Clock.Advance(20); f.Scheduler.Configure(true, 60); await f.Scheduler.EvaluateAsync("settings");
            check(f.Provider.Gets.Count == 1, "Package refresh longer interval preserves freshness");
            f.Scheduler.Configure(true, 15); await f.Scheduler.EvaluateAsync("settings");
            check(f.Provider.Gets.Count == 2, "Package refresh shorter interval replans immediately");
            f.Store.Save(f.Service.Shipments.Single() with { StatusMilestone = "delivered" });
            f.Clock.Advance(120); await f.Scheduler.EvaluateAsync();
            check(f.Provider.Gets.Count == 2, "Package refresh newly delivered stops automatic polling");
            await f.Service.RefreshAsync();
            check(f.Provider.Gets.Count == 3 && f.Provider.Creates == 0, "Package refresh delivered manual GET remains possible without POST");
        }
        foreach (var failure in new[] { App.TrackingProviderFailure.QuotaExhausted, App.TrackingProviderFailure.RateLimit,
            App.TrackingProviderFailure.Unauthorized, App.TrackingProviderFailure.InvalidCredential, App.TrackingProviderFailure.MissingCredential,
            App.TrackingProviderFailure.NetworkError, App.TrackingProviderFailure.Timeout })
        {
            using var f = new Fixture(root); var old = Shipment("failure", f.Clock.Now.AddHours(-1)); f.Store.Save(old);
            f.Provider.Get = (_, _) => throw new App.TrackingProviderException(failure);
            await f.Scheduler.EvaluateAsync("startup");
            check(f.Service.Shipments.Single().LastSuccessfulRefresh == old.LastSuccessfulRefresh && f.Service.Shipments.Single().StatusMilestone == old.StatusMilestone
                && f.Service.Shipments.Single().Events.Single().EventId == "keep", "Package refresh preserves cached status/events/time after " + failure);
            for (int i = 0; i < 8; i++) { f.Clock.Advance(1); await f.Scheduler.EvaluateAsync("resume"); }
            check(f.Provider.Gets.Count == 1 && f.Service.RefreshFailures[old.Id] == failure, "Package refresh categorized failure cannot cause retry storm " + failure);
            check(f.Logs.Any(l => l.Contains("provider failure: " + failure)), "Package refresh safe failure diagnostic " + failure);
            f.Clock.Advance(22); await f.Scheduler.EvaluateAsync();
            check(f.Provider.Gets.Count == 2 && f.Provider.Creates == 0, "Package refresh failure retries only after normal interval without POST " + failure);
        }
        foreach (string interruption in new[] { "disable", "fullscreen", "power", "dispose", "service-dispose", "none" })
        {
            using var f = new Fixture(root);
            f.Store.Save(Shipment("one", null)); f.Store.Save(Shipment("two", null));
            var pending = new TaskCompletionSource<App.TrackedShipment>();
            f.Provider.Get = (id, token) => id == "one" ? pending.Task.WaitAsync(token) : Task.FromResult(Shipment(id, f.Clock.Now));
            var cycle = f.Scheduler.EvaluateAsync("startup");
            check(f.Service.Busy && !cycle.IsCompleted, "Package refresh asynchronous in-flight request " + interruption);
            await Task.WhenAll(f.Scheduler.EvaluateAsync("timer"), f.Scheduler.EvaluateAsync("startup"), f.Scheduler.EvaluateAsync("resume"));
            check(f.Provider.Gets.Count == 1 && (await f.Service.RefreshAsync()).Outcome == App.PackageOperation.Busy,
                "Package refresh timer/startup/resume/manual collisions share service gate " + interruption);
            switch (interruption)
            {
                case "disable": f.Scheduler.Configure(false, 30); break;
                case "fullscreen": f.Scheduler.SetSuspended(true); break;
                case "power": f.Scheduler.SetPowerSuspended(true); break;
                case "dispose": f.Scheduler.Dispose(); break;
                case "service-dispose": f.Service.Dispose(); break;
            }
            pending.TrySetResult(Shipment("one", f.Clock.Now)); await cycle;
            check(f.Provider.Gets.Count == (interruption == "none" ? 2 : 1), "Package refresh checks policy before subsequent request " + interruption);
            check(!f.Service.Busy, "Package refresh shared gate released after " + interruption);
            f.Clock.Advance(120); await f.Scheduler.EvaluateAsync("resume");
            check(f.Provider.Gets.Count == (interruption == "none" ? 4 : 1), "Package refresh interrupted scheduler starts no future cycle " + interruption);
        }
        using (var f = new Fixture(root))
        {
            f.Store.Save(Shipment("one", null)); var pending = new TaskCompletionSource<App.TrackedShipment>();
            f.Provider.Get = (_, _) => pending.Task;
            var manual = f.Service.RefreshAsync(); await f.Scheduler.EvaluateAsync("startup");
            check(f.Provider.Gets.Count == 1, "Package refresh automatic does not duplicate running manual request");
            pending.SetResult(Shipment("one", f.Clock.Now)); await manual; await f.Scheduler.EvaluateAsync("resume");
            check(f.Provider.Gets.Count == 1, "Package refresh manual success makes queued evaluation fresh");
        }
    }

    private static void Ui(Action<bool, string> check, string root)
    {
        string key = @"Software\WallpaperControl.Tests\PackageRefresh\" + Guid.NewGuid().ToString("N");
        try
        {
            var defaults = App.WidgetSettings.Load(key);
            check(defaults.PackageAutomaticRefresh && defaults.PackageRefreshMinutes == 30, "Package refresh defaults enabled at 30 minutes for legacy settings");
            foreach (int interval in new[] { 15, 30, 60, 120, -1, 0, 1, 14, 16, 999 })
            {
                new App.WidgetSettings { PackageAutomaticRefresh = false, PackageRefreshMinutes = interval }.Save(key);
                var loaded = App.WidgetSettings.Load(key);
                check(!loaded.PackageAutomaticRefresh && loaded.PackageRefreshMinutes == (interval is 15 or 30 or 60 or 120 ? interval : 30)
                    && loaded.Clone().PackageRefreshMinutes == loaded.PackageRefreshMinutes, "Package refresh interval persistence normalization " + interval);
            }
            using (var registry = Registry.CurrentUser.OpenSubKey(key, true)) registry!.SetValue("PackageRefreshMinutes", 2);
            check(App.WidgetSettings.Load(key).PackageRefreshMinutes == 30, "Package refresh corrupt stored short interval safely resets to 30");
            foreach (string lang in new[] { "de", "en", "fr", "es", "ja" })
            {
                foreach (string resource in new[] { "PackageAutomaticRefresh", "PackageRefreshInterval", "PackageRefreshMinutesFormat" })
                    check(App.Localization.Get(resource, lang) != resource, "Package refresh localized resource " + lang + "/" + resource);
                foreach (int dpi in new[] { 96, 144, 192 })
                foreach (bool dark in new[] { false, true })
                {
                    using var form = new Form { ClientSize = new(1150, 760), Opacity = 0, AutoScaleMode = AutoScaleMode.None };
                    using var editor = new App.WidgetSettingsEditor(new() { PackageEnabled = true }, dark, lang) { Dock = DockStyle.Fill };
                    form.Controls.Add(editor); editor.SelectWidget("PackageTitle");
                    form.Scale(new SizeF(dpi / 96f, dpi / 96f)); form.Show(); Application.DoEvents();
                    var automatic = Field<CheckBox>(editor, "packageAutomatic"); var interval = Field<ComboBox>(editor, "packageInterval");
                    check(automatic.Checked && interval.Items.Count == 4 && interval.SelectedIndex == 1 && interval.Enabled,
                        $"Package refresh settings defaults {lang}/{dpi}/{dark}");
                    check(automatic.Width >= TextRenderer.MeasureText(automatic.Text, automatic.Font).Width && interval.Right <= interval.Parent!.ClientSize.Width
                        && !automatic.Bounds.IntersectsWith(interval.Bounds), $"Package refresh settings no clipped labels or overlap {lang}/{dpi}/{dark}");
                    automatic.Checked = false; interval.SelectedIndex = 3;
                    var read = editor.ReadWidgetSettings(false);
                    check(!read.PackageAutomaticRefresh && read.PackageRefreshMinutes == 120 && !interval.Enabled, $"Package refresh UI reads disabled and interval {lang}/{dpi}/{dark}");
                    editor.ApplyPresentation(!dark, lang);
                    check(interval.SelectedIndex == 3 && interval.Items[0]!.ToString() == string.Format(App.Localization.Get("PackageRefreshMinutesFormat", lang), 15),
                        $"Package refresh localization preserves interval {lang}/{dpi}/{dark}");
                    if (lang == "de" && dpi == 96 && dark) { using var shot = new Bitmap(form.Width, form.Height); form.DrawToBitmap(shot, new(Point.Empty, shot.Size)); shot.Save(Path.Combine(root, "settings.png")); }
                    form.Close();
                }
            }
            using var f = new Fixture(root);
            using var manager = new App.WidgetManager(() => { }, registryPath: key, notesStore: new App.NotesStore(Path.Combine(root, "notes.json")), packageTrackingService: f.Service);
            var preview = manager.Settings; preview.PackageAutomaticRefresh = true; preview.PackageRefreshMinutes = 60;
            manager.Preview(preview);
            check(manager.Settings.PackageAutomaticRefresh && manager.Settings.PackageRefreshMinutes == 60 && !App.WidgetSettings.Load(key).PackageAutomaticRefresh,
                "Package refresh settings preview updates runtime without saving");
            manager.CommitPreview(preview);
            check(App.WidgetSettings.Load(key).PackageRefreshMinutes == 60 && App.WidgetSettings.Load(key).PackageAutomaticRefresh, "Package refresh settings commit persists");
            manager.CancelPreview(new() { PackageAutomaticRefresh = false, PackageRefreshMinutes = 120 });
            check(!manager.Settings.PackageAutomaticRefresh && manager.Settings.PackageRefreshMinutes == 120, "Package refresh discard restores runtime configuration");
            manager.Preview(new() { PackageEnabled = true, PackageAutomaticRefresh = true });
            var ownedScheduler = Field<App.PackageRefreshScheduler>(manager, "packageScheduler");
            check(Field<System.Windows.Forms.Timer>(ownedScheduler, "timer").Enabled, "Package refresh widget enable owns active scheduler");
            manager.SetActivitySuspended(true);
            check(Field<bool>(ownedScheduler, "suspended"), "Package refresh uses existing fullscreen activity policy");
            manager.SetPackagePowerSuspended(true);
            check(Field<bool>(ownedScheduler, "powerSuspended"), "Package refresh manager forwards power suspend");
            manager.SetPackagePowerSuspended(false);
            check(!Field<bool>(ownedScheduler, "powerSuspended") && Field<bool>(ownedScheduler, "suspended"), "Package refresh power resume cannot clear fullscreen pause");
            manager.SetActivitySuspended(false);
            check(!Field<bool>(ownedScheduler, "suspended"), "Package refresh fullscreen exit resumes planning");
            manager.Preview(new() { PackageEnabled = true, PackageAutomaticRefresh = true, PackageRefreshMinutes = 15 });
            check(ReferenceEquals(ownedScheduler, Field<App.PackageRefreshScheduler>(manager, "packageScheduler"))
                && Field<int>(ownedScheduler, "intervalMinutes") == 15, "Package refresh changing settings replans same scheduler");
            manager.Preview(new() { PackageEnabled = true, PackageAutomaticRefresh = false });
            check(Field<object?>(ownedScheduler, "timer") == null, "Package refresh runtime disable stops timer");
            manager.Preview(new() { PackageEnabled = false });
            check(Field<bool>(ownedScheduler, "disposed") && Field<object?>(manager, "packageScheduler") == null, "Package refresh widget disable disposes scheduler");
            manager.Preview(new() { PackageEnabled = true });
            ownedScheduler = Field<App.PackageRefreshScheduler>(manager, "packageScheduler");
            Field<App.PackageTrackingWidgetForm>(manager, "packageWidget").Dispose();
            check(Field<bool>(ownedScheduler, "disposed") && Field<object?>(manager, "packageScheduler") == null,
                "Package refresh direct widget disposal releases owned scheduler");
            using var timed = new App.PackageRefreshScheduler(f.Service, () => f.Clock.Now, f.Logs.Add);
            timed.Configure(true, 30);
            check(Field<System.Windows.Forms.Timer>(timed, "timer").Enabled && f.Provider.Gets.Count == 0, "Package refresh startup schedules asynchronously without inline networking");
            timed.Configure(false, 30);
            check(Field<object?>(timed, "timer") == null, "Package refresh disabling releases timer");
            timed.Configure(true, 15); timed.Dispose(); timed.Dispose();
            check(Field<object?>(timed, "timer") == null, "Package refresh disposal is idempotent and releases timer");
            using var serviceOwned = new App.PackageRefreshScheduler(f.Service, () => f.Clock.Now, f.Logs.Add);
            serviceOwned.Configure(true, 30); f.Service.Dispose();
            check(Field<object?>(serviceOwned, "timer") == null && Field<bool>(serviceOwned, "disposed"), "Package refresh service disposal releases subscribed scheduler timer");
            using var uiFixture = new Fixture(root);
            uiFixture.Store.Save(Shipment("widget", null));
            using var widget = new App.PackageTrackingWidgetForm(uiFixture.Service, new(), _ => { }, _ => { });
            var pending = new TaskCompletionSource<App.TrackedShipment>();
            uiFixture.Provider.Get = (_, _) => pending.Task;
            var cycle = uiFixture.Scheduler.EvaluateAsync("startup");
            typeof(App.PackageTrackingWidgetForm).GetField("pressed", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(widget, (Guid?)Guid.Empty);
            typeof(App.PackageTrackingWidgetForm).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(widget,
                new object[] { new MouseEventArgs(MouseButtons.Left, 1, 355, 20, 0) });
            check(Field<string?>(widget, "error") == null && uiFixture.Provider.Gets.Count == 1, "Package refresh busy widget click does not leave a false error or duplicate request");
            typeof(App.PackageTrackingWidgetForm).GetField("error", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(widget, "Previous manual error");
            pending.SetResult(Shipment("widget", uiFixture.Clock.Now));
            while (!cycle.IsCompleted) { Application.DoEvents(); Thread.Sleep(1); }
            cycle.GetAwaiter().GetResult();
            check(Field<string?>(widget, "error") == null, "Package refresh automatic success clears stale manual footer error");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(key, false); }
    }
}
