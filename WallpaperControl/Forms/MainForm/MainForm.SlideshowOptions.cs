using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Main-window slideshowoptions responsibilities; see README.md in this directory for the code map.
    public partial class MainForm
    {
        /// <summary>
        /// Loads the native interval and shuffle options, falling back to the registry when needed.
        /// </summary>
        private void LoadSlideshowOptions()
        {
            IDesktopWallpaper? wallpaper = null;

            try
            {
                wallpaper =
                    (IDesktopWallpaper)
                    new DesktopWallpaper();

                wallpaper.GetSlideshowOptions(
                    out DesktopSlideshowOptions options,
                    out uint interval);

                UpdateWindowsIntervalLabel(interval);

                bool found = false;

                foreach (var item in intervals)
                {
                    if (item.Value == interval)
                    {
                        intervalComboBox.SelectedItem =
                            item;

                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    LoadOptionsFromRegistry();
                }

                shuffleCheckBox.Checked =
                    (options &
                     DesktopSlideshowOptions
                         .ShuffleImages) != 0;
            }
            catch
            {
                windowsIntervalLabel.Text =
                    Localization.Get("CurrentWindowsValueUnavailable");

                LoadOptionsFromRegistry();
            }
            finally
            {
                ReleaseComObject(wallpaper);
            }

            if (intervalComboBox.SelectedIndex < 0)
            {
                intervalComboBox.SelectedItem =
                    intervals.First(item => item.Value == 300000);
            }
        }

        /// <summary>
        /// Reads Windows slideshow preferences when the desktop COM API is unavailable.
        /// </summary>
        private void LoadOptionsFromRegistry()
        {
            try
            {
                using RegistryKey? key =
                    Registry.CurrentUser.OpenSubKey(
                        WindowsSlideshowRegistryPath);

                if (key == null)
                    return;

                object? intervalValue =
                    key.GetValue("Interval");

                if (intervalValue != null)
                {
                    uint currentInterval =
                        Convert.ToUInt32(
                            intervalValue);

                    foreach (var item in intervals)
                    {
                        if (item.Value ==
                            currentInterval)
                        {
                            intervalComboBox.SelectedItem =
                                item;

                            break;
                        }
                    }
                }

                object? shuffleValue =
                    key.GetValue("Shuffle");

                if (shuffleValue != null)
                {
                    shuffleCheckBox.Checked =
                        Convert.ToInt32(
                            shuffleValue) != 0;
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Selects the wallpaper layout currently reported by Windows.
        /// </summary>
        private void LoadWallpaperPosition()
        {
            IDesktopWallpaper? wallpaper = null;

            try
            {
                wallpaper =
                    (IDesktopWallpaper)
                    new DesktopWallpaper();

                DesktopWallpaperPosition current =
                    wallpaper.GetPosition();

                lastWallpaperPosition = current;

                foreach (var item in positions)
                {
                    if (item.Value == current)
                    {
                        positionComboBox.SelectedItem =
                            item;
                        return;
                    }
                }
            }
            catch
            {
                positionComboBox.SelectedIndex = -1;
            }
            finally
            {
                ReleaseComObject(wallpaper);
            }
        }

        /// <summary>
        /// Refreshes the layout selector without treating external changes as user edits.
        /// </summary>
        private void UpdateWallpaperPositionDisplay()
        {
            IDesktopWallpaper? wallpaper = null;

            try
            {
                wallpaper =
                    (IDesktopWallpaper)
                    new DesktopWallpaper();

                DesktopWallpaperPosition current =
                    wallpaper.GetPosition();

                if (lastWallpaperPosition == current)
                    return;

                lastWallpaperPosition = current;

                foreach (var item in positions)
                {
                    if (item.Value != current)
                        continue;

                    if (!Equals(
                        positionComboBox.SelectedItem,
                        item))
                    {
                        loading = true;

                        try
                        {
                            positionComboBox.SelectedItem =
                                item;
                        }
                        finally
                        {
                            loading = false;
                        }
                    }

                    return;
                }
            }
            catch
            {
                // A failed background query must not interrupt the interface.
            }
            finally
            {
                ReleaseComObject(wallpaper);
            }
        }

        /// <summary>
        /// Applies the selected image layout to Windows and the persistent desktop renderer.
        /// </summary>
        private void PositionComboBox_SelectedIndexChanged(
            object? sender,
            EventArgs e)
        {
            if (loading)
                return;

            if (positionComboBox.SelectedItem
                is not DisplayOption<DesktopWallpaperPosition> selected)
            {
                return;
            }

            DesktopWallpaperPosition position = selected.Value;

            IDesktopWallpaper? wallpaper = null;

            try
            {
                wallpaper =
                    (IDesktopWallpaper)
                    new DesktopWallpaper();

                wallpaper.SetPosition(position);

                DesktopWallpaperPosition actual =
                    wallpaper.GetPosition();

                lastWallpaperPosition = actual;
                PersistentDesktopTransitionManager.SetWallpaperPosition(actual);

                foreach (var item in positions)
                {
                    if (item.Value == actual)
                    {
                        if (!Equals(
                            positionComboBox.SelectedItem,
                            item))
                        {
                            loading = true;
                            positionComboBox.SelectedItem =
                                item;
                            loading = false;
                        }

                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Localization.Get("MsgPositionFailed") +
                    ex.Message,
                    "Wallpaper Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                loading = true;
                LoadWallpaperPosition();
                loading = false;
            }
            finally
            {
                ReleaseComObject(wallpaper);
            }
        }

        /// <summary>
        /// Applies a user interval change when loading and pause guards allow it.
        /// </summary>
        private void IntervalComboBox_SelectedIndexChanged(
            object? sender,
            EventArgs e)
        {
            if (loading)
                return;

            if (slideshowPaused || fullscreenPolicy.IsPaused)
                return;

            ApplySlideshowOptions();

            if (customSlideshowEngineActive)
            {
                RecalculateCustomSlideshowSchedule();
            }
        }

        /// <summary>
        /// Applies a user shuffle change when loading and pause guards allow it.
        /// </summary>
        private void ShuffleCheckBox_CheckedChanged(
            object? sender,
            EventArgs e)
        {
            if (loading)
                return;

            if (slideshowPaused || fullscreenPolicy.IsPaused)
                return;

            ApplySlideshowOptions();
        }

        /// <summary>
        /// Persists interval and shuffle preferences and refreshes the application schedule.
        /// </summary>
        private void ApplySlideshowOptions()
        {
            if (intervalComboBox.SelectedItem
                is not DisplayOption<uint> selected)
            {
                return;
            }

            uint milliseconds = selected.Value;

            IDesktopWallpaper? wallpaper = null;

            try
            {
                wallpaper =
                    (IDesktopWallpaper)
                    new DesktopWallpaper();

                DesktopSlideshowOptions options =
                    shuffleCheckBox.Checked
                    ? DesktopSlideshowOptions.ShuffleImages
                    : DesktopSlideshowOptions.None;

                wallpaper.SetSlideshowOptions(
                    options,
                    milliseconds);

                wallpaper.GetSlideshowOptions(
                    out _,
                    out uint actualInterval);

                UpdateWindowsIntervalLabel(
                    actualInterval);

                using RegistryKey key =
                    Registry.CurrentUser.CreateSubKey(
                        WindowsSlideshowRegistryPath);

                key.SetValue(
                    "Interval",
                    milliseconds,
                    RegistryValueKind.DWord);

                key.SetValue(
                    "Shuffle",
                    shuffleCheckBox.Checked ? 1 : 0,
                    RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Localization.Get("MsgSlideshowSettingsFailed") +
                    ex.Message,
                    "Wallpaper Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                ReleaseComObject(wallpaper);
            }
        }

        /// <summary>
        /// Displays the current Windows slideshow interval in the active language.
        /// </summary>
        private void UpdateWindowsIntervalLabel(
            uint milliseconds)
        {
            double seconds = milliseconds / 1000.0;
            double minutes = seconds / 60.0;

            string minuteText =
                minutes.ToString(
                    "0.##",
                    Localization.CurrentCulture);

            string secondText =
                seconds.ToString(
                    "0.##",
                    Localization.CurrentCulture);

            windowsIntervalLabel.Text =
                string.Format(
                    Localization.Get("CurrentWindowsValue"),
                    minuteText,
                    secondText);
        }
    }
}
