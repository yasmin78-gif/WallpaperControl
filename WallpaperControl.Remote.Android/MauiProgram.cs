using ZXing.Net.Maui.Controls;
namespace WallpaperControl.Remote.AndroidApp;
public static class MauiProgram
{
    public static MauiApp CreateMauiApp() => MauiApp.CreateBuilder().UseMauiApp<RemoteApp>().UseBarcodeReader().Build();
}
public sealed class RemoteApp : Application
{
    public RemoteApp() { UserAppTheme = AppTheme.Dark; }
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var page = new MainPage(); var window = new Window(new NavigationPage(page) { BarBackgroundColor = Color.FromArgb("#10141C"), BarTextColor = Colors.White });
        window.Resumed += (_, _) => page.Resume(); window.Stopped += (_, _) => page.Suspend(); return window;
    }
}
