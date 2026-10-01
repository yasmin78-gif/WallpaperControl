# Native video checks

From the repository root (Windows x64 with an interactive desktop):

```powershell
./WallpaperControl.RegressionTests/Generate-VideoFixtures.ps1 -Ffmpeg 'E:/yt-dlp/ffmpeg.exe' -OutputDirectory 'C:/Temp/WallpaperVideoFixtures'
dotnet build WallpaperControl.RegressionTests -c Debug -p:RunAnalyzers=true -p:RunAnalyzersDuringBuild=true
dotnet run --project WallpaperControl.RegressionTests -c Debug --no-build -- --video
dotnet run --project WallpaperControl.RegressionTests -c Debug --no-build -- --video-native C:/Temp/WallpaperVideoFixtures
```

FFmpeg is only needed to create deterministic test fixtures; it is not shipped or used by WallpaperControl. The native run opens a disabled, non-activating 240×240 video window in an uncovered desktop area for about 30 seconds. Keep it unobscured, and run no other UI tests concurrently. It never attaches to the desktop shell or changes the wallpaper, registry, Explorer, or focus. Native checks use a real STA WinForms message loop, matching production async continuations.

The loop checks use actual EVR image timestamps and DWM-synchronized screen pixels. They verify ten EOS transitions, delayed seek/restart, a pause at the third boundary, repeated WM_PAINT, Fill, mute, and cleanup. Black first-frame content is checked separately to ensure readiness never depends on pixel color. The EVR sample buffer is normally unavailable after EOS flush, so a failed GetCurrentImage there is not itself evidence of a black visible surface.

For production attachment/composition, additionally run:

```powershell
dotnet run --project WallpaperControl.RegressionTests -c Debug --no-build -- --video-desktop C:/Temp/WallpaperVideoFixtures
```

This explicit interactive check requires one monitor and an uncovered part of the desktop. It briefly displays blue/red fixture video behind icons and widgets, using two independently initialized production sessions. It verifies actual screen pixels, pause/resume, automatic loops without black, and successful reapplication. It removes its own renderer on completion and does not change registry, wallpaper settings, Explorer, or focus. Run it separately from other UI checks. The regression executable uses the application's Windows compatibility manifest so native child-layered-window support matches production.

## Phase 2

The complete commands include all Phase 1 checks: 36 native MF checks (23 original + 13 new), and 27 desktop checks (10 original + 17 new). To run only the additions:

```powershell
dotnet run --project WallpaperControl.RegressionTests -c Debug --no-build -- --video-native-phase2 C:/Temp/WallpaperVideoFixtures
dotnet run --project WallpaperControl.RegressionTests -c Debug --no-build -- --video-desktop-phase2 C:/Temp/WallpaperVideoFixtures
```

Repeat in Release after building with both analyzer properties. The new checks cover unreadable/removed/corrupt media, actual asynchronous MF errors, 24 player disposal cycles, bounded private-memory growth, native handle attribution, disposal safety, actual desktop rollback, paused replacements and cancellation of six initialized hidden MF candidates. No forced GC is used. Each canceled or replaced production session must destroy its HWND and release its EVR frame buffer; no event subscriptions may remain.

Resource checks warm up eight players, then measure 24 new players. They report total handle growth and named NVIDIA IPC handles separately, asserting bounded growth of the remaining handles. This is intentionally **not** a claim that total process handles remain stable with an injected graphics hook. On the tested machine, `nvspcap64.dll` (NVIDIA Corporation, 11.0.9.251) creates named Mutant/Section objects matching the reported NVIDIA IPC GUIDs. Those handles grew by 72 across 24 players. Test-only `NativeHandleSnapshot` queries current-process kernel handles; it never closes foreign handles or changes overlay settings. Compare runs with the NVIDIA overlay disabled manually to isolate this external influence. Private-memory growth is measured separately and cannot be attributed solely to the hook.

Production validation has a 15-second caller deadline and serializes metadata readers. A blocked native Source Reader call cannot be forcibly aborted in-process; its resources are released when that native call returns. Tests simulate cancellation and stale completions but cannot guarantee termination of a hung third-party decoder.

Production telemetry uses the existing UI polling infrastructure, logging one common CPU/RAM/handle sample per minute. Initial CPU is `n/a`; subsequent CPU values are normalized to total machine capacity. `workingSetMiB`, `privateMemoryMiB`, `handleCount`, timestamp, PID, mode, state and independent pause flags share the same log entry. No additional timer, background sampler or history buffer is created.

## Controlled native handle investigation

Explicit opt-in stress diagnostics use production MainForm mode ownership, controller and MF sessions in an isolated test form. Background services and settings writes are disabled; no production application instance or NVIDIA settings are changed. The test briefly owns real desktop renderer HWNDs. Run sequentially, with one monitor and a stable desktop. Each invocation is a fresh process and uses the same `loop-colors.mp4` / `switch-colors.mp4` fixtures.

```powershell
dotnet run --project WallpaperControl.RegressionTests -c Release --no-build -- --video-handle-stress C:/Temp/WallpaperVideoFixtures C:/Temp/recreate-25.jsonl 25 recreate
# Repeat with fresh processes and counts 50, 100, 250.
dotnet run --project WallpaperControl.RegressionTests -c Release --no-build -- --video-handle-stress C:/Temp/WallpaperVideoFixtures C:/Temp/rapid-100.jsonl 100 rapid
dotnet run --project WallpaperControl.RegressionTests -c Release --no-build -- --video-handle-stress C:/Temp/WallpaperVideoFixtures C:/Temp/cancelled-25.jsonl 25 cancelled
dotnet run --project WallpaperControl.RegressionTests -c Release --no-build -- --video-handle-stress C:/Temp/WallpaperVideoFixtures C:/Temp/stale-25.jsonl 25 stale
dotnet run --project WallpaperControl.RegressionTests -c Release --no-build -- --video-handle-stress C:/Temp/WallpaperVideoFixtures C:/Temp/failed-25.jsonl 25 failed
dotnet run --project WallpaperControl.RegressionTests -c Release --no-build -- --video-handle-stress C:/Temp/WallpaperVideoFixtures C:/Temp/reuse-250.jsonl 250 reuse
```

JSONL records contain kernel/process handle counts, handle-type census, named NVIDIA IPC handles, CPU, Working Set, Private Bytes and an ownership ledger. Unique kernel-object counts are available only when Windows returns nonzero object identities. Availability and redacted-identity counts are explicit; zero addresses are never counted as one shared object. The ledger checks native MF player pointers, renderer HWNDs, EVR sink/display pointers, callback/error subscriptions, tracked cancellation sources and recovery state. Disposed sessions must have zero owned pointers, HWNDs, callbacks and error subscriptions. Additional diagnostic revisions also count renderer Paint subscriptions and ordinary GC collection counts. No GC is forced; growth measurements do not add COM reference probes, and no foreign/vendor handles are closed. Only Mutant/Section names are queried, avoiding potentially blocking file/pipe-name queries.

A separate `com-audit` scenario temporarily holds exactly one additional reference to each of our own MFPlay player / EVR sink / EVR display interfaces through normal session Shutdown/Dispose. It drains callbacks for 500 ms, then releases only those additional references and records/asserts that each final Release returns zero. It never touches a freed pointer and balances all probes in `finally`. These extra references are **not** used in any handle-growth measurement. This is a native destruction check beyond observing cleared managed pointer fields:

```powershell
dotnet run --project WallpaperControl.RegressionTests -c Release --no-build -- --video-handle-stress C:/Temp/WallpaperVideoFixtures C:/Temp/com-audit-25.jsonl 25 com-audit
```

Eight warmup sessions precede the active baseline. Successful recreations are sampled after switch 1 and every five switches; preparation checkpoints verify two live sessions before swap. Final samples are immediate, after 30 seconds and 120 seconds, after actual UI image-mode restoration, after 30 seconds in image mode, after another video start/Stop, after 30 seconds stopped and after controller Dispose. A final restart creates one extra player; include it when interpreting vendor-handle totals.

`rapid` holds fully prepared candidates before publishing and repeatedly replaces them. `cancelled` cancels first-frame-ready preparation; `stale` additionally ignores cancellation until a newer player is published, then releases all stale continuations. `failed` injects failure after a real native first frame, retaining the old player. A final valid request ensures only the newest session survives. Completed session objects are not retained in a history list; audit data and weak CTS references avoid turning diagnostic tracking into a native resource owner.

`reuse` is exclusively diagnostic: Stop/ClearMediaItem, dispose the old custom EVR, asynchronously create/set the new media item on the same MFPlay player, then verify actual blue/green EVR pixels and mute. The MFPlay pointer remains identical, but custom EVR sink/display resources are recreated. Rebinding the same old EVR did not produce new frames in the initial experiment. This comparator is not transactional and does not prove no-black-frame or failure rollback. It skips repeated metadata validation and uses managed frame readback for color verification; its CPU/RAM should not be treated as a production performance benchmark. There is no production reuse change.

After the process exits, query only its kernel handle-table entry count from another process:

```powershell
dotnet run --project WallpaperControl.RegressionTests -c Release --no-build -- --video-handle-count-pid <terminated-test-PID>
```

This does not open, duplicate or close foreign handles. The measured NVIDIA GUIDs occur locally in NVIDIA binaries: `{2627E361-24E2-4F14-99ED-A20D0685D8DD}` in `nvspcap64.dll`, and `{52813408-3561-4705-820a-2b3b78be92ba}` in `nvd3dumx.dll`. Full raw observations and interpretation belong in the investigation report; these tests intentionally do not silently declare total process handles stable by subtracting the vendor component.
