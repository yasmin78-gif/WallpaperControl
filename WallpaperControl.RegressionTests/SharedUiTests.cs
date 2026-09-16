extern alias WallpaperApp;

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

/// <summary>Checks shared UI helpers using hidden test windows, without starting the wallpaper application.</summary>
internal static class SharedUiTests
{
    private static readonly Assembly App = typeof(WallpaperApp::WallpaperControl.MainForm).Assembly;
    private const BindingFlags Methods = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    /// <summary>Runs WinForms checks on an STA thread and forwards failures to the console test runner.</summary>
    internal static void Run(Action<bool, string> check)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try { CheckSettings(check); CheckNextStyles(check); CheckImages(check); CheckDrawing(check); CheckDragging(check); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static Type Type(string name) => App.GetType("WallpaperControl." + name, throwOnError: true)!;
    private static object? Call(string type, string method, params object?[] args) => Type(type).GetMethod(method, Methods)!.Invoke(null, args);
    private static object Value(object instance, string property) => instance.GetType().GetProperty(property)!.GetValue(instance)!;
    private static void Set(object instance, string property, object value) => instance.GetType().GetProperty(property)!.SetValue(instance, value);
    private static T Control<T>(Form form, string field) => (T)form.GetType().GetField(field, Methods)!.GetValue(form)!;

    /// <summary>Checks every editable widget property, cloning, and the save-only empty-location fallback.</summary>
    private static void CheckSettings(Action<bool, string> check)
    {
        object settings = Activator.CreateInstance(Type("WidgetSettings"))!;
        Set(settings, "ClockLocation", new Point(-300, 75));
        Set(settings, "WeatherLocationName", "Original city");
        using Form form = (Form)Activator.CreateInstance(Type("SettingsForm"),
            false, "system", 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, "", true, false, true, true, true, 92, settings, null)!;
        object Read(bool save) => form.GetType().GetMethod("ReadWidgetSettings", Methods)!.Invoke(form, new object[] { save })!;
        var expected = new Dictionary<string, object>();
        foreach (var (field, property) in new[]
        {
            ("clockEnabledCheckBox", "ClockEnabled"), ("clockLockedCheckBox", "ClockLocked"),
            ("clockSecondsCheckBox", "ClockShowSeconds"), ("nextWidgetEnabledCheckBox", "NextEnabled"),
            ("nextWidgetLockedCheckBox", "NextLocked"), ("systemWidgetEnabledCheckBox", "SystemEnabled"),
            ("systemWidgetLockedCheckBox", "SystemLocked"), ("systemShowCpuCheckBox", "SystemShowCpu"),
            ("systemShowRamCheckBox", "SystemShowRam"), ("systemShowGpuCheckBox", "SystemShowGpu"),
            ("systemShowVramCheckBox", "SystemShowVram"), ("systemShowNetworkCheckBox", "SystemShowNetwork"),
            ("systemShowDrivesCheckBox", "SystemShowDrives"), ("weatherWidgetEnabledCheckBox", "WeatherEnabled"),
            ("weatherWidgetLockedCheckBox", "WeatherLocked"), ("weatherShowForecastCheckBox", "WeatherShowForecast"),
            ("calendarWidgetEnabledCheckBox", "CalendarEnabled"), ("calendarWidgetLockedCheckBox", "CalendarLocked"),
            ("calendarShowLocationCheckBox", "CalendarShowLocation")
        })
        {
            var box = Control<CheckBox>(form, field);
            box.Checked = !box.Checked;
            expected[property] = box.Checked;
        }
        Control<NumericUpDown>(form, "clockSizeNumeric").Value = 180;
        expected["ClockSize"] = 180;
        foreach (var (field, property, index, value) in new[]
        {
            ("systemWidgetRefreshComboBox", "SystemRefreshSeconds", 2, 5),
            ("weatherWidgetRefreshComboBox", "WeatherRefreshMinutes", 3, 120),
            ("calendarRefreshComboBox", "CalendarRefreshMinutes", 0, 15),
            ("calendarMaxEntriesComboBox", "CalendarMaxEntries", 1, 5)
        }) { Control<ComboBox>(form, field).SelectedIndex = index; expected[property] = value; }
        foreach (var (field, property) in new[] { ("clockStyleComboBox", "ClockStyle"),
            ("nextWidgetStyleComboBox", "NextStyle"),
            ("systemWidgetStyleComboBox", "SystemStyle"), ("weatherWidgetStyleComboBox", "WeatherStyle"),
            ("calendarWidgetStyleComboBox", "CalendarStyle") })
        {
            Control<ComboBox>(form, field).SelectedIndex = 0;
            expected[property] = Enum.ToObject(settings.GetType().GetProperty(property)!.PropertyType, 0);
        }
        Control<TextBox>(form, "weatherLocationTextBox").Text = "  Berlin  ";
        Control<TextBox>(form, "calendarIcsUrlTextBox").Text = "  https://example.invalid/private.ics  ";
        Control<TextBox>(form, "calendarHolidayIcsUrlTextBox").Text = "  https://example.invalid/holidays.ics  ";
        expected["WeatherLocationName"] = "Berlin";
        expected["CalendarIcsUrl"] = "https://example.invalid/private.ics";
        expected["CalendarHolidayIcsUrl"] = "https://example.invalid/holidays.ics";
        expected["ClockLanguageCode"] = form.GetType().GetField("previewLanguageCode", Methods)!.GetValue(form)!;
        object preview = Read(false), saved = Read(true);
        check(expected.All(pair => Equals(Value(preview, pair.Key), pair.Value) && Equals(Value(saved, pair.Key), pair.Value)),
            "Shared settings reader retains every edited widget option in preview and save");
        check(Equals(Value(preview, "ClockLocation"), new Point(-300, 75)) && !ReferenceEquals(preview, saved),
            "Widget settings snapshots preserve unedited positions and remain independent");
        Set(preview, "ClockLocation", Point.Empty);
        check(Equals(Value(Read(false), "ClockLocation"), new Point(-300, 75)) && Equals(Value(settings, "WeatherLocationName"), "Original city"),
            "Editing a preview snapshot does not mutate initial widget settings");
        Control<TextBox>(form, "weatherLocationTextBox").Text = "   ";
        check(Equals(Value(Read(false), "WeatherLocationName"), "") && Equals(Value(Read(true), "WeatherLocationName"), "Karlsruhe"),
            "Empty weather locations retain distinct preview and save behavior");
        check((string?)Control<TabControl>(form, "settingsTabControl").SelectedTab?.Tag == "SettingsNavGeneral",
            "Settings still open on General");
    }

    /// <summary>Checks next-widget style persistence, settings previews, reset behavior, and distinct rendering.</summary>
    private static void CheckNextStyles(Action<bool, string> check)
    {
        object Style(int value) => Enum.ToObject(Type("SystemWidgetStyle"), value);
        string registryPath = @"Software\WallpaperControl.RegressionTests-" + Guid.NewGuid().ToString("N");
        try
        {
            object settings = Call("WidgetSettings", "Load", registryPath)!;
            check(Convert.ToInt32(Value(settings, "NextStyle")) == 0, "Existing installations retain the original Minimal next-widget style");
            bool roundTrip = true;
            for (int style = 0; style < 3; style++)
            {
                Set(settings, "NextStyle", Style(style));
                settings.GetType().GetMethod("Save")!.Invoke(settings, new object[] { registryPath });
                object loaded = Call("WidgetSettings", "Load", registryPath)!;
                object clone = settings.GetType().GetMethod("Clone")!.Invoke(settings, null)!;
                roundTrip &= Convert.ToInt32(Value(loaded, "NextStyle")) == style && Convert.ToInt32(Value(clone, "NextStyle")) == style;
            }
            check(roundTrip, "All next-widget styles survive save, load, and cloning");
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(registryPath, writable: true)!)
            {
                key.SetValue("NextWidgetStyle", 99);
                check(Convert.ToInt32(Value(Call("WidgetSettings", "Load", registryPath)!, "NextStyle")) == 0,
                    "Invalid saved next-widget styles fall back to Minimal");
                key.SetValue("NextWidgetStyle", "invalid");
                check(Convert.ToInt32(Value(Call("WidgetSettings", "Load", registryPath)!, "NextStyle")) == 0,
                    "Malformed next-widget styles fall back safely");
            }
        }
        finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(registryPath, throwOnMissingSubKey: false); }

        object initial = Activator.CreateInstance(Type("WidgetSettings"))!;
        Set(initial, "NextStyle", Style(1));
        var previews = new List<object>();
        Action<object> capture = value => previews.Add(value);
        Delegate callback = Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(Type("WidgetSettings")), capture.Target, capture.Method);
        using Form form = (Form)Activator.CreateInstance(Type("SettingsForm"),
            false, "system", 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, "", true, false, true, true, true, 92, initial, callback)!;
        var selector = Control<ComboBox>(form, "nextWidgetStyleComboBox");
        check(selector.Items.Count == 3 && selector.SelectedIndex == 1, "Next-widget selector restores the saved style");
        previews.Clear();
        selector.SelectedIndex = 2;
        object read = form.GetType().GetMethod("ReadWidgetSettings", Methods)!.Invoke(form, new object[] { true })!;
        check(previews.Count > 0 && Convert.ToInt32(Value(previews.Last(), "NextStyle")) == 2 && Convert.ToInt32(Value(read, "NextStyle")) == 2,
            "Next-widget style changes reach both live preview and accepted settings");
        form.GetType().GetMethod("ApplyPreviewLocalization", Methods)!.Invoke(form, new object[] { "en" });
        check(selector.SelectedIndex == 2, "Changing preview language preserves the next-widget style");
        form.GetType().GetMethod("ResetAllSettings", Methods)!.Invoke(form, null);
        check(selector.SelectedIndex == 0 && Convert.ToInt32(Value(initial, "NextStyle")) == 1,
            "Restoring defaults selects Minimal without mutating the original settings");

        var normalColors = new List<int>();
        for (int style = 0; style < 3; style++)
        {
            using Bitmap normal = new(52, 52, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using Bitmap hover = new(52, 52, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(normal)) Call("NextWidgetRenderer", "Draw", g, normal.Size, Style(style), false);
            using (Graphics g = Graphics.FromImage(hover)) Call("NextWidgetRenderer", "Draw", g, hover.Size, Style(style), true);
            normalColors.Add(normal.GetPixel(8, 26).ToArgb());
            check(normal.GetPixel(0, 0).A == 0 && hover.GetPixel(8, 26).A > normal.GetPixel(8, 26).A,
                $"Next-widget style {style} retains transparent corners and visible hover feedback");
        }
        check(normalColors.Distinct().Count() == 3, "Minimal, Clean, and Glow render distinct next-widget surfaces");
    }

    /// <summary>Checks accepted extensions and real image metadata without touching wallpaper files.</summary>
    private static void CheckImages(Action<bool, string> check)
    {
        check(new[] { ".jpg", ".JPEG", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp" }
            .All(ext => (bool)Call("WallpaperImageInfo", "IsSupportedWallpaperExtension", "image" + ext)!), "Shared image filter retains every supported extension");
        check(new[] { "image", "image.png.exe", "image.svg", "image.txt" }
            .All(path => !(bool)Call("WallpaperImageInfo", "IsSupportedWallpaperExtension", path)!), "Shared image filter rejects unsupported and misleading suffixes");
        string path = Path.Combine(Path.GetTempPath(), "WallpaperControl-image-test-" + Guid.NewGuid() + ".png");
        try
        {
            using (var image = new Bitmap(17, 23)) image.Save(path);
            check(Equals(Call("WallpaperImageInfo", "GetImageResolutionText", path), "17 × 23"), "Shared image metadata reads actual dimensions");
            File.Delete(path);
            check(!string.IsNullOrWhiteSpace((string?)Call("WallpaperImageInfo", "GetImageResolutionText", path)), "Missing image metadata uses the localized fallback");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    /// <summary>Checks path geometry and repeated native bitmap uploads to a window that is never shown.</summary>
    private static void CheckDrawing(Action<bool, string> check)
    {
        using var empty = (GraphicsPath)Call("WidgetDrawing", "RoundedRectangle", RectangleF.Empty, 4f, true)!;
        check(empty.PointCount == 0, "Empty layered-widget bounds retain an empty path");
        using var shape = (GraphicsPath)Call("WidgetDrawing", "RoundedRectangle", new RectangleF(0, 0, 80, 40), 100f, true)!;
        RectangleF bounds = shape.GetBounds();
        check(Math.Abs(bounds.X) < 0.01f && Math.Abs(bounds.Y) < 0.01f &&
            Math.Abs(bounds.Width - 80) < 0.01f && Math.Abs(bounds.Height - 40) < 0.01f &&
            shape.IsVisible(40, 20) && !shape.IsVisible(0, 0),
            "Rounded widget paths retain bounds, radius clamping, and transparent corners");
        using var calendarShape = (GraphicsPath)Call("WidgetDrawing", "RoundedRectangle", new RectangleF(0, 0, 80, 40), 100f, false)!;
        check(shape.PathPoints.SequenceEqual(calendarShape.PathPoints) && shape.PathTypes.SequenceEqual(calendarShape.PathTypes),
            "Calendar and other widgets retain identical geometry for valid bounds");
        using var window = new ProbeWindow();
        using var bitmap = new Bitmap(32, 32);
        using (Graphics g = Graphics.FromImage(bitmap)) g.Clear(Color.FromArgb(100, 20, 40, 60));
        void Upload() => Call("LayeredWidgetBitmap", "Update", window.Handle, window.Location, bitmap);
        Upload();
        int before = GetGuiResources(System.Diagnostics.Process.GetCurrentProcess().Handle, 0);
        for (int i = 0; i < 100; i++) Upload();
        int after = GetGuiResources(System.Diagnostics.Process.GetCurrentProcess().Handle, 0);
        check(before > 0 && after <= before + 2 && bitmap.GetPixel(10, 10).A == 100 && !window.Visible,
            "Repeated hidden bitmap uploads release native GDI resources and retain the caller's bitmap");
    }

    /// <summary>Checks drag guards, position deltas, completion callbacks, and handler disposal.</summary>
    private static void CheckDragging(Action<bool, string> check)
    {
        using var window = new ProbeWindow { Location = new Point(100, 100) };
        bool locked = true; Point cursor = new(10, 20); int rendered = 0; var positions = new List<Point>();
        using var handler = (IDisposable)Activator.CreateInstance(Type("WidgetDragHandler"), Methods, null,
            new object[] { window, (Func<bool>)(() => locked), (Action)(() => rendered++), (Action<Point>)positions.Add, (Func<Point>)(() => cursor) }, null)!;
        window.Down(MouseButtons.Left); cursor = new(20, 30); window.MoveMouse(); window.Up(MouseButtons.Left);
        check(window.Location == new Point(100, 100) && positions.Count == 0, "Locked widgets ignore dragging");
        locked = false;
        window.Down(MouseButtons.Right); window.MoveMouse(); window.Up(MouseButtons.Right);
        check(rendered == 0 && positions.Count == 0, "Non-left mouse buttons do not start widget dragging");
        window.Down(MouseButtons.Left); cursor = new(-10, 70); window.MoveMouse(); window.Up(MouseButtons.Right);
        check(window.Location == new Point(70, 140) && rendered == 1 && positions.Count == 0, "Dragging preserves screen-space deltas and ignores unrelated button releases");
        window.Up(MouseButtons.Left); window.Up(MouseButtons.Left);
        check(positions.SequenceEqual(new[] { new Point(70, 140) }) && !window.Capture, "Drag completion releases capture and reports the final position once");
        handler.Dispose(); window.Down(MouseButtons.Left); cursor = Point.Empty; window.MoveMouse(); window.Up(MouseButtons.Left);
        check(rendered == 1 && positions.Count == 1, "Disposed drag handlers no longer react to mouse events");
    }

    [DllImport("user32.dll")]
    private static extern int GetGuiResources(IntPtr process, int flags);

    private sealed class ProbeWindow : Form
    {
        protected override CreateParams CreateParams
        {
            get { var value = base.CreateParams; value.ExStyle |= 0x00080000 | 0x00000080; return value; }
        }
        internal void Down(MouseButtons button) => OnMouseDown(new MouseEventArgs(button, 1, 0, 0, 0));
        internal void MoveMouse() => OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, 0, 0, 0));
        internal void Up(MouseButtons button) => OnMouseUp(new MouseEventArgs(button, 1, 0, 0, 0));
    }
}
