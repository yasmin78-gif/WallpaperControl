# Regression checks

Run from the repository root:

```powershell
dotnet run --project WallpaperControl.RegressionTests
```

The checks use a dedicated temporary statistics directory and unique mutex and pipe names. They do not start the wallpaper application or change real user statistics.

`-- --skip-pipe` runs only storage and process-lock checks when the execution environment blocks Windows named-pipe connections. A full run is still required to verify command forwarding in that environment.

Remote command checks cover the 64-character limit, incomplete lines, the two-second read deadline, shutdown cancellation, and listener recovery after idle or oversized input. Test failures are printed to stderr and return exit code 1; they are caught before reaching Windows error reporting.
