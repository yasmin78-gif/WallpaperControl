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

    [ComImport]
    [Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
    [InterfaceType(
        ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDesktopWallpaper
    {
        void SetWallpaper(
            [MarshalAs(UnmanagedType.LPWStr)]
            string? monitorID,
            [MarshalAs(UnmanagedType.LPWStr)]
            string wallpaper);

        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetWallpaper(
            [MarshalAs(UnmanagedType.LPWStr)]
            string? monitorID);

        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetMonitorDevicePathAt(
            uint monitorIndex);

        uint GetMonitorDevicePathCount();

        void GetMonitorRECT(
            [MarshalAs(UnmanagedType.LPWStr)]
            string monitorID,
            out RECT displayRect);

        void SetBackgroundColor(
            uint color);

        uint GetBackgroundColor();

        void SetPosition(
            DesktopWallpaperPosition position);

        DesktopWallpaperPosition GetPosition();

        void SetSlideshow(
            [MarshalAs(UnmanagedType.Interface)]
            IShellItemArray items);

        void GetSlideshow(
            [MarshalAs(UnmanagedType.Interface)]
            out IShellItemArray items);

        void SetSlideshowOptions(
            DesktopSlideshowOptions options,
            uint slideshowTick);

        void GetSlideshowOptions(
            out DesktopSlideshowOptions options,
            out uint slideshowTick);

        void AdvanceSlideshow(
            [MarshalAs(UnmanagedType.LPWStr)]
            string? monitorID,
            DesktopSlideshowDirection direction);

        void GetStatus(
            out DesktopSlideshowState state);

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
        void BindToHandler(
            IntPtr pbc,
            ref Guid bhid,
            ref Guid riid,
            out IntPtr ppv);

        void GetParent(
            out IShellItem ppsi);

        void GetDisplayName(
            SIGDN sigdnName,
            out IntPtr ppszName);

        void GetAttributes(
            uint sfgaoMask,
            out uint psfgaoAttribs);

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
        void BindToHandler(
            IntPtr pbc,
            ref Guid bhid,
            ref Guid riid,
            out IntPtr ppvOut);

        void GetPropertyStore(
            int flags,
            ref Guid riid,
            out IntPtr ppv);

        void GetPropertyDescriptionList(
            IntPtr keyType,
            ref Guid riid,
            out IntPtr ppv);

        void GetAttributes(
            uint attribFlags,
            uint sfgaoMask,
            out uint psfgaoAttribs);

        void GetCount(
            out uint pdwNumItems);

        void GetItemAt(
            uint dwIndex,
            out IShellItem ppsi);

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
