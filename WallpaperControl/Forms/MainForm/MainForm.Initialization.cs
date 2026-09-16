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
    // Main-window initialization responsibilities; see README.md in this directory for the code map.
    public partial class MainForm
    {
        /// <summary>
        /// Builds controls and subscriptions in dependency order, then loads preferences and starts slideshow coordination.
        /// </summary>
        public MainForm()
        {
            pauseOnFullscreen = appSettings.LoadPauseOnFullscreen();
            widgetManager = new WidgetManager(() =>
                AdvanceWallpaper(DesktopSlideshowDirection.Forward));

            Text = "Wallpaper Control";

            Icon = Icon.ExtractAssociatedIcon(
                Application.ExecutablePath);

            ClientSize = new Size(425, 690);

            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;

            windowOpacityPercent =
                appSettings.LoadWindowOpacityPercent();

            themeMode =
                appSettings.LoadThemeMode();

            Opacity =
                windowOpacityPercent / 100.0;

            toolTip = new ToolTip
            {
                AutoPopDelay = 7000,
                InitialDelay = 500,
                ReshowDelay = 100,
                ShowAlways = true
            };

            customSlideshowPreciseTimer =
                new System.Threading.Timer(
                    CustomSlideshowPreciseTimerCallback,
                    null,
                    Timeout.Infinite,
                    Timeout.Infinite);

            automaticUpdateCheckTimer =
                new System.Windows.Forms.Timer
                {
                    Interval = (int)TimeSpan.FromHours(24).TotalMilliseconds
                };

            automaticUpdateCheckTimer.Tick +=
                AutomaticUpdateCheckTimer_Tick;

            RestoreWindowPosition();

            Font = CreateOwnedFont("Segoe UI", 10);

            AllowDrop = true;

            DragEnter +=
                MainForm_DragEnter;

            DragDrop +=
                MainForm_DragDrop;

            Shown +=
                MainForm_Shown;

            // ========================================================
            // Status and window actions
            // ========================================================

            statusLabel = new Label
            {
                AutoSize = false,
                Location = new Point(48, 18),
                Size = new Size(329, 25),
                Font = CreateOwnedFont(
                    "Segoe UI",
                    9,
                    FontStyle.Bold),
                Visible = false
            };

            activateButton = new MainFormButton
            {
                Text = Localization.Get("ActivateSlideshow"),
                Location = new Point(25, 48),
                Size = new Size(375, 34),
                Visible = false
            };

            activateButton.Click +=
                ActivateButton_Click;

            settingsButton = new MainFormButton
            {
                Text = "⚙",
                Location = new Point(387, 10),
                Size = new Size(28, 28),
                Font = CreateOwnedFont("Segoe UI Symbol", 12),
                FlatStyle = FlatStyle.Flat,
                TabStop = false,
                Cursor = Cursors.Hand
            };

            settingsButton.FlatAppearance.BorderSize = 0;

            settingsButton.Click +=
                SettingsButton_Click;

            aboutButton = new MainFormButton
            {
                Text = "ⓘ",
                Location = new Point(10, 10),
                Size = new Size(28, 28),
                Font = CreateOwnedFont("Segoe UI Symbol", 11),
                FlatStyle = FlatStyle.Flat,
                TabStop = false,
                Cursor = Cursors.Hand
            };

            aboutButton.FlatAppearance.BorderSize = 0;

            aboutButton.Click +=
                AboutButton_Click;

            // ========================================================
            // Source folder
            // ========================================================

            folderLabel = new Label
            {
                Text = Localization.Get("WallpaperFolder"),
                AutoSize = false,
                Location = new Point(48, 18),
                Size = new Size(329, 25),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = CreateOwnedFont(
                    "Segoe UI",
                    11,
                    FontStyle.Bold)
            };

            folderTextBox = new TextBox
            {
                Location = new Point(25, 56),
                Width = 300,
                ReadOnly = true
            };

            folderButton = new MainFormButton
            {
                Text = "...",
                Location = new Point(335, 55),
                Size = new Size(65, 28)
            };

            folderButton.Click +=
                FolderButton_Click;

            wallpaperCountLabel = new Label
            {
                Text = Localization.Get("WallpaperCountZero"),
                AutoSize = true,
                Location = new Point(25, 88),
                Font = CreateOwnedFont("Segoe UI", 8)
            };

            // ========================================================
            // Slideshow interval
            // ========================================================

            intervalLabel = new Label
            {
                Text = Localization.Get("WallpaperInterval"),
                AutoSize = true,
                Location = new Point(25, 110),
                Font = CreateOwnedFont(
                    "Segoe UI",
                    11,
                    FontStyle.Bold)
            };

            intervalComboBox = new MainFormComboBox
            {
                Location = new Point(25, 145),
                Width = 375,
                DropDownStyle =
                    ComboBoxStyle.DropDownList
            };

            foreach (var item in intervals)
            {
                intervalComboBox.Items.Add(item);
            }

            intervalComboBox.SelectedIndexChanged +=
                IntervalComboBox_SelectedIndexChanged;

            windowsIntervalLabel = new Label
            {
                Text = Localization.Get("CurrentWindowsValueEmpty"),
                AutoSize = false,
                Location = new Point(25, 176),
                Size = new Size(375, 20),
                Font = CreateOwnedFont(
                    "Segoe UI",
                    8.25f,
                    FontStyle.Regular)
            };

            // ========================================================
            // Shuffle
            // ========================================================

            shuffleCheckBox = new CheckBox
            {
                Text = Localization.Get("Shuffle"),
                AutoSize = true,
                Location = new Point(25, 200)
            };

            shuffleCheckBox.CheckedChanged +=
                ShuffleCheckBox_CheckedChanged;

            // ========================================================
            // Wallpaper layout
            // ========================================================

            positionLabel = new Label
            {
                Text = Localization.Get("WallpaperPosition"),
                AutoSize = true,
                Location = new Point(25, 235),
                Font = CreateOwnedFont(
                    "Segoe UI",
                    11,
                    FontStyle.Bold)
            };

            positionComboBox = new MainFormComboBox
            {
                Location = new Point(25, 270),
                Width = 375,
                DropDownStyle =
                    ComboBoxStyle.DropDownList
            };

            foreach (var item in positions)
            {
                positionComboBox.Items.Add(item);
            }

            positionComboBox.SelectedIndexChanged +=
                PositionComboBox_SelectedIndexChanged;

            // ========================================================
            // Transition effect
            // ========================================================

            transitionLabel = new Label
            {
                Text = Localization.Get("Transition"),
                AutoSize = true,
                Location = new Point(25, 310),
                Font = CreateOwnedFont(
                    "Segoe UI",
                    11,
                    FontStyle.Bold)
            };

            transitionComboBox = new MainFormComboBox
            {
                Location = new Point(25, 345),
                Width = 125,
                DropDownStyle =
                    ComboBoxStyle.DropDownList
            };

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

            transitionComboBox.SelectedIndex = 0;

            transitionComboBox.SelectedIndexChanged +=
                TransitionComboBox_SelectedIndexChanged;

            transitionDirectionComboBox = new MainFormComboBox
            {
                Location = new Point(160, 345),
                Width = 115,
                DropDownStyle =
                    ComboBoxStyle.DropDownList
            };

            transitionDirectionComboBox.Items.AddRange(
                new object[]
                {
                    Localization.Get("DirectionLeft"),
                    Localization.Get("DirectionRight"),
                    Localization.Get("DirectionUp"),
                    Localization.Get("DirectionDown"),
                    Localization.Get("DirectionRandom")
                });

            transitionDirectionComboBox.SelectedIndex = 0;

            transitionDirectionComboBox.SelectedIndexChanged +=
                TransitionDirectionComboBox_SelectedIndexChanged;

            transitionDurationLabel = new Label
            {
                Text = Localization.Get("TransitionDuration"),
                AutoSize = true,
                Location = new Point(285, 310),
                Font = CreateOwnedFont(
                    "Segoe UI",
                    11,
                    FontStyle.Bold)
            };

            transitionDurationComboBox = new MainFormComboBox
            {
                Location = new Point(285, 345),
                Width = 115,
                DropDownStyle =
                    ComboBoxStyle.DropDownList
            };

            transitionDurationComboBox.Items.AddRange(
                new object[]
                {
                    "0,5 s",
                    "1,0 s",
                    "1,5 s",
                    "2,0 s",
                    "3,0 s",
                    "5,0 s"
                });

            transitionDurationComboBox.SelectedIndexChanged +=
                TransitionDurationComboBox_SelectedIndexChanged;

            LoadTransitionSettings();

            // ========================================================
            // Pause and pin actions
            // ========================================================

            pauseButton = new MainFormButton
            {
                Text = Localization.Get("PauseSlideshow"),
                Location = new Point(25, 395),
                Size = new Size(180, 38)
            };

            pauseButton.Click +=
                PauseButton_Click;

            pinButton = new MainFormButton
            {
                Text = Localization.Get("PinImage"),
                Location = new Point(220, 395),
                Size = new Size(180, 38)
            };

            pinButton.Click +=
                PinButton_Click;

            // ========================================================
            // Wallpaper navigation
            // ========================================================

            nextWallpaperButton = new MainFormButton
            {
                Text = Localization.Get("NextWallpaper"),
                Location = new Point(25, 450),
                Size = new Size(375, 38)
            };

            nextWallpaperButton.Click +=
                NextWallpaperButton_Click;

            // ========================================================
            // Current wallpaper, preview, and history
            // ========================================================

            currentWallpaperLabel = new Label
            {
                Text = Localization.Get("CurrentWallpaperEmpty"),
                AutoEllipsis = true,
                Location = new Point(25, 505),
                Size = new Size(375, 24)
            };

            currentWallpaperLabel.Cursor = Cursors.Hand;

            currentWallpaperLabel.Click +=
                CurrentWallpaperLabel_Click;

            currentWallpaperLabel.MouseEnter +=
                CurrentWallpaperLabel_MouseEnter;

            currentWallpaperLabel.MouseLeave +=
                CurrentWallpaperLabel_MouseLeave;

            wallpaperPreviewForm = new PreviewForm
            {
                FormBorderStyle = FormBorderStyle.None,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                TopMost = true,
                ClientSize = new Size(420, 300),
                Padding = new Padding(8)
            };

            wallpaperPreviewPictureBox = new PictureBox
            {
                Location = new Point(8, 8),
                Size = new Size(404, 228),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black
            };

            wallpaperPreviewInfoLabel = new Label
            {
                Location = new Point(8, 242),
                Size = new Size(404, 50),
                AutoEllipsis = true,
                Font = CreateOwnedFont("Segoe UI", 8.25f)
            };

            wallpaperPreviewForm.Controls.Add(
                wallpaperPreviewPictureBox);

            wallpaperPreviewForm.Controls.Add(
                wallpaperPreviewInfoLabel);

            explorerButton = new MainFormButton
            {
                Text = Localization.Get("ShowInExplorer"),
                Location = new Point(25, 540),
                Size = new Size(180, 38)
            };

            explorerButton.Click +=
                ExplorerButton_Click;

            rejectButton = new MainFormButton
            {
                Text = Localization.Get("RejectWallpaper"),
                Location = new Point(220, 540),
                Size = new Size(180, 38)
            };

            rejectButton.Click +=
                RejectButton_Click;

            rejectMenu = new ContextMenuStrip
            {
                ShowImageMargin = false,
                ShowCheckMargin = false
            };

            rejectMenu.Items.Add(
                Localization.Get("OpenRejectedFolder"),
                null,
                (_, _) => OpenRejectedFolder());

            rejectButton.ContextMenuStrip =
                rejectMenu;

            undoRejectButton = new MainFormButton
            {
                Text = Localization.Get("Undo"),
                Location = new Point(25, 588),
                Size = new Size(375, 34),
                Enabled = false
            };

            undoRejectButton.Click +=
                UndoRejectButton_Click;

            historyButton = new MainFormButton
            {
                Text = Localization.Get("History"),
                Location = new Point(25, 630),
                Size = new Size(180, 34),
                Enabled = false
            };

            statisticsButton = new MainFormButton
            {
                Text = Localization.Get("Statistics"),
                Location = new Point(220, 630),
                Size = new Size(180, 34)
            };

            statisticsButton.Click +=
                StatisticsButton_Click;

            historyMenu = new ContextMenuStrip
            {
                ShowImageMargin = false,
                ShowCheckMargin = false
            };

            historyButton.Click +=
                HistoryButton_Click;

            // ========================================================
            // Tray menu and timers
            // ========================================================

            trayMenu = new ContextMenuStrip
            {
                ShowImageMargin = false,
                ShowCheckMargin = false
            };

            trayMenu.Items.Add(
                Localization.Get("OpenWallpaperControl"),
                null,
                (_, _) => RestoreFromTray());

            trayMenu.Items.Add(
                new ToolStripSeparator());

            trayMenu.Items.Add(
                Localization.Get("NextWallpaper"),
                null,
                (_, _) =>
                    AdvanceWallpaper(
                        DesktopSlideshowDirection.Forward));

            trayPauseItem =
                new ToolStripMenuItem(
                    Localization.Get("PauseSlideshow"));

            trayPauseItem.Click +=
                TrayPauseItem_Click;

            trayMenu.Items.Add(
                trayPauseItem);

            trayMenu.Items.Add(
                Localization.Get("PinImage"),
                null,
                (_, _) =>
                    PinButton_Click(
                        this,
                        EventArgs.Empty));

            trayMenu.Items.Add(
                Localization.Get("OpenRejectedFolder"),
                null,
                (_, _) => OpenRejectedFolder());

            trayMenu.Items.Add(
                new ToolStripSeparator());

            trayMenu.Items.Add(
                Localization.Get("Exit"),
                null,
                (_, _) =>
                {
                    exitRequested = true;
                    Close();
                });

            trayIcon = new NotifyIcon
            {
                Icon = Icon,
                Text = "Wallpaper Control",
                ContextMenuStrip = trayMenu,
                Visible = true
            };

            trayIcon.DoubleClick +=
                (_, _) => RestoreFromTray();

            wallpaperRefreshTimer =
                new System.Windows.Forms.Timer
                {
                    Interval = 1000
                };

            wallpaperRefreshTimer.Tick +=
                async (_, _) =>
                {
                    await UpdateFullscreenPauseAsync();
                    if (fullscreenPolicy.IsPaused || IsDisposed) return;
                    UpdateCurrentWallpaperDisplay();
                    UpdateWallpaperPositionDisplay();
                };

            wallpaperRefreshTimer.Start();

            // Coalesce file-copy notifications into one count refresh.
            wallpaperCountDebounceTimer =
                new System.Windows.Forms.Timer
                {
                    Interval = 300
                };

            wallpaperCountDebounceTimer.Tick +=
                (_, _) =>
                {
                    wallpaperCountDebounceTimer.Stop();
                    if (fullscreenPolicy.IsPaused) { deferredWallpaperCount = true; return; }
                    UpdateWallpaperCount();
                };

            mainHeading.Text = "Wallpaper Control";
            mainHeading.Font = CreateOwnedFont("Segoe UI", 15, FontStyle.Bold);
            mainHeading.TextAlign = ContentAlignment.MiddleCenter;
            currentHeading.Font = CreateOwnedFont("Segoe UI", 10, FontStyle.Bold);
            directionHeading.Font = CreateOwnedFont("Segoe UI", 10, FontStyle.Bold);
            currentHeading.Text = Localization.Get("MainCurrentWallpaperHeading");
            directionHeading.Text = Localization.Get("MainDirectionHeading");
            folderLabel.TextAlign = ContentAlignment.MiddleLeft;
            nextWallpaperButton.Font = CreateOwnedFont("Segoe UI", 11, FontStyle.Bold);
            Controls.Add(mainHeading);
            Controls.Add(currentHeading);
            Controls.Add(directionHeading);
            Controls.Add(statusLabel);
            Controls.Add(activateButton);
            Controls.Add(settingsButton);
            Controls.Add(aboutButton);

            Controls.Add(folderLabel);
            Controls.Add(folderTextBox);
            Controls.Add(folderButton);
            Controls.Add(wallpaperCountLabel);

            Controls.Add(intervalLabel);
            Controls.Add(intervalComboBox);
            Controls.Add(windowsIntervalLabel);

            Controls.Add(shuffleCheckBox);
            Controls.Add(positionLabel);
            Controls.Add(positionComboBox);

            Controls.Add(transitionLabel);
            Controls.Add(transitionComboBox);
            Controls.Add(transitionDirectionComboBox);
            Controls.Add(transitionDurationLabel);
            Controls.Add(transitionDurationComboBox);

            Controls.Add(pauseButton);
            Controls.Add(pinButton);
            Controls.Add(nextWallpaperButton);

            Controls.Add(currentWallpaperLabel);
            Controls.Add(explorerButton);
            Controls.Add(rejectButton);
            Controls.Add(undoRejectButton);
            Controls.Add(historyButton);
            Controls.Add(statisticsButton);

            LoadSettings();
            LoadHotkeySettings();
            LoadRejectSettings();
            UpdateToolTips();
            LoadAutostartState();
            LoadCloseToTraySetting();
            LoadAutomaticUpdateCheckSetting();
            statistics.Load();
            UpdateCurrentWallpaperDisplay();
            UpdateWallpaperCount();
            ConfigureWallpaperFolderWatcher();

            loading = false;

            ApplyWindowsTheme();

            // The application owns slideshow timing. Schedule the first change
            // at the next clock-aligned interval boundary.
            StartCustomSlideshowEngine();
            CheckSlideshowStatus();

            SystemEvents.UserPreferenceChanged +=
                SystemEvents_UserPreferenceChanged;

        }
    }
}
