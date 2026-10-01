using WallpaperControl.Video;

namespace WallpaperControl;

public partial class MainForm
{
    private readonly WallpaperModeOwnership wallpaperOwnership = new();
    private readonly SemaphoreSlim wallpaperModeChange = new(1, 1);
    private int wallpaperModeRequest;
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
    private readonly MainFormButton imageApplyButton = new();
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
        videoCard.Controls.AddRange(new Control[] { modeHeading, videoFileLabel, videoBrowseButton, videoApplyButton, videoPauseButton, videoStatusLabel, videoSoundCheck, videoVolumeSlider, videoVolumeLabel });
        wallpaperContent!.Controls.AddRange(new Control[] { videoCard, wallpaperModeCombo, imageApplyButton, videoStatusLabel });
        videoPathText.Text = servicesEnabled ? appSettings.LoadVideoWallpaperPath() : "";
        videoPathText.AccessibleName = Localization.Get("VideoBrowse");
        videoPathText.TextChanged += (_, _) => UpdateVideoControls();
        videoSoundCheck.Checked = servicesEnabled && appSettings.LoadVideoSound();
        videoVolumeSlider.Value = servicesEnabled ? appSettings.LoadVideoVolume() : 50;
        videoSoundCheck.CheckedChanged += (_, _) => ApplyVideoAudioConfiguration(true, false);
        videoVolumeSlider.ValueChanged += (_, _) => ApplyVideoAudioConfiguration(false, true);
        modeHeading.Font = CreateOwnedFont("Segoe UI", 12, FontStyle.Bold);
        videoBrowseButton.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = "MP4 (*.mp4)|*.mp4", CheckFileExists = true };
            if (dialog.ShowDialog(this) == DialogResult.OK) videoPathText.Text = dialog.FileName;
        };
        videoApplyButton.Click += async (_, _) => await ApplyWallpaperModeAsync(
            wallpaperModeCombo.SelectedIndex == 1 ? WallpaperOperatingMode.VideoWallpaper : WallpaperOperatingMode.ImageSlideshow);
        videoPauseButton.Click += async (_, _) => await ToggleSlideshowPauseAsync(refreshDisplay: false);
        imageApplyButton.Click += async (_, _) => await ApplyWallpaperModeAsync(WallpaperOperatingMode.ImageSlideshow);
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
        videoUiLoading = true;
        int selection = Math.Max(0, wallpaperModeCombo.SelectedIndex);
        wallpaperModeCombo.Items.Clear();
        wallpaperModeCombo.Items.AddRange(new object[] { Localization.Get("ModeImageSlideshow"), Localization.Get("ModeVideoWallpaper") });
        wallpaperModeCombo.SelectedIndex = selection;
        videoUiLoading = false;
        modeHeading.Text = Localization.Get("VideoSelectedFile");
        imageApplyButton.Text = Localization.Get("ImageStart");
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
        imageApplyButton.Visible = !selected && video;
        videoApplyButton.Text = Localization.Get(video && videoWallpaper?.HasSession == true ? "VideoChange" : "VideoStart");
        videoFileLabel.Text = string.IsNullOrWhiteSpace(videoPathText.Text) ? Localization.Get("VideoNoSelection") : Path.GetFileName(videoPathText.Text);
        videoFileLabel.AccessibleName = Localization.Get("VideoSelectedFile");
        toolTip.SetToolTip(videoFileLabel, videoPathText.Text);
        slideshowCard.Visible = displayCard.Visible = currentWallpaperCard.Visible = nextWallpaperButton.Visible = !selected;
        videoPathText.Enabled = videoBrowseButton.Enabled = selected;
        slideshowCard.Enabled = displayCard.Enabled = !video;
        videoApplyButton.Enabled = !selected || videoMonitorCount() == 1;
        string key = videoSelectionError ?? (!video ? "ModeImageSlideshow" : videoWallpaper?.State switch
        {
            VideoWallpaperState.Failed => videoWallpaper.ErrorKey,
            VideoWallpaperState.Initializing => "VideoInitializing",
            VideoWallpaperState.Recovering => "VideoRecovering",
            VideoWallpaperState.Paused => (videoWallpaper.PauseReasons & VideoPauseReason.Manual) != 0 ? "VideoManualPaused" : "VideoFullscreenPaused",
            _ => "VideoActive"
        });
        videoStatusLabel.Text = videoSelectionError != null ? Localization.Get(key)
            : selected != video ? Localization.Get(selected ? "VideoSelectedImagesActive" : "ImagesSelectedVideoActive")
            : video ? Localization.Get(key) : "";
        videoStatusLabel.Visible = selected || video;
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
    private VideoWallpaperController EnsureVideoWallpaper()
    {
        if (videoWallpaper != null) return videoWallpaper;
        videoWallpaper = new(new MediaFoundationVideoDesktop(this), message => AppLogger.Info($"Video wallpaper: pid={Environment.ProcessId}; " + message));
        videoWallpaper.SetAudio(videoSoundCheck.Checked, videoVolumeSlider.Value);
        videoWallpaper.Changed += VideoWallpaperChanged;
        return videoWallpaper;
    }
    private void VideoWallpaperChanged()
    { if (!IsDisposed && !Disposing && !wallpaperOwnership.Closed) { UpdateVideoControls(); LayoutWallpaperPage(); } }

    private async Task ApplyWallpaperModeAsync(WallpaperOperatingMode mode)
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
        // Cancel only an obsolete candidate. Keep the published video until a
        // new one is ready; returning to images releases video immediately.
        if (mode == WallpaperOperatingMode.VideoWallpaper) videoWallpaper?.CancelPendingStart();
        else videoWallpaper?.Stop();
        await wallpaperModeChange.WaitAsync();
        try
        {
            if (request != wallpaperModeRequest || wallpaperOwnership.Closed) return;
            if (mode == WallpaperOperatingMode.VideoWallpaper)
            {
                VideoWallpaperController engine = EnsureVideoWallpaper();
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
                        wallpaperOwnership.Switch(mode);
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
                        engine.Stop();
                        wallpaperOwnership.Switch(WallpaperOperatingMode.ImageSlideshow);
                        RestoreImageMode();
                        if (servicesEnabled) appSettings.SaveWallpaperOperatingMode(WallpaperOperatingMode.ImageSlideshow);
                    }
                }
            }
            else
            {
                wallpaperOwnership.Switch(mode);
                AppLogger.Info("Wallpaper operating mode: ImageSlideshow; video ownership released");
                RestoreImageMode();
                if (servicesEnabled) appSettings.SaveWallpaperOperatingMode(mode);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Info($"Wallpaper mode change failed; type={ex.GetType().Name}; hr=0x{ex.HResult:X8}");
            if (request != wallpaperModeRequest || wallpaperOwnership.Closed) return;
            videoWallpaper?.Stop();
            wallpaperOwnership.Switch(WallpaperOperatingMode.ImageSlideshow);
            RestoreImageMode();
            if (servicesEnabled) appSettings.SaveWallpaperOperatingMode(WallpaperOperatingMode.ImageSlideshow);
            if (!IsDisposed && !Disposing) MessageBox.Show(this, Localization.Get("VideoErrorPlayback"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
