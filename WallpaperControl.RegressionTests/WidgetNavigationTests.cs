extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using Microsoft.Win32;

internal static class WidgetNavigationTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly string[] Languages = { "de", "en", "fr", "es", "ja" };
    private static readonly string[] Keys = { "SettingsNavCalendar", "SettingsNavClock", "SettingsNavNextWallpaper", "NotesTitle", "SettingsNavSystem", "WallpaperInfoTitle", "SettingsNavWeather" };
    private static readonly float[] Scales = { 1, 1.5f, 2 };
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, Members)!.GetValue(value)!;
    private static object? Invoke(object value, string name, params object[] args) => value.GetType().GetMethod(name, Members)!.Invoke(value, args);
    private static string RegistryPath() => @"Software\WallpaperControl.NavigationTests-" + Guid.NewGuid().ToString("N");
    private static App.NotesStore Store() => new(Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"), "notes.json"));

    internal static void Run(Action<bool, string> check)
    {
        using Task work = new(() => { Transactions(check); MainNavigation(check); });
        Thread thread = new(() => work.RunSynchronously(TaskScheduler.Default));
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); work.GetAwaiter().GetResult();
    }
    private static void Transactions(Action<bool, string> check)
    {
        string path = RegistryPath();
        try
        {
            App.WidgetSettings initial = new() { NotesEnabled = true, NotesLocation = new Point(100, 100), ClockLocation = new Point(90, 80), WallpaperInfoFontSize = 18, WeatherLocationName = "Berlin" };
            initial.Save(path);
            using App.WidgetManager manager = new(() => { }, registryPath: path, notesStore: Store());
            manager.Start();
            Form notes = Field<Form>(manager, "notesWidget"); notes.Opacity = 0;
            App.WidgetEditSession? session = null;
            using App.WidgetSettingsEditor editor = new(manager.Settings, preview: value => session!.Preview(value));
            session = new(manager, editor.ReadWidgetSettings, editor.LoadSettings);
            session.Begin();
            check(session.Active && !session.IsDirty && ReferenceEquals(notes, Field<Form>(manager, "notesWidget")), "V2 begin editing retains widget identity without creating a dirty draft");
            Field<NumericUpDown>(editor, "notesMaximumHeight").Value = 750;
            check(session.IsDirty && manager.Settings.NotesMaximumHeight == 750 && App.WidgetSettings.Load(path).NotesMaximumHeight == 500, "V2 live preview changes runtime only, without persistence");
            foreach (string key in Keys) editor.SelectWidget(key);
            check(manager.Settings.NotesMaximumHeight == 750 && ReferenceEquals(notes, Field<Form>(manager, "notesWidget")), "V2 switching all widget pages preserves one draft and existing runtime window");
            check(!session.TryLeave(() => App.WidgetEditDecision.Cancel) && session.Active && session.IsDirty, "V2 navigation Cancel retains the preview and edit session");
            Invoke(manager, "SaveClockLocation", new Point(200, 210));
            check(App.WidgetSettings.Load(path).ClockLocation == initial.ClockLocation, "V2 desktop drag does not persist during editing");
            check(session.TryLeave(() => App.WidgetEditDecision.Discard) && !session.Active && manager.Settings.NotesMaximumHeight == 500
                && manager.Settings.ClockLocation == initial.ClockLocation && ReferenceEquals(notes, Field<Form>(manager, "notesWidget")), "V2 navigation Discard restores settings, position and existing widget identity");
            session.Begin(); Field<CheckBox>(editor, "notesLocked").Checked = true;
            Invoke(manager, "SaveClockLocation", new Point(220, 230));
            check(session.TryLeave(() => App.WidgetEditDecision.Save) && App.WidgetSettings.Load(path).NotesLocked
                && App.WidgetSettings.Load(path).ClockLocation == new Point(220, 230), "V2 navigation Save persists options and live desktop positions together");
            session.Begin(); editor.ResetDefaults();
            check(!manager.Settings.NotesEnabled && manager.Settings.WallpaperInfoFontSize == 13 && session.IsDirty, "V2 Reset previews widget defaults without saving them");
            session.Discard();
            check(manager.Settings.NotesEnabled && manager.Settings.NotesLocked && editor.ReadWidgetSettings(false).WallpaperInfoFontSize == 18, "V2 Discard after Reset restores controls and runtime");
            Field<CheckBox>(editor, "notesEnabled").Checked = false;
            check(Field<Form?>(manager, "notesWidget") == null, "V2 disabling a widget previews its removal");
            check(session.Save() && !session.IsDirty && !App.WidgetSettings.Load(path).NotesEnabled, "V2 explicit Save commits disabled state and starts a clean draft");
            int prompts = 0;
            check(session.TryLeave(() => { prompts++; return App.WidgetEditDecision.Cancel; }) && prompts == 0, "V2 clean navigation never prompts or saves implicitly");
            session.Begin(); Invoke(manager, "SaveClockLocation", new Point(330, 340));
            check(session.IsDirty, "V2 a desktop-position-only edit is detected as unsaved");
            session.Discard();
            check(!session.IsDirty && manager.Settings.ClockLocation == new Point(220, 230), "V2 position-only Discard restores the committed location");
            string editedCity = Guid.NewGuid().ToString("N");
            Field<TextBox>(editor, "weatherLocationTextBox").Text = editedCity;
            check(session.IsDirty && manager.Settings.WeatherLocationName == "Berlin", "V2 unsubmitted weather text is dirty without triggering a location refresh");
            session.Discard();
            check(Field<TextBox>(editor, "weatherLocationTextBox").Text == "Berlin", "V2 discard restores the weather input as well as runtime settings");
            session.TryLeave(() => App.WidgetEditDecision.Discard);
            // A failed initial load must remain protected even after Reset.
            var protectedSettings = manager.Settings;
            typeof(App.WidgetSettings).GetField("loadFailed", Members)!.SetValue(protectedSettings, true);
            typeof(App.WidgetManager).GetField("settings", Members)!.SetValue(manager, protectedSettings);
            session.Begin(); editor.ResetDefaults();
            check(!session.TryLeave(() => App.WidgetEditDecision.Save) && session.Active && session.IsDirty,
                "V2 failed Save after Reset keeps the transaction pending and does not bypass incomplete-load protection");
            session.Discard();

        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, false); }
    }
    private static void ScaleFonts(Control root, float scale, List<Font> owned)
    {
        if (System.ComponentModel.TypeDescriptor.GetProperties(root)["Font"]!.ShouldSerializeValue(root))
        {
            Font font = new(root.Font.FontFamily, root.Font.Size * scale, root.Font.Style);
            owned.Add(font); root.Font = font;
        }
        foreach (Control child in root.Controls) ScaleFonts(child, scale, owned);
    }
    private static void MainNavigation(Action<bool, string> check)
    {
        string path = RegistryPath();
        try
        {
            foreach (string language in Languages)
            foreach (float scale in Scales)
            {
                App.WidgetManager? manager = null;
                App.MainForm? main = null;
                try
                {
                    manager = new(() => { }, registryPath: path, notesStore: Store());
                    main = new(manager);
                    CheckMainLayout(check, main, language, scale);
                }
                finally { main?.Dispose(); manager?.Dispose(); }
            }
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, false); }
    }
    private static void CheckMainLayout(Action<bool, string> check, App.MainForm main, string language, float scale)
    {
        List<Font> scaledFonts = new();
        try
        {
                main.Opacity = 0; main.Show();
                var editor = Field<App.WidgetSettingsEditor>(main, "widgetEditor");
                editor.ApplyPresentation(false, language);
                check(!Field<bool>(main, "showingWidgets") && Field<Panel>(main, "wallpaperPage").Visible, $"V2 {language}/{scale} starts on Wallpaper");
                var timer = Field<System.Windows.Forms.Timer>(main, "wallpaperRefreshTimer");
                int interval = timer.Interval;
                for (int i = 0; i < 3; i++) { Invoke(main, "SelectMainSection", true); Invoke(main, "SelectMainSection", false); }
                check(ReferenceEquals(editor, Field<App.WidgetSettingsEditor>(main, "widgetEditor")) && timer.Interval == interval && !timer.Enabled
                    && !Field<bool>(main, "slideshowPaused"), $"V2 {language}/{scale} main navigation leaves services, timer and pause state unchanged");
                Invoke(main, "SelectMainSection", true);
                editor.ApplyPresentation(false, language);
                check(editor.WidgetKeys.Order().SequenceEqual(Keys.Order()) && editor.WidgetKeys.Select(k => App.Localization.Get(k, language))
                    .SequenceEqual(editor.WidgetKeys.Select(k => App.Localization.Get(k, language)).OrderBy(n => n, StringComparer.Create(CultureInfo.GetCultureInfo(language), true))),
                    $"V2 {language}/{scale} all seven widgets sort by their translated names");
                ScaleFonts(main, scale, scaledFonts);
                main.Scale(new SizeF(scale, scale)); main.PerformLayout();
                var navigation = Field<FlowLayoutPanel>(editor, "navigation");
                check(navigation.Controls.OfType<Button>().All(b => TextRenderer.MeasureText(b.Text, b.Font).Width + b.Padding.Horizontal <= b.Width)
                    && navigation.Controls.OfType<Button>().Single(b => b.AccessibleDescription == "▤").Text.Contains(App.Localization.Get("NotesTitle", language), StringComparison.Ordinal),
                    $"V2 {language}/{scale} complete navigation captions and Notes icon fit");
                foreach (string key in Keys) { editor.SelectWidget(key); check(editor.SelectedKey == key, $"V2 {language}/{scale} opens {key}"); }
                using App.SettingsForm general = new(false, "system", 0, 0, 0, 0, 0, 0, 0, 0, "", true, false, true, true, true, 92);
                var generalPages = Field<TabControl>(general, "settingsTabControl");
                check(generalPages.TabPages.Count == 5 && generalPages.TabPages.Cast<TabPage>().All(p => !Keys.Contains((string)p.Tag!)), $"V2 {language}/{scale} general Settings contains no widget configuration");
                foreach (bool dark in new[] { false, true })
                {
                    typeof(App.MainForm).GetField("themeMode", Members)!.SetValue(main, dark ? "dark" : "light");
                    Invoke(main, "ApplyWindowsTheme"); Invoke(main, "ApplyNavigationPresentation", language);
                    using App.NoteEditorForm noteEditor = new(Store(), null, language, darkMode: dark);
                    using App.NotesManagerForm noteManager = new(Store(), language, darkMode: dark);
                    check(dark ? noteEditor.BackColor == noteManager.BackColor : noteEditor.BackColor == editor.BackColor && noteManager.BackColor == editor.BackColor,
                        $"V2 {language}/{scale}/{dark} Notes dialogs retain application theme support");
                    using App.WidgetChangesDialog dialog = new(dark, language);
                    dialog.Scale(new SizeF(scale, scale)); dialog.PerformLayout();
                    check(dialog.CancelButton != null && dialog.Controls[0].Controls.OfType<FlowLayoutPanel>().Single().Controls.Count == 3
                        && dialog.BackColor == editor.BackColor, $"V2 {language}/{scale}/{dark} themed unsaved dialog has Save Discard Cancel");
                    string? output = Environment.GetEnvironmentVariable("V2_TEST_OUTPUT");
                    if (output != null)
                    {
                        Directory.CreateDirectory(output);
                        editor.SelectWidget("NotesTitle");
                        using Bitmap bitmap = new(main.Width, main.Height);
                        main.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        bitmap.Save(Path.Combine(output, $"main-{language}-{scale.ToString(CultureInfo.InvariantCulture)}-{dark}.png"));
                        if (scale == 1 && language == "de")
                        {
                            foreach (string key in Keys)
                            {
                                editor.SelectWidget(key);
                                using Bitmap pageImage = new(main.Width, main.Height); main.DrawToBitmap(pageImage, new Rectangle(Point.Empty, pageImage.Size));
                                pageImage.Save(Path.Combine(output, $"page-{key}-{dark}.png"));
                            }
                            Invoke(main, "SelectMainSection", false);
                            using Bitmap wallpaperImage = new(main.Width, main.Height); main.DrawToBitmap(wallpaperImage, new Rectangle(Point.Empty, wallpaperImage.Size));
                            wallpaperImage.Save(Path.Combine(output, $"wallpaper-{dark}.png"));
                            Invoke(main, "SelectMainSection", true);
                        }
                    }
                }
                main.ClientSize = new Size(Math.Max(main.MinimumSize.Width, (int)(960 * scale)), (int)(620 * scale)); main.PerformLayout();
                check(Field<Panel>(main, "widgetsPage").ClientSize.Width > 0 && editor.Width > navigation.Width && editor.Height > 0, $"V2 {language}/{scale} minimum window size keeps widget editor reachable");

        }
        finally
        {
            main.Dispose();
            foreach (Font font in scaledFonts) font.Dispose();
        }
    }

}
