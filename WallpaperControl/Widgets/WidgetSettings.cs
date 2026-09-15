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
        public ClockWidgetStyle ClockStyle { get; set; } = ClockWidgetStyle.Chrome;
        public string ClockLanguageCode { get; set; } = Localization.CurrentLanguage;
        public Point ClockLocation { get; set; } = new(40, 40);
        public bool NextEnabled { get; set; }
        public bool NextLocked { get; set; }
        public Point NextLocation { get; set; } = new(40, 330);
        public bool SystemEnabled { get; set; }
        public bool SystemLocked { get; set; }
        public int SystemRefreshSeconds { get; set; } = 2;
        public SystemWidgetStyle SystemStyle { get; set; } = SystemWidgetStyle.Glow;
        public bool SystemShowCpu { get; set; } = true;
        public bool SystemShowRam { get; set; } = true;
        public bool SystemShowGpu { get; set; } = true;
        public bool SystemShowVram { get; set; } = true;
        public bool SystemShowNetwork { get; set; } = true;
        public bool SystemShowDrives { get; set; } = true;
        public Point SystemLocation { get; set; } = new(40, 400);
        public bool WeatherEnabled { get; set; }
        public bool WeatherLocked { get; set; }
        public int WeatherRefreshMinutes { get; set; } = 30;
        public SystemWidgetStyle WeatherStyle { get; set; } = SystemWidgetStyle.Glow;
        public string WeatherLocationName { get; set; } = "Karlsruhe";
        public bool WeatherShowForecast { get; set; } = true;
        public Point WeatherLocation { get; set; } = new(390, 400);
        public bool CalendarEnabled { get; set; }
        public bool CalendarLocked { get; set; }
        public SystemWidgetStyle CalendarStyle { get; set; } = SystemWidgetStyle.Glow;
        public int CalendarMaxEntries { get; set; } = 9;
        public bool CalendarShowLocation { get; set; } = true;
        public int CalendarRefreshMinutes { get; set; } = 30;
        public string CalendarIcsUrl { get; set; } = string.Empty;
        public string CalendarHolidayIcsUrl { get; set; } = string.Empty;
        public Point CalendarLocation { get; set; } = new(740, 400);

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
                result.ClockStyle = ReadClockStyle(key, "ClockWidgetStyle", ClockWidgetStyle.Chrome);
                result.ClockLanguageCode = Localization.CurrentLanguage;
                result.ClockLocation = new Point(ReadInt(key, "ClockWidgetX", 40), ReadInt(key, "ClockWidgetY", 40));
                result.NextEnabled = ReadBool(key, "NextWidgetEnabled", false);
                result.NextLocked = ReadBool(key, "NextWidgetLocked", false);
                result.NextLocation = new Point(ReadInt(key, "NextWidgetX", 40), ReadInt(key, "NextWidgetY", 330));
                result.SystemEnabled = ReadBool(key, "SystemWidgetEnabled", false);
                result.SystemLocked = ReadBool(key, "SystemWidgetLocked", false);
                result.SystemRefreshSeconds = Math.Clamp(ReadInt(key, "SystemWidgetRefreshSeconds", 2), 1, 5);
                result.SystemStyle = ReadSystemStyle(key, "SystemWidgetStyle", SystemWidgetStyle.Glow);
                result.SystemShowCpu = ReadBool(key, "SystemWidgetShowCpu", true);
                result.SystemShowRam = ReadBool(key, "SystemWidgetShowRam", true);
                result.SystemShowGpu = ReadBool(key, "SystemWidgetShowGpu", true);
                result.SystemShowVram = ReadBool(key, "SystemWidgetShowVram", true);
                result.SystemShowNetwork = ReadBool(key, "SystemWidgetShowNetwork", true);
                result.SystemShowDrives = ReadBool(key, "SystemWidgetShowDrives", true);
                result.SystemLocation = new Point(ReadInt(key, "SystemWidgetX", 40), ReadInt(key, "SystemWidgetY", 400));
                result.WeatherEnabled = ReadBool(key, "WeatherWidgetEnabled", false);
                result.WeatherLocked = ReadBool(key, "WeatherWidgetLocked", false);
                result.WeatherRefreshMinutes = Math.Clamp(ReadInt(key, "WeatherWidgetRefreshMinutes", 30), 15, 120);
                result.WeatherStyle = ReadSystemStyle(key, "WeatherWidgetStyle", SystemWidgetStyle.Glow);
                result.WeatherLocationName = Convert.ToString(key.GetValue("WeatherWidgetLocation", "Karlsruhe"))?.Trim() ?? "Karlsruhe";
                if (string.IsNullOrWhiteSpace(result.WeatherLocationName)) result.WeatherLocationName = "Karlsruhe";
                result.WeatherShowForecast = ReadBool(key, "WeatherWidgetShowForecast", true);
                result.WeatherLocation = new Point(ReadInt(key, "WeatherWidgetX", 390), ReadInt(key, "WeatherWidgetY", 400));
                result.CalendarEnabled = ReadBool(key, "CalendarWidgetEnabled", false);
                result.CalendarLocked = ReadBool(key, "CalendarWidgetLocked", false);
                result.CalendarStyle = ReadSystemStyle(key, "CalendarWidgetStyle", SystemWidgetStyle.Glow);
                int storedCalendarDays = ReadInt(key, "CalendarWidgetMaxEntries", 9);
                result.CalendarMaxEntries = storedCalendarDays <= 3 ? 3 : storedCalendarDays <= 5 ? 5 : 9;
                result.CalendarShowLocation = ReadBool(key, "CalendarWidgetShowLocation", true);
                result.CalendarRefreshMinutes = Math.Clamp(ReadInt(key, "CalendarWidgetRefreshMinutes", 30), 15, 120);
                string protectedCalendarUrl = Convert.ToString(key.GetValue("CalendarWidgetIcsUrlProtected", "")) ?? "";
                result.CalendarIcsUrl = WindowsSecretProtector.Unprotect(protectedCalendarUrl);
                string protectedHolidayCalendarUrl = Convert.ToString(key.GetValue("CalendarWidgetHolidayIcsUrlProtected", "")) ?? "";
                result.CalendarHolidayIcsUrl = WindowsSecretProtector.Unprotect(protectedHolidayCalendarUrl);
                result.CalendarLocation = new Point(ReadInt(key, "CalendarWidgetX", 740), ReadInt(key, "CalendarWidgetY", 400));
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
                key.SetValue("ClockWidgetStyle", (int)ClockStyle, RegistryValueKind.DWord);
                key.SetValue("ClockWidgetX", ClockLocation.X, RegistryValueKind.DWord);
                key.SetValue("ClockWidgetY", ClockLocation.Y, RegistryValueKind.DWord);
                key.SetValue("NextWidgetEnabled", NextEnabled ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("NextWidgetLocked", NextLocked ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("NextWidgetX", NextLocation.X, RegistryValueKind.DWord);
                key.SetValue("NextWidgetY", NextLocation.Y, RegistryValueKind.DWord);
                key.SetValue("SystemWidgetEnabled", SystemEnabled ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("SystemWidgetLocked", SystemLocked ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("SystemWidgetRefreshSeconds", Math.Clamp(SystemRefreshSeconds, 1, 5), RegistryValueKind.DWord);
                key.SetValue("SystemWidgetStyle", (int)SystemStyle, RegistryValueKind.DWord);
                key.SetValue("SystemWidgetShowCpu", SystemShowCpu ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("SystemWidgetShowRam", SystemShowRam ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("SystemWidgetShowGpu", SystemShowGpu ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("SystemWidgetShowVram", SystemShowVram ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("SystemWidgetShowNetwork", SystemShowNetwork ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("SystemWidgetShowDrives", SystemShowDrives ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("SystemWidgetX", SystemLocation.X, RegistryValueKind.DWord);
                key.SetValue("SystemWidgetY", SystemLocation.Y, RegistryValueKind.DWord);
                key.SetValue("WeatherWidgetEnabled", WeatherEnabled ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("WeatherWidgetLocked", WeatherLocked ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("WeatherWidgetRefreshMinutes", Math.Clamp(WeatherRefreshMinutes, 15, 120), RegistryValueKind.DWord);
                key.SetValue("WeatherWidgetStyle", (int)WeatherStyle, RegistryValueKind.DWord);
                key.SetValue("WeatherWidgetLocation", WeatherLocationName?.Trim() ?? "", RegistryValueKind.String);
                key.SetValue("WeatherWidgetShowForecast", WeatherShowForecast ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("WeatherWidgetX", WeatherLocation.X, RegistryValueKind.DWord);
                key.SetValue("WeatherWidgetY", WeatherLocation.Y, RegistryValueKind.DWord);
                key.SetValue("CalendarWidgetEnabled", CalendarEnabled ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("CalendarWidgetLocked", CalendarLocked ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("CalendarWidgetStyle", (int)CalendarStyle, RegistryValueKind.DWord);
                key.SetValue("CalendarWidgetMaxEntries", CalendarMaxEntries <= 3 ? 3 : CalendarMaxEntries <= 5 ? 5 : 9, RegistryValueKind.DWord);
                key.SetValue("CalendarWidgetShowLocation", CalendarShowLocation ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("CalendarWidgetRefreshMinutes", Math.Clamp(CalendarRefreshMinutes, 15, 120), RegistryValueKind.DWord);
                key.SetValue("CalendarWidgetIcsUrlProtected", WindowsSecretProtector.Protect(CalendarIcsUrl?.Trim() ?? ""), RegistryValueKind.String);
                key.SetValue("CalendarWidgetHolidayIcsUrlProtected", WindowsSecretProtector.Protect(CalendarHolidayIcsUrl?.Trim() ?? ""), RegistryValueKind.String);
                key.SetValue("CalendarWidgetX", CalendarLocation.X, RegistryValueKind.DWord);
                key.SetValue("CalendarWidgetY", CalendarLocation.Y, RegistryValueKind.DWord);
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
            ClockStyle = ClockStyle,
            ClockLanguageCode = ClockLanguageCode,
            ClockLocation = ClockLocation,
            NextEnabled = NextEnabled,
            NextLocked = NextLocked,
            NextLocation = NextLocation,
            SystemEnabled = SystemEnabled,
            SystemLocked = SystemLocked,
            SystemRefreshSeconds = SystemRefreshSeconds,
            SystemStyle = SystemStyle,
            SystemShowCpu = SystemShowCpu,
            SystemShowRam = SystemShowRam,
            SystemShowGpu = SystemShowGpu,
            SystemShowVram = SystemShowVram,
            SystemShowNetwork = SystemShowNetwork,
            SystemShowDrives = SystemShowDrives,
            SystemLocation = SystemLocation,
            WeatherEnabled = WeatherEnabled,
            WeatherLocked = WeatherLocked,
            WeatherRefreshMinutes = WeatherRefreshMinutes,
            WeatherStyle = WeatherStyle,
            WeatherLocationName = WeatherLocationName,
            WeatherShowForecast = WeatherShowForecast,
            WeatherLocation = WeatherLocation,
            CalendarEnabled = CalendarEnabled,
            CalendarLocked = CalendarLocked,
            CalendarStyle = CalendarStyle,
            CalendarMaxEntries = CalendarMaxEntries,
            CalendarShowLocation = CalendarShowLocation,
            CalendarRefreshMinutes = CalendarRefreshMinutes,
            CalendarIcsUrl = CalendarIcsUrl,
            CalendarHolidayIcsUrl = CalendarHolidayIcsUrl,
            CalendarLocation = CalendarLocation
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

        private static ClockWidgetStyle ReadClockStyle(RegistryKey key, string name, ClockWidgetStyle fallback)
        {
            int value = ReadInt(key, name, (int)fallback);
            return Enum.IsDefined(typeof(ClockWidgetStyle), value)
                ? (ClockWidgetStyle)value
                : fallback;
        }

        private static SystemWidgetStyle ReadSystemStyle(RegistryKey key, string name, SystemWidgetStyle fallback)
        {
            int value = ReadInt(key, name, (int)fallback);
            return Enum.IsDefined(typeof(SystemWidgetStyle), value)
                ? (SystemWidgetStyle)value
                : fallback;
        }
    }
}
