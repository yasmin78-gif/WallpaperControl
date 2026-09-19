extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using Microsoft.Win32;
using System.Drawing;
using System.Reflection;
using System.Runtime.ExceptionServices;

internal static class CalendarScrollTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, Members)!.GetValue(value)!;
    private static void Invoke(object value, string name, params object[] args) => value.GetType().GetMethod(name, Members)!.Invoke(value, args);

    internal static void Run(Action<bool, string> check)
    {
        Geometry(check);
        Persistence(check);
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try { Rendering(check); Input(check); Settings(check); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Geometry(Action<bool, string> check)
    {
        foreach (int dpi in new[] { 96, 144, 192 })
        {
            App.CalendarViewport view = new();
            float scale = dpi / 96f;
            view.Update(200, 700, 2500, dpi);
            check(view.PhysicalHeight == Math.Ceiling(279 * scale) && !view.CanScroll && view.ScrollOffset == 0,
                $"Calendar {dpi} DPI compact content stays below its configured ceiling");
            view.Update(621, 700, 2500, dpi);
            check(view.PhysicalHeight == 700 * scale && !view.CanScroll,
                $"Calendar {dpi} DPI exact fit does not introduce a scrollbar");
            view.Update(622, 700, 2500, dpi);
            check(view.PhysicalHeight == 700 * scale && view.CanScroll && Math.Abs(view.MaxScrollOffset - 1) < .01f,
                $"Calendar {dpi} DPI overflow starts only beyond the viewport");
            check(view.Track.Width * scale == 6 * scale && view.ContentBounds.Right < view.Track.Left
                && view.Track.Bottom == view.LogicalHeight - 25 && view.Track.Top == 54,
                $"Calendar {dpi} DPI scrollbar, reserved text width and fixed chrome use consistent units");
            view.Update(1600, 1400, 500, dpi);
            check(view.PhysicalHeight == 500 && view.CanScroll,
                $"Calendar {dpi} DPI runtime height respects the physical work area");
            view.Update(1600, 1400, 4000, dpi);
            check(view.PhysicalHeight <= App.CalendarFeedLimits.MaxWidgetHeight && view.PhysicalHeight <= 1400 * scale,
                $"Calendar {dpi} DPI user ceiling also retains the hard bitmap height budget");
        }
        App.CalendarViewport scroll = new();
        scroll.Update(1500, 300, 1000, 96);
        check(scroll.ScrollOffset == 0 && scroll.Wheel(-120, 3) && scroll.ScrollOffset == 63,
            "Calendar wheel down starts at top and respects configured system lines");
        check(scroll.Wheel(-240, 3) && scroll.ScrollOffset == 189 && scroll.Wheel(120, 3) && scroll.ScrollOffset == 126,
            "Calendar multiple wheel notches and wheel up use opposite directions");
        scroll.Wheel(int.MaxValue, 100);
        check(scroll.ScrollOffset == 0, "Calendar extreme wheel delta safely clamps to the top");
        scroll.SetOffset(float.MaxValue);
        check(scroll.ScrollOffset == scroll.MaxScrollOffset && !scroll.SetOffset(float.MaxValue),
            "Calendar bottom clamp avoids redundant redraws");
        scroll.SetOffset(-1);
        check(scroll.ScrollOffset == 0 && !scroll.Wheel(-120, 0), "Calendar negative offsets and disabled wheel scrolling stay at zero");
        App.CalendarViewport fractional = new();
        fractional.Update(1000, 300, 1000, 96);
        check(!fractional.Wheel(-60, 3) && fractional.Wheel(-60, 3) && fractional.ScrollOffset == 63,
            "Calendar high-resolution half-notches accumulate without premature redraws");
        fractional.SetOffset(0);
        check(fractional.Wheel(-120, -1) && fractional.ScrollOffset == fractional.ViewportHeight,
            "Calendar Windows page-scroll setting uses one viewport");
        scroll.SetOffset(600);
        scroll.Update(1800, 300, 1000, 96);
        check(scroll.ScrollOffset == 600, "Calendar expanding content preserves a valid scroll offset");
        scroll.Update(700, 300, 1000, 96);
        check(scroll.ScrollOffset == scroll.MaxScrollOffset, "Calendar shorter content clamps stale offsets");
        scroll.Update(10, 300, 1000, 96);
        check(scroll.ScrollOffset == 0 && !scroll.CanScroll && !scroll.Wheel(-120, 3),
            "Calendar content-fit state removes scrollbar and hidden offset");
        scroll.Update(1000, 300, 80, 192);
        check(scroll.PhysicalHeight == 80 && scroll.ViewportHeight == 0,
            "Calendar impossibly small work areas cannot produce negative viewport dimensions");
    }

    private static void Persistence(Action<bool, string> check)
    {
        string path = @"Software\WallpaperControl.ScrollTests-" + Guid.NewGuid().ToString("N");
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(path);
            check(App.WidgetSettings.Load(path).CalendarMaximumHeight == 700, "Calendar existing settings default to 700 logical pixels");
            App.WidgetSettings settings = new() { CalendarMaximumHeight = 900 };
            settings.Save(path);
            check(key.GetValueKind("CalendarWidgetMaximumHeight") == RegistryValueKind.DWord
                && App.WidgetSettings.Load(path).CalendarMaximumHeight == 900 && settings.Clone().CalendarMaximumHeight == 900,
                "Calendar maximum height saves, reloads and clones as a culture-independent integer");
            foreach ((object value, int expected) in new[] { ((object)"bad", 700), ((object)(-1), 300), ((object)3000, 1400) })
            {
                key.SetValue("CalendarWidgetMaximumHeight", value);
                check(App.WidgetSettings.Load(path).CalendarMaximumHeight == expected,
                    "Calendar malformed and out-of-range maximum height safely defaults or clamps");
            }
            settings.CalendarMaximumHeight = 1400;
            settings.Save(path);
            App.CalendarViewport viewport = new();
            viewport.Update(2000, settings.CalendarMaximumHeight, 400, 192);
            check(App.WidgetSettings.Load(path).CalendarMaximumHeight == 1400 && settings.CalendarMaximumHeight == 1400,
                "Calendar runtime work-area and DPI clamps never rewrite the stored preference");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, false); }
    }

    private sealed class Provider : App.ICalendarProvider
    {
        internal App.CalendarEvent[] Events = EventsForDays();
        internal int Refreshes;
        internal int Reads;
        public string ProviderName => "Fixture";
        public string StatusResourceKey => "CalendarStatusConnected";
        public DateTime? LastRefresh => null;
        public Task RefreshAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); Refreshes++; return Task.CompletedTask; }
        public IReadOnlyList<App.CalendarEvent> GetUpcoming(DateTime from, int maxEntries, string languageCode)
        { Reads++; return Events; }
    }

    private static App.CalendarEvent[] EventsForDays() => Enumerable.Range(0, 36).Select(i => new App.CalendarEvent(
        DateTime.Today.AddDays(i / 4).AddHours(12), DateTime.Today.AddDays(i / 4).AddHours(13), false,
        i == 0 ? "HOLIDAY" : new string('W', 80), "LOCATION", "DO NOT DISPLAY SOURCE", Guid.NewGuid(),
        (i % 2 == 0 ? Color.Lime : Color.Magenta).ToArgb(), i == 0)).ToArray();

    private static App.CalendarWidgetForm Form(Provider provider, bool location = true, int maximum = 300,
        App.SystemWidgetStyle style = App.SystemWidgetStyle.Clean, Action<Point>? moved = null) =>
        new(true, style, 9, location, 30, "en", provider, new Point(30, 30), moved ?? (_ => { }), maximum);

    private static bool Same(Bitmap a, Bitmap b, Rectangle area)
    {
        for (int y = area.Top; y < Math.Min(area.Bottom, Math.Min(a.Height, b.Height)); y++)
            for (int x = area.Left; x < Math.Min(area.Right, Math.Min(a.Width, b.Width)); x++)
                if (a.GetPixel(x, y) != b.GetPixel(x, y)) return false;
        return true;
    }

    private static int Pixels(Bitmap image, Color color, Rectangle area)
    {
        int count = 0;
        for (int y = area.Top; y < Math.Min(area.Bottom, image.Height); y++)
            for (int x = area.Left; x < Math.Min(area.Right, image.Width); x++)
                if (image.GetPixel(x, y).ToArgb() == color.ToArgb()) count++;
        return count;
    }

    private static void Rendering(Action<bool, string> check)
    {
        Provider provider = new();
        foreach (App.SystemWidgetStyle style in Enum.GetValues<App.SystemWidgetStyle>())
        foreach (int dpi in new[] { 96, 144, 192 })
        {
            using App.CalendarWidgetForm form = Form(provider, style: style);
            using Bitmap top = form.RenderBitmap(provider.Events, 2000, dpi);
            App.CalendarViewport view = Field<App.CalendarViewport>(form, "viewport");
            float scale = dpi / 96f;
            check(top.Height == 300 * scale && top.Width == 340 * scale && view.CanScroll
                && view.TotalContentHeight == 9 * 32 + 36 * 34,
                $"Calendar {style}/{dpi} scrolling retains all nine selected days within the capped bitmap");
            Rectangle thumbTop = Rectangle.Ceiling(new RectangleF(view.Thumb.X * scale, view.Thumb.Y * scale, view.Thumb.Width * scale, view.Thumb.Height * scale));
            view.SetOffset(150);
            using Bitmap scrolled = form.RenderBitmap(provider.Events, 2000, dpi);
            if (style == App.SystemWidgetStyle.Clean && dpi == 96)
            {
                string images = Path.Combine(AppContext.BaseDirectory, "calendar-scroll-artifacts");
                Directory.CreateDirectory(images);
                top.Save(Path.Combine(images, "top.png"));
                scrolled.Save(Path.Combine(images, "scrolled.png"));
            }
            check(Same(top, scrolled, new Rectangle(0, 0, top.Width, (int)(54 * scale)))
                && Same(top, scrolled, new Rectangle(0, (int)(275 * scale), top.Width, (int)(25 * scale))),
                $"Calendar {style}/{dpi} clipped scrolling keeps header and footer pixels fixed");
            check(!Same(top, scrolled, new Rectangle(12, (int)(60 * scale), (int)(295 * scale), (int)(180 * scale)))
                && !Same(top, scrolled, thumbTop), $"Calendar {style}/{dpi} content translates and thumb reflects the offset");
            Rectangle content = new(0, (int)(54 * scale), (int)(310 * scale), (int)(221 * scale));
            check(Pixels(scrolled, Color.Lime, content) > 0 && Pixels(scrolled, Color.Magenta, content) > 0,
                $"Calendar {style}/{dpi} two source colors survive scrolling");
            Rectangle reserved = new((int)(311 * scale), (int)(54 * scale), (int)(29 * scale), (int)(220 * scale));
            check(Pixels(scrolled, Color.Lime, reserved) == 0 && Pixels(scrolled, Color.Magenta, reserved) == 0,
                $"Calendar {style}/{dpi} long event text cannot paint over scrollbar space");
            view.SetOffset(0);
            var changedHoliday = provider.Events.Select(e => e with { SourceColorArgb = Color.Blue.ToArgb() }).ToArray();
            using Bitmap differentColors = form.RenderBitmap(changedHoliday, 2000, dpi);
            check(Same(top, differentColors, new Rectangle((int)(12 * scale), (int)(74 * scale), (int)(290 * scale), (int)(18 * scale))),
                $"Calendar {style}/{dpi} holiday row retains its independent styling");
        }
        App.CalendarEvent original = provider.Events[1] with { Title = "FIXTURE", Location = "", IsHoliday = false };
        using App.CalendarWidgetForm locationForm = Form(provider, maximum: 700);
        using Bitmap noLocation = locationForm.RenderBitmap(new[] { original }, 2000);
        using Bitmap noSource = locationForm.RenderBitmap(new[] { original with { SourceName = "" } }, 2000);
        check(noLocation.Size == noSource.Size && Same(noLocation, noSource, new Rectangle(Point.Empty, noLocation.Size)),
            "Calendar named source without location produces neither detail text nor an extra line");
        using Bitmap location = locationForm.RenderBitmap(new[] { original with { Location = "LOCATION" } }, 2000);
        check(location.Height == noLocation.Height + 13 && !Same(location, noLocation, new Rectangle(72, 93, 230, 14)),
            "Calendar a real location creates its optional colored detail line");
        using App.CalendarWidgetForm hiddenLocationForm = Form(provider, location: false, maximum: 700);
        using Bitmap hidden = hiddenLocationForm.RenderBitmap(new[] { original with { Location = "LOCATION" } }, 2000);
        check(hidden.Size == noLocation.Size && Same(hidden, noLocation, new Rectangle(Point.Empty, hidden.Size)),
            "Calendar Show location off removes both detail pixels and detail height");
    }

    private static void Input(Action<bool, string> check)
    {
        Provider provider = new();
        int moves = 0;
        using App.CalendarWidgetForm form = Form(provider, moved: _ => moves++);
        form.Opacity = 0;
        form.Show();
        Application.DoEvents();
        var view = Field<App.CalendarViewport>(form, "viewport");
        Point position = form.Location;
        IntPtr handle = form.Handle;
        int refreshes = provider.Refreshes, reads = provider.Reads;
        check(form.ScrollWheel(new Point(100, 150), -120, 3) && form.Location == position && form.Handle == handle
            && provider.Refreshes == refreshes && provider.Reads == reads && moves == 0,
            "Calendar locked wheel input redraws cached content without provider work, saves, movement or recreation");
        float offset = view.ScrollOffset;
        Point screenPoint = form.PointToScreen(new Point(100, 150));
        int coordinates = unchecked((ushort)screenPoint.X | ((ushort)screenPoint.Y << 16));
        Message wheelMessage = Message.Create(form.Handle, 0x020A, new IntPtr(-120 << 16), new IntPtr(coordinates));
        Invoke(form, "WndProc", wheelMessage);
        check(view.ScrollOffset > offset && provider.Refreshes == refreshes && provider.Reads == reads,
            "Calendar native WM_MOUSEWHEEL reaches the local viewport without provider activity");
        offset = view.ScrollOffset;
        check(!form.ScrollWheel(new Point(100, 20), -120, 3) && view.ScrollOffset == offset,
            "Calendar wheel over fixed header cannot scroll content");
        Invoke(form, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, (int)view.Thumb.X + 2, (int)view.Thumb.Y + 2, 0));
        Invoke(form, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, (int)view.Track.X, (int)view.Track.Bottom, 0));
        Invoke(form, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 325, 270, 0));
        check(view.ScrollOffset == view.MaxScrollOffset && form.Location == position && moves == 0,
            "Calendar thumb dragging reaches bottom without starting a widget drag");
        form.Apply(false, App.SystemWidgetStyle.Clean, 9, true, 30, "en", 300);
        object drag = Field<object>(form, "dragHandler");
        Point cursor = new(100, 100);
        drag.GetType().GetField("cursorPosition", Members)!.SetValue(drag, (Func<Point>)(() => cursor));
        Invoke(form, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
        check(!form.ScrollWheel(new Point(100, 150), 120, 3), "Calendar wheel is ignored during a widget position drag");
        cursor.Offset(20, 15);
        Invoke(form, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 40, 35, 0));
        Invoke(form, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 40, 35, 0));
        check(form.Location == new Point(position.X + 20, position.Y + 15) && moves == 1,
            "Calendar ordinary unlocked dragging retains its final position callback");
        refreshes = provider.Refreshes;
        form.Apply(true, App.SystemWidgetStyle.Clean, 9, true, 30, "en", 700);
        check(form.Height > 300 && provider.Refreshes == refreshes && form.Handle == handle,
            "Calendar maximum-height preview recalculates geometry without feed refresh or window recreation");
        int savedHeight = form.Height;
        form.Apply(true, App.SystemWidgetStyle.Clean, 9, true, 30, "en", 300);
        form.Apply(true, App.SystemWidgetStyle.Clean, 9, true, 30, "en", 700);
        check(form.Height == savedHeight && provider.Refreshes == refreshes,
            "Calendar restoring a cancelled maximum-height preview restores geometry without reload");
        view.SetOffset(100);
        form.RefreshCalendar();
        check(view.ScrollOffset == 100, "Calendar same-size refresh retains a valid scroll position");
        provider.Events = provider.Events.Take(24).ToArray();
        view.SetOffset(view.MaxScrollOffset);
        form.RefreshCalendar();
        check(view.ScrollOffset <= view.MaxScrollOffset && view.ViewportHeight > 0,
            "Calendar shorter refreshed data cannot leave an empty stale viewport");
        provider.Events = provider.Events.Take(1).ToArray();
        form.RefreshCalendar();
        check(!view.CanScroll && view.ScrollOffset == 0 && form.Height < 700,
            "Calendar short refresh removes scrollbar, resets offset and shrinks the widget");
        provider.Events = EventsForDays();
        form.RefreshCalendar();
        check(view.CanScroll && view.ScrollOffset == 0 && form.Height == 700,
            "Calendar longer refreshed content recalculates height and scrollbar without stale offset");
        form.ScrollWheel(new Point(100, 150), -120, 3);
        offset = view.ScrollOffset;
        form.SetActivitySuspended(true);
        check(!form.ScrollWheel(new Point(100, 150), -120, 3) && view.ScrollOffset == offset,
            "Calendar fullscreen/activity suspension blocks scrolling while preserving scroll state");
        form.SetActivitySuspended(false);
        check(view.ScrollOffset == offset && Field<System.Windows.Forms.Timer>(form, "refreshTimer").Enabled,
            "Calendar resume refreshes normally and preserves valid scroll state");
        form.Dispose();
        check(!form.ScrollWheel(new Point(100, 150), -120, 3), "Calendar disposed scrolled widget rejects further input safely");
    }

    private static void Settings(Action<bool, string> check)
    {
        App.WidgetSettings original = new() { CalendarMaximumHeight = 900 };
        using (App.SettingsForm form = NewSettings(original))
        {
            NumericUpDown numeric = Field<NumericUpDown>(form, "calendarMaximumHeightNumeric");
            numeric.Value = 350;
            var draft = (App.WidgetSettings)form.GetType().GetMethod("ReadWidgetSettings", Members)!.Invoke(form, new object[] { false })!;
            check(draft.CalendarMaximumHeight == 350 && original.CalendarMaximumHeight == 900,
                "Calendar maximum-height draft and manual preview copy preserve the original for Cancel");
            form.DialogResult = DialogResult.Cancel;
            form.Close();
            check(original.CalendarMaximumHeight == 900, "Calendar Settings Cancel retains the original maximum height");
        }
        using (App.SettingsForm form = NewSettings(original))
        {
            Field<NumericUpDown>(form, "calendarMaximumHeightNumeric").Value = 1000;
            Invoke(form, "SaveAndClose");
            check(form.WidgetSettings.CalendarMaximumHeight == 1000 && original.CalendarMaximumHeight == 900,
                "Calendar Settings Save accepts an independent maximum-height snapshot");
        }
        using (App.SettingsForm form = NewSettings(original))
        {
            Invoke(form, "ResetAllSettings");
            check(Field<NumericUpDown>(form, "calendarMaximumHeightNumeric").Value == 700,
                "Calendar Reset restores the logical 700-pixel default");
        }
        foreach (string language in new[] { "de", "en", "fr", "es", "ja" })
        foreach (float scale in new[] { 1f, 1.5f, 2f })
        {
            using App.SettingsForm form = NewSettings(original);
            Invoke(form, "ApplyPreviewLocalization", language);
            NumericUpDown numeric = Field<NumericUpDown>(form, "calendarMaximumHeightNumeric");
            Control panel = numeric.Parent!;
            panel.Scale(new SizeF(scale, scale));
            panel.PerformLayout();
            Label label = panel.Controls.OfType<Label>().Single(c => (string?)c.Tag == "SettingsCalendarMaximumHeight");
            check(label.Right < numeric.Left && numeric.Bottom <= panel.Height && label.Text == App.Localization.Get("SettingsCalendarMaximumHeight", language)
                && numeric.Minimum == 300 && numeric.Maximum == 1400 && numeric.TabStop,
                $"Calendar {language}/{scale} maximum-height label and numeric control fit and remain keyboard accessible");
        }
    }

    private static App.SettingsForm NewSettings(App.WidgetSettings settings) => new(false, "system", 0u, 0u, 0u, 0u,
        0u, 0u, 0u, 0u, "", true, false, true, true, true, 92, settings, null);
}
