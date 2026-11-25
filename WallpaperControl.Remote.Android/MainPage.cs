using System.Text.Json;
using Microsoft.Maui.Controls.Shapes;
using ZXing.Net.Maui.Controls;

namespace WallpaperControl.Remote.AndroidApp;

public sealed class MainPage : ContentPage
{
    private static readonly Color PageBackground = Color.FromArgb("#10141C"), Card = Color.FromArgb("#1C2330"), Accent = Color.FromArgb("#8EAEFF"), Muted = Color.FromArgb("#ADB7C8");
    private readonly RemoteSession session = new();
    private readonly Label connectionTitle = Text("PC koppeln", 15, true);
    private readonly Label connectionDetail = Text("Sicher per QR-Code verbinden", 12);
    private readonly Label visitorDetail = Text("Normaler Hintergrund aktiv", 12);
    private readonly Label playbackTitle = Text("Diashow", 23, true);
    private readonly Label playbackDetail = Text("Keine Verbindung", 13);
    private readonly Label message = Text("", 12);
    private readonly Label countdown = Text("", 12);
    private readonly Switch visitor = new() { OnColor = Accent, ThumbColor = Colors.White };
    private readonly Switch shuffle = new() { OnColor = Accent, ThumbColor = Colors.White };
    private readonly Button play, next, slideshow, video;
    private readonly Picker interval = new() { Title = "Wechselintervall", TextColor = Colors.White, FontSize = 14, MinimumHeightRequest = 48 };
    private readonly Picker layout = new() { Title = "Darstellung", TextColor = Colors.White, FontSize = 14, MinimumHeightRequest = 48 };
    private readonly Border connectionCard;
    private bool rendering, initialized, initializing;
    private bool foreground = true;
    private string intervalSignature = "", layoutSignature = "";
    private CancellationTokenSource? pairing;
    private readonly IDispatcherTimer clock;
    public MainPage()
    {
        NavigationPage.SetHasNavigationBar(this, false); BackgroundColor = PageBackground;
        var content = new VerticalStackLayout { Spacing = 14, Padding = new Thickness(18, 12, 18, 24) };
        Content = new ScrollView { Content = content };
        var title = new VerticalStackLayout { Spacing = 2 }; title.Add(Text("WallpaperControl", 22, true)); title.Add(Text("Remote", 14));
        var settings = Button("⚙", 24); settings.Clicked += async (_, _) => await ShowSettingsAsync();
        var header = new Grid { ColumnDefinitions = [new(GridLength.Star), new(48)], ColumnSpacing = 10 }; header.Add(title); header.Add(settings, 1); content.Add(header);
        var connectionStack = new VerticalStackLayout { Spacing = 3 }; connectionStack.Add(connectionTitle); connectionStack.Add(connectionDetail);
        connectionCard = Panel(connectionStack); content.Add(connectionCard);
        var connectTap = new TapGestureRecognizer(); connectTap.Tapped += async (_, _) => { if (session.Saved == null) await ScanAsync(); else session.Reconnect(); }; connectionCard.GestureRecognizers.Add(connectTap);
        var visitorText = new VerticalStackLayout { Spacing = 5 }; visitorText.Add(Text("🛡  Besuchermodus", 17, true)); visitorText.Add(visitorDetail);
        var visitorRow = new Grid { ColumnDefinitions = [new(GridLength.Star), new(64)], ColumnSpacing = 10 }; visitorRow.Add(visitorText); visitorRow.Add(visitor, 1); content.Add(Panel(visitorRow));
        var playback = new VerticalStackLayout { Spacing = 16 };
        playback.Add(Text("WIEDERGABE", 12));
        slideshow = Button("Diashow", 14); video = Button("Video", 14);
        var modes = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)], ColumnSpacing = 8 }; modes.Add(slideshow); modes.Add(video, 1); playback.Add(modes);
        playbackTitle.HorizontalTextAlignment = TextAlignment.Center; playbackDetail.HorizontalTextAlignment = TextAlignment.Center; countdown.HorizontalTextAlignment = TextAlignment.Center;
        playback.Add(playbackTitle); playback.Add(playbackDetail); playback.Add(countdown);
        play = Button("Ⅱ", 26); next = Button("▷Ⅰ", 24);
        SemanticProperties.SetDescription(next, "Nächstes Hintergrundbild");
        play.BackgroundColor = Accent; play.TextColor = PageBackground; play.CornerRadius = 36; play.WidthRequest = play.HeightRequest = 72;
        var transport = new Grid { ColumnDefinitions = [new(84), new(72)], ColumnSpacing = 24, HorizontalOptions = LayoutOptions.Center };
        transport.Add(play); transport.Add(next, 1); playback.Add(transport); content.Add(Panel(playback));
        var options = new VerticalStackLayout { Spacing = 12 }; options.Add(Text("DIASHOW-EINSTELLUNGEN", 12));
        options.Add(Row("◷  Wechselintervall", interval)); options.Add(Divider()); options.Add(Row("⤨  Zufallswiedergabe", shuffle)); options.Add(Divider()); options.Add(Row("▣  Darstellung", layout)); content.Add(Panel(options));
        message.TextColor = Muted; content.Add(message); content.Add(Text("WallpaperControl Remote · v" + AppInfo.Current.VersionString, 12));
        session.Changed += () => MainThread.BeginInvokeOnMainThread(Render);
        next.Clicked += async (_, _) => await session.SendAsync("next");
        play.Clicked += async (_, _) => await session.SendAsync(session.State?.ManualPaused == true ? "resume" : "pause");
        slideshow.Clicked += async (_, _) => await session.SendAsync("mode", "slideshow"); video.Clicked += async (_, _) => await session.SendAsync("mode", "video");
        visitor.Toggled += async (_, e) =>
        {
            if (rendering) return;
            bool enabled = e.Value; Render();
            if (!enabled && !await DisplayAlertAsync("Besuchermodus deaktivieren?", "Normale und möglicherweise private Hintergründe werden wieder sichtbar.", "Deaktivieren", "Abbrechen")) return;
            await session.SendAsync(enabled ? "visitorOn" : "visitorOff");
        };
        shuffle.Toggled += async (_, e) => { if (rendering) return; bool value = e.Value; Render(); await session.SendAsync("shuffle", value.ToString()); };
        layout.SelectedIndexChanged += async (_, _) => { if (rendering || layout.SelectedItem is not Choice choice) return; Render(); await session.SendAsync("layout", choice.Value); };
        interval.SelectedIndexChanged += async (_, _) =>
        {
            if (rendering || interval.SelectedItem is not Choice choice) return; Render();
            string value = choice.Value;
            if (value == "custom")
            {
                string? input = await DisplayPromptAsync("Wechselintervall", "Dauer in Sekunden (1–86400)", "Übernehmen", "Abbrechen", keyboard: Keyboard.Numeric);
                if (!uint.TryParse(input, out uint seconds) || seconds is < 1 or > 86400) return; value = (seconds * 1000).ToString();
            }
            await session.SendAsync("interval", value);
        };
        clock = Dispatcher.CreateTimer(); clock.Interval = TimeSpan.FromSeconds(1); clock.Tick += (_, _) => UpdateCountdown();
        Render();
    }
    private static Label Text(string value, double size, bool bold = false) => new() { Text = value, FontSize = size, TextColor = Colors.White, FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None, LineBreakMode = LineBreakMode.WordWrap };
    private static Button Button(string text, double size) => new() { Text = text, FontSize = size, BackgroundColor = Colors.Transparent, TextColor = Accent, MinimumHeightRequest = 48, MinimumWidthRequest = 48, CornerRadius = 24 };
    private static Border Panel(View content) => new() { BackgroundColor = Card, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 16 }, Padding = 16, Content = content };
    private static BoxView Divider() => new() { HeightRequest = 1, BackgroundColor = Color.FromArgb("#343D4B"), Margin = new Thickness(0, 2) };
    private static Grid Row(string caption, View control)
    {
        var grid = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 12 };
        grid.Add(new Label { Text = caption, TextColor = Colors.White, FontSize = 14, VerticalTextAlignment = TextAlignment.Center }); grid.Add(control, 1); return grid;
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing(); await InitializeSessionAsync(true);
    }
    private async Task InitializeSessionAsync(bool requestPermission)
    {
        if (initialized || initializing) return; initializing = true;
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(37))
            {
                var permission = await Permissions.CheckStatusAsync<LocalNetworkPermission>();
                if (permission != PermissionStatus.Granted && requestPermission) permission = await Permissions.RequestAsync<LocalNetworkPermission>();
                if (permission != PermissionStatus.Granted)
                { message.Text = "Bitte lokalen Netzwerkzugriff in den Android-App-Einstellungen erlauben."; return; }
            }
            initialized = true;
            await session.InitializeAsync(); if (foreground) clock.Start(); else session.Suspend();
        }
        catch { initialized = false; message.Text = "Die Verbindung konnte nicht geladen werden. Über Einstellungen erneut koppeln."; }
        finally { initializing = false; }
    }
    public async void Resume() { foreground = true; if (initialized) { session.Resume(); clock.Start(); } else await InitializeSessionAsync(false); }
    public void Suspend() { foreground = false; pairing?.Cancel(); session.Suspend(); clock.Stop(); }
    private void Render()
    {
        rendering = true;
        try
        {
            var state = session.State; bool connected = session.Connected, ready = connected && !session.Pending && state?.Busy != true;
            bool Allows(string command) => ready && state?.Capabilities.Allows(command) == true;
            connectionTitle.Text = connected ? "✓  PC verbunden" : session.Status;
            connectionTitle.TextColor = connected ? Color.FromArgb("#65E7B9") : Colors.White;
            connectionCard.BackgroundColor = connected ? Color.FromArgb("#19332D") : Card;
            connectionDetail.Text = state?.ServerName ?? session.Saved?.Server.Name ?? "In WallpaperControl → Einstellungen → Remote einen QR erzeugen";
            visitor.IsToggled = state?.VisitorActive == true;
            visitor.IsEnabled = Allows(state?.VisitorActive == true ? "visitorOff" : "visitorOn");
            visitorDetail.Text = state?.VisitorActive == true ? state.VisitorFolder ? "Besucherordner aktiv" : "Geschützter Hintergrund aktiv" : "Normaler Hintergrund aktiv";
            if (state?.VisitorActive == true && !state.CanDisableVisitor) visitorDetail.Text += " · Ausschalten am PC freigeben";
            playbackTitle.Text = state?.VisitorActive == true ? "Besucherwiedergabe" : state?.MediaKind == "video" ? "Video" : "Diashow";
            playbackDetail.Text = !connected ? "Keine Liveverbindung – Steuerung gesperrt" : state?.Busy == true ? "Änderung wird ausgeführt …" : state?.PauseReasons.Contains("fullscreen") == true ? "Durch Vollbild pausiert"
                : state?.ManualPaused == true ? "Pausiert" : state?.Playback is "playing" ? "Wiedergabe läuft" : state?.Playback is "failed" ? "Wiedergabe am PC prüfen"
                : state?.Playback is "initializing" ? "Video wird vorbereitet …" : state?.Playback is "recovering" ? "Video wird wiederhergestellt …"
                : state?.Playback is "inactive" or "stopped" ? "Wiedergabe am PC starten" : "Bereit";
            slideshow.BackgroundColor = state?.Mode == "slideshow" ? Color.FromArgb("#303A4B") : Colors.Transparent;
            video.BackgroundColor = state?.Mode == "video" ? Color.FromArgb("#303A4B") : Colors.Transparent;
            slideshow.IsEnabled = Allows("mode") && state?.Mode != "slideshow"; video.IsEnabled = Allows("mode") && state?.Mode != "video";
            next.IsEnabled = Allows("next");
            play.Text = state?.ManualPaused == true ? "▶" : "Ⅱ"; play.IsEnabled = Allows(state?.ManualPaused == true ? "resume" : "pause");
            SemanticProperties.SetDescription(play, state?.ManualPaused == true ? "Wiedergabe fortsetzen" : "Wiedergabe pausieren");
            shuffle.IsToggled = state?.Shuffle == true; shuffle.IsEnabled = Allows("shuffle");
            var durations = state?.Capabilities.Intervals.ToList() ?? [];
            if (state != null && !durations.Contains(state.IntervalMilliseconds)) durations.Add(state.IntervalMilliseconds);
            string newIntervals = string.Join(",", durations) + ":" + state?.Capabilities.MinimumInterval;
            if (newIntervals != intervalSignature)
            {
                interval.ItemsSource = durations.Select(d => new Choice(d.ToString(), Duration(d))).Concat(state?.Capabilities.MinimumInterval > 0 ? [new Choice("custom", "Eigene Dauer …")] : []).ToArray(); intervalSignature = newIntervals;
            }
            interval.SelectedItem = (interval.ItemsSource as Choice[])?.FirstOrDefault(c => c.Value == state?.IntervalMilliseconds.ToString()); interval.IsEnabled = Allows("interval");
            string[] layouts = state?.Capabilities.Layouts ?? [];
            string newLayouts = string.Join(",", layouts);
            if (newLayouts != layoutSignature) { layout.ItemsSource = layouts.Select(l => new Choice(l, LayoutName(l))).ToArray(); layoutSignature = newLayouts; }
            layout.SelectedItem = (layout.ItemsSource as Choice[])?.FirstOrDefault(c => c.Value == state?.Layout); layout.IsEnabled = Allows("layout");
            message.Text = session.Pending ? "Wird am PC ausgeführt …" : session.Message;
            UpdateCountdown();
        }
        finally { rendering = false; }
    }
    private void UpdateCountdown()
    {
        if (session.Remaining is not { } remaining) { countdown.Text = ""; return; }
        var span = TimeSpan.FromMilliseconds(remaining);
        countdown.Text = session.State?.Playback == "paused" ? "Restzeit pausiert" : "Nächster Wechsel in " + (span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}" : $"{(int)span.TotalMinutes}:{span.Seconds:00}");
    }
    private static string Duration(uint ms) => ms < 60000 ? $"{ms / 1000} Sekunden" : ms < 3600000 ? $"{ms / 60000} Minuten" : ms < 86400000 ? $"{ms / 3600000} Stunden" : "1 Tag";
    private static string LayoutName(string key) => key switch { "fill" => "Ausfüllen", "fit" => "Einpassen", "stretch" => "Strecken", "tile" => "Kacheln", "center" => "Zentrieren", "span" => "Übergreifend", _ => key };
    private sealed record Choice(string Value, string Label) { public override string ToString() => Label; }
    public sealed class LocalNetworkPermission : Permissions.BasePlatformPermission
    { public override (string androidPermission, bool isRuntime)[] RequiredPermissions => [("android.permission.ACCESS_LOCAL_NETWORK", true)]; }
    private async Task ShowSettingsAsync()
    {
        var body = new VerticalStackLayout { Padding = 20, Spacing = 18 };
        body.Add(Text("Verbindung", 24, true)); body.Add(Text(session.Saved?.Server.Name ?? "Noch kein PC gekoppelt", 16));
        var scan = Button("PC über QR-Code koppeln", 16); scan.Clicked += async (_, _) => { await Navigation.PopAsync(); await ScanAsync(); }; body.Add(scan);
        var address = new Entry { Text = session.Saved?.Address, Placeholder = "Lokale HTTPS-Adresse", TextColor = Colors.White, Keyboard = Keyboard.Url }; body.Add(address);
        body.Add(Text("Manuelle Adresshilfe bei blockierter automatischer Erkennung. Der gespeicherte Zertifikatspin bleibt unverändert.", 13));
        var reconnect = Button("Adresse verwenden / neu verbinden", 15); reconnect.Clicked += async (_, _) =>
        {
            try { if (session.Saved == null) { await DisplayAlertAsync("Zuerst koppeln", "Bitte den QR-Code vom PC scannen.", "OK"); return; } await session.SetAddressAsync(address.Text?.Trim() ?? ""); await Navigation.PopAsync(); }
            catch { await DisplayAlertAsync("Adresse prüfen", "Eine gültige lokale HTTPS-Adresse ist erforderlich.", "OK"); }
        }; body.Add(reconnect);
        var forget = Button("Gespeicherte Verbindung entfernen", 14); forget.Clicked += async (_, _) =>
        {
            if (await DisplayAlertAsync("Verbindung entfernen?", "Zum erneuten Verbinden ist ein neuer QR-Code nötig. Geräteberechtigungen können am PC entfernt werden.", "Entfernen", "Abbrechen")) { await session.ForgetAsync(); await Navigation.PopAsync(); }
        }; body.Add(forget);
        var widgets = Button("Widgetstatus und Fehlerbericht", 14);
        widgets.Clicked += async (_, _) =>
        {
            try { await Navigation.PushAsync(new WidgetDiagnosticsPage()); }
            catch { message.Text = "Die Diagnoseseite konnte nicht geöffnet werden."; }
        }; body.Add(widgets);
        var licenses = Button("Open-Source-Lizenzen", 14); licenses.Clicked += async (_, _) =>
        {
            try
            {
                using var license = await FileSystem.Current.OpenAppPackageFileAsync("license.txt"); using var licenseReader = new StreamReader(license);
                using var notices = await FileSystem.Current.OpenAppPackageFileAsync("third-party-notices.txt"); using var noticesReader = new StreamReader(notices);
                var text = await licenseReader.ReadToEndAsync() + "\n\n" + await noticesReader.ReadToEndAsync();
                await Navigation.PushAsync(new ContentPage { Title = "Lizenzen", BackgroundColor = PageBackground, Content = new ScrollView { Padding = 18, Content = Text(text, 12) } });
            }
            catch { await DisplayAlertAsync("Lizenzen", "Lizenzinformationen konnten nicht geöffnet werden.", "OK"); }
        }; body.Add(licenses);
        var page = new ContentPage { Title = "Einstellungen", BackgroundColor = PageBackground, Content = new ScrollView { Content = body } }; await Navigation.PushAsync(page);
    }
    private async Task ScanAsync()
    {
        if (await Permissions.RequestAsync<Permissions.Camera>() != PermissionStatus.Granted) { await DisplayAlertAsync("Kamerazugriff", "Die Kamera wird nur zum lokalen Scannen des PC-QR-Codes benötigt.", "OK"); return; }
        var camera = new CameraBarcodeReaderView { HeightRequest = 420, IsDetecting = true, Options = new() { Formats = ZXing.Net.Maui.BarcodeFormats.TwoDimensional, Multiple = false, AutoRotate = true } };
        var hint = Text("Am PC: Einstellungen → WallpaperControl Remote → Neues Gerät koppeln", 16);
        var stack = new VerticalStackLayout { Padding = 18, Spacing = 18 }; stack.Add(hint); stack.Add(camera);
        var page = new ContentPage { Title = "PC koppeln", BackgroundColor = PageBackground, Content = new ScrollView { Content = stack } };
        pairing?.Cancel(); pairing = new(); var token = pairing.Token; bool handled = false;
        camera.BarcodesDetected += (_, detected) => MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (handled || token.IsCancellationRequested) return; handled = true; camera.IsDetecting = false;
            try
            {
                string? json = detected.Results.FirstOrDefault()?.Value;
                if (json == null || json.Length > 8192) throw new ArgumentException();
                var qr = JsonSerializer.Deserialize(json, RemoteJson.Type<RemoteQr>()) ?? throw new ArgumentException();
                hint.Text = "Sichere Kopplungsanfrage wird gesendet …";
                await session.PairAsync(qr, code => MainThread.BeginInvokeOnMainThread(() => hint.Text = $"Vergleichscode: {code}\n\nDiesen Code am PC vergleichen und dort bestätigen. Das Recht zum Ausschalten des Besuchermodus wird am PC separat vergeben."), token);
                await Navigation.PopAsync();
            }
            catch (OperationCanceledException) { hint.Text = "Kopplung abgebrochen oder abgelaufen. Am PC einen neuen QR erzeugen."; }
            catch { hint.Text = "Kopplung nicht abgeschlossen. Privaten PC-Adapter, HTTPS-Port und einen frischen WallpaperControl-QR prüfen. Die Zertifikatsprüfung bleibt aktiv."; }
        });
        page.Disappearing += (_, _) => { camera.IsDetecting = false; pairing?.Cancel(); };
        await Navigation.PushAsync(page);
    }
}
