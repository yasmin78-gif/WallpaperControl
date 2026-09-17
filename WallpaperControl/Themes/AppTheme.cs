using System.Drawing;
using System.Windows.Forms;

namespace WallpaperControl
{
    /// <summary>
    /// Shared application palette used by the WinForms UI.
    /// Keeping the color roles here prevents individual forms from drifting
    /// into slightly different dark themes over time.
    /// </summary>
    internal static class AppTheme
    {
        public static readonly Color DarkWindow = Color.FromArgb(17, 24, 31);
        public static readonly Color DarkSidebar = Color.FromArgb(13, 24, 33);
        public static readonly Color DarkPanel = Color.FromArgb(25, 35, 44);
        public static readonly Color DarkControl = Color.FromArgb(27, 39, 49);
        public static readonly Color DarkControlHover = Color.FromArgb(35, 51, 64);
        public static readonly Color DarkControlPressed = Color.FromArgb(42, 64, 82);
        public static readonly Color DarkSelection = Color.FromArgb(31, 54, 72);
        public static readonly Color DarkBorder = Color.FromArgb(58, 76, 91);
        public static readonly Color DarkBorderStrong = Color.FromArgb(80, 99, 115);
        public static readonly Color DarkTextPrimary = Color.FromArgb(235, 241, 246);
        public static readonly Color DarkTextSecondary = Color.FromArgb(174, 188, 199);
        public static readonly Color DarkAccent = Color.FromArgb(55, 145, 255);
        public static readonly Color DarkAccentSoft = Color.FromArgb(92, 184, 224);
        public static readonly Color DarkDanger = Color.FromArgb(235, 150, 150);

        /// <summary>
        /// Gets the theme color for window background elements.
        /// </summary>
        /// <param name="darkMode">True to use the dark palette; false to use the light palette.</param>
        /// <returns>The window background color for the selected theme.</returns>
        public static Color WindowBackground(bool darkMode) =>
            darkMode ? DarkWindow : SystemColors.Control;

        /// <summary>
        /// Gets the theme color for sidebar background elements.
        /// </summary>
        /// <param name="darkMode">True to use the dark palette; false to use the light palette.</param>
        /// <returns>The sidebar background color for the selected theme.</returns>
        public static Color SidebarBackground(bool darkMode) =>
            darkMode ? DarkSidebar : Color.FromArgb(238, 241, 245);

        /// <summary>
        /// Gets the theme color for panel background elements.
        /// </summary>
        /// <param name="darkMode">True to use the dark palette; false to use the light palette.</param>
        /// <returns>The panel background color for the selected theme.</returns>
        public static Color PanelBackground(bool darkMode) =>
            darkMode ? DarkPanel : Color.White;

        /// <summary>
        /// Gets the theme color for input background elements.
        /// </summary>
        /// <param name="darkMode">True to use the dark palette; false to use the light palette.</param>
        /// <returns>The input background color for the selected theme.</returns>
        public static Color InputBackground(bool darkMode) =>
            darkMode ? DarkPanel : SystemColors.Window;

        /// <summary>
        /// Gets the theme color for control background elements.
        /// </summary>
        /// <param name="darkMode">True to use the dark palette; false to use the light palette.</param>
        /// <returns>The control background color for the selected theme.</returns>
        public static Color ControlBackground(bool darkMode) =>
            darkMode ? DarkControl : SystemColors.Control;

        /// <summary>
        /// Gets the theme color for control hover elements.
        /// </summary>
        /// <param name="darkMode">True to use the dark palette; false to use the light palette.</param>
        /// <returns>The control hover color for the selected theme.</returns>
        public static Color ControlHover(bool darkMode) =>
            darkMode ? DarkControlHover : Color.FromArgb(230, 230, 230);

        /// <summary>
        /// Gets the theme color for control pressed elements.
        /// </summary>
        /// <param name="darkMode">True to use the dark palette; false to use the light palette.</param>
        /// <returns>The control pressed color for the selected theme.</returns>
        public static Color ControlPressed(bool darkMode) =>
            darkMode ? DarkControlPressed : Color.FromArgb(215, 225, 235);

        /// <summary>
        /// Gets the theme color for selection background elements.
        /// </summary>
        /// <param name="darkMode">True to use the dark palette; false to use the light palette.</param>
        /// <returns>The selection background color for the selected theme.</returns>
        public static Color SelectionBackground(bool darkMode) =>
            darkMode ? DarkSelection : Color.FromArgb(214, 229, 245);

        /// <summary>
        /// Gets the theme color for border elements.
        /// </summary>
        /// <param name="darkMode">True to use the dark palette; false to use the light palette.</param>
        /// <returns>The border color for the selected theme.</returns>
        public static Color Border(bool darkMode) =>
            darkMode ? DarkBorder : Color.FromArgb(180, 180, 180);

        /// <summary>
        /// Gets the theme color for text primary elements.
        /// </summary>
        /// <param name="darkMode">True to use the dark palette; false to use the light palette.</param>
        /// <returns>The text primary color for the selected theme.</returns>
        public static Color TextPrimary(bool darkMode) =>
            darkMode ? DarkTextPrimary : SystemColors.ControlText;

        /// <summary>
        /// Gets the theme color for text secondary elements.
        /// </summary>
        /// <param name="darkMode">True to use the dark palette; false to use the light palette.</param>
        /// <returns>The text secondary color for the selected theme.</returns>
        public static Color TextSecondary(bool darkMode) =>
            darkMode ? DarkTextSecondary : Color.DimGray;

        /// <summary>
        /// Gets the theme color for menu background elements.
        /// </summary>
        /// <param name="darkMode">True to use the dark palette; false to use the light palette.</param>
        /// <returns>The menu background color for the selected theme.</returns>
        public static Color MenuBackground(bool darkMode) =>
            darkMode ? DarkPanel : SystemColors.Control;

        /// <summary>
        /// Gets the theme color for list background elements.
        /// </summary>
        /// <param name="darkMode">True to use the dark palette; false to use the light palette.</param>
        /// <returns>The list background color for the selected theme.</returns>
        public static Color ListBackground(bool darkMode) =>
            darkMode ? DarkWindow : Color.White;

        /// <summary>
        /// Gets the theme color for list alternate background elements.
        /// </summary>
        /// <param name="darkMode">True to use the dark palette; false to use the light palette.</param>
        /// <returns>The list alternate background color for the selected theme.</returns>
        public static Color ListAlternateBackground(bool darkMode) =>
            darkMode ? Color.FromArgb(21, 30, 38) : Color.FromArgb(248, 248, 248);

        /// <summary>
        /// Gets the theme color for list hover background elements.
        /// </summary>
        /// <param name="darkMode">True to use the dark palette; false to use the light palette.</param>
        /// <returns>The list hover background color for the selected theme.</returns>
        public static Color ListHoverBackground(bool darkMode) =>
            darkMode ? DarkControlHover : Color.FromArgb(232, 241, 250);
    }
}
