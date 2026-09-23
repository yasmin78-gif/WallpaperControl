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

        /// <summary>
        /// Records an application error with its context and exception details.
        /// </summary>
        /// <param name="context">The operation context recorded alongside the exception.</param>
        /// <param name="exception">The exception to include in the diagnostic entry.</param>
        internal static void Error(string context, Exception exception)
        {
            Write("ERROR", context, exception);
        }

        /// <summary>
        /// Records a recoverable problem with its context and exception details.
        /// </summary>
        /// <param name="context">The operation context recorded alongside the exception.</param>
        /// <param name="exception">The exception to include in the diagnostic entry.</param>
        internal static void Warning(string context, Exception exception)
        {
            Write("WARN", context, exception);
        }

        internal static void Info(string context) => Write("INFO", context, null);

        /// <summary>
        /// Appends a timestamped diagnostic entry without allowing logging failures to disrupt the application.
        /// </summary>
        /// <param name="level">The diagnostic severity written to the log.</param>
        /// <param name="context">The operation context recorded alongside the exception.</param>
        /// <param name="exception">The exception to include in the diagnostic entry.</param>
        private static void Write(string level, string context, Exception? exception)
        {
            try
            {
                lock (SyncRoot)
                {
                    Directory.CreateDirectory(LogDirectory);
                    RotateIfNeeded();

                    StringBuilder entry = new();
                    entry.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture));
                    entry.Append(" [");
                    entry.Append(level);
                    entry.Append("] ");
                    entry.AppendLine(context);
                    if (exception != null) entry.AppendLine(exception.ToString());
                    entry.AppendLine();

                    File.AppendAllText(LogFilePath, entry.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // Logging must never interfere with Wallpaper Control itself.
            }
        }

        /// <summary>
        /// Rotates the log when it reaches the configured size limit.
        /// </summary>
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
