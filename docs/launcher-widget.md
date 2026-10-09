# Quick launcher

Enable **Widgets â†’ Schnellstarter**, choose Minimal/Clean/Glow, icon-only or icon-and-name, 2â€“6 columns, maximum height and position locking.

Add, edit, delete and reorder up to 100 named targets in the settings page or the widget's menu. Entry management saves immediately and independently, like notes and feed sources; visual preferences use the existing Save/Discard preview workflow.

- Existing programs, files, folders and HTTP/HTTPS URLs are supported. Windows opens the target with its standard association.
- Bare EXE names are resolved through PATH and stored as absolute paths. Environment variables in local paths are expanded when configuring a target.
- Arguments are supported for EXE programs only. WallpaperControl does not concatenate targets into shell commands or interpret scripts. Script files use their Windows association.
- Shortcuts are copied unchanged to `%LOCALAPPDATA%\WallpaperControl\Launcher\Shortcuts`, using unique filenames. The original remains untouched and may be deleted after import. Existing valid external shortcut entries are migrated on startup. Missing legacy shortcuts remain as entries and can be repaired or removed.
- Managed copies are opened as `.lnk` files, preserving embedded arguments, working directory and other Windows shortcut properties. Removing or replacing the final referencing entry removes its managed copy after the new configuration has been saved. Disabling the widget or exiting keeps the copies for the next start. Programs, folders and other files are never copied or deleted.
- Copy/write failures roll back new copies and keep the previous entries and files. Cleanup is restricted to explicitly owned GUID-named `.lnk` files in the dedicated directory; redirected storage directories are refused. No general folder sweep or recursive deletion occurs.
- Files and shortcuts have an Open file location action. URLs have Copy address. URL icons are neutral globes; local shell icons load asynchronously and are cached until their entry is removed or the widget closes. Owned native icon handles and managed bitmaps are disposed.
- File, folder and shortcut drops onto the widget open a confirmation/editor dialog for each target. HTTP/HTTPS text drops are also accepted. Position locking prevents widget movement only; adding targets by drop remains available. Activity/power suspension prevents drops.
- Nothing launches automatically at startup, while editing, or from a drop. The user must click an entry or choose its Open action.
- Missing or failed targets remain in the list and show a readable error on activation. Diagnostics include exception type only, not target arguments or URL contents.

## Manual check

1. Enable the widget, add an EXE, folder, text file, `.lnk` and an HTTPS URL. Test each click and its appropriate context actions.
2. Add an EXE with arguments, such as a browser's private mode. Compare a shortcut with its original Windows behavior.
3. Rename and reorder entries; restart and verify persistence.
   For a shortcut, delete its desktop original after adding it, then restart and test the launcher. Remove the entry and confirm its managed copy disappears while the original program remains installed.
4. Test icon-only and icon-and-name, 2â€“6 columns, all styles, dark/light and position locking.
5. Drop a local file or shortcut from Explorer onto the widget. Cancel once, then save; confirm it is added without starting it. Lock the position and confirm drops remain accepted while the widget cannot be moved.
6. Add enough entries to exceed the configured height. Test wheel and thumb dragging.
7. Rename/remove a configured file, then click its entry; expect an error with the entry retained.
8. Exercise Win+D, fullscreen pause/resume and Explorer desktop recovery using the existing widget behavior.

Explorer drag-and-drop across different integrity levels (for example, an elevated WallpaperControl and a normal Explorer) may be blocked by Windows. The focused automated tests validate drop data and lock/suspension policy, but do not replace the physical Explorer drag test on the attached desktop.

Focused checks: `dotnet run --project WallpaperControl.RegressionTests -c Release -- --launcher-widget-checks`.

Native icon ownership follows [SHGetFileInfo](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shgetfileinfow). Target opening uses [ProcessStartInfo.UseShellExecute](https://learn.microsoft.com/en-us/dotnet/fundamentals/runtime-libraries/system-diagnostics-processstartinfo-useshellexecute).
