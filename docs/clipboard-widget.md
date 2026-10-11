# Clipboard widget

Enable **Widgets → Zwischenablage** explicitly. The widget records new Unicode text and screenshot image changes; existing clipboard contents are not imported when enabled or resumed.

- Keep 5, 10, 20 or 50 text entries. Exact duplicates move to the top without changing their text.
- Click an entry to copy its full text without changing its history position or timestamp. No automatic paste or target application activation occurs.
- Right-click for full text, copy and individual deletion. Header buttons pause recording and clear the widget history.
- Minimal, Clean and Glow support light/dark themes, position locking, maximum height and scrolling.
- Text history stays in process memory and is cleared when disabled or disposed. Screenshot originals and previews are saved locally; clipboard contents are never logged.
- Windows clipboard exclusion/history flags are respected. Copies made by the widget request exclusion from Windows history and cloud clipboard.
- Whitespace-only text and text over 524,288 UTF-16 code units are ignored. Passwords cannot reliably be recognized if the source application does not mark them as excluded.
- Text recording retains its activity/power suspension. Enabled screenshot collection continues during the fullscreen/activity display pause, including capture overlays. Manual recording pause and power suspension disable both. Resuming does not import a backlog.

Clearing this widget does not clear the Windows clipboard or Windows clipboard history. RAM-only storage does not promise secure erasure of immutable managed strings or protection against process dumps.

## Manual verification

1. Start the new build and enable the widget. Choose a capacity and save.
2. Copy ordinary text, multiline code and a URL. Check preview, timestamp and duplicate ordering.
3. Click an entry and paste into a text editor. Confirm the exact original text, including line breaks.
4. Pause recording, copy another text and resume. The paused text must not appear.
5. Fill the history past the selected capacity; verify mouse wheel and thumb dragging at the configured height.
6. Open full text and delete an entry through the context menu. Clear all entries using the header button.
7. Disable/re-enable the widget and restart the application. Text history must be empty; screenshot retention follows its separate settings.
8. Check all three styles and light/dark themes.

## Focused checks

`dotnet run --project WallpaperControl.RegressionTests -c Release -- --clipboard-widget-checks`

These checks use an isolated clipboard source for copying and include passive native listener registration/disposal checks. They do not overwrite the user's Windows clipboard.

## Interaction during modal dialogs

All desktop widgets are independent top-level tool windows without owners. Nevertheless WinForms `ShowDialog()` disables visible windows on the same UI thread, regardless of their owner relationship. The clipboard widget now has a narrowly scoped exception: a thread-modal event queues enabling only its existing HWND after modal setup. Its own `WM_ENABLE` notification handles nested dialogs, which do not raise another outer `EnterThreadModal` event. Other windows and dialog ownership remain unchanged. Explicitly disabled, hidden, disposed or activity/power-suspended clipboard windows are excluded. Modal state subscriptions are removed on disposal, including after widget recreation.

During modal interaction, a clipboard mouse click may activate the widget when focus is needed; outside dialogs the original no-activation behavior is retained. No focus is stolen when the exception enables input. WinForms continues to own dialog closure, cancellation and restoration of the other windows. History, copying, storage and the listener remain unchanged.

Native focused checks open a real launcher editor and a widget-settings dialog, including nested modal windows. They verify native enabled states, click-to-copy routing through the test clipboard backend, wheel scrolling, delete/clear, pause/resume, focus return with unchanged dialog text, cancellation, explicit disabling/suspension and fresh clipboard/widget service creation. For a live clipboard integration check, open an editor, click a history entry and paste into its text field; also exercise the details context menu and repeat after application restart.

The ordering of thread-window disabling and modal enter/leave notifications is documented in the [WinForms implementation](https://source.dot.net/System.Windows.Forms/System/Windows/Forms/Application.ThreadContext.cs.html).

## Screenshots

Capture is enabled by default, with a separate limit of 20 screenshots and a 24-hour lifetime. Settings offer 10/20/50 screenshots, 1 hour/24 hours/7 days/until manually deleted, and keeping screenshots across restart (default enabled). Changing retention limits in an uncommitted settings preview does not delete files; saving applies the new limits.

`%LOCALAPPDATA%\WallpaperControl\Clipboard\Images\` contains original lossless PNGs, small proportional PNG previews, and an atomically replaced `history.json` containing image metadata only. Text is never written to this index. Originals load only for copy or regenerating a missing preview. Screenshots share the chronological view with texts, without sharing their respective count limits. Clicking an image restores the original image and PNG clipboard formats, preserving its place and timestamp.

The listener snapshots PNG/image/png, CF_DIBV5, CF_DIB, or CF_BITMAP on its STA thread. Decode, pixel hashing, PNG encoding, thumbnails and file operations run on a serialized worker queue. UI updates and clipboard writes explicitly return to the owning STA thread. Reads use at most five attempts, with asynchronous delays of 60/120/240/480 ms, for locked or temporarily missing image data; write retries use the existing three attempts at 40 ms. At most three image captures may be pending. Dimensions are limited to 16,384 per side and 24 million pixels; PNG data to 32 MiB and raw clipboard data to 128 MiB. Unsupported or corrupt data is skipped and errors log only their type, without clipboard content.

Cleanup runs every minute and on application startup, including when the widget is disabled. Expired/over-limit/manually deleted screenshots lose both original and preview. Temporary screenshots are marked for removal on the next startup, even after an interrupted index write. Valid persistent PNGs can be recovered after an interrupted index update. Cleanup handles only the widget's own GUID filenames in its image directory, avoids redirected paths, and never recursively removes directories or unrelated files.

Screenshot checks: `dotnet run --project WallpaperControl.RegressionTests -c Release -- --clipboard-screenshot-checks`. They exercise PNG/DIB inputs, alpha, lossless originals, thumbnail sizes, mixed text/image copying with a fake clipboard backend, echo suppression, retention, restart, corrupt files, startup cleanup while disabled, settings and async clear/capture races. Native checks verify rejection of image writes from the wrong thread and bounded retries while another thread briefly locks the real Windows clipboard. They do not overwrite the system clipboard.

Live verification remains separate: take screenshots with **Win+Shift+S**, alternate with copied text, click an image and paste into Paint or another image-capable application; confirm original resolution, and repeat with a launcher dialog open. Test restart with retention enabled and disabled, and a temporarily busy clipboard. The modal interaction implementation is unchanged by screenshot support.

## Native image regression checks

`dotnet run --project WallpaperControl.RegressionTests -c Release -- --clipboard-native-image-checks`

These separately invoked checks publish small synthetic images into the actual Windows clipboard, drive the production HWND listener through `WM_CLIPBOARDUPDATE`, and verify the service, PNG store, thumbnail, widget render and native copy-back reader. They cover 32-bit CF_DIB, CF_DIBV5 with RGB/alpha masks and sRGB color space, PNG, CF_BITMAP, multiple simultaneous formats, malformed preferred PNG fallback, and exclusion flags with values zero and one. Unlike the previous fake-source checks, they reproduced the DIBV5 encoding failure. The test snapshots existing clipboard formats and restores them unless a newer user change is detected; it aborts before writing if existing formats cannot safely be preserved. A running application may observe these small test images as normal clipboard changes.

DIBV5 decoding now copies decoded pixels into a standalone ARGB bitmap while the source stream is open. Bitmap.Clone retained the source stream dependency for DIBV5: encoding worked while the stream was open but failed after it closed. The detached pixel copy allows safe PNG encoding after source disposal. Other available representations are snapshotted within a combined 128 MiB budget on STA and tried in order on the worker if a representation cannot be decoded. Explicit zero monitor-exclusion flags allow processing; explicit opt-outs remain respected.

Image capture initialization creates the image directory even before the first screenshot. Re-enabling capture creates it asynchronously too. Diagnostics record listener registration/configuration, clipboard image formats, successful STA acquisition, format decoding/fallback, PNG storage and history changes. Errors include their stage, type, HRESULT and bounded reason; no clipboard contents or image bytes are logged.

These native tests are automated Windows clipboard integration tests, not interactive tests of the Print Screen or Win+Shift+S UI. Those keyboard workflows still need live verification in the new build.

## Capture overlays and delayed rendering

The live logs showed fullscreen suspension disabling image monitoring during capture overlays and re-enabling it about two seconds later. Resume reset the consumed clipboard sequence to the newly captured screenshot, so it was never imported. Screenshot collection now continues through the generic fullscreen/activity display pause, while text suspension, explicit recording pause and power suspension keep their previous behavior. This does not identify particular screenshot applications or add another listener.

Pending reads retain their clipboard sequence. A new sequence replaces an older pending read, duplicate notifications do not consume the retry budget, and successful rendering consumes the final sequence under the clipboard lock. Five attempts end the retry timer; there is no permanent polling. Temporarily absent image handles, locked data and the empty publication gap may retry asynchronously. Logs include the WM_CLIPBOARDUPDATE event, numeric format IDs, read attempts, Windows error codes, retry delays, superseding sequences and outcomes. Custom format names, texts and pixels are excluded from logs.

Native regression checks additionally cover generic activity-suspended screenshot acquisition, unchanged text suspension, manual/power pause, real WM_RENDERFORMAT publication that only succeeds on a later read, permanently unavailable data, a temporarily locked clipboard, a newer text change replacing a delayed screenshot, text between screenshots, and native copy-back without duplication. The interactive Print Screen / Win+Shift+S workflows still require live verification in the updated build; the automated tests simulate the Windows clipboard protocols.
