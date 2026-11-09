using System.Drawing.Imaging;

namespace WallpaperControl;

public partial class MainForm
{
    private VisitorModeService? visitorMode;
    private ImageModeSnapshot? visitorImageSnapshot;
    private readonly System.Windows.Forms.Timer visitorTimer = new();
    private bool visitorAdvanceRunning;
    private string? visitorCurrentPath;
    private bool visitorClosing;
    private string? neutralVisitorPath;
    private readonly Random visitorRandom = new();
    private readonly VisitorSlideshowClock visitorSlideshowClock = new();
    private CancellationTokenSource visitorTransitionCancellation = new();
    private readonly HashSet<string> visitorVideoSources = new(StringComparer.OrdinalIgnoreCase);
    private VisitorWallpaperHandoff? visitorHandoff;
    internal VisitorWallpaperHandoff? VisitorHandoffForTests = null;
    private Task visitorCheckpoint = Task.CompletedTask;
    private CancellationTokenSource? visitorCheckpointCancellation;
    private DateTime visitorCheckpointDue;
    private string? visitorCheckpointVideoPath;
    private bool VisitorHandoffEnabled => servicesEnabled || VisitorHandoffForTests != null;
    private VisitorWallpaperHandoff GetVisitorHandoff() => VisitorHandoffForTests ?? (visitorHandoff ??= new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WallpaperControl", "VisitorMode"),
        path => PersistentDesktopTransitionManager.ApplyNativeSelection(() => FreezeNativeWallpaper(path)),
        readNative: ReadVisitorNativeWallpaper,
        publishCovered: path => PersistentDesktopTransitionManager.ApplyCoveredNativeSelection(() => FreezeNativeWallpaper(path))));

    private static string? ReadVisitorNativeWallpaper()
    {
        IDesktopWallpaper? wallpaper = null;
        try
        {
            wallpaper = (IDesktopWallpaper)new DesktopWallpaper();
            uint count = wallpaper.GetMonitorDevicePathCount();
            return wallpaper.GetWallpaper(count > 0 ? wallpaper.GetMonitorDevicePathAt(0) : null);
        }
        finally { ReleaseComObject(wallpaper); }
    }

    private void ShowVisitorCover()
    {
        if (!VisitorHandoffEnabled || !GetVisitorHandoff().PublishExistingOrNeutral()) ShowVisitorNeutral();
    }


    private void InitializeVisitorMode(VisitorModeSettings? initial = null)
    {
        var store = new VisitorModeStore();
        visitorMode = new(initial ?? (servicesEnabled ? store.Load() : new()),
            value => !servicesEnabled || store.Save(value), CaptureVisitorResume,
            ActivateVisitorMediaAsync, RestoreVisitorResumeAsync);
        widgetManager.ConfigureVisitorMode(visitorMode);
        // Reserve before preferences, timers, hotkeys or the normal desktop warm-up.
        if (visitorMode.Active)
        {
            SwitchWallpaperOperatingMode(WallpaperOperatingMode.Visitor);
            ShowVisitorCover();
        }
        visitorTimer.Tick += async (_, _) =>
        {
            UpdateVisitorPlaybackPause();
            if (VisitorFolderActive && !visitorMode.Busy && !visitorAdvanceRunning && !visitorClosing && visitorSlideshowClock.Due)
            {
                await AdvanceVisitorFolderAsync();
                visitorSlideshowClock.Restart(visitorMode.Settings.IntervalMilliseconds);
            }
        };
    }

    private VisitorResumeState CaptureVisitorResume()
    {
        bool video = wallpaperOwnership.Mode == WallpaperOperatingMode.VideoWallpaper;
        if (!video)
        {
            visitorImageSnapshot?.Dispose();
            try { visitorImageSnapshot = CaptureImageMode(); }
            catch (Exception ex)
            {
                AppLogger.Warning("Native image collection snapshot unavailable; retaining current image.", ex);
                visitorImageSnapshot = new() { Custom = customSlideshowEngineActive, ManualPaused = slideshowPaused, StaticPath = GetCurrentWallpaperPath() };
            }
        }
        var images = new Dictionary<string, string>();
        if (servicesEnabled && !video)
        {
            IDesktopWallpaper? wallpaper = null;
            try
            {
                wallpaper = (IDesktopWallpaper)new DesktopWallpaper();
                for (uint i = 0; i < wallpaper.GetMonitorDevicePathCount(); i++)
                { string monitor = wallpaper.GetMonitorDevicePathAt(i); images[monitor] = wallpaper.GetWallpaper(monitor); }
            }
            catch (Exception ex) { AppLogger.Warning("Per-monitor visitor resume snapshot unavailable.", ex); }
            finally { ReleaseComObject(wallpaper); }
        }
        TryGetSelectedInterval(out uint interval);
        return new()
        {
            Video = video, VideoPath = videoWallpaper?.ActivePath ?? videoPathText.Text,
            VideoPosition = video ? videoWallpaper?.PlaybackPosition ?? 0 : 0,
            VideoPaused = video && (videoWallpaper?.PauseReasons & VideoPauseReason.Manual) != 0,
            ImagePath = video ? savedImageMode?.StaticPath : visitorImageSnapshot?.StaticPath, MonitorImages = images,
            Custom = video ? savedImageMode?.Custom ?? false : customSlideshowEngineActive,
            NativeActive = video ? savedImageMode?.NativeActive ?? false : visitorImageSnapshot?.NativeActive ?? false,
            ImagePaused = video ? savedImageMode?.ManualPaused ?? slideshowPaused : slideshowPaused,
            Interval = interval, Shuffle = shuffleCheckBox.Checked,
            RemainingTicks = customSlideshowNextChange == DateTime.MaxValue ? 0 :
                Math.Max(0, (customSlideshowNextChange - DateTime.Now).Ticks)
        };
    }

    private void ShowVisitorNeutral()
    {
        visitorCheckpointVideoPath = null;
        if (VisitorHandoffEnabled && GetVisitorHandoff().PublishNeutral()) return;
        if (!servicesEnabled) return;
        try
        {
            if (neutralVisitorPath == null)
            {
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WallpaperControl", "VisitorMode");
                Directory.CreateDirectory(directory);
                neutralVisitorPath = Path.Combine(directory, "neutral.bmp");
                using Bitmap bitmap = new(16, 16);
                using (Graphics graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.FromArgb(32, 38, 44));
                bitmap.Save(neutralVisitorPath, ImageFormat.Bmp);
            }
            // Apply the safe native image before retiring a desktop surface.
            FreezeNativeWallpaper(neutralVisitorPath);
            PersistentDesktopTransitionManager.Shutdown();
        }
        catch (Exception ex)
        {
            AppLogger.Warning("Could not set neutral visitor wallpaper; trying solid background.", ex);
            IDesktopWallpaper? wallpaper = null;
            try
            {
                wallpaper = (IDesktopWallpaper)new DesktopWallpaper();
                wallpaper.SetBackgroundColor(0x002C2620);
                wallpaper.SetWallpaper(null, "");
                PersistentDesktopTransitionManager.Shutdown();
            }
            catch (Exception fallback) { AppLogger.Warning("Visitor solid background failed.", fallback); }
            finally { ReleaseComObject(wallpaper); }
        }
    }

    private async Task ActivateVisitorMediaAsync()
    {
        visitorTimer.Stop();
        visitorTransitionCancellation.Cancel();
        ++wallpaperModeRequest;
        SwitchWallpaperOperatingMode(WallpaperOperatingMode.Visitor);
        customWallpaperCancellation?.Cancel();
        videoWallpaper?.CancelPendingStart();
        customSlideshowPreciseTimer.Change(Timeout.Infinite, Timeout.Infinite);
        StopVideoLiveTest();
        wallpaperPreviewForm.Hide();
        // Cover normal media before any asynchronous native cleanup can yield.
        ShowVisitorCover();
        await CancelVisitorCheckpointAsync();
        videoWallpaper?.Stop();
        while (visitorAdvanceRunning) await Task.Delay(10);
        await wallpaperModeChange.WaitAsync();
        try
        {
            if (visitorClosing || wallpaperOwnership.Closed) return;
            if (imageTransitionTasks.Count > 0) await Task.WhenAll(imageTransitionTasks.ToArray());
            if (visitorClosing || wallpaperOwnership.Closed) return;
            visitorTransitionCancellation.Dispose(); visitorTransitionCancellation = new();
            customSlideshowEngineActive = false;
            ShowVisitorCover();
            if (videoWallpaper != null) await videoWallpaper.StopAsync();
            visitorCurrentPath = null; visitorVideoSources.Clear();
            if (visitorMode!.Settings.Source == VisitorSourceKind.Folder)
            {
                await AdvanceVisitorFolderAsync();
                visitorSlideshowClock.Restart(visitorMode.Settings.IntervalMilliseconds);
                UpdateVisitorPlaybackPause();
                visitorTimer.Interval = 250;
                if (!visitorClosing) visitorTimer.Start();
            }
            else if (!await TryDisplayVisitorMediaAsync(visitorMode.Settings.Path, visitorMode.Settings.Source == VisitorSourceKind.Video) && !visitorClosing)
            { ShowVisitorNeutral(); visitorMode.ReportMediaError(); }
        }
        catch (Exception ex)
        {
            if (!visitorClosing) { ShowVisitorNeutral(); visitorMode!.ReportMediaError(); }
            AppLogger.Warning("Visitor media activation failed; safe wallpaper retained.", ex);
        }
        finally { wallpaperModeChange.Release(); }
    }

    private async Task<bool> TryDisplayVisitorMediaAsync(string path, bool video)
    {
        if (visitorClosing || wallpaperOwnership.Closed || !visitorMode!.Active) return false;
        try
        {
            if (!File.Exists(path)) throw new FileNotFoundException();
            if (video)
            {
                if (!Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase) || videoMonitorCount() != 1) return false;
                if (servicesEnabled) PersistentDesktopTransitionManager.Shutdown();
                var engine = await EnsureVideoWallpaperAsync(path, wallpaperModeRequest, false);
                if (engine == null || visitorClosing || wallpaperOwnership.Closed) return false;
                engine.SetPause(VideoPauseReason.Manual, VisitorFolderActive && visitorMode.Settings.SlideshowPaused);
                engine.SetPause(VideoPauseReason.Fullscreen, fullscreenPolicy.IsPaused);
                // Visitor playback never changes the normal audio preferences.
                engine.SetAudio(false, videoVolumeSlider.Value);
                visitorVideoSources.Add(path);
                if (!await engine.StartAsync(path)) { if (!visitorClosing) ShowVisitorNeutral(); return false; }
                visitorCurrentPath = path;
                await CheckpointVisitorVideoAsync(force: true);
            }
            else
            {
                if (!WallpaperImageInfo.IsSupportedWallpaperExtension(path)) return false;
                // Decode before asking Windows to display: corrupt files must never reveal normal media.
                using (Image image = Image.FromFile(path)) { _ = image.Width; }
                if (videoWallpaper != null) await videoWallpaper.StopAsync();
                if (visitorClosing || wallpaperOwnership.Closed) return false;
                if (VisitorHandoffEnabled)
                {
                    var handoff = GetVisitorHandoff();
                    string? previous = VisitorFolderActive && visitorCurrentPath != null
                        && WallpaperImageInfo.IsSupportedWallpaperExtension(visitorCurrentPath) && handoff.HasStaticImage ? handoff.StaticPath : null;
                    // Keep the old safe native image until its covering renderer is
                    // composition-ready. Updating the crash-safe underlay must not
                    // tear that renderer down before the animation can begin.
                    var transitionToken = visitorTransitionCancellation.Token;
                    bool rendererReady = previous != null && handoff.CanPublishCovered && !visitorClosing
                        && await PersistentDesktopTransitionManager.InitializeHostAsync(previous, transitionToken);
                    transitionToken.ThrowIfCancellationRequested();
                    if (visitorClosing || wallpaperOwnership.Closed) return false;
                    if (!handoff.PublishImage(path, rendererReady)) { if (!visitorClosing) ShowVisitorNeutral(); return false; }
                    visitorCheckpointVideoPath = null;
                    visitorCurrentPath = path;
                    if (rendererReady && previous != null && !visitorClosing)
                    {
                        try
                        {
                            await wallpaperTransitionService.ApplyAsync(previous, handoff.StaticPath, selectedTransitionKind,
                                selectedTransitionDurationMilliseconds, selectedTransitionDirection, selectedZoomMode, transitionToken);
                        }
                        catch (OperationCanceledException) when (visitorTransitionCancellation.IsCancellationRequested) { }
                        catch (Exception ex) { PersistentDesktopTransitionManager.Shutdown(); AppLogger.Warning("Visitor transition unavailable; safe native image retained.", ex); }
                    }
                }
                visitorCurrentPath = path;
            }
            visitorCurrentPath = path;
            if (visitorClosing) return false;
            visitorMode.ReportMediaReady();
            return true;
        }
        catch (Exception ex) { AppLogger.Warning("Visitor media could not be displayed.", ex); return false; }
    }

    private Task<bool> AdvanceVisitorFolderAsync() => AdvanceVisitorFolderDirectionAsync(false);
    private async Task<bool> AdvanceVisitorFolderDirectionAsync(bool previous)
    {
        if (visitorAdvanceRunning || visitorClosing || !VisitorFolderActive) return false;
        visitorAdvanceRunning = true;
        try
        {
            var settings = visitorMode!.Settings;
            string[] files = Directory.GetFiles(settings.Path).Where(path => WallpaperImageInfo.IsSupportedWallpaperExtension(path)
                || Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase)).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            string? displayed = await WallpaperCandidateRunner.TryAdvanceAsync(files, visitorCurrentPath, settings.Shuffle, previous,
                visitorRandom, path => TryDisplayVisitorMediaAsync(path, Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase)));
            if (displayed == null) await UseVisitorFallbackAsync();
            return displayed != null;
        }
        catch (Exception ex) { await UseVisitorFallbackAsync(); AppLogger.Warning("Visitor folder unavailable.", ex); return false; }
        finally { visitorAdvanceRunning = false; UpdateVisitorUi(); }
    }

    private async Task UseVisitorFallbackAsync()
    {
        if (visitorClosing) return;
        if (videoWallpaper != null) await videoWallpaper.StopAsync();
        ShowVisitorNeutral(); visitorMode!.ReportMediaError();
    }

    private async Task RestoreVisitorResumeAsync(VisitorResumeState? resume)
    {
        visitorTransitionCancellation.Cancel();
        RestoreVisitorPlaybackControls();
        visitorTimer.Stop();
        // A folder timer may already be preparing a video. Cancel and join it first.
        ++wallpaperModeRequest; videoWallpaper?.CancelPendingStart();
        await CancelVisitorCheckpointAsync();
        while (visitorAdvanceRunning) await Task.Delay(10);
        await wallpaperModeChange.WaitAsync();
        try
        {
            if (visitorClosing || wallpaperOwnership.Closed) throw new OperationCanceledException();
            if (videoWallpaper != null) await videoWallpaper.StopAsync();
            ShowVisitorNeutral();
            if (resume?.Video == true)
            {
                // A process restart retired the COM snapshot that normal video mode
                // uses when later returning to images. Retain its persisted image state too.
                savedImageMode ??= new ImageModeSnapshot { StaticPath = resume.ImagePath,
                    Custom = resume.Custom || resume.NativeActive, ManualPaused = resume.ImagePaused };
                var engine = await EnsureVideoWallpaperAsync(resume.VideoPath, wallpaperModeRequest, false)
                    ?? throw new InvalidOperationException("Video backend unavailable.");
                engine.SetAudio(videoSoundCheck.Checked, videoVolumeSlider.Value);
                engine.SetPause(VideoPauseReason.Manual, resume.VideoPaused);
                engine.SetPause(VideoPauseReason.Fullscreen, fullscreenPolicy.IsPaused);
                if (!await engine.StartAsync(resume.VideoPath, restorePosition: resume.VideoPosition))
                    throw new InvalidOperationException("Normal video could not be restored.");
                SwitchWallpaperOperatingMode(WallpaperOperatingMode.VideoWallpaper);
            }
            else
            {
                SwitchWallpaperOperatingMode(WallpaperOperatingMode.ImageSlideshow);
                if (resume != null)
                {
                    if (servicesEnabled && !string.IsNullOrEmpty(resume.ImagePath) && !File.Exists(resume.ImagePath))
                        throw new FileNotFoundException("Original image no longer exists.");
                    bool memorySnapshot = visitorImageSnapshot != null;
                    bool wasLoading = loading; loading = true;
                    try
                    {
                        var interval = intervals.FirstOrDefault(item => item.Value == resume.Interval);
                        if (interval != null) intervalComboBox.SelectedItem = interval;
                        shuffleCheckBox.Checked = resume.Shuffle;
                    }
                    finally { loading = wasLoading; }
                    savedImageMode = visitorImageSnapshot ?? new ImageModeSnapshot
                    { StaticPath = resume.ImagePath, Custom = resume.Custom, ManualPaused = resume.ImagePaused };
                    visitorImageSnapshot = null;
                    restoredNativePredecessors = new[] { visitorCurrentPath, neutralVisitorPath,
                        VisitorHandoffEnabled ? GetVisitorHandoff().StaticPath : null }
                        .Concat(resume.MonitorImages.Values).ToArray();
                    try { RestoreImageModeCore(strict: true); }
                    finally { restoredNativePredecessors = null; }
                    if (resume.NativeActive && !resume.Custom && !memorySnapshot && servicesEnabled && !resume.ImagePaused && !fullscreenPolicy.IsPaused)
                        if (!SetWallpaperFolder(folderTextBox.Text, showError: false))
                            throw new InvalidOperationException("Original native slideshow could not be restored.");
                    if (resume.NativeActive && !resume.Custom && !memorySnapshot && fullscreenPolicy.IsPaused && !resume.ImagePaused)
                        nativeSlideshowAutoPaused = true;
                    if (servicesEnabled && !resume.Custom) RestoreVisitorMonitorImages(resume);
                    if (resume.Custom)
                    {
                        customSlideshowNextChange = DateTime.Now.AddTicks(Math.Clamp(resume.RemainingTicks, 0, TimeSpan.FromDays(1).Ticks));
                        customSlideshowLastInterval = resume.Interval;
                        ArmCustomSlideshowPreciseTimer();
                    }
                }
            }
        }
        catch
        {
            SwitchWallpaperOperatingMode(WallpaperOperatingMode.Visitor);
            ShowVisitorNeutral();
            throw;
        }
        finally { wallpaperModeChange.Release(); }
    }

    private void RestoreVisitorMonitorImages(VisitorResumeState resume)
    {
        IDesktopWallpaper? wallpaper = null;
        try
        {
            wallpaper = (IDesktopWallpaper)new DesktopWallpaper();
            // A native active slideshow owns its collection; SetWallpaper would stop it.
            if (resume.NativeActive && !resume.ImagePaused && !fullscreenPolicy.IsPaused) return;
            foreach (var image in resume.MonitorImages)
                if (File.Exists(image.Value)) wallpaper.SetWallpaper(image.Key, image.Value);
        }
        finally { ReleaseComObject(wallpaper); }
    }

    private async Task ObserveVisitorVideoAsync()
    {
        if (visitorMode?.Active != true || visitorMode.Busy || visitorClosing || visitorAdvanceRunning || videoWallpaper == null) return;
        var controller = videoWallpaper;
        await controller.TickAsync();
        if (!visitorClosing && !IsDisposed && !wallpaperOwnership.Closed && ReferenceEquals(controller, videoWallpaper)
            && visitorMode.Active && !visitorMode.Busy && controller.State == VideoWallpaperState.Failed)
        { ShowVisitorNeutral(); visitorMode.ReportMediaError(); }
        else if (!visitorClosing && ReferenceEquals(controller, videoWallpaper) && visitorMode.Active && !visitorMode.Busy && controller.HasSession)
            visitorMode.ReportMediaReady();
        if (!visitorClosing) await CheckpointVisitorVideoAsync(force: false);
    }

    private async Task StopVisitorAndCloseAsync()
    {
        visitorTransitionCancellation.Cancel();
        visitorTimer.Stop(); ++wallpaperModeRequest;
        videoWallpaper?.CancelPendingStart();
        await CancelVisitorCheckpointAsync();
        if (visitorMode != null) await visitorMode.DrainAsync();
        while (visitorAdvanceRunning) await Task.Delay(10);
        await wallpaperModeChange.WaitAsync();
        try
        {
            await PublishVisitorExitWallpaperAsync();
            if (videoWallpaper != null) await videoWallpaper.StopAsync();
            PersistentDesktopTransitionManager.Shutdown();
            customSlideshowEngineActive = false;
            nativeSlideshowAutoPaused = false;
            videoShutdownFinished = true;
        }
        finally { wallpaperModeChange.Release(); }
        if (!IsDisposed) Close();
    }
}
