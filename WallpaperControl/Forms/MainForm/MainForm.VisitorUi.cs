namespace WallpaperControl;

public partial class MainForm
{
    private Panel? visitorPage;
    private Button? visitorNavigation;
    private bool showingVisitor;
    private bool visitorUiUpdating;
    private readonly VisitorToggleSwitch visitorSwitch = new();
    private readonly Label visitorStatus = new() { AutoSize = true, MaximumSize = new Size(670, 0) };
    private readonly ComboBox visitorSource = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };
    private readonly TextBox visitorPath = new() { Width = 620, ReadOnly = true };
    private readonly NumericUpDown visitorInterval = new() { Minimum = 1, Maximum = 86400, Width = 160 };
    private readonly CheckBox visitorShuffle = new() { AutoSize = true };
    private readonly CheckBox visitorWidget = new() { AutoSize = true };
    private readonly CheckBox visitorLocked = new() { AutoSize = true };
    private readonly Button visitorApply = new() { AutoSize = true, MinimumSize = new Size(140, 36) };

    private void InitializeVisitorUi()
    {
        visitorNavigation = MainNavigationButton("VisitorTitle", "◉", 120);
        visitorNavigation.TabIndex = 2; mainNavigation!.Controls.Add(visitorNavigation);
        visitorNavigation.Click += (_, _) =>
        {
            if (!ResolveWidgetNavigation()) return;
            showingWidgets = false; showingVisitor = true;
            widgetsPage!.Visible = wallpaperPage!.Visible = false; visitorPage!.Visible = true;
            mainNavigation.SendToBack(); PerformLayout(); ApplyVisitorPresentation(); UpdateWidgetPresentation();
        };
        visitorPage = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Visible = false, Padding = new Padding(24) };
        FlowLayoutPanel content = new() { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        Label Label(string key) => new() { Text = Localization.Get(key), Tag = key, AutoSize = true, MaximumSize = new Size(670, 0), Margin = new Padding(3, 12, 3, 5) };
        content.Controls.Add(Label("VisitorTitle"));
        content.Controls.Add(visitorSwitch); content.Controls.Add(visitorStatus);
        content.Controls.Add(Label("VisitorSource")); content.Controls.Add(visitorSource);
        content.Controls.Add(Label("VisitorPath")); content.Controls.Add(visitorPath);
        Button browse = new() { Tag = "VisitorBrowse", AutoSize = true, MinimumSize = new Size(140, 36) };
        content.Controls.Add(browse);
        content.Controls.Add(Label("VisitorInterval")); content.Controls.Add(visitorInterval);
        content.Controls.Add(visitorShuffle); content.Controls.Add(visitorWidget); content.Controls.Add(visitorLocked);
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
        visitorSource.SelectedIndex = -1;
        visitorPath.Text = settings.Path; visitorInterval.Value = settings.IntervalMilliseconds / 1000;
        visitorShuffle.Checked = settings.Shuffle; visitorWidget.Checked = settings.WidgetEnabled; visitorLocked.Checked = settings.WidgetLocked;
        visitorMode.Changed += UpdateVisitorUi;
        ApplyVisitorPresentation(); UpdateVisitorUi();
    }

    private Task SaveVisitorUiAsync()
    {
        var source = (VisitorSourceKind)Math.Max(0, visitorSource.SelectedIndex);
        string path = visitorPath.Text;
        uint interval = (uint)visitorInterval.Value * 1000;
        bool shuffle = visitorShuffle.Checked, widget = visitorWidget.Checked, locked = visitorLocked.Checked;
        return visitorMode!.ConfigureAsync(s => s with { Source = source, Path = path,
            IntervalMilliseconds = interval, Shuffle = shuffle, WidgetEnabled = widget, WidgetLocked = locked });
    }

    private void UpdateVisitorUi()
    {
        if (visitorPage == null || IsDisposed || Disposing) return;
        visitorUiUpdating = true;
        try
        {
            visitorSwitch.Checked = visitorMode!.Active;
            visitorSwitch.Enabled = !visitorMode.Busy;
            visitorApply.Enabled = !visitorMode.Busy;
            visitorStatus.Text = Localization.Get(visitorMode.Error ?? (visitorMode.Busy ? "VisitorBusy" : visitorMode.Active ? "VisitorActive" : "VisitorInactive"));
            wallpaperContent!.Enabled = !visitorMode.Active;
            if (!visitorMode.Active && !visitorMode.Busy) { CheckSlideshowStatus(); UpdateVideoControls(); }
        }
        finally { visitorUiUpdating = false; }
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
        visitorInterval.AccessibleName = Localization.Get("VisitorInterval");
        visitorShuffle.Text = Localization.Get("VisitorShuffle"); visitorWidget.Text = Localization.Get("VisitorWidget");
        visitorLocked.Text = Localization.Get("VisitorLocked"); visitorApply.Text = Localization.Get("SettingsSave");
        int selected = visitorSource.SelectedIndex < 0 ? (int)visitorMode!.Settings.Source : visitorSource.SelectedIndex;
        visitorSource.Items.Clear(); visitorSource.Items.AddRange(new object[] { Localization.Get("VisitorImage"), Localization.Get("VisitorVideo"), Localization.Get("VisitorFolder") });
        visitorSource.SelectedIndex = Math.Clamp(selected, 0, 2);
        visitorNavigation!.Text = "◉   " + Localization.Get("VisitorTitle");
        StyleButton(visitorNavigation, showingVisitor ? AppTheme.SelectionBackground(darkMode) : mainNavigation!.BackColor, visitorPage.ForeColor);
        visitorNavigation.FlatAppearance.BorderSize = 0;
        UpdateVisitorUi();
    }
}
