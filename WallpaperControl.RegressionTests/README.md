# Regression checks

Run from the repository root:

```powershell
dotnet run --project WallpaperControl.RegressionTests
```

The checks use a dedicated temporary statistics directory and unique mutex and pipe names. They do not start the wallpaper application or change real user statistics.

`-- --skip-pipe` runs all checks except named-pipe integration checks when the execution environment blocks Windows named-pipe connections. A full run is still required to verify command forwarding in that environment.

Remote command checks cover the 64-character limit, incomplete lines, the two-second read deadline, shutdown cancellation, and listener recovery after idle or oversized input. Test failures are printed to stderr and return exit code 1; they are caught before reaching Windows error reporting.

Calendar checks use simulated HTTP responses without contacting real feeds. They cover full and partial outages, repeated failures, recovery, empty feeds, and source changes.
