namespace WallpaperControl
{
    internal static class SettingsControlTheme
    {
        internal static void Apply(
            Control.ControlCollection controls,
            bool darkMode,
            Color background,
            Color foreground,
            Color inputBackground,
            Color buttonBackground)
        {
            foreach (Control control
                in controls)
            {
                if (control is TabControl tabControl)
                {
                    tabControl.BackColor =
                        background;

                    tabControl.ForeColor =
                        foreground;
                }
                else if (control is TabPage tabPage)
                {
                    tabPage.BackColor =
                        background;

                    tabPage.ForeColor =
                        foreground;
                }
                else if (control is GroupBox groupBox)
                {
                    groupBox.BackColor =
                        background;

                    groupBox.ForeColor =
                        foreground;

                    groupBox.FlatStyle =
                        FlatStyle.Flat;
                }
                else if (control is Label label)
                {
                    label.BackColor =
                        Color.Transparent;

                    label.ForeColor =
                        foreground;
                }
                else if (control is CheckBox checkBox)
                {
                    checkBox.BackColor =
                        background;

                    checkBox.ForeColor =
                        foreground;
                }
                else if (control is TextBox textBox)
                {
                    textBox.BackColor =
                        inputBackground;

                    textBox.ForeColor =
                        foreground;
                }
                else if (control is ComboBox combo)
                {
                    combo.BackColor =
                        inputBackground;

                    combo.ForeColor =
                        foreground;
                }
                else if (control is TrackBar trackBar)
                {
                    trackBar.BackColor =
                        background;

                    trackBar.ForeColor =
                        foreground;
                }
                else if (control is Button button)
                {
                    button.UseVisualStyleBackColor =
                        false;

                    button.BackColor =
                        buttonBackground;

                    button.ForeColor =
                        foreground;

                    button.FlatStyle =
                        FlatStyle.Flat;

                    button.FlatAppearance.BorderColor =
                        AppTheme.Border(darkMode);

                    button.FlatAppearance.MouseOverBackColor =
                        AppTheme.ControlHover(darkMode);

                    button.FlatAppearance.MouseDownBackColor =
                        AppTheme.ControlPressed(darkMode);
                }

                if (control.HasChildren)
                {
                    Apply(
                        control.Controls,
                        darkMode,
                        background,
                        foreground,
                        inputBackground,
                        buttonBackground);
                }
            }
        }

    }
}
