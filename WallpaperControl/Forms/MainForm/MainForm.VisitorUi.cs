namespace WallpaperControl;

public partial class MainForm
{
    private Panel? visitorPage;
    private Button? visitorNavigation;
    private bool showingVisitor;
    private bool visitorUiUpdating;
    private bool visitorSourceAvailable;
    private readonly VisitorToggleSwitch visitorSwitch = new();
    private readonly Label visitorStatus = new() { AutoSize = true, MaximumSize = new Size(670, 0) };
    private readonly ComboBox visitorSource = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };
    private readonly TextBox visitorPath = new() { Width = 620, ReadOnly = true };
    private readonly NumericUpDown visitorInterval = new() { Minimum = 1, Maximum = 86400, Width = 160 };
    private readonly CheckBox visitorShuffle = new() { AutoSize = true };
    private readonly Panel visitorMainBar = new();
    private readonly Label visitorMainLabel = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Dictionary<Control, bool> visitorDisabledControls = new();
    private readonly Button visitorApply = new() { AutoSize = true, MinimumSize = new Size(140, 36) };
    private readonly TextBox visitorHotkey = new() { ReadOnly = true, Width = 360 };
    private uint visitorHotkeyModifiers, visitorHotkeyKey;
    private const int HOTKEY_VISITOR = 5;

    private void InitializeVisitorUi()
    {
        visitorNavigation = MainNavigationButton("VisitorTitle", "◉", 120);
        visitorNavigation.TabIndex = 2; mainNavigation!.Controls.Add(visitorNavigation);
        visitorNavigation.Click += (_, _) => SelectVisitorSection();
        visitorPage = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Visible = false, Padding = new Padding(24) };
        FlowLayoutPanel content = new() { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        Label Label(string key) => new() { Text = Localization.Get(key), Tag = key, AutoSize = true, MaximumSize = new Size(670, 0), Margin = new Padding(3, 12, 3, 5) };
        var title = Label("VisitorTitle"); title.Font = CreateOwnedFont("Segoe UI", 16, FontStyle.Bold);
        content.Controls.Add(title);
        content.Controls.Add(visitorStatus);
        visitorMainBar.Controls.Add(visitorSwitch); visitorMainBar.Controls.Add(visitorMainLabel);
        wallpaperContent!.Controls.Add(visitorMainBar);
        content.Controls.Add(Label("VisitorSource")); content.Controls.Add(visitorSource);
        content.Controls.Add(Label("VisitorPath")); content.Controls.Add(visitorPath);
        Button browse = new() { Tag = "VisitorBrowse", AutoSize = true, MinimumSize = new Size(140, 36) };
        content.Controls.Add(browse);
        content.Controls.Add(Label("VisitorInterval")); content.Controls.Add(visitorInterval);
        content.Controls.Add(visitorShuffle);
        content.Controls.Add(Label("VisitorHotkey")); content.Controls.Add(visitorHotkey);
        visitorHotkey.KeyDown += (_, e) =>
        {
            e.SuppressKeyPress = true;
            if (e.KeyCode is Keys.Delete or Keys.Back) { visitorHotkeyModifiers = visitorHotkeyKey = 0; }
            else if (e.KeyCode is not (Keys.ControlKey or Keys.ShiftKey or Keys.Menu) && (e.Control || e.Alt))
            {
                visitorHotkeyModifiers = (e.Control ? 2u : 0) | (e.Alt ? 1u : 0) | (e.Shift ? 4u : 0);
                visitorHotkeyKey = (uint)e.KeyCode;
            }
            UpdateVisitorHotkeyText();
        };
        content.Controls.Add(visitorApply); content.Controls.Add(Label("VisitorHelp"));
        visitorPage.Controls.Add(content); Controls.Add(visitorPage); mainNavigation.SendToBack();
        visitorSwitch.CheckedChanged += async (_, _) =>
        { if (!visitorUiUpdating) await visitorMode!.SetActiveAsync(visitorSwitch.Checked); };
        browse.Click += (_, _) =>
        {
            if (visitorSource.SelectedIndex == (int)VisitorSourceKind.Folder)
            {
                using FolderBrowserDialog dialog = new();
                if (dialog.ShowDialog(this) == DialogResult.OK) visitorPath.Text = dialog.SelectedPath;
            }
            else
            {
                using OpenFileDialog dialog = new() { CheckFileExists = true, Filter = visitorSource.SelectedIndex == 1 ? "MP4 (*.mp4)|*.mp4" : "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.webp" };
                if (dialog.ShowDialog(this) == DialogResult.OK) visitorPath.Text = dialog.FileName;
            }
        };
        visitorApply.Click += async (_, _) => await SaveVisitorUiAsync();
        var settings = visitorMode!.Settings;
        visitorHotkeyModifiers = settings.HotkeyModifiers; visitorHotkeyKey = settings.HotkeyKey;
        visitorSource.SelectedIndex = -1;
        visitorPath.Text = settings.Path; visitorInterval.Value = settings.IntervalMilliseconds / 1000;
        visitorShuffle.Checked = settings.Shuffle;
        visitorMode.Changed += UpdateVisitorUi;
        ApplyVisitorPresentation(); UpdateVisitorUi();
    }

    private void SelectVisitorSection()
    {
        if (!ResolveWidgetNavigation()) return;
        showingWidgets = false; showingVisitor = true;
        widgetsPage!.Visible = wallpaperPage!.Visible = false; visitorPage!.Visible = true;
        mainNavigation!.SendToBack(); PerformLayout(); ApplyVisitorPresentation(); UpdateWidgetPresentation();
    }

    private async Task SaveVisitorUiAsync()
    {
        var source = (VisitorSourceKind)Math.Max(0, visitorSource.SelectedIndex);
        string path = visitorPath.Text;
        uint interval = (uint)visitorInterval.Value * 1000;
        bool shuffle = visitorShuffle.Checked;
        uint modifiers = visitorHotkeyModifiers, key = visitorHotkeyKey;
        if (GlobalHotkeyManager.HasConflict(GetHotkeyBindings().Where(b => b.Id != HOTKEY_VISITOR), modifiers, key))
        { visitorStatus.Text = Localization.Get("SettingsDuplicateHotkey"); return; }
        var old = VisitorHotkeyBinding();
        bool changed = old.Modifiers != modifiers || old.Key != key;
        if (changed && servicesEnabled && IsHandleCreated)
        {
            var failures = hotkeyManager.Replace(Handle, new[] { new HotkeyBinding(HOTKEY_VISITOR, modifiers, key, Localization.Get("VisitorTitle")) { NoRepeat = true } });
            if (failures.Count > 0)
            {
                var rollbackFailures = hotkeyManager.Replace(Handle, new[] { old });
                ShowHotkeyErrors(failures.Concat(rollbackFailures).ToArray(), true); return;
            }
        }
        bool mediaChanged = visitorMode!.Settings.Source != source || !string.Equals(visitorMode.Settings.Path, path, StringComparison.OrdinalIgnoreCase);
        bool intervalChanged = visitorMode.Settings.IntervalMilliseconds != interval;
        await visitorMode.ConfigureAsync(s => s with { Source = source, Path = path,
            IntervalMilliseconds = interval, Shuffle = shuffle,
            HotkeyModifiers = modifiers, HotkeyKey = key }, reloadMedia: mediaChanged);
        if (!mediaChanged && intervalChanged && VisitorFolderActive) visitorSlideshowClock.Restart(visitorMode.Settings.IntervalMilliseconds);
    }

    private void UpdateVisitorUi()
    {
        if (visitorPage == null || IsDisposed || Disposing) return;
        visitorUiUpdating = true;
        try
        {
            ProjectVisitorPlaybackControls();
            visitorSwitch.Checked = visitorMode!.Active;
            visitorSwitch.Enabled = !visitorMode.Busy;
            visitorApply.Enabled = !visitorMode.Busy;
            visitorStatus.Text = Localization.Get(visitorMode.Error ?? (visitorMode.Busy ? "VisitorBusy" : visitorMode.Active ? "VisitorActive" : "VisitorInactive"));
            visitorSourceAvailable = HasVisitorSource(visitorMode.Settings);
            visitorMainBar.Visible = VisitorMainSwitchAvailable;
            visitorMainLabel.Text = Localization.Get("VisitorTitle") + " · " + Localization.Get(visitorMode.Active ? "VisitorOn" : "VisitorOff");
            ApplyVisitorControlAvailability();
            UpdateVisitorCopyButton();
            LayoutWallpaperPage();
            if (!visitorMode.Active && !visitorMode.Busy) { CheckSlideshowStatus(); UpdateVideoControls(); ArmCustomSlideshowPreciseTimer(); }
        }
        finally { visitorUiUpdating = false; }
    }

    private void ApplyVisitorControlAvailability()
    {
        if (visitorMode == null || wallpaperContent == null) return;
        IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
        var controls = Descendants(wallpaperContent).Where(c => c != visitorMainBar && !visitorMainBar.Contains(c)).ToArray();
        if (visitorMode.Active)
        {
            foreach (Control control in controls)
                if (!visitorDisabledControls.ContainsKey(control)) visitorDisabledControls[control] = control.Enabled;
            foreach (Control control in controls) control.Enabled = false;
            if (VisitorFolderActive)
                foreach (Control control in new Control[] { slideshowCard, currentWallpaperCard, displayCard,
                    visitorFolderDisplay, intervalComboBox, shuffleCheckBox, pauseButton, pinButton, nextWallpaperButton,
                    positionComboBox, transitionComboBox, transitionDirectionComboBox, transitionDurationComboBox }
                    .Concat(controls.OfType<Label>())) control.Enabled = true;
            if (VisitorFolderActive)
            {
                bool ready = !visitorMode.Busy && !visitorClosing;
                pauseButton.Enabled = pinButton.Enabled = intervalComboBox.Enabled = shuffleCheckBox.Enabled = ready;
                transitionDirectionComboBox.Enabled = ready && selectedTransitionKind is WallpaperTransitionKind.DesktopWipe
                    or WallpaperTransitionKind.DesktopSlide or WallpaperTransitionKind.DesktopZoomFade;
                nextWallpaperButton.Enabled = ready && !visitorSlideshowClock.Paused && !visitorAdvanceRunning;
            }
        }
        if (!visitorMode.Active)
        {
            foreach (var entry in visitorDisabledControls) if (!entry.Key.IsDisposed) entry.Key.Enabled = entry.Value;
            visitorDisabledControls.Clear();
        }
        visitorFolderDisplay.Visible = VisitorFolderActive;
        folderTextBox.Visible = folderButton.Visible = wallpaperCountLabel.Visible = !VisitorFolderActive;
        bool selectedVideo = wallpaperModeCombo.SelectedIndex == 1 && !VisitorFolderActive;
        videoCard.Visible = selectedVideo;
        slideshowCard.Visible = displayCard.Visible = currentWallpaperCard.Visible = nextWallpaperButton.Visible = !selectedVideo;
        if (VisitorFolderActive) { videoStatusLabel.Visible = false; statusLabel.Visible = visitorSlideshowClock.Paused; }
    }

    private void ApplyVisitorPresentation()
    {
        if (visitorPage == null) return;
        visitorPage.BackColor = AppTheme.WindowBackground(darkMode);
        visitorPage.ForeColor = AppTheme.TextPrimary(darkMode);
        SettingsControlTheme.Apply(visitorPage.Controls, darkMode, visitorPage.BackColor, visitorPage.ForeColor,
            AppTheme.InputBackground(darkMode), AppTheme.ControlBackground(darkMode));
        void Translate(Control parent)
        {
            foreach (Control control in parent.Controls)
            { if (control.Tag is string key) control.Text = Localization.Get(key); Translate(control); }
        }
        Translate(visitorPage);
        visitorSwitch.AccessibleName = Localization.Get("VisitorTitle");
        visitorPath.AccessibleName = Localization.Get("VisitorPath");
        visitorFolderDisplay.AccessibleName = Localization.Get("VisitorPath");
        visitorInterval.AccessibleName = Localization.Get("VisitorInterval");
        visitorHotkey.AccessibleName = Localization.Get("VisitorHotkey"); UpdateVisitorHotkeyText();
        visitorSwitch.BackColor = visitorMainBar.BackColor = AppTheme.WindowBackground(darkMode);
        visitorMainLabel.ForeColor = AppTheme.TextPrimary(darkMode);
        visitorShuffle.Text = Localization.Get("VisitorShuffle"); visitorApply.Text = Localization.Get("SettingsSave");
        int selected = visitorSource.SelectedIndex < 0 ? (int)visitorMode!.Settings.Source : visitorSource.SelectedIndex;
        visitorSource.Items.Clear(); visitorSource.Items.AddRange(new object[] { Localization.Get("VisitorImage"), Localization.Get("VisitorVideo"), Localization.Get("VisitorFolder") });
        visitorSource.SelectedIndex = Math.Clamp(selected, 0, 2);
        visitorNavigation!.Text = "◉   " + Localization.Get("VisitorTitle");
        StyleButton(visitorNavigation, showingVisitor ? AppTheme.SelectionBackground(darkMode) : mainNavigation!.BackColor, visitorPage.ForeColor);
        visitorNavigation.FlatAppearance.BorderSize = 0;
        UpdateVisitorUi();
    }

    internal bool VisitorMainSwitchAvailable => visitorMode?.Active == true || visitorSourceAvailable;
    internal static bool HasVisitorSource(VisitorModeSettings settings)
    {
        try
        {
            if (settings.Source == VisitorSourceKind.Folder)
                return Directory.Exists(settings.Path) && Directory.EnumerateFiles(settings.Path).Any(path => WallpaperImageInfo.IsSupportedWallpaperExtension(path) || Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase));
            if (!File.Exists(settings.Path)) return false;
            if (settings.Source == VisitorSourceKind.Video) return Path.GetExtension(settings.Path).Equals(".mp4", StringComparison.OrdinalIgnoreCase);
            if (!WallpaperImageInfo.IsSupportedWallpaperExtension(settings.Path)) return false;
            using Image image = Image.FromFile(settings.Path); return image.Width > 0 && image.Height > 0;
        }
        catch { return false; }
    }

    private void UpdateVisitorHotkeyText() => visitorHotkey.Text = visitorHotkeyModifiers == 0 || visitorHotkeyKey == 0
        ? Localization.Get("SettingsModifierDisabled") : GlobalHotkeyManager.FormatHotkey(visitorHotkeyModifiers, visitorHotkeyKey);
    private HotkeyBinding VisitorHotkeyBinding() => new(HOTKEY_VISITOR, visitorMode?.Settings.HotkeyModifiers ?? 0,
        visitorMode?.Settings.HotkeyKey ?? 0, Localization.Get("VisitorTitle")) { NoRepeat = true };
}
