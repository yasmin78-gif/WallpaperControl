using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace WallpaperControl.Video;

// A process-lifetime module owner. Native clients must finish before process exit;
// retaining this one validated module prevents unloading code under an event worker.
internal sealed class MpvRuntime
{
    internal const string Build = "mpv v0.41.0-1092-g3186d369f";
    internal const string Hash = "EC77593D23735F36EB08E486154E89CF1CC6A5650327DAB125736C1BB5D3AF40";
    private static readonly object Gate = new();
    private static MpvRuntime? current;
    private readonly nint library;
    internal string Location { get; }
    private MpvRuntime(string path)
    {
        if (!Environment.Is64BitProcess || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("Pinned mpv requires x64");
        using var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (Convert.ToHexString(SHA256.HashData(file)) != Hash) throw new InvalidDataException("Pinned mpv hash mismatch");
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
