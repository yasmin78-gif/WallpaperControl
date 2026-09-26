extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using System.Drawing;
using System.Reflection;
using Microsoft.Win32;

internal static class SlideshowStatusUiTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly string[] Languages = { "de", "en", "fr", "es", "ja" };
    private static readonly float[] Scales = { 1f, 1.5f, 2f };
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Members)!.GetValue(target)!;
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Members)!.SetValue(target, value);
    private static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Members)!.Invoke(target, args);
    private static void Language(string language) => typeof(App.Localization).GetMethod("ApplyLanguage", Members)!.Invoke(null, new object[] { language, false });

    internal static void Run(Action<bool, string> check)
    {
        using Task work = new(() =>
        {
            App.Localization.RefreshAvailableLanguages();
            string original = App.Localization.CurrentLanguage;
            try
            {
                foreach (string language in Languages)
                foreach (float scale in Scales)
                {
                    Language(language);
                    CheckSequence(check, language, scale);
                }
            }
            finally { Language(original); }
        });
        Thread thread = new(() => work.RunSynchronously(TaskScheduler.Default));
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); work.GetAwaiter().GetResult();
    }

    private static void ScaleFonts(Control control, float scale, List<Font> fonts)
    {
        if (System.ComponentModel.TypeDescriptor.GetProperties(control)["Font"]!.ShouldSerializeValue(control))
        {
            Font font = new(control.Font.FontFamily, control.Font.Size * scale, control.Font.Style);
            fonts.Add(font); control.Font = font;
        }
        foreach (Control child in control.Controls) ScaleFonts(child, scale, fonts);
    }

    private static void CheckSequence(Action<bool, string> check, string language, float scale)
    {
        string path = @"Software\WallpaperControl.StatusUiTests-" + Guid.NewGuid().ToString("N");
        List<Font> fonts = new();
        App.DesktopSlideshowState native = App.DesktopSlideshowState.Enabled | App.DesktopSlideshowState.Slideshow;
        bool failQuery = false;
        int queries = 0;
        using App.WidgetManager manager = new(() => { }, registryPath: path,
            notesStore: new App.NotesStore(Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"), "notes.json")));
        using App.MainForm main = new(manager, () => { queries++; if (failQuery) throw new InvalidOperationException("Test query failure"); return native; });
        try
        {
            main.Opacity = 0; main.Show();
            check(main.FormBorderStyle == FormBorderStyle.FixedDialog && !main.MaximizeBox && main.MinimizeBox
                && main.ClientSize == new Size(1260, 800), $"Status {language}/{scale}: fixed default window with minimize only");
            ScaleFonts(main, scale, fonts); main.Scale(new SizeF(scale, scale));
            main.ClientSize = new Size((int)(1260 * scale), (int)(800 * scale));
            var page = Field<Panel>(main, "wallpaperPage");
            var content = Field<Panel>(main, "wallpaperContent");
            var status = Field<Label>(main, "statusLabel");
            var activate = Field<Button>(main, "activateButton");
            var pause = Field<Button>(main, "pauseButton");
            var next = Field<Button>(main, "nextWallpaperButton");
            var policy = Field<App.FullscreenPausePolicy>(main, "fullscreenPolicy");
            DateTime now = new(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);
            DateTime deadline = now.AddMinutes(5);
            Set(main, "customSlideshowNextChange", deadline);
            object timer = Field<object>(main, "customSlideshowPreciseTimer");
            void Fits(string state)
            {
                Invoke(main, "ArrangeWallpaperPage", scale);
                check(!page.VerticalScroll.Visible && !page.HorizontalScroll.Visible && page.ClientRectangle.Contains(content.Bounds),
                    $"Status {language}/{scale}/{state}: entire Wallpaper content fits without scrollbars");
                var current = Field<Panel>(main, "currentWallpaperCard");
                check(current.Controls.Cast<Control>().All(c => current.ClientRectangle.Contains(c.Bounds))
                    && Field<Button>(main, "statisticsButton").Bottom <= current.Height,
                    $"Status {language}/{scale}/{state}: complete current wallpaper actions remain visible");
            }
            Invoke(main, "RefreshWallpaperUi");
            check(!status.Visible && !activate.Visible && next.Enabled, $"Status {language}/{scale}: normal native slideshow active");
            Fits("active");
            ComboBox position = Field<ComboBox>(main, "positionComboBox");
            int originalPosition = position.SelectedIndex;
            string[] originalLabels = position.Items.Cast<object>().Select(item => item.ToString()!).ToArray();
            int spanIndex = position.Items.Count - 1;
            bool originalLoading = Field<bool>(main, "loading");
            Set(main, "loading", true);
            position.SelectedIndex = spanIndex;
            Set(main, "loading", originalLoading);
            main.UpdateSpanPositionCaption(2);
            string nativeCaption = App.Localization.Get("PositionSpanNative");
            check(position.Text == nativeCaption && nativeCaption != "PositionSpanNative" && position.SelectedIndex == spanIndex,
                $"Span {language}/{scale}: multi-monitor caption is localized and preserves selection");
            check(position.Items.Cast<object>().Take(spanIndex).Select(item => item.ToString()).SequenceEqual(originalLabels.Take(spanIndex)),
                $"Span {language}/{scale}: other layout captions remain unchanged");
            check(TextRenderer.MeasureText(nativeCaption, position.Font).Width + (int)(30 * scale) <= position.Width,
                $"Span {language}/{scale}: selected native caption fits");
            position.DroppedDown = true;
            main.UpdateSpanPositionCaption(1);
            check(position.DroppedDown && position.Text == nativeCaption,
                $"Span {language}/{scale}: monitor change defers caption update while dropdown is open");
            position.DroppedDown = false;
            main.UpdateSpanPositionCaption(1);
            check(position.Text == App.Localization.Get("PositionSpan") && position.SelectedIndex == spanIndex,
                $"Span {language}/{scale}: single monitor restores ordinary caption and selection");
            Set(main, "loading", true);
            position.SelectedIndex = originalPosition;
            Set(main, "loading", originalLoading);
            main.UpdateSpanPositionCaption(2);
            check(position.SelectedIndex == originalPosition && Field<bool>(main, "loading") == originalLoading,
                $"Span {language}/{scale}: caption refresh preserves another selected layout and loading state");
            main.UpdateSpanPositionCaption(Screen.AllScreens.Length);
            foreach (string name in new[] { "intervalComboBox", "positionComboBox", "transitionComboBox", "transitionDurationComboBox" })
            {
                ComboBox combo = Field<ComboBox>(main, name);
                // Explicit scaling simulates DPI without changing the window's real DeviceDpi.
                Invoke(main, "ArrangeWallpaperPage", scale);
                combo.DroppedDown = true;
                check(combo.DroppedDown, $"Status {language}/{scale}: {name} opens");
                Invoke(main, "ArrangeWallpaperPage", scale);
                check(combo.DroppedDown, $"Status {language}/{scale}: repeated layout preserves open {name}");
                combo.DroppedDown = false;
                Invoke(main, "RefreshWallpaperUi");
                combo.DroppedDown = true;
                Invoke(main, "RefreshWallpaperUi");
                check(combo.DroppedDown, $"Status {language}/{scale}: unchanged status poll preserves open {name}");
                combo.DroppedDown = false;
            }
            policy.Update(true, true, now);
            Invoke(main, "CheckSlideshowStatus");
            check(status.Text == App.Localization.Get("StatusFullscreenPaused") && status.Visible && !activate.Visible && !next.Enabled,
                $"Status {language}/{scale}: screenshot fullscreen pause displayed");
            Fits("fullscreen");
            policy.Update(true, false, now.AddSeconds(1));
            policy.Update(true, false, now.AddSeconds(1.5));
            native = App.DesktopSlideshowState.Enabled;
            Invoke(main, "CheckSlideshowStatus");
            check(status.Text == App.Localization.Get("StatusInactive") && activate.Visible,
                $"Status {language}/{scale}: reproduces asynchronous Windows resume reporting inactive");
            Fits("inactive+activation");
            string? output = Environment.GetEnvironmentVariable("STATUS_TEST_OUTPUT");
            if (output != null)
            {
                Directory.CreateDirectory(output);
                // Render the page surface: Windows caps top-level test windows
                // at the physical monitor height even with simulated DPI fonts.
                using Bitmap image = new(page.Width, page.Height);
                page.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                image.Save(Path.Combine(output, $"status-page-{language}-{scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}.png"));
            }
            native |= App.DesktopSlideshowState.Slideshow;
            Invoke(main, "RefreshWallpaperUi");
            check(!status.Visible && !activate.Visible && next.Enabled && pause.Enabled,
                $"Status {language}/{scale}: next regular poll clears stale inactive state after native resume");
            check(ReferenceEquals(timer, Field<object>(main, "customSlideshowPreciseTimer"))
                && Field<DateTime>(main, "customSlideshowNextChange") == deadline && !Field<bool>(main, "slideshowPaused"),
                $"Status {language}/{scale}: synchronization does not restart scheduling or change manual pause");
            Set(main, "customSlideshowEngineActive", true);
            native = App.DesktopSlideshowState.None;
            int before = queries;
            Invoke(main, "RefreshWallpaperUi");
            check(queries == before && !status.Visible && next.Enabled,
                $"Status {language}/{scale}: application engine outranks inactive Windows status");
            Set(main, "initializingDesktop", true);
            Invoke(main, "RefreshWallpaperUi");
            check(!next.Enabled, $"Status {language}/{scale}: regular polling preserves the desktop startup guard");
            Set(main, "initializingDesktop", false);
            Set(main, "slideshowPaused", true);
            Invoke(main, "RefreshWallpaperUi");
            check(status.Text == App.Localization.Get("StatusPaused") && pause.Text == App.Localization.Get("ResumeSlideshow") && !activate.Visible,
                $"Status {language}/{scale}: manual pause remains authoritative");
            Fits("manual");
            Set(main, "customSlideshowEngineActive", false); Set(main, "slideshowPaused", false);
            native = App.DesktopSlideshowState.DisabledByRemoteSession;
            Invoke(main, "RefreshWallpaperUi");
            check(status.Text == App.Localization.Get("StatusRemoteDisabled") && !activate.Visible,
                $"Status {language}/{scale}: remote-disabled state is not treated as active");
            Fits("remote");
            failQuery = true;
            Invoke(main, "RefreshWallpaperUi");
            check(status.Text == App.Localization.Get("StatusRemoteDisabled") && !activate.Visible,
                $"Status {language}/{scale}: query failure preserves last known status");
            object editor = Field<object>(main, "widgetEditor");
            Invoke(main, "SelectMainSection", true); Invoke(main, "SelectMainSection", false);
            check(ReferenceEquals(editor, Field<object>(main, "widgetEditor")), $"Status {language}/{scale}: Widgets editor remains unchanged");
        }
        finally
        {
            main.Dispose();
            foreach (Font font in fonts) font.Dispose();
            Registry.CurrentUser.DeleteSubKeyTree(path, false);
        }
    }
}
