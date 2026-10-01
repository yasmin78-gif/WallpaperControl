using System.Diagnostics;

namespace WallpaperControl;

public partial class MainForm
{
    private Process? videoLiveTestProcess;
    private ToolStripMenuItem? videoLiveTestItem;

    private void OpenVideoLiveTest()
    {
        if (!wallpaperOwnership.AllowsImages) return;
        try
        {
            if (videoLiveTestProcess is { HasExited: false })
            {
                MessageBox.Show(this, Localization.Get("VideoLiveTestAlreadyRunning"),
                    Localization.Get("VideoLiveTest"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            videoLiveTestProcess?.Dispose();
            var start = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            start.ArgumentList.Add(VideoWallpaperTest.VideoTestEntry.Switch);
            start.ArgumentList.Add("--video-test-parent");
            start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            using var parent = Process.GetCurrentProcess();
            start.ArgumentList.Add(parent.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
            videoLiveTestProcess = Process.Start(start);
        }
        catch (Exception ex)
        {
            AppLogger.Warning("Could not start isolated video wallpaper live test.", ex);
            MessageBox.Show(this, Localization.Get("VideoLiveTestStartFailed"),
                Localization.Get("VideoLiveTest"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void StopVideoLiveTest()
    {
        if (videoLiveTestProcess == null) return;
        try
        {
            // Kill only our tracked test child. Windows removes its HWNDs even if MF is blocked.
            if (!videoLiveTestProcess.HasExited) videoLiveTestProcess.Kill();
        }
        catch (Exception ex) { AppLogger.Warning("Could not stop video wallpaper live test.", ex); }
        finally { videoLiveTestProcess.Dispose(); videoLiveTestProcess = null; }
    }
}
