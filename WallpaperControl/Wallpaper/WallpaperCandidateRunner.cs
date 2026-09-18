namespace WallpaperControl;

internal static class WallpaperCandidateRunner
{
    // Each request tries every candidate at most once. Failed files are eligible
    // again on later requests, so a repaired/finished copy is never blacklisted.
    internal static async Task<string?> TryAdvanceAsync(string[] files, string? current,
        bool shuffle, bool backward, Random random, Func<string, Task<bool>> tryDisplay)
    {
        int index = Array.FindIndex(files, p => string.Equals(p, current, StringComparison.OrdinalIgnoreCase));
        IEnumerable<string> ordered;
        if (shuffle && files.Length > 1)
        {
            string[] candidates = files.Where(p => !string.Equals(p, current, StringComparison.OrdinalIgnoreCase)).ToArray();
            random.Shuffle(candidates);
            ordered = candidates;
        }
        else
        {
            ordered = Enumerable.Range(1, files.Length).Select(step => files[backward
                ? ((index < 0 ? 0 : index) - step % files.Length + files.Length) % files.Length
                : (index + step) % files.Length]);
        }

        foreach (string candidate in ordered)
            if (await tryDisplay(candidate)) return candidate;
        return null;
    }
}
