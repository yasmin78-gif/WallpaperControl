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
        /// <param name="comObject">The COM wrapper to release when it is non-null and managed by COM interop.</param>
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
        /// <param name="hWnd">The native window handle used by the operation.</param>
        /// <param name="nCmdShow">The native command controlling visibility and restored or minimized state.</param>
        /// <returns>True if the window was previously visible; otherwise, false.</returns>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(
            IntPtr hWnd,
            int nCmdShow);

        /// <summary>
        /// Requests foreground activation for the specified native window.
        /// </summary>
        /// <param name="hWnd">The native window handle used by the operation.</param>
        /// <returns>True if the window was brought to the foreground; otherwise, false.</returns>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(
            IntPtr hWnd);

        /// <summary>
        /// Creates a shell item for a filesystem path.
        /// </summary>
        /// <param name="pszPath">The filesystem path to parse into a shell item.</param>
        /// <param name="pbc">The optional shell binding context, or zero when no context is supplied.</param>
        /// <param name="riid">The interface identifier requested from the shell operation.</param>
        /// <param name="ppv">Receives the requested shell object or interface pointer.</param>
        /// <returns>The HRESULT status code; zero indicates success.</returns>
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
        /// <param name="psi">The shell item to compare or wrap in a collection.</param>
        /// <param name="riid">The interface identifier requested from the shell operation.</param>
        /// <param name="ppv">Receives the requested shell object or interface pointer.</param>
        /// <returns>The HRESULT status code; zero indicates success.</returns>
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
