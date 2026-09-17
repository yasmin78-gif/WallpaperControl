using System;
using System.Runtime.InteropServices;

namespace WallpaperControl
{
    [ComImport]
    [Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD")]
    internal class DesktopWallpaper
    {
    }

    internal enum DesktopSlideshowDirection
    {
        Forward = 0,
        Backward = 1
    }

    internal enum DesktopWallpaperPosition : uint
    {
        Center = 0,
        Tile = 1,
        Stretch = 2,
        Fit = 3,
        Fill = 4,
        Span = 5
    }

    [Flags]
    internal enum DesktopSlideshowOptions : uint
    {
        None = 0,
        ShuffleImages = 0x01
    }

    [Flags]
    internal enum DesktopSlideshowState : uint
    {
        None = 0,
        Enabled = 0x01,
        Slideshow = 0x02,
        DisabledByRemoteSession = 0x04
    }

    // COM dispatch depends on declaration order: keep these methods in the
    // native IDesktopWallpaper vtable order, including unused members.
    [ComImport]
    [Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
    [InterfaceType(
        ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDesktopWallpaper
    {
        /// <summary>
        /// Sets the wallpaper image for one monitor or all monitors.
        /// </summary>
        /// <param name="monitorID">The monitor device path; APIs that permit null apply their documented all-monitor behavior.</param>
        /// <param name="wallpaper">The wallpaper image path assigned through the desktop API.</param>
        void SetWallpaper(
            [MarshalAs(UnmanagedType.LPWStr)]
            string? monitorID,
            [MarshalAs(UnmanagedType.LPWStr)]
            string wallpaper);

        /// <summary>
        /// Reads the wallpaper path assigned to the specified monitor.
        /// </summary>
        /// <param name="monitorID">The monitor device path; APIs that permit null apply their documented all-monitor behavior.</param>
        /// <returns>The assigned wallpaper path returned by Windows.</returns>
        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetWallpaper(
            [MarshalAs(UnmanagedType.LPWStr)]
            string? monitorID);

        /// <summary>
        /// Retrieves a monitor&apos;s device path by its zero-based index.
        /// </summary>
        /// <param name="monitorIndex">The zero-based monitor index.</param>
        /// <returns>The monitor device path used by other desktop wallpaper operations.</returns>
        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetMonitorDevicePathAt(
            uint monitorIndex);

        /// <summary>
        /// Retrieves the number of monitors exposed by the desktop wallpaper API.
        /// </summary>
        /// <returns>The monitor count reported by Windows.</returns>
        uint GetMonitorDevicePathCount();

        /// <summary>
        /// Reads the screen-space rectangle for the specified monitor.
        /// </summary>
        /// <param name="monitorID">The monitor device path; APIs that permit null apply their documented all-monitor behavior.</param>
        /// <param name="displayRect">Receives the monitor rectangle in screen coordinates.</param>
        void GetMonitorRECT(
            [MarshalAs(UnmanagedType.LPWStr)]
            string monitorID,
            out RECT displayRect);

        /// <summary>
        /// Sets the desktop background color used around wallpaper images.
        /// </summary>
        /// <param name="color">The desktop background color encoded as a Windows COLORREF value.</param>
        void SetBackgroundColor(
            uint color);

        /// <summary>
        /// Reads the desktop background color.
        /// </summary>
        /// <returns>The native COLORREF background color.</returns>
        uint GetBackgroundColor();

        /// <summary>
        /// Sets how Windows positions wallpaper images on the desktop.
        /// </summary>
        /// <param name="position">The Windows wallpaper scaling and placement mode.</param>
        void SetPosition(
            DesktopWallpaperPosition position);

        /// <summary>
        /// Reads the current Windows wallpaper layout.
        /// </summary>
        /// <returns>The active fill, fit, stretch, tile, center, or span mode.</returns>
        DesktopWallpaperPosition GetPosition();

        /// <summary>
        /// Assigns the shell item collection used as the Windows slideshow source.
        /// </summary>
        /// <param name="items">The shell item array containing slideshow images or folders.</param>
        void SetSlideshow(
            [MarshalAs(UnmanagedType.Interface)]
            IShellItemArray items);

        /// <summary>
        /// Retrieves the shell item collection used by the Windows slideshow.
        /// </summary>
        /// <param name="items">Receives the shell item array configured for the desktop slideshow.</param>
        void GetSlideshow(
            [MarshalAs(UnmanagedType.Interface)]
            out IShellItemArray items);

        /// <summary>
        /// Sets the native slideshow options and interval.
        /// </summary>
        /// <param name="options">The native slideshow option flags, including shuffle when enabled.</param>
        /// <param name="slideshowTick">The native slideshow interval in milliseconds.</param>
        void SetSlideshowOptions(
            DesktopSlideshowOptions options,
            uint slideshowTick);

        /// <summary>
        /// Reads the native slideshow options and interval.
        /// </summary>
        /// <param name="options">The native slideshow option flags, including shuffle when enabled.</param>
        /// <param name="slideshowTick">The native slideshow interval in milliseconds.</param>
        void GetSlideshowOptions(
            out DesktopSlideshowOptions options,
            out uint slideshowTick);

        /// <summary>
        /// Requests the next or previous native slideshow image for the selected monitor.
        /// </summary>
        /// <param name="monitorID">The monitor device path; APIs that permit null apply their documented all-monitor behavior.</param>
        /// <param name="direction">Whether to move forward or backward in the slideshow.</param>
        void AdvanceSlideshow(
            [MarshalAs(UnmanagedType.LPWStr)]
            string? monitorID,
            DesktopSlideshowDirection direction);

        /// <summary>
        /// Reads the current native slideshow state flags.
        /// </summary>
        /// <param name="state">Receives the current desktop slideshow state flags.</param>
        void GetStatus(
            out DesktopSlideshowState state);

        /// <summary>
        /// Enables or disables the Windows desktop wallpaper display.
        /// </summary>
        /// <param name="enable">True to enable the desktop wallpaper; false to disable it.</param>
        /// <returns>The Boolean result reported by the desktop wallpaper interface.</returns>
        [return: MarshalAs(UnmanagedType.Bool)]
        bool Enable(
            [MarshalAs(UnmanagedType.Bool)]
            bool enable);
    }

    internal enum SIGDN : uint
    {
        FILESYSPATH = 0x80058000
    }

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(
        ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem
    {
        /// <summary>
        /// Binds a shell item to a handler exposing the requested interface.
        /// </summary>
        /// <param name="pbc">The optional shell binding context, or zero when no context is supplied.</param>
        /// <param name="bhid">The identifier of the requested shell handler.</param>
        /// <param name="riid">The interface identifier requested from the shell operation.</param>
        /// <param name="ppv">Receives the requested shell object or interface pointer.</param>
        void BindToHandler(
            IntPtr pbc,
            ref Guid bhid,
            ref Guid riid,
            out IntPtr ppv);

        /// <summary>
        /// Retrieves the parent shell item.
        /// </summary>
        /// <param name="ppsi">Receives the requested shell item.</param>
        void GetParent(
            out IShellItem ppsi);

        /// <summary>
        /// Retrieves the requested display or parsing name as a native string.
        /// </summary>
        /// <param name="sigdnName">The shell display-name format to request.</param>
        /// <param name="ppszName">Receives an allocated native name string; the caller releases it with CoTaskMemFree.</param>
        void GetDisplayName(
            SIGDN sigdnName,
            out IntPtr ppszName);

        /// <summary>
        /// Reads the shell attributes selected by the supplied mask.
        /// </summary>
        /// <param name="sfgaoMask">The shell attribute mask selecting the attributes to query.</param>
        /// <param name="psfgaoAttribs">Receives the shell attributes selected by the mask.</param>
        void GetAttributes(
            uint sfgaoMask,
            out uint psfgaoAttribs);

        /// <summary>
        /// Compares this shell item with another item using the requested hints.
        /// </summary>
        /// <param name="psi">The shell item to compare or wrap in a collection.</param>
        /// <param name="hint">The comparison hints supplied to the shell.</param>
        /// <param name="piOrder">Receives the relative ordering of the two shell items.</param>
        void Compare(
            IShellItem psi,
            uint hint,
            out int piOrder);
    }

    [ComImport]
    [Guid("B63EA76D-1F85-456F-A19C-48159EFA858B")]
    [InterfaceType(
        ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItemArray
    {
        /// <summary>
        /// Binds the shell item collection to a handler exposing the requested interface.
        /// </summary>
        /// <param name="pbc">The optional shell binding context, or zero when no context is supplied.</param>
        /// <param name="bhid">The identifier of the requested shell handler.</param>
        /// <param name="riid">The interface identifier requested from the shell operation.</param>
        /// <param name="ppvOut">Receives the requested shell handler interface pointer.</param>
        void BindToHandler(
            IntPtr pbc,
            ref Guid bhid,
            ref Guid riid,
            out IntPtr ppvOut);

        /// <summary>
        /// Retrieves the collection&apos;s property store using the requested access flags.
        /// </summary>
        /// <param name="flags">The flags controlling creation of the property store.</param>
        /// <param name="riid">The interface identifier requested from the shell operation.</param>
        /// <param name="ppv">Receives the requested shell object or interface pointer.</param>
        void GetPropertyStore(
            int flags,
            ref Guid riid,
            out IntPtr ppv);

        /// <summary>
        /// Retrieves property descriptions for the supplied property key.
        /// </summary>
        /// <param name="keyType">The native property key identifying the requested property descriptions.</param>
        /// <param name="riid">The interface identifier requested from the shell operation.</param>
        /// <param name="ppv">Receives the requested shell object or interface pointer.</param>
        void GetPropertyDescriptionList(
            IntPtr keyType,
            ref Guid riid,
            out IntPtr ppv);

        /// <summary>
        /// Reads combined attributes for the collection according to the supplied aggregation flags.
        /// </summary>
        /// <param name="attribFlags">The flags controlling how attributes are combined across the item collection.</param>
        /// <param name="sfgaoMask">The shell attribute mask selecting the attributes to query.</param>
        /// <param name="psfgaoAttribs">Receives the shell attributes selected by the mask.</param>
        void GetAttributes(
            uint attribFlags,
            uint sfgaoMask,
            out uint psfgaoAttribs);

        /// <summary>
        /// Reads the number of shell items in the collection.
        /// </summary>
        /// <param name="pdwNumItems">Receives the number of items in the shell collection.</param>
        void GetCount(
            out uint pdwNumItems);

        /// <summary>
        /// Retrieves a shell item at the requested zero-based index.
        /// </summary>
        /// <param name="dwIndex">The zero-based index of the shell item to retrieve.</param>
        /// <param name="ppsi">Receives the requested shell item.</param>
        void GetItemAt(
            uint dwIndex,
            out IShellItem ppsi);

        /// <summary>
        /// Retrieves an enumerator for the shell item collection.
        /// </summary>
        /// <param name="ppenumShellItems">Receives the shell item enumerator interface pointer.</param>
        void EnumItems(
            out IntPtr ppenumShellItems);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
