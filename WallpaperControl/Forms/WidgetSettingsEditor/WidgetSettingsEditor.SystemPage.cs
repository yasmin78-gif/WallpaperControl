using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Widget editor SystemPage members.
    internal sealed partial class WidgetSettingsEditor
    {
        private readonly CheckedListBox systemDriveList = new() { CheckOnClick = true, IntegralHeight = false, Location = new(18,350), Size = new(260,110) };
        private readonly NumericUpDown systemDriveWarning = new() { Minimum = 1, Maximum = 50, Value = 10, Location = new(310,389), Width = 90 };
        private HashSet<string> selectedSystemDrives = [];
        private void InitializeSystemDrives(GroupBox options)
        {
            options.Height = 560;
            var warning = new Label { Tag = "SettingsSystemDriveWarning", Location = new(310,350), Size = new(270,35) };
            var refresh = new Button { Tag = "SettingsSystemDriveRefresh", Location = new(310,430), AutoSize = true };
            options.Controls.Add(new Label { Tag = "SettingsSystemDriveSelection", Location = new(18,326), AutoSize = true });
            options.Controls.Add(systemDriveList); options.Controls.Add(systemDriveWarning); options.Controls.Add(warning); options.Controls.Add(refresh);
            LoadSystemDrives(initialWidgetSettings);
            systemDriveList.ItemCheck += (_,e) =>
            {
                if (loadingControls) return;
                string name = (string)systemDriveList.Items[e.Index];
                if (e.NewValue == CheckState.Checked) selectedSystemDrives.Add(name); else selectedSystemDrives.Remove(name);
                NotifyWidgetPreviewChanged();
            };
            systemDriveWarning.ValueChanged += (_,_) => NotifyWidgetPreviewChanged();
            systemShowDrivesCheckBox.CheckedChanged += (_,_) => UpdateDriveControls();
            refresh.Click += (_,_) => RefreshSystemDriveList();
        }
        private void LoadSystemDrives(WidgetSettings value)
        {
            selectedSystemDrives = SystemDriveSelection.Normalize(value.SystemSelectedDrives).ToHashSet(StringComparer.OrdinalIgnoreCase);
            systemDriveWarning.Value = Math.Clamp(value.SystemDriveWarningPercent,1,50); RefreshSystemDriveList(); UpdateDriveControls();
        }
        private void UpdateDriveControls() { systemDriveList.Enabled = systemShowDrivesCheckBox.Checked; systemDriveWarning.Enabled = systemShowDrivesCheckBox.Checked; }
        private void RefreshSystemDriveList()
        {
            bool previous = loadingControls; loadingControls = true;
            try
            {
                systemDriveList.Items.Clear();
                foreach (string name in SystemDriveSelection.Available().Concat(selectedSystemDrives).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
                    systemDriveList.Items.Add(name,selectedSystemDrives.Contains(name));
            }
            finally { loadingControls = previous; }
        }
        /// <summary>
        /// Rebuilds localized system-widget styles while retaining the selected style.
        /// </summary>
        /// <param name="selectedStyle">The widget style to keep selected.</param>
        private void RefreshSystemStyleChoices(SystemWidgetStyle selectedStyle)
        {
            RefreshWidgetStyleChoices(systemWidgetStyleComboBox, selectedStyle);
        }

        /// <summary>
        /// Creates a localized checkbox for an optional system-widget metric.
        /// </summary>
        /// <param name="resourceKey">The localization resource key used for the caption.</param>
        /// <param name="x">The horizontal coordinate.</param>
        /// <param name="y">The vertical coordinate.</param>
        /// <param name="isChecked">The initial checkbox state.</param>
        /// <returns>The checkbox for the requested system-monitor module.</returns>
        private CheckBox CreateSystemModuleCheckBox(string resourceKey, int x, int y, bool isChecked)
        {
            return new CheckBox
            {
                Text = Localization.Get(resourceKey, previewLanguageCode),
                Tag = resourceKey,
                Location = new Point(x, y),
                AutoSize = true,
                Checked = isChecked
            };
        }

        /// <summary>
        /// Returns the selected system-widget style with a fallback for an empty selector.
        /// </summary>
        /// <returns>The selected system widget style, with the default used for an invalid selection.</returns>
        private SystemWidgetStyle GetSelectedSystemStyle()
        {
            int index = systemWidgetStyleComboBox?.SelectedIndex ?? (int)SystemWidgetStyle.Glow;
            return Enum.IsDefined(typeof(SystemWidgetStyle), index)
                ? (SystemWidgetStyle)index
                : SystemWidgetStyle.Glow;
        }

        /// <summary>
        /// Maps the system refresh selector to an interval in seconds.
        /// </summary>
        /// <returns>The selected system-monitor refresh interval in seconds.</returns>
        private int GetSystemRefreshSeconds() =>
            systemWidgetRefreshComboBox.SelectedIndex switch
            {
                0 => 1,
                2 => 5,
                _ => 2
            };
    }
}
