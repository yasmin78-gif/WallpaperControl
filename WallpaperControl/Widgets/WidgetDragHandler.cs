using System.Drawing;
using System.Windows.Forms;

namespace WallpaperControl
{
    /// <summary>Owns mouse dragging for layered widgets, including capture and the final position callback.</summary>
    internal sealed class WidgetDragHandler : IDisposable
    {
        private readonly Control widget;
        private readonly Func<bool> isLocked;
        private readonly Action render;
        private readonly Action<Point> locationChanged;
        private readonly Func<Point> cursorPosition;
        private bool dragging;
        private Point mouseStart;
        private Point formStart;

        /// <summary>
        /// Connects dragging to one widget; the optional cursor reader supports deterministic checks.
        /// </summary>
        /// <param name="widget">The widget whose mouse interaction or rendering is coordinated.</param>
        /// <param name="isLocked">The callback that reports whether widget dragging is currently locked.</param>
        /// <param name="render">The callback that redraws the widget after movement.</param>
        /// <param name="locationChanged">The callback that receives the widget&apos;s final position after a drag.</param>
        /// <param name="cursorPosition">An optional screen-coordinate reader used for deterministic drag tests.</param>
        internal WidgetDragHandler(Control widget, Func<bool> isLocked, Action render,
            Action<Point> locationChanged, Func<Point>? cursorPosition = null)
        {
            this.widget = widget;
            this.isLocked = isLocked;
            this.render = render;
            this.locationChanged = locationChanged;
            this.cursorPosition = cursorPosition ?? (() => Cursor.Position);
            widget.MouseDown += BeginDrag;
            widget.MouseMove += ContinueDrag;
            widget.MouseUp += EndDrag;
        }

        /// <summary>
        /// Starts an unlocked left-button drag and retains its screen-space origin.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private void BeginDrag(object? sender, MouseEventArgs e)
        {
            if (isLocked() || e.Button != MouseButtons.Left) return;
            dragging = true;
            mouseStart = cursorPosition();
            formStart = widget.Location;
            widget.Capture = true;
        }

        /// <summary>
        /// Moves the widget relative to the drag origin and redraws its layered surface.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private void ContinueDrag(object? sender, MouseEventArgs e)
        {
            if (!dragging) return;
            Point now = cursorPosition();
            widget.Location = new Point(formStart.X + now.X - mouseStart.X, formStart.Y + now.Y - mouseStart.Y);
            render();
        }

        /// <summary>
        /// Ends a left-button drag and publishes the final position exactly once.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private void EndDrag(object? sender, MouseEventArgs e)
        {
            if (!dragging || e.Button != MouseButtons.Left) return;
            dragging = false;
            widget.Capture = false;
            locationChanged(widget.Location);
        }

        /// <summary>
        /// Disconnects mouse handlers and releases any capture still owned by this drag.
        /// </summary>
        public void Dispose()
        {
            widget.MouseDown -= BeginDrag;
            widget.MouseMove -= ContinueDrag;
            widget.MouseUp -= EndDrag;
            if (dragging)
            {
                dragging = false;
                widget.Capture = false;
            }
        }
    }
}
