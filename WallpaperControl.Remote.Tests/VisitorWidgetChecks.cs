using System.Net;
using WallpaperControl.Remote;

internal static partial class Program
{
    private static RemoteState WidgetState(bool active, bool allowOff = true, long revision = 1) => new()
    { Epoch = "test", Revision = revision, CapturedAt = DateTimeOffset.UtcNow, VisitorActive = active, CanDisableVisitor = allowOff,
        Capabilities = new(["visitorOn", .. allowOff ? new[] { "visitorOff" } : Array.Empty<string>()], [], []) };
    private sealed class WidgetFake : IVisitorWidgetConnection
    {
        public RemoteState Current = WidgetState(false);
        public readonly List<string> Commands = [];
        public TaskCompletionSource? Hold;
        public bool Fail, FailRead, RejectResult;
        public int Disposals;
        public Task<RemoteState> ReadAsync(CancellationToken cancellation) => FailRead ? Task.FromException<RemoteState>(new HttpRequestException()) : Task.FromResult(Current);
        public async Task<RemoteResult> SendAsync(string command, CancellationToken cancellation)
        {
            Commands.Add(command);
            if (Hold != null) await Hold.Task.WaitAsync(cancellation);
            if (Fail) throw new HttpRequestException();
            if (RejectResult) return new("test", "failed", "operationFailed", Current);
            Current = Current with { VisitorActive = command == "visitorOn", Revision = Current.Revision + 1, CapturedAt = DateTimeOffset.UtcNow };
            return new("test", "completed", null, Current);
        }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
    private static async Task VisitorWidgetChecks()
    {
        var lifecycleTime = new FakeTime();
        var lifecycle = new VisitorWidgetController(_ => Task.FromResult<IVisitorWidgetConnection?>(null), lifecycleTime);
        var foregroundState = WidgetState(true);
        lifecycle.SessionChanged(foregroundState, true, true, true);
        long confirmedAt = lifecycle.State.ConfirmedAt;
        lifecycleTime.Milliseconds = 5000;
        lifecycle.SessionChanged(foregroundState, true, true, false);
        lifecycle.SessionChanged(null, false, true, false);
        Check(lifecycle.Fresh && lifecycle.State.Active && !lifecycle.State.Live && lifecycle.State.ConfirmedAt == confirmedAt,
            "returning to launcher and intentional hub disposal preserve original widget confirmation");
        lifecycleTime.Milliseconds = 30000;
        Check(!lifecycle.Fresh && !lifecycle.CanRequestDisable, "background session notifications never extend widget confirmation deadline");
        lifecycle.Expire();
        Check(lifecycle.State.Status == VisitorWidgetStatus.Unknown && lifecycle.State.Active && !lifecycle.CanRequestDisable,
            "expired snapshot retains historical value without current confirmation or off permission");
        lifecycle.SessionChanged(foregroundState with { Revision = 2, CapturedAt = DateTimeOffset.UtcNow }, true, true, true);
        Check(lifecycle.Fresh && lifecycle.State.Live, "reopened connected app supplies a new confirmed widget state");
        lifecycle.SessionChanged(null, false, true, true);
        Check(!lifecycle.Fresh && lifecycle.State.Status == VisitorWidgetStatus.Unknown, "real foreground connection loss still clears confirmation");
        lifecycle.SessionChanged(null, false, false, false);
        Check(lifecycle.State.Status == VisitorWidgetStatus.Unpaired, "forgetting pairing while backgrounded still clears all widget state");
        var fake = new WidgetFake(); var time = new FakeTime();
        var controller = new VisitorWidgetController(_ => Task.FromResult<IVisitorWidgetConnection?>(fake), time);
        int updatesA = 0, updatesB = 0; controller.Changed += () => updatesA++; controller.Changed += () => updatesB++;
        Check(!controller.Fresh && !controller.CanRequestDisable, "widget cold process starts unknown and cannot disable");
        Check(await controller.ActivateAsync(default) && fake.Commands.SequenceEqual(["visitorOn"]) && controller.State.Active, "unknown widget explicitly activates, never toggles");
        controller.SessionChanged(null, false, true, true);
        Check(controller.Fresh && controller.State.Active && !controller.State.Live,
            "unrelated app hub disconnect cannot erase a confirmed widget REST result");
        controller.Disconnected(liveSessionOnly: true);
        Check(controller.Fresh && controller.State.Active,
            "late live-session disconnect cannot overwrite independent REST confirmation");
        Check(updatesA == updatesB && updatesA > 0, "all widget observers receive identical state transitions");
        Check(await controller.ActivateAsync(default) && fake.Commands.Count == 1 && controller.State.Active, "repeated activation of active visitor is idempotent and cannot deactivate");
        Check(!await controller.DeactivateAsync(false, default) && fake.Commands.Count == 1, "widget deactivation requires explicit dialog confirmation");
        time.Milliseconds = 30000;
        Check(!controller.Fresh && !controller.CanRequestDisable && !await controller.DeactivateAsync(true, default), "expired confirmed state cannot offer or execute deactivation");
        controller.Expire(); Check(controller.State.Status == VisitorWidgetStatus.Unknown, "expired widget state becomes explicitly unknown");
        Check(await controller.ActivateAsync(default) && fake.Commands.Count == 1, "stale gesture only attempts activation even if PC is already active");
        fake.Current = WidgetState(true, false, 2); controller.Observe(fake.Current);
        Check(!controller.CanRequestDisable && !await controller.DeactivateAsync(true, default), "device without visitor disable permission cannot deactivate");
        fake.Current = WidgetState(true, true, 3); controller.Observe(fake.Current);
        fake.Current = WidgetState(true, false, 4);
        Check(!await controller.DeactivateAsync(true, default) && fake.Commands.Count == 1, "permission revoked after dialog opening is rechecked on server snapshot");
        fake.Current = WidgetState(true, true, 5); controller.Observe(fake.Current);
        Check(await controller.DeactivateAsync(true, default) && fake.Commands.Last() == "visitorOff" && !controller.State.Active, "confirmed authorized off uses explicit visitorOff and waits for result");
        controller.Observe(WidgetState(false, true, 4)); Check(controller.State.Status == VisitorWidgetStatus.Confirmed && !controller.State.Active, "older app snapshot cannot overwrite newer widget result");
        fake.Current = WidgetState(false, true, 7); fake.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = controller.ActivateAsync(default);
        Check(controller.State.Status == VisitorWidgetStatus.Activating && !controller.Fresh && !controller.State.Active, "pending activation never reports success before server confirmation");
        int before = fake.Commands.Count;
        var concurrent = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => controller.ActivateAsync(default)));
        Check(concurrent.All(v => !v) && fake.Commands.Count == before, "twenty rapid taps across instances execute no duplicate mutation");
        fake.Hold.SetResult(); Check(await running && controller.State.Active, "actual completion confirms activation for every widget"); fake.Hold = null;
        controller.Disconnected(); Check(!controller.Fresh && !controller.State.Active && !controller.CanRequestDisable, "disconnect removes active confirmation and disable affordance");
        fake.Current = WidgetState(false, true, 9); fake.Fail = true;
        Check(!await controller.ActivateAsync(default) && controller.State.Status == VisitorWidgetStatus.Unreachable && !controller.State.Active, "interrupted mutation has no false active success");
        before = fake.Commands.Count; fake.Fail = false;
        Check(await controller.RefreshAsync(default) && fake.Commands.Count == before, "restored connection refreshes only and never replays an offline command");
        fake.FailRead = true;
        Check(!await controller.ActivateAsync(default) && fake.Commands.Count == before, "unreachable PC never receives a queued activation"); fake.FailRead = false;
        fake.RejectResult = true;
        Check(!await controller.ActivateAsync(default) && controller.State.Status == VisitorWidgetStatus.Failed, "server failure cannot be shown as successful activation"); fake.RejectResult = false;
        fake.Current = WidgetState(false) with { Capabilities = RemoteCapabilities.Empty };
        Check(!await controller.ActivateAsync(default) && controller.State.Status == VisitorWidgetStatus.Failed, "missing PC visitor source/capability produces useful failure");
        fake.Current = WidgetState(false); fake.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var deadline = new CancellationTokenSource(30)) Check(!await controller.ActivateAsync(deadline.Token) && !controller.Fresh, "bounded cancellation cannot leave widget marked successful");
        fake.Hold = null;
        var cold = new VisitorWidgetController(_ => Task.FromResult<IVisitorWidgetConnection?>(fake));
        Check(!await cold.DeactivateAsync(true, default) && await cold.ActivateAsync(default), "fresh coordinator after simulated process death rejects old off intent and can activate");
        var unpaired = new VisitorWidgetController(_ => Task.FromResult<IVisitorWidgetConnection?>(null));
        Check(!await unpaired.ActivateAsync(default) && unpaired.State.Status == VisitorWidgetStatus.Unpaired, "missing shared pairing routes widget to setup state");
        controller.Invalidate(); Check(!controller.CanRequestDisable && controller.State.Status == VisitorWidgetStatus.Unknown, "pairing change invalidates all widget confirmation");
        var delayedOpen = new TaskCompletionSource<IVisitorWidgetConnection?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var changedPairing = new VisitorWidgetController(_ => delayedOpen.Task);
        var delayedActivation = changedPairing.ActivateAsync(default); int commandsBeforeForget = fake.Commands.Count;
        changedPairing.Invalidate(unpaired: true); delayedOpen.SetResult(fake);
        Check(!await delayedActivation && fake.Commands.Count == commandsBeforeForget && changedPairing.State.Status == VisitorWidgetStatus.Unpaired,
            "forgetting pairing during connection prevents the pending command from being sent");
        controller.Observe(WidgetState(true, true, 50) with { ErrorCode = "visitorError" });
        Check(!controller.Fresh && !controller.CanRequestDisable && controller.State.Status == VisitorWidgetStatus.Failed,
            "foreground app visitor error cannot create a green confirmed widget");
        using (var canceled = new CancellationTokenSource())
        { canceled.Cancel(); Check(!await controller.ActivateAsync(canceled.Token), "already canceled widget request returns safely without sending"); }

        // Actual product REST/pinning/auth stack, without constructing or starting a SignalR connection.
        var vault = new RemoteVault(Path.Combine(Root, "widget-rest")); using var identity = vault.Identity();
        var credential = vault.Add("Widget", true); var state = WidgetState(false); int mutations = 0;
        await using var server = new RemoteServer(vault, () => Task.FromResult(state), (command, _) =>
        { mutations++; state = state with { VisitorActive = command.Type == "visitorOn" }; return Task.FromResult(new RemoteOutcome(true)); }, IPAddress.IsLoopback);
        await server.StartAsync([IPAddress.Loopback], 0, identity);
        var actual = new VisitorWidgetController(async cancellation =>
        {
            var connection = new RemoteConnection(Saved(server, identity, credential), true);
            await connection.ConnectRestAsync(cancellation); return new WidgetReal(connection);
        });
        Check(await actual.ActivateAsync(default) && actual.State.Active && !server.HasConnectedClients && mutations == 1, "widget activates over real pinned authenticated HTTPS with zero WSS sessions");
        Check(await actual.ActivateAsync(default) && mutations == 1, "real REST repeated activation has no further mutation");
        Check(await actual.DeactivateAsync(true, default) && !actual.State.Active && mutations == 2, "real REST widget confirms authorized deactivation");
        vault.Change(credential.DeviceId, "Widget", false);
        actual.Observe(state with { VisitorActive = true, CanDisableVisitor = true, Revision = 999, CapturedAt = DateTimeOffset.UtcNow });
        Check(!await actual.DeactivateAsync(true, default) && mutations == 2, "real revoked right blocks widget off before mutation");
        vault.Remove(credential.DeviceId);
        Check(!await actual.ActivateAsync(default) && !actual.Fresh, "revoked real device credential fails closed without mutation");
    }
    private sealed class WidgetReal(RemoteConnection connection) : IVisitorWidgetConnection
    {
        public Task<RemoteState> ReadAsync(CancellationToken cancellation) => connection.RefreshAsync(cancellation);
        public Task<RemoteResult> SendAsync(string command, CancellationToken cancellation) => connection.SendAsync(command, cancellation: cancellation);
        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
}
