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

        internal SingleInstanceGuard(string? mutexName = null)
        {
            mutex = new Mutex(false, mutexName ?? @"Global\WallpaperControl.Instance." + UserKey);
            try { IsPrimary = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { IsPrimary = true; }
        }

        public void Dispose()
        {
            if (IsPrimary) mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }
}
