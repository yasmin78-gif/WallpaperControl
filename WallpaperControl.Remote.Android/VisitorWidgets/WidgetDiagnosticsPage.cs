namespace WallpaperControl.Remote.AndroidApp;

// A normal navigation page, with a readable snapshot before any asynchronous/network work.
// Do not show an alert on the underlying MainPage while the settings page is on top.
internal sealed class WidgetDiagnosticsPage : ContentPage
{
    private readonly Editor report = new() { IsReadOnly = true, AutoSize = EditorAutoSizeOption.TextChanges,
        TextColor = Colors.White, BackgroundColor = Color.FromArgb("#1C2330"), MinimumHeightRequest = 360, FontSize = 14 };
    public WidgetDiagnosticsPage()
    {
        Title = "Widget-Fehlerbericht"; BackgroundColor = Color.FromArgb("#10141C");
        var refresh = new Button { Text = "Zustand neu abfragen (ohne Steuerbefehl)" };
        var copy = new Button { Text = "Bericht kopieren" };
        var body = new VerticalStackLayout { Padding = 18, Spacing = 16 };
        body.Add(new Label { Text = "Bitte zuerst den vorhandenen Bericht kopieren. Eine neue Abfrage verändert den aktuellen Status, aber nicht den letzten Widget-Aufruf.", TextColor = Colors.White });
        body.Add(report); body.Add(copy); body.Add(refresh); Content = new ScrollView { Content = body };
        ReadReport();
        copy.Clicked += async (_, _) =>
        {
            try { await Clipboard.Default.SetTextAsync(report.Text ?? ""); copy.Text = "Bericht kopiert"; }
            catch (Exception ex) { report.Text += "\nKopieren fehlgeschlagen: " + ex.GetType().Name; }
        };
        refresh.Clicked += async (_, _) =>
        {
            refresh.IsEnabled = false;
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(7));
                await WidgetRuntime.Controller.RefreshAsync(deadline.Token);
                WidgetRuntime.RenderAll(); ReadReport();
            }
            catch (Exception ex) { report.Text += "\nAbfrage fehlgeschlagen: " + ex.GetType().Name; }
            finally { refresh.IsEnabled = true; }
        };
    }
    private void ReadReport()
    {
        try { report.Text = "Version " + AppInfo.Current.VersionString + "\n\n" + WidgetRuntime.Diagnostics; }
        catch (Exception ex) { report.Text = "Diagnose konnte nicht gelesen werden.\nFehlertyp: " + ex.GetType().Name +
            (ex.InnerException == null ? "" : "\nInnerer Fehlertyp: " + ex.InnerException.GetType().Name); }
    }
}
