namespace WallpaperControl;

public partial class MainForm
{
    private Task CheckpointVisitorVideoAsync(bool force)
    {
        if (!VisitorHandoffEnabled || visitorClosing || visitorMode?.Active != true || videoWallpaper == null) return Task.CompletedTask;
        if (!visitorCheckpoint.IsCompleted) return visitorCheckpoint;
        if (!force && DateTime.UtcNow < visitorCheckpointDue) return Task.CompletedTask;
        var engine = videoWallpaper;
        string path = engine.ActivePath;
        if (!engine.HasSession || !visitorVideoSources.Contains(path)) return Task.CompletedTask;
        visitorCheckpointDue = DateTime.UtcNow.AddSeconds(15);
        visitorCheckpointCancellation?.Dispose();
        visitorCheckpointCancellation = new();
        int request = wallpaperModeRequest, lease = wallpaperOwnership.Generation;
        bool Current() => !visitorClosing && !IsDisposed && visitorMode.Active && !wallpaperOwnership.Closed
            && wallpaperOwnership.Mode == WallpaperOperatingMode.Visitor && wallpaperOwnership.Generation == lease
            && wallpaperModeRequest == request && ReferenceEquals(engine, videoWallpaper)
            && engine.HasSession && string.Equals(path, engine.ActivePath, StringComparison.OrdinalIgnoreCase);
        visitorCheckpoint = PublishCheckpointAsync(engine, path, Current, visitorCheckpointCancellation.Token);
        return visitorCheckpoint;
    }

    private async Task PublishCheckpointAsync(VideoWallpaperController engine, string path, Func<bool> current, CancellationToken cancellation)
    {
        try
        {
            var handoff = GetVisitorHandoff();
            bool saved = await handoff.PublishVideoAsync(path,
                (destination, token) => engine.SaveCurrentFrameAsync(path, destination, freeze: false, token), current, cancellation);
            if (!current()) return;
            if (saved) visitorCheckpointVideoPath = path;
            else if (!string.Equals(visitorCheckpointVideoPath, path, StringComparison.OrdinalIgnoreCase) || !handoff.HasStaticImage)
                ShowVisitorNeutral();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        { AppLogger.Warning("Could not refresh visitor crash-safe wallpaper.", ex); }
    }

    private async Task CancelVisitorCheckpointAsync()
    {
        visitorCheckpointCancellation?.Cancel();
        try { await visitorCheckpoint; }
        finally
        {
            visitorCheckpointCancellation?.Dispose(); visitorCheckpointCancellation = null;
            visitorCheckpoint = Task.CompletedTask;
        }
    }

    // Caller has stopped scheduling, cancelled preparation and joined media/configuration work.
    // Capture only explicitly approved visitor videos, never a normal video restored concurrently.
    private async Task PublishVisitorExitWallpaperAsync()
    {
        if (!VisitorHandoffEnabled) return;
        var handoff = GetVisitorHandoff();
        var engine = videoWallpaper;
        string? liveVideo = engine?.HasSession == true && visitorVideoSources.Contains(engine.ActivePath) ? engine.ActivePath : null;
        try
        {
            string? video = liveVideo ?? (visitorCurrentPath != null && visitorVideoSources.Contains(visitorCurrentPath) ? visitorCurrentPath : null);
            // A configured visitor video can still yield a thumbnail if presentation
            // failed (for example on an unsupported monitor topology). Never capture
            // an unrelated live session in that case.
            if (video == null && visitorMode?.Settings.Source == VisitorSourceKind.Video
                && Path.GetExtension(visitorMode.Settings.Path).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
                video = visitorMode.Settings.Path;
            if (video != null)
            {
                bool saved = await handoff.PublishVideoAsync(video,
                    liveVideo == null ? null : (destination, token) => engine!.SaveCurrentFrameAsync(video, destination, freeze: true, token),
                    () => visitorClosing && visitorMode?.Active == true && !IsDisposed, CancellationToken.None);
                if (saved) return;
                if (string.Equals(visitorCheckpointVideoPath, video, StringComparison.OrdinalIgnoreCase) && handoff.HasStaticImage
                    && handoff.PublishExistingOrNeutral()) return;
                ShowVisitorNeutral(); return;
            }
            if (visitorCurrentPath != null && WallpaperImageInfo.IsSupportedWallpaperExtension(visitorCurrentPath))
            {
                // The source may have changed or disappeared after presentation.
                // Prefer the exact pixels already committed, not a new file read.
                if (handoff.HasStaticImage && handoff.PublishExistingOrNeutral()) return;
                if (handoff.PublishImage(visitorCurrentPath)) return;
            }
            if (handoff.PublishExistingOrNeutral()) return;
        }
        catch (Exception ex) { AppLogger.Warning("Visitor shutdown still unavailable; using safe fallback.", ex); }
        ShowVisitorNeutral();
    }
}
