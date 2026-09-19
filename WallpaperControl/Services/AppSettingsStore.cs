using Microsoft.Win32;
using System;
using System.IO;
using System.Drawing;
using System.Collections.Generic;

namespace WallpaperControl
{
    /// <summary>
    /// Holds transition preferences after legacy registry values have been normalized.
    /// </summary>
    /// <param name="KindIndex">The transition effect selection index.</param>
    /// <param name="DurationMilliseconds">The animation duration in milliseconds.</param>
    /// <param name="DirectionIndex">The movement-direction selection index.</param>
    /// <param name="ZoomMode">The zoom-mode selection index.</param>
    internal sealed record TransitionSettings(int KindIndex, int DurationMilliseconds, int DirectionIndex, int ZoomMode);

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
        /// <summary>
        /// Selects the application registry key or an isolated key supplied by a test.
        /// </summary>
        /// <param name="registryPath">The registry subkey containing these preferences; tests use an isolated subkey.</param>
        internal AppSettingsStore(string registryPath = @"Software\WallpaperControl")
        {
            this.registryPath = registryPath;
        }
        /// <summary>
        /// Reads the fullscreen suspension preference, enabled by default.
        /// </summary>
        /// <returns>The stored preference, or true when no valid preference is available.</returns>
        internal bool LoadPauseOnFullscreen()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(registryPath);
                object? value = key?.GetValue("PauseOnFullscreen");
                return value == null || Convert.ToInt32(value) != 0;
            }
            catch { return true; }
        }
        /// <summary>
        /// Persists whether fullscreen applications automatically suspend background activity.
        /// </summary>
        /// <param name="enabled">True to suspend wallpaper activity during fullscreen applications.</param>
        internal void SavePauseOnFullscreen(bool enabled) => WriteValue("PauseOnFullscreen", enabled ? 1 : 0, RegistryValueKind.DWord);
        /// <summary>
        /// Persists the most recently selected wallpaper source folder.
        /// </summary>
        /// <param name="path">The image or folder path to process.</param>
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
            catch (Exception ex) { SettingsPersistence.ReportFailure("Could not persist SaveLastWallpaperFolder.", ex); }
        }

        /// <summary>
        /// Reads the saved wallpaper source folder when available.
        /// </summary>
        /// <returns>The saved wallpaper folder, or null when no value can be read.</returns>
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

        /// <summary>
        /// Reads whether closing the main window should hide it to the tray.
        /// </summary>
        /// <returns>The stored close-to-tray preference, with its default when unavailable.</returns>
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

        /// <summary>
        /// Persists the close-to-tray preference.
        /// </summary>
        /// <param name="enabled">True to hide the main window in the tray when it is closed.</param>
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
            catch (Exception ex) { SettingsPersistence.ReportFailure("Could not persist SaveCloseToTraySetting.", ex); }
        }

        /// <summary>
        /// Reads whether automatic release checks are enabled.
        /// </summary>
        /// <returns>The stored update-check preference, with its default when unavailable.</returns>
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

        /// <summary>
        /// Persists the preference for automatic release checks.
        /// </summary>
        /// <param name="enabled">True to allow automatic update checks.</param>
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
            catch (Exception ex) { SettingsPersistence.ReportFailure("Could not persist SaveAutomaticUpdateCheckSetting.", ex); }
        }

        /// <summary>
        /// Loads a supported theme name, falling back to the Windows system theme.
        /// </summary>
        /// <returns>The normalized light, dark, or system theme name.</returns>
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

        /// <summary>
        /// Normalizes and persists the selected theme mode.
        /// </summary>
        /// <param name="value">The theme name to persist.</param>
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
            catch (Exception ex) { SettingsPersistence.ReportFailure("Could not persist SaveThemeMode.", ex); }
        }

        /// <summary>
        /// Normalizes dark and light theme names and maps other values to system mode.
        /// </summary>
        /// <param name="value">The stored theme name to validate.</param>
        /// <returns>The normalized dark or light name, or system for other input.</returns>
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

        /// <summary>
        /// Reads the saved window opacity and applies the supported bounds.
        /// </summary>
        /// <returns>The saved opacity clamped to its supported percentage range.</returns>
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

        /// <summary>
        /// Clamps and persists the window opacity percentage.
        /// </summary>
        /// <param name="value">The window opacity percentage to persist.</param>
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
            catch (Exception ex) { SettingsPersistence.ReportFailure("Could not persist SaveWindowOpacityPercent.", ex); }
        }

        /// <summary>
        /// Loads shortcut combinations with compatible defaults for missing or invalid registry values.
        /// </summary>
        /// <returns>The loaded shortcut combinations with defaults for missing or invalid entries.</returns>
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

        /// <summary>
        /// Reads an unsigned shortcut value while tolerating supported legacy registry representations.
        /// </summary>
        /// <param name="key">The registry key containing the setting.</param>
        /// <param name="name">The registry value name.</param>
        /// <param name="defaultValue">The fallback when the stored value is missing or invalid.</param>
        /// <returns>The unsigned value, or the supplied default when it cannot be read.</returns>
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

        /// <summary>
        /// Persists all configured global shortcut combinations.
        /// </summary>
        /// <param name="settings">The settings to persist.</param>
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
            catch (Exception ex) { SettingsPersistence.ReportFailure("Could not persist SaveHotkeySettings.", ex); }
        }

        /// <summary>
        /// Loads the rejection root folder and subfolder preference.
        /// </summary>
        /// <returns>The loaded rejection folder preferences.</returns>
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

        /// <summary>
        /// Normalizes a configured rejection folder or falls back when its path is invalid.
        /// </summary>
        /// <param name="path">The image or folder path to process.</param>
        /// <returns>The normalized rejection folder or an empty fallback for invalid input.</returns>
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

        /// <summary>
        /// Persists the validated rejection folder preferences.
        /// </summary>
        /// <param name="settings">The settings to persist.</param>
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
            catch (Exception ex) { SettingsPersistence.ReportFailure("Could not persist SaveRejectSettings.", ex); }
        }
        /// <summary>
        /// Loads transition preferences while supporting legacy effect indices and fallback durations.
        /// </summary>
        /// <returns>The transition preferences with legacy mappings and supported fallbacks applied.</returns>
        internal TransitionSettings LoadTransitionSettings()
        {
            int transitionIndex = 0;
            int duration = 2000;
            int zoomMode = 0;

            try
            {
                using RegistryKey? key =
                    Registry.CurrentUser.OpenSubKey(
                        registryPath);

                object? transitionValue =
                    key?.GetValue(
                        "TransitionKind");

                if (transitionValue is string transitionName &&
                    Enum.TryParse(
                        transitionName,
                        ignoreCase: true,
                        out WallpaperTransitionKind storedKind) &&
                    Enum.IsDefined(storedKind))
                {
                    transitionIndex =
                        TransitionKindToIndex(storedKind);
                }
                else if (transitionValue != null)
                {
                    // Backward compatibility with v1.7.1 and older,
                    // which stored the ComboBox index as a DWORD.
                    transitionIndex =
                        Math.Clamp(
                            Convert.ToInt32(
                                transitionValue),
                            0,
                            6);
                }

                object? durationValue =
                    key?.GetValue(
                        "TransitionDurationMilliseconds");

                if (durationValue != null)
                {
                    duration =
                        Math.Clamp(
                            Convert.ToInt32(durationValue),
                            500,
                            5000);
                }
            }
            catch
            {
                transitionIndex = 0;
                duration = 2000;
            }

            int directionIndex = 0;

            try
            {
                using RegistryKey? key =
                    Registry.CurrentUser.OpenSubKey(registryPath);

                object? directionValue =
                    key?.GetValue("TransitionDirection");

                if (directionValue != null)
                {
                    directionIndex =
                        Math.Clamp(
                            Convert.ToInt32(directionValue),
                            0,
                            4);
                }
            }
            catch
            {
                directionIndex = 0;
            }

            try
            {
                using RegistryKey? key =
                    Registry.CurrentUser.OpenSubKey(registryPath);

                object? zoomModeValue =
                    key?.GetValue("TransitionZoomMode");

                if (zoomModeValue != null &&
                    Convert.ToInt32(zoomModeValue) == 1)
                {
                    zoomMode = 1;
                }
                else
                {
                    zoomMode = 0;
                }
            }
            catch
            {
                zoomMode = 0;
            }



            int[] durations =
                { 500, 1000, 1500, 2000, 3000, 5000 };

            int index =
                Array.IndexOf(
                    durations,
                    duration);

            if (index < 0)
            {
                index = 3;
                duration = 2000;
            }

            return new TransitionSettings(transitionIndex, duration, directionIndex, zoomMode);
        }

        /// <summary>
        /// Maps a transition effect to its compatibility index in the effect selector.
        /// </summary>
        /// <param name="kind">The transition effect to map to a selection index.</param>
        /// <returns>The effect&apos;s compatibility index in the transition selector.</returns>
        internal static int TransitionKindToIndex(
            WallpaperTransitionKind kind)
        {
            return kind switch
            {
                WallpaperTransitionKind.DesktopSlide => 1,
                WallpaperTransitionKind.DesktopFade => 2,
                WallpaperTransitionKind.DesktopZoomFade => 3,
                WallpaperTransitionKind.DesktopSplit => 4,
                WallpaperTransitionKind.DesktopCurtain => 5,
                WallpaperTransitionKind.DesktopRandom => 6,
                _ => 0
            };
        }

        /// <summary>
        /// Persists the selected transition effect in the supported registry representation.
        /// </summary>
        /// <param name="kind">The wallpaper transition effect to persist.</param>
        internal void SaveTransitionKind(WallpaperTransitionKind kind) => WriteValue("TransitionKind", kind.ToString(), RegistryValueKind.String);
        /// <summary>
        /// Persists the selected transition duration in milliseconds.
        /// </summary>
        /// <param name="duration">The transition duration in milliseconds.</param>
        internal void SaveTransitionDuration(int duration) => WriteValue("TransitionDurationMilliseconds", duration, RegistryValueKind.DWord);
        /// <summary>
        /// Persists the selected direction index.
        /// </summary>
        /// <param name="index">The selected transition-direction index.</param>
        internal void SaveTransitionDirection(int index) => WriteValue("TransitionDirection", Math.Clamp(index, 0, 4), RegistryValueKind.DWord);
        /// <summary>
        /// Persists the selected zoom-mode index.
        /// </summary>
        /// <param name="mode">The selected zoom-mode index.</param>
        internal void SaveTransitionZoomMode(int mode) => WriteValue("TransitionZoomMode", mode == 1 ? 1 : 0, RegistryValueKind.DWord);

        /// <summary>
        /// Writes a typed application preference while handling registry failures consistently.
        /// </summary>
        /// <param name="name">The registry value name.</param>
        /// <param name="value">The setting value to persist.</param>
        /// <param name="kind">The registry data type used to store the value.</param>
        private void WriteValue(string name, object value, RegistryValueKind kind)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(registryPath);
                key.SetValue(name, value, kind);
            }
            catch (Exception ex) { SettingsPersistence.ReportFailure("Could not persist WriteValue.", ex); }
        }

        /// <summary>
        /// Persists the main window&apos;s screen coordinates.
        /// </summary>
        /// <param name="position">The window location in screen coordinates.</param>
        internal void SaveWindowPosition(Point position)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(registryPath);
                key.SetValue("WindowX", position.X, RegistryValueKind.DWord);
                key.SetValue("WindowY", position.Y, RegistryValueKind.DWord);
            }
            catch (Exception ex) { SettingsPersistence.ReportFailure("Could not persist SaveWindowPosition.", ex); }
        }

        /// <summary>
        /// Reads the saved window position when both coordinates are valid.
        /// </summary>
        /// <returns>The saved position, or null when either coordinate is unavailable or invalid.</returns>
        internal Point? LoadWindowPosition()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(registryPath);
                object? x = key?.GetValue("WindowX"), y = key?.GetValue("WindowY");
                if (x != null && y != null) return new Point(Convert.ToInt32(x), Convert.ToInt32(y));
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Checks whether enough of a saved window overlaps a connected monitor&apos;s working area.
        /// </summary>
        /// <param name="bounds">The rectangle used for drawing or visibility checks.</param>
        /// <param name="workingAreas">The usable screen rectangles of the connected monitors.</param>
        /// <returns>True when the required portion of the window intersects a connected working area.</returns>
        internal static bool IsWindowPositionVisible(Rectangle bounds, IEnumerable<Rectangle> workingAreas)
        {
            foreach (var area in workingAreas)
            {
                Rectangle visible = Rectangle.Intersect(bounds, area);
                if (visible.Width >= 120 && visible.Height >= 80) return true;
            }
            return false;
        }
    }
}
