using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Main-window localization responsibilities; see README.md in this directory for the code map.
    public partial class MainForm
    {
        /// <summary>
        /// Refreshes localized action hints, including configured hotkey labels.
        /// </summary>
        private void UpdateToolTips()
        {
            toolTip.SetToolTip(
                settingsButton,
                Localization.Get("ToolTipSettings"));

            toolTip.SetToolTip(
                aboutButton,
                Localization.Get("ToolTipAbout"));

            toolTip.SetToolTip(
                nextWallpaperButton,
                Localization.Get("ToolTipNext"));

            toolTip.SetToolTip(
                rejectButton,
                Localization.Get("ToolTipReject"));

            toolTip.SetToolTip(
                undoRejectButton,
                Localization.Get("ToolTipUndo"));

            toolTip.SetToolTip(
                historyButton,
                Localization.Get("ToolTipHistory"));

            toolTip.SetToolTip(
                statisticsButton,
                Localization.Get("ToolTipStatistics"));
        }

        /// <summary>
        /// Rebuilds localized labels and option lists while preserving the current selections.
        /// </summary>
        private void ApplyLocalization()
        {
            LocalizeWallpaperGroups();
            UpdateWidgetPresentation();
            uint selectedInterval = 300000;

            if (intervalComboBox.SelectedItem
                is DisplayOption<uint> selectedIntervalOption)
            {
                selectedInterval = selectedIntervalOption.Value;
            }

            DesktopWallpaperPosition selectedPosition =
                lastWallpaperPosition ??
                DesktopWallpaperPosition.Fill;

            if (positionComboBox.SelectedItem
                is DisplayOption<DesktopWallpaperPosition> selectedPositionOption)
            {
                selectedPosition = selectedPositionOption.Value;
            }

            bool previousLoading = loading;
            loading = true;

            try
            {
                intervals.Clear();
                intervals.Add(new(60000, Localization.Get("Interval1Minute")));
                intervals.Add(new(120000, Localization.Get("Interval2Minutes")));
                intervals.Add(new(180000, Localization.Get("Interval3Minutes")));
                intervals.Add(new(300000, Localization.Get("Interval5Minutes")));
                intervals.Add(new(600000, Localization.Get("Interval10Minutes")));
                intervals.Add(new(900000, Localization.Get("Interval15Minutes")));
                intervals.Add(new(1800000, Localization.Get("Interval30Minutes")));
                intervals.Add(new(3600000, Localization.Get("Interval1Hour")));
                intervals.Add(new(21600000, Localization.Get("Interval6Hours")));
                intervals.Add(new(86400000, Localization.Get("Interval1Day")));

                intervalComboBox.Items.Clear();
                foreach (var item in intervals)
                    intervalComboBox.Items.Add(item);

                intervalComboBox.SelectedItem =
                    intervals.FirstOrDefault(item => item.Value == selectedInterval);

                positions.Clear();
                positions.Add(new(DesktopWallpaperPosition.Fill, Localization.Get("PositionFill")));
                positions.Add(new(DesktopWallpaperPosition.Fit, Localization.Get("PositionFit")));
                positions.Add(new(DesktopWallpaperPosition.Stretch, Localization.Get("PositionStretch")));
                positions.Add(new(DesktopWallpaperPosition.Tile, Localization.Get("PositionTile")));
                positions.Add(new(DesktopWallpaperPosition.Center, Localization.Get("PositionCenter")));
                positions.Add(new(DesktopWallpaperPosition.Span, Localization.Get("PositionSpan")));

                positionComboBox.Items.Clear();
                foreach (var item in positions)
                    positionComboBox.Items.Add(item);

                positionComboBox.SelectedItem =
                    positions.FirstOrDefault(item => item.Value == selectedPosition);

                int transitionIndex =
                    selectedTransitionKind switch
                    {
                        WallpaperTransitionKind.DesktopSlide => 1,
                        WallpaperTransitionKind.DesktopFade => 2,
                        WallpaperTransitionKind.DesktopZoomFade => 3,
                        WallpaperTransitionKind.DesktopSplit => 4,
                        WallpaperTransitionKind.DesktopCurtain => 5,
                        WallpaperTransitionKind.DesktopRandom => 6,
                        _ => 0
                    };

                transitionComboBox.BeginUpdate();

                try
                {
                    transitionComboBox.Items.Clear();
                    transitionComboBox.Items.AddRange(
                        new object[]
                        {
                            Localization.Get("TransitionWipe"),
                            Localization.Get("TransitionSlide"),
                            Localization.Get("TransitionFade"),
                            Localization.Get("TransitionZoomFade"),
                            Localization.Get("TransitionSplit"),
                            Localization.Get("TransitionCurtain"),
                            Localization.Get("TransitionRandom")
                        });

                    transitionComboBox.SelectedIndex =
                        Math.Clamp(transitionIndex, 0, 6);
                }
                finally
                {
                    transitionComboBox.EndUpdate();
                }

                PopulateDirectionOptions();
            }
            finally
            {
                loading = previousLoading;
            }

            activateButton.Text =
                Localization.Get("ActivateSlideshow");

            currentHeading.Text = Localization.Get("MainCurrentWallpaperHeading");
            directionHeading.Text = Localization.Get("MainDirectionHeading");
            folderLabel.Text = Localization.Get("WallpaperFolder");

            intervalLabel.Text =
                Localization.Get("WallpaperInterval");

            shuffleCheckBox.Text =
                Localization.Get("Shuffle");

            positionLabel.Text =
                Localization.Get("WallpaperPosition");

            transitionLabel.Text =
                Localization.Get("Transition");

            transitionDurationLabel.Text =
                Localization.Get("TransitionDuration");

            pinButton.Text =
                Localization.Get("PinImage");

            nextWallpaperButton.Text =
                Localization.Get("NextWallpaper");

            explorerButton.Text =
                Localization.Get("ShowInExplorer");

            rejectButton.Text =
                Localization.Get("RejectWallpaper");

            undoRejectButton.Text =
                Localization.Get("Undo");

            statisticsButton.Text =
                Localization.Get("Statistics");

            rejectMenu.Items[0].Text =
                Localization.Get("OpenRejectedFolder");

            trayMenu.Items[0].Text =
                Localization.Get("OpenWallpaperControl");

            trayMenu.Items[2].Text =
                Localization.Get("NextWallpaper");

            trayMenu.Items[4].Text =
                Localization.Get("PinImage");

            trayMenu.Items[5].Text =
                Localization.Get("OpenRejectedFolder");

            trayMenu.Items[7].Text =
                Localization.Get("Exit");

            UpdateWallpaperCount();

            lastDisplayedWallpaperPath = null;
            UpdateCurrentWallpaperDisplay();

            UpdateHistoryButtonText();
            UpdateTrayPauseText();
            RefreshWindowsIntervalText();
            CheckSlideshowStatus();
            UpdateToolTips();

            historyMenu.Close();
            rejectMenu.Close();
            HideWallpaperPreview();
        }

        /// <summary>
        /// Reloads the interval description after a language change.
        /// </summary>
        private void RefreshWindowsIntervalText()
        {
            IDesktopWallpaper? wallpaper = null;

            try
            {
                wallpaper =
                    (IDesktopWallpaper)
                    new DesktopWallpaper();

                wallpaper.GetSlideshowOptions(
                    out _,
                    out uint interval);

                UpdateWindowsIntervalLabel(
                    interval);
            }
            catch
            {
                windowsIntervalLabel.Text =
                    Localization.Get(
                        "CurrentWindowsValueUnavailable");
            }
            finally
            {
                ReleaseComObject(wallpaper);
            }
        }
    }
}
