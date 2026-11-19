namespace WallpaperControl;

internal sealed partial class WidgetManager
{
    private readonly PriceWatchTestData priceWatchTestData = new();
    private PriceWatchWidgetForm? priceWatchWidget;
    private HttpClient? priceWatchHttp;
    private PriceWatchClient? priceWatchClient;
    private PriceWatchStore? priceWatchStore;
    private int[]? previousPriceWatchShops;
    internal Action<PriceWatchAlert, string>? ShowPriceWatchNotification { get; set; }
    internal Action? ClearPriceWatchNotifications { get; set; }
    internal event Action<PriceWatchAlert>? SimulatedPriceWatchNotification;

    private IPriceWatchSource GetPriceWatchSource(bool live)
    {
        if (!live) return priceWatchTestData;
        priceWatchHttp ??= PriceWatchClient.CreateHttpClient();
        return priceWatchClient ??= new(priceWatchHttp, PriceWatchCredentials.Current.Load);
    }

    private void ApplyPriceWatch(WidgetSettings target, bool restoreLocations)
    {
        if (!target.PriceWatch.Enabled)
        {
            if (priceWatchWidget != null) previousPriceWatchShops = priceWatchWidget.SelectedShops;
            DisposePriceWatch();
            return;
        }

        if (priceWatchWidget == null || priceWatchWidget.IsDisposed)
        {
            priceWatchWidget = new(GetPriceWatchSource, target.PriceWatch, target.ClockLanguageCode, point =>
            {
                settings.PriceWatch.Location = point;
                if (!previewMode) settings.Save(registryPath);
            }, priceWatchStore ??= new(PriceWatchStore.DefaultPath), monitorPrices: true, alert: (notice, live) =>
            {
                if (live) ShowPriceWatchNotification?.Invoke(notice, settings.ClockLanguageCode);
                else SimulatedPriceWatchNotification?.Invoke(notice);
            }, clearPriceAlerts: () => ClearPriceWatchNotifications?.Invoke());
            priceWatchWidget.Apply(target.PriceWatch, target.ClockLanguageCode, previewMode);
            priceWatchWidget.SetSuspended(activitySuspended || packagePowerSuspended);
            priceWatchWidget.FitToScreen(target.PriceWatch.Location);
            RegisterDesktopWidget(priceWatchWidget);
            priceWatchWidget.Show();
            if (previousPriceWatchShops != null)
                priceWatchWidget.RestoreShopRefreshBaseline(previousPriceWatchShops);
            if (!DesktopWidgetNative.AttachToDesktop(priceWatchWidget, priceWatchWidget.Location))
                priceWatchWidget.Hide();
        }
        else
        {
            priceWatchWidget.Apply(target.PriceWatch, target.ClockLanguageCode, previewMode);
            if (restoreLocations) priceWatchWidget.FitToScreen(target.PriceWatch.Location);
            DesktopWidgetNative.KeepOnDesktop(priceWatchWidget);
        }
        if (previewMode) DesktopWidgetNative.EnableInteraction(priceWatchWidget);
        if (previewMode || !target.PriceWatch.AutomaticMonitoring || !target.PriceWatch.PriceNotifications)
            ClearPriceWatchNotifications?.Invoke();
    }

    private void DisposePriceWatch()
    {
        ClearPriceWatchNotifications?.Invoke();
        priceWatchWidget?.Dispose();
        priceWatchWidget = null;
        priceWatchClient = null;
        priceWatchHttp?.Dispose();
        priceWatchHttp = null;
    }
}
