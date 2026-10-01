extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using Microsoft.Win32;
using System.Reflection;
using System.Globalization;
using System.Resources;

internal static class VideoAudioTests
{
    private sealed class Session(Desktop owner) : App.IVideoSession
    {
        internal bool Playing, Muted = true, Published, Disposed, FailAudio, Attached = true;
        internal int Volume, AudioCalls;
        internal Task Preparation = Task.CompletedTask;
        public Task InitializeAsync(CancellationToken token) => Task.CompletedTask;
        public async Task PrepareAsync(CancellationToken token) { Playing = true; await Preparation; Playing = false; }
        public void Present() => Published = true;
        public bool IsAttached(App.VideoShellGeneration shell) => Attached && !Disposed;
        public long Position => 100;
        public void Play() => Playing = true;
        public void Pause() => Playing = false;
        public void Seek(long value) { }
        public event Action<Exception>? Failed { add { } remove { } }
        public void SetAudio(bool enabled, int volume)
        {
            AudioCalls++;
            if (FailAudio && enabled) throw new InvalidOperationException();
            if (enabled && !Published) throw new Exception("Candidate audio before publication");
            Muted = !enabled; Volume = volume;
            if (owner.Sessions.Count(s => s.Playing && !s.Muted && !s.Disposed) > 1) throw new Exception("Overlapping audio");
        }
        public void Dispose() { Disposed = true; Playing = false; Muted = true; }
    }
    private sealed class Desktop : App.IVideoDesktop
    {
        public int MonitorCount => 1;
        internal readonly List<Session> Sessions = new();
        internal Task Preparation = Task.CompletedTask;
        internal Exception? ValidationFailure;
        public bool FileExists(string path) => true;
        public Task ValidateAsync(string path, CancellationToken token) => ValidationFailure == null ? Task.CompletedTask : Task.FromException(ValidationFailure);
        public App.VideoShellGeneration? ResolveShell() => new(1, 2, 3, 4, 5);
        public App.IVideoSession Create(string path, App.VideoShellGeneration shell) { var s = new Session(this) { Preparation = Preparation }; Sessions.Add(s); return s; }
    }
    internal static void Run(Action<bool, string> check)
    {
        Settings(check);
        Task.Run(() => Playback(check)).GetAwaiter().GetResult();
        using Task work = new(() => Ui(check));
        var thread = new Thread(() => work.RunSynchronously(TaskScheduler.Default)); thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); work.GetAwaiter().GetResult();
    }
    private static void Settings(Action<bool, string> check)
    {
        string key = @"Software\WallpaperControl.AudioSettingsTests-" + Guid.NewGuid().ToString("N");
        try
        {
            var store = new App.AppSettingsStore(key);
            check(!store.LoadVideoSound() && store.LoadVideoVolume() == 50, "Audio settings: existing users default off/50");
            store.SaveVideoSound(true); store.SaveVideoVolume(73); var reload = new App.AppSettingsStore(key);
            check(reload.LoadVideoSound() && reload.LoadVideoVolume() == 73, "Audio settings: enabled and volume survive reload");
            store.SaveVideoSound(false); check(!reload.LoadVideoSound() && reload.LoadVideoVolume() == 73, "Audio settings: mute retains volume");
            store.SaveVideoSound(true); store.SaveVideoVolume(0);
            check(reload.LoadVideoSound() && reload.LoadVideoVolume() == 0, "Audio settings: zero volume retains enabled flag");
            store.SaveVideoVolume(1000); check(reload.LoadVideoVolume() == 100, "Audio settings: high input bounded");
            store.SaveVideoVolume(-10); check(reload.LoadVideoVolume() == 0, "Audio settings: low input bounded");
            using var raw = Registry.CurrentUser.CreateSubKey(key); raw.SetValue("VideoVolume", "invalid"); raw.SetValue("VideoSound", "invalid");
            check(!reload.LoadVideoSound() && reload.LoadVideoVolume() == 50, "Audio settings: invalid types use safe defaults");
            raw.SetValue("VideoVolume", 101); check(reload.LoadVideoVolume() == 50, "Audio settings: invalid stored range uses default");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(key, false); }
    }
    private static async Task Playback(Action<bool, string> check)
    {
        var desktop = new Desktop(); var logs = new List<string>(); var now = DateTime.UtcNow;
        using var controller = new App.VideoWallpaperController(desktop, logs.Add, () => now);
        await controller.StartAsync(@"C:\A.mp4"); var first = desktop.Sessions.Last();
        check(first.Playing && first.Muted && first.Volume == 50, "Audio: default video starts silent at configured 50");
        controller.SetAudio(true, 73); check(!first.Muted && first.Volume == 73, "Audio: enable active video immediately");
        controller.SetAudio(true, 12); check(first.Volume == 12 && desktop.Sessions.Count == 1, "Audio: live volume creates no player");
        controller.SetAudio(false, 12); check(first.Muted && controller.Volume == 12, "Audio: mute preserves configured level");
        controller.SetAudio(true, 0); check(!first.Muted && controller.SoundEnabled && first.Volume == 0, "Audio: zero volume distinct from disabled");
        controller.SetAudio(true, 73);
        controller.SetPause(App.VideoPauseReason.Manual, true); controller.SetPause(App.VideoPauseReason.Fullscreen, true);
        check(!first.Playing && controller.SoundEnabled, "Audio: manual/fullscreen pauses shared clock");
        controller.SetPause(App.VideoPauseReason.Manual, false); check(!first.Playing, "Audio: clearing one pause reason cannot resume");
        controller.SetPause(App.VideoPauseReason.Fullscreen, false); check(first.Playing && !first.Muted, "Audio: final reason resumes enabled audio/video");
        controller.SetAudio(false, 73); controller.SetPause(App.VideoPauseReason.Manual, true); controller.SetPause(App.VideoPauseReason.Manual, false);
        check(first.Playing && first.Muted, "Audio: resume stays silent when disabled"); controller.SetAudio(true, 73);
        var held = new TaskCompletionSource(); desktop.Preparation = held.Task;
        var start = controller.StartAsync(@"C:\B.mp4"); var pending = desktop.Sessions.Last();
        check(pending.Playing && pending.Muted && pending.AudioCalls == 0 && !pending.Published && first.Playing && !first.Muted, "Audio: hidden B silent while A active");
        controller.SetAudio(true, 35); check(first.Volume == 35 && pending.Muted, "Audio: changes during preparation affect only active A");
        held.SetResult(); await start;
        check(pending.Published && pending.Playing && !pending.Muted && pending.Volume == 35 && first.Disposed, "Audio: B enabled only after publication and old shutdown");
        check(desktop.Sessions.Count(s => !s.Disposed) == 1, "Audio: switch owns one published player");
        desktop.ValidationFailure = new InvalidDataException();
        check(!await controller.StartAsync(@"C:\bad.mp4") && !pending.Muted && !pending.Disposed, "Audio: failed validation retains active audio"); desktop.ValidationFailure = null;
        foreach (string scenario in new[] { "cancelled", "stale", "superseded", "failed" })
        {
            held = new(); desktop.Preparation = held.Task; start = controller.StartAsync(@"C:\candidate.mp4"); var obsolete = desktop.Sessions.Last();
            check(obsolete.Muted && obsolete.AudioCalls == 0, "Audio: " + scenario + " candidate silent during preparation");
            if (scenario == "failed") held.SetException(new InvalidDataException());
            else
            {
                controller.CancelPendingStart();
                if (scenario is "stale" or "superseded") { desktop.Preparation = Task.CompletedTask; await controller.StartAsync(@"C:\winner.mp4"); }
                held.SetResult();
            }
            check(!await start && obsolete.Disposed && obsolete.Muted && obsolete.AudioCalls == 0, "Audio: " + scenario + " continuation never unmutes");
        }
        desktop.Preparation = Task.CompletedTask;
        controller.SetPause(App.VideoPauseReason.Manual, true); await controller.StartAsync(@"C:\paused.mp4");
        var active = desktop.Sessions.Last(); check(!active.Playing && !active.Muted && active.Volume == 35, "Audio: switching while paused retains policy and volume");
        controller.SetPause(App.VideoPauseReason.Manual, false);
        active.FailAudio = true; controller.SetAudio(true, 44); controller.SetAudio(true, 45);
        check(active.Muted && active.Playing && controller.HasSession && logs.Count(s => s.StartsWith("audio control failed")) == 1, "Audio: control failure stays silent/video alive and logs once");
        controller.Stop(); check(desktop.Sessions.All(s => s.Disposed && s.Muted && !s.Playing), "Audio: stop releases all playback/audio");
        check(controller.SoundEnabled && controller.Volume == 45, "Audio: stop preserves audio preferences");
        await controller.StartAsync(@"C:\restore.mp4"); active = desktop.Sessions.Last();
        check(!active.Muted && active.Volume == 45, "Audio: restart reapplies latest preferences");
        active.Attached = false; await controller.TickAsync();
        check(active.Disposed && active.Muted, "Audio: recovery releases old audio immediately");
        now = now.AddSeconds(2); await controller.TickAsync(); active = desktop.Sessions.Last();
        check(active.Playing && !active.Muted && active.Volume == 45, "Audio: recovery restores sound and volume");
        controller.Dispose(); check(active.Disposed && !active.Playing, "Audio: shutdown stops existing player");
    }
    private static void Ui(Action<bool, string> check)
    {
        var resources = new ResourceManager("WallpaperControl.Strings", typeof(App.MainForm).Assembly);
        foreach (string language in new[] { "de", "en", "fr", "es", "ja" })
        foreach (string key in new[] { "VideoSound", "VideoVolume", "VideoVolumeFormat" })
        { var set = resources.GetResourceSet(language == "de" ? CultureInfo.InvariantCulture : new(language), true, false); check(!string.IsNullOrWhiteSpace(set?.GetString(key)), "Audio localization " + language + ": " + key); }
        string registry = @"Software\WallpaperControl.AudioUiTests-" + Guid.NewGuid().ToString("N");
        try
        {
            using var widgets = new App.WidgetManager(() => { }, registryPath: registry, notesStore: new App.NotesStore(Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"), "notes.json")));
            using var main = new App.MainForm(widgets, () => App.DesktopSlideshowState.Enabled | App.DesktopSlideshowState.Slideshow, () => null);
            var checkBox = Field<CheckBox>(main, "videoSoundCheck"); var slider = Field<TrackBar>(main, "videoVolumeSlider");
            check(!checkBox.Checked && slider.Value == 50 && !slider.Enabled, "Audio UI: default silent/50 and disabled slider");
            checkBox.Checked = true; slider.Value = 73; checkBox.Checked = false;
            check(slider.Value == 73 && !slider.Enabled, "Audio UI: toggling off retains slider");
            checkBox.Checked = true; slider.Value = 0;
            check(checkBox.Checked && Field<Label>(main, "videoVolumeLabel").Text.Contains("0"), "Audio UI: zero percentage independent of sound flag");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(registry, false); resources.ReleaseAllResources(); }
    }
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
}
