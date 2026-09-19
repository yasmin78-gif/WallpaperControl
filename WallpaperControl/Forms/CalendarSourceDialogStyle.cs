using System.Drawing;
using System.Windows.Forms;

namespace WallpaperControl
{
    internal static class CalendarSourceDialogStyle
    {
        internal static void Apply(Form form, bool dark)
        {
            form.AutoScaleDimensions = new SizeF(96, 96);
            form.AutoScaleMode = AutoScaleMode.Dpi;
            form.StartPosition = FormStartPosition.CenterParent;
            form.ShowInTaskbar = false;
            form.MinimizeBox = false;
            form.MaximizeBox = false;
            form.BackColor = AppTheme.WindowBackground(dark);
            form.ForeColor = AppTheme.TextPrimary(dark);
            ApplyControls(form, dark);
            form.HandleCreated += (_, _) => WindowsTheme.ApplyTitleBar(form, dark);
        }

        private static void ApplyControls(Control parent, bool dark)
        {
            foreach (Control control in parent.Controls)
            {
                control.ForeColor = AppTheme.TextPrimary(dark);
                control.BackColor = control is TextBox or ComboBox or ListView
                    ? AppTheme.InputBackground(dark) : AppTheme.WindowBackground(dark);
                if (control is Button button)
                {
                    button.FlatStyle = FlatStyle.Flat;
                    button.BackColor = AppTheme.ControlBackground(dark);
                    button.FlatAppearance.BorderColor = AppTheme.Border(dark);
                    button.FlatAppearance.MouseOverBackColor = AppTheme.ControlHover(dark);
                }
                ApplyControls(control, dark);
            }
        }

        internal static Button Button(string text) => new()
        {
            Text = text, AutoSize = true, MinimumSize = new Size(130, 34), Margin = new Padding(5)
        };
    }
}
