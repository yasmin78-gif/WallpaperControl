namespace WallpaperControl;

internal enum PackageOperation { Success, Busy, Duplicate, InvalidNumber, StorageError, Missing, ProviderError }
internal readonly record struct PackageResult(PackageOperation Outcome, TrackingProviderFailure? Failure = null);

/// <summary>UI-thread owner of package mutations. No timer, startup request or resume refresh.</summary>
internal sealed class PackageTrackingService : IDisposable
{
    private readonly TrackingStore store;
    private readonly ITrackingProvider provider;
    private readonly CancellationTokenSource lifetime = new();
    private bool disposed;
    internal event Action? Changed;
    internal bool Busy { get; private set; }
    internal bool CanWrite => store.CanWrite;
    internal bool LoadIssue => store.LoadIssue;
    internal IReadOnlyList<TrackedShipment> Shipments => store.Shipments;
    internal Dictionary<Guid, TrackingProviderFailure> RefreshFailures { get; } = new();
    internal PackageTrackingService(TrackingStore store, ITrackingProvider provider)
    { this.store = store; this.provider = provider; }
    private void Notify() { if (!disposed) Changed?.Invoke(); }
    internal async Task<PackageResult> AddAsync(string number, string? name, CancellationToken token = default)
    {
        if (Busy) return new(PackageOperation.Busy);
        if (!CanWrite || disposed) return new(PackageOperation.StorageError);
        number = number.Trim();
        if (number.Length is < 5 or > 50 || !number.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '/' or '.'))
            return new(PackageOperation.InvalidNumber);
        if (Shipments.Any(s => string.Equals(s.TrackingNumber.Trim(), number, StringComparison.OrdinalIgnoreCase)))
            return new(PackageOperation.Duplicate);
        Busy = true; Notify();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        try
        {
            var snapshot = await provider.CreateTrackerAsync(number, cancellationToken: linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            return new(store.Save(snapshot with { DisplayName = CleanName(name) }) ? PackageOperation.Success : PackageOperation.StorageError);
        }
        catch (TrackingProviderException ex) { return new(PackageOperation.ProviderError, ex.Failure); }
        finally { Busy = false; Notify(); }
    }
    internal PackageResult Rename(Guid id, string? name)
    {
        if (disposed) return new(PackageOperation.StorageError);
        if (Busy) return new(PackageOperation.Busy);
        var existing = Shipments.FirstOrDefault(s => s.Id == id);
        if (existing == null) return new(PackageOperation.Missing);
        bool saved = store.Save(existing with { DisplayName = CleanName(name) });
        if (saved) Notify();
        return new(saved ? PackageOperation.Success : PackageOperation.StorageError);
    }
    internal PackageResult Delete(Guid id)
    {
        if (disposed) return new(PackageOperation.StorageError);
        if (Busy) return new(PackageOperation.Busy);
        bool saved = store.Delete(id);
        if (saved) { RefreshFailures.Remove(id); Notify(); }
        return new(saved ? PackageOperation.Success : PackageOperation.StorageError);
    }
    internal async Task<PackageResult> RefreshAsync(CancellationToken token = default)
    {
        if (Busy) return new(PackageOperation.Busy);
        if (!CanWrite || disposed) return new(PackageOperation.StorageError);
        Busy = true; Notify();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        bool failed = false, storageFailed = false;
        try
        {
            foreach (var existing in Shipments)
            {
                linked.Token.ThrowIfCancellationRequested();
                try
                {
                    if (existing.Provider != provider.ProviderId || string.IsNullOrWhiteSpace(existing.ProviderTrackerId))
                        throw new TrackingProviderException(TrackingProviderFailure.TrackerNotFound);
                    var snapshot = await provider.GetTrackingAsync(existing.ProviderTrackerId, linked.Token);
                    linked.Token.ThrowIfCancellationRequested();
                    if (!store.Save(snapshot with { Id = existing.Id, DisplayName = existing.DisplayName,
                        ProviderTrackerId = existing.ProviderTrackerId, CreatedAt = existing.CreatedAt,
                        NotificationState = existing.NotificationState, AdditionalData = existing.AdditionalData }))
                    { storageFailed = true; continue; }
                    RefreshFailures.Remove(existing.Id);
                }
                catch (TrackingProviderException ex) { failed = true; RefreshFailures[existing.Id] = ex.Failure; }
            }
            return new(storageFailed ? PackageOperation.StorageError : failed ? PackageOperation.ProviderError : PackageOperation.Success);
        }
        finally { Busy = false; Notify(); }
    }
    private static string? CleanName(string? name) => string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; lifetime.Cancel();
        if (provider is IDisposable resource) resource.Dispose();
        lifetime.Dispose(); Changed = null;
    }
}
