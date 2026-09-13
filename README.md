# 🖼️ Wallpaper Control

**Wallpaper Control** is a lightweight Windows utility for managing and displaying desktop wallpaper slideshows with precise scheduling, animated transitions, statistics, native desktop widgets and additional quality-of-life controls.

It extends the standard Windows wallpaper experience with its own clock-aligned slideshow engine, desktop-rendered transition effects and optional desktop widgets, while integrating cleanly with the Windows desktop and restoring native wallpaper handling when the application exits.

**Current release: v1.8.0**

## ✨ Features

- 🖼️ **Wallpaper slideshow control**
  - Select your wallpaper folder
  - Change the slideshow interval
  - Enable or disable shuffle
  - Switch to the next wallpaper instantly
  - Pause and resume the slideshow
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
  - Optional Chrome-style desktop clock
  - Optional seconds display
  - Localized date formatting
  - Adjustable clock size
  - Optional Next Wallpaper button
  - Widgets can be positioned independently anywhere on the desktop
  - Independent position locking for each widget
  - Widget positions and settings are remembered
  - Live widget preview while changing settings
  - Widgets remain part of the desktop and do not stay above normal application windows
  - Next Wallpaper widget includes a localized tooltip

- 🖥️ **Windows integration**
  - Integrates with native Windows wallpaper APIs while providing its own slideshow timing and transition engine
  - Custom clock-aligned slideshow timing for precise wallpaper changes
  - Manual wallpaper changes do not reset the automatic slideshow schedule
  - Supports different wallpaper display modes
  - Detects external wallpaper changes
  - Opens folders using your configured default file manager
  - Optional automatic startup with Windows
  - Restores native Windows wallpaper handling when Wallpaper Control exits
  - Supports external wallpaper switching with the `--next` command-line argument
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
  - Default Reject hotkey: `Ctrl+Alt+Shift+R`

- 🔔 **System tray support**
  - Wallpaper Control can continue running in the notification area
  - Double-click the tray icon to restore the window
  - Optional **Close to Tray** behavior when clicking the window's X button
  - Exit the application directly from the tray menu

- 🎨 **Interface & appearance**
  - System, Dark and Light theme selection
  - System theme automatically follows the Windows app theme
  - Adjustable window opacity
  - Remembers window position
  - Drag & drop support
  - Reorganized settings interface
  - Separate appearance reset
  - Localized interface

## 🕐 Desktop Widgets

Wallpaper Control 1.8.0 introduces optional native desktop widgets that integrate directly with the Windows desktop.

### Clock

The desktop clock provides:

- Chrome-style rendering
- Hours and minutes display
- Optional seconds
- Localized date formatting
- Adjustable size
- Free positioning
- Optional position locking

The clock automatically follows the language selected in Wallpaper Control.

### Next Wallpaper

The Next Wallpaper widget provides a compact desktop button for immediately advancing to the next wallpaper.

It can be positioned and locked independently from the clock and uses the same wallpaper switching and transition handling as the main application.

Widget changes are previewed immediately in the settings window. They are permanently applied when the settings are saved. Cancelling the settings restores the previous widget state and position.

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
- No separate .NET installation required when using the self-contained release

## 🚀 Installation

1. Download `WallpaperControl.exe` from the latest release.
2. Start `WallpaperControl.exe`.
3. Select your wallpaper folder.
4. Configure the slideshow, desktop widgets and optional features to your liking.

No installer or separate .NET installation is required.

## 🔒 Privacy

Wallpaper Control works locally on your computer.

It does not require an account, cloud service or online connection to manage your wallpapers.

Wallpaper statistics are stored locally in the user's application data folder.

Diagnostic logs are also stored locally and are only created when needed for troubleshooting.

## 🛠️ Built With

- C#
- .NET 10
- Windows Forms
- Native Windows APIs / COM integration

## 📄 License

Copyright (c) 2026 Yasmin Mahr

Wallpaper Control is free and open-source software licensed under the **GNU General Public License v3.0 (GPL-3.0)**.

You are free to use, study, modify and redistribute Wallpaper Control under the terms of the GNU General Public License v3.0.

See the `LICENSE` file for the full license text.

---

**Wallpaper Control**  
A little more control over what Windows puts on your desktop. 🖼️
