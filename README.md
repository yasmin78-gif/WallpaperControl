# 🖼️ Wallpaper Control

**Wallpaper Control** is a lightweight Windows utility for managing and displaying desktop wallpaper slideshows with precise scheduling, animated transitions, statistics, native desktop widgets and additional quality-of-life controls.

It extends the standard Windows wallpaper experience with its own clock-aligned slideshow engine, desktop-rendered transition effects and optional desktop widgets, while integrating cleanly with the Windows desktop and restoring native wallpaper handling when the application exits.

**Current release: v1.8.3**

## ✨ Features

- 🖼️ **Wallpaper slideshow control**
  - Select your wallpaper folder
  - Change the slideshow interval
  - Enable or disable shuffle
  - Switch to the next wallpaper instantly
  - Highlighted **Next Wallpaper** action for easier access
  - Dedicated current-wallpaper section with quick actions and full-path tooltip
  - Pause and resume the slideshow
  - Automatic pause while fullscreen applications are active, including on additional monitors
  - Two-second resume delay prevents brief Alt-Tab switches from immediately restarting background activity
  - Manual slideshow pauses remain preserved independently
  - Pin the current wallpaper

- 🎬 **Wallpaper transition effects**
  - Smooth transitions rendered directly on the Windows desktop
  - Wipe with selectable direction: Left, Right, Up, Down or Random
  - Slide with selectable direction: Left, Right, Up, Down or Random
  - Fade
  - Zoom with In and Out variants
  - Split
  - Curtain
  - Random mode selects a different effect for each wallpaper change and randomizes direction or zoom mode where applicable
  - Configurable transition duration
  - Desktop icons and desktop tools remain visible above the transition layer

- 🕐 **Native desktop widgets**
  - Clock widget with 5 selectable themes
  - System monitoring widget
  - Weather widget with optional 3-day forecast
  - Calendar widget with iCalendar / ICS support
  - Optional Next Wallpaper button
  - Widgets can be positioned independently anywhere on the desktop
  - Independent position locking for each widget
  - Widget positions and settings are remembered
  - Live widget preview while changing settings
  - Widgets remain part of the desktop and do not stay above normal application windows

- 🖥️ **Windows integration**
  - Integrates with native Windows wallpaper APIs while providing its own slideshow timing and transition engine
  - Custom clock-aligned slideshow timing for precise wallpaper changes
  - Manual wallpaper changes do not reset the automatic slideshow schedule
  - Supports different wallpaper display modes
  - Detects external wallpaper changes
  - Opens folders using your configured default file manager
  - Optional automatic startup with Windows
  - Restores native Windows wallpaper handling when Wallpaper Control exits
  - Runs as a single instance per Windows user
  - Starting Wallpaper Control again brings the existing window to the foreground
  - Supports external wallpaper switching with the `--next` command-line argument
  - `--next` commands are forwarded securely to the running instance for the current Windows user
  - Exposes the enabled state of the native clock widget for external applications and scripts

- 📊 **Statistics dashboard**
  - Persistent wallpaper statistics across application restarts
  - Tracks views and when each wallpaper was last displayed
  - Time-based statistics for Today, Yesterday, Last 7 Days and Last 30 Days
  - Top 10, Top 25 and complete statistics views
  - Dashboard metrics for most viewed, least viewed and average views
  - Distribution fairness metric
  - Top 10 wallpaper chart
  - Average wallpaper recurrence time
  - Neglected wallpaper analysis
  - Detects wallpapers that have never been displayed
  - Search and sortable columns
  - Wallpaper thumbnails and hover previews
  - Set a wallpaper directly from the statistics window
  - Open wallpapers or their folders from the context menu
  - Remove individual entries or reset all statistics
  - Automatic backup and recovery if the main statistics file cannot be loaded
  - Damaged statistics files are preserved for possible recovery

- 🗑️ **Quick wallpaper rejection**
  - Move unwanted wallpapers to an `Aussortiert` folder with one click
  - Wallpaper rejection is temporarily disabled while a wallpaper transition is running
  - The next wallpaper is fully displayed before the rejected wallpaper is moved
  - Optional global rejection folder
  - Optional subfolders for individual wallpaper collections
  - Undo the last rejection

- 📜 **Wallpaper history**
  - Keeps track of recently displayed wallpapers during the current session
  - Open wallpapers directly in your default image viewer
  - Hover previews for quick identification

- ⌨️ **Global hotkeys**
  - Next wallpaper
  - Pause / Resume
  - Show current wallpaper in your file manager
  - Reject current wallpaper
  - Hotkeys can be customized or disabled
  - Detects duplicate hotkey assignments
  - Warns when Windows cannot register a selected hotkey
  - Hotkeys can be swapped between actions without conflicts from previous assignments
  - Unchanged hotkeys remain registered when other shortcuts are modified
  - Default Reject hotkey: `Ctrl+Alt+Shift+R`

- 🔔 **System tray support**
  - Wallpaper Control can continue running in the notification area
  - Double-click the tray icon to restore the window
  - Optional **Close to Tray** behavior when clicking the window's X button
  - Exit the application directly from the tray menu

- 🎨 **Interface & appearance**
  - Redesigned Settings interface
  - Main application redesigned to match the Settings interface
  - Consistent modern appearance across the application
  - System, Dark and Light theme selection
  - System theme automatically follows the Windows app theme
  - Adjustable window opacity
  - Remembers window position
  - Drag & drop support
  - Reorganized settings interface
  - Settings always open on the **General** tab
  - Refreshed main window with clearer grouping and improved visual hierarchy
  - Dark dropdowns and improved readability for disabled controls
  - Improved keyboard tab order and consistent spacing
  - Separate appearance reset
  - Localized interface

## 🎮 Fullscreen Pause

Wallpaper Control can automatically reduce background activity while a fullscreen application is active.

- Detects fullscreen applications on all connected monitors
- Pauses automatic wallpaper changes and transition animations
- Suspends regular desktop widget refreshes
- Postpones automatic update checks
- Resumes activity after two seconds without a fullscreen application
- Brief Alt-Tab switches therefore do not immediately restart paused activity
- A slideshow paused manually remains paused when fullscreen mode ends
- Fullscreen detection is enabled by default and can be disabled in Settings

## 🔄 Update Checks

Wallpaper Control can check GitHub Releases for newer versions without taking control away from the user.

- Manual update check available from Settings
- Optional automatic check whenever Wallpaper Control starts
- While the application remains running, automatic checks repeat every 24 hours
- Dedicated update dialogs show the installed and latest available versions
- The release page can be opened directly when a newer version is available
- Automatic update checks can be disabled in Settings
- Wallpaper Control **never downloads or installs updates automatically**

If the installed version is already current, automatic checks remain silent. Manual checks always provide feedback.

## 🕐 Desktop Widgets

Wallpaper Control includes native desktop widgets that integrate directly with the Windows desktop.

Widgets can be positioned independently and locked in place. Their positions and settings are remembered between application sessions.

Widget changes are previewed immediately while configuring them in Settings. They are permanently applied when the settings are saved. Cancelling the settings restores the previous widget state and position.

### Clock

The desktop clock provides:

- Hours and minutes display
- Optional seconds
- Localized date formatting
- Adjustable size
- 5 selectable visual themes
- Free positioning
- Optional position locking

The clock automatically follows the language selected in Wallpaper Control.

### 🖥️ System Monitor

The System widget provides an at-a-glance overview of important hardware and system information directly on the desktop.

It can display:

- CPU usage
- CPU temperature
- RAM usage
- GPU usage
- GPU temperature
- VRAM usage
- Network download activity
- Network upload activity
- Drive usage

The widget includes compact graphical usage bars and offers **Minimal**, **Clean** and **Glow** styles.

Hardware monitoring runs asynchronously so that sensor updates do not interfere with wallpaper transitions or the responsiveness of the main application.

### 🌦️ Weather

The Weather widget displays current weather information directly on the desktop.

It can display:

- Current temperature
- Feels-like temperature
- Current weather conditions
- Humidity
- Precipitation
- Wind speed
- Optional 3-day forecast

Weather data is provided by **Open-Meteo** and does not require an API key.

The location can be configured in Settings, and weather information can be refreshed automatically at selectable intervals.

The Weather widget offers **Minimal**, **Clean** and **Glow** styles and can be positioned and locked independently.

### 📅 Calendar

The Calendar widget provides a compact overview of upcoming appointments directly on the desktop.

Calendar data is loaded using read-only **iCalendar / ICS** feeds.

Features include:

- Support for multiple ICS calendar sources
- Timed appointments
- All-day events
- Recurring events
- Multi-day events
- Multi-day all-day events are displayed on every affected day
- Multiple appointments on the same day are grouped together
- Displays upcoming days that actually contain appointments
- Empty days are skipped
- Optional event location display
- Automatic widget sizing based on displayed appointments
- Configurable refresh interval
- **Minimal**, **Clean** and **Glow** styles
- Independent positioning and locking

Private ICS addresses are stored encrypted using **Windows Data Protection API (DPAPI)** for the current Windows user.

Wallpaper Control only reads calendar feeds and does not modify calendar data.

#### 🎌 Holiday Calendars

Separate ICS sources can be configured as holiday calendars.

Holiday events are visually highlighted and automatically placed before normal appointments on the same day, making public holidays and other special calendar entries easier to recognize.

Multiple normal and holiday calendar sources can be combined in the same Calendar widget.

Calendar refreshes are resilient to temporary feed failures. Previously loaded events remain visible when an individual source becomes unavailable, while available feeds continue to update. Wallpaper Control indicates when cached calendar data may be outdated and clears the warning after all configured feeds refresh successfully. Cached events are retained for the current application session.

### Next Wallpaper

The Next Wallpaper widget provides a compact desktop button for immediately advancing to the next wallpaper.

It offers **Minimal**, **Clean** and **Glow** styles. Style changes are shown immediately in the live preview, and the selected style is remembered between application sessions. Existing configurations continue to use the previous **Minimal** appearance by default.

It can be positioned and locked independently from the other widgets and uses the same wallpaper switching and transition handling as the main application.

## 📊 Statistics

Wallpaper Control keeps persistent statistics about wallpapers displayed through its slideshow.

The statistics dashboard can show:

- Total views for each wallpaper
- When a wallpaper was last displayed
- View share and popularity ranking
- Statistics for Today, Yesterday, the Last 7 Days and the Last 30 Days
- Most and least viewed wallpapers
- Average number of views
- Distribution fairness
- Average recurrence time
- A Top 10 chart
- Wallpapers that have never been displayed or have not been shown for a long time

Statistics are stored locally and survive application restarts.

Wallpaper Control keeps the previous statistics file as a backup. If the main statistics file cannot be loaded, it can automatically fall back to the backup while preserving damaged files for possible recovery. Statistics are written through unique temporary files to reduce the risk of save collisions or incomplete replacements.

Time-based statistics and recurrence tracking begin when the corresponding tracking data is first initialized. Historical daily or recurrence data from before tracking began is not reconstructed.

The recent wallpaper history remains session-based and is cleared when Wallpaper Control is completely closed.

## 🗑️ Rejecting Wallpapers

Don't like the wallpaper currently on screen?

Wallpaper Control switches to the next wallpaper first and waits until the wallpaper change has completed before moving the unwanted image into an `Aussortiert` folder. Rejection is temporarily disabled while a wallpaper transition is already running, preventing the currently displayed image from being moved before the transition has finished.

The destination can either be located inside the current wallpaper folder or configured as a global rejection folder.

Accidentally rejected the wrong image? The last rejection can be undone during the current session.

## 🕹️ External Control

Wallpaper Control can receive commands from external applications, scripts, shortcuts or desktop tools while it is already running.

### Next Wallpaper

The following command is supported:

```text
WallpaperControl.exe --next
```

This sends a request to the running Wallpaper Control instance and immediately switches to the next wallpaper.

Wallpaper Control runs as a single instance for the current Windows user. Starting it again normally brings the existing main window to the foreground. Command communication such as `--next` is restricted to the current user, and malformed, oversized or stalled requests are rejected without blocking subsequent commands.

External requests use the same slideshow, scheduling and transition handling as wallpaper changes triggered directly from Wallpaper Control.

This makes it possible to integrate Wallpaper Control with custom scripts, launchers, automation tools or other desktop applications without opening the main window.

### Clock Widget State

External applications can determine whether the native Wallpaper Control clock is enabled by reading:

```text
HKEY_CURRENT_USER\Software\WallpaperControl
```

Registry value:

```text
ClockWidgetEnabled
```

Values:

```text
0 = Native clock widget disabled
1 = Native clock widget enabled
```

This allows external applications or desktop tools to adapt their own behavior depending on whether Wallpaper Control's native clock is enabled.

The registry value represents the saved widget setting. Preview changes made while the settings window is open are not permanently applied until the settings are saved.

## 🩺 Diagnostics

Wallpaper Control includes lightweight diagnostic logging for unexpected errors and failures.

Logs are created only when needed and are stored in:

```text
%APPDATA%\WallpaperControl\Logs
```

The main log file is:

```text
wallpaper-control.log
```

Logging is designed for troubleshooting and does not require any additional configuration during normal use.

## 🌍 Languages

Wallpaper Control currently includes:

- 🇩🇪 German
- 🇬🇧 English
- 🇫🇷 French
- 🇪🇸 Spanish
- 🇯🇵 Japanese

The interface language can be changed directly from the application settings.

Desktop widget text and date formatting follow the selected application language.

## 💻 Requirements

- **Windows 11:** supported and tested
- **Windows 10:** expected to be compatible, currently untested
- 64-bit Windows
- No separate .NET installation required

## 🚀 Installation

The **official installation method** for Wallpaper Control is the Windows x64 installer provided with each release.

1. Download `WallpaperControl-1.8.3-Setup-x64.exe` from the latest GitHub release.
2. Completely exit an existing Wallpaper Control instance from the system tray before installing or upgrading.
3. Run the installer.
4. Optionally select a Desktop shortcut during setup.
5. Start Wallpaper Control from the Start Menu or Desktop shortcut.

Wallpaper Control is installed for the current Windows user and **does not require administrator privileges**.

The installer includes the required **.NET runtime**, so no separate .NET installation is necessary. A Start Menu shortcut is created automatically, while the Desktop shortcut is optional.

When upgrading from an existing installation, Wallpaper Control settings and statistics are preserved. If automatic startup was already enabled, its entry is updated to use the installed application path.

Uninstalling Wallpaper Control removes the application and its shortcuts while preserving user settings and statistics, allowing them to be reused by a later installation.

The installer is currently **not digitally signed**. Windows may therefore display a security warning when the setup file is launched.

## 🔒 Privacy

Wallpaper Control stores its application settings and wallpaper statistics locally on your computer.

No Wallpaper Control account is required.

Most functionality, including wallpaper management, slideshow control, transitions, fullscreen detection and statistics, works entirely locally.

Some optional features require an internet connection:

- The **Weather widget** connects to Open-Meteo to retrieve weather information.
- The **Calendar widget** connects to the configured iCalendar / ICS addresses to retrieve calendar data.
- The optional **Update Check** connects to GitHub Releases to determine whether a newer Wallpaper Control version is available. Automatic checks can be disabled, and Wallpaper Control never downloads or installs updates automatically.

Private ICS addresses configured for the Calendar widget are stored encrypted using the Windows Data Protection API (DPAPI) for the current Windows user.

Calendar access is read-only. Wallpaper Control does not modify appointments or calendar data.

Diagnostic logs are stored locally and are only created when needed for troubleshooting.

## 🛠️ Built With

Developer verification: run `pwsh -File ./Test.ps1` from the repository root for the complete regression suite. See [TESTING.md](TESTING.md) for release/analyzer commands, calendar limits and the manual Windows checklist.

- C#
- .NET 10
- Windows Forms
- Native Windows APIs / COM integration
- Open-Meteo
- iCalendar / ICS

## 📄 License

Copyright (c) 2026 Yasmin Mahr

Wallpaper Control is free and open-source software licensed under the **GNU General Public License v3.0 (GPL-3.0)**.

You are free to use, study, modify and redistribute Wallpaper Control under the terms of the GNU General Public License v3.0.

See the `LICENSE` file for the full license text.

---

**Wallpaper Control**  
A little more control over what Windows puts on your desktop. 🖼️
