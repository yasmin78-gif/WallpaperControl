extern alias WallpaperApp;

namespace WallpaperControl;

internal static class Localization
{
    internal static string Get(string key, string language) => WallpaperApp::WallpaperControl.Localization.Get(key, language);
}

internal static class WindowsSecretProtector
{
    internal static string Protect(string value) => WallpaperApp::WallpaperControl.WindowsSecretProtector.Protect(value);
    internal static string Unprotect(string value) => WallpaperApp::WallpaperControl.WindowsSecretProtector.Unprotect(value);
}
