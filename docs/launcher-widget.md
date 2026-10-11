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

### Website icons

Website entries automatically fetch favicons in the background, including existing entries on startup. HTML icon links are ordered by declared size; `/favicon.ico` is the fallback. Only HTTP/HTTPS sources and redirects are accepted. Downloads have a 35-second total timeout after admission and a 3-second timeout per page/request/icon candidate, at most two concurrent jobs, a 1 MiB HTML limit and a 4 MiB image limit. A candidate error or timeout continues to the next candidate, including the root fallback. Raster decoding rejects unsupported or corrupt images and excessive dimensions. Failures retain the globe and retry after five minutes (checked once a minute), on the next application start, or immediately when entries are saved. Only a decodable matching cached image suppresses further attempts. Manually selected icons are excluded from automatic retries.

The website editor shows an icon preview, **Choose custom icon** and **Use website icon**. PNG, JPG, ICO and WEBP use the Windows image decoder without an added dependency. Icons are proportionally normalized to transparent 128×128 PNGs and rendered in the existing 32×32 launcher area. Program entries keep their existing shell icons and arguments UI.

Owned images have unique names under `%LOCALAPPDATA%\WallpaperControl\Launcher\Icons\`. Custom selections are copied, so deleting their originals has no effect. Cached favicons work offline. Manual icons take priority; resetting uses the cached favicon or schedules a download. Address changes invalidate the old favicon while retaining an explicit custom icon. Cache cleanup only removes owned candidates no longer referenced by any entry; cancelling an edit cleans its unused imported images.

Focused website checks: `dotnet run --project WallpaperControl.RegressionTests -c Release -- --launcher-website-icon-checks`. These cover all four formats, transparency, resolution preference, fallback, offline use, redirects, size limits, cancellation, custom file independence, reset/address changes, JSON compatibility, cleanup references, editor visibility and background/editor persistence. A live website and real file-picker interaction remain useful manual checks.

HTTP diagnostics in the application log include host/path (without query strings or credentials), status, content type and declared size. Candidate failures record stage, exception type and HRESULT; successful cache writes and exhausted candidate lists are also logged. No message boxes are shown for favicon failures. An opt-in diagnostic probe accepts a website URL and a previously downloaded image: `dotnet run --project WallpaperControl.RegressionTests -c Debug -- --favicon-probe https://example.com/ C:\temp\favicon.ico`.

On 2026-10-11, live diagnosis of chatgpt.com returned HTTP 403 for both `/` and `/favicon.ico` through .NET HttpClient, including HTTP/2, direct connection without proxy and TLS 1.2 checks; the Windows HTTP client also returned 403. curl with the same application user agent returned HTTP 200 and a valid 32,038-byte, four-frame ICO that decoded successfully. Removing curl's Accept header, explicitly selecting HTTP/1.1 and limiting TLS to 1.2 also retained HTTP 200. These checks isolate the problem to server treatment of the client/transport rather than image decoding or an identified header/version mismatch; the exact remote filtering rule cannot be established locally.

The primary ChatGPT response explicitly included `cf-mitigated: challenge`, confirming a Cloudflare challenge response; the precise client fingerprint rule remains unknown. Only an HTTP 403 on an icon request permits one fallback request to the same public URL with Windows' `%SystemRoot%\System32\curl.exe`. HTML pages, successful requests, HTTP 401, explicit WWW-Authenticate challenges and other failures never use this fallback. No domain exceptions, browser sessions or credentials are used. The executable is launched directly with separate arguments, no shell and no visible console. `-q` disables curl config files; no cookies, authentication or relaxed certificate validation are supplied. HTTP/HTTPS protocol restrictions, actual body size checks, the existing shared three-second deadline and image validation remain active. Redirects are returned to the common validated redirect loop instead of being followed by curl. Missing curl or errors retain the ordinary globe/retry behavior. Valid cached icons still avoid further downloads. Live ChatGPT and YouTube probes now download, decode and locally cache valid images.

Native icon ownership follows [SHGetFileInfo](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shgetfileinfow). Target opening uses [ProcessStartInfo.UseShellExecute](https://learn.microsoft.com/en-us/dotnet/fundamentals/runtime-libraries/system-diagnostics-processstartinfo-useshellexecute).
