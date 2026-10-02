namespace WallpaperControl.Video;

using System.Security.Cryptography;
using System.Text.Json;

// The four pinned DLLs are one inseparable production runtime package.
internal static class CandidatePackageValidation
{
    internal const string Identity = "mpv v0.41.0-wc-minimal-r1-x64-api2.5-g3186d369f9f090cd1363be0ac46a037824b702c6";
    internal const string MpvCommit = "3186d369f9f090cd1363be0ac46a037824b702c6";
    internal const string FfmpegCommit = "5a54fcf75e0245111075b1c31593ba1919c25306";
    private static readonly string[] Names = ["libmpv-2.dll", "libspirv-cross-c-shared.dll", "libc++.dll", "libunwind.dll"];
    internal static void ValidateNative(uint api, string identity)
    {
        if (api != 0x20005) throw new NotSupportedException("Exact candidate native API mismatch");
        if (identity != Identity) throw new NotSupportedException("Exact candidate native build identity mismatch");
    }
    internal sealed class Lease : IDisposable
    {
        private readonly List<FileStream> streams = [];
        internal FileStream Open(string path)
        {
            var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            streams.Add(stream); return stream;
        }
        public void Dispose() { foreach (var stream in streams) stream.Dispose(); streams.Clear(); }
    }
    internal static void Validate(string directory, string trustedManifestHash)
    { using var lease = Acquire(directory, trustedManifestHash); }
    internal static Lease Acquire(string directory, string trustedManifestHash)
    {
        var lease = new Lease();
        try
        {
        using var memory = new MemoryStream();
        lease.Open(Path.Combine(directory, "runtime-package.json")).CopyTo(memory);
        byte[] bytes = memory.ToArray();
        if (Convert.ToHexString(SHA256.HashData(bytes)) != trustedManifestHash) throw new InvalidDataException("Package manifest hash mismatch");
        using var doc = JsonDocument.Parse(bytes); var root = doc.RootElement;
        void Expected(string key, string value) { if (root.GetProperty(key).GetString() != value) throw new InvalidDataException("Package " + key + " mismatch"); }
        Expected("packageId", "WallpaperControl.libmpv.minimal"); Expected("packageVersion", "1");
        Expected("mpvCommit", MpvCommit); Expected("ffmpegCommit", FfmpegCommit);
        Expected("clientApi", "2.5"); Expected("architecture", "x64"); Expected("buildIdentity", Identity);
        Expected("profile", "wc-minimal-r1-script-free");
        var files = root.GetProperty("files").EnumerateArray().ToArray();
        if (files.Length != Names.Length || !files.Select(f => f.GetProperty("name").GetString()).Order().SequenceEqual(Names.Order()))
            throw new InvalidDataException("Package file set mismatch");
        long total = 0;
        foreach (var file in files)
        {
            string path = Path.Combine(directory, file.GetProperty("name").GetString()!);
            var stream = lease.Open(path);
            total += stream.Length;
            if (stream.Length != file.GetProperty("size").GetInt64() || Convert.ToHexString(SHA256.HashData(stream)) != file.GetProperty("sha256").GetString())
                throw new InvalidDataException("Package DLL hash/size mismatch");
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
            stream.Position = 0; if (reader.ReadUInt16() != 0x5A4D) throw new BadImageFormatException("Missing MZ header");
            stream.Position = 0x3c; int offset = reader.ReadInt32();
            if (offset < 64 || offset > stream.Length - 26) throw new BadImageFormatException("Invalid PE offset");
            stream.Position = offset;
            if (reader.ReadUInt32() != 0x4550 || reader.ReadUInt16() != 0x8664) throw new BadImageFormatException("Package requires x64 PE");
        }
        if (total != root.GetProperty("totalSize").GetInt64()) throw new InvalidDataException("Package total size mismatch");
        return lease;
        }
        catch { lease.Dispose(); throw; }
    }
}
