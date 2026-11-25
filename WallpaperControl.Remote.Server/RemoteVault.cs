using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace WallpaperControl.Remote;

public sealed record RemoteSettings(bool Enabled = false, string AdapterId = "", int Port = 47831, string Name = "WallpaperControl");
public sealed record RemoteDevice(string Id, string Name, string TokenHash, bool CanDisableVisitor, DateTimeOffset Created, long Version = 1);
internal sealed record VaultData(string ServerId, RemoteSettings Settings, RemoteDevice[] Devices);
public sealed record DeviceGrant(string Id, string Name, bool CanDisableVisitor, long Version);

// Owns only Remote configuration. Whole-file DPAPI, atomic replace, fail closed on read/write failure.
public sealed class RemoteVault
{
    private readonly object gate = new();
    private readonly string folder;
    private VaultData data;
    public event Action? DevicesChanged;
    public RemoteVault(string folder)
    {
        this.folder = Path.GetFullPath(folder);
        data = File.Exists(DataPath) ? JsonSerializer.Deserialize<VaultData>(Unprotect(File.ReadAllBytes(DataPath))) ?? throw new InvalidDataException("Remote store invalid")
            : new(Guid.NewGuid().ToString("N"), new(), []);
        if (data.ServerId.Length != 32 || data.Settings.Port is < 1024 or > 65535 || data.Devices.Any(d => d.TokenHash.Length != 64)) throw new InvalidDataException("Remote store invalid");
    }
    private string DataPath => Path.Combine(folder, "devices.dat");
    public string ServerId { get { lock (gate) return data.ServerId; } }
    public RemoteSettings Settings { get { lock (gate) return data.Settings; } }
    public RemoteDevice[] Devices { get { lock (gate) return data.Devices.ToArray(); } }
    private void Commit(VaultData updated)
    {
        Directory.CreateDirectory(folder);
        WriteAtomic(DataPath, Protect(JsonSerializer.SerializeToUtf8Bytes(updated)));
        data = updated;
    }
    public void Configure(RemoteSettings settings)
    {
        if (settings.Port is < 1024 or > 65535 || settings.Name.Length is < 1 or > 64 || settings.Name.Any(char.IsControl)) throw new ArgumentException("Invalid remote settings");
        lock (gate) Commit(data with { Settings = settings });
    }
    public DeviceCredential Add(string name, bool disable)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64 || name.Any(char.IsControl)) throw new ArgumentException("Invalid device name");
        var credential = new DeviceCredential(Guid.NewGuid().ToString("N"), RemoteTrust.Secret(), disable);
        lock (gate)
        {
            if (data.Devices.Length >= 32) throw new InvalidOperationException("Device limit");
            Commit(data with { Devices = [.. data.Devices, new(credential.DeviceId, name, RemoteTrust.Hash(credential.Token), disable, DateTimeOffset.UtcNow)] });
        }
        NotifyDevicesChanged(); return credential;
    }
    public DeviceGrant? Authenticate(string token)
    {
        if (token.Length != 64) return null;
        string hash = RemoteTrust.Hash(token);
        lock (gate)
        {
            var device = data.Devices.FirstOrDefault(d => RemoteTrust.Same(d.TokenHash, hash));
            return device == null ? null : new(device.Id, device.Name, device.CanDisableVisitor, device.Version);
        }
    }
    public bool Current(DeviceGrant grant) { lock (gate) return data.Devices.Any(d => d.Id == grant.Id && d.Version == grant.Version); }
    public void Change(string id, string name, bool disable)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64 || name.Any(char.IsControl)) throw new ArgumentException("Invalid device name");
        lock (gate) Commit(data with { Devices = data.Devices.Select(d => d.Id == id ? d with { Name = name, CanDisableVisitor = disable, Version = d.Version + 1 } : d).ToArray() });
        NotifyDevicesChanged();
    }
    public void Remove(string id)
    {
        lock (gate) Commit(data with { Devices = data.Devices.Where(d => d.Id != id).ToArray() });
        NotifyDevicesChanged();
    }
    private void NotifyDevicesChanged()
    {
        foreach (Action listener in DevicesChanged?.GetInvocationList() ?? [])
            try { listener(); } catch { /* A notification failure cannot undo an already committed revocation or issue duplicate credentials. */ }
    }
    public X509Certificate2 Identity()
    {
        string path = Path.Combine(folder, "identity.dat");
        lock (gate)
        {
            X509Certificate2? previous = File.Exists(path) ? X509CertificateLoader.LoadPkcs12(Unprotect(File.ReadAllBytes(path)), null, X509KeyStorageFlags.Exportable) : null;
            if (previous != null && previous.NotAfter.ToUniversalTime() > DateTime.UtcNow.AddDays(7)) return previous;
            using (previous)
            using (var key = previous?.GetRSAPrivateKey() ?? RSA.Create(3072))
            {
                var request = new CertificateRequest("CN=WallpaperControl Remote", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
                using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(90));
                byte[] pfx = generated.Export(X509ContentType.Pfx);
                Directory.CreateDirectory(folder); WriteAtomic(path, Protect(pfx));
                return X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.Exportable);
            }
        }
    }
    private static byte[] Protect(byte[] bytes) => ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
    private static byte[] Unprotect(byte[] bytes) => ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
    private static void WriteAtomic(string path, byte[] bytes)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { output.Write(bytes); output.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
