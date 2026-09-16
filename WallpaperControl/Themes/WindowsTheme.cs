using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace WallpaperControl
{
    internal static class WindowsTheme
    {
        /// <summary>Reads the Windows app theme, falling back to light when unavailable.</summary>
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

        /// <summary>Applies a native title-bar theme only after the form has a window handle.</summary>
        internal static void ApplyTitleBar(Form form, bool darkMode)
        {
            if (!form.IsHandleCreated) return;
            int value = darkMode ? 1 : 0;
            DwmSetWindowAttribute(form.Handle, 20, ref value, sizeof(int));
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    }
}
