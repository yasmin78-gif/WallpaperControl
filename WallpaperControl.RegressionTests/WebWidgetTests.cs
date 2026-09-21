extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using Microsoft.Win32;
using System.Reflection;
using System.Text.Json;
using System.Runtime.InteropServices;

internal static class WebWidgetTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    private static bool AcceptsMouseActivation(Form form) => (GetWindowLong(form.Handle, -20) & 0x08000000) == 0;
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, Members)!.GetValue(value)!;
    internal static void Run(Action<bool, string> check)
    {
        foreach (string valid in new[] { "https://example.com", "http://localhost:1234/a?q=1", " https://example.com/a#b " })
            check(App.WebWidgetSettings.IsValidUrl(valid), "Web accepts " + valid);
        foreach (string invalid in new[] { "", " ", "example.com", "file:///C:/a", "javascript:alert(1)", "https://", "ftp://example.com", "https://name:secret@example.com", "https://example.com/a b", "https://example.com/%wrong" })
            check(!App.WebWidgetSettings.IsValidUrl(invalid), "Web rejects " + invalid);
        App.WebWidgetSettings model = App.WebWidgetSettings.Parse("{\"Zoom\":999,\"Width\":1,\"Height\":-3,\"Collapsed\":true}");
        check(model.Zoom == 200 && model.Width == 320 && model.Height == 200 && model.Collapsed, "Web normalizes stored bounds");
        model.Zoom = -1; model.Normalize(); check(model.Zoom == 50, "Web minimum zoom");
        App.WebWidgetSettings clone = model.Clone(); clone.Width = 700;
        check(model.Width == 320, "Web clone independent");
        foreach (float scale in new[] { 1f, 1.5f, 2f })
        {
            Rectangle area = new(-1920, 0, 1920, 1080);
            Rectangle bounds = App.WebWidgetSettings.ClampBounds(new Point(20000, -500), new Size(640, 420), area, scale, false);
            check(area.Contains(bounds) && bounds.Width == (int)(640 * scale), "Web monitor clamp " + scale);
            Rectangle compact = App.WebWidgetSettings.ClampBounds(bounds.Location, new Size(640, 420), area, scale, true);
            check(compact.Height == (int)(36 * scale) && compact.Location == bounds.Location, "Web collapsed geometry " + scale);
            check(area.Contains(App.WebWidgetSettings.ClampBounds(Point.Empty, new Size(20000, 20000), area, scale, false)), "Web large bounds fit " + scale);
            Size size = new(640, 420); int edge = (int)(5 * scale);
            Point[] points = [new(0, 210), new(639, 210), new(320, 0), new(320, 419), new(0, 0), new(639, 0), new(0, 419), new(639, 419)];
            int[] hits = [10, 11, 12, 15, 13, 14, 16, 17];
            for (int i = 0; i < points.Length; i++)
            {
                check(App.WebWidgetForm.ResizeHit(points[i], size, edge, false, false) == hits[i], $"Web edge {i}/{scale}");
                check(App.WebWidgetForm.ResizeHit(points[i], size, edge, true, false) == 1, $"Web lock blocks edge {i}/{scale}");
            }
            check(App.WebWidgetForm.ResizeHit(Point.Empty, size, edge, false, true) == 1, "Web collapsed resize disabled " + scale);
        }
        using Task work = new(() => { Ui(check); Layouts(check); });
        Thread thread = new(() => work.RunSynchronously(TaskScheduler.Default));
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); work.GetAwaiter().GetResult();
    }
    private static void Layouts(Action<bool, string> check)
    {
        string? render = Environment.GetEnvironmentVariable("WALLPAPER_WEB_RENDER_DIR");
        if (render != null) Directory.CreateDirectory(render);
        foreach (string language in new[] { "de", "en", "fr", "es", "ja" })
        foreach (float scale in new[] { 1f, 1.5f, 2f })
        foreach (bool dark in new[] { false, true })
        {
            List<Font> fonts = new();
            Form host = new();
            try
            {
                using App.WidgetSettingsEditor editor = new(new App.WidgetSettings(), dark, language);
                host.ClientSize = new Size(1050, 700); host.Opacity = 0; host.ShowInTaskbar = false;
                host.Controls.Add(editor); editor.Dock = DockStyle.Fill; host.Show(); editor.SelectWidget("WebTitle");
                ScaleFonts(editor, scale, fonts); editor.Scale(new SizeF(scale, scale));
                editor.Dock = DockStyle.None; editor.Size = new Size((int)(1050 * scale), (int)(700 * scale)); editor.PerformLayout();
                TabPage page = Field<TabControl>(editor, "pages").SelectedTab!;
                page.PerformLayout(); FlowLayoutPanel layout = page.Controls.OfType<FlowLayoutPanel>().Single();
                string context = $"Web layout {language}/{scale}/{dark}";
                check(!page.HorizontalScroll.Visible && layout.Right <= page.ClientSize.Width, context + " no horizontal overflow");
                Control[] controls = layout.Controls.Cast<Control>().ToArray();
                check(controls.Zip(controls.Skip(1)).All(pair => pair.First.Bottom <= pair.Second.Top), context + " fields do not overlap");
                check(controls.OfType<CheckBox>().All(c => c.GetPreferredSize(Size.Empty).Width <= c.Width), context + " complete checkbox captions");
                check(Field<TextBox>(editor, "webUrl").Width >= 400 * scale, context + " URL field scales");
                if (render != null)
                {
                    using Bitmap bitmap = new(editor.Width, editor.Height);
                    editor.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(Path.Combine(render, $"web-{language}-{scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}-{dark}.png"));
                }
            }
            finally { host.Dispose(); foreach (Font font in fonts) font.Dispose(); }
        }
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
    private static void Ui(Action<bool, string> check)
    {
        App.Localization.RefreshAvailableLanguages();
        string path = @"Software\WallpaperControl.WebTests-" + Guid.NewGuid().ToString("N");
        try
        {
            App.WidgetSettings initial = new() { Web = new() { Enabled = true, Url = "https://example.com", DisplayName = "Example", ReloadOnStartup = false, Width = 500, Height = 320, Collapsed = true, Zoom = 120, Locked = true, AllowInteraction = false } };
            initial.Save(path);
            check(JsonSerializer.Serialize(initial.Web) == JsonSerializer.Serialize(App.WidgetSettings.Load(path).Web), "Web configuration registry roundtrip");
            App.NotesStore store = new(Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"), "notes.json"));
            using App.WidgetManager manager = new(() => { }, registryPath: path, notesStore: store);
            manager.Start();
            Application.DoEvents(); // Shown attaches the native desktop window asynchronously.
            App.WebWidgetForm form = Field<App.WebWidgetForm>(manager, "webWidget");
            var active = Field<HashSet<Form>>(manager, "desktopWidgets");
            check(active.Contains(form) && !form.TopMost && form.FormBorderStyle == FormBorderStyle.None, "Web registered borderless desktop window");
            check(AcceptsMouseActivation(form), "Web cold startup permits native mouse activation for keyboard input");
            check(SendMessage(form.Handle, 0x21, form.Handle, new IntPtr(0x02010001)).ToInt64() == 1,
                "Web native WM_MOUSEACTIVATE accepts activation without consuming the browser click");
            check(form.Height == 36 && form.Configuration.Width == 500 && form.Configuration.Height == 320, "Web restores collapsed startup with expanded dimensions");
            check(!Field<bool>(form, "ready"), "Web reload-on-start disabled defers browser initialization");
            form.SetCollapsed(false);
            check(form.Height == 320 && form.Width == 500 && form.Configuration.Locked, "Web locked restore preserves dimensions");
            check(AcceptsMouseActivation(form), "Web geometry lock and restore do not suppress keyboard activation");
            form.SetCollapsed(true);
            check(App.WidgetSettings.Load(path).Web.Collapsed, "Web settled collapse persisted");
            manager.SetActivitySuspended(true); manager.SetActivitySuspended(false);
            check(ReferenceEquals(form, Field<App.WebWidgetForm>(manager, "webWidget")) && form.Configuration.Collapsed, "Web fullscreen preserves instance and collapsed state");
            check(AcceptsMouseActivation(form), "Web fullscreen repair preserves mouse activation");
            App.WebWidgetSettings interactive = form.Configuration;
            interactive.AllowInteraction = true;
            form.ApplyConfiguration(interactive, "en", false);
            Control browserControl = Field<Control>(form, "browser");
            check(form.Configuration.Locked && browserControl.Enabled && browserControl.TabStop,
                "Web locked widget independently enables keyboard and pointer interaction");
            interactive.AllowInteraction = false; form.ApplyConfiguration(interactive, "en", false);
            check(!browserControl.Enabled && !browserControl.TabStop && form.Enabled,
                "Web interaction off disables browser only, retaining header input");
            interactive.AllowInteraction = true; form.ApplyConfiguration(interactive, "en", false);
            check(browserControl.Enabled && browserControl.TabStop && AcceptsMouseActivation(form),
                "Web re-enabling interaction immediately restores keyboard eligibility");
            form.ApplyConfiguration(initial.Web, "en", true);
            using (Form passive = new())
            {
                _ = passive.Handle;
                App.DesktopWidgetNative.AttachToDesktop(passive, new Point(100, 100));
                check(!AcceptsMouseActivation(passive), "Existing desktop widgets retain default no-activation policy");
            }
            typeof(App.WidgetManager).GetMethod("RestoreDesktopWidgetBand", Members)!.Invoke(manager, null);
            check(active.Contains(form) && !form.IsDisposed, "Web shared desktop repair participates");
            using App.WidgetSettingsEditor editor = new(manager.Settings);
            App.WidgetEditSession session = new(manager, editor.ReadWidgetSettings, editor.LoadSettings);
            session.Begin();
            App.WidgetSettings preview = manager.Settings;
            preview.Web.Zoom = 180; preview.Web.DisplayName = "Preview"; preview.Web.Locked = false;
            session.Preview(preview); form.SetCollapsed(false);
            check(session.IsDirty && form.Configuration.Zoom == 180, "Web preview and geometry make transaction dirty");
            session.Discard();
            check(form.Configuration.Collapsed && form.Configuration.Zoom == 120 && form.Configuration.DisplayName == "Example" && form.Configuration.Locked, "Web discard restores configuration and geometry");
            editor.ResetDefaults();
            check(!editor.ReadWidgetSettings(false).Web.Enabled && editor.ReadWidgetSettings(false).Web.Zoom == 100, "Web reset defaults");
            session.Discard();
            Field<TextBox>(editor, "webUrl").Text = new Uri(initial.Web.Url).Host;
            check(!session.Save() && session.IsDirty, "Web invalid enabled URL blocks save");
            manager.SetActivitySuspended(true);
            Field<TextBox>(editor, "webUrl").Text = initial.Web.Url.Replace(".com", ".org", StringComparison.Ordinal);
            check(session.Save() && App.WidgetSettings.Load(path).Web.Url == "https://example.org", "Web save stores valid URL");
            App.WidgetSettings disabled = manager.Settings; disabled.Web.Enabled = false;
            manager.Preview(disabled);
            check(form.IsDisposed && !active.Contains(form), "Web disable disposes and unregisters");
            manager.CancelPreview(initial);
            Application.DoEvents();
            check(active.Count == 1 && !ReferenceEquals(form, Field<App.WebWidgetForm>(manager, "webWidget")), "Web re-enable registers fresh instance");
            check(AcceptsMouseActivation(Field<App.WebWidgetForm>(manager, "webWidget")),
                "Web recreated from persisted configuration still permits activation after Shown");
            manager.Preview(disabled);
            App.WidgetEditSession? urlSession = null;
            using App.WidgetSettingsEditor urlEditor = new(manager.Settings, preview: value => urlSession?.Preview(value));
            urlSession = new(manager, urlEditor.ReadWidgetSettings, urlEditor.LoadSettings); urlSession.Begin();
            string originalUrl = manager.Settings.Web.Url;
            Field<TextBox>(urlEditor, "webUrl").Text = initial.Web.Url;
            Field<TextBox>(urlEditor, "webName").Text = initial.Web.DisplayName;
            check(manager.Settings.Web.Url == originalUrl && urlSession.IsDirty, "Web typing and unrelated previews do not apply URL");
            typeof(App.WidgetSettingsEditor).GetMethod("ApplyWebUrl", Members)!.Invoke(urlEditor, null);
            check(manager.Settings.Web.Url == initial.Web.Url, "Web explicit apply previews URL");
            urlSession.Discard();
            check(manager.Settings.Web.Url == originalUrl && !urlSession.IsDirty, "Web discard restores URL draft and applied configuration");
            foreach (string language in new[] { "de", "en", "fr", "es", "ja" })
            {
                editor.ApplyPresentation(false, language); editor.SelectWidget("WebTitle");
                check(editor.SelectedKey == "WebTitle" && Field<TextBox>(editor, "webName").PlaceholderText == App.Localization.Get("WebTitle", language), "Web translated page " + language);
                foreach (string key in new[] { "WebTitle", "WebEnabled", "WebName", "WebUrl", "WebApplyUrl", "WebZoom", "WebInteraction", "WebStartup", "WebLocked", "WebCollapse", "WebRestore", "WebReload", "WebLoading", "WebReady", "WebInvalidUrl", "WebRuntimeMissing", "WebInitializationFailed", "WebNavigationFailed", "WebProcessFailed" })
                    check(App.Localization.Get(key, language) != key && App.Localization.Get(key, language).Length > 0, "Web localization " + language + "/" + key);
            }
            manager.Dispose(); check(active.Count == 0, "Web manager disposal clears active collection");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, false); }
    }
}

