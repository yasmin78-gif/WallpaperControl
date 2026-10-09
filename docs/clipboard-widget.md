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
