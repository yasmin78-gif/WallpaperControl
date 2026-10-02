namespace WallpaperControl;

internal sealed partial class WidgetManager
{
    private PackageTrackingService? packages;
    private PackageRefreshScheduler? packageScheduler;
    private bool packagePowerSuspended;
    private PackageStatusNotifications? packageNotifications;
    private bool packageAlertsEnabled;
    internal Action<TrackedShipment, string>? ShowPackageNotification { get; set; }
    internal Action? ClearPackageNotifications { get; set; }
    internal void SetPackagePowerSuspended(bool value)
    { packagePowerSuspended = value; packageNotifications?.SetSuspended(value || activitySuspended); if (value) ClearPackageNotifications?.Invoke(); packageScheduler?.SetPowerSuspended(value); }
    private PackageTrackingWidgetForm? packageWidget;
    private bool packageDialogOpen;
    private readonly TrackingCredentialStore packageCredentialStore;
    private TrackingCredentialStore PackageCredentials => packageCredentialStore;
    // Ship24 documents up to one minute for first tracking results; allow that without retrying POST.
    private PackageTrackingService Packages
    {
        get
        {
            packages ??= new(new TrackingStore(), new Ship24TrackingProvider(PackageCredentials, timeout: TimeSpan.FromSeconds(75)));
            packageNotifications ??= new(packages, shipment => ShowPackageNotification?.Invoke(shipment, settings.ClockLanguageCode));
            packageNotifications.Configure(packageAlertsEnabled); packageNotifications.SetSuspended(activitySuspended || packagePowerSuspended);
            return packages;
        }
    }
    internal void OpenPackageNotification(Guid id) { if (Packages.Shipments.Any(shipment => shipment.Id == id)) PackageDetails(id); }
    internal bool PackageHasCredential() => PackageCredentials.Load("ship24", out _) is TrackingCredentialLoadResult.Loaded or TrackingCredentialLoadResult.Recovered;
    internal void ShowPackages(IWin32Window? owner, string language, string action)
    {
        if (packageDialogOpen) return;
        packageDialogOpen = true;
        try
        {
            if (action is "PackageSetup" or "PackageTest")
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
        packageAlertsEnabled = target.PackageStatusNotifications;
        if (packages != null) { _ = Packages; }
        if (!packageAlertsEnabled) ClearPackageNotifications?.Invoke();
        if (!target.PackageEnabled)
        {
            packageScheduler?.Dispose(); packageScheduler = null;
            packageWidget?.Close(); packageWidget?.Dispose(); packageWidget = null; return;
        }
        if (packageScheduler == null)
        {
            packageScheduler = new PackageRefreshScheduler(Packages);
            packageScheduler.SetSuspended(activitySuspended);
            packageScheduler.SetPowerSuspended(packagePowerSuspended);
        }
        packageScheduler.Configure(target.PackageAutomaticRefresh, target.PackageRefreshMinutes);
        if (packageWidget == null || packageWidget.IsDisposed)
        {
            packageWidget = new PackageTrackingWidgetForm(Packages, target, p => { settings.PackageLocation = p; if (!previewMode) settings.Save(registryPath); }, PackageDetails,
                () => ShowPackages(null, settings.ClockLanguageCode, "PackageAdd"));
            packageWidget.Disposed += (_, _) => { packageScheduler?.Dispose(); packageScheduler = null; };
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
