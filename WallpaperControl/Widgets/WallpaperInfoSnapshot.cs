using System.IO;

namespace WallpaperControl
{
    /// <summary>A coherent snapshot obtained after a wallpaper change or statistics mutation on the UI thread.</summary>
    internal readonly record struct WallpaperInfoSnapshot(string? Path, int Views, int WallpaperCount, WallpaperViewSummary Summary)
    {
        internal static WallpaperInfoSnapshot Create(string? path, int wallpaperCount,
            IReadOnlyDictionary<string, int> counts, bool advanced)
        {
            int views = 0;
            if (!string.IsNullOrEmpty(path)) counts.TryGetValue(path, out views);
            return new(path, views, Math.Max(0, wallpaperCount), advanced ? WallpaperViewSummary.Calculate(counts) : default);
        }

        /// <summary>Formats a display name without changing paths, extensions, or statistics keys.</summary>
        internal static string DisplayName(string? path, string? suffix, bool showExtension)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            int matchedLength = 0;
            if (!string.IsNullOrEmpty(suffix))
            {
                // Preserve exact single-suffix behavior; whitespace around list entries is formatting.
                string[] candidates = suffix.Contains(',', StringComparison.Ordinal)
                    ? suffix.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    : new[] { suffix };
                foreach (string candidate in candidates)
                {
                    if (candidate.Length > matchedLength && name.EndsWith(candidate, StringComparison.OrdinalIgnoreCase))
                        matchedLength = candidate.Length;
                }
            }
            // Remove only the longest match from the original stem, never a chain of suffixes.
            if (matchedLength > 0) name = name[..^matchedLength];
            return showExtension ? name + System.IO.Path.GetExtension(path) : name;
        }
    }
}
