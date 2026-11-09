extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using System.Reflection;

internal static class WidgetDisplayPositionTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Members)!.GetValue(target)!;
    private static void Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, Members)!.Invoke(target, args);

    internal static void Run(Action<bool, string> check)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                CheckWidget(true, check);
                CheckWidget(false, check);
                var position = new App.WidgetDisplayPosition(new Point(2917, 288));
                foreach (int dpi in new[] { 96, 144, 192 })
                {
                    Size size = new(340 * dpi / 96, 400 * dpi / 96);
                    Point fallback = position.Resolve(size, new Rectangle(0, 0, 1920, 1040));
                    check(fallback.X == 1920 - size.Width && position.Preferred == new Point(2917, 288)
                        && position.Resolve(size, new Rectangle(0, 0, 5120, 2880)) == position.Preferred,
                        "Display recovery: DPI-dependent fallback never replaces the preferred position " + dpi);
                }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void CheckWidget(bool calendar, Action<bool, string> check)
    {
        string name = calendar ? "Calendar" : "Notes";
        Point preferred = calendar ? new(2917, 288) : new(2403, 1);
        int saves = 0;
        var settings = new App.WidgetSettings { NotesLocation = preferred, NotesLocked = true, ClockLanguageCode = "en" };
        // Isolated empty store and invisible native windows; no user's settings,
        // monitor configuration, calendar sources or notes are changed.
        string path = Path.Combine(AppContext.BaseDirectory, "test-data", "display-" + Guid.NewGuid().ToString("N"), "notes.json");
        using Form form = calendar
            ? new App.CalendarWidgetForm(true, App.SystemWidgetStyle.Clean, 3, false, 30, "en", new Provider(), preferred, _ => saves++)
            : new App.NotesWidgetForm(new App.NotesStore(path), settings, _ => saves++, _ => { });
        var position = Field<App.WidgetDisplayPosition>(form, "displayPosition");
        Rectangle area = new(0, 0, 3440, 1392), requested = default;
        position.WorkingAreaForBounds = bounds => { requested = bounds; return area; };
        _ = form.Handle;
        form.GetType().GetField("shown", Members)!.SetValue(form, true);
        void Changed(int message = 0x007E) => Invoke(form, "WndProc", Message.Create(form.Handle, message, IntPtr.Zero, IntPtr.Zero));
        void Restore(Point point)
        {
            if (calendar) ((App.CalendarWidgetForm)form).RestoreLocation(point);
            else ((App.NotesWidgetForm)form).RestoreLocation(point);
        }
        Changed();
        check(form.Location == preferred, name + " display recovery: saved position is rendered initially");
        area = new(0, 0, 1920, 1040); Changed();
        check(form.Left == 1920 - form.Width && position.Preferred == preferred && saves == 0,
            name + " display recovery: temporary 1920-wide display uses a visible fallback without saving it");
        area = new(0, 0, 3440, 1392); Changed();
        check(form.Location == preferred && requested.Location == preferred && saves == 0,
            name + " display recovery: locked widget returns to its original position when 3440-wide display returns");
        for (int repeat = 0; repeat < 3; repeat++)
        {
            area = new(0, 0, 1920, 1040); Changed(0x001A);
            area = new(0, 0, 3440, 1392); Changed(0x001A);
        }
        check(form.Location == preferred && saves == 0,
            name + " display recovery: repeated work-area changes do not drift or persist fallback positions");
        Restore(new Point(-1200, 100));
        area = new(-1920, 0, 1920, 1040); Changed();
        check(form.Location == new Point(-1200, 100) && requested.Left == -1200,
            name + " display recovery: monitor selection uses preferred coordinates including negative origins");
        area = new(0, 0, 1920, 1040); Changed();
        check(form.Left == 0 && position.Preferred.X == -1200 && saves == 0,
            name + " display recovery: disconnected monitor keeps widget reachable without losing its original position");
        area = new(-1920, 0, 1920, 1040); Changed();
        check(form.Left == -1200, name + " display recovery: reconnected monitor restores original placement");
        area = new(0, 0, 3440, 1392); Restore(preferred);
        object drag = Field<object>(form, calendar ? "dragHandler" : "drag");
        // Exercise the real shared handler's drag-completion callback, which is
        // the only path allowed to replace preferred placement and persist it.
        Field<Action<Point>>(drag, "locationChanged")(new Point(900, 120));
        Changed();
        check(form.Location == new Point(900, 120) && saves == 1,
            name + " display recovery: completed user move becomes the new preferred location and saves once");
        Restore(preferred); Changed();
        check(form.Location == preferred && saves == 1,
            name + " display recovery: explicit settings restore replaces a preview position without a spurious save");
    }

    private sealed class Provider : App.ICalendarProvider
    {
        public string ProviderName => "Fixture";
        public string StatusResourceKey => "CalendarStatusConnected";
        public DateTime? LastRefresh => null;
        public Task RefreshAsync(CancellationToken token) => Task.CompletedTask;
        public IReadOnlyList<App.CalendarEvent> GetUpcoming(DateTime from, int maxEntries, string languageCode) => Array.Empty<App.CalendarEvent>();
    }
}
