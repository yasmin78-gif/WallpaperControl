using System.Diagnostics;
namespace WallpaperControl.VideoWallpaperTest;

/// <summary>Explicit test subprocess entry point, before production mutex and services.</summary>
internal static class VideoTestEntry
{
    internal const string Switch = "--video-wallpaper-test";
    internal static bool TryRun(string[] arguments)
    {
        if (!arguments.Contains(Switch, StringComparer.OrdinalIgnoreCase)) return false;
        ApplicationConfiguration.Initialize();
        if (!Environment.Is64BitProcess)
        {
            MessageBox.Show("Der Video-Live-Test benötigt die x64-Version.", "Video-Hintergrund testen");
            return true;
        }
        using var instance = new SingleInstanceGuard(@"Local\WallpaperControl.VideoLiveTest." + SingleInstanceGuard.UserKey);
        if (!instance.IsPrimary)
        {
            MessageBox.Show("Der Video-Live-Test läuft bereits. Öffne sein Steuerfenster über die Taskleiste.",
                "Video-Hintergrund testen", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }
        Process? parent = null;
        int parentArgument = Array.IndexOf(arguments, "--video-test-parent");
        if (parentArgument >= 0)
        {
            if (arguments.Length <= parentArgument + 2 ||
                !int.TryParse(arguments[parentArgument + 1], out int parentId) ||
                !long.TryParse(arguments[parentArgument + 2], out long parentStart)) return true;
            try
            {
                parent = Process.GetProcessById(parentId);
                if (parent.StartTime.ToUniversalTime().Ticks != parentStart) { parent.Dispose(); return true; }
            }
            catch (Exception ex) when (ex is ArgumentException or System.ComponentModel.Win32Exception or InvalidOperationException)
            { parent?.Dispose(); return true; }
        }
        using (parent)
        using (var lifetime = new CancellationTokenSource())
        using (var window = new ControlForm())
        {
            int smokeArgument = Array.IndexOf(arguments, "--video-test-smoke");
            if (smokeArgument >= 0 && arguments.Length > smokeArgument + 1)
                window.ConfigureSmoke(arguments[smokeArgument + 1]);
            // Independent of the UI thread: removes orphaned/blocked video surfaces.
            Task watchdog = Task.Run(async () =>
            {
                try
                {
                    while (!lifetime.IsCancellationRequested)
                    {
                        if ((parent?.HasExited ?? false) || Environment.TickCount64-window.LastPulse>30000)
                        { Environment.Exit(0); return; }
                        await Task.Delay(1000, lifetime.Token);
                    }
                }
                catch (OperationCanceledException) { }
            });
            Application.Run(window);
            lifetime.Cancel();
            watchdog.GetAwaiter().GetResult();
        }
        return true;
    }
}
