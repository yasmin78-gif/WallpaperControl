using System.Text.Json;
namespace WallpaperControl;
internal static class SystemDriveSelection
{
    internal static string[] Default => Normalize([Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\"]);
    internal static string[] Normalize(IEnumerable<string>? drives) => (drives ?? []).Where(s => s != null)
        .Select(s => s.Trim().TrimEnd('\\','/').ToUpperInvariant())
        .Where(s => s.Length == 2 && s[0] is >= 'A' and <= 'Z' && s[1] == ':')
        .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    internal static string[] Parse(string? json)
    {
        if (json == null) return Default;
        try { var value = JsonSerializer.Deserialize<string[]>(json); return value == null ? Default : Normalize(value); }
        catch (JsonException) { return Default; }
    }
    internal static bool IsLocal(DriveType type) => type is DriveType.Fixed or DriveType.Removable;
    internal static string[] Available()
    {
        try { return Normalize(DriveInfo.GetDrives().Where(d => IsLocal(d.DriveType)).Select(d => d.Name)); }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }
    internal static DriveSnapshot[] Visible(IEnumerable<DriveSnapshot> readings,IEnumerable<string> selected)
    {
        var wanted = Normalize(selected).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return readings.Where(d => d.TotalBytes > 0 && wanted.Contains(d.Name.TrimEnd('\\')))
            .OrderBy(d => d.Name,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    internal static bool Warning(DriveSnapshot drive,int threshold) => drive.TotalBytes > 0 && drive.FreeBytes >= 0
        && 100d * Math.Clamp(drive.FreeBytes,0,drive.TotalBytes) / drive.TotalBytes < Math.Clamp(threshold,1,50);
}
