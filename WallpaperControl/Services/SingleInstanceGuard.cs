using System;
using System.Security.Principal;
using System.Threading;

namespace WallpaperControl
{
    internal sealed class SingleInstanceGuard : IDisposable
    {
        internal static string UserKey
        {
            get
            {
                using var identity = WindowsIdentity.GetCurrent();
                return identity.User!.Value;
            }
        }
        private readonly Mutex mutex;
        internal bool IsPrimary { get; }

        /// <summary>
        /// Attempts to acquire the named mutex that identifies the primary application instance.
        /// </summary>
        /// <param name="mutexName">An optional mutex name, allowing tests to isolate instance ownership.</param>
        internal SingleInstanceGuard(string? mutexName = null)
        {
            mutex = new Mutex(false, mutexName ?? @"Global\WallpaperControl.Instance." + UserKey);
            try { IsPrimary = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { IsPrimary = true; }
        }

        /// <summary>
        /// Releases the instance mutex when this process owns it and disposes the handle.
        /// </summary>
        public void Dispose()
        {
            if (IsPrimary) mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }
}
