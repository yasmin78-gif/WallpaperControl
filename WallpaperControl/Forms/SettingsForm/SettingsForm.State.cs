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

        // Sidebar navigation state
        private readonly List<Button> settingsNavigationButtons = new();
        private readonly Dictionary<Button, TabPage> settingsNavigationPages = new();
        private TabControl? settingsTabControl;
        private Panel? settingsNavigationPanel;
        private Button? settingsAppearanceNavigationButton;
        private Button? settingsLanguageNavigationButton;

        // Application preference preview state
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

    }
}
