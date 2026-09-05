# 🖼️ Wallpaper Control

**Wallpaper Control** is a lightweight Windows utility for managing and controlling the built-in Windows desktop wallpaper slideshow.

It adds the controls and quality-of-life features that are missing from the standard Windows wallpaper settings, while continuing to use the native Windows slideshow system.

**Current release: v1.6.0**

## ✨ Features

- 🖼️ **Wallpaper slideshow control**
  - Select your wallpaper folder
  - Change the slideshow interval
  - Enable or disable shuffle
  - Switch to the next wallpaper instantly
  - Pause and resume the slideshow
  - Pin the current wallpaper

- 🗑️ **Quick wallpaper rejection**
  - Move unwanted wallpapers to an `Aussortiert` folder with one click
  - Optional global rejection folder
  - Optional subfolders for individual wallpaper collections
  - Undo the last rejection

- 📜 **Wallpaper history**
  - Keeps track of recently displayed wallpapers during the current session
  - Open wallpapers directly in your default image viewer
  - Hover previews for quick identification

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

- ⌨️ **Global hotkeys**
  - Next wallpaper
  - Pause / Resume
  - Show current wallpaper in your file manager
  - Reject current wallpaper
  - Hotkeys can be customized or disabled
  - Detects duplicate hotkey assignments
  - Warns when Windows cannot register a selected hotkey
  - Default Reject hotkey: `Ctrl+Alt+Shift+R`

- 🖥️ **Windows integration**
  - Uses the native Windows wallpaper slideshow
  - Supports different wallpaper display modes
  - Detects external wallpaper changes
  - Opens folders using your configured default file manager
  - Optional automatic startup with Windows

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

## 🌍 Languages

Wallpaper Control currently includes:

- 🇩🇪 German
- 🇬🇧 English
- 🇫🇷 French
- 🇪🇸 Spanish
- 🇯🇵 Japanese

The interface language can be changed directly from the application settings.

## 💻 Requirements

- Windows 10 or Windows 11
- 64-bit Windows
- No separate .NET installation required when using the self-contained release

## 🚀 Installation

1. Download `WallpaperControl.exe` from the latest release.
2. Start `WallpaperControl.exe`.
3. Select your wallpaper folder.
4. Configure the slideshow and optional features to your liking.

No installer or separate .NET installation is required.

## 📊 Statistics

Wallpaper Control keeps persistent statistics about the wallpapers selected by the Windows slideshow.

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

Wallpaper Control can immediately switch to the next wallpaper and move the unwanted image into an `Aussortiert` folder.

The destination can either be located inside the current wallpaper folder or configured as a global rejection folder.

Accidentally rejected the wrong image? The last rejection can be undone during the current session.

## 🔒 Privacy

Wallpaper Control works locally on your computer.

It does not require an account, cloud service or online connection to manage your wallpapers.

Wallpaper statistics are stored locally in the user's application data folder.

## 🛠️ Built With

- C#
- .NET 9
- Windows Forms
- Native Windows APIs / COM integration

## 📄 License

Copyright (c) 2026 Yasmin Mahr

Wallpaper Control is free and open-source software licensed under the
**GNU General Public License v3.0 (GPL-3.0)**.

You are free to use, study, modify and redistribute Wallpaper Control
under the terms of the GNU General Public License v3.0.

See the `LICENSE` file for the full license text.

---

**Wallpaper Control**  
A little more control over what Windows puts on your desktop. 🖼️
