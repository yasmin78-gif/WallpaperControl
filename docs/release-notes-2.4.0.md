# WallpaperControl v2.4.0
### A Guest-Friendly Desktop, One Toggle Away 🛡️

WallpaperControl 2.4.0 introduces Visitor Mode: switch to a separate set of guest-friendly wallpapers, then return to your normal playback when your visitors leave.

This release also adds a configurable Visitor Mode desktop widget and keeps a safe static wallpaper visible when WallpaperControl exits.

## ✨ What's New

### 🛡️ Visitor Mode
Choose what visitors see without changing your normal wallpaper collection.

- Use a single image, an MP4 video, or a separate media folder
- Configure the visitor folder's slideshow interval and shuffle setting
- Switch Visitor Mode on or off from its settings page, the wallpaper page, a configurable global hotkey, or the desktop widget
- Prevent normal wallpaper changes and scheduled actions from replacing visitor media while the mode is active
- Keep Visitor Mode active across application restarts
- Initialize the saved mode before normal wallpaper playback can become visible
- Use a neutral background when visitor media is missing or damaged, without falling back to the normal wallpaper collection

### ↩️ Return to Your Previous Wallpaper
- Restore the previous normal wallpaper immediately when Visitor Mode is switched off
- Preserve manual slideshow pauses independently of other pause reasons
- Continue an active slideshow using its saved remaining timer time
- Restore video position and manual pause state where supported by the saved state and decoder
- Retain the normal playback snapshot across application restarts

**Restoration:** Missing original files and decoder limitations can affect recovery. After a restart, Windows does not expose the exact previous native slideshow shuffle queue or remaining native timer time.

### ◉ Visitor Mode Desktop Widget
- Toggle the same shared mode directly from the desktop
- Choose Minimal, Clean, or Glow styling
- Customize size, title font, colors, and background opacity
- Preview changes live, then save or discard them
- Hide the title for a compact toggle-only layout
- Drag the compact widget directly by its toggle without switching the mode accidentally
- Lock the position independently and retain widget settings after restarting
- Scale the widget and its click target together with monitor DPI

## 🖼️ A Safe Wallpaper After Exit

- Keep the last displayed visitor image as a native static Windows wallpaper
- Retain an app-owned copy even if the original image is later changed or deleted
- Prefer the current decoded video frame when exiting during video playback
- Fall back to a visitor-video thumbnail, a suitable saved safe frame, or a neutral background if frame capture is unavailable
- Hand the static image to Windows before releasing the video engine and desktop host
- Retain the active Visitor Mode setting for the next launch

Video playback also saves a native safe snapshot after starting and every 15 seconds. After a crash or forced process termination, Windows retains the last successfully applied safe snapshot; the exact final visible frame cannot be captured after the process has stopped. The initial native handoff must have succeeded for this protection to be available.

Windows can display the retained static wallpaper before WallpaperControl starts again, including after a Windows restart. Windows themes, policies, Spotlight, and other wallpaper applications can still change it outside WallpaperControl's control.

**Multiple monitors:** Static visitor images use the native wallpaper path on all monitors. Live MP4 wallpaper playback continues to support one monitor; on unsupported configurations, a safe fallback is used, and a visitor-video still can be extracted for exit.

## 🛠️ Stability and Quality

- Preserve the original slideshow pause state through native wallpaper handoff
- Cancel pending visitor media changes before shutdown or restoration
- Prevent stale frame preparation from replacing a newer wallpaper
- Expand coverage for restart recovery, timer restoration, compact widget interactions, and forced process termination
- Validate frame extraction with the pinned mpv runtime and Windows video thumbnail provider
- Update documentation across English, German, French, Spanish, and Japanese

## 📦 Installation

Download the Windows x64 installer from the assets below.

WallpaperControl uses a per-user installation and does not require administrator privileges. Existing installations can be upgraded while preserving application settings and user data.

Exit WallpaperControl completely from its tray menu before installing, including any portable copy.

**Windows SmartScreen notice:** WallpaperControl is currently distributed without a code-signing certificate. Windows may display a SmartScreen warning when launching the installer.

## 🔐 Verification

A SHA-256 checksum file is provided alongside the installer to verify download integrity.

## ❤️ Thank You

Thank you for using WallpaperControl!

Feedback, suggestions, and bug reports are always welcome.
