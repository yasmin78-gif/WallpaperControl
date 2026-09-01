using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WallpaperControl
{
    public class MainForm : Form
    {
        private const string WindowsSlideshowRegistryPath =
            @"Control Panel\Personalization\Desktop Slideshow";

        private const string AppRegistryPath =
            @"Software\WallpaperControl";

        private readonly Label statusLabel;
        private readonly Button activateButton;
        private readonly Button settingsButton;
        private readonly Button aboutButton;

        private readonly Label folderLabel;
        private readonly TextBox folderTextBox;
        private readonly Button folderButton;
        private readonly Label wallpaperCountLabel;

        private readonly Label intervalLabel;
        private readonly ComboBox intervalComboBox;
        private readonly Label windowsIntervalLabel;

        private readonly CheckBox shuffleCheckBox;

        private readonly Label positionLabel;
        private readonly ComboBox positionComboBox;
        private DesktopWallpaperPosition? lastWallpaperPosition;

        private readonly Button pauseButton;
        private readonly Button pinButton;
        private readonly Button nextWallpaperButton;

        private readonly Label currentWallpaperLabel;
        private readonly Form wallpaperPreviewForm;
        private readonly PictureBox wallpaperPreviewPictureBox;
        private readonly Label wallpaperPreviewInfoLabel;
        private readonly Button explorerButton;
        private readonly Button rejectButton;
        private readonly ContextMenuStrip rejectMenu;
        private readonly Button undoRejectButton;
        private readonly Button historyButton;
        private readonly Button statisticsButton;
        private readonly ContextMenuStrip historyMenu;
        private readonly ToolTip toolTip;

        private readonly List<string> wallpaperHistory = new();
        private const int MaxWallpaperHistory = 10;

        private readonly Dictionary<string, int>
            wallpaperViewCounts =
                new(StringComparer.OrdinalIgnoreCase);

        private readonly DateTime statisticsStartedAt =
            DateTime.Now;

        private readonly Dictionary<string, DateTime>
            wallpaperLastShown =
                new(StringComparer.OrdinalIgnoreCase);

        private string? lastCountedWallpaperPath;

        private string? lastRejectedSourcePath;
        private string? lastRejectedDestinationPath;

        private readonly NotifyIcon trayIcon;
        private readonly ContextMenuStrip trayMenu;
        private readonly ToolStripMenuItem trayPauseItem;

        private readonly System.Windows.Forms.Timer wallpaperRefreshTimer;
        private string? lastDisplayedWallpaperPath;

        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_NEXT = 1;
        private const int HOTKEY_PAUSE = 2;
        private const int HOTKEY_EXPLORER = 3;
        private const int HOTKEY_REJECT = 4;

        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;

        private const uint VK_RIGHT = 0x27;
        private const uint VK_P = 0x50;
        private const uint VK_E = 0x45;
        private const uint VK_R = 0x52;

        private uint hotkeyNextModifiers = MOD_CONTROL | MOD_ALT;
        private uint hotkeyNextKey = VK_RIGHT;

        private uint hotkeyPauseModifiers = MOD_CONTROL | MOD_ALT;
        private uint hotkeyPauseKey = VK_P;

        private uint hotkeyExplorerModifiers = MOD_CONTROL | MOD_ALT;
        private uint hotkeyExplorerKey = VK_E;

        private uint hotkeyRejectModifiers = MOD_CONTROL | MOD_ALT;
        private uint hotkeyRejectKey = VK_R;

        private string rejectRootFolder = "";
        private bool rejectUseSubfolder = true;

        private bool loading = true;
        private bool darkMode;
        private bool slideshowPaused = false;
        private bool closingAfterPauseResume = false;
        private bool restoringFromTray = false;
        private bool autostartEnabled = false;
        private bool closeToTrayEnabled = true;
        private bool exitRequested = false;
        private int windowOpacityPercent = 92;

        private readonly Dictionary<string, uint> intervals = new()
        {
            { Localization.Get("Interval1Minute"), 60000 },
            { Localization.Get("Interval2Minutes"), 120000 },
            { Localization.Get("Interval3Minutes"), 180000 },
            { Localization.Get("Interval5Minutes"), 300000 },
            { Localization.Get("Interval10Minutes"), 600000 },
            { Localization.Get("Interval15Minutes"), 900000 },
            { Localization.Get("Interval30Minutes"), 1800000 },
            { Localization.Get("Interval1Hour"), 3600000 },
            { Localization.Get("Interval6Hours"), 21600000 },
            { Localization.Get("Interval1Day"), 86400000 }
        };

        private readonly Dictionary<string, DesktopWallpaperPosition> positions = new()
        {
            { Localization.Get("PositionFill"), DesktopWallpaperPosition.Fill },
            { Localization.Get("PositionFit"), DesktopWallpaperPosition.Fit },
            { Localization.Get("PositionStretch"), DesktopWallpaperPosition.Stretch },
            { Localization.Get("PositionTile"), DesktopWallpaperPosition.Tile },
            { Localization.Get("PositionCenter"), DesktopWallpaperPosition.Center },
            { Localization.Get("PositionSpan"), DesktopWallpaperPosition.Span }
        };

        public MainForm()
        {
            Text = "Wallpaper Control";

            Icon = Icon.ExtractAssociatedIcon(
                Application.ExecutablePath);

            ClientSize = new Size(425, 615);

            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;

            windowOpacityPercent =
                LoadWindowOpacityPercent();

            Opacity =
                windowOpacityPercent / 100.0;

            toolTip = new ToolTip
            {
                AutoPopDelay = 7000,
                InitialDelay = 500,
                ReshowDelay = 100,
                ShowAlways = true
            };

            RestoreWindowPosition();

            Font = new Font("Segoe UI", 10);

            AllowDrop = true;

            DragEnter +=
                MainForm_DragEnter;

            DragDrop +=
                MainForm_DragDrop;

            // ========================================================
            // STATUS
            // ========================================================

            statusLabel = new Label
            {
                AutoSize = false,
                Location = new Point(48, 18),
                Size = new Size(329, 25),
                Font = new Font(
                    "Segoe UI",
                    9,
                    FontStyle.Bold),
                Visible = false
            };

            activateButton = new Button
            {
                Text = Localization.Get("ActivateSlideshow"),
                Location = new Point(25, 48),
                Size = new Size(375, 34),
                Visible = false
            };

            activateButton.Click +=
                ActivateButton_Click;

            settingsButton = new Button
            {
                Text = "⚙",
                Location = new Point(387, 10),
                Size = new Size(28, 28),
                Font = new Font("Segoe UI Symbol", 12),
                FlatStyle = FlatStyle.Flat,
                TabStop = false,
                Cursor = Cursors.Hand
            };

            settingsButton.FlatAppearance.BorderSize = 0;

            settingsButton.Click +=
                SettingsButton_Click;

            aboutButton = new Button
            {
                Text = "ⓘ",
                Location = new Point(10, 10),
                Size = new Size(28, 28),
                Font = new Font("Segoe UI Symbol", 11),
                FlatStyle = FlatStyle.Flat,
                TabStop = false,
                Cursor = Cursors.Hand
            };

            aboutButton.FlatAppearance.BorderSize = 0;

            aboutButton.Click +=
                AboutButton_Click;

            // ========================================================
            // ORDNER
            // ========================================================

            folderLabel = new Label
            {
                Text = Localization.Get("WallpaperFolder"),
                AutoSize = false,
                Location = new Point(48, 18),
                Size = new Size(329, 25),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(
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

            folderButton = new Button
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
                Font = new Font("Segoe UI", 8)
            };

            // ========================================================
            // INTERVALL
            // ========================================================

            intervalLabel = new Label
            {
                Text = Localization.Get("WallpaperInterval"),
                AutoSize = true,
                Location = new Point(25, 110),
                Font = new Font(
                    "Segoe UI",
                    11,
                    FontStyle.Bold)
            };

            intervalComboBox = new ComboBox
            {
                Location = new Point(25, 145),
                Width = 375,
                DropDownStyle =
                    ComboBoxStyle.DropDownList
            };

            foreach (var item in intervals.Keys)
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
                Font = new Font(
                    "Segoe UI",
                    8.25f,
                    FontStyle.Regular)
            };

            // ========================================================
            // SHUFFLE
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
            // DARSTELLUNG
            // ========================================================

            positionLabel = new Label
            {
                Text = Localization.Get("WallpaperPosition"),
                AutoSize = true,
                Location = new Point(25, 235),
                Font = new Font(
                    "Segoe UI",
                    11,
                    FontStyle.Bold)
            };

            positionComboBox = new ComboBox
            {
                Location = new Point(25, 270),
                Width = 375,
                DropDownStyle =
                    ComboBoxStyle.DropDownList
            };

            foreach (var item in positions.Keys)
            {
                positionComboBox.Items.Add(item);
            }

            positionComboBox.SelectedIndexChanged +=
                PositionComboBox_SelectedIndexChanged;

            // ========================================================
            // PAUSE + FESTLEGEN
            // ========================================================

            pauseButton = new Button
            {
                Text = Localization.Get("PauseSlideshow"),
                Location = new Point(25, 320),
                Size = new Size(180, 38)
            };

            pauseButton.Click +=
                PauseButton_Click;

            pinButton = new Button
            {
                Text = Localization.Get("PinImage"),
                Location = new Point(220, 320),
                Size = new Size(180, 38)
            };

            pinButton.Click +=
                PinButton_Click;

            // ========================================================
            // NAVIGATION
            // ========================================================

            nextWallpaperButton = new Button
            {
                Text = Localization.Get("NextWallpaper"),
                Location = new Point(25, 375),
                Size = new Size(375, 38)
            };

            nextWallpaperButton.Click +=
                NextWallpaperButton_Click;

            // ========================================================
            // AKTUELLES WALLPAPER
            // ========================================================

            currentWallpaperLabel = new Label
            {
                Text = Localization.Get("CurrentWallpaperEmpty"),
                AutoEllipsis = true,
                Location = new Point(25, 430),
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
                Font = new Font("Segoe UI", 8.25f)
            };

            wallpaperPreviewForm.Controls.Add(
                wallpaperPreviewPictureBox);

            wallpaperPreviewForm.Controls.Add(
                wallpaperPreviewInfoLabel);

            explorerButton = new Button
            {
                Text = Localization.Get("ShowInExplorer"),
                Location = new Point(25, 465),
                Size = new Size(180, 38)
            };

            explorerButton.Click +=
                ExplorerButton_Click;

            rejectButton = new Button
            {
                Text = Localization.Get("RejectWallpaper"),
                Location = new Point(220, 465),
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

            undoRejectButton = new Button
            {
                Text = Localization.Get("Undo"),
                Location = new Point(25, 513),
                Size = new Size(375, 34),
                Enabled = false
            };

            undoRejectButton.Click +=
                UndoRejectButton_Click;

            historyButton = new Button
            {
                Text = Localization.Get("History"),
                Location = new Point(25, 555),
                Size = new Size(180, 34),
                Enabled = false
            };

            statisticsButton = new Button
            {
                Text = Localization.Get("Statistics"),
                Location = new Point(220, 555),
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
            // TRAY
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
                (_, _) =>
                {
                    UpdateCurrentWallpaperDisplay();
                    UpdateWallpaperPositionDisplay();
                };

            wallpaperRefreshTimer.Start();

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
            UpdateCurrentWallpaperDisplay();
            UpdateWallpaperCount();

            loading = false;

            ApplyWindowsTheme();
            CheckSlideshowStatus();

            SystemEvents.UserPreferenceChanged +=
                SystemEvents_UserPreferenceChanged;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SystemEvents.UserPreferenceChanged -=
                    SystemEvents_UserPreferenceChanged;

                if (IsHandleCreated)
                {
                    UnregisterHotKeys();
                }

                wallpaperRefreshTimer.Stop();
                wallpaperRefreshTimer.Dispose();

                if (wallpaperPreviewPictureBox.Image != null)
                {
                    wallpaperPreviewPictureBox.Image.Dispose();
                    wallpaperPreviewPictureBox.Image = null;
                }

                wallpaperPreviewForm.Dispose();

                historyMenu.Dispose();
                rejectMenu.Dispose();

                trayIcon.Visible = false;
                trayIcon.Dispose();
                trayMenu.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override void OnHandleCreated(
            EventArgs e)
        {
            base.OnHandleCreated(e);

            ApplyTitleBarTheme();
            RegisterHotKeys();
        }

        // ============================================================
        // DARK MODE
        // ============================================================

        private void SystemEvents_UserPreferenceChanged(
            object sender,
            UserPreferenceChangedEventArgs e)
        {
            if (IsDisposed)
                return;

            BeginInvoke(() =>
            {
                ApplyWindowsTheme();
            });
        }

        private bool IsWindowsDarkMode()
        {
            try
            {
                using RegistryKey? key =
                    Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

                object? value =
                    key?.GetValue("AppsUseLightTheme");

                if (value != null)
                {
                    return Convert.ToInt32(value) == 0;
                }
            }
            catch
            {
            }

            return false;
        }

        private void ApplyWindowsTheme()
        {
            darkMode = IsWindowsDarkMode();

            Color background =
                darkMode
                ? Color.FromArgb(32, 32, 32)
                : SystemColors.Control;

            Color foreground =
                darkMode
                ? Color.FromArgb(235, 235, 235)
                : SystemColors.ControlText;

            Color inputBackground =
                darkMode
                ? Color.FromArgb(48, 48, 48)
                : SystemColors.Window;

            Color inputForeground =
                darkMode
                ? Color.White
                : SystemColors.WindowText;

            Color buttonBackground =
                darkMode
                ? Color.FromArgb(50, 50, 50)
                : SystemColors.Control;

            BackColor = background;
            ForeColor = foreground;

            folderLabel.ForeColor = foreground;
            wallpaperCountLabel.ForeColor = foreground;
            intervalLabel.ForeColor = foreground;

            statusLabel.ForeColor =
                darkMode
                ? Color.Orange
                : Color.DarkOrange;

            folderTextBox.BackColor =
                inputBackground;

            folderTextBox.ForeColor =
                inputForeground;

            intervalComboBox.BackColor =
                inputBackground;

            intervalComboBox.ForeColor =
                inputForeground;

            windowsIntervalLabel.ForeColor =
                darkMode
                ? Color.FromArgb(180, 180, 180)
                : Color.DimGray;

            shuffleCheckBox.ForeColor =
                foreground;

            positionLabel.ForeColor =
                foreground;

            positionComboBox.BackColor =
                inputBackground;

            positionComboBox.ForeColor =
                inputForeground;

            settingsButton.BackColor =
                background;

            settingsButton.ForeColor =
                darkMode
                ? Color.FromArgb(210, 210, 210)
                : Color.DimGray;

            settingsButton.FlatAppearance.MouseOverBackColor =
                darkMode
                ? Color.FromArgb(55, 55, 55)
                : Color.FromArgb(225, 225, 225);

            settingsButton.FlatAppearance.MouseDownBackColor =
                darkMode
                ? Color.FromArgb(70, 70, 70)
                : Color.FromArgb(210, 210, 210);

            aboutButton.BackColor =
                background;

            aboutButton.ForeColor =
                darkMode
                ? Color.FromArgb(210, 210, 210)
                : Color.DimGray;

            aboutButton.FlatAppearance.MouseOverBackColor =
                darkMode
                ? Color.FromArgb(55, 55, 55)
                : Color.FromArgb(225, 225, 225);

            aboutButton.FlatAppearance.MouseDownBackColor =
                darkMode
                ? Color.FromArgb(70, 70, 70)
                : Color.FromArgb(210, 210, 210);

            StyleButton(
                folderButton,
                buttonBackground,
                foreground);

            StyleButton(
                activateButton,
                buttonBackground,
                foreground);

            StyleButton(
                pauseButton,
                buttonBackground,
                foreground);

            StyleButton(
                pinButton,
                buttonBackground,
                foreground);

            StyleButton(
                nextWallpaperButton,
                buttonBackground,
                foreground);
            StyleButton(
                explorerButton,
                buttonBackground,
                foreground);

            StyleButton(
                rejectButton,
                buttonBackground,
                foreground);

            StyleButton(
                undoRejectButton,
                buttonBackground,
                foreground);

            StyleButton(
                historyButton,
                buttonBackground,
                foreground);

            StyleButton(
                statisticsButton,
                buttonBackground,
                foreground);

            historyMenu.BackColor =
                background;

            historyMenu.ForeColor =
                foreground;

            rejectMenu.BackColor =
                background;

            rejectMenu.ForeColor =
                foreground;

            currentWallpaperLabel.ForeColor =
                foreground;

            wallpaperPreviewForm.BackColor =
                darkMode
                ? Color.FromArgb(32, 32, 32)
                : SystemColors.Control;

            wallpaperPreviewInfoLabel.ForeColor =
                foreground;

            wallpaperPreviewPictureBox.BackColor =
                Color.Black;

            trayMenu.BackColor =
                background;

            trayMenu.ForeColor =
                foreground;


            ApplyTitleBarTheme();
            Invalidate(true);
        }

        private void StyleButton(
            Button button,
            Color background,
            Color foreground)
        {
            button.UseVisualStyleBackColor = false;
            button.BackColor = background;
            button.ForeColor = foreground;
            button.FlatStyle = FlatStyle.Flat;

            button.FlatAppearance.BorderColor =
                darkMode
                ? Color.FromArgb(85, 85, 85)
                : Color.FromArgb(180, 180, 180);
        }

        private void ApplyTitleBarTheme()
        {
            if (!IsHandleCreated)
                return;

            int value =
                darkMode ? 1 : 0;

            DwmSetWindowAttribute(
                Handle,
                DWMWA_USE_IMMERSIVE_DARK_MODE,
                ref value,
                sizeof(int));
        }

        // ============================================================
        // EINSTELLUNGEN LADEN
        // ============================================================

        private void LoadSettings()
        {
            LoadSlideshowFolder();
            LoadSlideshowOptions();
            LoadWallpaperPosition();
        }

        // ============================================================
        // STATUS
        // ============================================================

        private void CheckSlideshowStatus()
        {
            // Nur für die aktuelle Programmsitzung pausiert?
            if (slideshowPaused)
            {
                ShowPausedStatus();
                return;
            }

            IDesktopWallpaper? wallpaper = null;

            try
            {
                wallpaper =
                    (IDesktopWallpaper)
                    new DesktopWallpaper();

                wallpaper.GetStatus(
                    out DesktopSlideshowState state);

                bool enabled =
                    (state &
                     DesktopSlideshowState.Enabled) != 0;

                bool slideshow =
                    (state &
                     DesktopSlideshowState.Slideshow) != 0;

                bool remoteDisabled =
                    (state &
                     DesktopSlideshowState
                         .DisabledByRemoteSession) != 0;

                if (remoteDisabled)
                {
                    ShowInactiveStatus(
                        Localization.Get("StatusRemoteDisabled"),
                        false);

                    return;
                }

                if (!enabled || !slideshow)
                {
                    ShowInactiveStatus(
                        Localization.Get("StatusInactive"),
                        true);

                    return;
                }

                ShowActiveStatus();
            }
            catch
            {
                ShowActiveStatus();
            }
            finally
            {
                ReleaseComObject(wallpaper);
            }
        }

        private void ShowActiveStatus()
        {
            statusLabel.Visible = false;
            activateButton.Visible = false;

            pauseButton.Text =
                Localization.Get("PauseSlideshow");

            pauseButton.Enabled = true;
            pinButton.Enabled = true;
            nextWallpaperButton.Enabled = true;

            UpdateCurrentWallpaperDisplay();
            UpdateTrayPauseText();

            SetNormalLayout();
        }

        private void ShowPausedStatus()
        {
            statusLabel.Text =
                Localization.Get("StatusPaused");

            statusLabel.Visible = true;
            activateButton.Visible = false;

            pauseButton.Text =
                Localization.Get("ResumeSlideshow");

            pauseButton.Enabled = true;
            pinButton.Enabled = true;
            nextWallpaperButton.Enabled = false;

            UpdateCurrentWallpaperDisplay();
            UpdateTrayPauseText();

            SetWarningLayout(false);
        }

        private void ShowInactiveStatus(
            string message,
            bool canActivate)
        {
            statusLabel.Text = message;
            statusLabel.Visible = true;

            activateButton.Visible =
                canActivate;

            pauseButton.Text =
                Localization.Get("PauseSlideshow");

            pauseButton.Enabled = false;
            nextWallpaperButton.Enabled = false;

            UpdateCurrentWallpaperDisplay();
            UpdateTrayPauseText();

            // Localization.Get("PinImage") ergibt ohne aktive
            // Diashow ebenfalls keinen Sinn.
            pinButton.Enabled = false;

            SetWarningLayout(canActivate);
        }

        // ============================================================
        // LAYOUT
        // ============================================================

        private void SetNormalLayout()
        {
            folderLabel.Location =
                new Point(48, 18);

            folderTextBox.Location =
                new Point(25, 56);

            folderButton.Location =
                new Point(335, 55);

            wallpaperCountLabel.Location =
                new Point(25, 88);

            intervalLabel.Location =
                new Point(25, 110);

            intervalComboBox.Location =
                new Point(25, 145);

            windowsIntervalLabel.Location =
                new Point(25, 176);

            shuffleCheckBox.Location =
                new Point(25, 200);

            positionLabel.Location =
                new Point(25, 235);

            positionComboBox.Location =
                new Point(25, 270);

            pauseButton.Location =
                new Point(25, 320);

            pinButton.Location =
                new Point(220, 320);

            nextWallpaperButton.Location =
                new Point(25, 375);

            currentWallpaperLabel.Location =
                new Point(25, 430);

            explorerButton.Location =
                new Point(25, 465);

            rejectButton.Location =
                new Point(220, 465);

            undoRejectButton.Location =
                new Point(25, 513);

            historyButton.Location =
                new Point(25, 555);

            statisticsButton.Location =
                new Point(220, 555);

            ClientSize =
                new Size(425, 615);
        }

        private void SetWarningLayout(
            bool showActivateButton)
        {
            int offset =
                showActivateButton ? 75 : 40;

            folderLabel.Location =
                new Point(48, 18 + offset);

            folderTextBox.Location =
                new Point(25, 56 + offset);

            folderButton.Location =
                new Point(335, 55 + offset);

            wallpaperCountLabel.Location =
                new Point(25, 88 + offset);

            intervalLabel.Location =
                new Point(25, 110 + offset);

            intervalComboBox.Location =
                new Point(25, 145 + offset);

            windowsIntervalLabel.Location =
                new Point(25, 176 + offset);

            shuffleCheckBox.Location =
                new Point(25, 200 + offset);

            positionLabel.Location =
                new Point(25, 235 + offset);

            positionComboBox.Location =
                new Point(25, 270 + offset);

            pauseButton.Location =
                new Point(25, 320 + offset);

            pinButton.Location =
                new Point(220, 320 + offset);

            nextWallpaperButton.Location =
                new Point(25, 375 + offset);

            currentWallpaperLabel.Location =
                new Point(25, 430 + offset);

            explorerButton.Location =
                new Point(25, 465 + offset);

            rejectButton.Location =
                new Point(220, 465 + offset);

            undoRejectButton.Location =
                new Point(25, 513 + offset);

            historyButton.Location =
                new Point(25, 555 + offset);

            statisticsButton.Location =
                new Point(220, 555 + offset);

            ClientSize =
                new Size(
                    425,
                    615 + offset);
        }

        // ============================================================
        // ORDNER LADEN
        // ============================================================

        private void LoadSlideshowFolder()
        {
            IDesktopWallpaper? wallpaper = null;
            IShellItemArray? array = null;
            IShellItem? item = null;

            bool folderFound = false;

            try
            {
                wallpaper =
                    (IDesktopWallpaper)
                    new DesktopWallpaper();

                wallpaper.GetSlideshow(out array);

                if (array != null)
                {
                    array.GetCount(
                        out uint count);

                    if (count > 0)
                    {
                        array.GetItemAt(
                            0,
                            out item);

                        item.GetDisplayName(
                            SIGDN.FILESYSPATH,
                            out IntPtr pathPointer);

                        if (pathPointer != IntPtr.Zero)
                        {
                            try
                            {
                                string? path =
                                    Marshal.PtrToStringUni(
                                        pathPointer);

                                if (!string.IsNullOrWhiteSpace(
                                    path))
                                {
                                    folderTextBox.Text =
                                        path;

                                    SaveLastWallpaperFolder(
                                        path);

                                    folderFound = true;
                                }
                            }
                            finally
                            {
                                Marshal.FreeCoTaskMem(
                                    pathPointer);
                            }
                        }
                    }
                }
            }
            catch
            {
            }
            finally
            {
                ReleaseComObject(item);
                ReleaseComObject(array);
                ReleaseComObject(wallpaper);
            }

            if (!folderFound)
            {
                string? saved =
                    LoadLastWallpaperFolder();

                if (!string.IsNullOrWhiteSpace(
                    saved))
                {
                    folderTextBox.Text =
                        saved;
                }
            }
        }

        // ============================================================
        // WALLPAPER-ANZAHL
        // ============================================================

        private void UpdateWallpaperCount()
        {
            string folder = folderTextBox.Text;

            if (string.IsNullOrWhiteSpace(folder) ||
                !Directory.Exists(folder))
            {
                wallpaperCountLabel.Text = Localization.Get("WallpaperCountZero");
                return;
            }

            try
            {
                string[] extensions =
                {
                    ".jpg", ".jpeg", ".png", ".bmp",
                    ".gif", ".tif", ".tiff", ".webp"
                };

                int count = Directory.EnumerateFiles(
                        folder,
                        "*",
                        SearchOption.TopDirectoryOnly)
                    .Count(file =>
                        extensions.Contains(
                            Path.GetExtension(file),
                            StringComparer.OrdinalIgnoreCase));

                wallpaperCountLabel.Text =
                    count == 1
                    ? Localization.Get("WallpaperCountOne")
                    : string.Format(
                        Localization.Get("WallpaperCountMany"),
                        count);
            }
            catch
            {
                wallpaperCountLabel.Text = Localization.Get("WallpaperCountUnavailable");
            }
        }

        // ============================================================
        // OPTIONEN LADEN
        // ============================================================

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
                            item.Key;

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
                    Localization.Get("Interval5Minutes");
            }
        }

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
                                item.Key;

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

        // ============================================================
        // APP-EIGENE REGISTRY
        // ============================================================

        private void SaveLastWallpaperFolder(
            string path)
        {
            try
            {
                using RegistryKey key =
                    Registry.CurrentUser.CreateSubKey(
                        AppRegistryPath);

                key.SetValue(
                    "LastWallpaperFolder",
                    path,
                    RegistryValueKind.String);
            }
            catch
            {
            }
        }

        private string? LoadLastWallpaperFolder()
        {
            try
            {
                using RegistryKey? key =
                    Registry.CurrentUser.OpenSubKey(
                        AppRegistryPath);

                return key?.GetValue(
                    "LastWallpaperFolder")
                    as string;
            }
            catch
            {
                return null;
            }
        }

        private void SaveWindowPosition()
        {
            try
            {
                Rectangle bounds =
                    WindowState == FormWindowState.Normal
                    ? Bounds
                    : RestoreBounds;

                using RegistryKey key =
                    Registry.CurrentUser.CreateSubKey(
                        AppRegistryPath);

                key.SetValue("WindowX", bounds.X, RegistryValueKind.DWord);
                key.SetValue("WindowY", bounds.Y, RegistryValueKind.DWord);
            }
            catch
            {
            }
        }

        private void RestoreWindowPosition()
        {
            StartPosition = FormStartPosition.Manual;

            try
            {
                using RegistryKey? key =
                    Registry.CurrentUser.OpenSubKey(
                        AppRegistryPath);

                object? xValue = key?.GetValue("WindowX");
                object? yValue = key?.GetValue("WindowY");

                if (xValue != null && yValue != null)
                {
                    int x = Convert.ToInt32(xValue);
                    int y = Convert.ToInt32(yValue);

                    Rectangle savedBounds =
                        new Rectangle(
                            x,
                            y,
                            ClientSize.Width,
                            ClientSize.Height);

                    if (IsWindowPositionVisible(savedBounds))
                    {
                        Location = new Point(x, y);
                        return;
                    }
                }
            }
            catch
            {
            }

            Screen screen =
                Screen.PrimaryScreen ??
                Screen.AllScreens[0];

            Rectangle area = screen.WorkingArea;

            Location = new Point(
                area.Left + Math.Max(0, (area.Width - Width) / 2),
                area.Top + Math.Max(0, (area.Height - Height) / 2));
        }

        private bool IsWindowPositionVisible(
            Rectangle windowBounds)
        {
            foreach (Screen screen in Screen.AllScreens)
            {
                Rectangle visible =
                    Rectangle.Intersect(
                        windowBounds,
                        screen.WorkingArea);

                if (visible.Width >= 120 &&
                    visible.Height >= 80)
                {
                    return true;
                }
            }

            return false;
        }

        protected override void OnResize(
            EventArgs e)
        {
            base.OnResize(e);

            if (!restoringFromTray &&
                WindowState ==
                    FormWindowState.Minimized)
            {
                Hide();
                ShowInTaskbar = false;
            }
        }

        protected override async void OnFormClosing(
            FormClosingEventArgs e)
        {
            SaveWindowPosition();

            if (closeToTrayEnabled &&
                !exitRequested &&
                e.CloseReason ==
                    CloseReason.UserClosing)
            {
                e.Cancel = true;

                Hide();
                ShowInTaskbar = false;

                return;
            }

            if (slideshowPaused &&
                !closingAfterPauseResume)
            {
                e.Cancel = true;
                closingAfterPauseResume = true;

                bool resumed =
                    await ResumeSlideshowAsync(
                        showError: true);

                closingAfterPauseResume = false;

                if (resumed)
                {
                    Close();
                }

                return;
            }

            base.OnFormClosing(e);
        }

        // ============================================================
        // DRAG & DROP
        // ============================================================

        private void MainForm_DragEnter(
            object? sender,
            DragEventArgs e)
        {
            if (!e.Data!.GetDataPresent(
                DataFormats.FileDrop))
            {
                e.Effect =
                    DragDropEffects.None;

                return;
            }

            if (e.Data.GetData(
                    DataFormats.FileDrop)
                is not string[] paths ||
                paths.Length == 0)
            {
                e.Effect =
                    DragDropEffects.None;

                return;
            }

            string path =
                paths[0];

            if (Directory.Exists(path) ||
                IsSupportedWallpaperFile(path))
            {
                e.Effect =
                    DragDropEffects.Copy;
            }
            else
            {
                e.Effect =
                    DragDropEffects.None;
            }
        }

        private void MainForm_DragDrop(
            object? sender,
            DragEventArgs e)
        {
            if (e.Data!.GetData(
                    DataFormats.FileDrop)
                is not string[] paths ||
                paths.Length == 0)
            {
                return;
            }

            string droppedPath =
                paths[0];

            string? folder = null;

            if (Directory.Exists(
                droppedPath))
            {
                folder =
                    droppedPath;
            }
            else if (IsSupportedWallpaperFile(
                droppedPath))
            {
                folder =
                    Path.GetDirectoryName(
                        droppedPath);
            }

            if (string.IsNullOrWhiteSpace(folder) ||
                !Directory.Exists(folder))
            {
                MessageBox.Show(
                    Localization.Get("MsgDropWallpaperFolderOrImage"),
                    "Wallpaper Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            ApplyNewWallpaperFolder(folder);
        }

        private static bool IsSupportedWallpaperFile(
            string path)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            string extension =
                Path.GetExtension(path);

            return extension.Equals(
                       ".jpg",
                       StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(
                       ".jpeg",
                       StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(
                       ".png",
                       StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(
                       ".bmp",
                       StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(
                       ".gif",
                       StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(
                       ".tif",
                       StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(
                       ".tiff",
                       StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(
                       ".webp",
                       StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyNewWallpaperFolder(
            string folder)
        {
            slideshowPaused = false;

            lastRejectedSourcePath = null;
            lastRejectedDestinationPath = null;
            undoRejectButton.Enabled = false;

            wallpaperHistory.Clear();
            historyButton.Enabled = false;
            historyButton.Text = Localization.Get("History");

            HideWallpaperPreview();

            SaveLastWallpaperFolder(
                folder);

            SetWallpaperFolder(
                folder);

            CheckSlideshowStatus();
        }

        // ============================================================
        // ORDNER AUSWÄHLEN
        // ============================================================

        private void FolderButton_Click(
            object? sender,
            EventArgs e)
        {
            using FolderBrowserDialog dialog =
                new FolderBrowserDialog();

            dialog.Description =
                Localization.Get("SelectWallpaperFolder");

            dialog.UseDescriptionForTitle =
                true;

            if (!string.IsNullOrWhiteSpace(
                folderTextBox.Text))
            {
                dialog.SelectedPath =
                    folderTextBox.Text;
            }

            if (dialog.ShowDialog(this) !=
                DialogResult.OK)
            {
                return;
            }

            ApplyNewWallpaperFolder(
                dialog.SelectedPath);
        }

        // ============================================================
        // ORDNER ALS SLIDESHOW SETZEN
        // ============================================================

        private void SetWallpaperFolder(
            string path)
        {
            IShellItem? folderItem = null;
            IShellItemArray? folderArray = null;
            IDesktopWallpaper? wallpaper = null;

            try
            {
                Guid shellItemGuid =
                    typeof(IShellItem).GUID;

                int result =
                    SHCreateItemFromParsingName(
                        path,
                        IntPtr.Zero,
                        ref shellItemGuid,
                        out folderItem);

                Marshal.ThrowExceptionForHR(
                    result);

                Guid shellItemArrayGuid =
                    typeof(IShellItemArray).GUID;

                result =
                    SHCreateShellItemArrayFromShellItem(
                        folderItem,
                        ref shellItemArrayGuid,
                        out folderArray);

                Marshal.ThrowExceptionForHR(
                    result);

                wallpaper =
                    (IDesktopWallpaper)
                    new DesktopWallpaper();

                wallpaper.SetSlideshow(
                    folderArray);

                folderTextBox.Text =
                    path;

                SaveLastWallpaperFolder(
                    path);

                UpdateWallpaperCount();
                ApplySlideshowOptions();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Localization.Get("MsgSetWallpaperFolderFailed") +
                    ex.Message,
                    "Wallpaper Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                ReleaseComObject(folderArray);
                ReleaseComObject(folderItem);
                ReleaseComObject(wallpaper);
            }
        }

        // ============================================================
        // AKTUELLES WALLPAPER HOLEN
        // ============================================================

        private string? GetCurrentWallpaperPath()
        {
            IDesktopWallpaper? wallpaper = null;

            try
            {
                wallpaper =
                    (IDesktopWallpaper)
                    new DesktopWallpaper();

                uint monitorCount =
                    wallpaper.GetMonitorDevicePathCount();

                if (monitorCount > 0)
                {
                    string monitorId =
                        wallpaper.GetMonitorDevicePathAt(0);

                    return wallpaper.GetWallpaper(
                        monitorId);
                }

                return wallpaper.GetWallpaper(null);
            }
            catch
            {
                return null;
            }
            finally
            {
                ReleaseComObject(wallpaper);
            }
        }

        // ============================================================
        // PAUSE / FORTSETZEN
        // ============================================================

        private async void PauseButton_Click(
            object? sender,
            EventArgs e)
        {
            if (slideshowPaused)
            {
                await ResumeSlideshowAsync(
                    showError: true);
            }
            else
            {
                PauseSlideshow();
            }
        }

        private void PauseSlideshow()
        {
            string? wallpaperPath =
                GetCurrentWallpaperPath();

            if (string.IsNullOrWhiteSpace(
                wallpaperPath))
            {
                MessageBox.Show(
                    Localization.Get("MsgCurrentWallpaperUnknown"),
                    "Wallpaper Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            IDesktopWallpaper? wallpaper = null;

            try
            {
                wallpaper =
                    (IDesktopWallpaper)
                    new DesktopWallpaper();

                wallpaper.SetWallpaper(
                    null,
                    wallpaperPath);

                slideshowPaused = true;

                CheckSlideshowStatus();
            }
            catch (Exception ex)
            {
                slideshowPaused = false;

                MessageBox.Show(
                    Localization.Get("MsgPauseFailed") +
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

        private async Task<bool> ResumeSlideshowAsync(
            bool showError)
        {
            string? folder =
                LoadLastWallpaperFolder();

            if (string.IsNullOrWhiteSpace(folder))
            {
                if (showError)
                {
                    MessageBox.Show(
                        Localization.Get("MsgWallpaperFolderUnknown"),
                        "Wallpaper Control",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                return false;
            }

            try
            {
                SetWallpaperFolder(folder);

                await Task.Delay(300);

                slideshowPaused = false;

                CheckSlideshowStatus();

                return true;
            }
            catch (Exception ex)
            {
                if (showError)
                {
                    MessageBox.Show(
                        Localization.Get("MsgResumeFailed") +
                        ex.Message,
                        "Wallpaper Control",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }

                return false;
            }
        }

        // ============================================================
        // AKTUELLES BILD FESTLEGEN
        // ============================================================

        private async void PinButton_Click(
            object? sender,
            EventArgs e)
        {
            string? wallpaperPath =
                GetCurrentWallpaperPath();

            if (string.IsNullOrWhiteSpace(
                wallpaperPath))
            {
                MessageBox.Show(
                    Localization.Get("MsgCurrentWallpaperUnknown"),
                    "Wallpaper Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            IDesktopWallpaper? wallpaper = null;

            try
            {
                wallpaper =
                    (IDesktopWallpaper)
                    new DesktopWallpaper();

                slideshowPaused = false;

                wallpaper.SetWallpaper(
                    null,
                    wallpaperPath);

                // Windows braucht einen kurzen Moment, um den
                // Hintergrundmodus von Diashow auf Bild umzustellen.
                await Task.Delay(300);

                CheckSlideshowStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Localization.Get("MsgPinFailed") +
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

        // ============================================================
        // DIASHOW AKTIVIEREN
        // ============================================================

        private async void ActivateButton_Click(
            object? sender,
            EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(
                folderTextBox.Text))
            {
                MessageBox.Show(
                    Localization.Get("MsgSelectWallpaperFolderFirst"),
                    "Wallpaper Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            slideshowPaused = false;

            SetWallpaperFolder(
                folderTextBox.Text);

            // Windows übernimmt den neuen Slideshow-Status nicht
            // synchron. Erst nach kurzer Verzögerung die UI aktualisieren.
            await Task.Delay(300);

            CheckSlideshowStatus();
        }

        // ============================================================
        // WALLPAPER-DARSTELLUNG
        // ============================================================

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
                            item.Key;
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
                        item.Key))
                    {
                        loading = true;

                        try
                        {
                            positionComboBox.SelectedItem =
                                item.Key;
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
                // Eine fehlgeschlagene Hintergrundabfrage soll
                // die Oberfläche nicht stören.
            }
            finally
            {
                ReleaseComObject(wallpaper);
            }
        }

        private void PositionComboBox_SelectedIndexChanged(
            object? sender,
            EventArgs e)
        {
            if (loading)
                return;

            if (positionComboBox.SelectedItem
                is not string selected ||
                !positions.TryGetValue(
                    selected,
                    out DesktopWallpaperPosition position))
            {
                return;
            }

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

                foreach (var item in positions)
                {
                    if (item.Value == actual)
                    {
                        if (!Equals(
                            positionComboBox.SelectedItem,
                            item.Key))
                        {
                            loading = true;
                            positionComboBox.SelectedItem =
                                item.Key;
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

        // ============================================================
        // INTERVALL / SHUFFLE
        // ============================================================

        private void IntervalComboBox_SelectedIndexChanged(
            object? sender,
            EventArgs e)
        {
            if (loading)
                return;

            if (slideshowPaused)
                return;

            ApplySlideshowOptions();
        }

        private void ShuffleCheckBox_CheckedChanged(
            object? sender,
            EventArgs e)
        {
            if (loading)
                return;

            if (slideshowPaused)
                return;

            ApplySlideshowOptions();
        }

        private void ApplySlideshowOptions()
        {
            if (intervalComboBox.SelectedItem
                is not string selected)
            {
                return;
            }

            if (!intervals.TryGetValue(
                selected,
                out uint milliseconds))
            {
                return;
            }

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

        // ============================================================
        // WALLPAPER-NAVIGATION
        // ============================================================

        private void NextWallpaperButton_Click(
            object? sender,
            EventArgs e)
        {
            AdvanceWallpaper(
                DesktopSlideshowDirection.Forward);
        }

        private void AdvanceWallpaper(
            DesktopSlideshowDirection direction)
        {
            if (slideshowPaused)
                return;

            IDesktopWallpaper? wallpaper = null;

            try
            {
                wallpaper =
                    (IDesktopWallpaper)
                    new DesktopWallpaper();

                wallpaper.AdvanceSlideshow(
                    null,
                    direction);

                _ = RefreshCurrentWallpaperSoonAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Localization.Get("MsgAdvanceFailed") +
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

        private async Task RefreshCurrentWallpaperSoonAsync()
        {
            await Task.Delay(300);

            if (!IsDisposed)
            {
                UpdateCurrentWallpaperDisplay();
            }
        }

        // ============================================================
        // AKTUELLES WALLPAPER / EXPLORER
        // ============================================================

        private void UpdateCurrentWallpaperDisplay()
        {
            string? path =
                GetCurrentWallpaperPath();

            if (!string.Equals(
                path,
                lastDisplayedWallpaperPath,
                StringComparison.OrdinalIgnoreCase))
            {
                lastDisplayedWallpaperPath =
                    path;

                currentWallpaperLabel.Text =
                    string.IsNullOrWhiteSpace(path)
                    ? Localization.Get("CurrentWallpaperEmpty")
                    : string.Format(
                        Localization.Get("CurrentWallpaper"),
                        Path.GetFileName(path));

                if (!string.IsNullOrWhiteSpace(path) &&
                    File.Exists(path))
                {
                    RecordWallpaperView(path);
                    AddWallpaperToHistory(path);
                }

                if (wallpaperPreviewForm.Visible)
                {
                    UpdateWallpaperPreview(path);
                }
            }

            bool exists =
                !string.IsNullOrWhiteSpace(path) &&
                File.Exists(path);

            explorerButton.Enabled =
                exists;

            rejectButton.Enabled =
                exists &&
                !slideshowPaused;
        }

        private void RecordWallpaperView(
            string path)
        {
            if (string.Equals(
                path,
                lastCountedWallpaperPath,
                StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            lastCountedWallpaperPath =
                path;

            wallpaperLastShown[path] =
                DateTime.Now;

            if (wallpaperViewCounts.TryGetValue(
                path,
                out int count))
            {
                wallpaperViewCounts[path] =
                    count + 1;
            }
            else
            {
                wallpaperViewCounts[path] = 1;
            }
        }

        private void CurrentWallpaperLabel_MouseEnter(
            object? sender,
            EventArgs e)
        {
            ShowWallpaperPreview();
        }

        private void CurrentWallpaperLabel_MouseLeave(
            object? sender,
            EventArgs e)
        {
            HideWallpaperPreview();
        }

        private void ShowWallpaperPreview()
        {
            string? path =
                GetCurrentWallpaperPath();

            if (string.IsNullOrWhiteSpace(path) ||
                !File.Exists(path))
            {
                return;
            }

            UpdateWallpaperPreview(path);

            Point screenPoint =
                currentWallpaperLabel.PointToScreen(
                    new Point(
                        currentWallpaperLabel.Width + 8,
                        0));

            Screen screen =
                Screen.FromControl(this);

            int x = screenPoint.X;
            int y = screenPoint.Y;

            Rectangle area =
                screen.WorkingArea;

            if (x + wallpaperPreviewForm.Width >
                area.Right)
            {
                x =
                    currentWallpaperLabel
                        .PointToScreen(Point.Empty).X
                    - wallpaperPreviewForm.Width
                    - 8;
            }

            if (y + wallpaperPreviewForm.Height >
                area.Bottom)
            {
                y =
                    area.Bottom
                    - wallpaperPreviewForm.Height;
            }

            if (y < area.Top)
            {
                y = area.Top;
            }

            wallpaperPreviewForm.Location =
                new Point(x, y);

            wallpaperPreviewForm.Show(this);
        }

        private void HideWallpaperPreview()
        {
            if (wallpaperPreviewForm.IsHandleCreated &&
                wallpaperPreviewForm.Visible)
            {
                ShowWindow(
                    wallpaperPreviewForm.Handle,
                    SW_HIDE);
            }
        }

        private void UpdateWallpaperPreview(
            string? path)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                !File.Exists(path))
            {
                HideWallpaperPreview();
                return;
            }

            try
            {
                Image? oldImage =
                    wallpaperPreviewPictureBox.Image;

                using (Image source =
                    Image.FromFile(path))
                {
                    wallpaperPreviewPictureBox.Image =
                        new Bitmap(source);
                }

                oldImage?.Dispose();

                FileInfo fileInfo =
                    new FileInfo(path);

                string resolution =
                    GetImageResolutionText(path);

                string sizeText =
                    FormatFileSize(fileInfo.Length);

                wallpaperPreviewInfoLabel.Text =
                    $"{Path.GetFileName(path)}\n" +
                    $"{resolution}   •   {sizeText}   •   " +
                    fileInfo.LastWriteTime.ToString(
                        "g",
                        Localization.CurrentCulture) + "\n" +
                    path;
            }
            catch
            {
                wallpaperPreviewInfoLabel.Text =
                    path;
            }
        }

        private static string GetImageResolutionText(
            string path)
        {
            try
            {
                using Image image =
                    Image.FromFile(path);

                return
                    $"{image.Width} × {image.Height}";
            }
            catch
            {
                return Localization.Get("NotAvailable");
            }
        }

        private static string FormatFileSize(
            long bytes)
        {
            const double KB = 1024.0;
            const double MB = KB * 1024.0;
            const double GB = MB * 1024.0;

            if (bytes >= GB)
            {
                return
                    $"{bytes / GB:0.##} GB";
            }

            if (bytes >= MB)
            {
                return
                    $"{bytes / MB:0.##} MB";
            }

            if (bytes >= KB)
            {
                return
                    $"{bytes / KB:0.##} KB";
            }

            return string.Format(
                Localization.CurrentCulture,
                Localization.Get(
                    bytes == 1
                    ? "FileSizeByteSingular"
                    : "FileSizeBytePlural"),
                bytes);
        }

        // ============================================================
        // AUSSORTIERT-ORDNER ÖFFNEN
        // ============================================================

        private void OpenRejectedFolder()
        {
            string sourceFolder =
                folderTextBox.Text;

            if (string.IsNullOrWhiteSpace(sourceFolder) ||
                !Directory.Exists(sourceFolder))
            {
                MessageBox.Show(
                    Localization.Get("MsgWallpaperFolderUnavailable"),
                    "Wallpaper Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            string rejectedFolder =
                GetRejectedFolder(
                    sourceFolder);

            try
            {
                Directory.CreateDirectory(
                    rejectedFolder);

                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = rejectedFolder,
                        UseShellExecute = true
                    });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Localization.Get("MsgOpenRejectedFolderFailed") +
                    ex.Message,
                    "Wallpaper Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private string GetRejectedFolder(
            string sourceFolder)
        {
            if (string.IsNullOrWhiteSpace(
                rejectRootFolder))
            {
                return Path.Combine(
                    sourceFolder,
                    "Aussortiert");
            }

            if (!rejectUseSubfolder)
            {
                return rejectRootFolder;
            }

            string normalizedSource =
                sourceFolder.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

            string folderName =
                Path.GetFileName(
                    normalizedSource);

            if (string.IsNullOrWhiteSpace(
                folderName))
            {
                folderName = "Wallpaper";
            }

            return Path.Combine(
                rejectRootFolder,
                folderName);
        }

        // ============================================================
        // WALLPAPER-VERLAUF
        // ============================================================

        private void UpdateHistoryButton()
        {
            int availableCount =
                wallpaperHistory.Count(
                    File.Exists);

            historyButton.Enabled =
                availableCount > 0;

            historyButton.Text =
                availableCount > 0
                ? string.Format(
                    Localization.Get("HistoryCount"),
                    availableCount)
                : Localization.Get("History");
        }

        private void AddWallpaperToHistory(
            string path)
        {
            wallpaperHistory.RemoveAll(
                item =>
                    string.Equals(
                        item,
                        path,
                        StringComparison.OrdinalIgnoreCase));

            wallpaperHistory.Insert(
                0,
                path);

            while (wallpaperHistory.Count >
                   MaxWallpaperHistory)
            {
                wallpaperHistory.RemoveAt(
                    wallpaperHistory.Count - 1);
            }

            UpdateHistoryButton();
        }

        private void HistoryButton_Click(
            object? sender,
            EventArgs e)
        {
            wallpaperHistory.RemoveAll(
                path => !File.Exists(path));

            UpdateHistoryButton();


            BuildHistoryMenu();

            if (historyMenu.Items.Count == 0)
            {
                return;
            }

            historyMenu.Show(
                historyButton,
                new Point(
                    0,
                    historyButton.Height));
        }

        private void BuildHistoryMenu()
        {
            historyMenu.Items.Clear();

            string? currentPath =
                GetCurrentWallpaperPath();

            for (int i = 0;
                 i < wallpaperHistory.Count;
                 i++)
            {
                string path =
                    wallpaperHistory[i];

                if (!File.Exists(path))
                {
                    continue;
                }

                string fileName =
                    Path.GetFileName(path);

                bool isCurrent =
                    !string.IsNullOrWhiteSpace(currentPath) &&
                    string.Equals(
                        path,
                        currentPath,
                        StringComparison.OrdinalIgnoreCase);

                string text =
                    $"{i + 1}. {fileName}" +
                    (isCurrent
                        ? Localization.Get("HistoryCurrentSuffix")
                        : string.Empty);

                ToolStripMenuItem item =
                    new ToolStripMenuItem(text)
                    {
                        Tag = path
                    };

                item.Click +=
                    HistoryItem_Click;

                item.MouseEnter +=
                    HistoryItem_MouseEnter;

                item.MouseLeave +=
                    HistoryItem_MouseLeave;

                historyMenu.Items.Add(item);
            }

            if (historyMenu.Items.Count == 0)
            {
                historyButton.Enabled = false;
                historyButton.Text = Localization.Get("History");
            }
        }

        private void HistoryItem_MouseEnter(
            object? sender,
            EventArgs e)
        {
            if (sender is not ToolStripMenuItem item ||
                item.Tag is not string path ||
                !File.Exists(path))
            {
                return;
            }

            ShowHistoryWallpaperPreview(path);
        }

        private void HistoryItem_MouseLeave(
            object? sender,
            EventArgs e)
        {
            HideWallpaperPreview();
        }

        private void ShowHistoryWallpaperPreview(
            string path)
        {
            UpdateWallpaperPreview(path);

            Rectangle menuBounds =
                historyMenu.Bounds;

            Screen screen =
                Screen.FromRectangle(menuBounds);

            Rectangle area =
                screen.WorkingArea;

            int x =
                menuBounds.Right + 8;

            int y =
                menuBounds.Top;

            if (x + wallpaperPreviewForm.Width >
                area.Right)
            {
                x =
                    menuBounds.Left
                    - wallpaperPreviewForm.Width
                    - 8;
            }

            if (x < area.Left)
            {
                x = area.Left;
            }

            if (y + wallpaperPreviewForm.Height >
                area.Bottom)
            {
                y =
                    area.Bottom
                    - wallpaperPreviewForm.Height;
            }

            if (y < area.Top)
            {
                y = area.Top;
            }

            wallpaperPreviewForm.Location =
                new Point(x, y);

            // Wichtig: Bei einem geöffneten ContextMenuStrip darf hier
            // kein normales Form.Show() verwendet werden. Das würde das
            // Menü als "außerhalb geklickt/aktiviert" behandeln und schließen.
            // Stattdessen zeigen wir das Vorschaufenster direkt per Win32
            // ohne Aktivierung an.
            ShowWindow(
                wallpaperPreviewForm.Handle,
                SW_SHOWNOACTIVATE);
        }

        private void HistoryItem_Click(
            object? sender,
            EventArgs e)
        {
            if (sender is not ToolStripMenuItem item ||
                item.Tag is not string path ||
                !File.Exists(path))
            {
                return;
            }

            try
            {
                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true
                    });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Localization.Get("MsgOpenHistoryImageFailed") +
                    ex.Message,
                    "Wallpaper Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void ExplorerButton_Click(
            object? sender,
            EventArgs e)
        {
            ShowCurrentWallpaperInExplorer();
        }

        private void ShowCurrentWallpaperInExplorer()
        {
            string? path =
                GetCurrentWallpaperPath();

            if (string.IsNullOrWhiteSpace(path) ||
                !File.Exists(path))
            {
                MessageBox.Show(
                    Localization.Get("MsgCurrentWallpaperNotFound"),
                    "Wallpaper Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            try
            {
                string? folder =
                    Path.GetDirectoryName(path);

                if (string.IsNullOrWhiteSpace(folder) ||
                    !Directory.Exists(folder))
                {
                    return;
                }

                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = folder,
                        UseShellExecute = true
                    });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Localization.Get("MsgFileManagerFailed") +
                    ex.Message,
                    "Wallpaper Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        // ============================================================
        // WALLPAPER ÖFFNEN
        // ============================================================

        private void CurrentWallpaperLabel_Click(object? sender, EventArgs e)
        {
            string? path =
                GetCurrentWallpaperPath();

            if (string.IsNullOrWhiteSpace(path) ||
                !File.Exists(path))
            {
                return;
            }

            try
            {
                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true
                    });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Localization.Get("MsgOpenImageFailed") +
                    ex.Message,
                    "Wallpaper Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        // ============================================================
        // WALLPAPER AUSSORTIEREN
        // ============================================================

        private async void RejectButton_Click(
            object? sender,
            EventArgs e)
        {
            await RejectCurrentWallpaperAsync();
        }

        private async Task RejectCurrentWallpaperAsync()
        {
            if (slideshowPaused)
                return;

            string? path =
                GetCurrentWallpaperPath();

            if (string.IsNullOrWhiteSpace(path) ||
                !File.Exists(path))
            {
                MessageBox.Show(
                    Localization.Get("MsgCurrentWallpaperNotFound"),
                    "Wallpaper Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            try
            {
                string? sourceFolder =
                    Path.GetDirectoryName(path);

                if (string.IsNullOrWhiteSpace(
                    sourceFolder))
                {
                    return;
                }

                AdvanceWallpaper(
                    DesktopSlideshowDirection.Forward);

                await Task.Delay(600);

                string rejectFolder =
                    GetRejectedFolder(
                        sourceFolder);

                Directory.CreateDirectory(
                    rejectFolder);

                string destination =
                    GetUniqueDestinationPath(
                        rejectFolder,
                        Path.GetFileName(path));

                File.Move(
                    path,
                    destination);

                lastRejectedSourcePath = path;
                lastRejectedDestinationPath = destination;
                undoRejectButton.Enabled = true;

                UpdateWallpaperCount();
                UpdateHistoryButton();
                UpdateCurrentWallpaperDisplay();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Localization.Get("MsgRejectFailed") +
                    ex.Message,
                    "Wallpaper Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void UndoRejectButton_Click(
            object? sender,
            EventArgs e)
        {
            UndoLastReject();
        }

        private void UndoLastReject()
        {
            if (string.IsNullOrWhiteSpace(lastRejectedSourcePath) ||
                string.IsNullOrWhiteSpace(lastRejectedDestinationPath))
            {
                undoRejectButton.Enabled = false;
                return;
            }

            if (!File.Exists(lastRejectedDestinationPath))
            {
                lastRejectedSourcePath = null;
                lastRejectedDestinationPath = null;
                undoRejectButton.Enabled = false;
                return;
            }

            try
            {
                string restorePath =
                    lastRejectedSourcePath;

                if (File.Exists(restorePath))
                {
                    string? restoreFolder =
                        Path.GetDirectoryName(restorePath);

                    if (string.IsNullOrWhiteSpace(restoreFolder))
                    {
                        return;
                    }

                    restorePath =
                        GetUniqueDestinationPath(
                            restoreFolder,
                            Path.GetFileName(restorePath));
                }

                File.Move(
                    lastRejectedDestinationPath,
                    restorePath);

                lastRejectedSourcePath = null;
                lastRejectedDestinationPath = null;
                undoRejectButton.Enabled = false;

                UpdateWallpaperCount();
                UpdateHistoryButton();
                UpdateCurrentWallpaperDisplay();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Localization.Get("MsgUndoRejectFailed") +
                    ex.Message,
                    "Wallpaper Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static string GetUniqueDestinationPath(
            string folder,
            string fileName)
        {
            string destination =
                Path.Combine(
                    folder,
                    fileName);

            if (!File.Exists(destination))
            {
                return destination;
            }

            string name =
                Path.GetFileNameWithoutExtension(
                    fileName);

            string extension =
                Path.GetExtension(
                    fileName);

            int number = 2;

            do
            {
                destination =
                    Path.Combine(
                        folder,
                        $"{name} ({number}){extension}");

                number++;
            }
            while (File.Exists(destination));

            return destination;
        }

        // ============================================================
        // TRAY
        // ============================================================

        private async void TrayPauseItem_Click(
            object? sender,
            EventArgs e)
        {
            if (slideshowPaused)
            {
                await ResumeSlideshowAsync(
                    showError: true);
            }
            else
            {
                PauseSlideshow();
            }

            UpdateCurrentWallpaperDisplay();
            UpdateTrayPauseText();
        }

        private void RestoreFromTray()
        {
            if (IsDisposed ||
                Disposing)
            {
                return;
            }

            // Nicht direkt innerhalb des NotifyIcon-Events restaurieren.
            // WinForms kann dabei sonst in einen ungünstigen Sichtbarkeits-
            // bzw. Resize-Zustand geraten.
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed ||
                    Disposing)
                {
                    return;
                }

                restoringFromTray = true;

                try
                {
                    ShowInTaskbar = true;

                    if (!Visible)
                    {
                        Show();
                    }

                    WindowState =
                        FormWindowState.Normal;

                    // Windows explizit aus dem minimierten Zustand holen.
                    ShowWindow(
                        Handle,
                        SW_RESTORE);

                    BringToFront();
                    Activate();

                    SetForegroundWindow(
                        Handle);

                    UpdateCurrentWallpaperDisplay();
                }
                finally
                {
                    restoringFromTray = false;
                }
            }));
        }

        private void UpdateTrayPauseText()
        {
            trayPauseItem.Text =
                slideshowPaused
                ? Localization.Get("ResumeSlideshow")
                : Localization.Get("PauseSlideshow");
        }

        // ============================================================
        // AUTOSTART
        // ============================================================

        private void LoadAutostartState()
        {
            try
            {
                using RegistryKey? key =
                    Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Run");

                string? value =
                    key?.GetValue(
                        "WallpaperControl")
                    as string;

                autostartEnabled =
                    !string.IsNullOrWhiteSpace(
                        value);
            }
            catch
            {
                autostartEnabled =
                    false;
            }
        }

        private void LoadCloseToTraySetting()
        {
            try
            {
                using RegistryKey? key =
                    Registry.CurrentUser.OpenSubKey(
                        AppRegistryPath);

                object? value =
                    key?.GetValue(
                        "CloseToTray");

                closeToTrayEnabled =
                    value == null ||
                    Convert.ToInt32(value) != 0;
            }
            catch
            {
                closeToTrayEnabled = true;
            }
        }

        private void SaveCloseToTraySetting(
            bool enabled)
        {
            try
            {
                using RegistryKey key =
                    Registry.CurrentUser.CreateSubKey(
                        AppRegistryPath);

                key.SetValue(
                    "CloseToTray",
                    enabled ? 1 : 0,
                    RegistryValueKind.DWord);
            }
            catch
            {
            }
        }

        private int LoadWindowOpacityPercent()
        {
            try
            {
                using RegistryKey? key =
                    Registry.CurrentUser.OpenSubKey(
                        AppRegistryPath);

                object? value =
                    key?.GetValue(
                        "WindowOpacity");

                if (value != null)
                {
                    return Math.Clamp(
                        Convert.ToInt32(value),
                        80,
                        100);
                }
            }
            catch
            {
            }

            return 92;
        }

        private void SaveWindowOpacityPercent(
            int value)
        {
            try
            {
                using RegistryKey key =
                    Registry.CurrentUser.CreateSubKey(
                        AppRegistryPath);

                key.SetValue(
                    "WindowOpacity",
                    Math.Clamp(value, 80, 100),
                    RegistryValueKind.DWord);
            }
            catch
            {
            }
        }

        private void SetAutostart(
            bool enabled)
        {
            try
            {
                using RegistryKey key =
                    Registry.CurrentUser.CreateSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Run");

                if (enabled)
                {
                    key.SetValue(
                        "WallpaperControl",
                        "\"" +
                        Application.ExecutablePath +
                        "\" --tray",
                        RegistryValueKind.String);
                }
                else
                {
                    key.DeleteValue(
                        "WallpaperControl",
                        false);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Localization.Get("MsgAutostartFailed") +
                    ex.Message,
                    "Wallpaper Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

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

        // ============================================================
        // EINSTELLUNGEN / HOTKEYS
        // ============================================================

        private void StatisticsButton_Click(
            object? sender,
            EventArgs e)
        {
            using StatisticsForm dialog =
                new StatisticsForm(
                    darkMode,
                    windowOpacityPercent,
                    wallpaperViewCounts,
                    wallpaperLastShown,
                    statisticsStartedAt);

            dialog.ShowDialog(this);
        }

        private void AboutButton_Click(
            object? sender,
            EventArgs e)
        {
            using AboutForm dialog =
                new AboutForm(
                    darkMode,
                    windowOpacityPercent);

            dialog.ShowDialog(this);
        }

        private void SettingsButton_Click(
            object? sender,
            EventArgs e)
        {
            using SettingsForm dialog =
                new SettingsForm(
                    darkMode,
                    hotkeyNextModifiers,
                    hotkeyNextKey,
                    hotkeyPauseModifiers,
                    hotkeyPauseKey,
                    hotkeyExplorerModifiers,
                    hotkeyExplorerKey,
                    hotkeyRejectModifiers,
                    hotkeyRejectKey,
                    rejectRootFolder,
                    rejectUseSubfolder,
                    autostartEnabled,
                    closeToTrayEnabled,
                    windowOpacityPercent);

            string languageBefore =
                Localization.CurrentLanguage;

            if (dialog.ShowDialog(this) !=
                DialogResult.OK)
            {
                return;
            }

            bool languageChanged =
                !string.Equals(
                    languageBefore,
                    Localization.CurrentLanguage,
                    StringComparison.OrdinalIgnoreCase);

            hotkeyNextModifiers =
                dialog.NextModifiers;

            hotkeyNextKey =
                dialog.NextKey;

            hotkeyPauseModifiers =
                dialog.PauseModifiers;

            hotkeyPauseKey =
                dialog.PauseKey;

            hotkeyExplorerModifiers =
                dialog.ExplorerModifiers;

            hotkeyExplorerKey =
                dialog.ExplorerKey;

            hotkeyRejectModifiers =
                dialog.RejectModifiers;

            hotkeyRejectKey =
                dialog.RejectKey;

            rejectRootFolder =
                dialog.RejectRootFolder;

            rejectUseSubfolder =
                dialog.RejectUseSubfolder;

            bool newAutostartEnabled =
                dialog.AutostartEnabled;

            bool newCloseToTrayEnabled =
                dialog.CloseToTrayEnabled;

            int newWindowOpacityPercent =
                dialog.WindowOpacityPercent;

            SaveHotkeySettings();
            SaveRejectSettings();

            if (newAutostartEnabled !=
                autostartEnabled)
            {
                SetAutostart(
                    newAutostartEnabled);

                autostartEnabled =
                    newAutostartEnabled;
            }

            if (newCloseToTrayEnabled !=
                closeToTrayEnabled)
            {
                closeToTrayEnabled =
                    newCloseToTrayEnabled;

                SaveCloseToTraySetting(
                    closeToTrayEnabled);
            }

            if (newWindowOpacityPercent !=
                windowOpacityPercent)
            {
                windowOpacityPercent =
                    newWindowOpacityPercent;

                Opacity =
                    windowOpacityPercent / 100.0;

                SaveWindowOpacityPercent(
                    windowOpacityPercent);
            }

            if (IsHandleCreated)
            {
                UnregisterHotKeys();
                RegisterHotKeys();
            }

            if (languageChanged)
            {
                ApplyLocalization();
            }
        }

        private void ApplyLocalization()
        {
            uint selectedInterval = 300000;

            if (intervalComboBox.SelectedItem
                is string selectedIntervalText &&
                intervals.TryGetValue(
                    selectedIntervalText,
                    out uint intervalValue))
            {
                selectedInterval = intervalValue;
            }

            DesktopWallpaperPosition selectedPosition =
                lastWallpaperPosition ??
                DesktopWallpaperPosition.Fill;

            if (positionComboBox.SelectedItem
                is string selectedPositionText &&
                positions.TryGetValue(
                    selectedPositionText,
                    out DesktopWallpaperPosition positionValue))
            {
                selectedPosition = positionValue;
            }

            bool previousLoading = loading;
            loading = true;

            try
            {
                intervals.Clear();
                intervals.Add(Localization.Get("Interval1Minute"), 60000);
                intervals.Add(Localization.Get("Interval2Minutes"), 120000);
                intervals.Add(Localization.Get("Interval3Minutes"), 180000);
                intervals.Add(Localization.Get("Interval5Minutes"), 300000);
                intervals.Add(Localization.Get("Interval10Minutes"), 600000);
                intervals.Add(Localization.Get("Interval15Minutes"), 900000);
                intervals.Add(Localization.Get("Interval30Minutes"), 1800000);
                intervals.Add(Localization.Get("Interval1Hour"), 3600000);
                intervals.Add(Localization.Get("Interval6Hours"), 21600000);
                intervals.Add(Localization.Get("Interval1Day"), 86400000);

                intervalComboBox.Items.Clear();

                foreach (string item in intervals.Keys)
                {
                    intervalComboBox.Items.Add(item);
                }

                foreach (var item in intervals)
                {
                    if (item.Value == selectedInterval)
                    {
                        intervalComboBox.SelectedItem =
                            item.Key;
                        break;
                    }
                }

                positions.Clear();
                positions.Add(Localization.Get("PositionFill"), DesktopWallpaperPosition.Fill);
                positions.Add(Localization.Get("PositionFit"), DesktopWallpaperPosition.Fit);
                positions.Add(Localization.Get("PositionStretch"), DesktopWallpaperPosition.Stretch);
                positions.Add(Localization.Get("PositionTile"), DesktopWallpaperPosition.Tile);
                positions.Add(Localization.Get("PositionCenter"), DesktopWallpaperPosition.Center);
                positions.Add(Localization.Get("PositionSpan"), DesktopWallpaperPosition.Span);

                positionComboBox.Items.Clear();

                foreach (string item in positions.Keys)
                {
                    positionComboBox.Items.Add(item);
                }

                foreach (var item in positions)
                {
                    if (item.Value == selectedPosition)
                    {
                        positionComboBox.SelectedItem =
                            item.Key;
                        break;
                    }
                }
            }
            finally
            {
                loading = previousLoading;
            }

            activateButton.Text =
                Localization.Get("ActivateSlideshow");

            folderLabel.Text =
                Localization.Get("WallpaperFolder");

            intervalLabel.Text =
                Localization.Get("WallpaperInterval");

            shuffleCheckBox.Text =
                Localization.Get("Shuffle");

            positionLabel.Text =
                Localization.Get("WallpaperPosition");

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

        private void UpdateHistoryButtonText()
        {
            historyButton.Text =
                wallpaperHistory.Count > 0
                ? string.Format(
                    Localization.CurrentCulture,
                    Localization.Get("HistoryCount"),
                    wallpaperHistory.Count)
                : Localization.Get("History");
        }

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

        private void LoadRejectSettings()
        {
            try
            {
                using RegistryKey? key =
                    Registry.CurrentUser.OpenSubKey(
                        AppRegistryPath);

                if (key == null)
                {
                    return;
                }

                rejectRootFolder =
                    key.GetValue(
                        "RejectRootFolder")
                    as string ?? "";

                object? subfolderValue =
                    key.GetValue(
                        "RejectUseSubfolder");

                if (subfolderValue != null)
                {
                    rejectUseSubfolder =
                        Convert.ToInt32(
                            subfolderValue) != 0;
                }
            }
            catch
            {
            }
        }

        private void SaveRejectSettings()
        {
            try
            {
                using RegistryKey key =
                    Registry.CurrentUser.CreateSubKey(
                        AppRegistryPath);

                key.SetValue(
                    "RejectRootFolder",
                    rejectRootFolder,
                    RegistryValueKind.String);

                key.SetValue(
                    "RejectUseSubfolder",
                    rejectUseSubfolder ? 1 : 0,
                    RegistryValueKind.DWord);
            }
            catch
            {
            }
        }

        private void LoadHotkeySettings()
        {
            try
            {
                using RegistryKey? key =
                    Registry.CurrentUser.OpenSubKey(
                        AppRegistryPath);

                if (key == null)
                {
                    return;
                }

                hotkeyNextModifiers =
                    ReadRegistryUInt(
                        key,
                        "HotkeyNextModifiers",
                        MOD_CONTROL | MOD_ALT);

                hotkeyNextKey =
                    ReadRegistryUInt(
                        key,
                        "HotkeyNextKey",
                        VK_RIGHT);

                hotkeyPauseModifiers =
                    ReadRegistryUInt(
                        key,
                        "HotkeyPauseModifiers",
                        MOD_CONTROL | MOD_ALT);

                hotkeyPauseKey =
                    ReadRegistryUInt(
                        key,
                        "HotkeyPauseKey",
                        VK_P);

                hotkeyExplorerModifiers =
                    ReadRegistryUInt(
                        key,
                        "HotkeyExplorerModifiers",
                        MOD_CONTROL | MOD_ALT);

                hotkeyExplorerKey =
                    ReadRegistryUInt(
                        key,
                        "HotkeyExplorerKey",
                        VK_E);

                hotkeyRejectModifiers =
                    ReadRegistryUInt(
                        key,
                        "HotkeyRejectModifiers",
                        MOD_CONTROL | MOD_ALT);

                hotkeyRejectKey =
                    ReadRegistryUInt(
                        key,
                        "HotkeyRejectKey",
                        VK_R);
            }
            catch
            {
            }
        }

        private static uint ReadRegistryUInt(
            RegistryKey key,
            string name,
            uint defaultValue)
        {
            object? value =
                key.GetValue(name);

            if (value == null)
            {
                return defaultValue;
            }

            try
            {
                return Convert.ToUInt32(value);
            }
            catch
            {
                return defaultValue;
            }
        }

        private void SaveHotkeySettings()
        {
            try
            {
                using RegistryKey key =
                    Registry.CurrentUser.CreateSubKey(
                        AppRegistryPath);

                key.SetValue(
                    "HotkeyNextModifiers",
                    hotkeyNextModifiers,
                    RegistryValueKind.DWord);

                key.SetValue(
                    "HotkeyNextKey",
                    hotkeyNextKey,
                    RegistryValueKind.DWord);

                key.SetValue(
                    "HotkeyPauseModifiers",
                    hotkeyPauseModifiers,
                    RegistryValueKind.DWord);

                key.SetValue(
                    "HotkeyPauseKey",
                    hotkeyPauseKey,
                    RegistryValueKind.DWord);

                key.SetValue(
                    "HotkeyExplorerModifiers",
                    hotkeyExplorerModifiers,
                    RegistryValueKind.DWord);

                key.SetValue(
                    "HotkeyExplorerKey",
                    hotkeyExplorerKey,
                    RegistryValueKind.DWord);

                key.SetValue(
                    "HotkeyRejectModifiers",
                    hotkeyRejectModifiers,
                    RegistryValueKind.DWord);

                key.SetValue(
                    "HotkeyRejectKey",
                    hotkeyRejectKey,
                    RegistryValueKind.DWord);
            }
            catch
            {
            }
        }

        // ============================================================
        // GLOBALE HOTKEYS
        // ============================================================

        private void RegisterHotKeys()
        {
            if (hotkeyNextModifiers != 0 &&
                hotkeyNextKey != 0)
            {
                RegisterHotKey(
                    Handle,
                    HOTKEY_NEXT,
                    hotkeyNextModifiers,
                    hotkeyNextKey);
            }

            if (hotkeyPauseModifiers != 0 &&
                hotkeyPauseKey != 0)
            {
                RegisterHotKey(
                    Handle,
                    HOTKEY_PAUSE,
                    hotkeyPauseModifiers,
                    hotkeyPauseKey);
            }

            if (hotkeyExplorerModifiers != 0 &&
                hotkeyExplorerKey != 0)
            {
                RegisterHotKey(
                    Handle,
                    HOTKEY_EXPLORER,
                    hotkeyExplorerModifiers,
                    hotkeyExplorerKey);
            }

            if (hotkeyRejectModifiers != 0 &&
                hotkeyRejectKey != 0)
            {
                RegisterHotKey(
                    Handle,
                    HOTKEY_REJECT,
                    hotkeyRejectModifiers,
                    hotkeyRejectKey);
            }
        }

        private void UnregisterHotKeys()
        {
            UnregisterHotKey(
                Handle,
                HOTKEY_NEXT);

            UnregisterHotKey(
                Handle,
                HOTKEY_PAUSE);

            UnregisterHotKey(
                Handle,
                HOTKEY_EXPLORER);

            UnregisterHotKey(
                Handle,
                HOTKEY_REJECT);
        }

        protected override void WndProc(
            ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int id =
                    m.WParam.ToInt32();

                switch (id)
                {
                    case HOTKEY_NEXT:
                        AdvanceWallpaper(
                            DesktopSlideshowDirection.Forward);
                        break;

                    case HOTKEY_PAUSE:
                        TogglePauseFromHotkey();
                        break;

                    case HOTKEY_EXPLORER:
                        ShowCurrentWallpaperInExplorer();
                        break;

                    case HOTKEY_REJECT:
                        if (rejectButton.Enabled)
                        {
                            _ = RejectCurrentWallpaperAsync();
                        }
                        break;
                }

                return;
            }

            base.WndProc(ref m);
        }

        private async void TogglePauseFromHotkey()
        {
            if (slideshowPaused)
            {
                await ResumeSlideshowAsync(
                    showError: true);
            }
            else
            {
                PauseSlideshow();
            }

            UpdateCurrentWallpaperDisplay();
            UpdateTrayPauseText();
        }

        private static void ReleaseComObject(
            object? comObject)
        {
            if (comObject != null &&
                Marshal.IsComObject(comObject))
            {
                Marshal.ReleaseComObject(
                    comObject);
            }
        }

        // ============================================================
        // WINDOWS
        // ============================================================

        private const int
            DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        private const int
            SW_RESTORE = 9;

        private const int
            SW_SHOWNOACTIVATE = 4;

        private const int
            SW_HIDE = 0;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(
            IntPtr hWnd,
            int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(
            IntPtr hWnd);

        [DllImport("dwmapi.dll")]
        private static extern int
            DwmSetWindowAttribute(
                IntPtr hwnd,
                int attribute,
                ref int attributeValue,
                int attributeSize);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterHotKey(
            IntPtr hWnd,
            int id,
            uint fsModifiers,
            uint vk);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterHotKey(
            IntPtr hWnd,
            int id);

        [DllImport(
            "shell32.dll",
            CharSet = CharSet.Unicode,
            PreserveSig = true)]
        private static extern int
            SHCreateItemFromParsingName(
                string pszPath,
                IntPtr pbc,
                ref Guid riid,
                [MarshalAs(UnmanagedType.Interface)]
                out IShellItem ppv);

        [DllImport(
            "shell32.dll",
            PreserveSig = true)]
        private static extern int
            SHCreateShellItemArrayFromShellItem(
                [MarshalAs(UnmanagedType.Interface)]
                IShellItem psi,
                ref Guid riid,
                [MarshalAs(UnmanagedType.Interface)]
                out IShellItemArray ppv);
    }
}
