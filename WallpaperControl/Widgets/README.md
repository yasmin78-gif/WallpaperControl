# Shared widget infrastructure

Each widget retains its own content, refresh interval, asynchronous cancellation, and fullscreen suspension behavior. Common infrastructure is provided by small helpers:

- `WidgetDrawing`: shared text glow, weather/calendar palette, and rounded paths. The calendar wrapper explicitly retains its previous empty-bounds policy; system and weather return an empty path for empty bounds.
- `LayeredWidgetBitmap`: transparent bitmap upload for calendar, system, and weather, including the matching native declarations and `finally` cleanup of temporary GDI handles. The caller still owns the bitmap.
- `WidgetDragHandler`: mouse subscriptions, capture, screen-relative movement, redraw, and the final position callback for calendar, system, and weather. Each widget disposes its handler. The clock and next-wallpaper button retain their distinct interaction behavior.
- `DesktopWidgetNative`: desktop attachment/placement and shared handling of non-activating mouse messages for all widgets.

Keep data retrieval and widget-specific rendering out of these helpers. For drawing/drag changes, run the regression suite and then manually check transparency, moving/locking widgets, and fullscreen suspension. The automated native-upload checks use windows that are never shown.
