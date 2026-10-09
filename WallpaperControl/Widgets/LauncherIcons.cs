using System.Runtime.InteropServices;
namespace WallpaperControl;
internal static class LauncherIcons
{
    // Run on a worker. The owned HICON is always freed after making a managed copy.
    internal static Bitmap Load(LauncherEntry entry)
    {
        if(LauncherTargets.IsWebsite(entry.Target))return SystemIcons.Information.ToBitmap();
        var info=new ShellInfo();
        int initialized=CoInitializeEx(IntPtr.Zero,0);
        try
        {
            if(SHGetFileInfo(entry.Target,0,ref info,(uint)Marshal.SizeOf<ShellInfo>(),0x100)!=IntPtr.Zero&&info.Icon!=IntPtr.Zero)
            { using var icon=Icon.FromHandle(info.Icon); return icon.ToBitmap(); }
        }
        finally { if(info.Icon!=IntPtr.Zero)DestroyIcon(info.Icon); if(initialized>=0)CoUninitialize(); }
        return SystemIcons.Application.ToBitmap();
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct ShellInfo
    { public IntPtr Icon; public int Index; public uint Attributes; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string DisplayName; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=80)] public string TypeName; }
    [DllImport("shell32.dll",EntryPoint="SHGetFileInfoW",CharSet=CharSet.Unicode)] private static extern IntPtr SHGetFileInfo(string path,uint attributes,ref ShellInfo info,uint size,uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved,uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}
