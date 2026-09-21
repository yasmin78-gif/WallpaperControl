namespace WallpaperControl
{
    public partial class MainForm
    {
        private Panel? wallpaperPage;
        private Panel? wallpaperContent;
        private Panel? widgetsPage;
        private Panel? mainNavigation;
        private Button? wallpaperNavigation;
        private Button? widgetsNavigation;
        private WidgetSettingsEditor? widgetEditor;
        private WidgetEditSession? widgetEditSession;
        private bool showingWidgets;

        private void FitMainWindowToMonitor()
        {
            Rectangle area = Screen.FromControl(this).WorkingArea;
            int scaleWidth = 960 * DeviceDpi / 96, scaleHeight = 620 * DeviceDpi / 96;
            MinimumSize = new Size(Math.Min(scaleWidth, area.Width), Math.Min(scaleHeight, area.Height));
            Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
            Location = new Point(Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width)),
                Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height)));
        }
        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            if (mainNavigation == null) return;
            FitMainWindowToMonitor();
            LayoutWallpaperPage();
        }

        private void InitializeMainNavigation()
        {
            SuspendLayout();
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
            wallpaperPage = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            wallpaperContent = new Panel();
            foreach (Control control in Controls.Cast<Control>().ToArray()) wallpaperContent.Controls.Add(control);
            wallpaperPage.Controls.Add(wallpaperContent);
            Resize += (_, _) => LayoutWallpaperPage();
            mainNavigation = new Panel { Dock = DockStyle.Left, Width = 200, Padding = new Padding(10) };
            wallpaperNavigation = MainNavigationButton("MainNavWallpaper", "▣", 20);
            widgetsNavigation = MainNavigationButton("SettingsTabWidgets", "▥", 70);
            mainNavigation.Controls.Add(wallpaperNavigation); mainNavigation.Controls.Add(widgetsNavigation);
            wallpaperNavigation.Click += (_, _) => SelectMainSection(false);
            widgetsNavigation.TabIndex = 1;
            widgetsNavigation.Click += (_, _) => SelectMainSection(true);
            // Application actions remain accessible from either main section.
            FlowLayoutPanel applicationActions = new() { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8), WrapContents = false };
            settingsButton.Anchor = aboutButton.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            settingsButton.TabStop = aboutButton.TabStop = true;
            settingsButton.TabIndex = 0; aboutButton.TabIndex = 1;
            applicationActions.Controls.Add(settingsButton); applicationActions.Controls.Add(aboutButton);
            mainNavigation.Controls.Add(applicationActions);
            widgetsPage = new Panel { Dock = DockStyle.Fill, Visible = false };
            widgetEditor = new WidgetSettingsEditor(widgetManager.Settings, darkMode, Localization.CurrentLanguage,
                value => widgetEditSession?.Preview(value)) { Dock = DockStyle.Fill };
            widgetEditor.ConfigureNotesManager(widgetManager.ShowNotesManager);
            widgetEditSession = new WidgetEditSession(widgetManager, widgetEditor.ReadWidgetSettings, widgetEditor.LoadSettings);
            FlowLayoutPanel actions = new() { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
            foreach (var (key, action) in new (string, Action)[] {
                ("SettingsSave", () => { if (!widgetEditSession.Save()) ShowWidgetSaveFailure(); }),
                ("WidgetDiscard", () => widgetEditSession.Discard()),
                ("SettingsRestoreDefaults", () => widgetEditor.ResetDefaults()) })
            {
                Button button = CalendarSourceDialogStyle.Button(Localization.Get(key)); button.Tag = key;
                button.Click += (_, _) => action(); actions.Controls.Add(button);
            }
            widgetsPage.Controls.Add(widgetEditor); widgetsPage.Controls.Add(actions);
            Controls.Add(wallpaperPage); Controls.Add(widgetsPage); Controls.Add(mainNavigation);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = true;
            MinimumSize = new Size(960, 620); ClientSize = new Size(1260, 800);
            InitializeWallpaperPage();
            ResumeLayout(true);
            UpdateWidgetPresentation();
            LayoutWallpaperPage();
        }
        private static Button MainNavigationButton(string key, string icon, int top) => new()
        {
            Text = $"{icon}   {Localization.Get(key)}", Tag = key, AccessibleDescription = icon,
            Location = new Point(10, top), Size = new Size(180, 44), TextAlign = ContentAlignment.MiddleLeft,
            FlatStyle = FlatStyle.Flat, Padding = new Padding(8, 0, 0, 0), TabStop = true
        };
        private void SelectMainSection(bool widgets)
        {
            if (widgets == showingWidgets) return;
            if (!widgets && !ResolveWidgetNavigation()) return;
            if (widgets) widgetEditSession!.Begin();
            showingWidgets = widgets;
            widgetsPage!.Visible = widgets; wallpaperPage!.Visible = !widgets;
            UpdateWidgetPresentation();
        }
        private bool ResolveWidgetNavigation()
        {
            if (widgetEditSession == null || !widgetEditSession.Active) return true;
            long revision = SettingsPersistence.FailureRevision;
            bool result = widgetEditSession.TryLeave(() =>
            {
                using WidgetChangesDialog dialog = new(darkMode, Localization.CurrentLanguage);
                dialog.ShowDialog(this); return dialog.Decision;
            });
            if (!result && revision != SettingsPersistence.FailureRevision) ShowWidgetSaveFailure();
            return result;
        }
        private void ShowWidgetSaveFailure() => MessageBox.Show(this, Localization.Get("SettingsSaveFailed"), "Wallpaper Control", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        private void UpdateWidgetPresentation() => ApplyNavigationPresentation(Localization.CurrentLanguage);

        private void ApplyNavigationPresentation(string language)
        {
            if (mainNavigation == null || widgetsPage == null || widgetEditor == null) return;
            wallpaperPage!.BackColor = AppTheme.WindowBackground(darkMode);
            wallpaperContent!.BackColor = wallpaperPage.BackColor;
            mainNavigation.BackColor = AppTheme.SidebarBackground(darkMode);
            widgetEditor.ApplyPresentation(darkMode, language);
            foreach (Control control in widgetsPage.Controls)
                if (control != widgetEditor)
                {
                    foreach (Control button in control.Controls)
                        if (button.Tag is string key) button.Text = Localization.Get(key, language);
                    SettingsControlTheme.Apply(control.Controls, darkMode, AppTheme.WindowBackground(darkMode), AppTheme.TextPrimary(darkMode), AppTheme.InputBackground(darkMode), AppTheme.ControlBackground(darkMode));
                    control.BackColor = AppTheme.WindowBackground(darkMode);
                }
            foreach (Button button in new[] { wallpaperNavigation!, widgetsNavigation! })
            {
                button.Text = $"{button.AccessibleDescription}   {Localization.Get((string)button.Tag!, language)}";
                StyleButton(button, (button == widgetsNavigation) == showingWidgets ? AppTheme.SelectionBackground(darkMode) : mainNavigation.BackColor, AppTheme.TextPrimary(darkMode));
                button.FlatAppearance.BorderSize = 0;
            }
        }
    }
}
