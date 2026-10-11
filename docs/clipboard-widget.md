# Clipboard widget

Enable **Widgets → Zwischenablage** explicitly. The widget records new Unicode text changes only; existing clipboard contents are not imported when enabled or resumed.

- Keep 5, 10, 20 or 50 entries. Exact duplicates move to the top without changing their text.
- Click an entry to copy its full text without changing its history position or timestamp. No automatic paste or target application activation occurs.
- Right-click for full text, copy and individual deletion. Header buttons pause recording and clear the widget history.
- Minimal, Clean and Glow support light/dark themes, position locking, maximum height and scrolling.
- Only preferences persist. History is held in process memory and cleared when disabled or disposed. Clipboard contents are not logged or exported.
- Windows clipboard exclusion/history flags are respected. Copies made by the widget request exclusion from Windows history and cloud clipboard.
- Whitespace-only text and text over 524,288 UTF-16 code units are ignored. Passwords cannot reliably be recognized if the source application does not mark them as excluded.
- Recording is suspended during the existing widget activity/power suspension. There is no backlog import on resume.

Clearing this widget does not clear the Windows clipboard or Windows clipboard history. RAM-only storage does not promise secure erasure of immutable managed strings or protection against process dumps.

## Manual verification

1. Start the new build and enable the widget. Choose a capacity and save.
2. Copy ordinary text, multiline code and a URL. Check preview, timestamp and duplicate ordering.
3. Click an entry and paste into a text editor. Confirm the exact original text, including line breaks.
4. Pause recording, copy another text and resume. The paused text must not appear.
5. Fill the history past the selected capacity; verify mouse wheel and thumb dragging at the configured height.
6. Open full text and delete an entry through the context menu. Clear all entries using the header button.
7. Disable/re-enable the widget and restart the application. The history must be empty.
8. Check all three styles and light/dark themes.

## Focused checks

`dotnet run --project WallpaperControl.RegressionTests -c Release -- --clipboard-widget-checks`

These checks use an isolated clipboard source for copying and include passive native listener registration/disposal checks. They do not overwrite the user's Windows clipboard.

## Interaction during modal dialogs

All desktop widgets are independent top-level tool windows without owners. Nevertheless WinForms `ShowDialog()` disables visible windows on the same UI thread, regardless of their owner relationship. The clipboard widget now has a narrowly scoped exception: a thread-modal event queues enabling only its existing HWND after modal setup. Its own `WM_ENABLE` notification handles nested dialogs, which do not raise another outer `EnterThreadModal` event. Other windows and dialog ownership remain unchanged. Explicitly disabled, hidden, disposed or activity/power-suspended clipboard windows are excluded. Modal state subscriptions are removed on disposal, including after widget recreation.

During modal interaction, a clipboard mouse click may activate the widget when focus is needed; outside dialogs the original no-activation behavior is retained. No focus is stolen when the exception enables input. WinForms continues to own dialog closure, cancellation and restoration of the other windows. History, copying, storage and the listener remain unchanged.

Native focused checks open a real launcher editor and a widget-settings dialog, including nested modal windows. They verify native enabled states, click-to-copy routing through the test clipboard backend, wheel scrolling, delete/clear, pause/resume, focus return with unchanged dialog text, cancellation, explicit disabling/suspension and fresh clipboard/widget service creation. For a live clipboard integration check, open an editor, click a history entry and paste into its text field; also exercise the details context menu and repeat after application restart.

The ordering of thread-window disabling and modal enter/leave notifications is documented in the [WinForms implementation](https://source.dot.net/System.Windows.Forms/System/Windows/Forms/Application.ThreadContext.cs.html).
