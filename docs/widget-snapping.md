# Widget snapping

**Settings → General → Snap widgets while moving** controls all desktop widgets. Default: enabled. Saving the global settings applies it immediately; cancelling leaves it unchanged.

- X and Y are resolved independently against other visible, non-minimized desktop widgets. Locked widgets remain valid reference targets.
- Match left/right/top/bottom edges, horizontal/vertical centers, touching opposite edges and a 10 logical-pixel gap.
- Enter a snap within 8 logical pixels; keep that same candidate until the raw pointer position moves beyond 12 pixels. These values scale with the moving widget's DPI.
- Hold **Alt** during movement for unsnapped placement. Releasing Alt allows fresh snapping without retained hysteresis.
- Thin cyan guides appear only while dragging, without taking focus or mouse input. Hidden targets are ignored. Release, capture loss, hiding/closing the widget, disabling snapping and application shutdown clear the guides.
- Position callbacks/persistence still occur only at the end of the existing drag gesture. The Web widget uses its native moving messages and retains its existing resize/geometry-settled handling.
- Position locking prevents movement and leaves launcher drop-to-add behavior unchanged.

Manual verification: place two unlocked widgets near matching edges/centers, stack them with the 10-pixel gap, test X/Y against different widgets, hold Alt, move away to release a snap, then disable the global option. Repeat with the clock, Next and Web widgets, mixed monitor DPI, negative monitor coordinates and a locked reference widget. Confirm guides disappear after each gesture and that ordinary buttons, scrollbars, media controls and launcher drops still work.

Focused checks: `--widget-snapping-checks`, `--shared-ui-checks` and `--widget-navigation-checks` in WallpaperControl.RegressionTests.
