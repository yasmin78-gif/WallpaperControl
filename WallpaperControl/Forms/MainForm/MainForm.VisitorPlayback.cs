namespace WallpaperControl;

public partial class MainForm
{
    private bool visitorPlaybackProjected;
    private object? normalIntervalSelection;
    private bool normalShuffleSelection;
    private readonly TextBox visitorFolderDisplay = new() { ReadOnly = true, Visible = false };
    private bool VisitorFolderActive => visitorMode?.Active == true && visitorMode.Settings.Source == VisitorSourceKind.Folder;

    private void UpdateVisitorPlaybackPause()
    {
        if (!VisitorFolderActive) return;
        visitorSlideshowClock.SetPaused(visitorMode!.Settings.SlideshowPaused || fullscreenPolicy.IsPaused);
        if (videoWallpaper?.HasSession == true && visitorVideoSources.Contains(videoWallpaper.ActivePath)
            && wallpaperOwnership.Mode == WallpaperOperatingMode.Visitor)
            videoWallpaper.SetPause(VideoPauseReason.Manual, visitorMode.Settings.SlideshowPaused);
    }

    private async Task<bool> AdvanceVisitorManuallyAsync(DesktopSlideshowDirection direction)
    {
        if (!VisitorFolderActive || visitorMode!.Busy || visitorClosing || visitorAdvanceRunning) return false;
        await UpdateFullscreenPauseAsync();
        UpdateVisitorPlaybackPause();
        if (visitorSlideshowClock.Paused) return false;
        return await AdvanceVisitorFolderDirectionAsync(direction == DesktopSlideshowDirection.Backward);
    }

    private async Task ToggleVisitorPauseAsync()
    {
        if (!VisitorFolderActive || visitorMode!.Busy || visitorClosing) return;
        await visitorMode.ConfigureAsync(s => s with { SlideshowPaused = !s.SlideshowPaused }, reloadMedia: false);
        UpdateVisitorPlaybackPause(); UpdateVisitorUi();
    }

    private async Task SaveVisitorPlaybackOptionsAsync()
    {
        if (!VisitorFolderActive || visitorClosing) return;
        uint interval = intervalComboBox.SelectedItem is DisplayOption<uint> option ? option.Value : visitorMode!.Settings.IntervalMilliseconds;
        bool shuffle = shuffleCheckBox.Checked;
        bool intervalChanged = interval != visitorMode!.Settings.IntervalMilliseconds;
        await visitorMode.ConfigureAsync(s => s with { IntervalMilliseconds = interval, Shuffle = shuffle }, reloadMedia: false);
        visitorInterval.Value = visitorMode.Settings.IntervalMilliseconds / 1000;
        visitorShuffle.Checked = visitorMode.Settings.Shuffle;
        if (intervalChanged) visitorSlideshowClock.Restart(visitorMode.Settings.IntervalMilliseconds);
        UpdateVisitorPlaybackPause(); UpdateVisitorUi();
    }

    private void RestoreVisitorPlaybackControls()
    {
        if (!visitorPlaybackProjected) return;
        bool wasLoading = loading; loading = true;
        try { intervalComboBox.SelectedItem = normalIntervalSelection; shuffleCheckBox.Checked = normalShuffleSelection; }
        finally { loading = wasLoading; visitorPlaybackProjected = false; }
    }

    private void ProjectVisitorPlaybackControls()
    {
        if (!VisitorFolderActive) { RestoreVisitorPlaybackControls(); return; }
        if (!visitorPlaybackProjected)
        {
            normalIntervalSelection = intervalComboBox.SelectedItem;
            normalShuffleSelection = shuffleCheckBox.Checked;
            visitorPlaybackProjected = true;
        }
        uint milliseconds = visitorMode!.Settings.IntervalMilliseconds;
        var option = intervalComboBox.Items.OfType<DisplayOption<uint>>().FirstOrDefault(item => item.Value == milliseconds);
        if (option == null)
        {
            option = new DisplayOption<uint>(milliseconds, TimeSpan.FromMilliseconds(milliseconds).ToString());
            intervalComboBox.Items.Add(option);
        }
        intervalComboBox.SelectedItem = option;
        shuffleCheckBox.Checked = visitorMode.Settings.Shuffle;
        visitorFolderDisplay.Text = visitorMode.Settings.Path;
        UpdateVisitorPlaybackPause();
        pauseButton.Text = Localization.Get(visitorMode.Settings.SlideshowPaused ? "ResumeSlideshow" : "PauseSlideshow");
        statusLabel.Text = Localization.Get(fullscreenPolicy.IsPaused ? "StatusFullscreenPaused" : "StatusPaused");
        statusLabel.Visible = visitorSlideshowClock.Paused;
        wallpaperWarning = visitorSlideshowClock.Paused;
        wallpaperActivation = false;
        activateButton.Visible = false;
        trayPauseItem.Text = pauseButton.Text;
        currentWallpaperLabel.Text = visitorCurrentPath == null ? Localization.Get("CurrentWallpaperEmpty") : Path.GetFileName(visitorCurrentPath);
        lastDisplayedWallpaperPath = null;
        toolTip.SetToolTip(currentWallpaperLabel, visitorCurrentPath ?? "");
    }
}
