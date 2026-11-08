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

    private void InitializeVisitorMode()
    {
        var store = new VisitorModeStore();
        visitorMode = new(servicesEnabled ? store.Load() : new(),
            value => !servicesEnabled || store.Save(value), CaptureVisitorResume,
            ActivateVisitorMediaAsync, RestoreVisitorResumeAsync);
        // Reserve before preferences, timers, hotkeys or the normal desktop warm-up.
        if (visitorMode.Active)
        {
            SwitchWallpaperOperatingMode(WallpaperOperatingMode.Visitor);
            ShowVisitorNeutral();
        }
        visitorTimer.Tick += async (_, _) =>
        {
            if (visitorMode.Active && !visitorMode.Busy && !visitorAdvanceRunning && !visitorClosing && !fullscreenPolicy.IsPaused)
                await AdvanceVisitorFolderAsync();
        };
    }

    private VisitorResumeState CaptureVisitorResume()
    {
        bool video = wallpaperOwnership.Mode == WallpaperOperatingMode.VideoWallpaper;
        if (!video) { visitorImageSnapshot?.Dispose(); visitorImageSnapshot = CaptureImageMode(); }
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
            finally { ReleaseComObject(wallpaper); }
        }
        TryGetSelectedInterval(out uint interval);
        return new()
        {
            Video = video, VideoPath = videoWallpaper?.ActivePath ?? videoPathText.Text,
            VideoPosition = video ? videoWallpaper?.PlaybackPosition ?? 0 : 0,
            VideoPaused = video && (videoWallpaper?.PauseReasons & VideoPauseReason.Manual) != 0,
            ImagePath = visitorImageSnapshot?.StaticPath, MonitorImages = images,
            Custom = customSlideshowEngineActive, NativeActive = visitorImageSnapshot?.NativeActive ?? false,
            ImagePaused = slideshowPaused, Interval = interval, Shuffle = shuffleCheckBox.Checked,
            RemainingTicks = customSlideshowNextChange == DateTime.MaxValue ? 0 :
                Math.Max(0, (customSlideshowNextChange - DateTime.Now).Ticks)
        };
    }

    private void ShowVisitorNeutral()
    {
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
        ++wallpaperModeRequest;
        SwitchWallpaperOperatingMode(WallpaperOperatingMode.Visitor);
        customWallpaperCancellation?.Cancel();
        videoWallpaper?.CancelPendingStart();
        customSlideshowPreciseTimer.Change(Timeout.Infinite, Timeout.Infinite);
        StopVideoLiveTest();
        wallpaperPreviewForm.Hide();
        while (visitorAdvanceRunning) await Task.Delay(10);
        await wallpaperModeChange.WaitAsync();
        try
        {
            if (visitorClosing || wallpaperOwnership.Closed) return;
            if (imageTransitionTasks.Count > 0) await Task.WhenAll(imageTransitionTasks.ToArray());
            if (visitorClosing || wallpaperOwnership.Closed) return;
            customSlideshowEngineActive = false;
            ShowVisitorNeutral();
            if (videoWallpaper != null) await videoWallpaper.StopAsync();
            visitorCurrentPath = null;
            if (visitorMode!.Settings.Source == VisitorSourceKind.Folder)
            {
                await AdvanceVisitorFolderAsync();
                visitorTimer.Interval = (int)visitorMode.Settings.IntervalMilliseconds;
                if (!visitorClosing) visitorTimer.Start();
            }
            else if (!await TryDisplayVisitorMediaAsync(visitorMode.Settings.Path, visitorMode.Settings.Source == VisitorSourceKind.Video))
                visitorMode.ReportMediaError();
        }
        catch (Exception ex)
        {
            ShowVisitorNeutral(); visitorMode!.ReportMediaError();
            AppLogger.Warning("Visitor media activation failed; neutral wallpaper retained.", ex);
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
                var engine = await EnsureVideoWallpaperAsync(path, wallpaperModeRequest, false);
                if (engine == null || visitorClosing || wallpaperOwnership.Closed) return false;
                engine.SetPause(VideoPauseReason.Manual, false);
                engine.SetPause(VideoPauseReason.Fullscreen, fullscreenPolicy.IsPaused);
                // Visitor playback never changes the normal audio preferences.
                engine.SetAudio(false, videoVolumeSlider.Value);
                if (!await engine.StartAsync(path)) { ShowVisitorNeutral(); return false; }
            }
            else
            {
                if (!WallpaperImageInfo.IsSupportedWallpaperExtension(path)) return false;
                // Decode before asking Windows to display: corrupt files must never reveal normal media.
                using (Image image = Image.FromFile(path)) { _ = image.Width; }
                if (videoWallpaper != null) await videoWallpaper.StopAsync();
                if (visitorClosing || wallpaperOwnership.Closed) return false;
                await wallpaperTransitionService.ApplyAsync(null, path, WallpaperTransitionKind.Direct, 0,
                    WallpaperTransitionDirection.Left, WallpaperZoomMode.In);
            }
            visitorCurrentPath = path;
            return true;
        }
        catch (Exception ex) { AppLogger.Warning("Visitor media could not be displayed.", ex); return false; }
    }

    private async Task AdvanceVisitorFolderAsync()
    {
        if (visitorAdvanceRunning || visitorClosing) return;
        visitorAdvanceRunning = true;
        try
        {
            var settings = visitorMode!.Settings;
            string[] files = Directory.GetFiles(settings.Path).Where(path => WallpaperImageInfo.IsSupportedWallpaperExtension(path)
                || Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase)).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            string? displayed = await WallpaperCandidateRunner.TryAdvanceAsync(files, visitorCurrentPath, settings.Shuffle, false,
                customSlideshowRandom, path => TryDisplayVisitorMediaAsync(path, Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase)));
            if (displayed == null) { ShowVisitorNeutral(); visitorMode.ReportMediaError(); }
        }
        catch (Exception ex) { ShowVisitorNeutral(); visitorMode!.ReportMediaError(); AppLogger.Warning("Visitor folder unavailable.", ex); }
        finally { visitorAdvanceRunning = false; }
    }

    private async Task RestoreVisitorResumeAsync(VisitorResumeState? resume)
    {
        visitorTimer.Stop();
        // A folder timer may already be preparing a video. Cancel and join it first.
        ++wallpaperModeRequest; videoWallpaper?.CancelPendingStart();
        while (visitorAdvanceRunning) await Task.Delay(10);
        await wallpaperModeChange.WaitAsync();
        try
        {
            if (visitorClosing || wallpaperOwnership.Closed) throw new OperationCanceledException();
            if (videoWallpaper != null) await videoWallpaper.StopAsync();
            ShowVisitorNeutral();
            if (resume?.Video == true)
            {
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
                    savedImageMode = visitorImageSnapshot ?? new ImageModeSnapshot
                    { StaticPath = resume.ImagePath, Custom = resume.Custom, ManualPaused = resume.ImagePaused };
                    visitorImageSnapshot = null;
                    RestoreImageMode();
                    if (resume.NativeActive && !resume.Custom && savedImageMode == null && servicesEnabled && !resume.ImagePaused && !fullscreenPolicy.IsPaused)
                        SetWallpaperFolder(folderTextBox.Text, showError: false);
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
        await videoWallpaper.TickAsync();
        if (visitorMode.Active && !visitorMode.Busy && videoWallpaper.State == VideoWallpaperState.Failed)
        { ShowVisitorNeutral(); visitorMode.ReportMediaError(); }
    }

    private async Task StopVisitorAndCloseAsync()
    {
        visitorTimer.Stop(); ++wallpaperModeRequest;
        videoWallpaper?.CancelPendingStart();
        if (visitorMode != null) await visitorMode.DrainAsync();
        while (visitorAdvanceRunning) await Task.Delay(10);
        await wallpaperModeChange.WaitAsync();
        try
        {
            ShowVisitorNeutral();
            if (videoWallpaper != null) await videoWallpaper.StopAsync();
            customSlideshowEngineActive = false;
            nativeSlideshowAutoPaused = false;
            videoShutdownFinished = true;
        }
        finally { wallpaperModeChange.Release(); }
        if (!IsDisposed) Close();
    }
}
