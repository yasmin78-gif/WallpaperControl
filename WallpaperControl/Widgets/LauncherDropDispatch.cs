namespace WallpaperControl;

/// <summary>Return to the OLE source before opening modal launcher dialogs.</summary>
internal static class LauncherDropDispatch
{
    internal static bool TryPost(Control owner, Action work, Action cleanup)
    {
        bool cleaned = false;
        EventHandler? onDisposed = null;
        void Complete()
        {
            if (cleaned) return;
            cleaned = true;
            owner.Disposed -= onDisposed;
            cleanup();
        }
        onDisposed = (_, _) => Complete();
        if (owner.IsDisposed || !owner.IsHandleCreated) { Complete(); return false; }
        owner.Disposed += onDisposed;
        try {
            owner.BeginInvoke((Action)(() => {
                try { if (!owner.IsDisposed && !cleaned) work(); }
                finally { Complete(); }
            }));
            return true;
        }
        catch (InvalidOperationException) { Complete(); return false; }
    }
}
