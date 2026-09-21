extern alias WallpaperApp;

using App = WallpaperApp::WallpaperControl;
using WallpaperControl;
using Microsoft.Win32;
using System.Drawing;
using System.Net;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;

internal static class CalendarSourceTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const string Secret = "https://calendar.test/private-token-417/feed.ics";

    internal static void Run(Action<bool, string> check)
    {
        Persistence(check);
        Feeds(check);
        DisableDuringRefresh(check);
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try { Editors(check); Transactions(check); Drawing(check); Layout(check); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Persistence(Action<bool, string> check)
    {
        foreach ((string normal, string holiday, int count) in new[]
        {
            (Secret, "", 1), (Secret + "\r\nhttps://calendar.test/two", "", 2),
            ("", Secret, 1), (Secret + "\nhttps://calendar.test/two", "https://calendar.test/holiday\nhttps://calendar.test/holiday2", 4)
        })
        {
            string path = @"Software\WallpaperControl.SourceTests-" + Guid.NewGuid().ToString("N");
            try
            {
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(path);
                string encryptedNormal = App.WindowsSecretProtector.Protect(normal);
                string encryptedHoliday = App.WindowsSecretProtector.Protect(holiday);
                key.SetValue("CalendarWidgetIcsUrlProtected", encryptedNormal);
                key.SetValue("CalendarWidgetHolidayIcsUrlProtected", encryptedHoliday);
                App.WidgetSettings first = App.WidgetSettings.Load(path);
                check(first.CalendarSources.Count == count && first.CalendarSources.Select(s => s.Url)
                    .SequenceEqual((normal + "\n" + holiday).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)),
                    $"Sources migration preserves all {count} encrypted legacy entries");
                check(first.CalendarSources.Count(s => s.IsHoliday) == holiday.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length
                    && first.CalendarSources.All(s => s.Enabled && s.Id != Guid.Empty && s.DisplayName("de").Length > 0)
                    && first.CalendarSources.Select(s => s.ColorArgb).Distinct().Count() == count,
                    "Sources migration assigns types, stable IDs, fallback names and distinct colors");
                check(App.WidgetSettings.Load(path).CalendarSources.SequenceEqual(first.CalendarSources)
                    && (string?)key.GetValue("CalendarWidgetIcsUrlProtected") == encryptedNormal
                    && (string?)key.GetValue("CalendarWidgetHolidayIcsUrlProtected") == encryptedHoliday,
                    "Sources second startup is idempotent and retains legacy recovery ciphertext");
                first.CalendarSources = first.CalendarSources.Select((s, index) => s with
                {
                    Name = "Named " + index, Enabled = false, Type = App.CalendarSourceType.Holiday,
                    ColorArgb = Color.Fuchsia.ToArgb(), Url = Secret + "?changed=" + index
                }).Reverse().ToList();
                first.Save(path);
                check(App.WidgetSettings.Load(path).CalendarSources.SequenceEqual(first.CalendarSources),
                    "Sources save round-trips every field, source ID and list order");
                string ciphertext = (string)key.GetValue(App.CalendarSourceStore.ValueName)!;
                check(!ciphertext.Contains("private-token", StringComparison.Ordinal)
                    && !ciphertext.Contains("Named", StringComparison.Ordinal)
                    && App.WindowsSecretProtector.Unprotect(ciphertext).Contains("private-token", StringComparison.Ordinal),
                    "Sources entire persisted envelope is DPAPI encrypted");
                first.CalendarSources.Clear();
                first.Save(path);
                check(App.WidgetSettings.Load(path).CalendarSources.Count == 0,
                    "Sources saving an empty list does not remigrate retained legacy calendars");
            }
            finally { Registry.CurrentUser.DeleteSubKeyTree(path, false); }
        }

        string failurePath = @"Software\WallpaperControl.SourceTests-" + Guid.NewGuid().ToString("N");
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(failurePath);
            string encrypted = App.WindowsSecretProtector.Protect(Secret);
            key.SetValue("CalendarWidgetIcsUrlProtected", encrypted);
            List<App.CalendarSource> migration = App.CalendarSourceStore.Migrate(Secret, "");
            bool rejected = false;
            try { App.CalendarSourceStore.Save(key, migration, _ => "invalid ciphertext"); }
            catch (InvalidOperationException ex) { rejected = !ex.Message.Contains(Secret, StringComparison.Ordinal); }
            check(rejected && key.GetValue(App.CalendarSourceStore.ValueName) == null
                && (string?)key.GetValue("CalendarWidgetIcsUrlProtected") == encrypted,
                "Sources failed encryption leaves legacy recoverable and reports no secret");
            check(App.WidgetSettings.Load(failurePath).CalendarSources.Single().Url == Secret,
                "Sources migration retries successfully after failed persistence");
            key.SetValue("CalendarWidgetHolidayIcsUrlProtected", "unreadable DPAPI");
            key.DeleteValue(App.CalendarSourceStore.ValueName);
            check(App.WidgetSettings.Load(failurePath).CalendarSources.Single().Url == Secret
                && key.GetValue(App.CalendarSourceStore.ValueName) == null
                && (string?)key.GetValue("CalendarWidgetHolidayIcsUrlProtected") == "unreadable DPAPI",
                "Sources partially unreadable legacy data prevents incomplete migration commit");
            key.SetValue(App.CalendarSourceStore.ValueName, App.WindowsSecretProtector.Protect("{bad-json"));
            check(App.WidgetSettings.Load(failurePath).CalendarSources.Single().Url == Secret,
                "Sources malformed new envelope safely falls back to preserved legacy data");
            key.DeleteValue("CalendarWidgetHolidayIcsUrlProtected");
            App.CalendarSourceStore.Save(key, new[] { migration[0] with { ColorArgb = 0 } });
            check(App.WidgetSettings.Load(failurePath).CalendarSources[0].ColorArgb == App.CalendarSource.DefaultColor(0),
                "Sources invalid transparent saved color receives a safe numeric fallback");
            App.CalendarSourceStore.Save(key, new[] { migration[0] with { Name = "Recoverable" } });
            string beforeFailure = (string)key.GetValue(App.CalendarSourceStore.ValueName)!;
            using (RegistryKey readOnly = Registry.CurrentUser.OpenSubKey(failurePath)!)
            {
                bool failed = false;
                try { App.CalendarSourceStore.Save(readOnly, migration); }
                catch (UnauthorizedAccessException) { failed = true; }
                check(failed && (string?)key.GetValue(App.CalendarSourceStore.ValueName) == beforeFailure,
                    "Sources failed registry write preserves the existing configuration");
            }
            App.CalendarSourceStore.Save(key, new[] { migration[0] with { Name = "Newer" } });
            key.SetValue(App.CalendarSourceStore.ValueName, "broken ciphertext");
            check(App.WidgetSettings.Load(failurePath).CalendarSources[0].Name == "Recoverable",
                "Sources damaged primary envelope recovers the last verified new-format snapshot");
            foreach (string invalid in new[] { "null", "{}", "{\"Version\":2,\"Sources\":[]}", "{\"Version\":1,\"Sources\":[null]}" })
                check(!App.CalendarSourceStore.TryDecode(App.WindowsSecretProtector.Protect(invalid), out _),
                    "Sources malformed or unsupported envelope is rejected safely");
            check(App.CalendarSourceStore.Migrate(Secret + "\n" + Secret, Secret).Count == 3,
                "Sources migration never silently discards duplicate legacy entries across or within types");
            System.Globalization.CultureInfo previousCulture = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new("fr-FR");
                App.CalendarSourceStore.Save(key, new[] { migration[0] with { ColorArgb = Color.Orange.ToArgb() } });
                check(App.WidgetSettings.Load(failurePath).CalendarSources[0].ColorArgb == Color.Orange.ToArgb(),
                    "Sources numeric color persistence is independent of the current culture");
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = previousCulture; }
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(failurePath, false); }
    }

    private sealed class FeedHandler : HttpMessageHandler
    {
        internal readonly List<string> Requests = new();
        internal bool Fail;
        internal string Metadata = "X-WR-CALNAME:Team calendar\r\n";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsolutePath);
            if (Fail) throw new HttpRequestException(Secret);
            string day = DateTime.Today.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
            string end = DateTime.Today.AddDays(3).ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
            string ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n" + Metadata
                + "BEGIN:VEVENT\r\nUID:timed\r\nDTSTART:" + day + "T120000\r\nDTEND:" + day
                + "T130000\r\nRRULE:FREQ=DAILY;COUNT=2\r\nSUMMARY:Timed fixture\r\nLOCATION:Office\r\nEND:VEVENT\r\n"
                + "BEGIN:VEVENT\r\nUID:allday\r\nDTSTART;VALUE=DATE:" + day + "\r\nDTEND;VALUE=DATE:" + end
                + "\r\nSUMMARY:Multi-day fixture\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ics) });
        }
    }

    private static void Feeds(Action<bool, string> check)
    {
        using FeedHandler handler = new();
        using HttpClient client = new(handler);
        using IcsCalendarProvider provider = new(client);
        CalendarSource a = new() { Url = Secret, ColorArgb = Color.Cyan.ToArgb() };
        CalendarSource b = new() { Url = "https://calendar.test/b", Name = "Personal", ColorArgb = Color.Yellow.ToArgb(), Enabled = false };
        CalendarSource holiday = new() { Url = "https://calendar.test/holidays", Type = CalendarSourceType.Holiday };
        void Refresh() => provider.RefreshAsync().GetAwaiter().GetResult();
        IReadOnlyList<CalendarEvent> Events() => provider.GetUpcoming(DateTime.Today, 9, "en");
        provider.SetSources(new[] { a, b, holiday });
        Refresh();
        check(handler.Requests.Count == 2 && !handler.Requests.Contains("/b") && Events().All(e => e.SourceId != b.Id),
            "Sources disabled feeds are neither downloaded nor displayed");
        check(Events().Where(e => e.SourceId == a.Id).All(e => e.SourceName == "Team calendar" && e.SourceColorArgb == a.ColorArgb),
            "Sources X-WR-CALNAME suggestion and color follow stable source identity");
        check(Events()[0].IsHoliday && Events().Where(e => e.SourceId == a.Id).Count(e => e.IsAllDay) == 3
            && Events().Where(e => e.SourceId == a.Id).Count(e => !e.IsAllDay) == 2,
            "Sources preserve holiday priority, timed recurrence and multi-day all-day expansion");
        b = b with { Enabled = true };
        provider.SetSources(new[] { b, holiday, a });
        Refresh();
        check(Events().Any(e => e.SourceId == b.Id) && Events().Where(e => e.SourceId == b.Id).All(e => e.SourceName == "Personal"),
            "Sources re-enable downloads and preserve manual names against metadata");
        check(Events().Where(e => e.SourceId == a.Id).All(e => e.SourceColorArgb == a.ColorArgb)
            && Events().Where(e => e.SourceId == b.Id).All(e => e.SourceColorArgb == b.ColorArgb),
            "Sources reordering cannot swap event origin or color");
        string[] titles = Events().Select(e => e.Title).ToArray();
        using StringWriter log = new();
        TextWriter previous = Console.Out;
        try { Console.SetOut(log); handler.Fail = true; Refresh(); }
        finally { Console.SetOut(previous); }
        check(Events().Select(e => e.Title).SequenceEqual(titles) && provider.StatusResourceKey == "CalendarStatusStale",
            "Sources transient network failure preserves session caches");
        check(!log.ToString().Contains(Secret, StringComparison.Ordinal) && log.ToString().Contains("HttpRequestException", StringComparison.Ordinal)
            && !a.ToString().Contains(Secret, StringComparison.Ordinal),
            "Sources logs and generated model diagnostics never expose a private URL");
        handler.Fail = false;
        foreach (string metadata in new[] { "", "X-WR-CALNAME\r\n", "X-WR-CALNAME:https://calendar.test/private-token\r\n" })
        {
            handler.Metadata = metadata;
            Refresh();
            check(Events().Where(e => e.SourceId == a.Id).All(e => e.SourceName == "Calendar 1")
                && provider.StatusResourceKey == "CalendarStatusConnected",
                "Sources missing, malformed or URL-valued metadata uses fallback without breaking events");
        }
        handler.Metadata = "X-WR-CALNAME:Folded\r\n name\r\n";
        Refresh();
        check(Events().Where(e => e.SourceId == a.Id).All(e => e.SourceName == "Foldedname"),
            "Sources optional name metadata supports ICS line unfolding");
        handler.Metadata = "NAME:Modern calendar\r\n";
        Refresh();
        check(Events().Where(e => e.SourceId == a.Id).All(e => e.SourceName == "Modern calendar"),
            "Sources RFC 7986 NAME metadata is also supported");
        handler.Requests.Clear();
        provider.SetSources(new[] { a with { Enabled = false }, b with { Enabled = false }, holiday with { Enabled = false } });
        Refresh();
        check(handler.Requests.Count == 0 && Events().Count == 0 && provider.StatusResourceKey == "CalendarStatusNoSource",
            "Sources disabling all calendars clears display and performs no requests");
        provider.SetSources(new[] { a with { Url = "file:///private.ics" } });
        Refresh();
        check(handler.Requests.Count == 0 && provider.StatusResourceKey == "CalendarStatusInvalidAddress",
            "Sources legacy unsupported schemes are preserved but never fetched");
        provider.SetSources(Enumerable.Range(0, 17).Select(index => a with { Id = Guid.NewGuid(), Url = Secret + "?" + index }));
        Refresh();
        check(handler.Requests.Count == CalendarFeedLimits.MaxSources && provider.StatusResourceKey == "CalendarStatusPartial",
            "Sources new configuration path retains the 16-active-feed resource limit");
    }

    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, Members)!.GetValue(value)!;

    private sealed class WaitingHandler : HttpMessageHandler
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Requests;
        internal bool Cancelled;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { Cancelled = true; throw; }
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private static void DisableDuringRefresh(Action<bool, string> check)
    {
        using WaitingHandler handler = new();
        using HttpClient client = new(handler);
        using IcsCalendarProvider provider = new(client);
        CalendarSource a = new() { Url = Secret };
        CalendarSource b = new() { Url = Secret + "?second" };
        provider.SetSources(new[] { a, b });
        Task refresh = provider.RefreshAsync();
        handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        provider.SetSources(new[] { a with { Enabled = false }, b with { Enabled = false } });
        refresh.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        check(handler.Cancelled && handler.Requests == 1 && provider.GetUpcoming(DateTime.Today, 9, "en").Count == 0
            && provider.StatusResourceKey == "CalendarStatusNoSource",
            "Sources disabling during refresh cancels the active request and prevents queued downloads");
    }
    private static string NativeText(TextBox box)
    {
        StringBuilder text = new(2048);
        SendMessage(box.Handle, 0x000D, (IntPtr)text.Capacity, text);
        return text.ToString();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr SendMessage(IntPtr handle, uint message, IntPtr parameter, StringBuilder? text);

    private static void Editors(Action<bool, string> check)
    {
        App.CalendarSource source = new() { Url = Secret, Name = "Manual" };
        using (App.CalendarSourceEditorForm editor = new(source, false, "en"))
        {
            check(NativeText(editor.UrlBox) == "********" && SendMessage(editor.UrlBox.Handle, 0x00D2, IntPtr.Zero, null) != IntPtr.Zero
                && editor.UrlBox.AccessibilityObject.Value == "********" && string.IsNullOrEmpty(editor.UrlBox.AccessibleDescription),
                "Sources real editor starts with native password masking and no accessible secret");
            Button reveal = AllControls(editor).OfType<Button>().Single(b => b.Text == App.Localization.Get("SettingsCalendarShowSource", "en"));
            // Invoke the real click handler without displaying a window on the desktop.
            typeof(Button).GetMethod("OnClick", Members)!.Invoke(reveal, new object[] { EventArgs.Empty });
            check(NativeText(editor.UrlBox) == Secret && !editor.UrlBox.Multiline && !editor.UrlBox.ReadOnly,
                "Sources Show reveals the single-line editable address");
            editor.UrlBox.Text = Secret + "?edited";
            typeof(Button).GetMethod("OnClick", Members)!.Invoke(reveal, new object[] { EventArgs.Empty });
            check(NativeText(editor.UrlBox) == "********" && !editor.UrlBox.CanUndo && editor.UrlBox.Text.EndsWith("?edited", StringComparison.Ordinal),
                "Sources Hide clears native secret and undo history while preserving edits");
            Field<TextBox>(editor, "nameBox").Text = "Changed";
            Field<ComboBox>(editor, "typeBox").SelectedIndex = 1;
            Field<CheckBox>(editor, "enabledBox").Checked = false;
            check(editor.TryAccept() && editor.Source.Id == source.Id && editor.Source.Name == "Changed"
                && editor.Source.IsHoliday && !editor.Source.Enabled && editor.Source.Url.EndsWith("?edited", StringComparison.Ordinal),
                "Sources editor applies all fields without changing identity");
            foreach (string invalid in new[] { "file:///secret", "ftp://calendar.test", "javascript:alert(1)", "https://", Secret + "\n" + Secret })
            {
                editor.UrlBox.Text = invalid;
                check(!editor.TryAccept(), "Sources editor rejects invalid or non-HTTP address without echoing it");
            }
        }
        using App.CalendarSourceEditorForm reopened = new(source, true, "de");
        check(reopened.UrlBox.SourcesHidden && NativeText(reopened.UrlBox) == "********",
            "Sources closing and reopening resets reveal state");
        using App.CalendarSourcesForm manager = new(new[] { source }, false, "en");
        ListView list = Field<ListView>(manager, "list");
        _ = list.Handle;
        check(list.Items.Count == 1 && list.SmallImageList!.Images.Count == 1
            && !AllControls(manager).Any(c => c.Text.Contains(Secret, StringComparison.Ordinal))
            && list.Items.Cast<ListViewItem>().All(item => item.SubItems.Cast<ListViewItem.ListViewSubItem>().All(cell => !cell.Text.Contains(Secret, StringComparison.Ordinal))),
            "Sources manager shows a color swatch and name without any URL column");
        list.Items[0].Checked = false;
        check(!manager.Sources[0].Enabled && source.Enabled,
            "Sources manager checkbox edits only its isolated draft");
    }

    private static App.WidgetSettingsEditor Settings(App.WidgetSettings initial) => new(initial, preview: null);

    private static void Transactions(Action<bool, string> check)
    {
        string path = @"Software\WallpaperControl.SourceTests-" + Guid.NewGuid().ToString("N");
        App.CalendarSource original = new() { Url = Secret, Name = "Original" };
        App.WidgetSettings initial = new() { CalendarSources = new() { original } };
        try
        {
            initial.Save(path);
            foreach ((string name, Action<List<App.CalendarSource>> change) in new (string, Action<List<App.CalendarSource>>)[]
            {
                ("Add", list => list.Add(new App.CalendarSource { Url = Secret + "?new" })),
                ("Delete", list => list.Clear()),
                ("Color", list => list[0] = list[0] with { ColorArgb = Color.Red.ToArgb() }),
                ("URL", list => list[0] = list[0] with { Url = Secret + "?new" }),
                ("Name", list => list[0] = list[0] with { Name = "Changed" }),
                ("Type", list => list[0] = list[0] with { Type = App.CalendarSourceType.Holiday }),
                ("Enabled", list => list[0] = list[0] with { Enabled = false })
            })
            {
                using App.WidgetSettingsEditor settings = Settings(initial);
                change(Field<List<App.CalendarSource>>(settings, "calendarSources"));
                App.WidgetSettings preview = (App.WidgetSettings)settings.GetType().GetMethod("ReadWidgetSettings", Members)!.Invoke(settings, new object[] { false })!;
                settings.Dispose();
                check(!preview.CalendarSources.SequenceEqual(initial.CalendarSources)
                    && initial.CalendarSources.Single() == original
                    && App.WidgetSettings.Load(path).CalendarSources.Single() == original,
                    "Sources Settings " + name + " + Cancel preserves original and persisted data");
            }
            using App.WidgetSettingsEditor accepted = Settings(initial);
            List<App.CalendarSource> draft = Field<List<App.CalendarSource>>(accepted, "calendarSources");
            draft[0] = original with { Name = "Saved", Enabled = false, Type = App.CalendarSourceType.Holiday,
                ColorArgb = Color.Yellow.ToArgb(), Url = Secret + "?saved" };
            draft.Add(new App.CalendarSource { Name = "Added", Url = Secret + "?added" });
            var acceptedSettings = accepted.ReadWidgetSettings(true);
            acceptedSettings.Save(path);
            check(App.WidgetSettings.Load(path).CalendarSources.SequenceEqual(draft)
                && initial.CalendarSources.Single() == original,
                "Sources Settings Save accepts all source edits and persists an independent snapshot");
            draft.Clear();
            check(acceptedSettings.CalendarSources.Count == 2,
                "Sources accepted settings cannot be mutated through the closed dialog draft");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, false); }
    }

    private static void Drawing(Action<bool, string> check)
    {
        App.CalendarEvent a = new(DateTime.Today.AddHours(12), DateTime.Today.AddHours(13), false, "ALPHA", "OFFICE", "A", Guid.NewGuid(), Color.Lime.ToArgb());
        App.CalendarEvent b = a with { Title = "BRAVO", Location = "HOME", SourceName = "PERSONAL", SourceId = Guid.NewGuid(), SourceColorArgb = Color.Magenta.ToArgb() };
        App.CalendarEvent holiday = a with { Title = "HOLIDAY", Location = "", SourceName = "", IsHoliday = true, SourceId = Guid.NewGuid() };
        using App.IcsCalendarProvider provider = new();
        foreach (App.SystemWidgetStyle style in Enum.GetValues<App.SystemWidgetStyle>())
        {
            using App.CalendarWidgetForm form = new(true, style, 9, true, 30, "en", provider, Point.Empty, _ => { });
            using Bitmap bitmap = form.RenderBitmap(new[] { a, b, holiday }, 1000);
            using Bitmap alternate = form.RenderBitmap(new[] { a with { SourceColorArgb = Color.Yellow.ToArgb() }, b with { SourceColorArgb = Color.Cyan.ToArgb() }, holiday with { SourceColorArgb = Color.Blue.ToArgb() } }, 1000);
            check(CountColor(bitmap, new Rectangle(16, 74, 54, 21), Color.Lime) > 0
                && CountColor(bitmap, new Rectangle(72, 73, 230, 21), Color.Lime) > 0
                && CountColor(bitmap, new Rectangle(16, 108, 286, 21), Color.Magenta) > 0,
                $"Sources {style} pixels use independent configured colors for event time and title");
            check(CountColor(bitmap, new Rectangle(72, 93, 230, 14), Color.Lime) > 0
                && CountColor(bitmap, new Rectangle(72, 127, 230, 14), Color.Magenta) > 0,
                $"Sources {style} locations from both sources use the event color");
            check(SamePixels(bitmap, alternate, new Rectangle(0, 0, 340, 73)),
                $"Sources {style} day headings and widget chrome remain theme controlled");
            check(SamePixels(bitmap, alternate, new Rectangle(10, 141, 320, 22)),
                $"Sources {style} holiday highlighting ignores conflicting source text color");
        }
    }

    private static int CountColor(Bitmap bitmap, Rectangle area, Color expected)
    {
        int count = 0;
        for (int y = area.Top; y < Math.Min(area.Bottom, bitmap.Height); y++)
            for (int x = area.Left; x < area.Right; x++)
                if (bitmap.GetPixel(x, y).ToArgb() == expected.ToArgb()) count++;
        return count;
    }

    private static bool SamePixels(Bitmap a, Bitmap b, Rectangle area)
    {
        for (int y = area.Top; y < Math.Min(area.Bottom, a.Height); y++)
            for (int x = area.Left; x < area.Right; x++)
                if (a.GetPixel(x, y) != b.GetPixel(x, y)) return false;
        return true;
    }

    private static IEnumerable<Control> AllControls(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (Control descendant in AllControls(child)) yield return descendant;
        }
    }

    private static void Layout(Action<bool, string> check)
    {
        foreach (string language in new[] { "de", "en", "fr", "es", "ja" })
        foreach (bool dark in new[] { false, true })
        {
            using App.CalendarSourceEditorForm editor = new(new App.CalendarSource { Url = Secret }, dark, language);
            using App.CalendarSourcesForm manager = new(new[] { new App.CalendarSource { Url = Secret } }, dark, language);
            // Real native layout, while keeping the test windows invisible to the user.
            editor.Opacity = 0;
            manager.Opacity = 0;
            editor.Show();
            manager.Show(editor);
            Application.DoEvents();
            editor.PerformLayout();
            manager.PerformLayout();
            check(AllControls(editor).Concat(AllControls(manager)).Where(c => c is Button or Label)
                .All(c => !c.Text.StartsWith("CalendarSource", StringComparison.Ordinal) && c.Width >= TextRenderer.MeasureText(c.Text, c.Font,
                    new Size(c.Width, int.MaxValue), TextFormatFlags.WordBreak).Width - 4),
                $"Sources {language} {(dark ? "dark" : "light")} labels are localized and fit allocated widths");
            check(AllControls(editor).Where(c => c is Button or TextBox or ComboBox or CheckBox)
                .All(c => c.Parent!.ClientRectangle.Contains(c.Bounds)),
                $"Sources {language} {(dark ? "dark" : "light")} editor controls stay inside their layout containers");
            if (language is "de" or "fr")
            {
                string artifactRoot = Path.Combine(AppContext.BaseDirectory, "calendar-source-artifacts");
                Directory.CreateDirectory(artifactRoot);
                using Bitmap editorImage = new(editor.Width, editor.Height);
                editor.DrawToBitmap(editorImage, new Rectangle(Point.Empty, editorImage.Size));
                editorImage.Save(Path.Combine(artifactRoot, $"editor-{language}-{dark}.png"));
                using Bitmap managerImage = new(manager.Width, manager.Height);
                manager.DrawToBitmap(managerImage, new Rectangle(Point.Empty, managerImage.Size));
                managerImage.Save(Path.Combine(artifactRoot, $"manager-{language}-{dark}.png"));
            }
            editor.Scale(new SizeF(1.5f, 1.5f));
            editor.PerformLayout();
            check(AllControls(editor).Where(c => c is Button or TextBox or ComboBox or CheckBox)
                .All(c => c.Parent!.ClientRectangle.Contains(c.Bounds)),
                $"Sources {language} {(dark ? "dark" : "light")} 150-percent scaled controls remain inside layout");
        }
    }
}
