extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using System.Drawing;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Win32;

internal static class WallpaperInfoTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, Members)!.GetValue(value)!;
    private static object? Invoke(object value, string name, params object[] args) => value.GetType().GetMethod(name, Members)!.Invoke(value, args);

    internal static void Run(Action<bool, string> check)
    {
        Names(check);
        Statistics(check);
        Persistence(check);
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try { Rendering(check); Settings(check); Manager(check); }
            // Marshal arbitrary assertion failures back to the runner after the STA thread joins.
#pragma warning disable CA1031
            catch (Exception ex) { failure = ex; }
#pragma warning restore CA1031
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Names(Action<bool, string> check)
    {
        foreach (var (input, suffix, extension, expected) in new[]
        {
            ("Serpent_Temple_3440x1440.png", "_3440x1440", false, "Serpent_Temple"),
            ("Serpent_Temple_3440x1440.png", "_3440x1440", true, "Serpent_Temple.png"),
            ("Dragon_Queen_3440x1440.jpg", "_3440x1440", false, "Dragon_Queen"),
            ("Vampire_Castle.png", "_3440x1440", false, "Vampire_Castle"),
            ("3440x1440_Temple.png", "_3440x1440", false, "3440x1440_Temple"),
            ("A_3440x1440_B.png", "_3440x1440", false, "A_3440x1440_B"),
            ("A_3440X1440.JPEG", "_3440x1440", true, "A.JPEG"),
            ("A_3440x1440.webp", "", false, "A_3440x1440"),
            ("A_3440x1440.tiff", "_3440x1440", true, "A.tiff"),
            ("A_3440x14400.png", "_3440x1440", false, "A_3440x14400"),
            ("A_3440x1440_3440x1440.png", "_3440x1440", false, "A_3440x1440"),
            ("A internal suffix.png", " internal suffix", false, "A"),
            (@"C:\unchanged\A_3440x1440.bmp", "_3440x1440", true, "A.bmp"),
            (new string('x', 10000) + ".png", "_3440x1440", false, new string('x', 10000)),
            ("", "_3440x1440", false, ""),
            ("A_1920x1080.png", "_3440x1440, _1920x1080, _4K", false, "A"),
            ("A_4k.JPG", "_3440x1440, _1920x1080, _4K", true, "A.JPG"),
            ("A_3440x1440.webp", "_3440x1440, _1920x1080", true, "A.webp"),
            ("A_4K_middle.png", "_4K, _1920x1080", false, "A_4K_middle"),
            ("A_4K.png", ", , _4K,, ", false, "A"),
            ("A.png", ", ,", false, "A"),
            ("A_4K.png", "  _4K  , _1080p ", false, "A"),
            ("A_4K.png", "K, _4K", false, "A"),
            ("A_4K.png", "_4K, K", false, "A"),
            ("A_4K_1080p.png", "_4K, _1080p", false, "A_4K"),
            ("A_internal suffix.png", "_other, _internal suffix", false, "A"),
            ("A_4K.png", "_4K, _4K", false, "A")
        })
            check(App.WallpaperInfoSnapshot.DisplayName(input, suffix, extension) == expected,
                $"Wallpaper info display-name case {input[..Math.Min(input.Length, 48)]}/{extension}");
        check(App.WallpaperInfoSnapshot.DisplayName(null, null, true).Length == 0, "Wallpaper info missing path is neutral");
    }

    // The analyzer does not model Changed callbacks updating these captured assertion values.
#pragma warning disable CA1508
    private static void Statistics(Action<bool, string> check)
    {
        App.WallpaperStatistics stats = new(saveOverride: () => { });
        const string path = @"C:\missing\A_3440x1440.png";
        int events = 0;
        App.WallpaperInfoSnapshot received = default;
        stats.Changed += () => { events++; received = App.WallpaperInfoSnapshot.Create(path, 305, stats.ViewCounts, true); };
        stats.RecordView(path);
        check(events == 1 && received.Views == 1 && received.Summary.TotalViews == 1,
            "Wallpaper info change event observes committed count and matching path together");
        stats.RecordView(path);
        check(events == 1, "Wallpaper info duplicate views do not raise a statistics change");
        stats.RecordView("B.jpg");
        stats.RecordView(path);
        check(received.Views == 2 && received.WallpaperCount == 305 && received.Summary.TotalViews == 3
            && received.Summary.AverageViews == 1.5 && received.Summary.RecordViews == 2,
            "Wallpaper info six values use current collection and all stored tracked-wallpaper statistics");
        check(App.WallpaperInfoSnapshot.Create("untracked.jpg", 0, stats.ViewCounts, true).Views == 0,
            "Wallpaper info current untracked or removed file needs no filesystem access");
        var compact = App.WallpaperInfoSnapshot.Create(path, 305, stats.ViewCounts, false);
        check(compact.Views == 2 && compact.Summary == default, "Wallpaper info compact mode omits aggregate calculation");
        stats.Remove("B.jpg");
        check(received.Summary.TotalViews == 2 && received.Summary.AverageViews == 2,
            "Wallpaper info removal refreshes the shared summary");
        stats.Reset(path);
        check(received.Views == 0 && received.Summary == default, "Wallpaper info reset immediately clears all displayed counters");
        stats.RecordView(path);
        check(received.Views == 0, "Wallpaper info reset retains zero until a genuine change");
        stats.Load(new App.PersistentStatisticsData());
        check(events == 6 && received.Summary == default, "Wallpaper info statistics reload raises one neutral snapshot event");
        check(App.WallpaperInfoSnapshot.Create(null, -1, stats.ViewCounts, true).WallpaperCount == 0,
            "Wallpaper info empty collection and absent wallpaper remain neutral");
    }

#pragma warning restore CA1508

    private static void Persistence(Action<bool, string> check)
    {
        string keyPath = @"Software\WallpaperControl.RegressionTests-" + Guid.NewGuid().ToString("N");
        try
        {
            App.WidgetSettings value = App.WidgetSettings.Load(keyPath);
            check(!value.WallpaperInfoEnabled && !value.WallpaperInfoLocked && !value.WallpaperInfoShowAdvanced
                && !value.WallpaperInfoShowExtension && value.WallpaperInfoHiddenSuffix == "_3440x1440"
                && value.WallpaperInfoStyle == App.SystemWidgetStyle.Minimal, "Wallpaper info missing registry uses requested defaults");
            value.WallpaperInfoEnabled = value.WallpaperInfoLocked = value.WallpaperInfoShowAdvanced = value.WallpaperInfoShowExtension = true;
            value.WallpaperInfoHiddenSuffix = "  _a b  ";
            value.WallpaperInfoStyle = App.SystemWidgetStyle.Clean;
            value.WallpaperInfoLocation = new Point(-400, 60);
            value.ClockSize = 180;
            value.Save(keyPath);
            App.WidgetSettings loaded = App.WidgetSettings.Load(keyPath);
            check(loaded.WallpaperInfoEnabled && loaded.WallpaperInfoLocked && loaded.WallpaperInfoShowAdvanced && loaded.WallpaperInfoShowExtension
                && loaded.WallpaperInfoHiddenSuffix == "_a b" && loaded.WallpaperInfoStyle == App.SystemWidgetStyle.Clean
                && loaded.WallpaperInfoLocation == value.WallpaperInfoLocation && loaded.ClockSize == 180,
                "Wallpaper info preferences and negative position persist, suffix trims only outside whitespace");
            loaded.WallpaperInfoHiddenSuffix = "";
            loaded.Save(keyPath);
            check(App.WidgetSettings.Load(keyPath).WallpaperInfoHiddenSuffix.Length == 0, "Wallpaper info empty suffix persists without reverting to default");
            loaded.WallpaperInfoHiddenSuffix = "  _3440x1440, _1920x1080, _4K  ";
            loaded.Save(keyPath);
            loaded = App.WidgetSettings.Load(keyPath);
            check(loaded.WallpaperInfoHiddenSuffix == "_3440x1440, _1920x1080, _4K"
                && App.WallpaperInfoSnapshot.DisplayName("A_1920x1080.png", loaded.WallpaperInfoHiddenSuffix, true) == "A.png",
                "Wallpaper info comma-separated suffixes persist and apply after reload");
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(keyPath, true)!)
            {
                key.SetValue("WallpaperInfoWidgetEnabled", 2);
                key.SetValue("WallpaperInfoWidgetShowAdvanced", "bad");
                key.SetValue("WallpaperInfoWidgetShowExtension", new byte[] { 1 });
                key.SetValue("WallpaperInfoWidgetStyle", 999);
                key.SetValue("WallpaperInfoWidgetHiddenSuffix", new byte[] { 2 });
                key.SetValue("WallpaperInfoWidgetX", "broken");
                key.SetValue("WallpaperInfoWidgetY", long.MaxValue);
            }
            loaded = App.WidgetSettings.Load(keyPath);
            check(!loaded.WallpaperInfoEnabled && !loaded.WallpaperInfoShowAdvanced && !loaded.WallpaperInfoShowExtension
                && loaded.WallpaperInfoStyle == App.SystemWidgetStyle.Minimal && loaded.WallpaperInfoHiddenSuffix == "_3440x1440"
                && loaded.WallpaperInfoLocation == new Point(220, 40) && loaded.ClockSize == 180,
                "Wallpaper info invalid values fall back independently and leave other settings loadable");
            loaded.Save(keyPath);
            check(App.WidgetSettings.Load(keyPath).ClockSize == 180, "Wallpaper info malformed values do not poison subsequent persistence");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(keyPath, false); }
    }

    private static App.WidgetSettingsEditor NewSettings(App.WidgetSettings value, Action<App.WidgetSettings>? preview = null) =>
        new(value, preview: preview);

    private static void Settings(Action<bool, string> check)
    {
        App.WidgetSettings original = new();
        App.WidgetSettings? preview = null;
        using (App.WidgetSettingsEditor form = NewSettings(original, value => preview = value))
        {
            Field<CheckBox>(form, "wallpaperInfoEnabled").Checked = true;
            Field<CheckBox>(form, "wallpaperInfoAdvanced").Checked = true;
            // Deliberately unlocalized input fixture verifies preservation of internal whitespace.
#pragma warning disable CA1303
            Field<TextBox>(form, "wallpaperInfoSuffix").Text = " _a b ";
#pragma warning restore CA1303
            check(preview is { WallpaperInfoEnabled: true, WallpaperInfoShowAdvanced: true, WallpaperInfoHiddenSuffix: " _a b " }
                && !original.WallpaperInfoEnabled, "Wallpaper info edits publish independent live previews");
            var acceptedSettings = form.ReadWidgetSettings(true);
            check(acceptedSettings.WallpaperInfoHiddenSuffix == "_a b" && acceptedSettings.WallpaperInfoEnabled,
                "Wallpaper info Settings Save accepts and normalizes the draft");
        }
        using (App.WidgetSettingsEditor form = NewSettings(original))
        {
            Field<CheckBox>(form, "wallpaperInfoEnabled").Checked = true;
            form.Dispose();
            check(!original.WallpaperInfoEnabled && original.WallpaperInfoHiddenSuffix == "_3440x1440",
                "Wallpaper info Settings Cancel preserves initial preferences");
        }
        using (App.WidgetSettingsEditor form = NewSettings(new App.WidgetSettings { WallpaperInfoEnabled = true, WallpaperInfoShowAdvanced = true,
            WallpaperInfoShowExtension = true, WallpaperInfoLocked = true, WallpaperInfoStyle = App.SystemWidgetStyle.Glow, WallpaperInfoHiddenSuffix = "custom" }))
        {
            form.ResetDefaults();
            var draft = (App.WidgetSettings)Invoke(form, "ReadWidgetSettings", false)!;
            check(!draft.WallpaperInfoEnabled && !draft.WallpaperInfoLocked && !draft.WallpaperInfoShowAdvanced
                && !draft.WallpaperInfoShowExtension && draft.WallpaperInfoStyle == App.SystemWidgetStyle.Minimal
                && draft.WallpaperInfoHiddenSuffix == "_3440x1440", "Wallpaper info Reset restores every requested default");
        }
        foreach (string language in new[] { "de", "en", "fr", "es", "ja" })
        foreach (float scale in new[] { 1f, 1.5f, 2f })
        {
            using App.WidgetSettingsEditor form = NewSettings(original);
            form.ApplyPresentation(false, language);
            Control layout = Field<TextBox>(form, "wallpaperInfoSuffix").Parent!;
            Button navigation = Field<FlowLayoutPanel>(form, "navigation").Controls.OfType<Button>().Single(b => b.AccessibleDescription == "ⓘ");
            Size caption = TextRenderer.MeasureText(navigation.Text, navigation.Font);
            check(caption.Width + navigation.Padding.Horizontal <= navigation.ClientSize.Width,
                $"Wallpaper info {language}/{scale} full sidebar caption fits");
            string? output = Environment.GetEnvironmentVariable("WALLPAPER_INFO_TEST_OUTPUT");
            if (output != null && scale == 1)
            {
                Field<TabControl>(form, "pages").SelectedTab = (TabPage)layout.Parent!;

                form.Show();
                using Bitmap settingsImage = new(form.Width, form.Height);
                form.DrawToBitmap(settingsImage, new Rectangle(Point.Empty, settingsImage.Size));
                settingsImage.Save(Path.Combine(output, language + "-settings.png"));
                form.Hide();
            }
            layout.Scale(new SizeF(scale, scale));
            layout.PerformLayout();
            Control[] controls = layout.Controls.Cast<Control>().ToArray();
            check(controls.Zip(controls.Skip(1)).All(pair => pair.First.Bottom <= pair.Second.Top)
                && controls.All(c => c.Right <= layout.Width && c.Bottom <= layout.Height)
                && controls.Where(c => c.Tag is string).All(c => c.Text == App.Localization.Get((string)c.Tag!, language)),
                $"Wallpaper info Settings {language}/{scale} localized controls fit without overlap");
        }
    }

    private static void Rendering(Action<bool, string> check)
    {
        string? output = Environment.GetEnvironmentVariable("WALLPAPER_INFO_TEST_OUTPUT");
        if (output != null) Directory.CreateDirectory(output);
        foreach (string language in new[] { "de", "en", "fr", "es", "ja" })
        foreach (int dpi in new[] { 96, 144, 192 })
        {
            App.WidgetSettings settings = new() { ClockLanguageCode = language };
            using App.WallpaperInfoWidgetForm form = new(settings, _ => { });
            form.SetData(new("Serpent_Temple_3440x1440.png", 7, 305, new(2162, 7.28, 19)));
            using Bitmap compact = form.RenderBitmap(dpi);
            check(compact.Height == 34 * dpi / 96 && compact.Width >= 420 * dpi / 96 && compact.Width <= 900 * dpi / 96,
                $"Wallpaper info {language}/{dpi} compact bitmap uses bounded DPI-aware geometry");
            settings.WallpaperInfoShowAdvanced = true;
            form.Apply(settings);
            using Bitmap advanced = form.RenderBitmap(dpi);
            check(advanced.Height == 58 * dpi / 96 && advanced.Width <= 900 * dpi / 96 && form.Controls.Count == 0,
                $"Wallpaper info {language}/{dpi} extended geometry has no child controls");
            form.SetData(new(new string('W', 10000) + "_3440x1440.png", 7, 305, new(2162, 7.28, 19)));
            using Bitmap longName = form.RenderBitmap(dpi);
            form.SetData(new(new string('W', 20000) + "_3440x1440.png", 7, 305, new(2162, 7.28, 19)));
            using Bitmap longerName = form.RenderBitmap(dpi);
            check(longName.Size == longerName.Size && Same(longName, longerName),
                $"Wallpaper info {language}/{dpi} long names ellipsize with stable complete statistics");
            if (output != null && dpi == 96) advanced.Save(Path.Combine(output, language + "-advanced.png"));
            if (output != null && dpi == 192) longName.Save(Path.Combine(output, language + "-long-200.png"));
        }
        using App.WallpaperInfoWidgetForm themed = new(new(), _ => { });
        themed.SetData(new("Serpent_Temple.png", 7, 305, new(2162, 7.28, 19)));
        foreach (App.SystemWidgetStyle style in Enum.GetValues<App.SystemWidgetStyle>())
        {
            themed.Apply(new() { WallpaperInfoStyle = style, WallpaperInfoShowAdvanced = true });
            using Bitmap image = themed.RenderBitmap();
            check(image.GetPixel(10, 10).A > 0, $"Wallpaper info {style} free surface remains draggable rather than transparent");
            if (output != null) image.Save(Path.Combine(output, style + ".png"));
        }
        check(!typeof(App.WallpaperInfoWidgetForm).GetFields(Members).Any(f => f.FieldType.Name.Contains("Timer", StringComparison.Ordinal)),
            "Wallpaper info owns no polling or refresh timer");
    }

    private static bool Same(Bitmap left, Bitmap right)
    {
        if (left.Size != right.Size) return false;
        for (int y = 0; y < left.Height; y++)
        for (int x = 0; x < left.Width; x++)
            if (left.GetPixel(x, y) != right.GetPixel(x, y)) return false;
        return true;
    }

    private static void Manager(Action<bool, string> check)
    {
        string keyPath = @"Software\WallpaperControl.RegressionTests-" + Guid.NewGuid().ToString("N");
        try
        {
            int reads = 0;
            bool advancedRequested = false;
            using App.WidgetManager manager = new(() => { }, advanced =>
            {
                reads++;
                advancedRequested = advanced;
                return new("fixture.png", reads, 305, advanced ? new(2162, 7.28, 19) : default);
            }, keyPath, new App.NotesStore(Path.Combine(AppContext.BaseDirectory, "test-data", "info-notes-" + Guid.NewGuid().ToString("N"), "notes.json")));
            manager.RefreshWallpaperInfo();
            check(reads == 0, "Wallpaper info disabled widget does not read statistics");
            App.WidgetSettings original = manager.Settings;
            App.WidgetSettings draft = original.Clone();
            draft.WallpaperInfoEnabled = true;
            draft.WallpaperInfoLocked = true;
            manager.Preview(draft);
            var widget = Field<App.WallpaperInfoWidgetForm>(manager, "wallpaperInfoWidget");
            widget.Opacity = 0;
            var active = Field<HashSet<Form>>(manager, "desktopWidgets");
            check(active.Contains(widget) && reads == 1 && !advancedRequested, "Wallpaper info activation registers centrally and reads compact snapshot once");
            IntPtr handle = widget.Handle;
            draft.WallpaperInfoShowAdvanced = true;
            manager.Preview(draft);
            check(widget.Handle == handle && advancedRequested && widget.Height == 58 * widget.DeviceDpi / 96,
                "Wallpaper info advanced preview updates content and height without recreating window");
            Invoke(manager, "RestoreDesktopWidgetBand");
            check(active.Contains(widget) && widget.Handle == handle, "Wallpaper info shared Show Desktop repair retains the registered window");
            Point cursor = new(100, 100);
            object drag = Field<object>(widget, "dragHandler");
            drag.GetType().GetField("cursorPosition", Members)!.SetValue(drag, (Func<Point>)(() => cursor));
            Point position = widget.Location;
            Invoke(widget, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 100, 15, 0));
            cursor.Offset(30, 20);
            Invoke(widget, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 130, 35, 0));
            Invoke(widget, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 130, 35, 0));
            check(widget.Location == position, "Wallpaper info locked surface cannot drag");
            draft.WallpaperInfoLocked = false;
            manager.Preview(draft);
            Invoke(widget, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 100, 15, 0));
            cursor.Offset(30, 20);
            Invoke(widget, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 130, 35, 0));
            Invoke(widget, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 130, 35, 0));
            check(widget.Location == new Point(position.X + 30, position.Y + 20) && manager.Settings.WallpaperInfoLocation == widget.Location,
                "Wallpaper info free surface uses shared drag handler and preview position storage");
            draft.WallpaperInfoLocked = true;
            manager.Preview(draft);
            manager.SetActivitySuspended(true);
            int pausedReads = reads;
            manager.RefreshWallpaperInfo();
            check(reads == pausedReads && Field<bool>(widget, "suspended"), "Wallpaper info fullscreen suspension defers snapshot work");
            manager.SetActivitySuspended(false);
            check(reads == pausedReads + 1 && !Field<bool>(widget, "suspended") && active.Contains(widget),
                "Wallpaper info resume refreshes latest content through the existing manager");
            manager.CancelPreview(original);
            check(widget.IsDisposed && active.Count == 0 && !manager.Settings.WallpaperInfoEnabled,
                "Wallpaper info Cancel closes preview and deregisters it centrally");
            manager.Preview(draft);
            widget = Field<App.WallpaperInfoWidgetForm>(manager, "wallpaperInfoWidget");
            widget.Opacity = 0;
            var changed = manager.Settings;
            changed.WallpaperInfoLocation = new Point(350, 90);
            typeof(App.WidgetManager).GetField("settings", Members)!.SetValue(manager, changed);
            manager.CommitPreview(draft);
            App.WidgetSettings saved = App.WidgetSettings.Load(keyPath);
            check(saved.WallpaperInfoEnabled && saved.WallpaperInfoShowAdvanced && saved.WallpaperInfoLocation == new Point(350, 90),
                "Wallpaper info Commit saves preferences and retains position collected during preview");
            App.WidgetSettings committed = manager.Settings;
            draft.WallpaperInfoHiddenSuffix = "changed";
            draft.WallpaperInfoLocked = false;
            manager.Preview(draft);
            manager.CancelPreview(committed);
            check(manager.Settings.WallpaperInfoHiddenSuffix == committed.WallpaperInfoHiddenSuffix
                && manager.Settings.WallpaperInfoLocked && widget.Location == committed.WallpaperInfoLocation,
                "Wallpaper info Cancel restores active widget preferences, lock and position");
            draft.WallpaperInfoEnabled = false;
            manager.Preview(draft);
            check(widget.IsDisposed && active.Count == 0, "Wallpaper info deactivation closes and deregisters active widget");
            manager.Preview(committed);
            widget = Field<App.WallpaperInfoWidgetForm>(manager, "wallpaperInfoWidget");
            manager.Dispose();
            check(widget.IsDisposed && active.Count == 0, "Wallpaper info application shutdown releases widget and registration");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(keyPath, false); }
    }
}
