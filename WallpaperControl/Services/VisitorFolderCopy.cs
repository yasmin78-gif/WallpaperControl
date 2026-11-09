namespace WallpaperControl;

internal static class VisitorFolderCopy
{
    internal static bool HasDestination(VisitorModeSettings settings) =>
        settings.Source == VisitorSourceKind.Folder && Directory.Exists(settings.Path);

    internal static async Task<string> CopyAsync(string source, VisitorModeSettings settings, CancellationToken cancellation = default)
    {
        if (settings.Active || !HasDestination(settings)) throw new InvalidOperationException("Visitor folder copy is unavailable.");
        if (!WallpaperImageInfo.IsSupportedWallpaperExtension(source)) throw new InvalidDataException("Only images may be copied.");
        string destination = Path.GetFullPath(settings.Path), original = Path.GetFullPath(source);
        return await Task.Run(async () =>
        {
            cancellation.ThrowIfCancellationRequested();
            using (Image image = Image.FromFile(original)) { _ = image.Width; }
            if (string.Equals(Path.GetDirectoryName(original), Path.TrimEndingDirectorySeparator(destination), StringComparison.OrdinalIgnoreCase)) return original;
            string temporary = Path.Combine(destination, ".visitor-copy-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                await using (var input = new FileStream(original, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                    await input.CopyToAsync(output, cancellation);
                for (int suffix = 0; suffix < 10000; suffix++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    string filename = suffix == 0 ? Path.GetFileName(original)
                        : Path.GetFileNameWithoutExtension(original) + " (" + suffix + ")" + Path.GetExtension(original);
                    string target = Path.Combine(destination, filename);
                    try { File.Move(temporary, target, overwrite: false); return target; }
                    catch (IOException) when (File.Exists(target)) { }
                }
                throw new IOException("No free destination filename.");
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }, cancellation);
    }
}
