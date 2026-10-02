using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace WallpaperControl.Video;

// A process-lifetime module owner. Native clients must finish before process exit;
// retaining this one validated module prevents unloading code under an event worker.
internal sealed class MpvRuntime
{
    internal const string Build = "mpv v0.41.0-wc-minimal-r1-x64-api2.5-g3186d369f9f090cd1363be0ac46a037824b702c6";
    internal const string Hash = "91A96C03DC56F7FA4D146F3E23CE491C478DABCB7F101BA6509BEC538C6A95FC";
    private static readonly object Gate = new();
    private static MpvRuntime? current;
    private readonly nint library;
    internal string Location { get; }
    internal string ExpectedBuild { get; } = Build;
    internal MpvOptionProfile Profile => MpvOptionProfile.MinimalR1;
    private MpvRuntime(string path)
    {
        if (!Environment.Is64BitProcess || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("Pinned mpv requires x64");
        using var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        string actualHash = Convert.ToHexString(SHA256.HashData(file));
        if (actualHash != Hash) throw new InvalidDataException("Pinned mpv hash mismatch");
        using var validatedPackage = CandidatePackageValidation.Acquire(Path.GetDirectoryName(path)!, "AF320392B27FB679FD09981FC628AF395242F152059E15C44F772DCD43D72C0F");
        using var reader = new BinaryReader(file, System.Text.Encoding.UTF8, true);
        file.Position = 0x3c; int pe = reader.ReadInt32(); file.Position = pe;
        if (reader.ReadUInt32() != 0x4550 || reader.ReadUInt16() != 0x8664)
            throw new BadImageFormatException("Pinned mpv is not an x64 PE image");
        library = NativeLibrary.Load(path); Location = path;
        try
        {
            uint version = Export<ApiVersion>("mpv_client_api_version")();
            if (version != 0x20005) throw new NotSupportedException("Unsupported pinned mpv client ABI");
        }
        catch { NativeLibrary.Free(library); throw; }
    }
    private Task? verification;
    internal Task VerifyIdentityAsync()
    {
        lock (Gate) return verification ??= VerifyAsync();
    }
    private async Task VerifyAsync()
    {
        await using var probe = new MpvClient(this, 0, _ => { });
        await probe.Initialized.ConfigureAwait(false);
    }
    internal static MpvRuntime Open(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("An explicit local runtime path is required");
        path = Path.GetFullPath(path);
        lock (Gate)
        {
            if (current != null && !string.Equals(path, current.Location, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A different mpv module is already owned");
            return current ??= new(path);
        }
    }
    internal T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ApiVersion();
}
