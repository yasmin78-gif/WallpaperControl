namespace WallpaperControl.Remote;

public sealed record PendingPairing(string DeviceName, string Code, DateTimeOffset ExpiresAt);

public sealed class RemotePairing(RemoteVault vault, TimeProvider? clock = null)
{
    private readonly object gate = new();
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private string session = "", secretHash = "", claim = "", deviceName = "", code = "";
    private long issued;
    private bool reserved, approved;
    private bool permission;
    private DeviceCredential? issuedCredential;
    public event Action? Changed;
    private bool Valid => secretHash.Length != 0 && time.GetElapsedTime(issued) < TimeSpan.FromMinutes(5);
    public RemoteQr Begin(string name, string[] addresses, string pin)
    {
        RemoteQr qr;
        lock (gate)
        {
            string secret = RemoteTrust.Secret(); session = Guid.NewGuid().ToString("N"); secretHash = RemoteTrust.Hash(secret);
            issued = time.GetTimestamp(); reserved = approved = permission = false; issuedCredential = null; claim = code = deviceName = "";
            qr = new(1, vault.ServerId, name, addresses, pin, session, secret);
        }
        Changed?.Invoke(); return qr;
    }
    public PairPending? Request(PairRequest request)
    {
        PairPending result;
        lock (gate)
        {
            if (!Valid || reserved || request.Session != session || request.Secret == null || request.Secret.Length != 64 || string.IsNullOrWhiteSpace(request.DeviceName) || request.DeviceName.Length > 64 || request.DeviceName.Any(char.IsControl)
                || !RemoteTrust.Same(RemoteTrust.Hash(request.Secret), secretHash)) return null;
            reserved = true; deviceName = request.DeviceName; claim = RemoteTrust.Secret(); code = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            result = new(claim, code);
        }
        Changed?.Invoke(); return result;
    }
    public PendingPairing? Pending
    {
        get { lock (gate) return Valid && reserved && !approved ? new(deviceName, code, time.GetUtcNow() + (TimeSpan.FromMinutes(5) - time.GetElapsedTime(issued))) : null; }
    }
    public void Approve(bool canDisable)
    {
        lock (gate) { if (!Valid || !reserved || approved) throw new InvalidOperationException("Pairing expired"); approved = true; permission = canDisable; }
        Changed?.Invoke();
    }
    public DeviceCredential? Claim(ClaimRequest proof)
    {
        lock (gate)
        {
            if (!Valid || !approved || proof.Claim == null || !RemoteTrust.Same(proof.Claim, claim)) return null;
            return issuedCredential ??= vault.Add(deviceName, permission);
        }
    }
    public void Complete(ClaimRequest proof)
    {
        lock (gate) if (proof.Claim != null && RemoteTrust.Same(proof.Claim, claim) && issuedCredential != null) { Clear(); }
        Changed?.Invoke();
    }
    public void Cancel() { lock (gate) Clear(); Changed?.Invoke(); }
    private void Clear() { secretHash = session = claim = code = deviceName = ""; issuedCredential = null; reserved = approved = false; }
}
