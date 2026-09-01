# 🖼️ Wallpaper Control

**Wallpaper Control** is a lightweight Windows utility for managing and controlling the built-in Windows desktop wallpaper slideshow.

It adds the controls and quality-of-life features that are missing from the standard Windows wallpaper settings, while continuing to use the native Windows slideshow system.

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

- 📊 **Session statistics**
  - Tracks how often each wallpaper has been displayed
  - Shows when each wallpaper was last displayed
  - Top 10 and complete statistics views
  - Shows when statistics tracking started
  - Hover over a wallpaper name for a preview
  - Click a wallpaper name to open it in your default application

- ⌨️ **Global hotkeys**
  - Next wallpaper
  - Pause / Resume
  - Show current wallpaper in your file manager
  - Reject current wallpaper
  - Hotkeys can be customized or disabled

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

- 🎨 **Interface**
  - Light and dark mode support
  - Adjustable window opacity
  - Remembers window position
  - Drag & drop support
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

- Windows 11
- 64-bit Windows
- No separate .NET installation required when using the self-contained release

## 🚀 Installation

1. Download the latest release.
2. Extract the archive if necessary.
3. Start `WallpaperControl.exe`.
4. Select your wallpaper folder.
5. Configure the slideshow and optional features to your liking.

No installer is required.

## 📊 Statistics

Statistics are intentionally session-based.

Tracking starts when Wallpaper Control is launched and resets when the application is completely closed. Minimizing Wallpaper Control to the system tray does **not** reset the statistics.

This makes it easy to see which wallpapers Windows actually selected during the current session without creating a permanent usage database.

## 🗑️ Rejecting Wallpapers

Don't like the wallpaper currently on screen?

Wallpaper Control can immediately switch to the next wallpaper and move the unwanted image into an `Aussortiert` folder.

The destination can either be located inside the current wallpaper folder or configured as a global rejection folder.

Accidentally rejected the wrong image? The last rejection can be undone during the current session.

## 🔒 Privacy

Wallpaper Control works locally on your computer.

It does not require an account, cloud service or online connection to manage your wallpapers.

## 🛠️ Built With

- C#
- .NET 9
- Windows Forms
- Native Windows APIs / COM integration

## 📄 License

See the `LICENSE` file for licensing information.

---

**Wallpaper Control**  
A little more control over what Windows puts on your desktop. 🖼️
