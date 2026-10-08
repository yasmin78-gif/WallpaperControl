extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using System.Drawing;
using System.Reflection;
using Microsoft.Win32;
internal static class NowPlayingTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static async Task Idle(App.NowPlayingService service)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while ((bool)typeof(App.NowPlayingService).GetField("refreshing",Members)!.GetValue(service)!)
        { if (watch.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Media refresh did not complete"); await Task.Delay(10); }
    }
    private static void Sta(Func<Task> action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            try { var task = action(); var watch = System.Diagnostics.Stopwatch.StartNew(); while (!task.IsCompleted) { if (watch.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException(); Application.DoEvents(); Thread.Sleep(5); } task.GetAwaiter().GetResult(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if (error != null) throw error;
    }
    internal static void Run(Action<bool,string> check) => Sta(() =>
    {
        string path = @"Software\WallpaperControl.NowPlayingTests\" + Guid.NewGuid().ToString("N");
        try
        {
            var settings = new App.WidgetSettings();
            check(!settings.NowPlaying.Enabled && !settings.NowPlaying.WheelVolume && !settings.NowPlaying.HideEmpty,"New widget and system-volume control default off");
            settings.NowPlaying = new() { Enabled = true, Locked = true, HideEmpty = true, WheelVolume = true, Style = App.SystemWidgetStyle.Glow, Location = new(123,456) }; settings.Save(path);
            var loaded = App.WidgetSettings.Load(path).NowPlaying;
            check(loaded.Enabled && loaded.Locked && loaded.HideEmpty && loaded.WheelVolume && loaded.Style == App.SystemWidgetStyle.Glow && loaded.Location == new Point(123,456),"All media settings roundtrip through isolated registry");
            var clone = settings.Clone(); clone.NowPlaying.Location = Point.Empty;
            check(settings.NowPlaying.Location == new Point(123,456),"Cloning does not alias media preferences");
            check(!App.NowPlayingSettings.Parse("{broken").Enabled,"Corrupt settings recover safely");
            check(App.NowPlayingSettings.Parse("{\"Style\":99}").Style == App.SystemWidgetStyle.Minimal,"Unknown style normalizes");
            var instant = DateTimeOffset.UtcNow;
            var state = new App.NowPlayingState { SessionId = "a", Source = "test.exe",Title = "My Immortal",Artist = "Evanescence",Album = "Fallen", Start = TimeSpan.FromSeconds(10),End = TimeSpan.FromSeconds(273),Position = TimeSpan.FromSeconds(171), MinimumSeek = TimeSpan.FromSeconds(20),MaximumSeek = TimeSpan.FromSeconds(260),Updated = instant, Playing = true, Rate = 1, Previous = true,Toggle = true,Next = true,Seek = true };
            foreach (bool hide in new[] { false,true })
            {
                using var startup = new Fake(); long startupClock = 0;
                using var startupWidget = new App.NowPlayingWidgetForm(startup,new() { HideEmpty = hide },"de",_=>{},() => startupClock);
                _ = startupWidget.Handle;
                check(!startupWidget.ShouldShow && !startupWidget.Visible,$"Startup with unknown/empty session remains hidden; hide={hide}");
                startup.Raise(); startupClock = 60_000; startupWidget.Tick();
                check(!startupWidget.ShouldShow && !startupWidget.Visible,$"Empty startup does not reveal widget after grace period; hide={hide}");
                startupWidget.SetActivitySuspended(true); startupWidget.SetActivitySuspended(false);
                startupWidget.Apply(new() { HideEmpty = !hide },"de");
                check(!startupWidget.ShouldShow,$"Resume/settings changes cannot reveal widget before first session; hide={hide}");
                startup.State = state; startup.Raise();
                check(startupWidget.ShouldShow && startupWidget.DisplayState.Title == state.Title,$"First detected session reveals display immediately; hide={hide}");
                startupWidget.Apply(new() { HideEmpty = hide },"de"); startup.State = new(); startup.Raise();
                check(startupWidget.ShouldShow && startupWidget.DisplayState.Title == state.Title,$"Grace period only applies after recognized session; hide={hide}");
                startupClock += 15_000; startupWidget.Tick();
                check(startupWidget.ShouldShow == !hide,$"Subsequent genuine absence follows configured empty behavior; hide={hide}");
            }
            check(state.PositionAt(instant.AddSeconds(2)) == TimeSpan.FromSeconds(173),"Playing timeline interpolates from Windows update timestamp");
            check(state.PositionAt(instant.AddHours(1)) == state.End,"Interpolation clamps at media end");
            check(state.PositionAt(instant.AddSeconds(-10)) == state.Position,"Backward clock movement does not rewind");
            check((state with { Playing = false }).PositionAt(instant.AddSeconds(10)) == state.Position,"Paused timeline stays still");
            check((state with { Rate = 2 }).PositionAt(instant.AddSeconds(2)) == TimeSpan.FromSeconds(175),"Nonstandard playback rates are respected");
            check(state.SeekAt(-1) == state.MinimumSeek && state.SeekAt(2) == state.MaximumSeek,"Seeking clamps to session seek limits including DVR ranges");
            check(state.SeekAt(.5) == TimeSpan.FromSeconds(141.5),"Seeking accounts for nonzero timeline start");
            check(new App.NowPlayingState().PositionAt(instant) == TimeSpan.Zero,"Missing duration is safe");
            check(App.NowPlayingService.SourceName("Vivaldi.7I3S7YDOZJL2HU5ELWYXIZUPXQ") == "Vivaldi" && App.NowPlayingService.SourceName("Spotify.exe") == "Spotify","Readable source names cover executable and Chromium profile IDs");
            check(App.NowPlayingService.SourceName("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic","de") == "Medienwiedergabe","Windows media player displays localized friendly name");
            check(App.NowPlayingService.SourceName("AmazonMobileLLC.AmazonMusic_kc6t79cpj4tp0!AmazonMobileLLC.AmazonMusic") == "Amazon Music","Amazon packaged app displays friendly name");
            foreach (var example in new[]
            {
                (Source: "OpenAI.ChatGPT-Desktop_2p2nqsd0c76g0!ChatGPT", Name: "ChatGPT"),
                (Source: "Vendor.MusicPlayer_abcdefghijklm!Vendor.MusicPlayer", Name: "MusicPlayer"),
                (Source: "Vendor.MusicPlayer_abcdefghijklm!App", Name: "MusicPlayer"),
                (Source: "Vendor.MusicPlayer_abcdefghijklm!Main", Name: "MusicPlayer"),
                (Source: "Vendor.Player_abcdefghijklm!12345", Name: "Player"),
                (Source: "Vendor.App_abcdefghijklm!App", Name: "Vendor.App_abcdefghijklm!App"),
                (Source: "Unknown source", Name: "Unknown source"),
                (Source: "Vendor.Player_invalid!Player", Name: "Vendor.Player_invalid!Player"),
                (Source: "Vendor.Player_abcdefghijklm!", Name: "Vendor.Player_abcdefghijklm!"),
                (Source: "Vendor.Player_abcdefghijklm!App!Player", Name: "Vendor.Player_abcdefghijklm!App!Player"),
                (Source: "Vendor.Player_abcdefghijklm!Unclear name", Name: "Vendor.Player_abcdefghijklm!Unclear name")
            }) check(App.NowPlayingService.SourceName(example.Source) == example.Name,$"Packaged source normalization: {example.Source}");
            check(App.NowPlayingWidgetForm.FormatTime(TimeSpan.FromSeconds(161)) == "02:41" && App.NowPlayingWidgetForm.FormatTime(TimeSpan.FromSeconds(3661)) == "1:01:01","Minute and hour display formats");
            using var fake = new Fake { State = state };
            long clock = 0;
            using var widget = new App.NowPlayingWidgetForm(fake,new(),"de",_=>{},() => clock);
            void Mouse(string method,int x,int y) => typeof(App.NowPlayingWidgetForm).GetMethod(method,Members)!.Invoke(widget,[new MouseEventArgs(MouseButtons.Left,1,x,y,0)]);
            Mouse("OnMouseDown",120,100); Mouse("OnMouseDown",190,100); Mouse("OnMouseDown",250,100);
            check(fake.Commands.Select(c=>c.Command).SequenceEqual([App.MediaCommand.Previous,App.MediaCommand.Toggle,App.MediaCommand.Next]),"Three transport buttons dispatch to selected service");
            widget.SetActivitySuspended(true); Mouse("OnMouseDown",190,100);
            check(fake.Commands.Count == 3 && fake.Suspended,"Fullscreen suspension never sends a pause command");
            widget.SetPowerSuspended(true); widget.SetActivitySuspended(false);
            check(fake.Suspended && fake.Commands.Count == 3,"Removing fullscreen leaves independent power suspension active");
            widget.SetPowerSuspended(false); check(!fake.Suspended && fake.Commands.Count == 3,"Resume never sends a play command");
            Mouse("OnMouseDown",190,119); Mouse("OnMouseUp",190,119);
            check(fake.Commands.Last().Command == App.MediaCommand.Seek && fake.Commands.Last().Position == state.SeekAt(.5),"Progress interaction seeks without dragging widget");
            fake.State = state with { Previous = false, Toggle = false, Next = false, Seek = false }; fake.Raise();
            int commandCount = fake.Commands.Count; Mouse("OnMouseDown",120,100); Mouse("OnMouseDown",190,100); Mouse("OnMouseDown",250,100);
            check(fake.Commands.Count == commandCount,"Unsupported transport buttons issue no command");
            fake.State = state; fake.Raise(); Mouse("OnMouseDown",190,119);
            fake.State = state with { SessionId = "b" }; fake.Raise(); Mouse("OnMouseUp",190,119);
            check(fake.Commands.Count == commandCount,"Session change cancels outstanding seek gesture");
            fake.State = state; fake.Raise(); Mouse("OnMouseDown",190,119); fake.Raise(); Mouse("OnMouseUp",190,119);
            check(fake.Commands.Count == commandCount+1,"Same-session timeline refresh preserves active seek gesture");
            fake.State = new(); fake.Raise(); widget.Apply(new() { HideEmpty = true },"de");
            check(widget.ShouldShow,"Missing session keeps widget visible during grace period");
            using (var transition = widget.RenderBitmap()) check(transition.Height == 140,"Transient missing session retains widget height");
            clock = 14_999; widget.Tick(); check(widget.ShouldShow,"Widget stays visible just before fifteen-second deadline");
            fake.Raise(); clock = 15_000; widget.Tick(); check(!widget.ShouldShow,"Repeated empty events do not extend fifteen-second deadline");
            fake.State = state with { Playing = false }; fake.Raise(); check(widget.ShouldShow,"Paused source remains visible");
            clock += 60_000; widget.Tick(); check(widget.ShouldShow,"Paused session never expires");
            fake.State = new(); fake.Raise(); clock += 10_000;
            fake.State = state; fake.Raise(); fake.State = new(); fake.Raise();
            clock += 10_000; widget.Tick(); check(widget.ShouldShow,"Returning session cancels old deadline and starts a fresh interval");
            widget.SetActivitySuspended(true); clock += 60_000; widget.Tick(); widget.SetActivitySuspended(false);
            check(widget.ShouldShow,"Suspension does not consume unobserved empty-session grace period");
            foreach (bool hide in new[] { false,true })
            {
                fake.State = state with { Cover = [1,2,3] }; fake.Raise(); widget.Apply(new() { HideEmpty = hide },"de");
                fake.State = new(); fake.Raise(); var retained = widget.DisplayState;
                check(retained.Title == state.Title && retained.Artist == state.Artist && retained.Cover?.Length == 3,$"Transient missing session retains full metadata and cover; hide={hide}");
                using(var transition = widget.RenderBitmap()) check(transition.Height == 140,$"Transient missing session retains full size in both modes; hide={hide}");
                check(!retained.Previous && !retained.Next && !retained.Toggle && !retained.Seek && retained.PositionAt(instant.AddHours(1)) == retained.Position,$"Retained state freezes progress and disables stale controls; hide={hide}");
                int before = fake.Commands.Count; Mouse("OnMouseDown",190,100); Mouse("OnMouseDown",190,119); Mouse("OnMouseUp",190,119);
                check(fake.Commands.Count == before,$"Retained display cannot command a vanished source; hide={hide}");
                clock += 14_999; widget.Tick(); check(widget.DisplayState.Title == state.Title,$"Full display remains until grace deadline; hide={hide}");
                clock++; widget.Tick(); check(widget.DisplayState.SessionId == null && widget.DisplayState.Cover == null && widget.ShouldShow == !hide,$"Deadline clears retained content and applies visibility option; hide={hide}");
                fake.State = state with { Title = "Next track" }; fake.Raise();
                check(widget.DisplayState.Title == "Next track" && widget.DisplayState.Rate == 1 && widget.ShouldShow,$"Returning track replaces retained content immediately; hide={hide}");
            }
            fake.State = new() { Unavailable = true }; fake.Raise(); check(widget.ShouldShow,"API failure is reported even with hide-empty enabled");
            fake.State = new(); widget.Apply(new(),"de"); using(var empty = widget.RenderBitmap()) check(empty.Height == 62 && empty.Width == 380,"Empty widget uses compact layout");
            fake.State = state; fake.Raise();
            var preview = Environment.GetEnvironmentVariable("WALLPAPER_NOWPLAYING_PREVIEW");
            if (!string.IsNullOrEmpty(preview)) { widget.Apply(new() { Style = App.SystemWidgetStyle.Glow },"de"); using var image = widget.RenderBitmap(192); image.Save(preview); }
            foreach (var style in Enum.GetValues<App.SystemWidgetStyle>()) foreach (int dpi in new[] {96,144,192}) foreach (bool dark in new[] {true,false})
            {
                widget.Apply(new() { Style = style },"de"); widget.ApplyTheme(dark); using var image = widget.RenderBitmap(dpi);
                check(image.Width == 380*dpi/96 && image.Height == 140*dpi/96,$"Now Playing renders {style}, {dpi} DPI, dark={dark}");
            }
            fake.State = state with { Cover = [1,2,3] }; fake.Raise(); using(var corruptCover = widget.RenderBitmap()) check(corruptCover.Height == 140,"Invalid cover keeps metadata usable");
            foreach (string language in new[] {"de","en","fr","es","ja"})
            {
                using var editor = new App.WidgetSettingsEditor(settings); editor.ApplyPresentation(true,language);
                check(editor.WidgetKeys.Contains("NowPlayingTitle"),$"Media settings are discoverable in {language}");
                editor.SelectWidget("NowPlayingTitle"); var result = editor.ReadWidgetSettings(false).NowPlaying;
                check(result.Enabled && result.Locked && result.HideEmpty && result.WheelVolume && result.Style == App.SystemWidgetStyle.Glow,$"Media preferences survive presentation changes in {language}");
            }
            widget.Dispose(); check(fake.SubscriberCount == 0,"Closing widget unregisters service subscription");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path,false); }
        return Task.CompletedTask;
    });
    internal static void Native(Action<bool,string> check) => Sta(async () =>
    {
        using var service = new App.NowPlayingService(); await service.Initialization;
        // Read-only: the test intentionally issues no playback or volume commands.
        await Idle(service);
        check(!service.State.Unavailable,"Native Windows media-session manager opens in unpackaged desktop process");
        check(service.Sessions.Select(s=>s.Id).Distinct().Count() == service.Sessions.Count,"Native session IDs are unique");
        check(App.NowPlayingVolume.Read(out float volume) && volume is >= 0 and <= 1,"Core Audio endpoint volume is readable without changing it");
        using (var hidden = new Form { ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None })
        {
            _ = hidden.Handle;
            check(App.DesktopWidgetNative.AttachToDesktop(hidden,new(40,40),showWindow:false),"Hidden startup window attaches to desktop band successfully");
            check(!IsWindowVisible(hidden.Handle),"Native desktop attachment does not briefly reveal startup window");
        }
        var manager = (Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager)typeof(App.NowPlayingService).GetField("manager",Members)!.GetValue(service)!;
        var rawCurrent = manager.GetCurrentSession();
        if (rawCurrent != null)
        {
            var rawMedia = await rawCurrent.TryGetMediaPropertiesAsync();
            Console.WriteLine($"Native thumbnail supplied: {rawMedia.Thumbnail != null}");
            if (rawMedia.Thumbnail != null)
            {
                try { using var stream = await rawMedia.Thumbnail.OpenReadAsync(); Console.WriteLine($"Native thumbnail size: {stream.Size}; content type: {stream.ContentType}"); }
                catch (Exception ex) { Console.WriteLine($"Native thumbnail open failed: {ex.GetType().Name}: {ex.Message}"); }
            }
        }
        check(rawCurrent == null || service.State.SessionId != null,"Windows current session resolves despite different native wrappers");
        Console.WriteLine($"Native sessions: {service.Sessions.Count}; selected source: {service.State.Source}; playing: {service.State.Playing}; cover bytes: {service.State.Cover?.Length ?? 0}");
        if (service.State.SessionId != null)
        {
            check(!string.IsNullOrEmpty(service.State.Title),"Live session supplies a media title");
            Console.WriteLine($"Title: {service.State.Title}; artist: {service.State.Artist}; album: {service.State.Album}; position: {service.State.Position}; end: {service.State.End}; seek: {service.State.Seek}");
            var observed = service.State; var before = observed.PositionAt(DateTimeOffset.UtcNow); await Task.Delay(1100);
            check(!observed.Playing || observed.End <= observed.Start || observed.PositionAt(DateTimeOffset.UtcNow) >= before,"Live playing timeline progresses without commanding source");
            using var widget = new App.NowPlayingWidgetForm(service,new() { Style = App.SystemWidgetStyle.Glow },"de",_=>{});
            widget.Apply(new() { Style = App.SystemWidgetStyle.Glow },"de"); using var bitmap = widget.RenderBitmap(192);
            check(bitmap.Width == 760 && bitmap.Height == 280,"Live Windows metadata renders with supplied cover or placeholder");
            if (service.State.Cover != null) check(typeof(App.NowPlayingWidgetForm).GetField("cover",Members)!.GetValue(widget) != null,"Supplied native artwork decodes into widget cover");
            string? preview = Environment.GetEnvironmentVariable("WALLPAPER_NOWPLAYING_PREVIEW"); if (!string.IsNullOrEmpty(preview)) bitmap.Save(preview);
            string id = service.Sessions.First().Id; service.Select(id); await Idle(service);
            check(service.State.SessionId == id && service.Selected == id,"Explicit session selection uses stable application session ID");
            var cachedCover = service.State.Cover; service.RequestRefresh(); await Idle(service);
            check(service.State.SessionId == id && ReferenceEquals(cachedCover,service.State.Cover),"Timeline refresh keeps session identity and reuses cover bytes");
            service.Select("missing-session"); await Idle(service); check(service.State.SessionId == null,"Disappeared selected session cannot keep stale media active");
            service.Select(null); await Idle(service); check(service.State.SessionId != null,"Returning to automatic selection restores Windows current session");
            for (int i=0;i<25;i++) { service.Select(id); service.Select("missing-session"); }
            service.Select(null); await Idle(service);
            check(service.Selected == null && service.State.SessionId != null,"Fifty rapid selection changes cannot publish stale preparation");
            check(service.Sessions.Count == manager.GetSessions().Count,"Rapid selection retains only current session inventory");
        }
        var state = service.State; service.SetSuspended(true); await Task.Delay(100); check(service.State == state,"Native fullscreen suspension leaves playback state untouched");
        service.SetSuspended(false); await Task.Delay(100); service.Dispose();
        check(typeof(App.NowPlayingService).GetField("manager",Members)!.GetValue(service) == null && typeof(App.NowPlayingService).GetField("current",Members)!.GetValue(service) == null,"Native disposal releases manager/current-session references and unsubscribes");
        check(service.Sessions.Count == 0 && service.State.SessionId == null,"Native disposal clears session/cover ownership");
        check(!await service.CommandAsync(App.MediaCommand.Toggle),"Disposed service cannot command real playback");
        using var early = new App.NowPlayingService(); early.Dispose(); await early.Initialization; check(early.Sessions.Count == 0,"Disposal during native initialization cannot revive service");
        using var pending = new App.NowPlayingService(); await pending.Initialization; pending.Dispose(); await Idle(pending);
        check(pending.State.SessionId == null && pending.State.Cover == null && pending.Sessions.Count == 0,"Disposal during pending media/cover read cannot revive metadata or resources");
    });
    private sealed class Fake : App.INowPlayingService
    {
        public event Action? Changed;
        internal int SubscriberCount => Changed?.GetInvocationList().Length ?? 0;
        public App.NowPlayingState State { get; set; } = new();
        public IReadOnlyList<App.MediaSessionChoice> Sessions { get; } = [new("a","Test")];
        public string? Selected { get; private set; }
        internal bool Suspended;
        internal List<(App.MediaCommand Command,TimeSpan Position)> Commands = [];
        public void Select(string? id) { Selected = id; Raise(); }
        public void SetSuspended(bool value) => Suspended = value;
        public Task<bool> CommandAsync(App.MediaCommand command,TimeSpan position = default) { Commands.Add((command,position)); return Task.FromResult(true); }
        internal void Raise() => Changed?.Invoke();
        public void Dispose() => Changed = null;
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);
}
