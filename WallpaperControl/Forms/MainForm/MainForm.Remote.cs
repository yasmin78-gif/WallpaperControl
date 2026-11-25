using WallpaperControl.Remote;

namespace WallpaperControl;

public partial class MainForm
{
    private void InitializeRemote()
    {
        if (!servicesEnabled || remoteClosing || remoteManager != null || IsDisposed) return;
        try
        {
            remoteManager = new(() => RemoteUiAsync(() => Task.FromResult(CapturePublishedRemoteState())),
                (command, grant) => RemoteUiAsync(() => ExecuteRemoteControlAsync(command, grant)));
            remoteStateTimer = new() { Interval = 500 };
            remoteStateTimer.Tick += (_, _) =>
            {
                if (remoteManager?.Server is { Accepting: true, HasConnectedClients: true } server)
                    server.Observe(CaptureRemoteState(), lastDisplayedWallpaperPath ?? "");
            };
            remoteManager.Changed += () =>
            {
                if (!remoteClosing && !IsDisposed && IsHandleCreated) BeginInvoke(() => { if (remoteStateTimer != null) remoteStateTimer.Enabled = remoteManager?.Server?.Accepting == true; });
            };
            _ = remoteManager.InitializeAsync();
        }
        catch (Exception ex) { AppLogger.Info("Remote configuration unavailable: " + ex.GetType().Name); }
    }
    private Task<T> RemoteUiAsync<T>(Func<Task<T>> operation)
    {
        if (remoteClosing || IsDisposed || Disposing || !IsHandleCreated) return Task.FromException<T>(new ObjectDisposedException(nameof(MainForm)));
        if (!InvokeRequired) return operation();
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            BeginInvoke(async () =>
            {
                try { if (remoteClosing || IsDisposed || Disposing) throw new ObjectDisposedException(nameof(MainForm)); result.TrySetResult(await operation()); }
                catch (Exception ex) { result.TrySetException(ex); }
            });
        }
        catch (Exception ex) { result.TrySetException(ex); }
        return result.Task;
    }
    internal RemoteState CaptureRemoteState()
    {
        bool visitor = visitorMode?.Active == true;
        bool visitorFolder = VisitorFolderActive;
        bool normalVideo = !visitor && wallpaperOwnership.Mode == WallpaperOperatingMode.VideoWallpaper;
        bool visitorVideo = visitor && videoWallpaper?.HasSession == true && visitorVideoSources.Contains(videoWallpaper.ActivePath);
        bool manualPause = visitor ? visitorMode!.Settings.SlideshowPaused : normalVideo ? ((videoWallpaper?.PauseReasons ?? VideoPauseReason.None) & VideoPauseReason.Manual) != 0 : slideshowPaused;
        bool busy = remoteClosing || remoteCommandExecuting || initializingDesktop || visitorMode?.Busy == true || visitorAdvanceRunning || wallpaperModeChange.CurrentCount == 0
            || customSlideshowChangeRunning || nativeSlideshowAdvance.IsRunning;
        bool nativeActive = false;
        string? error = null;
        if (!visitor && !normalVideo && !customSlideshowEngineActive && !slideshowPaused)
        {
            try { var native = readNativeSlideshowStatus(); nativeActive = (native & (DesktopSlideshowState.Enabled | DesktopSlideshowState.Slideshow)) == (DesktopSlideshowState.Enabled | DesktopSlideshowState.Slideshow); }
            catch { error = "playbackUnavailable"; }
        }
        bool imagesActive = customSlideshowEngineActive || nativeActive || slideshowPaused;
        bool ready = !busy && !wallpaperOwnership.Closed;
        bool imageControls = visitor ? visitorFolder : !normalVideo && imagesActive;
        var commands = new List<string>();
        if (ready)
        {
            if (visitor) commands.Add("visitorOff");
            else if (visitorMode != null && HasVisitorSource(visitorMode.Settings)) commands.Add("visitorOn");
            if (imageControls)
            {
                commands.AddRange(["pause", "resume", "layout"]);
                if (!manualPause && !fullscreenPolicy.IsPaused) commands.AddRange(["next", "previous"]);
                if (visitorFolder || !manualPause && !fullscreenPolicy.IsPaused) commands.AddRange(["interval", "shuffle"]);
            }
            if (normalVideo && videoWallpaper?.HasSession == true && videoWallpaper.State is not (VideoWallpaperState.Failed or VideoWallpaperState.Recovering)) commands.AddRange(["pause", "resume"]);
            if (!visitor && (normalVideo || servicesEnabled && videoMonitorCount() == 1 && File.Exists(appSettings.LoadVideoWallpaperPath()))) commands.Add("mode");
        }
        TryGetSelectedInterval(out uint interval);
        if (visitor) interval = visitorMode!.Settings.IntervalMilliseconds;
        string layout = positionComboBox.SelectedItem is DisplayOption<DesktopWallpaperPosition> selected ? selected.Value.ToString().ToLowerInvariant() : "fill";
        var reasons = new List<string>();
        if (manualPause) reasons.Add("manual");
        if (fullscreenPolicy.IsPaused) reasons.Add("fullscreen");
        if (remoteClosing) reasons.Add("shutdown");
        string playback = busy ? "busy" : reasons.Count != 0 ? "paused" : visitor ? visitorMode?.Error != null ? "failed" : "playing"
            : normalVideo ? videoWallpaper?.State.ToString().ToLowerInvariant() ?? "inactive" : imagesActive ? "playing" : "inactive";
        long? remaining = null;
        if (visitorFolder && !busy) remaining = visitorSlideshowClock.Remaining;
        else if (!visitor && !normalVideo && customSlideshowEngineActive && !busy && reasons.Count == 0 && customSlideshowNextChange != DateTime.MaxValue)
            remaining = Math.Max(0, (long)(customSlideshowNextChange - DateTime.Now).TotalMilliseconds);
        return new()
        {
            Mode = visitor ? "visitor" : normalVideo ? "video" : "slideshow", MediaKind = normalVideo || visitorVideo ? "video" : "image", Playback = playback,
            VisitorActive = visitor, VisitorFolder = visitorFolder, ManualPaused = manualPause, PauseReasons = reasons.ToArray(), Busy = busy,
            IntervalMilliseconds = interval, Shuffle = visitor ? visitorMode!.Settings.Shuffle : shuffleCheckBox.Checked, Layout = layout,
            RemainingMilliseconds = remaining, ErrorCode = error ?? (visitorMode?.Error != null && visitor ? "visitorError" : normalVideo && videoWallpaper?.State == VideoWallpaperState.Failed ? "videoError" : null),
            Capabilities = new(commands.ToArray(), imageControls ? positions.Select(p => p.Value.ToString().ToLowerInvariant()).ToArray() : [],
                imageControls ? intervals.Select(i => i.Value).ToArray() : [], visitorFolder ? 1000u : 0, visitorFolder ? 86400000u : 0)
        };
    }
    private RemoteState CapturePublishedRemoteState()
    {
        var state = CaptureRemoteState();
        remoteManager?.Server?.Observe(state, lastDisplayedWallpaperPath ?? "");
        return remoteManager?.Server?.State ?? state;
    }
    internal async Task<RemoteOutcome> ExecuteRemoteControlAsync(RemoteCommand command, DeviceGrant grant)
    {
        if (remoteClosing || remoteCommandExecuting || servicesEnabled && (remoteManager?.Server?.Accepting != true || remoteManager.Server.Epoch != command.Epoch || !remoteManager.Vault.Current(grant))) return new(false, "unauthorized");
        if (servicesEnabled && CapturePublishedRemoteState().Revision != command.Revision) return new(false, "stale");
        var state = CaptureRemoteState().ForDevice(grant.CanDisableVisitor);
        if (!state.Capabilities.Allows(command.Type)) return new(false, "notAllowed");
        long persistence = SettingsPersistence.FailureRevision;
        remoteCommandExecuting = true;
        try
        {
            bool success;
            switch (command.Type)
            {
                case "next": case "previous":
                    success = await AdvanceWallpaperAsync(command.Type == "next" ? DesktopSlideshowDirection.Forward : DesktopSlideshowDirection.Backward); break;
                case "pause": case "resume":
                    bool paused = command.Type == "pause";
                    if (visitorMode?.Active == true)
                    {
                        if (!VisitorFolderActive) return new(false, "visitorProtected");
                        await visitorMode.ConfigureAsync(s => s with { SlideshowPaused = paused }, reloadMedia: false);
                        UpdateVisitorPlaybackPause(); UpdateVisitorUi(); success = visitorMode.Settings.SlideshowPaused == paused && visitorMode.Error == null;
                    }
                    else if (wallpaperOwnership.Mode == WallpaperOperatingMode.VideoWallpaper)
                    {
                        if (videoWallpaper == null) return new(false, "notAvailable");
                        videoWallpaper.SetPause(VideoPauseReason.Manual, paused); UpdateVideoControls();
                        success = videoWallpaper.State != VideoWallpaperState.Failed && ((videoWallpaper.PauseReasons & VideoPauseReason.Manual) != 0) == paused;
                    }
                    else if (slideshowPaused == paused) success = true;
                    else if (paused) { PauseSlideshowCore(showError: false); success = slideshowPaused; }
                    else success = await ResumeSlideshowAsync(showError: false);
                    break;
                case "layout":
                    if (!Enum.TryParse<DesktopWallpaperPosition>(command.Value, true, out var position) || !positions.Any(p => p.Value == position)) return new(false, "invalidLayout");
                    success = ApplyWallpaperPosition(position, showError: false); break;
                case "interval": case "shuffle":
                    uint interval = state.IntervalMilliseconds; bool shuffle = state.Shuffle;
                    if (command.Type == "interval" && (!uint.TryParse(command.Value, out interval) || (VisitorFolderActive ? interval is < 1000 or > 86400000 : !intervals.Any(i => i.Value == interval)))) return new(false, "invalidInterval");
                    if (command.Type == "shuffle" && !bool.TryParse(command.Value, out shuffle)) return new(false, "invalidShuffle");
                    if (visitorMode?.Active == true)
                    {
                        if (!VisitorFolderActive) return new(false, "visitorProtected");
                        bool intervalChanged = visitorMode.Settings.IntervalMilliseconds != interval;
                        await visitorMode.ConfigureAsync(s => s with { IntervalMilliseconds = interval, Shuffle = shuffle }, reloadMedia: false);
                        if (intervalChanged) visitorSlideshowClock.Restart(interval);
                        UpdateVisitorPlaybackPause(); UpdateVisitorUi();
                        success = visitorMode.Error == null && visitorMode.Settings.IntervalMilliseconds == interval && visitorMode.Settings.Shuffle == shuffle;
                    }
                    else
                    {
                        bool wasLoading = loading; loading = true;
                        try { intervalComboBox.SelectedItem = intervals.First(i => i.Value == interval); shuffleCheckBox.Checked = shuffle; }
                        finally { loading = wasLoading; }
                        success = ApplySlideshowOptions(showError: false);
                        if (success && customSlideshowEngineActive && command.Type == "interval") RecalculateCustomSlideshowSchedule();
                        if (!success) { loading = true; try { LoadSlideshowOptions(); } finally { loading = wasLoading; } }
                    }
                    break;
                case "mode":
                    if (visitorMode?.Active == true) return new(false, "visitorProtected");
                    if (command.Value is not ("video" or "slideshow")) return new(false, "invalidMode");
                    var mode = command.Value == "video" ? WallpaperOperatingMode.VideoWallpaper : WallpaperOperatingMode.ImageSlideshow;
                    if (mode == WallpaperOperatingMode.VideoWallpaper)
                    {
                        string path = appSettings.LoadVideoWallpaperPath();
                        if (videoMonitorCount() != 1 || !File.Exists(path)) return new(false, "videoUnavailable");
                        videoPathText.Text = path;
                    }
                    await ApplyWallpaperModeAsync(mode);
                    success = wallpaperOwnership.Mode == mode && (mode != WallpaperOperatingMode.VideoWallpaper || videoWallpaper?.HasSession == true && videoWallpaper.State is VideoWallpaperState.Playing or VideoWallpaperState.Paused);
                    break;
                case "visitorOn": case "visitorOff":
                    bool active = command.Type == "visitorOn";
                    if (!active && !grant.CanDisableVisitor) return new(false, "visitorPermission");
                    if (visitorMode == null || active && !HasVisitorSource(visitorMode.Settings)) return new(false, "visitorSourceUnavailable");
                    await visitorMode.SetActiveAsync(active);
                    success = visitorMode.Active == active && visitorMode.Error == null; break;
                default: return new(false, "unknownCommand");
            }
            return success && persistence == SettingsPersistence.FailureRevision ? new(true) : new(false, "operationFailed");
        }
        catch (Exception ex) { AppLogger.Info("Remote control failed: " + ex.GetType().Name); return new(false, "operationFailed"); }
        finally { remoteCommandExecuting = false; if (!IsDisposed && !Disposing) remoteManager?.Server?.Observe(CaptureRemoteState(), lastDisplayedWallpaperPath ?? ""); }
    }
    private void ShowRemoteSettings()
    {
        InitializeRemote();
        if (remoteManager == null) { MessageBox.Show(this, "Die sichere Remote-Ablage konnte nicht geöffnet werden.", "WallpaperControl Remote"); return; }
        using var dialog = new RemoteSettingsForm(remoteManager, darkMode); dialog.ShowDialog(this);
    }
    private void StopRemoteAccepting() { remoteClosing = true; remoteStateTimer?.Stop(); remoteManager?.StopAccepting(); }
    private void DisposeRemote() { StopRemoteAccepting(); remoteStateTimer?.Dispose(); remoteStateTimer = null; remoteManager?.Dispose(); remoteManager = null; }
}
