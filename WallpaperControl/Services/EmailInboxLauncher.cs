using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WallpaperControl;

internal static class EmailInboxLauncher
{
    internal static async Task OpenProgramAsync(string path)
    {
        if (!EmailAccountSettings.IsMailProgramPath(path)) throw new FileNotFoundException();
        nint window = await Task.Run(() => FindWindow(path));
        if (window != 0)
        {
            if (IsIconic(window)) ShowWindowAsync(window, 9);
            if (!SetForegroundWindow(window)) AppLogger.Info("Email watcher: existing mail window activation was declined by Windows.");
            return;
        }
        // Also lets single-instance clients restore a window hidden in the tray.
        using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path)! });
    }

    private static nint FindWindow(string path)
    {
        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(path));
        try {
            foreach (var process in processes)
            {
                try {
                    if (process.MainWindowHandle != 0 && string.Equals(process.MainModule?.FileName, path, StringComparison.OrdinalIgnoreCase))
                        return process.MainWindowHandle;
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
            }
        }
        finally { foreach (var process in processes) process.Dispose(); }
        return 0;
    }

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindowAsync(nint window, int command);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(nint window);
}
