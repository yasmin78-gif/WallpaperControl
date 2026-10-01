# Native video checks

From the repository root (Windows x64 with an interactive desktop):

```powershell
./WallpaperControl.RegressionTests/Generate-VideoFixtures.ps1 -Ffmpeg 'E:/yt-dlp/ffmpeg.exe' -OutputDirectory 'C:/Temp/WallpaperVideoFixtures'
dotnet build WallpaperControl.RegressionTests -c Debug -p:RunAnalyzers=true -p:RunAnalyzersDuringBuild=true
dotnet run --project WallpaperControl.RegressionTests -c Debug --no-build -- --video
dotnet run --project WallpaperControl.RegressionTests -c Debug --no-build -- --video-native C:/Temp/WallpaperVideoFixtures
```

FFmpeg is only needed to create deterministic test fixtures; it is not shipped or used by WallpaperControl. The native run opens a disabled, non-activating 240×240 video window at (20,20) for about 30 seconds. Keep it unobscured, and run no other UI tests concurrently. It never attaches to the desktop shell or changes the wallpaper, registry, Explorer, or focus.

The loop checks use actual EVR image timestamps and DWM-synchronized screen pixels. They verify ten EOS transitions, delayed seek/restart, a pause at the third boundary, repeated WM_PAINT, Fill, mute, and cleanup. Black first-frame content is checked separately to ensure readiness never depends on pixel color. The EVR sample buffer is normally unavailable after EOS flush, so a failed GetCurrentImage there is not itself evidence of a black visible surface.

For production attachment/composition, additionally run:

```powershell
dotnet run --project WallpaperControl.RegressionTests -c Debug --no-build -- --video-desktop C:/Temp/WallpaperVideoFixtures
```

This explicit interactive check requires one monitor and an uncovered part of the desktop. It briefly displays blue/red fixture video behind icons and widgets, using two independently initialized production sessions. It verifies actual screen pixels, pause/resume, automatic loops without black, and successful reapplication. It removes its own renderer on completion and does not change registry, wallpaper settings, Explorer, or focus. Run it separately from other UI checks. The regression executable uses the application's Windows compatibility manifest so native child-layered-window support matches production.
