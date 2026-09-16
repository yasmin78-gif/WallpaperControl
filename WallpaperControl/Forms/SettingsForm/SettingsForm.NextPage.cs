using System.Windows.Forms;

namespace WallpaperControl
{
    internal sealed partial class SettingsForm
    {
        /// <summary>Rebuilds the next-wallpaper widget's localized styles while retaining the selection.</summary>
        private void RefreshNextStyleChoices(SystemWidgetStyle selectedStyle)
        {
            RefreshWidgetStyleChoices(nextWidgetStyleComboBox, selectedStyle);
        }

        /// <summary>Returns the selected next-widget style, falling back to the original minimal appearance.</summary>
        private SystemWidgetStyle GetSelectedNextStyle()
        {
            int index = nextWidgetStyleComboBox?.SelectedIndex ?? (int)SystemWidgetStyle.Minimal;
            return Enum.IsDefined(typeof(SystemWidgetStyle), index)
                ? (SystemWidgetStyle)index
                : SystemWidgetStyle.Minimal;
        }
    }
}
