extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using System.Drawing;
using System.Reflection;
using Microsoft.Win32;

internal static class VisitorRevisionTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Members)!.GetValue(target)!;
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Members)!.SetValue(target, value);
    internal static void Run(Action<bool, string> check)
    {
        Exception? error = null;
        Thread thread = new(() =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "WallpaperControl-VisitorRevision-" + Guid.NewGuid().ToString("N"));
            string key = @"Software\WallpaperControl.VisitorRevision-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(directory);
            try
            {
                string original = Path.Combine(directory, "original.png"), visitor = Path.Combine(directory, "visitor.png");
                using (Bitmap bitmap = new(8, 8)) { bitmap.Save(original); bitmap.Save(visitor); }
                foreach (bool paused in new[] { false, true })
                {
                    string nativePath = original;
                    using App.WidgetManager widgets = new(() => { }, registryPath: key);
                    using App.MainForm form = new(widgets, () => 0, () => nativePath);
                    var service = Field<App.VisitorModeService>(form, "visitorMode");
                    check(!form.VisitorMainSwitchAvailable, "Visitor revision: no main switch without a saved source");
                    Field<TextBox>(form, "visitorPath").Text = visitor;
                    check(!form.VisitorMainSwitchAvailable, "Visitor revision: unsaved source selection does not expose main switch");
                    service.ConfigureAsync(s => s with { Path = visitor, Source = App.VisitorSourceKind.Image }).GetAwaiter().GetResult();
                    check(form.VisitorMainSwitchAvailable && Field<CheckBox>(form, "visitorSwitch").Parent == Field<Panel>(form, "visitorMainBar")
                        && Field<Panel>(form, "visitorMainBar").Parent == Field<Panel>(form, "wallpaperContent"), "Visitor revision: saved valid source exposes switch in Wallpaper workspace");
                    Set(form, "loading", true);
                    var interval = Field<ComboBox>(form, "intervalComboBox"); interval.SelectedIndex = 0;
                    Field<TextBox>(form, "folderTextBox").Text = directory;
                    Set(form, "loading", false);
                    Set(form, "customSlideshowEngineActive", true); Set(form, "slideshowPaused", paused);
                    Set(form, "customSlideshowNextChange", DateTime.Now.AddSeconds(37));
                    service.SetActiveAsync(true).GetAwaiter().GetResult();
                    nativePath = visitor; // Windows still reports visitor media while SetWallpaper is pending.
                    var host = new FakeHost(); int starts = 0;
                    form.StartImageSessionForTests = (path, predecessors) => { starts++; return App.PersistentDesktopTransitionManager.TryStartSession(path, () => { }, () => host = new(), predecessors); };
                    long remaining = service.Settings.Resume!.RemainingTicks;
                    check(Field<CheckBox>(form, "visitorSwitch").Enabled && !Field<Panel>(form, "slideshowCard").Enabled,
                        "Visitor revision: main switch remains enabled while normal wallpaper controls are locked");
                    service.SetActiveAsync(false).GetAwaiter().GetResult();
                    string? reconciled = form.ReconcileNativeWallpaper(nativePath, () => true);
                    form.GetType().GetMethod("CheckSlideshowStatus", Members)!.Invoke(form, null);
                    check(reconciled == original && !host.IsDisposed && Field<bool>(form, "slideshowPaused") == paused,
                        "Visitor pause: delayed own native read retains renderer and original manual pause=" + paused);
                    check(Field<Button>(form, "pauseButton").Text == App.Localization.Get(paused ? "ResumeSlideshow" : "PauseSlideshow"),
                        "Visitor pause: main page status matches original manual pause=" + paused);
                    check(starts == 1 && host.CurrentWallpaperPath == original && host.Transitions == 0 && nativePath == visitor,
                        "Visitor revision: original image presented immediately despite stale Windows path; paused=" + paused);
                    check(Field<bool>(form, "slideshowPaused") == paused && Field<bool>(form, "customSlideshowEngineActive")
                        && Math.Abs((Field<DateTime>(form, "customSlideshowNextChange") - DateTime.Now).TotalSeconds - TimeSpan.FromTicks(remaining).TotalSeconds) < 1,
                        "Visitor revision: original pause and remaining deadline retained without a scheduler advance");
                    if (!paused)
                    {
                        string preview = Path.Combine(Path.GetTempPath(), "WallpaperControl-Visitor-Validation"); Directory.CreateDirectory(preview);
                        form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents();
                        using Bitmap image = new(form.Width, form.Height);
                        form.DrawToBitmap(image, new Rectangle(0, 0, image.Width, image.Height)); image.Save(Path.Combine(preview, "visitor-main-revised.png"));
                        form.Hide();
                    }
                    for (int toggle = 0; toggle < 6; toggle++)
                    {
                        nativePath = original;
                        check(form.ReconcileNativeWallpaper(nativePath, () => true) == original, "Visitor pause: native acknowledgement keeps original image");
                        service.SetActiveAsync(true).GetAwaiter().GetResult(); nativePath = visitor;
                        service.SetActiveAsync(false).GetAwaiter().GetResult();
                        check(form.ReconcileNativeWallpaper(nativePath, () => true) == original && Field<bool>(form, "slideshowPaused") == paused
                            && host.Transitions == 0, "Visitor pause: rapid round trip retains pause without extra image advance " + paused + "/" + toggle);
                    }
                    nativePath = original; form.ReconcileNativeWallpaper(nativePath, () => true);
                    var policy = Field<App.FullscreenPausePolicy>(form, "fullscreenPolicy");
                    policy.Update(true, true, DateTime.UtcNow);
                    service.SetActiveAsync(true).GetAwaiter().GetResult(); nativePath = visitor;
                    service.SetActiveAsync(false).GetAwaiter().GetResult(); form.ReconcileNativeWallpaper(nativePath, () => true);
                    form.GetType().GetMethod("CheckSlideshowStatus", Members)!.Invoke(form, null);
                    check(policy.IsPaused && Field<bool>(form, "slideshowPaused") == paused
                        && Field<Label>(form, "statusLabel").Text == App.Localization.Get("StatusFullscreenPaused"),
                        "Visitor pause: fullscreen reason and its status remain independent of manual pause");
                    nativePath = original; form.ReconcileNativeWallpaper(nativePath, () => true);
                    check(form.ReconcileNativeWallpaper("external-user-wallpaper", () => true) == "external-user-wallpaper"
                        && Field<bool>(form, "slideshowPaused") && policy.IsPaused && host.IsDisposed,
                        "Visitor pause: real external changes still pause the image engine without clearing fullscreen pause");
                    App.PersistentDesktopTransitionManager.Shutdown();
                }
                VerifyNativeHandoff(check, original, visitor);
                using (var registry = Registry.CurrentUser.CreateSubKey(key))
                    registry.SetValue("VisitorMode", "{\"WidgetEnabled\":true,\"WidgetLocked\":true,\"WidgetX\":-123,\"WidgetY\":45}");
                var settings = App.WidgetSettings.Load(key);
                check(settings.Visitor.ShowTitle && App.VisitorWidgetSettings.Parse("{}").ShowTitle,
                    "Visitor widget title: existing configurations keep the title visible");
                check(settings.Visitor.Enabled && settings.Visitor.Locked && settings.Visitor.Location == new Point(-123, 45),
                    "Visitor revision: legacy widget visibility lock and position migrate without losing settings");
                settings.Visitor.Style = App.SystemWidgetStyle.Clean; settings.Visitor.ScalePercent = 150; settings.Visitor.FontSize = 16;
                settings.Visitor.BackgroundArgb = Color.Blue.ToArgb(); settings.Visitor.TextArgb = Color.Yellow.ToArgb(); settings.Visitor.BackgroundOpacityPercent = 23;
                settings.Save(key); var loaded = App.WidgetSettings.Load(key);
                check(loaded.Visitor.Style == App.SystemWidgetStyle.Clean && loaded.Visitor.ScalePercent == 150 && loaded.Visitor.FontSize == 16
                    && loaded.Visitor.BackgroundArgb == Color.Blue.ToArgb() && loaded.Visitor.TextArgb == Color.Yellow.ToArgb()
                    && loaded.Visitor.BackgroundOpacityPercent == 23 && loaded.Visitor.Location == settings.Visitor.Location,
                    "Visitor revision: design options and geometry survive canonical widget persistence");
                var clone = loaded.Clone(); clone.Visitor.FontSize = 9;
                check(loaded.Visitor.FontSize == 16, "Visitor revision: widget snapshots clone visitor design independently");
                // No service attached: exercise normal editor transaction mechanics without showing a desktop widget.
                using App.WidgetManager manager = new(() => { }, registryPath: key);
                App.WidgetEditSession? session = null;
                using App.WidgetSettingsEditor editor = new(manager.Settings, preview: value => session!.Preview(value));
                session = new(manager, editor.ReadWidgetSettings, editor.LoadSettings); session.Begin();
                editor.SelectWidget("VisitorTitle");
                Field<NumericUpDown>(editor, "visitorScale").Value = 180;
                check(session.IsDirty && manager.Settings.Visitor.ScalePercent == 180 && App.WidgetSettings.Load(key).Visitor.ScalePercent == 150,
                    "Visitor revision: visitor page uses existing unsaved live preview");
                session.Discard();
                check(manager.Settings.Visitor.ScalePercent == 150 && Field<NumericUpDown>(editor, "visitorScale").Value == 150,
                    "Visitor revision: discard restores visitor design and editor controls");
                Field<CheckBox>(editor, "visitorShowTitle").Checked = false;
                check(session.IsDirty && !manager.Settings.Visitor.ShowTitle && App.WidgetSettings.Load(key).Visitor.ShowTitle,
                    "Visitor widget title: hiding uses live preview without saving prematurely");
                session.Discard();
                check(manager.Settings.Visitor.ShowTitle && Field<CheckBox>(editor, "visitorShowTitle").Checked,
                    "Visitor widget title: discard restores visibility and the editor checkbox");
                Field<CheckBox>(editor, "visitorShowTitle").Checked = false;
                check(session.Save() && !App.WidgetSettings.Load(key).Visitor.ShowTitle,
                    "Visitor widget title: hidden title survives canonical persistence");
                Field<CheckBox>(editor, "visitorLocked").Checked = false;
                Field<NumericUpDown>(editor, "visitorFont").Value = 18;
                check(session.Save() && App.WidgetSettings.Load(key).Visitor.FontSize == 18 && !App.WidgetSettings.Load(key).Visitor.Locked,
                    "Visitor revision: existing widget Save commits lock and styling");
                using (Form previewForm = new() { ClientSize = new(1100, 800), ShowInTaskbar = false, Opacity = 0 })
                {
                    editor.Dock = DockStyle.Fill; previewForm.Controls.Add(editor); previewForm.Show(); Application.DoEvents();
                    string preview = Path.Combine(Path.GetTempPath(), "WallpaperControl-Visitor-Validation"); Directory.CreateDirectory(preview);
                    using Bitmap image = new(previewForm.Width, previewForm.Height);
                    previewForm.DrawToBitmap(image, new Rectangle(0, 0, image.Width, image.Height)); image.Save(Path.Combine(preview, "visitor-widget-editor.png"));
                    previewForm.Controls.Remove(editor);
                }
                var serviceForWidget = new App.VisitorModeService(new(), _ => true, () => new(), () => Task.CompletedTask, _ => Task.CompletedTask);
                Point? moved = null;
                using App.VisitorModeWidgetForm widget = new(serviceForWidget, point => moved = point);
                string output = Path.Combine(Path.GetTempPath(), "WallpaperControl-Visitor-Validation"); Directory.CreateDirectory(output);
                foreach (string language in new[] { "de", "en", "fr", "es", "ja" })
                foreach (App.SystemWidgetStyle style in Enum.GetValues<App.SystemWidgetStyle>())
                {
                    widget.Apply(new() { Style = style }, language);
                    using Bitmap image = widget.RenderBitmap();
                    check(image.Width * image.Height < 248 * 72 && image.GetPixel(0, 0).A == 0,
                        "Visitor revision: compact shared-style layout and transparent corners " + language + "/" + style);
                    image.Save(Path.Combine(output, "visitor-widget-" + language + "-" + style + ".png"));
                }
                widget.Apply(loaded.Visitor, "de");
                widget.Apply(new() { ShowTitle = false, FontSize = 24 }, "de");
                using (Bitmap compact = widget.RenderBitmap())
                    check(compact.Size == new Size(58, 32) && compact.GetPixel(0, 0).A == 0
                        && Field<Rectangle>(widget, "switchBounds") == new Rectangle(4, 4, 50, 24),
                        "Visitor widget title: only the toggle remains with a compact transparent window and matching hit target");
                widget.Apply(new() { ShowTitle = false, ScalePercent = 150 }, "ja");
                using (Bitmap compact = widget.RenderBitmap(192))
                    check(compact.Size == new Size(174, 96) && Field<Rectangle>(widget, "switchBounds") == new Rectangle(12, 12, 150, 72),
                        "Visitor widget title: compact size and toggle hit target scale together with DPI");
                widget.Apply(loaded.Visitor, "de");
                using (Bitmap image = widget.RenderBitmap(192))
                    check(image.Width > 400 && image.Height > 96, "Visitor revision: font size and user scale combine with monitor DPI");
                var drag = Field<App.WidgetDragHandler>(widget, "drag");
                drag.GetType().GetMethod("BeginDrag", Members)!.Invoke(drag, new object[] { widget, new MouseEventArgs(MouseButtons.Left, 1, 2, 2, 0) });
                check(!Field<bool>(drag, "dragging"), "Visitor revision: canonical position lock prevents dragging");
                var unlocked = loaded.Visitor.Clone(); unlocked.Locked = false; widget.Apply(unlocked, "de");
                _ = widget.Handle; widget.Location = new Point(80, 90);
                Point cursor = new(10, 10); Set(drag, "cursorPosition", (Func<Point>)(() => cursor));
                drag.GetType().GetMethod("BeginDrag", Members)!.Invoke(drag, new object[] { widget, new MouseEventArgs(MouseButtons.Left, 1, 2, 2, 0) });
                cursor = new Point(50, 30);
                drag.GetType().GetMethod("ContinueDrag", Members)!.Invoke(drag, new object[] { widget, new MouseEventArgs(MouseButtons.Left, 0, 2, 2, 0) });
                drag.GetType().GetMethod("EndDrag", Members)!.Invoke(drag, new object[] { widget, new MouseEventArgs(MouseButtons.Left, 1, 2, 2, 0) });
                check(widget.Location == new Point(120, 110) && moved == widget.Location,
                    "Visitor revision: unlocked drag uses shared positioning and reports the final saved location");
                widget.Apply(new() { ShowTitle = false }, "de");
                cursor = new(10, 10); widget.Location = new(80, 90);
                typeof(Control).GetMethod("OnMouseDown", Members)!.Invoke(widget, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 15, 15, 0) });
                cursor = new(50, 30);
                typeof(Control).GetMethod("OnMouseMove", Members)!.Invoke(widget, new object[] { new MouseEventArgs(MouseButtons.Left, 0, 15, 15, 0) });
                typeof(Control).GetMethod("OnMouseUp", Members)!.Invoke(widget, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 15, 15, 0) });
                check(widget.Location == new Point(120, 110) && !serviceForWidget.Active,
                    "Visitor widget title: dragging the compact toggle moves the widget without changing visitor mode");
                typeof(Control).GetMethod("OnMouseDown", Members)!.Invoke(widget, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 15, 15, 0) });
                typeof(Control).GetMethod("OnMouseUp", Members)!.Invoke(widget, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 15, 15, 0) });
                check(serviceForWidget.Active, "Visitor widget title: a compact toggle click still changes visitor mode");
            }
            catch (Exception ex) { error = ex; }
            finally
            {
                App.PersistentDesktopTransitionManager.Shutdown(); Registry.CurrentUser.DeleteSubKeyTree(key, false);
                string resolved = Path.GetFullPath(directory);
                if (Path.GetDirectoryName(resolved) == Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))
                    && Path.GetFileName(resolved).StartsWith("WallpaperControl-VisitorRevision-", StringComparison.Ordinal)) Directory.Delete(resolved, true);
            }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error != null) throw error;
    }
    private static void VerifyNativeHandoff(Action<bool, string> check, string original, string visitor)
    {
        var clock = new ManualClock();
        void Start() => App.PersistentDesktopTransitionManager.TryStartSession(original, () => { }, () => new FakeHost(),
            new[] { visitor, "neutral" }, clock);
        Start();
        check(!App.PersistentDesktopTransitionManager.HasExternalWallpaperChange(original, observeHandoff: false)
            && App.PersistentDesktopTransitionManager.HasExternalWallpaperChange("diagnostic-only-external", observeHandoff: false)
            && !App.PersistentDesktopTransitionManager.HasExternalWallpaperChange(visitor),
            "Visitor pause: diagnostic reads neither acknowledge nor consume the pending native handoff");
        check(!App.PersistentDesktopTransitionManager.HasExternalWallpaperChange(visitor)
            && !App.PersistentDesktopTransitionManager.HasExternalWallpaperChange("neutral"),
            "Visitor pause: only recorded visitor and neutral predecessors are deferred");
        check(App.PersistentDesktopTransitionManager.HasExternalWallpaperChange("unrelated-user-wallpaper"),
            "Visitor pause: genuine unrelated external wallpaper is still detected immediately");
        Start();
        check(!App.PersistentDesktopTransitionManager.HasExternalWallpaperChange(original)
            && App.PersistentDesktopTransitionManager.HasExternalWallpaperChange(visitor),
            "Visitor pause: after native acknowledgement even the former visitor path is external again");
        Start(); clock.Advance(TimeSpan.FromSeconds(5));
        check(App.PersistentDesktopTransitionManager.HasExternalWallpaperChange(visitor),
            "Visitor pause: handoff expires using monotonic time and never suppresses a change indefinitely");
        App.PersistentDesktopTransitionManager.Shutdown();
        App.PersistentDesktopTransitionManager.TryStartSession(original, () => { }, () => new FakeHost());
        check(App.PersistentDesktopTransitionManager.HasExternalWallpaperChange(visitor),
            "Visitor pause: ordinary image sessions retain existing external-change policy");
        App.PersistentDesktopTransitionManager.Shutdown();
    }
    private sealed class ManualClock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => ticks;
        internal void Advance(TimeSpan duration) => ticks += duration.Ticks;
    }
    private sealed class FakeHost : App.IPersistentWallpaperHost
    {
        public bool IsDisposed { get; private set; }
        public string? CurrentWallpaperPath { get; private set; }
        internal int Transitions;
        public bool Initialize(string path) { CurrentWallpaperPath = path; return true; }
        public bool EnsureDesktopPlacement() => !IsDisposed;
        public void SetActivitySuspended(bool suspended) { }
        public void SetWallpaperPosition(App.DesktopWallpaperPosition position) { }
        public Task TransitionToAsync(string path, App.WallpaperTransitionKind kind, int milliseconds, App.WallpaperTransitionDirection direction,
            App.WallpaperZoomMode zoomMode, CancellationToken cancellationToken) { Transitions++; CurrentWallpaperPath = path; return Task.CompletedTask; }
        public void CommitCurrentPath(string path) => CurrentWallpaperPath = path;
        public void Dispose() => IsDisposed = true;
    }
}
