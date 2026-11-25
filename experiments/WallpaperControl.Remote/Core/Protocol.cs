using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace RemoteExperiment;

public sealed record Snapshot(string ServerId, string Epoch, long Revision, string Mode = "slideshow", bool Visitor = false,
    bool Paused = false, bool Shuffle = false, int Interval = 300000, string Layout = "fill", int SyntheticAdvances = 0);
public sealed record Command(string Id, string Epoch, long Revision, string Type, string Value);
public sealed record Result(string Status, string? Error, Snapshot State);
public sealed record PairQr(int Version, string Address, string ServerId, string Pin, string Session, string Secret);
public sealed record PairRequest(string Session, string Secret, string Name);
public sealed record PairPending(string Claim, string Code);
public sealed record ClaimRequest(string Claim);
public sealed record Credential(string DeviceId, string Token, bool CanDisableVisitor);
public sealed record SavedConnection(PairQr Server, Credential Credential);

public static class Trust
{
    public static string Pin(X509Certificate2 certificate) => Convert.ToHexString(SHA256.HashData(certificate.PublicKey.ExportSubjectPublicKeyInfo()));
    public static bool Validate(X509Certificate2? certificate, string pin, DateTimeOffset? now = null)
    {
        if (certificate == null || pin.Length != 64) return false;
        var time = (now ?? DateTimeOffset.UtcNow).UtcDateTime;
        if (time < certificate.NotBefore.ToUniversalTime() || time > certificate.NotAfter.ToUniversalTime()) return false;
        if (!certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().Any(e => e.EnhancedKeyUsages.Cast<Oid>().Any(o => o.Value == "1.3.6.1.5.5.7.3.1"))) return false;
        using var rsa = certificate.GetRSAPublicKey();
        if (rsa == null || rsa.KeySize < 2048) return false;
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(pin), Convert.FromHexString(Pin(certificate))); }
        catch (FormatException) { return false; }
    }
    public static Uri Endpoint(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || !IPAddress.TryParse(uri.DnsSafeHost.Trim('[', ']'), out var ip) || !Local(ip))
            throw new ArgumentException("Only literal local HTTPS endpoints are accepted.");
        return uri;
    }
    public static bool Local(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return true;
        byte[] b = ip.GetAddressBytes();
        return b.Length == 4 ? b[0] == 10 || b[0] == 192 && b[1] == 168 || b[0] == 172 && b[1] >= 16 && b[1] <= 31 || b[0] == 169 && b[1] == 254
            : ip.IsIPv6LinkLocal || (b[0] & 0xfe) == 0xfc;
    }
    public static HttpClientHandler Handler(string pin) => new()
    {
        UseProxy = false, AllowAutoRedirect = false,
        ServerCertificateCustomValidationCallback = (_, cert, _, _) => Validate(cert, pin)
    };
    public static X509Certificate2 CreateCertificate(bool expired = false, X509Certificate2? identity = null)
    {
        using var rsa = identity?.GetRSAPrivateKey() ?? RSA.Create(2048);
        var request = new CertificateRequest("CN=WallpaperControl Remote Experiment", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        var eku = new OidCollection { new("1.3.6.1.5.5.7.3.1") };
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(eku, true));
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), expired ? DateTimeOffset.UtcNow.AddDays(-1) : DateTimeOffset.UtcNow.AddDays(30));
        return X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
    }
    public static string Secret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
}
