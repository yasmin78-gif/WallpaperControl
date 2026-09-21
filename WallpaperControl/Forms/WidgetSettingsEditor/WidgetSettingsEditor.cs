using System.Globalization;

namespace WallpaperControl
{
    /// <summary>Owns the existing widget controls; persistence and edit transactions belong to its host.</summary>
    internal sealed partial class WidgetSettingsEditor
    {
        internal IReadOnlyList<string> WidgetKeys => entries.OrderBy(e => e.Button.TabIndex).Select(e => e.Key).ToArray();
        internal string? SelectedKey => pages.SelectedTab?.Tag as string;
        internal void SelectWidget(string key)
        {
            var entry = entries.First(e => e.Key == key);
            pages.SelectedTab = entry.Page;
            UpdateSelection();
        }
        private TabPage AddPage(string key, string icon)
        {
            TabPage page = new() { Tag = key, AutoScroll = true, Padding = new Padding(4) };
            Button button = new() { AutoSize = false, Width = 192, Height = 44, TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0), FlatStyle = FlatStyle.Flat, UseMnemonic = false, AccessibleDescription = icon, TabStop = true };
            button.FlatAppearance.BorderSize = 0;
            button.Click += (_, _) => SelectWidget(key);
            pages.TabPages.Add(page); navigation.Controls.Add(button); entries.Add((key, icon, page, button));
            return page;
        }
        internal void LoadSettings(WidgetSettings value) => LoadControls(value, true);

        private void LoadControls(WidgetSettings value, bool replaceBaseline)
        {
            loadingControls = true;
            try
            {
                if (replaceBaseline) initialWidgetSettings = value.Clone();
                calendarSources = new(value.CalendarSources);
                LoadWebControls(value.Web);
                notesEnabled.Checked = value.NotesEnabled;
                notesLocked.Checked = value.NotesLocked;
                notesMaximumHeight.Value = Math.Clamp(value.NotesMaximumHeight, (int)notesMaximumHeight.Minimum, (int)notesMaximumHeight.Maximum);
                wallpaperInfoFontSize.Value = Math.Clamp(value.WallpaperInfoFontSize, (int)wallpaperInfoFontSize.Minimum, (int)wallpaperInfoFontSize.Maximum);
                clockEnabledCheckBox.Checked = value.ClockEnabled;
                clockLockedCheckBox.Checked = value.ClockLocked;
                clockSizeNumeric.Value = Math.Clamp(value.ClockSize, (int)clockSizeNumeric.Minimum, (int)clockSizeNumeric.Maximum);
                clockSecondsCheckBox.Checked = value.ClockShowSeconds;
                wallpaperInfoEnabled.Checked = value.WallpaperInfoEnabled;
                wallpaperInfoLocked.Checked = value.WallpaperInfoLocked;
                wallpaperInfoAdvanced.Checked = value.WallpaperInfoShowAdvanced;
                wallpaperInfoExtension.Checked = value.WallpaperInfoShowExtension;
                nextWidgetEnabledCheckBox.Checked = value.NextEnabled;
                nextWidgetLockedCheckBox.Checked = value.NextLocked;
                systemWidgetEnabledCheckBox.Checked = value.SystemEnabled;
                systemWidgetLockedCheckBox.Checked = value.SystemLocked;
                systemShowCpuCheckBox.Checked = value.SystemShowCpu;
                systemShowRamCheckBox.Checked = value.SystemShowRam;
                systemShowGpuCheckBox.Checked = value.SystemShowGpu;
                systemShowVramCheckBox.Checked = value.SystemShowVram;
                systemShowNetworkCheckBox.Checked = value.SystemShowNetwork;
                systemShowDrivesCheckBox.Checked = value.SystemShowDrives;
                weatherWidgetEnabledCheckBox.Checked = value.WeatherEnabled;
                weatherWidgetLockedCheckBox.Checked = value.WeatherLocked;
                weatherShowForecastCheckBox.Checked = value.WeatherShowForecast;
                calendarWidgetEnabledCheckBox.Checked = value.CalendarEnabled;
                calendarWidgetLockedCheckBox.Checked = value.CalendarLocked;
                calendarMaximumHeightNumeric.Value = Math.Clamp(value.CalendarMaximumHeight, (int)calendarMaximumHeightNumeric.Minimum, (int)calendarMaximumHeightNumeric.Maximum);
                calendarShowLocationCheckBox.Checked = value.CalendarShowLocation;
                wallpaperInfoSuffix.Text = value.WallpaperInfoHiddenSuffix;
                weatherLocationTextBox.Text = value.WeatherLocationName;
                RefreshClockStyleChoices(value.ClockStyle);
                RefreshNextStyleChoices(value.NextStyle); RefreshSystemStyleChoices(value.SystemStyle);
                RefreshWeatherStyleChoices(value.WeatherStyle); RefreshCalendarStyleChoices(value.CalendarStyle);
                RefreshWidgetStyleChoices(notesStyle, value.NotesStyle); RefreshWidgetStyleChoices(wallpaperInfoStyle, value.WallpaperInfoStyle);
                systemWidgetRefreshComboBox.SelectedIndex = value.SystemRefreshSeconds switch { 1 => 0, 5 => 2, _ => 1 };
                weatherWidgetRefreshComboBox.SelectedIndex = value.WeatherRefreshMinutes switch { 15 => 0, 60 => 2, 120 => 3, _ => 1 };
                calendarRefreshComboBox.SelectedIndex = value.CalendarRefreshMinutes switch { 15 => 0, 60 => 2, 120 => 3, _ => 1 };
                calendarMaxEntriesComboBox.SelectedIndex = value.CalendarMaxEntries switch { 3 => 0, 5 => 1, _ => 2 };
            }
            finally { loadingControls = false; }
        }
        internal void ResetDefaults()
        {
            LoadControls(new WidgetSettings { ClockLanguageCode = previewLanguageCode }, false);
            NotifyWidgetPreviewChanged();
        }
        internal void ApplyPresentation(bool dark, string language)
        {
            darkMode = dark; previewLanguageCode = language;
            loadingControls = true;
            try
            {
                Localize(Controls);
                RefreshWidgetStyleChoices(webStyle, (SystemWidgetStyle)Math.Max(0, webStyle.SelectedIndex));
                RefreshClockStyleChoices(GetSelectedClockStyle()); RefreshSystemStyleChoices(GetSelectedSystemStyle());
                RefreshWeatherStyleChoices(GetSelectedWeatherStyle()); RefreshNextStyleChoices(GetSelectedNextStyle());
                RefreshCalendarStyleChoices(GetSelectedCalendarStyle());
                RefreshWidgetStyleChoices(notesStyle, (SystemWidgetStyle)Math.Max(0, notesStyle.SelectedIndex));
                RefreshWidgetStyleChoices(wallpaperInfoStyle, GetWallpaperInfoStyle());
                wallpaperInfoSuffix.AccessibleName = Localization.Get("WallpaperInfoSuffix", language);
                var ordered = entries.OrderBy(e => Localization.Get(e.Key, language), StringComparer.Create(CultureInfo.GetCultureInfo(language), true)).ToArray();
                navigation.SuspendLayout();
                for (int i = 0; i < ordered.Length; i++)
                {
                    var entry = ordered[i];
                    entry.Button.Text = $"{entry.Icon}   {Localization.Get(entry.Key, language)}";
                    entry.Button.TabIndex = i;
                    navigation.Controls.SetChildIndex(entry.Button, i);
                    entry.Button.AccessibleName = Localization.Get(entry.Key, language);
                }
                navigation.ResumeLayout();
                if (pages.SelectedTab == null) pages.SelectedTab = ordered[0].Page;
                BackColor = AppTheme.WindowBackground(dark); ForeColor = AppTheme.TextPrimary(dark);
                SettingsControlTheme.Apply(Controls, dark, BackColor, ForeColor, AppTheme.InputBackground(dark), AppTheme.ControlBackground(dark));
                LocalizeWebControls();
                navigation.BackColor = AppTheme.SidebarBackground(dark);
                UpdateSelection();
            }
            finally { loadingControls = false; }
        }
        private void Localize(Control.ControlCollection controls)
        {
            foreach (Control control in controls)
            {
                if (control.Tag is string key) control.Text = Localization.Get(key, previewLanguageCode);
                Localize(control.Controls);
            }
        }
        private void UpdateSelection()
        {
            foreach (var entry in entries)
                entry.Button.BackColor = pages.SelectedTab == entry.Page ? AppTheme.SelectionBackground(darkMode) : AppTheme.SidebarBackground(darkMode);
        }
        private bool ResolvePreviewDarkMode() => darkMode;
        private Font CreateOwnedFont(string family, float size, FontStyle style = FontStyle.Regular)
        {
            Font font = new(family, size, style); ownedFonts.Add(font); return font;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeWebControls();
                navigation.Dispose(); pages.Dispose();
                calendarMaximumHeightNumeric?.Dispose(); notesManageButton.Dispose();
                notesEnabled.Dispose(); notesLocked.Dispose(); notesMaximumHeight.Dispose(); notesStyle.Dispose(); wallpaperInfoFontSize.Dispose();
                foreach (Font font in ownedFonts) font.Dispose(); ownedFonts.Clear();
            }
            base.Dispose(disposing);
        }
    }
}
