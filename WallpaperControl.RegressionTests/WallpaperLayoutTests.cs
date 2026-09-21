extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using Microsoft.Win32;

internal static class WallpaperLayoutTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, Members)!.GetValue(value)!;
    private static object? Invoke(object value, string name, params object[] args) => value.GetType().GetMethod(name, Members)!.Invoke(value, args);
    private static void Language(string language) => typeof(App.Localization).GetMethod("ApplyLanguage", Members)!.Invoke(null, new object[] { language, false });
    private static readonly string[] Languages = { "de", "en", "fr", "es", "ja" };
    private static readonly float[] Scales = { 1f, 1.5f, 2f };
    private static readonly string[] Actions = { "pauseButton", "pinButton", "explorerButton", "rejectButton", "undoRejectButton", "historyButton", "statisticsButton" };
    private static readonly string[] Controls = { "folderTextBox", "folderButton", "wallpaperCountLabel", "intervalComboBox",
        "windowsIntervalLabel", "shuffleCheckBox", "positionComboBox", "transitionComboBox", "transitionDirectionComboBox",
        "transitionDurationComboBox", "nextWallpaperButton", "pauseButton", "pinButton", "explorerButton", "rejectButton",
        "undoRejectButton", "historyButton", "statisticsButton", "currentWallpaperLabel" };

    internal static void Run(Action<bool, string> check)
    {
        using Task work = new(() =>
        {
            App.Localization.RefreshAvailableLanguages();
            string originalLanguage = App.Localization.CurrentLanguage;
            try
            {
                foreach (string language in Languages)
                foreach (float scale in Scales)
                {
                    Language(language);
                    check(App.Localization.IsLanguageAvailable(language), $"Wallpaper language resources available: {language}");
                    CheckLayout(check, language, scale);
                }
            }
            finally { Language(originalLanguage); }
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

    private static void CheckLayout(Action<bool, string> check, string language, float scale)
    {
        string path = @"Software\WallpaperControl.WallpaperLayoutTests-" + Guid.NewGuid().ToString("N");
        List<Font> fonts = new();
        using App.WidgetManager manager = new(() => { }, registryPath: path,
            notesStore: new App.NotesStore(Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"), "notes.json")));
        using App.MainForm main = new(manager);
        try
        {
            main.Opacity = 0; main.Show();
            ScaleFonts(main, scale, fonts); main.Scale(new SizeF(scale, scale));
            var page = Field<Panel>(main, "wallpaperPage");
            var content = Field<Panel>(main, "wallpaperContent");
            var slideshow = Field<Panel>(main, "slideshowCard");
            var display = Field<Panel>(main, "displayCard");
            var current = Field<Panel>(main, "currentWallpaperCard");
            var next = Field<Button>(main, "nextWallpaperButton");
            var filename = Field<Label>(main, "currentWallpaperLabel");
            string longName = new string('W', 180) + " 日本語 – très-long-fichier.jpg";
            filename.Text = longName;
            string fullPath = @"C:\wallpapers\" + longName;
            Field<ToolTip>(main, "toolTip").SetToolTip(filename, fullPath);
            check(main.AllowDrop && Controls.All(name => content.Contains(Field<Control>(main, name))), $"Wallpaper {language}/{scale}: all existing controls and drag/drop retained");
            check(Field<Label>(main, "slideshowHeading").Text == App.Localization.Get("MainSlideshowHeading", language)
                && Field<Label>(main, "displayHeading").Text == App.Localization.Get("MainDisplayHeading", language)
                && App.Localization.Get("MainDisplayHeading", language) != "MainDisplayHeading", $"Wallpaper {language}/{scale}: localized group headings");

            foreach (bool dark in new[] { false, true })
            {
                typeof(App.MainForm).GetField("themeMode", Members)!.SetValue(main, dark ? "dark" : "light");
                Invoke(main, "ApplyWindowsTheme");
                foreach (int logicalWidth in new[] { 1260, 960, 1900, 1260 })
                {
                    page.AutoScrollPosition = Point.Empty;
                    if (logicalWidth == 960) main.Size = main.MinimumSize;
                    else main.ClientSize = new Size((int)(logicalWidth * scale), (int)(760 * scale));
                    Invoke(main, "ArrangeWallpaperPage", scale);
                    bool stacked = logicalWidth == 960;
                    string context = $"Wallpaper {language}/{scale}/{dark}/{logicalWidth}";
                    check(stacked ? display.Left == slideshow.Left && display.Top > slideshow.Bottom
                        : display.Top == slideshow.Top && display.Left > slideshow.Right && display.Height == slideshow.Height,
                        context + ": responsive card arrangement");
                    check(content.Width <= 881 * scale && content.Left >= 0 && content.Right <= page.ClientSize.Width && !page.HorizontalScroll.Visible,
                        context + ": centered bounded workspace without horizontal scrolling");
                    check(next.Top > display.Bottom && next.Width == content.Width && current.Top > next.Bottom,
                        context + ": full-width primary action precedes current wallpaper area");
                    check(new[] { slideshow, display, current }.All(card => card.Controls.Cast<Control>().All(c => card.ClientRectangle.Contains(c.Bounds))),
                        context + ": every control fits inside its card");
                    check(new[] { slideshow, display, current }.All(card => card.Controls.Cast<Control>().All(a => card.Controls.Cast<Control>().All(b => a == b || !a.Bounds.IntersectsWith(b.Bounds)))),
                        context + ": card controls do not overlap");
                    check(filename.AutoEllipsis && filename.Text == longName && Field<ToolTip>(main, "toolTip").GetToolTip(filename) == fullPath,
                        context + ": long filename and full-path tooltip survive resize");
                    check(slideshow.BackColor == App.AppTheme.PanelBackground(dark) && display.BackColor == slideshow.BackColor && current.BackColor == slideshow.BackColor
                        && next.BackColor == Color.FromArgb(29, 105, 184), context + ": existing themed surfaces and primary accent");
                    check(Actions
                        .All(name => Field<Button>(main, name).Parent == current), context + ": all seven actions grouped together");
                    if (logicalWidth == 960)
                    {
                        var lastButton = Field<Button>(main, "statisticsButton");
                        page.ScrollControlIntoView(lastButton);
                        Rectangle visible = page.RectangleToScreen(page.ClientRectangle);
                        check(visible.Contains(lastButton.RectangleToScreen(lastButton.ClientRectangle)), context + ": last action reachable by scrolling");
                        page.AutoScrollPosition = Point.Empty;
                    }
                    string? output = Environment.GetEnvironmentVariable("WALLPAPER_TEST_OUTPUT");
                    if (output != null && logicalWidth is 1260 or 960)
                    {
                        Directory.CreateDirectory(output);
                        using Bitmap image = new(main.Width, main.Height);
                        main.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                        image.Save(Path.Combine(output, $"wallpaper-{language}-{scale.ToString(CultureInfo.InvariantCulture)}-{logicalWidth}-{dark}.png"));
                    }
                }
            }
            Invoke(main, "SetWarningLayout", true);
            Field<Label>(main, "statusLabel").Visible = true;
            Field<Button>(main, "activateButton").Visible = true;
            Invoke(main, "ArrangeWallpaperPage", scale);
            check(Field<Button>(main, "activateButton").Bottom < slideshow.Top && Field<Label>(main, "statusLabel").Bottom < Field<Button>(main, "activateButton").Top,
                $"Wallpaper {language}/{scale}: warning and activation reserve nonoverlapping space");
            Invoke(main, "SetNormalLayout");
            object scheduler = Field<object>(main, "customSlideshowPreciseTimer");
            var timer = Field<System.Windows.Forms.Timer>(main, "wallpaperRefreshTimer");
            bool paused = Field<bool>(main, "slideshowPaused");
            object widgets = Field<object>(main, "widgetEditor");
            int interval = timer.Interval;
            for (int i = 0; i < 3; i++) { Invoke(main, "SelectMainSection", true); Invoke(main, "SelectMainSection", false); }
            check(ReferenceEquals(scheduler, Field<object>(main, "customSlideshowPreciseTimer")) && timer.Interval == interval
                && !timer.Enabled && Field<bool>(main, "slideshowPaused") == paused && filename.Text == longName
                && ReferenceEquals(widgets, Field<object>(main, "widgetEditor")), $"Wallpaper {language}/{scale}: navigation preserves wallpaper state and existing Widgets editor");
        }
        finally
        {
            main.Dispose();
            foreach (Font font in fonts) font.Dispose();
            Registry.CurrentUser.DeleteSubKeyTree(path, false);
        }
    }
}
