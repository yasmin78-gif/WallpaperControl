using ZXing.Net.Maui.Controls;
namespace RemoteExperiment.AndroidApp;
public static class MauiProgram
{
    public static MauiApp CreateMauiApp() => MauiApp.CreateBuilder().UseMauiApp<App>().UseBarcodeReader().Build();
}
public sealed class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var page = new MainPage(); var window = new Window(new NavigationPage(page));
        window.Resumed += (_, _) => page.Resume(); window.Stopped += (_, _) => page.Suspend(); return window;
    }
}
