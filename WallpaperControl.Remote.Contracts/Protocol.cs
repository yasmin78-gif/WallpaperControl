using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace WallpaperControl.Remote;

public sealed record RemoteCapabilities(string[] Commands, string[] Layouts, uint[] Intervals, uint MinimumInterval = 0, uint MaximumInterval = 0)
{
    public static RemoteCapabilities Empty { get; } = new([], [], []);
    public bool Allows(string command) => Commands.Contains(command, StringComparer.Ordinal);
}

public sealed record RemoteState
{
    public int ApiVersion { get; init; } = 1;
    public string ServerId { get; init; } = "";
    public string ServerName { get; init; } = "WallpaperControl";
    public string Epoch { get; init; } = "";
    public long Revision { get; init; }
    public string Mode { get; init; } = "slideshow";
    public string MediaKind { get; init; } = "image";
    public string Playback { get; init; } = "inactive";
    public bool ManualPaused { get; init; }
    public string[] PauseReasons { get; init; } = [];
    public bool VisitorActive { get; init; }
    public bool VisitorFolder { get; init; }
    public bool Busy { get; init; }
    public bool Shuffle { get; init; }
    public uint IntervalMilliseconds { get; init; } = 300000;
    public string Layout { get; init; } = "fill";
    public RemoteCapabilities Capabilities { get; init; } = RemoteCapabilities.Empty;
    public bool CanDisableVisitor { get; init; }
    public long? RemainingMilliseconds { get; init; }
    public DateTimeOffset CapturedAt { get; init; }
    public string? ErrorCode { get; init; }

    public RemoteState ForDevice(bool canDisable) => this with
    {
        CanDisableVisitor = canDisable,
        Capabilities = Capabilities with { Commands = Capabilities.Commands.Where(c => c != "visitorOff" || canDisable).ToArray() }
    };
}

public sealed record RemoteCommand(string Id, string Epoch, long Revision, string Type, string Value);
public sealed record RemoteResult(string CommandId, string Status, string? ErrorCode, RemoteState State);
public sealed record RemoteOutcome(bool Success, string? ErrorCode = null);
public sealed record RemoteQr(int Version, string ServerId, string Name, string[] Addresses, string Pin, string Session, string Secret);
public sealed record PairRequest(string Session, string Secret, string DeviceName);
public sealed record PairPending(string Claim, string Code);
public sealed record ClaimRequest(string Claim);
public sealed record DeviceCredential(string DeviceId, string Token, bool CanDisableVisitor);
public sealed record SavedRemote(RemoteQr Server, DeviceCredential Credential, string Address);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(RemoteState))]
[JsonSerializable(typeof(RemoteCommand))]
[JsonSerializable(typeof(RemoteResult))]
[JsonSerializable(typeof(RemoteOutcome))]
[JsonSerializable(typeof(RemoteQr))]
[JsonSerializable(typeof(PairRequest))]
[JsonSerializable(typeof(PairPending))]
[JsonSerializable(typeof(ClaimRequest))]
[JsonSerializable(typeof(DeviceCredential))]
[JsonSerializable(typeof(SavedRemote))]
public partial class RemoteJsonContext : JsonSerializerContext { }

public static class RemoteJson
{
    public static JsonTypeInfo<T> Type<T>() => (JsonTypeInfo<T>)(RemoteJsonContext.Default.GetTypeInfo(typeof(T)) ?? throw new InvalidOperationException("Unknown protocol type"));
}

public static class RemoteTrust
{
    public static string Secret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
    public static string Pin(X509Certificate2 certificate) => Convert.ToHexString(SHA256.HashData(certificate.PublicKey.ExportSubjectPublicKeyInfo()));
    public static bool Same(string first, string second)
    {
        if (first.Length != second.Length) return false;
        return CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(first), System.Text.Encoding.UTF8.GetBytes(second));
    }
    public static bool Validate(X509Certificate2? certificate, string pin)
    {
        if (certificate == null || pin.Length != 64 || certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow || certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow) return false;
        using var rsa = certificate.GetRSAPublicKey();
        if (rsa == null || rsa.KeySize < 2048 || certificate.SignatureAlgorithm.Value is not ("1.2.840.113549.1.1.11" or "1.2.840.113549.1.1.12" or "1.2.840.113549.1.1.13")) return false;
        if (!certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().Any(e => e.EnhancedKeyUsages.Cast<Oid>().Any(o => o.Value == "1.3.6.1.5.5.7.3.1"))) return false;
        return Same(Pin(certificate), pin.ToUpperInvariant());
    }
    public static bool Local(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return true;
        var bytes = ip.GetAddressBytes();
        return bytes.Length == 4
            ? bytes[0] == 10 || bytes[0] == 192 && bytes[1] == 168 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 || bytes[0] == 169 && bytes[1] == 254
            : ip.IsIPv6LinkLocal || (bytes[0] & 0xfe) == 0xfc;
    }
    public static Uri Endpoint(string address, bool allowLoopback = false)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || !IPAddress.TryParse(uri.DnsSafeHost.Trim('[', ']'), out var ip) || !Local(ip) || !allowLoopback && IPAddress.IsLoopback(ip))
            throw new ArgumentException("A local HTTPS address is required.");
        return uri;
    }
    public static string Address(IPAddress ip, int port) => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? $"https://[{ip}]:{port}/" : $"https://{ip}:{port}/";
    public static HttpClientHandler Handler(string pin) => new()
    {
        UseProxy = false, AllowAutoRedirect = false,
        ServerCertificateCustomValidationCallback = (_, certificate, _, _) => Validate(certificate, pin)
    };
}
