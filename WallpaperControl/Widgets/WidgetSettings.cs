using Microsoft.Win32;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace WallpaperControl
{
    internal sealed class WidgetSettings
    {
        internal const string RegistryPath = @"Software\WallpaperControl";

        public bool ClockEnabled { get; set; }
        public bool ClockLocked { get; set; }
        public int ClockSize { get; set; } = 150;
        public bool ClockShowSeconds { get; set; }
        public string ClockLanguageCode { get; set; } = Localization.CurrentLanguage;
        public Point ClockLocation { get; set; } = new(40, 40);
        public bool NextEnabled { get; set; }
        public bool NextLocked { get; set; }
        public Point NextLocation { get; set; } = new(40, 330);

        public static WidgetSettings Load()
        {
            WidgetSettings result = new();
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryPath);
                if (key == null) return result;

                result.ClockEnabled = ReadBool(key, "ClockWidgetEnabled", false);
                result.ClockLocked = ReadBool(key, "ClockWidgetLocked", false);
                result.ClockSize = Math.Clamp(ReadInt(key, "ClockWidgetSize", 150), 70, 240);
                result.ClockShowSeconds = ReadBool(key, "ClockWidgetShowSeconds", false);
                result.ClockLanguageCode = Localization.CurrentLanguage;
                result.ClockLocation = new Point(ReadInt(key, "ClockWidgetX", 40), ReadInt(key, "ClockWidgetY", 40));
                result.NextEnabled = ReadBool(key, "NextWidgetEnabled", false);
                result.NextLocked = ReadBool(key, "NextWidgetLocked", false);
                result.NextLocation = new Point(ReadInt(key, "NextWidgetX", 40), ReadInt(key, "NextWidgetY", 330));
            }
            catch (Exception ex)
            {
                AppLogger.Warning("Widget settings could not be loaded.", ex);
            }
            return result;
        }

        public void Save()
        {
            try
            {
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath);
                key.SetValue("ClockWidgetEnabled", ClockEnabled ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("ClockWidgetLocked", ClockLocked ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("ClockWidgetSize", Math.Clamp(ClockSize, 70, 240), RegistryValueKind.DWord);
                key.SetValue("ClockWidgetShowSeconds", ClockShowSeconds ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("ClockWidgetX", ClockLocation.X, RegistryValueKind.DWord);
                key.SetValue("ClockWidgetY", ClockLocation.Y, RegistryValueKind.DWord);
                key.SetValue("NextWidgetEnabled", NextEnabled ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("NextWidgetLocked", NextLocked ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("NextWidgetX", NextLocation.X, RegistryValueKind.DWord);
                key.SetValue("NextWidgetY", NextLocation.Y, RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                AppLogger.Warning("Widget settings could not be saved.", ex);
            }
        }

        public WidgetSettings Clone() => new()
        {
            ClockEnabled = ClockEnabled,
            ClockLocked = ClockLocked,
            ClockSize = ClockSize,
            ClockShowSeconds = ClockShowSeconds,
            ClockLanguageCode = ClockLanguageCode,
            ClockLocation = ClockLocation,
            NextEnabled = NextEnabled,
            NextLocked = NextLocked,
            NextLocation = NextLocation
        };

        public static Point EnsureVisible(Point location, Size size)
        {
            Rectangle candidate = new(location, size);
            foreach (Screen screen in Screen.AllScreens)
            {
                if (Rectangle.Intersect(candidate, screen.WorkingArea).Width >= 30 &&
                    Rectangle.Intersect(candidate, screen.WorkingArea).Height >= 30)
                    return location;
            }

            Rectangle area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
            return new Point(area.Left + 30, area.Top + 30);
        }

        private static bool ReadBool(RegistryKey key, string name, bool fallback) =>
            key.GetValue(name) is object value ? Convert.ToInt32(value) != 0 : fallback;

        private static int ReadInt(RegistryKey key, string name, int fallback) =>
            key.GetValue(name) is object value ? Convert.ToInt32(value) : fallback;
    }
}
