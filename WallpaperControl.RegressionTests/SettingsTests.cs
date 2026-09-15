using Microsoft.Win32;
using System.Drawing;
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
            check(store.LoadTransitionSettings() == new TransitionSettings(0, 2000, 0, 0) && store.LoadWindowPosition() == null,
                "Missing transition and position settings preserve defaults");
            for (int legacyIndex = 0; legacyIndex <= 6; legacyIndex++)
            {
                using var key = Registry.CurrentUser.OpenSubKey(path, writable: true)!;
                key.SetValue("TransitionKind", legacyIndex, RegistryValueKind.DWord);
                check(store.LoadTransitionSettings().KindIndex == legacyIndex,
                    "Legacy transition index retained: " + legacyIndex);
            }
            store.SaveTransitionKind(WallpaperTransitionKind.DesktopZoomFade);
            store.SaveTransitionDuration(3000);
            store.SaveTransitionDirection(4);
            store.SaveTransitionZoomMode(1);
            check(store.LoadTransitionSettings() == new TransitionSettings(3, 3000, 4, 1),
                "Named transition, duration, direction and zoom round-trip");
            using (var key = Registry.CurrentUser.OpenSubKey(path, writable: true)!)
            {
                check(key.GetValueKind("TransitionKind") == RegistryValueKind.String &&
                    key.GetValueKind("TransitionDirection") == RegistryValueKind.DWord &&
                    key.GetValueKind("TransitionDurationMilliseconds") == RegistryValueKind.DWord,
                    "Transition registry value types retained");
                key.SetValue("TransitionDurationMilliseconds", 2200, RegistryValueKind.DWord);
            }
            check(store.LoadTransitionSettings().DurationMilliseconds == 2000,
                "Unsupported duration falls back to an available dropdown value");
            using (var key = Registry.CurrentUser.OpenSubKey(path, writable: true)!)
            {
                key.SetValue("TransitionKind", "invalid", RegistryValueKind.String);
                key.SetValue("TransitionDirection", "invalid", RegistryValueKind.String);
                key.SetValue("TransitionZoomMode", "invalid", RegistryValueKind.String);
            }
            check(store.LoadTransitionSettings() == new TransitionSettings(0, 2000, 0, 0),
                "Invalid transition settings retain fallback behavior");
            store.SaveWindowPosition(new Point(-1500, 120));
            check(store.LoadWindowPosition() == new Point(-1500, 120),
                "Negative monitor coordinates round-trip");
            var primary = new Rectangle(0, 0, 1920, 1040);
            var secondary = new Rectangle(-1920, 0, 1920, 1040);
            check(AppSettingsStore.IsWindowPositionVisible(new Rectangle(-1500, 120, 600, 500), new[] { primary, secondary }) &&
                !AppSettingsStore.IsWindowPositionVisible(new Rectangle(-1500, 120, 600, 500), new[] { primary }),
                "Disconnected monitor positions trigger existing centering fallback");
            check(AppSettingsStore.IsWindowPositionVisible(new Rectangle(1800, 960, 600, 500), new[] { primary }) &&
                !AppSettingsStore.IsWindowPositionVisible(new Rectangle(1801, 960, 600, 500), new[] { primary }),
                "Minimum visible area remains 120 by 80 pixels");
            using (var key = Registry.CurrentUser.OpenSubKey(path, writable: true)!)
                key.SetValue("WindowX", "invalid", RegistryValueKind.String);
            check(store.LoadWindowPosition() == null, "Invalid position triggers centering fallback");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
    }
}
