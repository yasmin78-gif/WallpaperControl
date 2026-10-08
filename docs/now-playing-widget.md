# Now Playing / Media Control

The optional widget observes Windows GSMTC sessions. WallpaperControl does not
create an audio player, contact a media service, or require a login. Enable it
under **Widgets → Aktuelle Wiedergabe**.

Features:

- Startup queries Windows immediately but keeps the widget hidden until the first
  session is recognized, regardless of the empty-display option. Desktop placement
  creates/attaches its window without revealing it. Later absence uses the grace period.

- Cover, title, artist and album in Minimal/Clean/Glow; 380 × 140 logical pixels.
- Previous, play/pause, next and seek are available only when the selected app
  exposes the corresponding capability. Missing timelines are not fabricated.
- Automatic selection follows Windows' current session. The context menu can
  select an application session for this widget's lifetime.
- An empty widget is 380 × 62 pixels; optionally hide it after 15 seconds without
  a session. Brief gaps retain the previous full metadata, cover and widget size
  in both visibility modes, with frozen progress and inactive controls. A returning session
  cancels the deadline. Repeated empty events do not extend it. Paused sessions remain
  visible. An unavailable Windows API is shown rather than silently hidden.
- Optional mouse wheel changes default Windows multimedia output volume in 2%
  steps. It does not change an app's individual mixer volume or mute state.
- Cover/title clicks try to foreground a running source process. No app is
  launched. Packaged/custom app IDs and individual browser tabs are not generally
  resolvable to a window; Windows can refuse foreground activation.
- Fullscreen/power suspension stops widget work; it never pauses/resumes the
  source app. Returning refreshes the selected session.

## Lifetime and interoperability

The widget owns one UI timer, one decoded 96 × 96 cover, a tooltip and a themed
context menu. Metadata and covers are cached until a media-property change.
The service serializes refreshes, discards obsolete async results and prevents
overlapping transport commands. Closing/disabling unregisters manager/session
events, cancels async work, clears retained session/cover references and disposes
widget resources. Core Audio COM objects are acquired only for volume access
and released in `finally`.

GSMTC returns different wrappers (including different COM identities) from
`GetCurrentSession` and `GetSessions` for the same logical session. Application
IDs associate automatic playback with the inventory. Repeated application IDs
get numbered entries; GSMTC exposes no durable browser-tab identifier, so those
numbers describe the current enumeration and can change when sessions disappear.
Session choice is intentionally not persisted across app restarts.

Both projects target `net10.0-windows10.0.17763.0` to reference the Windows SDK
projection. Windows 10 build 17763 already matches the installer's minimum;
version/installer settings are unchanged. Build output directories therefore
include this more specific TFM. Publishing includes the WinRT projection/runtime
assemblies automatically.

Official references:

- [Desktop WinRT API configuration](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/winrt-apis-desktop-apps)
- [GSMTC session API](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssession)

## Validation and manual checks

Focused tests: `--now-playing-checks`, `--now-playing-native-checks`,
`--widget-navigation-checks`, `--wallpaper-widget-mode-checks` in the regression
project. Native checks are read-only with respect to source playback and volume.
They exercise session enumeration, live metadata/cover/timeline rendering,
selection, cached refreshes, rapid switching, suspension and disposal.

Manual checks with the newly built app (stop the previous running app first):

1. Enable the widget; start Spotify or a browser media session. Verify title,
   artist, cover and progress. An app may omit album or duration.
2. Test previous/play-pause/next and dragging the progress bar where supported.
3. Start a second media app, switch via the menu, then restore automatic selection.
4. Pause the source: the widget should remain visible with a stationary timeline.
5. Close all sessions; check compact and hide-empty modes, then restart playback.
6. Try all styles, dark/light, display scaling, dragging and the lock option.
7. Enable mouse-wheel volume, verify system volume, then disable the option.
8. Test Win+D and fullscreen suspension: music must continue; widget data refreshes
   after returning. Disable/re-enable the widget and restart WallpaperControl.

The native live check was exercised with a Vivaldi YouTube session. Actual
transport/volume changes and foreground activation still need the manual checks
above; the automated live probe deliberately does not interrupt playback.
