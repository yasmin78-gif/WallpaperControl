# WallpaperControl v2.6.1 — Android Remote & Visitor Widgets

Control your Windows wallpaper from your Android phone on your private local network. This release includes **WallpaperControl 2.6.1 for Windows** and the signed **WallpaperControl Remote 1.1.7 APK**.

## What's new

- **Android remote control:** Visitor Mode, pause/resume, next image, slideshow interval, shuffle, wallpaper layout and playback of a video already configured on the PC.
- **Secure local pairing:** Scan the PC QR code, compare the confirmation codes and approve the device on the PC. HTTPS/SPKI pinning and individual device credentials protect the connection. Remote control is disabled by default; access can be revoked on the PC.
- **Native Visitor Mode widgets:** Choose a compact **1×1** shield or a **2×1** switch. Activate with one tap without opening the app. Turning Visitor Mode off requires a fresh confirmed state, the separate device permission and a safety confirmation.
- **Clear feedback:** An unknown or stale widget tap only activates or reconfirms Visitor Mode. After confirmation, a short message explains that another tap offers deactivation. Background status is timestamped and may be delayed by Android.
- **Android stability and icon fixes:** Correct native discovery-listener lifetime handling prevents the discovery-shutdown crash. The Android launcher uses the desktop icon, and the diagnostic page remains usable during repeated checks.

## Downloads and updates

- **Windows:** `WallpaperControl-2.6.1-Setup-x64.exe` — self-contained Windows x64 installer, Windows 10 version 1809 or newer. No separate .NET installation is required.
- **Android:** `WallpaperControl.Remote-1.1.7.apk` — Android 12 or newer, ARM64. The app currently has a German interface. Install as an update **without uninstalling** to retain your existing pairing. The productive app is distinct from the old WC Remote Experiment prototype.
- **Integrity:** `SHA256SUMS.txt` covers the installer and APK. Individual `.sha256` files are also included.

The APK retains application ID `de.wallpapercontrol.remote`, its existing release signing identity and update path (versionCode 10). The desktop version and file version are 2.6.1 / 2.6.1.0. Existing productive pairings remain compatible.

On the PC, open **Settings → General → WallpaperControl Remote**, enable the selected private physical network adapter and pair using the QR code. Add either widget from the Android widget picker. No cloud service, router port forwarding, media previews or background command queue is used.

## Validation

- 102 Remote integration/logic checks and 15 Android packaging checks passed.
- The existing 6,302 desktop regression checks passed during development.
- Real Pixel 9 Pro XL / Android 17 checks covered process-cold widget operation, both widget variants, repeated diagnostic requests, icon display and a complete activation/deactivation cycle.
- The Windows installer passed isolated installation, upgrade and uninstallation checks, including payload hashes and autostart handling.

Background updates are subject to Android scheduling; continuous live status while the app is closed is not promised. Extended Doze, VPN/DHCP and long-duration field scenarios remain outside this acceptance.

## Deutsch

Neu sind die sichere Android-Fernsteuerung im Heimnetz und zwei native Besuchermodus-Widgets. Die APK **1.1.7** ist direkt in diesem Release enthalten. Für ein Update bitte **nicht vorher deinstallieren**, damit die Kopplung erhalten bleibt. Bei veraltetem Widgetstatus bestätigt der erste Tipp ausschließlich die Aktivierung; erst der nächste Tipp bietet das Ausschalten mit Sicherheitsabfrage an.

[Einrichtungsanleitung](https://github.com/yasmin78-gif/WallpaperControl/blob/v2.6.1/docs/REMOTE_PRODUCTION_GUIDE.md) · [Technical report and test evidence](https://github.com/yasmin78-gif/WallpaperControl/blob/v2.6.1/docs/REMOTE_PHASE_07_ANDROID_VISITOR_WIDGET_REPORT.md) · [Source code at this release](https://github.com/yasmin78-gif/WallpaperControl/tree/v2.6.1)
