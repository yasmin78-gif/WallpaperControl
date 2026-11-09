# WallpaperControl v2.5.0
### More Control for Your Guest-Friendly Desktop 🛡️

WallpaperControl 2.5.0 makes visitor-folder slideshows easier to control, adds a quick way to copy approved images into your visitor collection, and smooths out visitor wallpaper transitions.

This release also keeps Calendar and Notes & Reminders widgets at their preferred positions after temporary display changes, including those that can occur during graphics driver updates.

## ✨ What's New

### 🎞️ Visitor Slideshow Controls
Control a visitor folder directly from the wallpaper page, using familiar slideshow actions.

- Pause and resume visitor playback
- Choose the slideshow interval and shuffle setting
- Keep the current visitor image pinned by pausing its playback
- Move through visitor media with the existing Next/Previous actions
- Keep the enabled Next Wallpaper desktop widget available while a visitor folder is active
- Use the existing wallpaper layout, transition effect, direction, and duration settings
- Show the visitor folder and current visitor filename on the wallpaper page
- Keep the slideshow controls visible even when your normal configuration uses video wallpaper

Visitor playback uses its own manual pause state and timer. Pausing retains the remaining interval, and resuming continues from that point. Manual changes do not reset the automatic deadline; choosing a different interval starts a new one. Fullscreen suspension remains independent of manual pause.

**Your normal playback stays separate:** Visitor controls do not replace the saved normal wallpaper, playback position, manual pause, or remaining application slideshow time. Image rejection, undoing rejection, and normal wallpaper history remain unavailable in Visitor Mode.

**Folder controls:** These slideshow controls and the Next Wallpaper widget apply to visitor folders. Single-image and single-video visitor sources retain their existing behavior.

### 📁 Copy Images into Your Visitor Folder
Build your guest-friendly collection while browsing your normal wallpapers.

- Use **Copy to visitor folder** for the current image in normal image mode
- Offer the action only when an existing visitor folder is configured
- Hide the action when the visitor source is a single image, a single video, or an unavailable folder
- Preserve the original image without moving or modifying it
- Keep existing destination files and add numbered filenames when names collide
- Avoid duplicating an image that is already in the destination folder
- Reject damaged images before copying
- Make completed copies available to the visitor slideshow without exposing partially written image files

The copy action remains unavailable while Visitor Mode itself is active. You choose which images belong in your visitor collection.

## 🔧 Fixes and Improvements

### 🖼️ Smooth Visitor Transitions
- Fix the white flash before an animation when advancing visitor images
- Retain the existing renderer instead of closing and recreating it for each image
- Keep the previous safe image visible until the renderer is ready
- Reveal newly initialized renderer windows only after their first image has been painted
- Continue updating the safe native Windows wallpaper underneath the rendered image
- Preserve safe wallpaper handoff when exiting or cancelling a pending change

**Crash protection remains active:** A crash or forced termination retains the last successfully applied safe native wallpaper. Video playback continues to save safe snapshots every 15 seconds; an exact final visible video frame cannot be captured after the process has stopped.

### 📍 Calendar and Notes & Reminders Positions
- Preserve preferred positions separately from temporary on-screen fallback positions
- Restore widgets after a temporary resolution reduction and return to the original resolution
- Handle monitor disconnection and reconnection, including monitors with negative desktop coordinates
- Keep position locks effective without losing the original preferred placement
- Continue saving deliberate user moves as the new preferred position

A widget on a disconnected monitor remains reachable on an available screen. It returns to its preferred position when that screen becomes available again, unless you deliberately move it elsewhere.

## 🛠️ Stability and Quality

- Expand regression coverage for visitor playback, real timer ticks, manual/fullscreen pause separation, and image copying
- Check renderer reuse, delayed readiness, cancellation, and native handoff ordering
- Retain existing video and widget behavior
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
