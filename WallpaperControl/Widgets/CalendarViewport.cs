using System;
using System.Drawing;

namespace WallpaperControl
{
    /// <summary>Calendar-only logical (96-DPI) viewport geometry and transient scroll state.</summary>
    internal sealed class CalendarViewport
    {
        internal const int MinimumMaximumHeight = 300;
        internal const int DefaultMaximumHeight = 700;
        internal const int MaximumMaximumHeight = 1400;
        internal const int Width = 340;
        internal const int HeaderHeight = 54;
        internal const int FooterHeight = 25;
        private int wheelRemainder;
        internal float Scale { get; private set; } = 1;
        internal int PhysicalHeight { get; private set; }
        internal float LogicalHeight => PhysicalHeight / Scale;
        internal float TotalContentHeight { get; private set; }
        internal float ViewportHeight { get; private set; }
        internal float ScrollOffset { get; private set; }
        internal float MaxScrollOffset => Math.Max(0, TotalContentHeight - ViewportHeight);
        internal bool CanScroll => MaxScrollOffset > 0 && ViewportHeight > 0;
        internal RectangleF ContentBounds => new(12, HeaderHeight, Width - (CanScroll ? 42 : 24), ViewportHeight);
        internal RectangleF Track => CanScroll ? new(Width - 18, HeaderHeight, 6, ViewportHeight) : RectangleF.Empty;
        internal RectangleF Thumb
        {
            get
            {
                if (!CanScroll) return RectangleF.Empty;
                float height = Math.Min(ViewportHeight, Math.Max(20, ViewportHeight * ViewportHeight / TotalContentHeight));
                return new RectangleF(Track.X, Track.Y + (ViewportHeight - height) * ScrollOffset / MaxScrollOffset, Track.Width, height);
            }
        }

        internal static int NormalizeMaximum(int value) => Math.Clamp(value, MinimumMaximumHeight, MaximumMaximumHeight);

        internal void Update(float contentHeight, int configuredMaximum, int workAreaHeight, int dpi)
        {
            Scale = Math.Clamp(dpi, 48, 768) / 96f;
            TotalContentHeight = Math.Max(0, contentHeight);
            // The preference is always logical. Work area and the existing hard bitmap budget
            // are physical. Runtime clamping never changes the stored user preference.
            int ceiling = Math.Max(1, Math.Min(Math.Min((int)Math.Floor(NormalizeMaximum(configuredMaximum) * Scale),
                workAreaHeight), CalendarFeedLimits.MaxWidgetHeight));
            PhysicalHeight = Math.Min(ceiling, (int)Math.Ceiling(Math.Max(120, HeaderHeight + FooterHeight + TotalContentHeight) * Scale));
            ViewportHeight = Math.Max(0, LogicalHeight - HeaderHeight - FooterHeight);
            ScrollOffset = Math.Clamp(ScrollOffset, 0, MaxScrollOffset);
            if (!CanScroll) { ScrollOffset = 0; wheelRemainder = 0; }
        }

        internal bool SetOffset(float offset)
        {
            float next = CanScroll && float.IsFinite(offset) ? Math.Clamp(offset, 0, MaxScrollOffset) : 0;
            if (next == ScrollOffset) return false;
            ScrollOffset = next;
            return true;
        }

        internal bool Wheel(int delta, int lines)
        {
            if (!CanScroll || lines == 0) return false;
            long accumulated = (long)wheelRemainder + delta;
            long notches = accumulated / 120;
            wheelRemainder = (int)(accumulated % 120);
            float distance = lines < 0 ? ViewportHeight : Math.Clamp(lines, 1, 100) * 21;
            return SetOffset((float)(ScrollOffset - notches * (double)distance));
        }

        internal bool DragThumb(float pointerY, float grabOffset)
        {
            float travel = Track.Height - Thumb.Height;
            return CanScroll && travel > 0 && SetOffset((pointerY - grabOffset - Track.Top) / travel * MaxScrollOffset);
        }
    }
}
