using System.Diagnostics;
using System.Security.Cryptography;
namespace RemoteExperiment;

public sealed class Pairing
{
    public TimeProvider Clock { get; init; } = TimeProvider.System;
    private readonly object gate = new();
    private string session = "", hash = "", claim = "", code = "";
    private long issued;
    private bool reserved, approved, allowDisable;
    private Credential? credential;
    private readonly Dictionary<string, (string Device, bool Disable)> devices = new();
    public event Action<string, string>? Requested;
    public void Begin(string id, string secret) { lock (gate) { session = id; hash = Trust.Hash(secret); issued = Clock.GetTimestamp(); reserved = approved = false; claim = code = ""; credential = null; } }
    private bool Valid => hash.Length != 0 && Clock.GetElapsedTime(issued) < TimeSpan.FromMinutes(5);
    public PairPending? Request(PairRequest request)
    {
        PairPending pending;
        lock (gate)
        {
            if (!Valid || reserved || request.Session != session || request.Secret == null || request.Name == null || request.Secret.Length > 128 || request.Name.Length is < 1 or > 64 || !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(hash), Convert.FromHexString(Trust.Hash(request.Secret)))) return null;
            reserved = true; claim = Trust.Secret(); code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(); pending = new(claim, code);
        }
        Requested?.Invoke(request.Name, pending.Code); return pending;
    }
    public void Confirm(bool canDisable) { lock (gate) { if (reserved && Valid) { approved = true; allowDisable = canDisable; } } }
    public Credential? Claim(string proof)
    {
        lock (gate)
        {
            if (!Valid || !approved || proof != claim) return null;
            if (credential == null)
            {
                var issuedCredential = new Credential(Guid.NewGuid().ToString("N"), Trust.Secret(), allowDisable);
                devices[Trust.Hash(issuedCredential.Token)] = (issuedCredential.DeviceId, issuedCredential.CanDisableVisitor);
                try { DevicesChanged?.Invoke(); credential = issuedCredential; }
                catch { devices.Remove(Trust.Hash(issuedCredential.Token)); throw; }
            }
            return credential;
        }
    }
    public (string Device, bool Disable)? Authenticate(string token) { lock (gate) return devices.TryGetValue(Trust.Hash(token), out var device) ? device : null; }
    public void AddTestDevice(Credential device) { lock (gate) devices[Trust.Hash(device.Token)] = (device.DeviceId, device.CanDisableVisitor); }
    public sealed record StoredDevice(string Hash, string Device, bool Disable);
    public event Action? DevicesChanged;
    public StoredDevice[] ExportDevices() { lock (gate) return devices.Select(d => new StoredDevice(d.Key, d.Value.Device, d.Value.Disable)).ToArray(); }
    public void ImportDevices(StoredDevice[] stored) { lock (gate) foreach (var d in stored) devices[d.Hash] = (d.Device, d.Disable); }
}
