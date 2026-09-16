# Regression checks

Run from the repository root:

```powershell
dotnet run --project WallpaperControl.RegressionTests
```

The checks use a dedicated temporary statistics directory and unique mutex and pipe names. They do not start the wallpaper application or change real user statistics.

`-- --skip-pipe` runs all checks except named-pipe integration checks when the execution environment blocks Windows named-pipe connections. A full run is still required to verify command forwarding in that environment.

Remote command checks cover the 64-character limit, incomplete lines, the two-second read deadline, shutdown cancellation, and listener recovery after idle or oversized input. Test failures are printed to stderr and return exit code 1; they are caught before reaching Windows error reporting.

Calendar checks use simulated HTTP responses without contacting real feeds. They cover full and partial outages, repeated failures, recovery, empty feeds, and source changes.

Statistics service checks cover duplicate suppression, day rollover, recurrence, removal, reset, loading saved data, and save triggers. They use a controlled clock and do not modify real user statistics.

Hotkey checks simulate native registration and release calls. They cover disabled combinations, conflicts, swapped combinations, unchanged bindings, cleanup, and labels without taking over real global hotkeys.

Settings checks use a unique temporary key under HKCU\\Software\\WallpaperControl.RegressionTests-<GUID> and remove it afterward. They verify defaults, legacy value types, round trips, bounds, and invalid values without touching production settings.

Transition checks include legacy numeric indices, named effects, duration fallback, and direction/zoom persistence. Position checks cover negative coordinates, invalid values, and the existing visibility threshold for disconnected monitors.

Fullscreen tests cover monitor bounds (including secondary monitors), automatic pause state, Alt-Tab grace periods, manual pause preservation, and persisted opt-out. Native foreground detection, rendering suspension, and actual game frame times still need a desktop smoke test.

Shared UI checks reference the built application with an assembly alias, alongside the existing linked service tests. They construct an unshown settings dialog on an STA thread and exercise the actual shared settings reader, image helpers, geometry, bitmap upload, and drag handler. The checks cover all editable widget values, independent snapshots, the save-only default weather location, accepted image extensions, metadata fallback, empty drawing bounds, drag locking/button rules, disposal, and GDI resource counts after repeated uploads. Floating-point drawing bounds use a small tolerance.

These checks never start MainForm, show their test windows, invoke SaveAndClose, change the desktop wallpaper, move the real pointer, fetch calendars/weather, or persist application preferences. Settings initialization can read the existing language and Windows theme. Image fixtures use unique temporary paths and are deleted afterward. Actual widget appearance, manual interactions, and fullscreen/game behavior still require a desktop smoke test.
