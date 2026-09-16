using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Main-window state responsibilities; see README.md in this directory for the code map.
    public partial class MainForm
    {
        // Shared services and owned resources
        private readonly AppSettingsStore appSettings = new();
        private readonly List<Font> ownedFonts = new();
        private readonly WidgetManager widgetManager;
        private const string WindowsSlideshowRegistryPath =
            @"Control Panel\Personalization\Desktop Slideshow";

        // Main-window controls
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
        private readonly Label transitionLabel;
        private readonly ComboBox transitionComboBox;
        private readonly ComboBox transitionDirectionComboBox;
        private readonly Label transitionDurationLabel;
        private readonly ComboBox transitionDurationComboBox;
        private WallpaperTransitionKind selectedTransitionKind =
            WallpaperTransitionKind.DesktopWipe;
        private WallpaperTransitionDirection selectedTransitionDirection =
            WallpaperTransitionDirection.Left;
        private WallpaperZoomMode selectedZoomMode =
            WallpaperZoomMode.In;
        private int selectedTransitionDurationMilliseconds = 2000;
        private readonly Button pauseButton;
        private readonly Button pinButton;
        private readonly Button nextWallpaperButton;
        private readonly Label currentWallpaperLabel;
        private readonly Label mainHeading = new();
        private readonly Label currentHeading = new();
        private readonly Label directionHeading = new();
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

        // History and statistics
        private readonly List<string> wallpaperHistory = new();
        private const int MaxWallpaperHistory = 10;
        private readonly WallpaperStatistics statistics = new();
        private string? lastRejectedSourcePath;
        private string? lastRejectedDestinationPath;

        // Tray and folder observation
        private readonly NotifyIcon trayIcon;
        private readonly ContextMenuStrip trayMenu;
        private readonly ToolStripMenuItem trayPauseItem;
        private readonly System.Windows.Forms.Timer wallpaperRefreshTimer;
        private FileSystemWatcher? wallpaperFolderWatcher;
        private readonly System.Windows.Forms.Timer wallpaperCountDebounceTimer;
        private string? lastDisplayedWallpaperPath;

        // Global shortcut identifiers and preferences
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
        private uint hotkeyRejectModifiers =
            MOD_CONTROL | MOD_ALT | MOD_SHIFT;
        private uint hotkeyRejectKey = VK_R;

        // Rejection preferences
        private string rejectRootFolder = "";
        private bool rejectUseSubfolder = true;

        // Window and slideshow state
        private bool loading = true;
        private bool darkMode;
        private string themeMode = "system";
        private bool slideshowPaused = false;

        // Fullscreen suspension and deferred work
        private readonly FullscreenPausePolicy fullscreenPolicy = new();
        private bool pauseOnFullscreen;
        private bool fullscreenUpdateRunning;
        private bool nativeSlideshowAutoPaused;
        private IShellItemArray? fullscreenSavedSlideshow;
        private DesktopSlideshowOptions fullscreenSavedOptions;
        private uint fullscreenSavedInterval;
        private bool deferredUpdateCheck;
        private bool deferredWallpaperCount;
        private CancellationTokenSource? automaticUpdateCancellation;

        // Application-owned slideshow scheduling and rendering
        private bool customSlideshowEngineActive = false;
        private bool customSlideshowChangeRunning = false;
        private uint customSlideshowLastInterval = 0;
        private DateTime customSlideshowNextChange = DateTime.MaxValue;
        private readonly System.Threading.Timer customSlideshowPreciseTimer;
        private readonly Random customSlideshowRandom = new Random();
        private readonly WallpaperTransitionService wallpaperTransitionService =
            new WallpaperTransitionService();

        // Window lifetime, startup, and update preferences
        private bool closingAfterPauseResume = false;
        private bool restoringFromTray = false;
        private bool autostartEnabled = false;
        private bool closeToTrayEnabled = true;
        private bool automaticUpdateCheckEnabled = true;
        private readonly System.Windows.Forms.Timer automaticUpdateCheckTimer;
        private bool automaticUpdateCheckRunning = false;
        private bool exitRequested = false;
        private int windowOpacityPercent = 92;

        // Localized selector values
        private sealed record DisplayOption<T>(T Value, string Text)
        {
            /// <summary>Returns the localized label displayed by a selector.</summary>
            public override string ToString() => Text;
        }

        private readonly List<DisplayOption<uint>> intervals =
        [
            new(60000, Localization.Get("Interval1Minute")),
            new(120000, Localization.Get("Interval2Minutes")),
            new(180000, Localization.Get("Interval3Minutes")),
            new(300000, Localization.Get("Interval5Minutes")),
            new(600000, Localization.Get("Interval10Minutes")),
            new(900000, Localization.Get("Interval15Minutes")),
            new(1800000, Localization.Get("Interval30Minutes")),
            new(3600000, Localization.Get("Interval1Hour")),
            new(21600000, Localization.Get("Interval6Hours")),
            new(86400000, Localization.Get("Interval1Day"))
        ];
        private readonly List<DisplayOption<DesktopWallpaperPosition>> positions =
        [
            new(DesktopWallpaperPosition.Fill, Localization.Get("PositionFill")),
            new(DesktopWallpaperPosition.Fit, Localization.Get("PositionFit")),
            new(DesktopWallpaperPosition.Stretch, Localization.Get("PositionStretch")),
            new(DesktopWallpaperPosition.Tile, Localization.Get("PositionTile")),
            new(DesktopWallpaperPosition.Center, Localization.Get("PositionCenter")),
            new(DesktopWallpaperPosition.Span, Localization.Get("PositionSpan"))
        ];

        // Native hotkey ownership and window constants
        private readonly GlobalHotkeyManager hotkeyManager = new();
        private const int
            SW_RESTORE = 9;
        private const int
            SW_SHOWNOACTIVATE = 4;
        private const int
            SW_HIDE = 0;
    }
}
