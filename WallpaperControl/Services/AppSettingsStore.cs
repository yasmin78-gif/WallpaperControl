using Microsoft.Win32;
using System;
using System.IO;

namespace WallpaperControl
{
    internal sealed class HotkeySettings
    {
        public uint NextModifiers { get; set; } = 3;
        public uint NextKey { get; set; } = 0x27;
        public uint PauseModifiers { get; set; } = 3;
        public uint PauseKey { get; set; } = 0x50;
        public uint ExplorerModifiers { get; set; } = 3;
        public uint ExplorerKey { get; set; } = 0x45;
        public uint RejectModifiers { get; set; } = 7;
        public uint RejectKey { get; set; } = 0x52;
    }

    internal sealed class RejectSettings
    {
        public string RootFolder { get; set; } = "";
        public bool UseSubfolder { get; set; } = true;
    }

    // Uses the existing registry names and value types. An isolated path can be
    // supplied for tests, leaving real user settings untouched.
    internal sealed class AppSettingsStore
    {
        private readonly string registryPath;
        internal AppSettingsStore(string registryPath = @"Software\WallpaperControl")
        {
            this.registryPath = registryPath;
        }
        internal void SaveLastWallpaperFolder(
            string path)
        {
            try
            {
                using RegistryKey key =
                    Registry.CurrentUser.CreateSubKey(
                        registryPath);

                key.SetValue(
                    "LastWallpaperFolder",
                    path,
                    RegistryValueKind.String);
            }
            catch
            {
            }
        }

        internal string? LoadLastWallpaperFolder()
        {
            try
            {
                using RegistryKey? key =
                    Registry.CurrentUser.OpenSubKey(
                        registryPath);

                return key?.GetValue(
                    "LastWallpaperFolder")
                    as string;
            }
            catch
            {
                return null;
            }
        }

        internal bool LoadCloseToTraySetting()
        {
            try
            {
                using RegistryKey? key =
                    Registry.CurrentUser.OpenSubKey(
                        registryPath);

                object? value =
                    key?.GetValue(
                        "CloseToTray");

                return
                    value == null ||
                    Convert.ToInt32(value) != 0;
            }
            catch
            {
                return true;
            }
        }

        internal void SaveCloseToTraySetting(
            bool enabled)
        {
            try
            {
                using RegistryKey key =
                    Registry.CurrentUser.CreateSubKey(
                        registryPath);

                key.SetValue(
                    "CloseToTray",
                    enabled ? 1 : 0,
                    RegistryValueKind.DWord);
            }
            catch
            {
            }
        }

        internal bool LoadAutomaticUpdateCheckSetting()
        {
            try
            {
                using RegistryKey? key =
                    Registry.CurrentUser.OpenSubKey(
                        registryPath);

                object? value =
                    key?.GetValue(
                        "AutomaticUpdateCheck");

                return
                    value == null ||
                    Convert.ToInt32(value) != 0;
            }
            catch
            {
                return true;
            }
        }

        internal void SaveAutomaticUpdateCheckSetting(
            bool enabled)
        {
            try
            {
                using RegistryKey key =
                    Registry.CurrentUser.CreateSubKey(
                        registryPath);

                key.SetValue(
                    "AutomaticUpdateCheck",
                    enabled ? 1 : 0,
                    RegistryValueKind.DWord);
            }
            catch
            {
            }
        }

        internal string LoadThemeMode()
        {
            try
            {
                using RegistryKey? key =
                    Registry.CurrentUser.OpenSubKey(
                        registryPath);

                string? value =
                    key?.GetValue(
                        "ThemeMode")
                    as string;

                return NormalizeThemeMode(
                    value);
            }
            catch
            {
                return "system";
            }
        }

        internal void SaveThemeMode(
            string value)
        {
            try
            {
                using RegistryKey key =
                    Registry.CurrentUser.CreateSubKey(
                        registryPath);

                key.SetValue(
                    "ThemeMode",
                    NormalizeThemeMode(value),
                    RegistryValueKind.String);
            }
            catch
            {
            }
        }

        internal static string NormalizeThemeMode(
            string? value)
        {
            return value?.Trim().ToLowerInvariant() switch
            {
                "dark" => "dark",
                "light" => "light",
                _ => "system"
            };
        }

        internal int LoadWindowOpacityPercent()
        {
            try
            {
                using RegistryKey? key =
                    Registry.CurrentUser.OpenSubKey(
                        registryPath);

                object? value =
                    key?.GetValue(
                        "WindowOpacity");

                if (value != null)
                {
                    return Math.Clamp(
                        Convert.ToInt32(value),
                        80,
                        100);
                }
            }
            catch
            {
            }

            return 92;
        }

        internal void SaveWindowOpacityPercent(
            int value)
        {
            try
            {
                using RegistryKey key =
                    Registry.CurrentUser.CreateSubKey(
                        registryPath);

                key.SetValue(
                    "WindowOpacity",
                    Math.Clamp(value, 80, 100),
                    RegistryValueKind.DWord);
            }
            catch
            {
            }
        }

        internal HotkeySettings LoadHotkeySettings()
        {
            var settings = new HotkeySettings();
            try
            {
                using RegistryKey? key =
                    Registry.CurrentUser.OpenSubKey(
                        registryPath);

                if (key == null)
                {
                    return settings;
                }

                settings.NextModifiers =
                    ReadRegistryUInt(
                        key,
                        "HotkeyNextModifiers",
                        0x0002 | 0x0001);

                settings.NextKey =
                    ReadRegistryUInt(
                        key,
                        "HotkeyNextKey",
                        0x27);

                settings.PauseModifiers =
                    ReadRegistryUInt(
                        key,
                        "HotkeyPauseModifiers",
                        0x0002 | 0x0001);

                settings.PauseKey =
                    ReadRegistryUInt(
                        key,
                        "HotkeyPauseKey",
                        0x50);

                settings.ExplorerModifiers =
                    ReadRegistryUInt(
                        key,
                        "HotkeyExplorerModifiers",
                        0x0002 | 0x0001);

                settings.ExplorerKey =
                    ReadRegistryUInt(
                        key,
                        "HotkeyExplorerKey",
                        0x45);

                settings.RejectModifiers =
                    ReadRegistryUInt(
                        key,
                        "HotkeyRejectModifiers",
                        0x0002 | 0x0001 | 0x0004);

                settings.RejectKey =
                    ReadRegistryUInt(
                        key,
                        "HotkeyRejectKey",
                        0x52);
            }
            catch
            {
            }
            return settings;
        }

        internal static uint ReadRegistryUInt(
            RegistryKey key,
            string name,
            uint defaultValue)
        {
            object? value =
                key.GetValue(name);

            if (value == null)
            {
                return defaultValue;
            }

            try
            {
                return Convert.ToUInt32(value);
            }
            catch
            {
                return defaultValue;
            }
        }

        internal void SaveHotkeySettings(HotkeySettings settings)
        {
            try
            {
                using RegistryKey key =
                    Registry.CurrentUser.CreateSubKey(
                        registryPath);

                key.SetValue(
                    "HotkeyNextModifiers",
                    settings.NextModifiers,
                    RegistryValueKind.DWord);

                key.SetValue(
                    "HotkeyNextKey",
                    settings.NextKey,
                    RegistryValueKind.DWord);

                key.SetValue(
                    "HotkeyPauseModifiers",
                    settings.PauseModifiers,
                    RegistryValueKind.DWord);

                key.SetValue(
                    "HotkeyPauseKey",
                    settings.PauseKey,
                    RegistryValueKind.DWord);

                key.SetValue(
                    "HotkeyExplorerModifiers",
                    settings.ExplorerModifiers,
                    RegistryValueKind.DWord);

                key.SetValue(
                    "HotkeyExplorerKey",
                    settings.ExplorerKey,
                    RegistryValueKind.DWord);

                key.SetValue(
                    "HotkeyRejectModifiers",
                    settings.RejectModifiers,
                    RegistryValueKind.DWord);

                key.SetValue(
                    "HotkeyRejectKey",
                    settings.RejectKey,
                    RegistryValueKind.DWord);
            }
            catch
            {
            }
        }

        internal RejectSettings LoadRejectSettings()
        {
            var settings = new RejectSettings();
            try
            {
                using RegistryKey? key =
                    Registry.CurrentUser.OpenSubKey(
                        registryPath);

                if (key == null)
                {
                    return settings;
                }

                string storedRejectRoot =
                    key.GetValue(
                        "RejectRootFolder")
                    as string ?? "";

                settings.RootFolder =
                    NormalizeRejectRootFolder(storedRejectRoot);

                object? subfolderValue =
                    key.GetValue(
                        "RejectUseSubfolder");

                if (subfolderValue != null)
                {
                    settings.UseSubfolder =
                        Convert.ToInt32(
                            subfolderValue) != 0;
                }
            }
            catch
            {
            }
            return settings;
        }

        internal static string NormalizeRejectRootFolder(
            string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                string trimmed = path.Trim();

                return Path.IsPathFullyQualified(trimmed)
                    ? Path.GetFullPath(trimmed)
                    : string.Empty;
            }
            catch (Exception ex) when (
                ex is ArgumentException or
                NotSupportedException or
                PathTooLongException)
            {
                return string.Empty;
            }
        }

        internal void SaveRejectSettings(RejectSettings settings)
        {
            try
            {
                using RegistryKey key =
                    Registry.CurrentUser.CreateSubKey(
                        registryPath);

                key.SetValue(
                    "RejectRootFolder",
                    settings.RootFolder,
                    RegistryValueKind.String);

                key.SetValue(
                    "RejectUseSubfolder",
                    settings.UseSubfolder ? 1 : 0,
                    RegistryValueKind.DWord);
            }
            catch
            {
            }
        }
    }
}
