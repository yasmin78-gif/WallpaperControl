using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Main-window transitions responsibilities; see README.md in this directory for the code map.
    public partial class MainForm
    {
        /// <summary>
        /// Loads the selected effect, direction, zoom mode, and transition duration.
        /// </summary>
        private void LoadTransitionSettings()
        {
            var settings = appSettings.LoadTransitionSettings();
            selectedTransitionDurationMilliseconds = settings.DurationMilliseconds;
            transitionComboBox.SelectedIndex = settings.KindIndex;
            selectedTransitionKind = settings.KindIndex switch
            {
                1 => WallpaperTransitionKind.DesktopSlide,
                2 => WallpaperTransitionKind.DesktopFade,
                3 => WallpaperTransitionKind.DesktopZoomFade,
                4 => WallpaperTransitionKind.DesktopSplit,
                5 => WallpaperTransitionKind.DesktopCurtain,
                6 => WallpaperTransitionKind.DesktopRandom,
                _ => WallpaperTransitionKind.DesktopWipe
            };
            // Restore values before populating the effect-specific choices.
            selectedTransitionDirection = DirectionFromIndex(settings.DirectionIndex);
            selectedZoomMode = settings.ZoomMode == 1 ? WallpaperZoomMode.Out : WallpaperZoomMode.In;
            UpdateTransitionDirectionState();
            transitionDurationComboBox.SelectedIndex = Array.IndexOf(
                new[] { 500, 1000, 1500, 2000, 3000, 5000 }, settings.DurationMilliseconds);
        }

        /// <summary>
        /// Updates and persists the selected transition effect and its dependent controls.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private void TransitionComboBox_SelectedIndexChanged(
            object? sender,
            EventArgs e)
        {
            int index =
                transitionComboBox.SelectedIndex;

            selectedTransitionKind =
                index switch
                {
                    1 => WallpaperTransitionKind.DesktopSlide,
                    2 => WallpaperTransitionKind.DesktopFade,
                    3 => WallpaperTransitionKind.DesktopZoomFade,
                    4 => WallpaperTransitionKind.DesktopSplit,
                    5 => WallpaperTransitionKind.DesktopCurtain,
                    6 => WallpaperTransitionKind.DesktopRandom,
                    _ => WallpaperTransitionKind.DesktopWipe
                };

            UpdateTransitionDirectionState();

            if (index < 0)
            {
                return;
            }

            appSettings.SaveTransitionKind(selectedTransitionKind);
        }

        /// <summary>
        /// Maps the direction selector index to its transition direction.
        /// </summary>
        /// <param name="index">The transition-direction selection index.</param>
        /// <returns>The transition direction corresponding to the selection index.</returns>
        private WallpaperTransitionDirection DirectionFromIndex(int index)
        {
            return index switch
            {
                1 => WallpaperTransitionDirection.Right,
                2 => WallpaperTransitionDirection.Up,
                3 => WallpaperTransitionDirection.Down,
                4 => WallpaperTransitionDirection.Random,
                _ => WallpaperTransitionDirection.Left
            };
        }

        /// <summary>
        /// Builds direction or zoom choices appropriate to the selected transition.
        /// </summary>
        private void PopulateDirectionOptions()
        {
            transitionDirectionComboBox.BeginUpdate();

            try
            {
                transitionDirectionComboBox.Items.Clear();

                if (selectedTransitionKind == WallpaperTransitionKind.DesktopWipe ||
                    selectedTransitionKind == WallpaperTransitionKind.DesktopSlide)
                {
                    transitionDirectionComboBox.Items.AddRange(
                        new object[]
                        {
                            Localization.Get("DirectionLeft"),
                            Localization.Get("DirectionRight"),
                            Localization.Get("DirectionUp"),
                            Localization.Get("DirectionDown"),
                            Localization.Get("DirectionRandom")
                        });

                    transitionDirectionComboBox.Enabled = true;

                    int directionIndex =
                        selectedTransitionDirection switch
                        {
                            WallpaperTransitionDirection.Right => 1,
                            WallpaperTransitionDirection.Up => 2,
                            WallpaperTransitionDirection.Down => 3,
                            WallpaperTransitionDirection.Random => 4,
                            _ => 0
                        };

                    transitionDirectionComboBox.SelectedIndex =
                        Math.Clamp(directionIndex, 0, 4);
                }
                else if (selectedTransitionKind == WallpaperTransitionKind.DesktopZoomFade)
                {
                    transitionDirectionComboBox.Items.AddRange(
                        new object[]
                        {
                            Localization.Get("ZoomIn"),
                            Localization.Get("ZoomOut")
                        });

                    transitionDirectionComboBox.Enabled = true;
                    transitionDirectionComboBox.SelectedIndex =
                        selectedZoomMode == WallpaperZoomMode.Out ? 1 : 0;
                }
                else
                {
                    transitionDirectionComboBox.Items.Add(
                        Localization.Get("NotApplicable"));

                    transitionDirectionComboBox.SelectedIndex = 0;
                    transitionDirectionComboBox.Enabled = false;
                }
            }
            finally
            {
                transitionDirectionComboBox.EndUpdate();
            }
        }

        /// <summary>
        /// Refreshes direction choices after the transition effect changes.
        /// </summary>
        private void UpdateTransitionDirectionState()
        {
            PopulateDirectionOptions();
        }

        /// <summary>
        /// Persists the selected transition direction or zoom mode.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private void TransitionDirectionComboBox_SelectedIndexChanged(
            object? sender,
            EventArgs e)
        {
            int index = transitionDirectionComboBox.SelectedIndex;

            if (index < 0)
            {
                return;
            }

            if (selectedTransitionKind == WallpaperTransitionKind.DesktopZoomFade)
            {
                selectedZoomMode =
                    index == 1
                    ? WallpaperZoomMode.Out
                    : WallpaperZoomMode.In;

                appSettings.SaveTransitionZoomMode(selectedZoomMode == WallpaperZoomMode.Out ? 1 : 0);

                return;
            }

            if (selectedTransitionKind != WallpaperTransitionKind.DesktopWipe &&
                selectedTransitionKind != WallpaperTransitionKind.DesktopSlide)
            {
                return;
            }

            selectedTransitionDirection =
                DirectionFromIndex(index);

            appSettings.SaveTransitionDirection(index);
        }

        /// <summary>
        /// Persists the selected transition duration when a valid option is chosen.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private void TransitionDurationComboBox_SelectedIndexChanged(
            object? sender,
            EventArgs e)
        {
            int[] durations =
                { 500, 1000, 1500, 2000, 3000, 5000 };

            int index =
                transitionDurationComboBox.SelectedIndex;

            if (index < 0 ||
                index >= durations.Length)
            {
                return;
            }

            selectedTransitionDurationMilliseconds =
                durations[index];

            appSettings.SaveTransitionDuration(selectedTransitionDurationMilliseconds);
        }
    }
}
