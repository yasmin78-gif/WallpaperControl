namespace WallpaperControl
{
    /// <summary>Uses the selected notes widget palette on opaque, editable dialog surfaces.</summary>
    internal static class NotesDialogStyle
    {
        internal static void Apply(Form form, SystemWidgetStyle style)
        {
            var palette = WidgetDrawing.GetPalette(style);
            Color background = Color.FromArgb(255, palette.panel);
            Color foreground = Color.FromArgb(255, palette.text);
            Color accent = Color.FromArgb(255, palette.accent);
            Color input = Color.FromArgb(Math.Min(255, background.R + 10), Math.Min(255, background.G + 12), Math.Min(255, background.B + 15));
            form.Font = SystemFonts.MessageBoxFont;
            form.BackColor = background;
            form.ForeColor = foreground;
            ApplyControls(form);
            form.HandleCreated += (_, _) => WindowsTheme.ApplyTitleBar(form, true);

            void ApplyControls(Control parent)
            {
                foreach (Control control in parent.Controls)
                {
                    control.ForeColor = foreground;
                    control.BackColor = control is TextBox or ListView ? input : background;
                    if (control is Button button)
                    {
                        button.UseVisualStyleBackColor = false;
                        button.FlatStyle = FlatStyle.Flat;
                        button.BackColor = input;
                        button.FlatAppearance.BorderColor = accent;
                        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(36, 54, 66);
                        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(48, 70, 85);
                    }
                    if (control is CheckBox check) check.FlatStyle = FlatStyle.Standard;
                    if (control is TextBox textBox) textBox.BorderStyle = BorderStyle.FixedSingle;
                    if (control is DateTimePicker picker)
                    {
                        picker.CalendarMonthBackground = input;
                        picker.CalendarForeColor = foreground;
                        picker.CalendarTitleBackColor = background;
                        picker.CalendarTitleForeColor = accent;
                        picker.CalendarTrailingForeColor = Color.FromArgb(255, palette.muted);
                    }
                    if (control is ListView list)
                    {
                        list.BorderStyle = BorderStyle.None;
                        list.OwnerDraw = true;
                        list.DrawColumnHeader += (_, e) =>
                        {
                            using SolidBrush fill = new(background);
                            e.Graphics.FillRectangle(fill, e.Bounds);
                            TextRenderer.DrawText(e.Graphics, e.Header?.Text, list.Font, Inset(e.Bounds, list.DeviceDpi), accent,
                                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                        };
                        list.DrawItem += (_, e) => { if (list.View != View.Details) e.DrawDefault = true; };
                        list.DrawSubItem += (_, e) =>
                        {
                            Color fillColor = e.Item?.Selected == true ? Color.FromArgb(43, 66, 82) : input;
                            using SolidBrush fill = new(fillColor);
                            e.Graphics.FillRectangle(fill, e.Bounds);
                            TextRenderer.DrawText(e.Graphics, e.SubItem?.Text, list.Font, Inset(e.Bounds, list.DeviceDpi), foreground,
                                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                            if (e.ColumnIndex == 0 && e.Item?.Focused == true && list.Focused)
                                ControlPaint.DrawFocusRectangle(e.Graphics, e.Bounds, foreground, fillColor);
                        };
                    }
                    ApplyControls(control);
                }
            }
        }

        private static Rectangle Inset(Rectangle bounds, int dpi)
        {
            int padding = Math.Max(3, 6 * dpi / 96);
            return new Rectangle(bounds.X + padding, bounds.Y, Math.Max(0, bounds.Width - padding * 2), bounds.Height);
        }
    }
}
