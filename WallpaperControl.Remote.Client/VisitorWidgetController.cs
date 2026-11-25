namespace WallpaperControl.Remote;

public enum VisitorWidgetStatus { Unknown, Unpaired, Unreachable, Confirmed, Activating, Deactivating, Failed }
public sealed record VisitorWidgetState(VisitorWidgetStatus Status, bool Active = false, bool CanDisable = false,
    long ConfirmedAt = 0, string Detail = "", bool Live = false);
public interface IVisitorWidgetConnection : IAsyncDisposable
{
    Task<RemoteState> ReadAsync(CancellationToken cancellation);
    Task<RemoteResult> SendAsync(string command, CancellationToken cancellation);
}

// No persisted commands, no retries of a mutation, and one gate shared by every widget instance.
public sealed class VisitorWidgetController(Func<CancellationToken, Task<IVisitorWidgetConnection?>> open, TimeProvider? clock = null)
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object stateGate = new();
    private long generation;
    private RemoteState? lastConfirmed;
    public static readonly TimeSpan FreshFor = TimeSpan.FromSeconds(30);
    public VisitorWidgetState State { get; private set; } = new(VisitorWidgetStatus.Unknown);
    public bool Busy => gate.CurrentCount == 0;
    public event Action? Changed;
    public bool Fresh { get { lock (stateGate) return State.Status == VisitorWidgetStatus.Confirmed && time.GetElapsedTime(State.ConfirmedAt) < FreshFor; } }
    public bool CanRequestDisable => Fresh && State.Active && State.CanDisable && !Busy;
    private void Set(VisitorWidgetState state) { lock (stateGate) State = state; Changed?.Invoke(); }
    public void Invalidate(bool unpaired = false)
    { Interlocked.Increment(ref generation); lock (stateGate) lastConfirmed = null; Set(new(unpaired ? VisitorWidgetStatus.Unpaired : VisitorWidgetStatus.Unknown)); }
    public void Observe(RemoteState state, bool live = false)
    {
        if (Busy) return;
        lock (stateGate)
        {
            if (lastConfirmed is { } previous && previous.Epoch == state.Epoch &&
                (state.Revision < previous.Revision || state.Revision == previous.Revision && state.CapturedAt <= previous.CapturedAt)) return;
            lastConfirmed = state;
        }
        if (state.ErrorCode != null) { Set(new(VisitorWidgetStatus.Failed, Detail: "Wiedergabe am PC prüfen")); return; }
        Set(new(VisitorWidgetStatus.Confirmed, state.VisitorActive, state.CanDisableVisitor && state.Capabilities.Allows("visitorOff"), time.GetTimestamp(), Live: live));
    }
    public void Disconnected(bool liveSessionOnly = false)
    {
        // A closed/failed app hub is not evidence against a newer independent widget REST confirmation.
        if (!Busy && (!liveSessionOnly || State.Live)) Set(new(VisitorWidgetStatus.Unknown));
    }
    public void SessionChanged(RemoteState? state, bool connected, bool paired, bool foreground)
    {
        if (!paired) { Invalidate(unpaired: true); return; }
        if (!foreground)
        {
            // Closing the foreground hub is intentional, not evidence that the PC became unavailable.
            // Preserve the original confirmation deadline; backgrounding must never renew it.
            if (!Busy && State.Live) Set(State with { Live = false });
            return;
        }
        if (connected && state != null) Observe(state, live: true); else Disconnected(liveSessionOnly: true);
    }
    public void Expire() { if (!Fresh && !Busy && State.Status == VisitorWidgetStatus.Confirmed) Set(State with { Status = VisitorWidgetStatus.Unknown, Detail = "Status veraltet" }); }
    public Task<bool> RefreshAsync(CancellationToken cancellation) => RunAsync(null, false, cancellation);
    public Task<bool> ActivateAsync(CancellationToken cancellation) => RunAsync("visitorOn", false, cancellation);
    public Task<bool> DeactivateAsync(bool confirmed, CancellationToken cancellation) => RunAsync("visitorOff", confirmed, cancellation);
    private async Task<bool> RunAsync(string? command, bool confirmed, CancellationToken cancellation)
    {
        // A stale old PendingIntent can never authorize deactivation, including after process death.
        if (command == "visitorOff" && (!confirmed || !CanRequestDisable)) return false;
        if (!await gate.WaitAsync(0)) return false;
        long requestGeneration = Interlocked.Read(ref generation);
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (command != null) Set(new(command == "visitorOn" ? VisitorWidgetStatus.Activating : VisitorWidgetStatus.Deactivating));
            await using var connection = await open(cancellation);
            if (connection == null) { Set(new(VisitorWidgetStatus.Unpaired)); return false; }
            var current = await connection.ReadAsync(cancellation);
            if (requestGeneration != Interlocked.Read(ref generation)) return false;
            if (command == "visitorOff" && (!current.VisitorActive || !current.CanDisableVisitor || !current.Capabilities.Allows("visitorOff")))
            { Set(new(VisitorWidgetStatus.Failed, Detail: "Ausschalten nicht freigegeben – Zustand am PC prüfen")); return false; }
            if (current.VisitorActive && current.ErrorCode != null) { Set(new(VisitorWidgetStatus.Failed, Detail: "Besuchermodus meldet einen Fehler – am PC prüfen")); return false; }
            if (command == "visitorOn" && current.VisitorActive)
            { Confirm(current); return true; } // Idempotent activation: never send visitorOff.
            if (command != null)
            {
                if (!current.Capabilities.Allows(command)) { Set(new(VisitorWidgetStatus.Failed, Detail: "Besuchermodus am PC konfigurieren / Freigabe prüfen")); return false; }
                var result = await connection.SendAsync(command, cancellation);
                if (requestGeneration != Interlocked.Read(ref generation)) return false;
                if (result.Status != "completed" || result.State.VisitorActive != (command == "visitorOn") || result.State.ErrorCode != null)
                { Set(new(VisitorWidgetStatus.Failed, Detail: "Aktion nicht bestätigt – am PC prüfen")); return false; }
                current = result.State;
            }
            Confirm(current); return true;
        }
        catch (OperationCanceledException) { Set(new(VisitorWidgetStatus.Unreachable, Detail: "Zeitlimit – Ergebnis nicht bestätigt. Keine Wiederholung.")); return false; }
        catch { Set(new(VisitorWidgetStatus.Unreachable, Detail: "Nicht erreichbar – WLAN, PC und Freigabe prüfen")); return false; }
        finally { gate.Release(); Changed?.Invoke(); }
    }
    private void Confirm(RemoteState state)
    {
        lock (stateGate) lastConfirmed = state;
        Set(new(VisitorWidgetStatus.Confirmed, state.VisitorActive, state.CanDisableVisitor && state.Capabilities.Allows("visitorOff"), time.GetTimestamp()));
    }
}
