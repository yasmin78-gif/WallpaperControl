using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Main-window native responsibilities; see README.md in this directory for the code map.
    public partial class MainForm
    {
        /// <summary>
        /// Releases a non-null COM wrapper after desktop or shell operations.
        /// </summary>
        private static void ReleaseComObject(
            object? comObject)
        {
            if (comObject != null &&
                Marshal.IsComObject(comObject))
            {
                Marshal.ReleaseComObject(
                    comObject);
            }
        }

        /// <summary>
        /// Changes the native window visibility or restore state.
        /// </summary>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(
            IntPtr hWnd,
            int nCmdShow);

        /// <summary>
        /// Requests foreground activation for the specified native window.
        /// </summary>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(
            IntPtr hWnd);

        /// <summary>
        /// Creates a shell item for a filesystem path.
        /// </summary>
        [DllImport(
            "shell32.dll",
            CharSet = CharSet.Unicode,
            PreserveSig = true)]
        private static extern int
            SHCreateItemFromParsingName(
                string pszPath,
                IntPtr pbc,
                ref Guid riid,
                [MarshalAs(UnmanagedType.Interface)]
                out IShellItem ppv);

        /// <summary>
        /// Wraps a shell item in the collection required by the Windows slideshow API.
        /// </summary>
        [DllImport(
            "shell32.dll",
            PreserveSig = true)]
        private static extern int
            SHCreateShellItemArrayFromShellItem(
                [MarshalAs(UnmanagedType.Interface)]
                IShellItem psi,
                ref Guid riid,
                [MarshalAs(UnmanagedType.Interface)]
                out IShellItemArray ppv);
    }
}
