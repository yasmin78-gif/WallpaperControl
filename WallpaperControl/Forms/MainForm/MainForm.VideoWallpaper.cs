using WallpaperControl.Video;

namespace WallpaperControl;

public partial class MainForm
{
    private readonly WallpaperModeOwnership wallpaperOwnership = new();
    private readonly SemaphoreSlim wallpaperModeChange = new(1, 1);
    private int wallpaperModeRequest;
    private bool startupVideoRestored;
    private VideoWallpaperController? videoWallpaper;
    private readonly HashSet<Task<bool>> imageTransitionTasks = new();
    private ImageModeSnapshot? savedImageMode;
    private readonly Panel videoCard = new();
    private readonly Label modeHeading = new();
    private readonly MainFormComboBox wallpaperModeCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox videoPathText = new() { ReadOnly = true };
    private readonly MainFormButton videoBrowseButton = new() { Text = "…" };
    private readonly MainFormButton videoApplyButton = new();
    private readonly MainFormButton videoPauseButton = new();
    private readonly Label videoStatusLabel = new();
    private readonly Label videoFileLabel = new() { AutoEllipsis = true };
    private readonly Label videoEngineHeading = new();
    private readonly RadioButton videoStandardRadio = new();
    private readonly RadioButton videoCompatibilityRadio = new();
    private readonly Label videoStandardDescription = new();
    private readonly Label videoCompatibilityDescription = new();
    private readonly Label videoEngineStatus = new();
    private VideoEngine preferredVideoEngine;
    private VideoBackendSelection? currentVideoBackend;
    private VideoEngine? controllerVideoPreference;
    // Isolated UI tests replace selection, without native desktop side effects.
    internal Func<VideoEngine, Task<VideoBackendSelection>>? SelectVideoBackendForTests = null;
    private readonly CheckBox videoSoundCheck = new();
    private readonly TrackBar videoVolumeSlider = new() { Minimum = 0, Maximum = 100, Value = 50, TickStyle = TickStyle.None };
    private readonly Label videoVolumeLabel = new();
    private bool VideoConfigurationSelected => wallpaperModeCombo.SelectedIndex == 1;
    private bool videoUiLoading;
    private string? videoSelectionError;
    private readonly Func<int> videoMonitorCount = () => Screen.AllScreens.Length;

    private sealed class ImageModeSnapshot : IDisposable
    {
        internal bool Custom, ManualPaused, NativeActive;
        internal string? StaticPath;
        internal IShellItemArray? Items;
        internal DesktopSlideshowOptions Options;
        internal uint Interval;
        public void Dispose() { ReleaseComObject(Items); Items = null; }
    }

    private void InitializeVideoWallpaperUi()
    {
        videoCard.Controls.AddRange(new Control[] { modeHeading, videoFileLabel, videoBrowseButton, videoApplyButton, videoPauseButton, videoStatusLabel, videoSoundCheck, videoVolumeSlider, videoVolumeLabel, videoEngineHeading, videoStandardRadio, videoCompatibilityRadio, videoStandardDescription, videoCompatibilityDescription, videoEngineStatus });
        wallpaperContent!.Controls.AddRange(new Control[] { videoCard, wallpaperModeCombo, videoStatusLabel });
        preferredVideoEngine = servicesEnabled ? appSettings.LoadVideoEngine() : VideoEngine.Standard;
        videoPathText.Text = servicesEnabled ? appSettings.LoadVideoWallpaperPath() : "";
        videoPathText.AccessibleName = Localization.Get("VideoBrowse");
        videoPathText.TextChanged += (_, _) => UpdateVideoControls();
        videoSoundCheck.Checked = servicesEnabled && appSettings.LoadVideoSound();
        videoVolumeSlider.Value = servicesEnabled ? appSettings.LoadVideoVolume() : 50;
        videoSoundCheck.CheckedChanged += (_, _) => ApplyVideoAudioConfiguration(true, false);
        videoVolumeSlider.ValueChanged += (_, _) => ApplyVideoAudioConfiguration(false, true);
        modeHeading.Font = videoEngineHeading.Font = CreateOwnedFont("Segoe UI", 12, FontStyle.Bold);
        videoBrowseButton.Click += async (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = "MP4 (*.mp4)|*.mp4", CheckFileExists = true };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                videoPathText.Text = dialog.FileName;
                if (VideoConfigurationSelected) await ApplyWallpaperModeAsync(WallpaperOperatingMode.VideoWallpaper);
            }
        };
        videoApplyButton.Click += async (_, _) => await ApplyWallpaperModeAsync(
            wallpaperModeCombo.SelectedIndex == 1 ? WallpaperOperatingMode.VideoWallpaper : WallpaperOperatingMode.ImageSlideshow);
        videoPauseButton.Click += async (_, _) => await ToggleSlideshowPauseAsync(refreshDisplay: false);
        wallpaperModeCombo.SelectionChangeCommitted += async (_, _) =>
        {
            if (videoUiLoading) return;
            var mode = VideoConfigurationSelected ? WallpaperOperatingMode.VideoWallpaper : WallpaperOperatingMode.ImageSlideshow;
            if (wallpaperModeChange.CurrentCount == 1 && wallpaperOwnership.Mode == mode && (mode != WallpaperOperatingMode.VideoWallpaper || videoWallpaper?.HasSession == true)) return;
            await ApplyWallpaperModeAsync(mode);
        };
        videoStandardRadio.Click += async (_, _) => await SelectVideoEngineAsync(VideoEngine.Standard);
        videoCompatibilityRadio.Click += async (_, _) => await SelectVideoEngineAsync(VideoEngine.Compatibility);
        wallpaperModeCombo.SelectedIndexChanged += (_, _) =>
        {
            if (videoUiLoading) return;
            if (wallpaperModeCombo.SelectedIndex == 0) { videoSelectionError = null; CheckSlideshowStatus(); }
            UpdateVideoControls();
        };
        LocalizeVideoWallpaperUi();
    }
    private void ApplyVideoAudioConfiguration(bool saveSound, bool saveVolume)
    {
        if (servicesEnabled)
        {
            if (saveSound) appSettings.SaveVideoSound(videoSoundCheck.Checked);
            if (saveVolume) appSettings.SaveVideoVolume(videoVolumeSlider.Value);
        }
        videoWallpaper?.SetAudio(videoSoundCheck.Checked, videoVolumeSlider.Value);
        videoVolumeSlider.Enabled = videoSoundCheck.Checked;
        videoVolumeLabel.Text = string.Format(Localization.Get("VideoVolumeFormat"), videoVolumeSlider.Value);
    }
    private void LocalizeVideoWallpaperUi()
    {
        bool wasLoading = videoUiLoading;
        videoUiLoading = true;
        int selection = Math.Max(0, wallpaperModeCombo.SelectedIndex);
        wallpaperModeCombo.Items.Clear();
        wallpaperModeCombo.Items.AddRange(new object[] { Localization.Get("ModeImageSlideshow"), Localization.Get("ModeVideoWallpaper") });
        wallpaperModeCombo.SelectedIndex = selection;
        videoStandardRadio.Checked = preferredVideoEngine == VideoEngine.Standard;
        videoCompatibilityRadio.Checked = preferredVideoEngine == VideoEngine.Compatibility;
        videoUiLoading = wasLoading;
        modeHeading.Text = Localization.Get("VideoSelectedFile");
        videoEngineHeading.Text = Localization.Get("VideoEngineHeading");
        videoStandardRadio.Text = Localization.Get("VideoEngineStandard");
        videoCompatibilityRadio.Text = Localization.Get("VideoEngineCompatibility");
        videoStandardDescription.Text = Localization.Get("VideoEngineStandardDescription");
        videoCompatibilityDescription.Text = Localization.Get("VideoEngineCompatibilityDescription");
        videoSoundCheck.Text = Localization.Get("VideoSound");
        videoVolumeSlider.AccessibleName = Localization.Get("VideoVolume");
        ApplyVideoAudioConfiguration(false, false);
        wallpaperModeCombo.AccessibleName = Localization.Get("WallpaperModeHeading");

        videoPathText.AccessibleName = Localization.Get("VideoBrowse");
        toolTip.SetToolTip(videoBrowseButton, Localization.Get("VideoBrowse"));
        toolTip.SetToolTip(videoPathText, videoPathText.Text);
        UpdateVideoControls();
    }
    private void UpdateVideoControls()
    {
        bool video = wallpaperOwnership.Mode == WallpaperOperatingMode.VideoWallpaper;
        bool selected = wallpaperModeCombo.SelectedIndex == 1;
        videoCard.Visible = selected;
        videoApplyButton.Text = Localization.Get(video && videoWallpaper?.HasSession == true ? "VideoRestart" : "VideoStart");
        videoFileLabel.Text = string.IsNullOrWhiteSpace(videoPathText.Text) ? Localization.Get("VideoNoSelection") : Path.GetFileName(videoPathText.Text);
        videoFileLabel.AccessibleName = Localization.Get("VideoSelectedFile");
        toolTip.SetToolTip(videoFileLabel, videoPathText.Text);
        slideshowCard.Visible = displayCard.Visible = currentWallpaperCard.Visible = nextWallpaperButton.Visible = !selected;
        videoPathText.Enabled = videoBrowseButton.Enabled = selected;
        slideshowCard.Enabled = displayCard.Enabled = !video;
        videoApplyButton.Enabled = selected && videoMonitorCount() == 1 && !string.IsNullOrWhiteSpace(videoPathText.Text);
        string key = videoSelectionError ?? (!video ? "ModeImageSlideshow" : videoWallpaper?.State switch
        {
            VideoWallpaperState.Failed => videoWallpaper.ErrorKey,
            VideoWallpaperState.Initializing => "VideoInitializing",
            VideoWallpaperState.Recovering => "VideoRecovering",
            VideoWallpaperState.Paused => (videoWallpaper.PauseReasons & VideoPauseReason.Manual) != 0 ? "VideoManualPaused" : "VideoFullscreenPaused",
            _ => "VideoActive"
        });
        videoStatusLabel.Text = videoSelectionError != null ? Localization.Get(key)
            : selected && !video ? Localization.Get("VideoChooseFileSafe")
            : video ? Localization.Get(key) : "";
        videoStatusLabel.Visible = selected || video;
        videoEngineStatus.Text = currentVideoBackend is { } backend && video
            ? backend.Fallback ? Localization.Get("VideoEngineFallback")
                : string.Format(Localization.Get("VideoEngineActive"), Localization.Get(backend.Effective == VideoEngine.Standard ? "VideoEngineStandard" : "VideoEngineCompatibility"))
            : "";
        videoEngineStatus.Visible = !string.IsNullOrEmpty(videoEngineStatus.Text);
        UpdateVideoEngineStatusColor();
        bool manualVideoPause = ((videoWallpaper?.PauseReasons ?? VideoPauseReason.None) & VideoPauseReason.Manual) != 0;
        videoPauseButton.Text = Localization.Get(manualVideoPause ? "VideoResume" : "VideoPause");
        videoPauseButton.Enabled = video && videoWallpaper?.HasSession == true;
        if (video)
        {
            UpdateTrayPauseText();
            if (videoLiveTestItem != null) videoLiveTestItem.Enabled = false;
            statusLabel.Visible = false;
            activateButton.Visible = false;
            nextWallpaperButton.Enabled = pinButton.Enabled = rejectButton.Enabled = undoRejectButton.Enabled = false;
            pauseButton.Enabled = videoWallpaper?.HasSession == true;
            pauseButton.Text = videoPauseButton.Text;
            SetNormalLayout();
        }
        else if (videoLiveTestItem != null) videoLiveTestItem.Enabled = true;
        if (selected) statusLabel.Visible = activateButton.Visible = false;
        LayoutWallpaperPage();
    }
    private void UpdateVideoEngineStatusColor()
    {
        bool fallback = wallpaperOwnership.Mode == WallpaperOperatingMode.VideoWallpaper && currentVideoBackend?.Fallback == true;
        videoEngineStatus.ForeColor = fallback
            ? darkMode ? Color.FromArgb(245, 160, 145) : Color.Firebrick
            : AppTheme.TextPrimary(darkMode);
    }

    private async Task SelectVideoEngineAsync(VideoEngine preferred)
    {
        if (videoUiLoading) return;
        bool changed = preferredVideoEngine != preferred;
        preferredVideoEngine = preferred;
        if (servicesEnabled) appSettings.SaveVideoEngine(preferred);
        bool wasLoading = videoUiLoading; videoUiLoading = true;
        videoStandardRadio.Checked = preferred == VideoEngine.Standard;
        videoCompatibilityRadio.Checked = preferred == VideoEngine.Compatibility;
        videoUiLoading = wasLoading;
        if ((wallpaperOwnership.Mode == WallpaperOperatingMode.VideoWallpaper || (wallpaperModeChange.CurrentCount == 0 && VideoConfigurationSelected)) && !string.IsNullOrWhiteSpace(videoPathText.Text)
            && (changed || currentVideoBackend?.Fallback == true))
            await ApplyWallpaperModeCoreAsync(WallpaperOperatingMode.VideoWallpaper, replaceEngine: true);
        UpdateVideoControls();
    }
    private async Task<VideoWallpaperController?> EnsureVideoWallpaperAsync(string path, int request, bool replaceEngine)
    {
        if (videoWallpaper != null && !replaceEngine && (controllerVideoPreference == null || controllerVideoPreference == preferredVideoEngine)) return videoWallpaper;
        VideoBackendSelection selection = SelectVideoBackendForTests != null
            ? await SelectVideoBackendForTests(preferredVideoEngine)
            : await VideoBackendFactory.SelectAsync(this, preferredVideoEngine);
        if (request != wallpaperModeRequest || wallpaperOwnership.Closed) return null;
        VideoPauseReason reasons = videoWallpaper?.PauseReasons ?? VideoPauseReason.None;
        if (videoWallpaper != null)
        {
            // Validate media before retiring a stable engine. The new desktop has
            // no session/renderer yet, so engines never overlap on the desktop.
            try
            {
                if (!selection.Desktop.FileExists(path)) throw new FileNotFoundException("Video file missing", path);
                await selection.Desktop.ValidateAsync(path, CancellationToken.None);
            }
            catch (Exception ex)
            {
                AppLogger.Warning("Video engine change validation failed; stable engine retained.", ex);
                videoSelectionError = "VideoErrorFile";
                return null;
            }
            if (request != wallpaperModeRequest || wallpaperOwnership.Closed) return null;
            // Read policy again after the await: pause/audio may change during validation.
            var old = videoWallpaper;
            old.Changed -= VideoWallpaperChanged;
            await old.StopAsync();
            reasons = old.PauseReasons;
            old.Dispose(); videoWallpaper = null;
            if (request != wallpaperModeRequest || wallpaperOwnership.Closed) return null;
        }
        currentVideoBackend = selection;
        controllerVideoPreference = preferredVideoEngine;
        videoWallpaper = new(selection.Desktop, message => AppLogger.Info($"Video wallpaper: pid={Environment.ProcessId}; " + message));
        videoWallpaper.SetAudio(videoSoundCheck.Checked, videoVolumeSlider.Value);
        videoWallpaper.SetPause(VideoPauseReason.Manual, (reasons & VideoPauseReason.Manual) != 0);
        videoWallpaper.SetPause(VideoPauseReason.Fullscreen, (reasons & VideoPauseReason.Fullscreen) != 0);
        videoWallpaper.Changed += VideoWallpaperChanged;
        return videoWallpaper;
    }
    private void VideoWallpaperChanged()
    { if (!IsDisposed && !Disposing && !wallpaperOwnership.Closed) { UpdateVideoControls(); LayoutWallpaperPage(); } }

    private Task RestoreStartupVideoAsync(WallpaperOperatingMode mode, string path)
    {
        if (mode != WallpaperOperatingMode.VideoWallpaper || startupVideoRestored) return Task.CompletedTask;
        startupVideoRestored = true;
        videoPathText.Text = path;
        bool wasLoading = videoUiLoading; videoUiLoading = true;
        wallpaperModeCombo.SelectedIndex = 1;
        videoUiLoading = wasLoading;
        return ApplyWallpaperModeAsync(mode);
    }

    private Task ApplyWallpaperModeAsync(WallpaperOperatingMode mode) => ApplyWallpaperModeCoreAsync(mode);

    private void SwitchWallpaperOperatingMode(WallpaperOperatingMode mode)
    {
        wallpaperOwnership.Switch(mode);
        widgetManager.SetWallpaperMode(mode);
    }

    private async Task ApplyWallpaperModeCoreAsync(WallpaperOperatingMode mode, bool replaceEngine = false)
    {
        // Reject unsupported topology before touching native wallpaper paths,
        // image scheduler state or transition ownership (especially Span).
        if (mode == WallpaperOperatingMode.VideoWallpaper && videoMonitorCount() != 1)
        {
            videoSelectionError = "VideoErrorMonitor";
            videoUiLoading = true; wallpaperModeCombo.SelectedIndex = 1; videoUiLoading = false;
            UpdateVideoControls();
            return;
        }
        videoSelectionError = null;
        int request = ++wallpaperModeRequest;
        string selectedPath = videoPathText.Text;
        // Cancel obsolete preparation immediately. Publication and retirement
        // stay serialized, so older queued requests cannot stop a newer player.
        videoWallpaper?.CancelPendingStart();
        await wallpaperModeChange.WaitAsync();
        try
        {
            if (request != wallpaperModeRequest || wallpaperOwnership.Closed) return;
            if (mode == WallpaperOperatingMode.VideoWallpaper)
            {
                if (string.IsNullOrWhiteSpace(selectedPath))
                {
                    if (wallpaperOwnership.Mode == WallpaperOperatingMode.VideoWallpaper && videoWallpaper?.HasSession != true)
                    {
                        savedImageMode ??= CaptureImageMode();
                        SwitchWallpaperOperatingMode(WallpaperOperatingMode.ImageSlideshow);
                        RestoreImageMode();
                        if (servicesEnabled) appSettings.SaveWallpaperOperatingMode(WallpaperOperatingMode.ImageSlideshow);
                    }
                    return;
                }
                VideoWallpaperController? engine = await EnsureVideoWallpaperAsync(selectedPath, request, replaceEngine);
                if (engine == null) return;
                bool started = await engine.StartAsync(selectedPath, async () =>
                {
                    if (request != wallpaperModeRequest || wallpaperOwnership.Closed) throw new OperationCanceledException();
                    bool enteringVideo = savedImageMode == null;
                    if (savedImageMode == null)
                    {
                        savedImageMode = CaptureImageMode();
                        if (servicesEnabled) appSettings.SaveImageActiveBeforeVideo(savedImageMode.Custom || savedImageMode.NativeActive);
                    }
                    if (wallpaperOwnership.AllowsImages)
                    {
                        SwitchWallpaperOperatingMode(mode);
                        AppLogger.Info("Wallpaper operating mode: VideoWallpaper; image ownership suspended");
                        customSlideshowPreciseTimer.Change(Timeout.Infinite, Timeout.Infinite);
                        customWallpaperCancellation?.Cancel();
                        if (imageTransitionTasks.Count > 0) await Task.WhenAll(imageTransitionTasks.ToArray());
                        if (request != wallpaperModeRequest || wallpaperOwnership.Closed) throw new OperationCanceledException();
                        customSlideshowEngineActive = false;
                        StopVideoLiveTest();
                        FreezeNativeWallpaper(savedImageMode.StaticPath);
                        PersistentDesktopTransitionManager.Shutdown();
                    }
                    // A saved video-mode startup already reserved ownership.
                    else if (enteringVideo)
                    {
                        StopVideoLiveTest();
                        FreezeNativeWallpaper(savedImageMode.StaticPath);
                        PersistentDesktopTransitionManager.Shutdown();
                    }
                    if (enteringVideo) engine.SetPause(VideoPauseReason.Manual, slideshowPaused);
                    engine.SetPause(VideoPauseReason.Fullscreen, fullscreenPolicy.IsPaused);
                });
                if (request != wallpaperModeRequest || wallpaperOwnership.Closed) return;
                if (started || engine.State == VideoWallpaperState.Recovering)
                {
                    if (servicesEnabled)
                    {
                        appSettings.SaveVideoWallpaperPath(selectedPath);
                        appSettings.SaveWallpaperOperatingMode(mode);
                    }
                }
                else
                {
                    videoSelectionError = engine.ErrorKey;
                    if (!engine.HasSession)
                    {
                        await engine.StopAsync();
                        savedImageMode ??= CaptureImageMode();
                        SwitchWallpaperOperatingMode(WallpaperOperatingMode.ImageSlideshow);
                        RestoreImageMode();
                        if (servicesEnabled) appSettings.SaveWallpaperOperatingMode(WallpaperOperatingMode.ImageSlideshow);
                    }
                }
            }
            else
            {
                if (videoWallpaper != null) await videoWallpaper.StopAsync();
                if (request != wallpaperModeRequest || wallpaperOwnership.Closed) return;
                SwitchWallpaperOperatingMode(mode);
                AppLogger.Info("Wallpaper operating mode: ImageSlideshow; video ownership released");
                RestoreImageMode();
                if (servicesEnabled) appSettings.SaveWallpaperOperatingMode(mode);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Info($"Wallpaper mode change failed; type={ex.GetType().Name}; hr=0x{ex.HResult:X8}");
            if (request != wallpaperModeRequest || wallpaperOwnership.Closed) return;
            if (videoWallpaper != null) await videoWallpaper.StopAsync();
            SwitchWallpaperOperatingMode(WallpaperOperatingMode.ImageSlideshow);
            RestoreImageMode();
            if (servicesEnabled) appSettings.SaveWallpaperOperatingMode(WallpaperOperatingMode.ImageSlideshow);
            videoSelectionError = "VideoErrorPlayback";
        }
        finally
        {
            wallpaperModeChange.Release();
            if (request == wallpaperModeRequest && !wallpaperOwnership.Closed && !IsDisposed && !Disposing)
            {
                CheckSlideshowStatus(); UpdateVideoControls();
            }
        }
    }
    private ImageModeSnapshot CaptureImageMode()
    {
        var snapshot = new ImageModeSnapshot { Custom = customSlideshowEngineActive, ManualPaused = slideshowPaused, StaticPath = GetCurrentWallpaperPath() };
        if (!servicesEnabled) return snapshot;
        IDesktopWallpaper? wallpaper = null;
        try
        {
            wallpaper = (IDesktopWallpaper)new DesktopWallpaper();
            wallpaper.GetStatus(out var state);
            snapshot.NativeActive = (state & (DesktopSlideshowState.Enabled | DesktopSlideshowState.Slideshow)) == (DesktopSlideshowState.Enabled | DesktopSlideshowState.Slideshow);
            if (!snapshot.NativeActive && !snapshot.Custom && appSettings.LoadWallpaperOperatingMode() == WallpaperOperatingMode.VideoWallpaper)
                snapshot.Custom = appSettings.LoadImageActiveBeforeVideo();
            // Fullscreen may already own the native slideshow snapshot.
            if (nativeSlideshowAutoPaused && fullscreenSavedSlideshow != null)
            {
                snapshot.NativeActive = true;
                snapshot.Items = fullscreenSavedSlideshow; fullscreenSavedSlideshow = null;
                snapshot.Options = fullscreenSavedOptions; snapshot.Interval = fullscreenSavedInterval;
                nativeSlideshowAutoPaused = false;
            }
            else if (snapshot.NativeActive)
            {
                wallpaper.GetSlideshow(out snapshot.Items);
                wallpaper.GetSlideshowOptions(out snapshot.Options, out snapshot.Interval);
            }
            return snapshot;
        }
        catch { snapshot.Dispose(); throw; }
        finally { ReleaseComObject(wallpaper); }
    }
    private void FreezeNativeWallpaper(string? staticPath)
    {
        if (!servicesEnabled) return;
        IDesktopWallpaper? wallpaper = null;
        try
        {
            wallpaper = (IDesktopWallpaper)new DesktopWallpaper();
            // Empty restores the default static background when no image exists.
            wallpaper.SetWallpaper(null, staticPath != null && File.Exists(staticPath) ? staticPath : "");
        }
        finally { ReleaseComObject(wallpaper); }
    }
    private void RestoreImageMode()
    {
        var snapshot = savedImageMode; savedImageMode = null;
        if (snapshot == null) return;
        try
        {
            FreezeNativeWallpaper(snapshot.StaticPath);
            slideshowPaused = snapshot.ManualPaused;
            customSlideshowEngineActive = false;
            if (snapshot.Custom)
            {
                if (servicesEnabled) StartCustomSlideshowEngine();
                else customSlideshowEngineActive = true;
                slideshowPaused = snapshot.ManualPaused;
                if (servicesEnabled && !customSlideshowEngineActive && !slideshowPaused && !fullscreenPolicy.IsPaused && Directory.Exists(folderTextBox.Text))
                    SetWallpaperFolder(folderTextBox.Text, showError: false);
                RecalculateCustomSlideshowSchedule(); // Future aligned deadline, never catch up.
            }
            else if (snapshot.NativeActive && snapshot.Items != null)
            {
                if (fullscreenPolicy.IsPaused || slideshowPaused)
                {
                    ReleaseComObject(fullscreenSavedSlideshow);
                    fullscreenSavedSlideshow = snapshot.Items; snapshot.Items = null;
                    fullscreenSavedOptions = snapshot.Options; fullscreenSavedInterval = snapshot.Interval;
                    nativeSlideshowAutoPaused = true;
                }
                else
                {
                    IDesktopWallpaper? wallpaper = null;
                    try { wallpaper = (IDesktopWallpaper)new DesktopWallpaper(); wallpaper.SetSlideshow(snapshot.Items); wallpaper.SetSlideshowOptions(snapshot.Options, snapshot.Interval); }
                    finally { ReleaseComObject(wallpaper); }
                }
            }
        }
        catch (Exception ex)
        {
            customSlideshowEngineActive = false;
            AppLogger.Info($"Image mode restoration failed safely; type={ex.GetType().Name}; hr=0x{ex.HResult:X8}");
        }
        finally { snapshot.Dispose(); }
    }
    private void DisposeVideoWallpaper()
    {
        wallpaperModeRequest++; wallpaperOwnership.Close();
        if (videoWallpaper != null) { videoWallpaper.Changed -= VideoWallpaperChanged; videoWallpaper.Dispose(); }
        // Exiting video leaves a static desktop. Persisted mode is resumed next
        // launch, so do not restart a competing Windows slideshow on shutdown.
        savedImageMode?.Dispose(); savedImageMode = null;
        videoPathText.Dispose();
        videoCard.Dispose();
    }
}
