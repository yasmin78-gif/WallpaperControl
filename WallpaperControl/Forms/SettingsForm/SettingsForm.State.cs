using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Settings dialog State members. See README.md in this directory for the code map.
    internal sealed partial class SettingsForm
    {

        // Owned resources and shortcut modifier constants
        private readonly List<Font> ownedFonts = new();
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;

        // Shortcut controls
        private readonly ComboBox nextModifierCombo;
        private readonly ComboBox nextKeyCombo;
        private readonly ComboBox pauseModifierCombo;
        private readonly ComboBox pauseKeyCombo;
        private readonly ComboBox explorerModifierCombo;
        private readonly ComboBox explorerKeyCombo;
        private readonly ComboBox rejectModifierCombo;
        private readonly ComboBox rejectKeyCombo;

        // Rejection, general, appearance, and language controls
        private readonly TextBox rejectRootTextBox;
        private readonly Button rejectRootBrowseButton;
        private readonly CheckBox rejectSubfolderCheckBox;
        private readonly CheckBox autostartCheckBox;
        private readonly CheckBox closeToTrayCheckBox;
        private readonly CheckBox automaticUpdateCheckCheckBox;
        private readonly CheckBox pauseOnFullscreenCheckBox;
        private readonly Button checkForUpdatesButton;
        private readonly ComboBox languageComboBox;
        private readonly ComboBox themeComboBox;
        private readonly TrackBar opacityTrackBar;
        private readonly Label opacityValueLabel;
        private readonly Label hotkeyWarningLabel;
        private readonly Button resetAppearanceButton;

        // Clock controls and previews
        private readonly CheckBox clockEnabledCheckBox;
        private readonly CheckBox clockLockedCheckBox;
        private readonly NumericUpDown clockSizeNumeric;
        private readonly CheckBox clockSecondsCheckBox;
        private readonly ComboBox clockStyleComboBox;
        private readonly ClockSettingsPreview clockSettingsPreview;
        private readonly List<ClockStyleCard> clockStyleCards = new();

        // Sidebar navigation state
        private readonly List<Button> settingsNavigationButtons = new();
        private readonly Dictionary<Button, TabPage> settingsNavigationPages = new();
        private TabControl? settingsTabControl;
        private Panel? settingsNavigationPanel;
        private Button? settingsWidgetsToggleButton;
        private Button? settingsClockNavigationButton;
        private Button? settingsNextNavigationButton;
        private Button? settingsSystemNavigationButton;
        private Button? settingsWeatherNavigationButton;
        private Button? settingsCalendarNavigationButton;
        private Button? settingsAppearanceNavigationButton;
        private Button? settingsLanguageNavigationButton;
        private bool settingsWidgetsExpanded = true;

        // Widget page controls
        private readonly CheckBox nextWidgetEnabledCheckBox;
        private readonly CheckBox nextWidgetLockedCheckBox;
        private readonly ComboBox nextWidgetStyleComboBox;
        private readonly CheckBox systemWidgetEnabledCheckBox;
        private readonly CheckBox systemWidgetLockedCheckBox;
        private readonly ComboBox systemWidgetRefreshComboBox;
        private readonly ComboBox systemWidgetStyleComboBox;
        private readonly CheckBox systemShowCpuCheckBox;
        private readonly CheckBox systemShowRamCheckBox;
        private readonly CheckBox systemShowGpuCheckBox;
        private readonly CheckBox systemShowVramCheckBox;
        private readonly CheckBox systemShowNetworkCheckBox;
        private readonly CheckBox systemShowDrivesCheckBox;
        private readonly CheckBox weatherWidgetEnabledCheckBox;
        private readonly CheckBox weatherWidgetLockedCheckBox;
        private readonly TextBox weatherLocationTextBox;
        private readonly ComboBox weatherWidgetRefreshComboBox;
        private readonly ComboBox weatherWidgetStyleComboBox;
        private readonly CheckBox weatherShowForecastCheckBox;
        private readonly CheckBox calendarWidgetEnabledCheckBox;
        private readonly CheckBox calendarWidgetLockedCheckBox;
        private readonly ComboBox calendarWidgetStyleComboBox;
        private readonly ComboBox calendarMaxEntriesComboBox;
        private readonly CheckBox calendarShowLocationCheckBox;
        private readonly PrivateCalendarTextBox calendarIcsUrlTextBox;
        private readonly PrivateCalendarTextBox calendarHolidayIcsUrlTextBox;
        private readonly ComboBox calendarRefreshComboBox;

        // Original values and live preview coordination
        private readonly WidgetSettings initialWidgetSettings;
        private readonly Action<WidgetSettings>? widgetPreviewChanged;
        private string previewLanguageCode;
        private string previewThemeMode;
        private bool updatingLanguagePreview;
        private bool updatingThemePreview;


        // Accepted dialog values read by MainForm after DialogResult.OK
        public uint NextModifiers { get; private set; }
        public uint NextKey { get; private set; }
        public uint PauseModifiers { get; private set; }
        public uint PauseKey { get; private set; }
        public uint ExplorerModifiers { get; private set; }
        public uint ExplorerKey { get; private set; }
        public uint RejectModifiers { get; private set; }
        public uint RejectKey { get; private set; }
        public string RejectRootFolder { get; private set; } = "";

        public bool RejectUseSubfolder { get; private set; } = true;

        public bool AutostartEnabled { get; private set; }
        public bool CloseToTrayEnabled { get; private set; } = true;

        public bool PauseOnFullscreen { get; private set; } = true;

        public bool AutomaticUpdateCheckEnabled { get; private set; } = true;

        public int WindowOpacityPercent { get; private set; } = 80;

        public string ThemeMode { get; private set; } = "system";

        public WidgetSettings WidgetSettings { get; private set; } = new();
    }
}
