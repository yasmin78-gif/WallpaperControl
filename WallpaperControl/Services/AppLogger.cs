using System;
using System.IO;
using System.Text;

namespace WallpaperControl
{
    internal static class AppLogger
    {
        private const long MaxLogFileBytes = 2 * 1024 * 1024;
        private static readonly object SyncRoot = new();

        private static string LogDirectory =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WallpaperControl",
                "Logs");

        internal static string LogFilePath =>
            Path.Combine(LogDirectory, "wallpaper-control.log");

        internal static void Error(string context, Exception exception)
        {
            Write("ERROR", context, exception);
        }

        internal static void Warning(string context, Exception exception)
        {
            Write("WARN", context, exception);
        }

        private static void Write(string level, string context, Exception exception)
        {
            try
            {
                lock (SyncRoot)
                {
                    Directory.CreateDirectory(LogDirectory);
                    RotateIfNeeded();

                    StringBuilder entry = new();
                    entry.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                    entry.Append(" [");
                    entry.Append(level);
                    entry.Append("] ");
                    entry.AppendLine(context);
                    entry.AppendLine(exception.ToString());
                    entry.AppendLine();

                    File.AppendAllText(LogFilePath, entry.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // Logging must never interfere with Wallpaper Control itself.
            }
        }

        private static void RotateIfNeeded()
        {
            if (!File.Exists(LogFilePath))
                return;

            FileInfo info = new(LogFilePath);
            if (info.Length < MaxLogFileBytes)
                return;

            string previousLog =
                Path.Combine(LogDirectory, "wallpaper-control.previous.log");

            File.Move(LogFilePath, previousLog, true);
        }
    }
}
