namespace WallpaperControl;

internal sealed partial class WidgetManager
{
    private PackageTrackingService? packages;
    private PackageTrackingWidgetForm? packageWidget;
    private bool packageDialogOpen;
    private readonly TrackingCredentialStore packageCredentialStore;
    private TrackingCredentialStore PackageCredentials => packageCredentialStore;
    // Ship24 documents up to one minute for first tracking results; allow that without retrying POST.
    private PackageTrackingService Packages => packages ??= new(new TrackingStore(), new Ship24TrackingProvider(PackageCredentials, timeout: TimeSpan.FromSeconds(75)));
    internal bool PackageHasCredential() => PackageCredentials.Load("ship24", out _) is TrackingCredentialLoadResult.Loaded or TrackingCredentialLoadResult.Recovered;
    internal void ShowPackages(IWin32Window? owner, string language, string action)
    {
        if (packageDialogOpen) return;
        packageDialogOpen = true;
        try
        {
            if (action is "PackageSetup" or "PackageTest" || !PackageHasCredential() && action == "PackageAdd")
            { using var setup = new Ship24SetupForm(PackageCredentials, language, testOnShown: action == "PackageTest"); setup.ShowDialog(owner); if (action != "PackageAdd" || !PackageHasCredential()) return; }
            if (action == "PackageAdd") { using var editor = new PackageEditorForm(Packages, null, language); editor.ShowDialog(owner); }
            else { using var manager = new PackageManagerForm(Packages, language); manager.ShowDialog(owner); }
        }
        finally { packageDialogOpen = false; }
    }
    private void PackageDetails(Guid id)
    {
        if (packageDialogOpen) return;
        packageDialogOpen = true;
        try { using var detail = new PackageDetailsForm(Packages, id, settings.ClockLanguageCode); detail.ShowDialog(); }
        finally { packageDialogOpen = false; }
    }
    private void ApplyPackageWidget(WidgetSettings target, bool restoreLocations)
    {
        if (!target.PackageEnabled) { packageWidget?.Close(); packageWidget?.Dispose(); packageWidget = null; return; }
        if (packageWidget == null || packageWidget.IsDisposed)
        {
            packageWidget = new PackageTrackingWidgetForm(Packages, target, p => { settings.PackageLocation = p; if (!previewMode) settings.Save(registryPath); }, PackageDetails,
                () => ShowPackages(null, settings.ClockLanguageCode, "PackageAdd"));
            packageWidget.SetActivitySuspended(activitySuspended); RegisterDesktopWidget(packageWidget); packageWidget.Show();
            if (!DesktopWidgetNative.AttachToDesktop(packageWidget, target.PackageLocation)) { packageWidget.Hide(); AppLogger.Info("Package widget desktop attachment failed."); }
        }
        else
        {
            packageWidget.Apply(target);
            if (restoreLocations) packageWidget.Location = WidgetSettings.EnsureVisible(target.PackageLocation, packageWidget.Size);
            DesktopWidgetNative.KeepOnDesktop(packageWidget);
        }
        if (previewMode) DesktopWidgetNative.EnableInteraction(packageWidget);
    }
}
