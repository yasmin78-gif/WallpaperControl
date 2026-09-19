using System;
using System.Drawing;
using System.Windows.Forms;

namespace WallpaperControl
{
    internal sealed partial class CalendarWidgetForm
    {
        private bool widgetDragging;
        private bool thumbDragging;
        private float thumbGrabOffset;
        private bool rendering;

        internal bool ScrollWheel(Point point, int delta, int lines)
        {
            if (lifetimeEnded || activitySuspended || widgetDragging || thumbDragging) return false;
            PointF logical = new(point.X / viewport.Scale, point.Y / viewport.Scale);
            RectangleF wheelArea = new(12, CalendarViewport.HeaderHeight, WidgetWidth - 24, viewport.ViewportHeight);
            if (!wheelArea.Contains(logical) || !viewport.Wheel(delta, lines)) return false;
            RenderLayeredWindowCore(updateContent: false);
            return true;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (ScrollWheel(e.Location, e.Delta, SystemInformation.MouseWheelScrollLines) && e is HandledMouseEventArgs handled)
                handled.Handled = true;
            // Do not forward handled wheel input to another control or activate the desktop widget.
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (lifetimeEnded || activitySuspended) return;
            PointF point = new(e.X / viewport.Scale, e.Y / viewport.Scale);
            RectangleF hit = viewport.Track;
            hit.Inflate(4, 0);
            if (e.Button == MouseButtons.Left && viewport.CanScroll && hit.Contains(point))
            {
                if (point.Y >= viewport.Thumb.Top && point.Y <= viewport.Thumb.Bottom)
                {
                    thumbGrabOffset = point.Y - viewport.Thumb.Top;
                    thumbDragging = true;
                    Capture = true;
                }
                else if (viewport.SetOffset(viewport.ScrollOffset + (point.Y < viewport.Thumb.Top ? -1 : 1) * viewport.ViewportHeight))
                    RenderLayeredWindowCore(updateContent: false);
                return; // Shared widget dragging never sees a scrollbar gesture.
            }
            widgetDragging = e.Button == MouseButtons.Left && !locked;
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (thumbDragging)
            {
                if (viewport.DragThumb(e.Y / viewport.Scale, thumbGrabOffset)) RenderLayeredWindowCore(updateContent: false);
                return;
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (thumbDragging && e.Button == MouseButtons.Left)
            {
                thumbDragging = false;
                Capture = false;
                return;
            }
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left) widgetDragging = false;
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            if (!Capture)
            {
                thumbDragging = false;
                if (widgetDragging && !lifetimeEnded)
                {
                    widgetDragging = false;
                    base.OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                }
            }
            base.OnMouseCaptureChanged(e);
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            RenderLayeredWindow();
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            if (IsHandleCreated && !rendering) RenderLayeredWindow();
        }
    }
}
