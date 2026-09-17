# Windows installer

The installer packages a self-contained Release build for Windows x64, including
the .NET runtime. It installs for the current user under
`%LOCALAPPDATA%\Programs\Wallpaper Control`, without requesting administrator rights.
The setup offers English, German, French, Spanish, and Japanese.

## Build

Install the .NET 10 SDK and [Inno Setup](https://jrsoftware.org/isdl.php)
(6.3 or later), then run from the repository root:

```powershell
./installer/Build-Installer.ps1
# For a compiler outside the standard installation directories:
./installer/Build-Installer.ps1 -InnoCompiler 'C:\Tools\Inno Setup\ISCC.exe'
```

The script reads the version from the application project, publishes to a clean
temporary directory, compiles the setup, and writes a SHA-256 checksum alongside it
in `artifacts/installer`. Do not change the stable `AppId` between releases: it
allows later installers to reuse the existing installation and uninstall entry.

## Installation and upgrades

- Exit Wallpaper Control from its tray menu before installation, including any
  portable copy. Closing the main window may only minimize it to the tray.
- A Start menu shortcut is created; a desktop shortcut is optional.
- Launching the application at the end is optional and unchecked by default.
- An existing enabled `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
  value named `WallpaperControl` is redirected to the installed executable.
  An absent or empty entry stays disabled. New users enable autostart in the app.
- Application preferences, encrypted calendar sources, statistics, and wallpaper
  files are preserved. The installer does not delete older portable folders.
- Uninstall removes the installed files and shortcuts. It removes autostart only
  when it still matches this installation, and retains user settings and statistics.

The generated installer is unsigned unless a separate code-signing process is
configured. Windows may display a publisher or SmartScreen warning.

## Isolated installation tests

Using PowerShell 7, pass the compiler and an existing self-contained publish folder:

```powershell
./installer/Test-Installer.ps1 -InnoCompiler 'C:\Tools\Inno Setup\ISCC.exe' -PublishDir 'C:\Build\WallpaperControl'
```

The test compiles the same setup script with a unique application identity and a
test registry key in place of the Windows autostart key. It checks fresh installation,
payload hashes, shortcut creation, repeated installation, autostart migration, and
uninstallation. The application is never launched. Logs stay in the ignored artifacts
directory; the temporary installation and test registry key are removed.
