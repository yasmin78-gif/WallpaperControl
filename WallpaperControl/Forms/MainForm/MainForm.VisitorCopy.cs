namespace WallpaperControl;

public partial class MainForm
{
    private readonly Button visitorCopyButton = new MainFormButton();
    private bool visitorCopyRunning;
    private void UpdateVisitorCopyButton()
    {
        if (visitorMode == null) return;
        visitorCopyButton.Visible = !visitorMode.Active && VisitorFolderCopy.HasDestination(visitorMode.Settings);
        visitorCopyButton.Enabled = visitorCopyButton.Visible && !visitorCopyRunning && wallpaperOwnership.AllowsImages
            && File.Exists(GetCurrentWallpaperPath());
        visitorCopyButton.Text = Localization.Get("VisitorCopyImage");
        toolTip.SetToolTip(visitorCopyButton, visitorMode.Settings.Path);
    }
    private async Task CopyCurrentWallpaperToVisitorFolderAsync()
    {
        if (visitorMode?.Active != false || !wallpaperOwnership.AllowsImages || visitorCopyRunning
            || !VisitorFolderCopy.HasDestination(visitorMode.Settings)) return;
        string? source = GetCurrentWallpaperPath();
        if (source == null) return;
        visitorCopyRunning = true; UpdateVisitorCopyButton();
        try
        {
            string target = await VisitorFolderCopy.CopyAsync(source, visitorMode.Settings);
            if (!IsDisposed) MessageBox.Show(this, Localization.Get("VisitorCopySuccess") + Environment.NewLine + target,
                Localization.Get("VisitorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppLogger.Warning("Could not copy image to visitor folder.", ex);
            if (!IsDisposed) MessageBox.Show(this, Localization.Get("VisitorCopyError"), Localization.Get("VisitorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { visitorCopyRunning = false; if (!IsDisposed) UpdateVisitorCopyButton(); }
    }
}
