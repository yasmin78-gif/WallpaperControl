namespace RemoteExperiment;

// Owns only synthetic data. All access is serialized through the WinForms dispatcher.
public sealed class StateModel(string serverId)
{
    public Snapshot State { get; private set; } = new(serverId, Guid.NewGuid().ToString("N"), 0);
    private readonly Dictionary<(string Device, string Id), (Command Command, Result Result)> completed = new();
    public event Action<Snapshot>? Changed;
    public Result Execute(Command command, string device, bool canDisableVisitor)
    {
        if (string.IsNullOrEmpty(command.Id) || command.Id.Length > 64 || command.Type == null || command.Value == null) return new("rejected", "invalidCommand", State);
        var key = (device, command.Id);
        if (completed.TryGetValue(key, out var previous)) return previous.Command == command ? previous.Result : new("rejected", "idReuse", State);
        if (command.Id.Length is < 1 or > 64) return new("rejected", "invalidId", State);
        if (command.Epoch != State.Epoch || command.Revision != State.Revision) return new("rejected", "stale", State);
        if (State.Visitor && command.Type == "mode") return new("rejected", "visitorProtected", State);
        if (command.Type == "visitor" && command.Value == "false" && !canDisableVisitor) return new("rejected", "visitorPermission", State);
        Snapshot? next = command.Type switch
        {
            "next" when !State.Paused && (State.Mode == "slideshow" || State.Visitor) => State with { SyntheticAdvances = State.SyntheticAdvances + 1 },
            "previous" when !State.Paused && (State.Mode == "slideshow" || State.Visitor) => State with { SyntheticAdvances = State.SyntheticAdvances - 1 },
            "pause" when bool.TryParse(command.Value, out var paused) => State with { Paused = paused },
            "shuffle" when bool.TryParse(command.Value, out var shuffle) => State with { Shuffle = shuffle },
            "visitor" when bool.TryParse(command.Value, out var visitor) => State with { Visitor = visitor },
            "mode" when command.Value is "slideshow" or "video" => State with { Mode = command.Value },
            "interval" when int.TryParse(command.Value, out var interval) && interval is >= 1000 and <= 86400000 => State with { Interval = interval },
            "layout" when command.Value is "fill" or "fit" or "stretch" or "tile" or "center" or "span" => State with { Layout = command.Value },
            _ => null
        };
        if (next == null) return new("rejected", "notAvailable", State);
        if (next != State) { State = next with { Revision = State.Revision + 1 }; Changed?.Invoke(State); }
        var result = new Result("completed", null, State);
        if (completed.Count >= 1024) completed.Remove(completed.Keys.First());
        completed[key] = (command, result);
        return result;
    }
}
