using Microsoft.Win32;
using WallpaperControl;

internal static class SettingsTests
{
    internal static void Run(Action<bool, string> check)
    {
        // Every write and the final cleanup target only this unique test key.
        string path = @"Software\WallpaperControl.RegressionTests-" + Guid.NewGuid().ToString("N");
        var store = new AppSettingsStore(path);
        try
        {
            check(store.LoadCloseToTraySetting() && store.LoadAutomaticUpdateCheckSetting() &&
                store.LoadThemeMode() == "system" && store.LoadWindowOpacityPercent() == 92 &&
                store.LoadLastWallpaperFolder() == null,
                "Missing settings retain existing defaults");
            var hotkeys = store.LoadHotkeySettings();
            var reject = store.LoadRejectSettings();
            check(hotkeys.NextModifiers == 3 && hotkeys.NextKey == 0x27 && hotkeys.PauseKey == 0x50 &&
                hotkeys.ExplorerKey == 0x45 && hotkeys.RejectModifiers == 7 && hotkeys.RejectKey == 0x52 &&
                reject.RootFolder == "" && reject.UseSubfolder,
                "Hotkey and rejection defaults remain compatible");
            store.SaveCloseToTraySetting(false);
            store.SaveAutomaticUpdateCheckSetting(false);
            store.SaveThemeMode(" DARK ");
            store.SaveWindowOpacityPercent(85);
            store.SaveLastWallpaperFolder(@"C:\Images");
            check(!store.LoadCloseToTraySetting() && !store.LoadAutomaticUpdateCheckSetting() &&
                store.LoadThemeMode() == "dark" && store.LoadWindowOpacityPercent() == 85 &&
                store.LoadLastWallpaperFolder() == @"C:\Images",
                "Appearance and general settings round-trip");
            using (var key = Registry.CurrentUser.OpenSubKey(path)!)
                check(key.GetValueKind("CloseToTray") == RegistryValueKind.DWord &&
                    key.GetValueKind("WindowOpacity") == RegistryValueKind.DWord &&
                    key.GetValueKind("ThemeMode") == RegistryValueKind.String &&
                    key.GetValueKind("LastWallpaperFolder") == RegistryValueKind.String,
                    "Existing registry value names and types preserved");
            store.SaveWindowOpacityPercent(20);
            check(store.LoadWindowOpacityPercent() == 80, "Opacity lower bound retained");
            store.SaveWindowOpacityPercent(200);
            check(store.LoadWindowOpacityPercent() == 100, "Opacity upper bound retained");
            hotkeys.NextModifiers = 0;
            hotkeys.NextKey = 0;
            hotkeys.PauseKey = 0x71;
            store.SaveHotkeySettings(hotkeys);
            hotkeys = store.LoadHotkeySettings();
            check(hotkeys.NextModifiers == 0 && hotkeys.NextKey == 0 && hotkeys.PauseKey == 0x71,
                "Disabled and customized hotkeys round-trip");
            store.SaveRejectSettings(new RejectSettings { RootFolder = @"C:\Rejected", UseSubfolder = false });
            reject = store.LoadRejectSettings();
            check(reject.RootFolder == @"C:\Rejected" && !reject.UseSubfolder,
                "Rejection settings round-trip");
            using (var key = Registry.CurrentUser.OpenSubKey(path, writable: true)!)
            {
                key.SetValue("CloseToTray", "invalid", RegistryValueKind.String);
                key.SetValue("AutomaticUpdateCheck", "invalid", RegistryValueKind.String);
                key.SetValue("WindowOpacity", "invalid", RegistryValueKind.String);
                key.SetValue("ThemeMode", "unknown", RegistryValueKind.String);
                key.SetValue("HotkeyNextKey", -1, RegistryValueKind.DWord);
                key.SetValue("RejectRootFolder", "relative-folder", RegistryValueKind.String);
            }
            check(store.LoadCloseToTraySetting() && store.LoadAutomaticUpdateCheckSetting() &&
                store.LoadWindowOpacityPercent() == 92 && store.LoadThemeMode() == "system" &&
                store.LoadHotkeySettings().NextKey == 0x27 && store.LoadRejectSettings().RootFolder == "",
                "Invalid legacy settings fall back safely");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
    }
}
