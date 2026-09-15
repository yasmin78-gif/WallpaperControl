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
