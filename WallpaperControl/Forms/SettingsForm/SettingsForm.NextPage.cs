using System.Windows.Forms;

namespace WallpaperControl
{
    internal sealed partial class SettingsForm
    {
        /// <summary>
        /// Rebuilds the next-wallpaper widget&apos;s localized styles while retaining the selection.
        /// </summary>
        /// <param name="selectedStyle">The widget style to keep selected.</param>
        private void RefreshNextStyleChoices(SystemWidgetStyle selectedStyle)
        {
            RefreshWidgetStyleChoices(nextWidgetStyleComboBox, selectedStyle);
        }

        /// <summary>
        /// Returns the selected next-widget style, falling back to the original minimal appearance.
        /// </summary>
        /// <returns>The selected next-wallpaper widget style, with the default used for an invalid selection.</returns>
        private SystemWidgetStyle GetSelectedNextStyle()
        {
            int index = nextWidgetStyleComboBox?.SelectedIndex ?? (int)SystemWidgetStyle.Minimal;
            return Enum.IsDefined(typeof(SystemWidgetStyle), index)
                ? (SystemWidgetStyle)index
                : SystemWidgetStyle.Minimal;
        }
    }
}
