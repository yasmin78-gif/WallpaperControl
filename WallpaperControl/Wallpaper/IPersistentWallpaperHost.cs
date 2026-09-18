namespace WallpaperControl;

// Narrow native-host seam: permits failure-path tests without taking over the desktop.
internal interface IPersistentWallpaperHost : IDisposable
{
    bool IsDisposed { get; }
    string? CurrentWallpaperPath { get; }
    bool Initialize(string path);
    bool EnsureDesktopPlacement();
    void SetActivitySuspended(bool suspended);
    void SetWallpaperPosition(DesktopWallpaperPosition position);
    Task TransitionToAsync(string path, WallpaperTransitionKind kind, int milliseconds,
        WallpaperTransitionDirection direction, WallpaperZoomMode zoomMode, CancellationToken cancellationToken);
    void CommitCurrentPath(string path);
}
