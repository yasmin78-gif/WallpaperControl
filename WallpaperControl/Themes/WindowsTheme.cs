using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace WallpaperControl
{
    internal static class WindowsTheme
    {
        /// <summary>
        /// Reads the Windows app theme, falling back to light when unavailable.
        /// </summary>
        /// <returns>True when Windows prefers dark application surfaces; otherwise, false.</returns>
        internal static bool IsDarkMode()
        {
            try
            {
                using RegistryKey? key =
                    Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

                object? value =
                    key?.GetValue(
                        "AppsUseLightTheme");

                return value != null &&
                       Convert.ToInt32(value) == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Applies a native title-bar theme only after the form has a window handle.
        /// </summary>
        /// <param name="form">The window whose native state or test controls are accessed.</param>
        /// <param name="darkMode">True to use the dark palette; false to use the light palette.</param>
        internal static void ApplyTitleBar(Form form, bool darkMode)
        {
            if (!form.IsHandleCreated) return;
            int value = darkMode ? 1 : 0;
            DwmSetWindowAttribute(form.Handle, 20, ref value, sizeof(int));
        }

        /// <summary>
        /// Sets a Desktop Window Manager attribute on the specified native window.
        /// </summary>
        /// <param name="hwnd">The native window handle used by the operation.</param>
        /// <param name="attribute">The native Desktop Window Manager attribute identifier.</param>
        /// <param name="value">The attribute value passed to the Desktop Window Manager.</param>
        /// <param name="size">The attribute buffer size in bytes.</param>
        /// <returns>The HRESULT status code; zero indicates success.</returns>
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    }
}
