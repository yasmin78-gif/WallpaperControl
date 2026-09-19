using System;
using System.Threading;

namespace WallpaperControl
{
    // A revision lets an explicit save report partial failure once, while background
    // saves remain non-modal. Never log setting values, paths or calendar URLs.
    internal static class SettingsPersistence
    {
        private static long failureRevision;
        internal static long FailureRevision => Interlocked.Read(ref failureRevision);

        internal static void ReportFailure(string operation, Exception exception)
        {
            Interlocked.Increment(ref failureRevision);
            AppLogger.Warning(operation, new InvalidOperationException(exception.GetType().Name));
        }
    }
}
