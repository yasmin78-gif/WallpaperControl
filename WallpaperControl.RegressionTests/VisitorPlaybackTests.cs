extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using System.Reflection;
using Microsoft.Win32;

internal static class VisitorPlaybackTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Members)!.GetValue(target)!;
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Members)!.SetValue(target, value);
    private static Task Invoke(object target, string method, params object[] args) => (Task)target.GetType().GetMethod(method, Members)!.Invoke(target, args)!;
    internal static void Run(Action<bool, string> check)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            using Form loop = new() { ShowInTaskbar = false, Location = new(-20000, -20000) };
            loop.Shown += async (_, _) => { try { await Checks(check); } catch (Exception ex) { failure = ex; } finally { loop.Close(); } };
            Application.Run(loop);
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static async Task Checks(Action<bool, string> check)
    {
        long now = 0;
        var clock = new App.VisitorSlideshowClock(() => now);
        clock.Restart(10000); now = 2500; clock.SetPaused(true); now = 50000;
        check(clock.Remaining == 7500 && !clock.Due, "Visitor playback: pausing freezes the remaining interval");
        clock.SetPaused(false); now += 7499;
        check(!clock.Due, "Visitor playback: resuming retains the exact remaining deadline");
        now++; check(clock.Due, "Visitor playback: automatic advance becomes due after the remaining time");

        string root = Path.Combine(AppContext.BaseDirectory, "test-data", "visitor-playback-" + Guid.NewGuid().ToString("N"));
        string folder = Path.Combine(root, "visitors"); Directory.CreateDirectory(folder);
        string normal = Path.Combine(root, "normal.png");
        void Picture(string path, Color color) { using Bitmap bitmap = new(16, 12); using (Graphics g = Graphics.FromImage(bitmap)) g.Clear(color); bitmap.Save(path); }
        Picture(normal, Color.Red);
        string first = Path.Combine(folder, "a.png"), second = Path.Combine(folder, "b.png");
        Picture(first, Color.Blue); Picture(second, Color.Green);
        var settings = new App.VisitorModeSettings { Source = App.VisitorSourceKind.Folder, Path = folder };
        byte[] original = File.ReadAllBytes(normal);
        string copied = await App.VisitorFolderCopy.CopyAsync(normal, settings);
        check(File.ReadAllBytes(copied).SequenceEqual(original) && File.ReadAllBytes(normal).SequenceEqual(original),
            "Visitor copy: original pixels are copied without modifying the source");
        string collision = await App.VisitorFolderCopy.CopyAsync(normal, settings);
        check(copied != collision && File.ReadAllBytes(copied).SequenceEqual(original) && File.ReadAllBytes(collision).SequenceEqual(original),
            "Visitor copy: colliding filenames are kept separately without overwriting");
        File.Delete(copied); File.Delete(collision);
        check(await App.VisitorFolderCopy.CopyAsync(first, settings) == first && Directory.GetFiles(folder).Length == 2,
            "Visitor copy: image already in the visitor folder is not duplicated");
        foreach (var unavailable in new[] { settings with { Source = App.VisitorSourceKind.Image }, settings with { Source = App.VisitorSourceKind.Video },
            settings with { Active = true }, settings with { Path = Path.Combine(root, "missing") } })
        {
            bool rejected = false;
            try { await App.VisitorFolderCopy.CopyAsync(normal, unavailable); } catch (InvalidOperationException) { rejected = true; }
            check(rejected, "Visitor copy: rejects non-folder, active-mode or missing-folder destination " + unavailable.Source + "/" + unavailable.Active);
        }
        string corrupt = Path.Combine(root, "bad.png"); File.WriteAllText(corrupt, "Not an image");
        bool corruptRejected = false;
        try { await App.VisitorFolderCopy.CopyAsync(corrupt, settings); } catch { corruptRejected = true; }
        check(corruptRejected && Directory.GetFiles(folder).Length == 2, "Visitor copy: damaged image never enters the visitor folder");

        string registry = @"Software\WallpaperControl.VisitorPlayback-" + Guid.NewGuid().ToString("N");
        try
        {
            new App.WidgetSettings { NextEnabled = true }.Save(registry);
            using App.WidgetManager widgets = new(() => { }, registryPath: registry);
            using App.MainForm form = new(widgets, () => 0, () => normal);
            form.Opacity = 0; form.ShowInTaskbar = false; form.Show();
            var service = Field<App.VisitorModeService>(form, "visitorMode");
            Set(form, "loading", true);
            var interval = Field<ComboBox>(form, "intervalComboBox"); interval.SelectedIndex = 0;
            Field<CheckBox>(form, "shuffleCheckBox").Checked = false;
            object normalInterval = interval.SelectedItem!;
            Set(form, "loading", false); Set(form, "pauseOnFullscreen", false);
            await service.ConfigureAsync(s => s with { Source = App.VisitorSourceKind.Folder, Path = folder, IntervalMilliseconds = 15000 });
            check(Field<Button>(form, "visitorCopyButton").Visible && Field<Button>(form, "visitorCopyButton").Enabled,
                "Visitor copy UI: configured visitor folder enables the normal-mode copy action");
            widgets.Start();
            await service.SetActiveAsync(true);
            var resume = service.Settings.Resume;
            string displayed = Field<string>(form, "visitorCurrentPath");
            check(displayed == first && Field<Button>(form, "pauseButton").Enabled && interval.Enabled
                && !Field<Button>(form, "rejectButton").Enabled && !Field<Button>(form, "undoRejectButton").Enabled
                && !Field<Button>(form, "visitorCopyButton").Visible,
                "Visitor folder UI: playback controls are available, rejection and copying remain blocked");
            check(Field<Form>(widgets, "nextWidget").Visible,
                "Visitor folder widget: configured Next widget remains available");
            await Invoke(form, "AdvanceWallpaperAsync", App.DesktopSlideshowDirection.Forward);
            check(Field<string>(form, "visitorCurrentPath") == second,
                "Visitor playback: shared Next action advances only through visitor files");
            await Invoke(form, "AdvanceWallpaperAsync", App.DesktopSlideshowDirection.Backward);
            check(Field<string>(form, "visitorCurrentPath") == first,
                "Visitor playback: shared Previous action selects the previous visitor image");
            await Invoke(form, "ToggleSlideshowPauseAsync", false);
            await Invoke(form, "AdvanceWallpaperAsync", App.DesktopSlideshowDirection.Forward);
            check(service.Settings.SlideshowPaused && !Field<Button>(form, "nextWallpaperButton").Enabled
                && Field<string>(form, "visitorCurrentPath") == first && !Field<bool>(form, "slideshowPaused"),
                "Visitor playback: pause freezes visitor advance without pausing normal playback");
            check(Field<Label>(form, "statusLabel").Visible && Field<Label>(form, "statusLabel").Bottom < Field<Panel>(form, "slideshowCard").Top,
                "Visitor pause UI: paused status has reserved space above slideshow controls");
            var visitorClock = Field<App.VisitorSlideshowClock>(form, "visitorSlideshowClock");
            long remaining = visitorClock.Remaining;
            await Invoke(form, "ToggleSlideshowPauseAsync", false);
            check(!service.Settings.SlideshowPaused && visitorClock.Remaining <= remaining && visitorClock.Remaining > remaining - 1000,
                "Visitor playback: main pause action resumes the retained interval");
            interval.SelectedIndex = 1;
            Field<CheckBox>(form, "shuffleCheckBox").Checked = true;
            await Invoke(form, "SaveVisitorPlaybackOptionsAsync");
            check(service.Settings.IntervalMilliseconds != 15000 && service.Settings.Shuffle && ReferenceEquals(resume, service.Settings.Resume)
                && Field<string>(form, "visitorCurrentPath") == first,
                "Visitor playback: interval and shuffle persist without media reload or changing the normal snapshot");
            long fakeTime = 0;
            var timedClock = new App.VisitorSlideshowClock(() => fakeTime);
            timedClock.Restart(service.Settings.IntervalMilliseconds);
            Set(form, "visitorSlideshowClock", timedClock);
            var timer = Field<System.Windows.Forms.Timer>(form, "visitorTimer"); timer.Stop();
            void Tick() => typeof(System.Windows.Forms.Timer).GetMethod("OnTick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(timer, new object[] { EventArgs.Empty });
            fakeTime = service.Settings.IntervalMilliseconds - 1; Tick();
            check(Field<string>(form, "visitorCurrentPath") == first, "Visitor timer: no automatic change before its deadline");
            fakeTime++; Tick();
            check(Field<string>(form, "visitorCurrentPath") == second && timedClock.Remaining == service.Settings.IntervalMilliseconds,
                "Visitor timer: actual tick advances within the visitor folder and arms the next interval");
            var fullscreen = Field<App.FullscreenPausePolicy>(form, "fullscreenPolicy");
            fullscreen.Update(true, true, DateTime.UtcNow);
            await Invoke(form, "ToggleSlideshowPauseAsync", false);
            await Invoke(form, "ToggleSlideshowPauseAsync", false);
            check(!service.Settings.SlideshowPaused && timedClock.Paused && !Field<Button>(form, "nextWallpaperButton").Enabled,
                "Visitor pause: resuming the manual reason never clears fullscreen suspension");
            fullscreen.Update(false, false, DateTime.UtcNow); Tick();
            check(!timedClock.Paused, "Visitor pause: leaving fullscreen releases only the fullscreen reason");
            await service.SetActiveAsync(false);
            check(Equals(interval.SelectedItem, normalInterval) && !Field<CheckBox>(form, "shuffleCheckBox").Checked
                && Field<Button>(form, "visitorCopyButton").Visible,
                "Visitor playback: deactivation restores normal controls and copy availability");
            Set(form, "videoUiLoading", true); Field<ComboBox>(form, "wallpaperModeCombo").SelectedIndex = 1; Set(form, "videoUiLoading", false);
            await service.SetActiveAsync(true);
            check(Field<Panel>(form, "slideshowCard").Visible && !Field<Panel>(form, "videoCard").Visible
                && Field<Button>(form, "nextWallpaperButton").Visible,
                "Visitor folder UI: slideshow controls are visible even when the normal configuration selects video");
            await service.SetActiveAsync(false);
            Set(form, "videoUiLoading", true); Field<ComboBox>(form, "wallpaperModeCombo").SelectedIndex = 0; Set(form, "videoUiLoading", false);
            foreach (var source in new[] { App.VisitorSourceKind.Image, App.VisitorSourceKind.Video })
            {
                await service.ConfigureAsync(s => s with { Source = source, Path = first });
                check(!Field<Button>(form, "visitorCopyButton").Visible, "Visitor copy UI: action is hidden for " + source);
            }
            await service.ConfigureAsync(s => s with { Source = App.VisitorSourceKind.Image, Path = first });
            await service.SetActiveAsync(true);
            check(!Field<Form>(widgets, "nextWidget").Visible && !Field<Button>(form, "nextWallpaperButton").Enabled,
                "Visitor single-image mode: Next remains unavailable without a visitor folder");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(registry, false); }
    }
}
