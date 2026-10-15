namespace WallpaperControl;

internal static class WallpaperDiagnostics
{
    internal static string Escape(string? value) => (value ?? "<none>").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
    internal static string PathInfo(string? path) => $"[{Escape(path)}]; exists={File.Exists(path)}";
}
