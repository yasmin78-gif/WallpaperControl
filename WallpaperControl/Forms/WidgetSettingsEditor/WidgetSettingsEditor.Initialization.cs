namespace WallpaperControl
{
    internal sealed partial class WidgetSettingsEditor : UserControl
    {
        private readonly List<Font> ownedFonts = new();
        private WidgetSettings initialWidgetSettings;
        private readonly Action<WidgetSettings>? widgetPreviewChanged;
        private string previewLanguageCode;
        private bool darkMode;
        private bool loadingControls = true;
        private readonly TabControl pages = new() { Dock = DockStyle.Fill, Appearance = TabAppearance.FlatButtons, SizeMode = TabSizeMode.Fixed, ItemSize = new Size(0, 1), Multiline = true };
        private readonly FlowLayoutPanel navigation = new() { Dock = DockStyle.Left, Width = 218, AutoScroll = true, WrapContents = false, FlowDirection = FlowDirection.TopDown, Padding = new Padding(8) };
        private readonly List<(string Key, string Icon, TabPage Page, Button Button)> entries = new();
        internal WidgetSettingsEditor(WidgetSettings settings, bool dark = false, string? language = null, Action<WidgetSettings>? preview = null)
        {
            initialWidgetSettings = settings.Clone(); calendarSources = new(settings.CalendarSources);
            darkMode = dark; previewLanguageCode = language ?? Localization.CurrentLanguage; widgetPreviewChanged = preview;
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
            Size = new Size(1050, 680); Font = CreateOwnedFont("Segoe UI", 10);
            Controls.Add(pages); Controls.Add(navigation);
            TabPage clockPage = AddPage("SettingsNavClock", "◷");
            TabPage nextWidgetPage = AddPage("SettingsNavNextWallpaper", "▷");
            TabPage systemWidgetPage = AddPage("SettingsNavSystem", "▥");
            TabPage weatherWidgetPage = AddPage("SettingsNavWeather", "☀");
            TabPage calendarWidgetPage = AddPage("SettingsNavCalendar", "▣");
            TabPage wallpaperInfoPage = AddPage("WallpaperInfoTitle", "ⓘ"); InitializeWallpaperInfoPage(wallpaperInfoPage);
            TabPage notesPage = AddPage("NotesTitle", "▤"); InitializeNotesPage(notesPage);
            InitializeWebPage(AddPage("WebTitle", "⊕"));
            #region Clock widget page

            Label widgetsTitle = new Label
            {
                Text = Localization.Get("SettingsNavClock", previewLanguageCode),
                Tag = "SettingsNavClock",
                Location = new Point(18, 14),
                AutoSize = true,
                Font = CreateOwnedFont("Segoe UI", 12, FontStyle.Bold)
            };

            clockSettingsPreview = new ClockSettingsPreview
            {
                Location = new Point(18, 46),
                Size = new Size(810, 180),
                Style = initialWidgetSettings.ClockStyle,
                ShowSeconds = initialWidgetSettings.ClockShowSeconds,
                LanguageCode = previewLanguageCode
            };

            Label clockStyleLabel = new Label
            {
                Text = Localization.Get("SettingsClockStyle", previewLanguageCode),
                Tag = "SettingsClockStyle",
                Location = new Point(18, 238),
                Size = new Size(220, 24),
                Font = CreateOwnedFont("Segoe UI", 9.5f, FontStyle.Bold)
            };

            clockStyleComboBox = new ComboBox
            {
                Visible = false,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            RefreshClockStyleChoices(initialWidgetSettings.ClockStyle);

            string[] styleKeys = { "ClockStyleMinimal", "ClockStyleChrome", "ClockStyleClean", "ClockStyleGlow", "ClockStyleClassic" };
            for (int i = 0; i < styleKeys.Length; i++)
            {
                ClockWidgetStyle cardStyle = (ClockWidgetStyle)i;
                ClockStyleCard card = new ClockStyleCard
                {
                    Location = new Point(18 + i * 160, 266),
                    Size = new Size(148, 92),
                    Style = cardStyle,
                    Caption = Localization.Get(styleKeys[i], previewLanguageCode),
                    Selected = cardStyle == initialWidgetSettings.ClockStyle
                };
                card.Click += (_, _) => SelectClockStyle(cardStyle);
                clockStyleCards.Add(card);
                clockPage.Controls.Add(card);
            }

            GroupBox clockOptions = new GroupBox
            {
                Text = Localization.Get("SettingsClockEnabled", previewLanguageCode),
                Tag = "SettingsClockEnabled",
                Location = new Point(18, 370),
                Size = new Size(500, 190)
            };

            clockEnabledCheckBox = new CheckBox
            {
                Text = Localization.Get("SettingsClockEnabled", previewLanguageCode),
                Tag = "SettingsClockEnabled",
                Location = new Point(18, 28),
                AutoSize = true,
                Checked = initialWidgetSettings.ClockEnabled
            };

            clockSecondsCheckBox = new CheckBox
            {
                Text = Localization.Get("SettingsClockShowSeconds", previewLanguageCode),
                Tag = "SettingsClockShowSeconds",
                Location = new Point(250, 28),
                AutoSize = true,
                Checked = initialWidgetSettings.ClockShowSeconds
            };

            Label clockSizeLabel = new Label
            {
                Text = Localization.Get("SettingsClockSize", previewLanguageCode),
                Tag = "SettingsClockSize",
                Location = new Point(18, 68),
                Size = new Size(210, 25)
            };

            clockSizeNumeric = new NumericUpDown
            {
                Location = new Point(250, 64),
                Size = new Size(90, 28),
                Minimum = 70,
                Maximum = 240,
                Increment = 5,
                Value = Math.Clamp(initialWidgetSettings.ClockSize, 70, 240)
            };

            clockLockedCheckBox = new CheckBox
            {
                Text = Localization.Get("SettingsWidgetLocked", previewLanguageCode),
                Tag = "SettingsWidgetLocked",
                Location = new Point(18, 108),
                AutoSize = true,
                Checked = initialWidgetSettings.ClockLocked
            };

            clockOptions.Controls.Add(clockEnabledCheckBox);
            clockOptions.Controls.Add(clockSecondsCheckBox);
            clockOptions.Controls.Add(clockSizeLabel);
            clockOptions.Controls.Add(clockSizeNumeric);
            clockOptions.Controls.Add(clockLockedCheckBox);

            #endregion

            #region Next wallpaper widget page

            Label nextWidgetPageTitle = new Label
            {
                Text = Localization.Get("SettingsNavNextWallpaper", previewLanguageCode),
                Tag = "SettingsNavNextWallpaper",
                Location = new Point(18, 18),
                AutoSize = true,
                Font = CreateOwnedFont("Segoe UI", 12, FontStyle.Bold)
            };
            nextWidgetPage.Controls.Add(nextWidgetPageTitle);

            GroupBox nextOptions = new GroupBox
            {
                Text = Localization.Get("SettingsNextWidgetTitle", previewLanguageCode),
                Tag = "SettingsNextWidgetTitle",
                Location = new Point(18, 58),
                Size = new Size(500, 150)
            };

            nextWidgetEnabledCheckBox = new CheckBox
            {
                Text = Localization.Get("SettingsNextWidgetEnabled", previewLanguageCode),
                Tag = "SettingsNextWidgetEnabled",
                Location = new Point(18, 32),
                AutoSize = true,
                Checked = initialWidgetSettings.NextEnabled
            };

            nextWidgetLockedCheckBox = new CheckBox
            {
                Text = Localization.Get("SettingsWidgetLocked", previewLanguageCode),
                Tag = "SettingsWidgetLocked",
                Location = new Point(18, 72),
                AutoSize = true,
                Checked = initialWidgetSettings.NextLocked
            };

            nextOptions.Controls.Add(nextWidgetEnabledCheckBox);
            nextOptions.Controls.Add(nextWidgetLockedCheckBox);

            Label nextStyleLabel = new Label
            {
                Text = Localization.Get("SettingsSystemStyle", previewLanguageCode),
                Tag = "SettingsSystemStyle",
                Location = new Point(18, 114),
                AutoSize = true
            };
            nextWidgetStyleComboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(180, 110),
                Size = new Size(220, 28)
            };
            RefreshNextStyleChoices(initialWidgetSettings.NextStyle);
            nextOptions.Controls.Add(nextStyleLabel);
            nextOptions.Controls.Add(nextWidgetStyleComboBox);

            #endregion

            #region System widget page

            Label systemWidgetPageTitle = new Label
            {
                Text = Localization.Get("SettingsNavSystem", previewLanguageCode),
                Tag = "SettingsNavSystem",
                Location = new Point(18, 18),
                AutoSize = true,
                Font = CreateOwnedFont("Segoe UI", 12, FontStyle.Bold)
            };
            systemWidgetPage.Controls.Add(systemWidgetPageTitle);

            GroupBox systemOptions = new GroupBox
            {
                Text = Localization.Get("SettingsSystemWidgetTitle", previewLanguageCode),
                Tag = "SettingsSystemWidgetTitle",
                Location = new Point(18, 58),
                Size = new Size(620, 430)
            };

            systemWidgetEnabledCheckBox = new CheckBox
            {
                Text = Localization.Get("SettingsSystemWidgetEnabled", previewLanguageCode),
                Tag = "SettingsSystemWidgetEnabled",
                Location = new Point(18, 32),
                AutoSize = true,
                Checked = initialWidgetSettings.SystemEnabled
            };

            systemWidgetLockedCheckBox = new CheckBox
            {
                Text = Localization.Get("SettingsWidgetLocked", previewLanguageCode),
                Tag = "SettingsWidgetLocked",
                Location = new Point(18, 68),
                AutoSize = true,
                Checked = initialWidgetSettings.SystemLocked
            };

            Label systemStyleLabel = new Label
            {
                Text = Localization.Get("SettingsSystemStyle", previewLanguageCode),
                Tag = "SettingsSystemStyle",
                Location = new Point(18, 110),
                AutoSize = true
            };

            systemWidgetStyleComboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(190, 106),
                Size = new Size(170, 30)
            };
            systemWidgetStyleComboBox.Items.Add(Localization.Get("ClockStyleMinimal", previewLanguageCode));
            systemWidgetStyleComboBox.Items.Add(Localization.Get("ClockStyleClean", previewLanguageCode));
            systemWidgetStyleComboBox.Items.Add(Localization.Get("ClockStyleGlow", previewLanguageCode));
            systemWidgetStyleComboBox.SelectedIndex = Math.Clamp((int)initialWidgetSettings.SystemStyle, 0, 2);

            Label systemRefreshLabel = new Label
            {
                Text = Localization.Get("SettingsSystemRefresh", previewLanguageCode),
                Tag = "SettingsSystemRefresh",
                Location = new Point(18, 150),
                AutoSize = true
            };

            systemWidgetRefreshComboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(190, 146),
                Size = new Size(125, 30)
            };
            systemWidgetRefreshComboBox.Items.AddRange(new object[] { "1 s", "2 s", "5 s" });
            systemWidgetRefreshComboBox.SelectedIndex = initialWidgetSettings.SystemRefreshSeconds switch { 1 => 0, 5 => 2, _ => 1 };

            Label modulesLabel = new Label
            {
                Text = Localization.Get("SettingsSystemModules", previewLanguageCode),
                Tag = "SettingsSystemModules",
                Location = new Point(18, 194),
                AutoSize = true,
                Font = CreateOwnedFont("Segoe UI", 9f, FontStyle.Bold)
            };

            systemShowCpuCheckBox = CreateSystemModuleCheckBox("SettingsSystemCpu", 18, 225, initialWidgetSettings.SystemShowCpu);
            systemShowRamCheckBox = CreateSystemModuleCheckBox("SettingsSystemRam", 18, 258, initialWidgetSettings.SystemShowRam);
            systemShowGpuCheckBox = CreateSystemModuleCheckBox("SettingsSystemGpu", 18, 291, initialWidgetSettings.SystemShowGpu);
            systemShowVramCheckBox = CreateSystemModuleCheckBox("SettingsSystemVram", 300, 225, initialWidgetSettings.SystemShowVram);
            systemShowNetworkCheckBox = CreateSystemModuleCheckBox("SettingsSystemNetwork", 300, 258, initialWidgetSettings.SystemShowNetwork);
            systemShowDrivesCheckBox = CreateSystemModuleCheckBox("SettingsSystemDrives", 300, 291, initialWidgetSettings.SystemShowDrives);

            Label systemHint = new Label
            {
                Text = Localization.Get("SettingsSystemWidgetHint", previewLanguageCode),
                Tag = "SettingsSystemWidgetHint",
                Location = new Point(18, 345),
                Size = new Size(570, 55)
            };

            systemOptions.Controls.Add(systemWidgetEnabledCheckBox);
            systemOptions.Controls.Add(systemWidgetLockedCheckBox);
            systemOptions.Controls.Add(systemStyleLabel);
            systemOptions.Controls.Add(systemWidgetStyleComboBox);
            systemOptions.Controls.Add(systemRefreshLabel);
            systemOptions.Controls.Add(systemWidgetRefreshComboBox);
            systemOptions.Controls.Add(modulesLabel);
            systemOptions.Controls.Add(systemShowCpuCheckBox);
            systemOptions.Controls.Add(systemShowRamCheckBox);
            systemOptions.Controls.Add(systemShowGpuCheckBox);
            systemOptions.Controls.Add(systemShowVramCheckBox);
            systemOptions.Controls.Add(systemShowNetworkCheckBox);
            systemOptions.Controls.Add(systemShowDrivesCheckBox);
            systemOptions.Controls.Add(systemHint);
            systemWidgetPage.Controls.Add(systemOptions);

            #endregion

            #region Weather widget page

            Label weatherWidgetPageTitle = new Label
            {
                Text = Localization.Get("SettingsNavWeather", previewLanguageCode),
                Tag = "SettingsNavWeather",
                Location = new Point(18, 18),
                AutoSize = true,
                Font = CreateOwnedFont("Segoe UI", 12, FontStyle.Bold)
            };
            weatherWidgetPage.Controls.Add(weatherWidgetPageTitle);

            GroupBox weatherOptions = new GroupBox
            {
                Text = Localization.Get("SettingsWeatherWidgetTitle", previewLanguageCode),
                Tag = "SettingsWeatherWidgetTitle",
                Location = new Point(18, 58),
                Size = new Size(620, 330)
            };

            weatherWidgetEnabledCheckBox = new CheckBox
            {
                Text = Localization.Get("SettingsWeatherWidgetEnabled", previewLanguageCode),
                Tag = "SettingsWeatherWidgetEnabled",
                Location = new Point(18, 32),
                AutoSize = true,
                Checked = initialWidgetSettings.WeatherEnabled
            };

            weatherWidgetLockedCheckBox = new CheckBox
            {
                Text = Localization.Get("SettingsWidgetLocked", previewLanguageCode),
                Tag = "SettingsWidgetLocked",
                Location = new Point(18, 68),
                AutoSize = true,
                Checked = initialWidgetSettings.WeatherLocked
            };

            Label weatherLocationLabel = new Label
            {
                Text = Localization.Get("SettingsWeatherLocation", previewLanguageCode),
                Tag = "SettingsWeatherLocation",
                Location = new Point(18, 110),
                AutoSize = true
            };

            weatherLocationTextBox = new TextBox
            {
                Location = new Point(190, 106),
                Size = new Size(205, 28),
                Text = initialWidgetSettings.WeatherLocationName
            };

            Button weatherApplyLocationButton = new Button
            {
                Text = Localization.Get("SettingsWeatherApplyLocation", previewLanguageCode),
                Tag = "SettingsWeatherApplyLocation",
                Location = new Point(405, 104),
                Size = new Size(110, 31),
                Cursor = Cursors.Hand
            };

            Label weatherStyleLabel = new Label
            {
                Text = Localization.Get("SettingsSystemStyle", previewLanguageCode),
                Tag = "SettingsSystemStyle",
                Location = new Point(18, 150),
                AutoSize = true
            };

            weatherWidgetStyleComboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(190, 146),
                Size = new Size(170, 30)
            };
            weatherWidgetStyleComboBox.Items.Add(Localization.Get("ClockStyleMinimal", previewLanguageCode));
            weatherWidgetStyleComboBox.Items.Add(Localization.Get("ClockStyleClean", previewLanguageCode));
            weatherWidgetStyleComboBox.Items.Add(Localization.Get("ClockStyleGlow", previewLanguageCode));
            weatherWidgetStyleComboBox.SelectedIndex = Math.Clamp((int)initialWidgetSettings.WeatherStyle, 0, 2);

            Label weatherRefreshLabel = new Label
            {
                Text = Localization.Get("SettingsWeatherRefresh", previewLanguageCode),
                Tag = "SettingsWeatherRefresh",
                Location = new Point(18, 190),
                AutoSize = true
            };

            weatherWidgetRefreshComboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(190, 186),
                Size = new Size(125, 30)
            };
            weatherWidgetRefreshComboBox.Items.AddRange(new object[] { "15 min", "30 min", "60 min", "120 min" });
            weatherWidgetRefreshComboBox.SelectedIndex = initialWidgetSettings.WeatherRefreshMinutes switch { 15 => 0, 60 => 2, 120 => 3, _ => 1 };

            weatherShowForecastCheckBox = new CheckBox
            {
                Text = Localization.Get("SettingsWeatherForecast", previewLanguageCode),
                Tag = "SettingsWeatherForecast",
                Location = new Point(18, 232),
                AutoSize = true,
                Checked = initialWidgetSettings.WeatherShowForecast
            };

            Label weatherHint = new Label
            {
                Text = Localization.Get("SettingsWeatherHint", previewLanguageCode),
                Tag = "SettingsWeatherHint",
                Location = new Point(18, 270),
                Size = new Size(570, 45)
            };

            weatherOptions.Controls.Add(weatherWidgetEnabledCheckBox);
            weatherOptions.Controls.Add(weatherWidgetLockedCheckBox);
            weatherOptions.Controls.Add(weatherLocationLabel);
            weatherOptions.Controls.Add(weatherLocationTextBox);
            weatherOptions.Controls.Add(weatherApplyLocationButton);
            weatherOptions.Controls.Add(weatherStyleLabel);
            weatherOptions.Controls.Add(weatherWidgetStyleComboBox);
            weatherOptions.Controls.Add(weatherRefreshLabel);
            weatherOptions.Controls.Add(weatherWidgetRefreshComboBox);
            weatherOptions.Controls.Add(weatherShowForecastCheckBox);
            weatherOptions.Controls.Add(weatherHint);
            weatherWidgetPage.Controls.Add(weatherOptions);

            #endregion

            #region Calendar widget page

            Label calendarWidgetPageTitle = new Label
            {
                Text = Localization.Get("SettingsNavCalendar", previewLanguageCode),
                Tag = "SettingsNavCalendar",
                Location = new Point(18, 18),
                AutoSize = true,
                Font = CreateOwnedFont("Segoe UI", 12, FontStyle.Bold)
            };
            calendarWidgetPage.Controls.Add(calendarWidgetPageTitle);

            GroupBox calendarOptions = new GroupBox
            {
                Text = Localization.Get("SettingsCalendarWidgetTitle", previewLanguageCode),
                Tag = "SettingsCalendarWidgetTitle",
                Location = new Point(18, 58),
                Size = new Size(630, 475)
            };

            calendarWidgetEnabledCheckBox = new CheckBox
            {
                Text = Localization.Get("SettingsCalendarWidgetEnabled", previewLanguageCode),
                Tag = "SettingsCalendarWidgetEnabled",
                Location = new Point(18, 32),
                AutoSize = true,
                Checked = initialWidgetSettings.CalendarEnabled
            };

            calendarWidgetLockedCheckBox = new CheckBox
            {
                Text = Localization.Get("SettingsWidgetLocked", previewLanguageCode),
                Tag = "SettingsWidgetLocked",
                Location = new Point(18, 68),
                AutoSize = true,
                Checked = initialWidgetSettings.CalendarLocked
            };

            Button calendarManageSourcesButton = new Button
            {
                Text = Localization.Get("CalendarSourcesManage", previewLanguageCode),
                Tag = "CalendarSourcesManage",
                Location = new Point(18, 108),
                Size = new Size(390, 36)
            };
            calendarManageSourcesButton.Click += (_, _) =>
            {
                using CalendarSourcesForm manager = new(calendarSources, ResolvePreviewDarkMode(), previewLanguageCode);
                manager.Opacity = FindForm()?.Opacity ?? 1;
                if (manager.ShowDialog(this) == DialogResult.OK)
                {
                    calendarSources = new(manager.Sources);
                    NotifyWidgetPreviewChanged();
                }
            };

            Label calendarStyleLabel = new Label
            {
                Text = Localization.Get("SettingsSystemStyle", previewLanguageCode),
                Tag = "SettingsSystemStyle",
                Location = new Point(18, 184),
                AutoSize = true
            };

            calendarWidgetStyleComboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(190, 180),
                Size = new Size(170, 30)
            };
            calendarWidgetStyleComboBox.Items.Add(Localization.Get("ClockStyleMinimal", previewLanguageCode));
            calendarWidgetStyleComboBox.Items.Add(Localization.Get("ClockStyleClean", previewLanguageCode));
            calendarWidgetStyleComboBox.Items.Add(Localization.Get("ClockStyleGlow", previewLanguageCode));
            calendarWidgetStyleComboBox.SelectedIndex = Math.Clamp((int)initialWidgetSettings.CalendarStyle, 0, 2);

            Label calendarEntriesLabel = new Label
            {
                Text = Localization.Get("SettingsCalendarEntries", previewLanguageCode),
                Tag = "SettingsCalendarEntries",
                Location = new Point(18, 224),
                AutoSize = true
            };

            calendarMaxEntriesComboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(190, 220),
                Size = new Size(125, 30)
            };
            calendarMaxEntriesComboBox.Items.AddRange(new object[] { "3", "5", "9" });
            calendarMaxEntriesComboBox.SelectedIndex = initialWidgetSettings.CalendarMaxEntries switch { 3 => 0, 5 => 1, _ => 2 };

            Label calendarRefreshLabel = new Label
            {
                Text = Localization.Get("SettingsCalendarRefresh", previewLanguageCode),
                Tag = "SettingsCalendarRefresh",
                Location = new Point(18, 264),
                AutoSize = true
            };

            calendarRefreshComboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(190, 260),
                Size = new Size(125, 30)
            };
            calendarRefreshComboBox.Items.AddRange(new object[] { "15 min", "30 min", "60 min", "120 min" });
            calendarRefreshComboBox.SelectedIndex = initialWidgetSettings.CalendarRefreshMinutes switch { 15 => 0, 60 => 2, 120 => 3, _ => 1 };

            calendarShowLocationCheckBox = new CheckBox
            {
                Text = Localization.Get("SettingsCalendarShowLocation", previewLanguageCode),
                Tag = "SettingsCalendarShowLocation",
                Location = new Point(18, 302),
                AutoSize = true,
                Checked = initialWidgetSettings.CalendarShowLocation
            };

            Label calendarMaximumHeightLabel = new Label
            {
                Text = Localization.Get("SettingsCalendarMaximumHeight", previewLanguageCode),
                Tag = "SettingsCalendarMaximumHeight", Location = new Point(18, 342), AutoSize = true
            };
            calendarMaximumHeightNumeric = new NumericUpDown
            {
                Location = new Point(220, 338), Size = new Size(125, 30),
                Minimum = CalendarViewport.MinimumMaximumHeight, Maximum = CalendarViewport.MaximumMaximumHeight,
                Increment = 50, Value = CalendarViewport.NormalizeMaximum(initialWidgetSettings.CalendarMaximumHeight)
            };
            calendarMaximumHeightNumeric.ValueChanged += (_, _) => NotifyWidgetPreviewChanged();
            calendarOptions.Controls.Add(calendarMaximumHeightLabel);
            calendarOptions.Controls.Add(calendarMaximumHeightNumeric);

            Label calendarHint = new Label
            {
                Text = Localization.Get("SettingsCalendarHint", previewLanguageCode),
                Tag = "SettingsCalendarHint",
                Location = new Point(18, 378),
                Size = new Size(575, 58)
            };

            calendarOptions.Controls.Add(calendarWidgetEnabledCheckBox);
            calendarOptions.Controls.Add(calendarWidgetLockedCheckBox);
            calendarOptions.Controls.Add(calendarManageSourcesButton);
            calendarOptions.Controls.Add(calendarStyleLabel);
            calendarOptions.Controls.Add(calendarWidgetStyleComboBox);
            calendarOptions.Controls.Add(calendarEntriesLabel);
            calendarOptions.Controls.Add(calendarMaxEntriesComboBox);
            calendarOptions.Controls.Add(calendarRefreshLabel);
            calendarOptions.Controls.Add(calendarRefreshComboBox);
            calendarOptions.Controls.Add(calendarShowLocationCheckBox);
            calendarOptions.Controls.Add(calendarHint);
            calendarWidgetPage.Controls.Add(calendarOptions);

            Label widgetHint = new Label
            {
                Text = Localization.Get("SettingsWidgetsHint", previewLanguageCode),
                Tag = "SettingsWidgetsHint",
                Location = new Point(18, 575),
                Size = new Size(810, 45),
                Font = CreateOwnedFont("Segoe UI", 8.25f)
            };

            clockPage.Controls.Add(widgetsTitle);
            clockPage.Controls.Add(clockSettingsPreview);
            clockPage.Controls.Add(clockStyleLabel);
            clockPage.Controls.Add(clockStyleComboBox);
            clockPage.Controls.Add(clockOptions);
            nextWidgetPage.Controls.Add(nextOptions);
            clockPage.Controls.Add(widgetHint);

            #endregion

            #region Live widget preview subscriptions

            clockEnabledCheckBox.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            clockLockedCheckBox.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            clockSizeNumeric.ValueChanged += (_, _) => NotifyWidgetPreviewChanged();
            clockSecondsCheckBox.CheckedChanged += (_, _) =>
            {
                clockSettingsPreview.ShowSeconds = clockSecondsCheckBox.Checked;
                clockSettingsPreview.Invalidate();
                foreach (ClockStyleCard card in clockStyleCards) { card.ShowSeconds = clockSecondsCheckBox.Checked; card.Invalidate(); }
                NotifyWidgetPreviewChanged();
            };
            clockStyleComboBox.SelectedIndexChanged += (_, _) =>
            {
                UpdateClockStyleSelection();
                NotifyWidgetPreviewChanged();
            };
            ConnectNotesPreview();
            ConnectWallpaperInfoPreview();
            nextWidgetEnabledCheckBox.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            nextWidgetLockedCheckBox.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            nextWidgetStyleComboBox.SelectedIndexChanged += (_, _) => NotifyWidgetPreviewChanged();
            systemWidgetEnabledCheckBox.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            systemWidgetLockedCheckBox.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            systemWidgetRefreshComboBox.SelectedIndexChanged += (_, _) => NotifyWidgetPreviewChanged();
            systemWidgetStyleComboBox.SelectedIndexChanged += (_, _) => NotifyWidgetPreviewChanged();
            systemShowCpuCheckBox.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            systemShowRamCheckBox.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            systemShowGpuCheckBox.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            systemShowVramCheckBox.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            systemShowNetworkCheckBox.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            systemShowDrivesCheckBox.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            weatherWidgetEnabledCheckBox.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            weatherWidgetLockedCheckBox.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            weatherWidgetRefreshComboBox.SelectedIndexChanged += (_, _) => NotifyWidgetPreviewChanged();
            weatherWidgetStyleComboBox.SelectedIndexChanged += (_, _) => NotifyWidgetPreviewChanged();
            weatherShowForecastCheckBox.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            weatherApplyLocationButton.Click += (_, _) => NotifyWidgetPreviewChanged();
            weatherLocationTextBox.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    weatherApplyLocationButton.PerformClick();
                    e.SuppressKeyPress = true;
                }
            };
            calendarWidgetEnabledCheckBox.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            calendarWidgetLockedCheckBox.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();
            calendarWidgetStyleComboBox.SelectedIndexChanged += (_, _) => NotifyWidgetPreviewChanged();
            calendarMaxEntriesComboBox.SelectedIndexChanged += (_, _) => NotifyWidgetPreviewChanged();
            calendarRefreshComboBox.SelectedIndexChanged += (_, _) => NotifyWidgetPreviewChanged();
            calendarShowLocationCheckBox.CheckedChanged += (_, _) => NotifyWidgetPreviewChanged();

            #endregion

            loadingControls = false;
            ApplyPresentation(dark, previewLanguageCode);
        }
    }
}
