using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using System.Collections.Concurrent;

namespace WallpaperControl.Remote.AndroidApp;

internal static class WidgetRuntime
{
    internal const string Tap = "de.wallpapercontrol.remote.widget.TAP";
    internal const string Expire = "de.wallpapercontrol.remote.widget.EXPIRE";
    internal static readonly VisitorWidgetController Controller = new(OpenAsync);
    private static Context Context => global::Android.App.Application.Context;
    private static DateTimeOffset confirmedWallTime;
    private static long lastTimestamp = long.MinValue;
    private static long expiryScheduledAt;
    internal static string LastRenderError { get; private set; } = "";
    private static ISharedPreferences DiagnosticStore => Context.GetSharedPreferences("widget-diagnostics", FileCreationMode.Private)!;
    internal static string Diagnostics => $"Kompakt: {Ids(typeof(CompactVisitorWidget)).Length}, Schalter: {Ids(typeof(SwitchVisitorWidget)).Length}\nZustand jetzt: {Controller.State.Status}\nAktualisierung jetzt: {(LastRenderError.Length == 0 ? "ohne gemeldeten Fehler" : LastRenderError)}\n\n{DiagnosticStore.GetString("last-attempt", "Noch kein Widget-Aufruf protokolliert")}";
    static WidgetRuntime() { Controller.Changed += RenderAll; }
    internal static bool HasWidgets => Ids(typeof(CompactVisitorWidget)).Length + Ids(typeof(SwitchVisitorWidget)).Length > 0;
    private static int[] Ids(Type type) => AppWidgetManager.GetInstance(Context)!.GetAppWidgetIds(new ComponentName(Context.PackageName!,
        type == typeof(CompactVisitorWidget) ? "de.wallpapercontrol.remote.CompactVisitorWidget" : "de.wallpapercontrol.remote.SwitchVisitorWidget")) ?? [];
    internal static void Observe(RemoteState? state, bool connected, bool paired, bool foreground)
    {
        if (!HasWidgets) return;
        Controller.SessionChanged(state, connected, paired, foreground);
    }
    internal static void RenderAll()
    {
        if (!MainThread.IsMainThread) { MainThread.BeginInvokeOnMainThread(RenderAll); return; }
        try
        {
            var manager = AppWidgetManager.GetInstance(Context)!;
            var state = Controller.State;
            if (state.Status == VisitorWidgetStatus.Confirmed && state.ConfirmedAt != lastTimestamp)
            {
                lastTimestamp = state.ConfirmedAt; confirmedWallTime = DateTimeOffset.Now;
                // Alarm delivery is auxiliary. Its failure must never prevent the actual widget update.
                try { ScheduleExpiry(); } catch { }
            }
            LastRenderError = "";
            foreach (var type in new[] { typeof(CompactVisitorWidget), typeof(SwitchVisitorWidget) })
                foreach (int id in Ids(type)) manager.UpdateAppWidget(id, Views(id, type == typeof(CompactVisitorWidget)));
        }
        catch (Exception ex) { LastRenderError = ex.GetType().Name; } // No exception text, identity or credential data.
    }
    private static RemoteViews Views(int id, bool compact)
    {
        var state = Controller.State;
        bool fresh = Controller.Fresh, busy = Controller.Busy;
        bool confirmed = fresh && state.Status == VisitorWidgetStatus.Confirmed;
        bool active = confirmed && state.Active;
        string status = state.Status switch
        {
            VisitorWidgetStatus.Unpaired => "Nicht gekoppelt",
            VisitorWidgetStatus.Activating => "Wird aktiviert …",
            VisitorWidgetStatus.Deactivating => "Wird deaktiviert …",
            VisitorWidgetStatus.Unreachable => "Nicht erreichbar",
            VisitorWidgetStatus.Failed => "Nicht bestätigt",
            _ => confirmed ? active ? "Zuletzt aktiv" : "Zuletzt inaktiv" : state.ConfirmedAt != 0 ? "Veraltet" : "Unbekannt"
        };
        // Always timestamp confirmations: even delayed alarms never promise a permanently live state.
        string detail = confirmed ? $"{(compact ? "" : "Stand ")}{confirmedWallTime:HH:mm}" :
            state.Status == VisitorWidgetStatus.Unknown && state.ConfirmedAt != 0
                ? $"{(state.Active ? "aktiv" : "inaktiv")} · {confirmedWallTime:HH:mm}" : state.Detail;
        var views = new RemoteViews(Context.PackageName, compact ? Resource.Layout.visitor_compact : Resource.Layout.visitor_switch);
        if (compact)
        {
            var options = AppWidgetManager.GetInstance(Context)!.GetAppWidgetOptions(id);
            bool roomForTitle = (options?.GetInt(AppWidgetManager.OptionAppwidgetMinWidth) ?? 0) >= 100 ||
                (options?.GetInt(AppWidgetManager.OptionAppwidgetMinHeight) ?? 0) >= 130;
            views.SetViewVisibility(Resource.Id.visitor_title, roomForTitle ? ViewStates.Visible : ViewStates.Gone);
        }
        views.SetTextViewText(Resource.Id.visitor_status, status);
        views.SetTextViewText(Resource.Id.visitor_detail, detail);
        views.SetImageViewResource(Resource.Id.visitor_shield, active ? Resource.Drawable.visitor_shield_active :
            confirmed ? Resource.Drawable.visitor_shield_blue : state.Status == VisitorWidgetStatus.Unreachable ? Resource.Drawable.visitor_shield_gray : Resource.Drawable.visitor_shield_neutral);
        views.SetViewVisibility(Resource.Id.visitor_progress, busy ? ViewStates.Visible : ViewStates.Gone);
        if (!compact) views.SetImageViewResource(Resource.Id.visitor_toggle, active ? Resource.Drawable.visitor_toggle_on : Resource.Drawable.visitor_toggle_off);
        // Resolve the gesture at receipt time, never encode an old toggle/off decision in an intent.
        var action = PendingIntent.GetBroadcast(Context, id, new Intent(Context, typeof(VisitorWidgetActionReceiver)).SetAction(Tap)
            .AddFlags(ActivityFlags.ReceiverForeground), PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        // Keep the wake-up intent even while busy: after process death a cached spinner must not trap the user.
        // The receiver/controller gates ignore duplicate taps in the running process.
        views.SetOnClickPendingIntent(Resource.Id.visitor_root, action);
        if (!compact) views.SetOnClickPendingIntent(Resource.Id.visitor_toggle, action);
        views.SetContentDescription(Resource.Id.visitor_root, "Besuchermodus. " + status + ". " + detail + (Controller.CanRequestDisable ? ". Ausschalten mit Bestätigung" : ". Antippen zum Aktivieren"));
        return views;
    }
    internal static void ScheduleExpiry()
    {
        long now = SystemClock.ElapsedRealtime();
        if (expiryScheduledAt > now) return;
        expiryScheduledAt = now + 30000;
        var pending = PendingIntent.GetBroadcast(Context, 706, new Intent(Context, typeof(VisitorWidgetActionReceiver)).SetAction(Expire), PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
        ((AlarmManager)Context.GetSystemService(Context.AlarmService)!).Set(AlarmType.ElapsedRealtime, expiryScheduledAt, pending);
    }
    internal static void Run(BroadcastReceiver receiver, bool activate)
    {
        var pending = receiver.GoAsync();
        _ = RunAsync();
        async Task RunAsync()
        {
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            // Broadcast processing (including cold start) stays below Android's 10-second boundary.
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(7));
            try
            {
                bool completed = activate ? await Controller.ActivateAsync(deadline.Token) : await Controller.RefreshAsync(deadline.Token);
                // Keep the broadcast alive until the final native update has actually been submitted.
                await MainThread.InvokeOnMainThreadAsync(RenderAll);
                if (activate && completed && Controller.Fresh && Controller.State.Active)
                    await MainThread.InvokeOnMainThreadAsync(() => Toast.MakeText(Context,
                        Controller.State.CanDisable ? "Besuchermodus aktiv. Zum Ausschalten erneut tippen." :
                        "Besuchermodus aktiv. Ausschalten ist am PC nicht freigegeben.", ToastLength.Long)?.Show());
                if (activate && Controller.State.Status == VisitorWidgetStatus.Unpaired) OpenSetup();
                if (activate && Controller.State.Status is VisitorWidgetStatus.Failed or VisitorWidgetStatus.Unreachable)
                    MainThread.BeginInvokeOnMainThread(() => Toast.MakeText(Context, Controller.State.Detail, ToastLength.Long)?.Show());
            }
            catch { Controller.Disconnected(); RenderAll(); }
            finally
            {
                try
                {
                    // Completed diagnostic only: no identity, address, credential or replayable command.
                    string record = $"Letzter Widget-Aufruf {DateTimeOffset.Now:HH:mm:ss}\nDauer: {elapsed.Elapsed.TotalSeconds:F1} s\nErgebnis: {Controller.State.Status}\n{Controller.State.Detail}\nAnzeige: {(LastRenderError.Length == 0 ? "ohne gemeldeten Fehler" : LastRenderError)}";
                    DiagnosticStore.Edit()!.PutString("last-attempt", record)!.Apply();
                }
                catch { }
                pending?.Finish();
            }
        }
    }
    internal static void OpenSetup() => Context.StartActivity(new Intent(Context, typeof(MainActivity)).AddFlags(ActivityFlags.NewTask));
    internal static void ShowDisableDialog()
    {
        // A direct launcher-widget gesture is Android's user-interaction exception for activity starts.
        try { Context.StartActivity(new Intent(Context, typeof(VisitorOffActivity)).AddFlags(ActivityFlags.NewTask | ActivityFlags.ExcludeFromRecents)); }
        catch { Toast.MakeText(Context, "Bestätigung konnte nicht geöffnet werden. Bitte die Remote-App verwenden.", ToastLength.Long)?.Show(); }
    }
    private static async Task<IVisitorWidgetConnection?> OpenAsync(CancellationToken cancellation)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(37) && Context.CheckSelfPermission("android.permission.ACCESS_LOCAL_NETWORK") != Permission.Granted)
            throw new InvalidOperationException("Local network permission required");
        var saved = await RemoteCredentialStore.ReadAsync().WaitAsync(cancellation);
        if (saved == null) return null;
        var found = new ConcurrentQueue<string>();
        using var foundSignal = new SemaphoreSlim(0, 16);
        var discovery = new NsdDiscovery(saved.Server.ServerId);
        discovery.Found += address => { if (found.Count < 16) { found.Enqueue(address); try { foundSignal.Release(); } catch (Exception ex) when (ex is SemaphoreFullException or ObjectDisposedException) { } } };
        try
        {
            await MainThread.InvokeOnMainThreadAsync(() => { try { discovery.Start(); } catch { } });
            var addresses = new Queue<string>(new[] { saved.Address }.Concat(saved.Server.Addresses).Distinct().Take(16));
            var tried = new HashSet<string>();
            for (int attempt = 0; attempt < 3; attempt++)
            {
                while (found.TryDequeue(out var discovered)) if (!tried.Contains(discovered)) addresses.Enqueue(discovered);
                if (attempt > 0 && found.IsEmpty) await foundSignal.WaitAsync(TimeSpan.FromMilliseconds(300), cancellation);
                string? address = null;
                // Prefer newly discovered candidates after a failed saved address.
                while (found.TryDequeue(out var discovered)) if (tried.Add(discovered)) { address = discovered; break; }
                while (address == null && addresses.TryDequeue(out var candidate)) if (tried.Add(candidate)) address = candidate;
                if (address == null) break;
                RemoteConnection? connection = null;
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromMilliseconds(1400));
                    connection = new(saved with { Address = AndroidAddress.Normalize(address) });
                    await connection.ConnectRestAsync(timeout.Token);
                    return new WidgetConnection(connection);
                }
                catch (Exception) when (!cancellation.IsCancellationRequested) { if (connection != null) await connection.DisposeAsync(); }
                catch { if (connection != null) await connection.DisposeAsync(); throw; }
            }
            throw new HttpRequestException("Unavailable");
        }
        finally { await MainThread.InvokeOnMainThreadAsync(discovery.Stop); }
    }
    private sealed class WidgetConnection(RemoteConnection connection) : IVisitorWidgetConnection
    {
        public Task<RemoteState> ReadAsync(CancellationToken cancellation) => connection.RefreshAsync(cancellation);
        public Task<RemoteResult> SendAsync(string command, CancellationToken cancellation) => connection.SendAsync(command, cancellation: cancellation);
        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
}

[BroadcastReceiver(Name = "de.wallpapercontrol.remote.CompactVisitorWidget", Label = "Besuchermodus · Kompakt (1 × 1)", Exported = false)]
[IntentFilter(["android.appwidget.action.APPWIDGET_UPDATE"])]
[MetaData("android.appwidget.provider", Resource = "@xml/visitor_compact_info")]
public sealed class CompactVisitorWidget : AppWidgetProvider
{
    public override void OnUpdate(Context? context, AppWidgetManager? manager, int[]? ids) { WidgetRuntime.RenderAll(); WidgetRuntime.Run(this, false); }
    public override void OnAppWidgetOptionsChanged(Context? context, AppWidgetManager? manager, int id, Bundle? options) => WidgetRuntime.RenderAll();
}
[BroadcastReceiver(Name = "de.wallpapercontrol.remote.SwitchVisitorWidget", Label = "Besuchermodus · Schalter (2 × 1)", Exported = false)]
[IntentFilter(["android.appwidget.action.APPWIDGET_UPDATE"])]
[MetaData("android.appwidget.provider", Resource = "@xml/visitor_switch_info")]
public sealed class SwitchVisitorWidget : AppWidgetProvider
{
    public override void OnUpdate(Context? context, AppWidgetManager? manager, int[]? ids) { WidgetRuntime.RenderAll(); WidgetRuntime.Run(this, false); }
    public override void OnAppWidgetOptionsChanged(Context? context, AppWidgetManager? manager, int id, Bundle? options) => WidgetRuntime.RenderAll();
}
[BroadcastReceiver(Name = "de.wallpapercontrol.remote.VisitorWidgetActionReceiver", Exported = false)]
public sealed class VisitorWidgetActionReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action == WidgetRuntime.Expire)
        {
            WidgetRuntime.Controller.Expire(); WidgetRuntime.RenderAll();
            if (WidgetRuntime.Controller.Fresh) WidgetRuntime.ScheduleExpiry();
        }
        else if (intent?.Action == WidgetRuntime.Tap && WidgetRuntime.HasWidgets)
        {
            if (WidgetRuntime.Controller.Busy) return;
            if (WidgetRuntime.Controller.CanRequestDisable) WidgetRuntime.ShowDisableDialog();
            else if (WidgetRuntime.Controller.State.Status == VisitorWidgetStatus.Unpaired) WidgetRuntime.OpenSetup();
            else WidgetRuntime.Run(this, true);
        }
    }
}
