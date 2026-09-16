# Main window code map

`../MainForm.cs` is the entry point for window startup, handle creation, positioning, and shutdown. The files in this directory are responsibility-based parts of the **same** `MainForm` class. They do not create additional forms, threads, or services.

## Where to make changes

| File | Responsibility |
| --- | --- |
| `MainForm.State.cs` | Shared fields, owned resources, selector values, and native constants. |
| `MainForm.Initialization.cs` | Constructor: create controls, connect events, load preferences, and start coordination. |
| `MainForm.Appearance.cs` | Theme, fonts, title bar, and control layout. |
| `MainForm.Localization.cs` | Translated captions, tooltips, and selector labels. |
| `MainForm.Status.cs` | Translate slideshow state into visible control state. |
| `MainForm.Settings.cs` | Settings/about dialogs, accepted preferences, and Windows autostart. |
| `MainForm.Folders.cs` | Folder selection, drag and drop, shell collections, file watching, and image counts. |
| `MainForm.SlideshowOptions.cs` | Interval, shuffle, image positioning, and Windows preference fallback. |
| `MainForm.Slideshow.cs` | Manual pause, resume, pinning, and activation. |
| `MainForm.Scheduler.cs` | Clock-aligned deadlines, one-shot timer, and UI-thread dispatch. |
| `MainForm.Wallpaper.cs` | Current image lookup, next/previous selection, transitions, and display refresh. |
| `MainForm.Transitions.cs` | Effect, direction, zoom, and duration selectors and preferences. |
| `MainForm.Fullscreen.cs` | Automatic suspension, native slideshow capture/restore, and deferred work. |
| `MainForm.Preview.cs` | Hover preview, metadata, opening an image, and Explorer actions. |
| `MainForm.History.cs` | History menu and statistics dialog integration. |
| `MainForm.Rejection.cs` | Rejection destination, moving an image, and undo. |
| `MainForm.Tray.cs` | Tray pause action and restoring the window. |
| `MainForm.Hotkeys.cs` | Shortcut preferences, native message dispatch, and forwarded-instance commands. |
| `MainForm.Updates.cs` | Scheduled release checks and fullscreen-aware notifications. |
| `MainForm.Native.cs` | Windows API declarations and COM-wrapper release. |

## Ownership and ordering

- Keep field initializers in `MainForm.State.cs`. Their relative order is preserved; splitting initializers across partial files would make their cross-file ordering unspecified.
- The constructor deliberately retains direct assignments to readonly controls, timers, and menus. Its order matters: some event handlers can run while selectors are populated. The `loading` guard remains active until preferences are loaded.
- Dispose form-owned fonts, images, watchers, timers, widgets, tray items, and subscriptions in `MainForm.Dispose`. Continue releasing temporary COM wrappers in their existing `finally` blocks.
- File-watcher and precise-timer callbacks must marshal UI work to the window thread. Do not move control access onto a worker thread when editing these areas.
- Manual pause (`slideshowPaused`) and automatic fullscreen suspension (`fullscreenPolicy`) are independent. Resuming from fullscreen must not clear a manual pause.
- The application owns slideshow timing while running. Shutdown restores the native Windows slideshow; preserve the existing transition-host shutdown order.
- Existing services remain the owners of persisted settings, statistics, hotkey registrations, wallpaper rendering, and widgets. This file split organizes form coordination; it does not introduce independent service boundaries between the partial files.

## Verification

From the repository root:

```powershell
dotnet build WallpaperControl/WallpaperControl.csproj
dotnet run --project WallpaperControl.RegressionTests
```

For the structural split, all 254 original top-level class members were compared using C# syntax tokens. Their executable content, attributes, signatures, and field initialization order were unchanged. English documentation and file organization are the intentional differences.

The regression checks do not exercise the live desktop window. Before releasing, smoke-test startup, folder selection, next/pause/resume, fullscreen suspension, transitions, history/preview, rejection/undo, settings cancellation/acceptance, hotkeys, tray restoration, and explicit exit. Use disposable wallpaper copies when testing rejection.
