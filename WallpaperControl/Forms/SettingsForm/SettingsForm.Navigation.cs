using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Settings dialog Navigation members. See README.md in this directory for the code map.
    internal sealed partial class SettingsForm
    {
        /// <summary>
        /// Creates a tab page with a localized caption and its resource key.
        /// </summary>
        /// <param name="resourceKey">The localization resource key used for the caption.</param>
        /// <returns>The newly configured settings tab page.</returns>
        private TabPage CreateSettingsPage(string resourceKey)
        {
            return new TabPage
            {
                Text = Localization.Get(resourceKey, previewLanguageCode),
                Tag = resourceKey
            };
        }

        /// <summary>
        /// Creates a sidebar action linked to a settings page and tracks its selection state.
        /// </summary>
        /// <param name="navigationPanel">The sidebar panel that owns the navigation actions.</param>
        /// <param name="tabControl">The tab control containing the settings pages.</param>
        /// <param name="page">The settings page associated with the navigation action.</param>
        /// <param name="icon">The glyph displayed before the navigation label.</param>
        /// <param name="resourceKey">The localization resource key used for the caption.</param>
        /// <param name="y">The vertical coordinate.</param>
        /// <param name="extraLeftPadding">The additional indentation used for a nested navigation action.</param>
        /// <returns>The navigation button added to the settings sidebar.</returns>
        private Button AddSettingsNavigationButton(
            Panel navigationPanel,
            TabControl tabControl,
            TabPage page,
            string icon,
            string resourceKey,
            int y,
            int extraLeftPadding = 0)
        {
            Button button = new Button
            {
                Text = $"{icon}   {Localization.Get(resourceKey, previewLanguageCode)}",
                Tag = resourceKey,
                Location = new Point(14 + extraLeftPadding, y),
                Size = new Size(192 - extraLeftPadding, 42),
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0),
                Cursor = Cursors.Hand,
                TabStop = false
            };

            button.FlatAppearance.BorderSize = 0;
            button.Click += (_, _) => tabControl.SelectedTab = page;
            button.AccessibleDescription = icon;
            settingsNavigationButtons.Add(button);
            settingsNavigationPages[button] = page;
            navigationPanel.Controls.Add(button);
            return button;
        }

        /// <summary>
        /// Expands or collapses widget navigation and repositions the following sidebar entries.
        /// </summary>
        private void UpdateWidgetsNavigationLayout()
        {
            if (settingsWidgetsToggleButton == null)
                return;

            settingsWidgetsToggleButton.Text =
                $"{(settingsWidgetsExpanded ? "▾" : "▸")}   {Localization.Get("SettingsTabWidgets", previewLanguageCode)}";

            if (settingsClockNavigationButton != null)
                settingsClockNavigationButton.Visible = settingsWidgetsExpanded;

            if (settingsNextNavigationButton != null)
                settingsNextNavigationButton.Visible = settingsWidgetsExpanded;

            if (settingsSystemNavigationButton != null)
                settingsSystemNavigationButton.Visible = settingsWidgetsExpanded;

            if (settingsWeatherNavigationButton != null)
                settingsWeatherNavigationButton.Visible = settingsWidgetsExpanded;

            if (settingsCalendarNavigationButton != null)
                settingsCalendarNavigationButton.Visible = settingsWidgetsExpanded;

            int appearanceY = settingsWidgetsExpanded ? 490 : 252;
            int languageY = settingsWidgetsExpanded ? 536 : 298;

            if (settingsAppearanceNavigationButton != null)
                settingsAppearanceNavigationButton.Location = new Point(14, appearanceY);

            if (settingsLanguageNavigationButton != null)
                settingsLanguageNavigationButton.Location = new Point(14, languageY);

            UpdateSettingsNavigationSelection();
        }

        /// <summary>
        /// Highlights the sidebar action associated with the selected settings page.
        /// </summary>
        private void UpdateSettingsNavigationSelection()
        {
            if (settingsTabControl == null)
                return;

            bool darkMode = ResolvePreviewDarkMode();
            Color normal = AppTheme.SidebarBackground(darkMode);
            Color selected = AppTheme.SelectionBackground(darkMode);
            Color foreground = darkMode ? AppTheme.DarkTextPrimary : Color.FromArgb(35, 35, 35);

            if (settingsWidgetsToggleButton != null)
            {
                settingsWidgetsToggleButton.BackColor = normal;
                settingsWidgetsToggleButton.ForeColor = foreground;
                settingsWidgetsToggleButton.FlatAppearance.MouseOverBackColor =
                    darkMode ? AppTheme.DarkControlHover : Color.FromArgb(225, 232, 239);
                settingsWidgetsToggleButton.FlatAppearance.MouseDownBackColor = selected;
            }

            for (int i = 0; i < settingsNavigationButtons.Count; i++)
            {
                Button button = settingsNavigationButtons[i];
                button.BackColor = settingsNavigationPages.TryGetValue(button, out TabPage? page) && page == settingsTabControl.SelectedTab ? selected : normal;
                button.ForeColor = foreground;
            }
        }

        /// <summary>
        /// Refreshes sidebar captions for the preview language.
        /// </summary>
        private void UpdateSettingsNavigationText()
        {
            foreach (Button button in settingsNavigationButtons)
            {
                if (button.Tag is not string resourceKey)
                    continue;

                string icon = button.AccessibleDescription ?? "";
                button.Text = $"{icon}   {Localization.Get(resourceKey, previewLanguageCode)}";
            }

            UpdateWidgetsNavigationLayout();
        }
    }
}
