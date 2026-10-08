extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using System.Net;
using System.Text;
using System.Reflection;
using Microsoft.Win32;
internal static class FeedTests
{
    private const string Rss = "<rss version='2.0'><channel><title>Example feed</title><item><guid>one</guid><title>First article</title><link>/first</link><pubDate>Wed, 07 Oct 2026 12:00:00 GMT</pubDate><description>&lt;p&gt;Description &amp;amp; details&lt;/p&gt;</description></item><item><guid>two</guid><title>Second article</title><link>/second</link><pubDate>Wed, 07 Oct 2026 13:00:00 GMT</pubDate></item></channel></rss>";
    private sealed class Handler : HttpMessageHandler
    {
        internal HttpStatusCode Status = HttpStatusCode.OK;
        internal string Body = Rss;
        internal int Calls;
        internal string? Conditional;
        internal TaskCompletionSource? Hold;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            Calls++; Conditional = request.Headers.IfNoneMatch.FirstOrDefault()?.ToString();
            if (Hold != null) await Hold.Task.WaitAsync(cancellation);
            return new(Status) { Content = new StringContent(Body), Headers = { ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"version1\"") }, RequestMessage = request };
        }
    }
    internal static void Run(Action<bool,string> check, string? liveUrl = null)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                var task = RunAsync(check, liveUrl);
                while (!task.IsCompleted) { Application.DoEvents(); Thread.Sleep(5); }
                task.GetAwaiter().GetResult();
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if (error != null) throw error;
    }
    private static App.ParsedFeed Parse(string text) { using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text)); return App.FeedParser.Parse(stream,new Uri("https://example.test/feed")); }
    private static async Task RunAsync(Action<bool,string> check, string? liveUrl)
    {
        var filterNow = DateTimeOffset.Parse("2026-03-31T12:00:00Z");
        check(App.FeedService.WithinPeriod(filterNow.AddHours(-24),App.FeedPeriod.Day,filterNow) && !App.FeedService.WithinPeriod(filterNow.AddHours(-24).AddTicks(-1),App.FeedPeriod.Day,filterNow), "24-hour filter includes exact boundary");
        check(App.FeedService.WithinPeriod(filterNow.AddDays(-7),App.FeedPeriod.Week,filterNow) && !App.FeedService.WithinPeriod(filterNow.AddDays(-8),App.FeedPeriod.Week,filterNow), "Seven-day filter boundary");
        check(App.FeedService.WithinPeriod(filterNow.AddMonths(-1),App.FeedPeriod.Month,filterNow) && !App.FeedService.WithinPeriod(filterNow.AddMonths(-1).AddTicks(-1),App.FeedPeriod.Month,filterNow), "Month filter handles short February");
        foreach (var period in Enum.GetValues<App.FeedPeriod>()) check(App.FeedService.WithinPeriod(null,period,filterNow), "Undated entry remains visible "+period);
        check(App.FeedService.WithinPeriod(filterNow.AddYears(-5),App.FeedPeriod.All,filterNow), "All available entries has no age filter");
        check(App.FeedWidgetSettings.Parse("{\"Period\":999}").Period == App.FeedPeriod.All, "Invalid period defaults to all");
        var parsed = Parse(Rss);
        check(parsed.Title == "Example feed" && parsed.Entries.Count == 2 && parsed.Entries[0].Id == "two", "RSS title, identity and descending dates");
        check(parsed.Entries[1].Description == "Description & details" && parsed.Entries[1].Link == "https://example.test/first", "HTML description becomes plain text and relative links resolve");
        check(parsed.Entries[0].Published == DateTimeOffset.Parse("2026-10-07T13:00:00Z"), "RSS GMT date preserves instant");
        var atom = Parse("<feed xmlns='http://www.w3.org/2005/Atom'><title>Atom</title><entry><id>a</id><title>Entry</title><updated>2026-10-07T14:00:00+02:00</updated><link rel='self' href='/api'/><link rel='alternate' href='/article'/><summary>Text</summary></entry></feed>");
        check(atom.Entries.Single().Link == "https://example.test/article" && atom.Entries[0].Published == DateTimeOffset.Parse("2026-10-07T12:00:00Z"), "Atom alternate link and updated timestamp");
        check(Parse("<rss><channel><title>Empty</title></channel></rss>").Entries.Count == 0, "Valid empty feed");
        check(Parse("<rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#' xmlns='http://purl.org/rss/1.0/'><channel><title>RSS1</title></channel><item><title>One</title><link>/one</link></item></rdf:RDF>").Entries.Count == 1, "RSS 1.0 RDF entries");
        check(Parse("<feed xmlns='http://www.w3.org/2005/Atom' xml:base='https://other.test/'><title>Atom</title><entry xml:base='news/'><id>a</id><title>A</title><link href='article'/></entry></feed>").Entries[0].Link == "https://other.test/news/article", "Inherited Atom xml:base");
        check(Parse("<rss><channel><item><guid>a</guid><title>A</title></item><item><guid>a</guid><title>B</title></item></channel></rss>").Entries.Count == 1, "Duplicate GUID does not duplicate entries");
        var image = Parse("<rss><channel><item><description>&lt;img src='/cover.jpg'&gt;Review</description></item></channel></rss>");
        check(image.Entries[0].ImageUrl == "https://example.test/cover.jpg", "Letterboxd-style embedded thumbnail resolves");
        check(App.FeedParser.WebUri("javascript:alert(1)") == null && App.FeedParser.WebUri("file:///C:/secret") == null && App.FeedParser.WebUri("https://user:pass@example.test") == null, "Only credential-free HTTP/S feed and article URLs");
        try { Parse("<!DOCTYPE rss [<!ENTITY x SYSTEM 'file:///C:/secret'>]><rss><channel><title>&x;</title></channel></rss>"); check(false,"DTD rejected"); } catch (System.Xml.XmlException) { check(true,"DTD/external entity rejected"); }
        try { Parse("<html><body>Not a feed</body></html>"); check(false,"HTML rejected"); } catch (System.Xml.XmlException) { check(true,"HTML is not mistaken for RSS"); }
        check(Parse("<rss><channel>"+string.Concat(Enumerable.Range(0,150).Select(i => "<item><guid>"+i+"</guid><title>A</title></item>"))+"</channel></rss>").Entries.Count == 60, "Entries are bounded");
        var source = new App.FeedSource { Title = "News", Url = "https://example.test/feed", RefreshMinutes = 15 };
        var configuration = new App.FeedWidgetSettings { Enabled = true, Sources = [source] };
        var handler = new Handler(); using var service = new App.FeedService(handler, false);
        var now = DateTimeOffset.Parse("2026-10-07T12:00:00Z"); service.Now = () => now; service.Configure(configuration);
        await service.RefreshAsync(); check(handler.Calls == 1 && service.Items(null).Count() == 2, "Initial refresh loads configured feed");
        await service.RefreshAsync(); check(handler.Calls == 1, "No early automatic refetch");
        check(service.Items(null,App.FeedPeriod.Day).Count() == 2, "Recent entries pass date filter");
        service.Now = () => now.AddDays(2); check(!service.Items(source.Id,App.FeedPeriod.Day).Any() && service.Items(source.Id,App.FeedPeriod.Week).Count() == 2, "Source selection and date filter combine without refetch"); service.Now = () => now;
        now += TimeSpan.FromMinutes(15); handler.Status = HttpStatusCode.NotModified; await service.RefreshAsync();
        check(handler.Calls == 2 && handler.Conditional == "\"version1\"" && service.Items(null).Count() == 2, "Conditional 304 preserves entries");
        handler.Status = HttpStatusCode.InternalServerError; await service.RefreshAsync(true);
        check(service.Snapshots[source.Id].Error != null && service.Items(null).Count() == 2, "Network error retains cached entries and marks failure");
        handler.Status = HttpStatusCode.OK; handler.Body = "<html/>"; await service.RefreshAsync(true);
        check(service.Items(null).Count() == 2 && service.Snapshots[source.Id].Error != null, "Malformed response does not replace valid data");
        handler.Body = Rss; handler.Hold = new(); var held = service.RefreshAsync(true); int calls = handler.Calls; await service.RefreshAsync(true);
        check(handler.Calls == calls && service.Busy, "Rapid manual refreshes do not overlap");
        service.Configure(new() { Enabled = true }); handler.Hold.SetResult(); await held;
        check(service.Items(null).Count() == 0 && service.Snapshots.Count == 0, "Removed source and stale response cannot reappear");
        handler.Hold = null; service.Configure(configuration); service.SetSuspended(true); calls = handler.Calls; await service.RefreshAsync(true);
        check(handler.Calls == calls, "Fullscreen suspension prevents requests");
        service.SetSuspended(false); while (service.Busy) { await Task.Delay(5); }
        check(service.Items(null).Count() == 2, "Resume refreshes pending source");
        source = source with { RefreshMinutes = 0 }; configuration.Sources = [source]; service.Configure(configuration); now += TimeSpan.FromDays(10); await service.RefreshAsync();
        calls = handler.Calls; now += TimeSpan.FromDays(10); await service.RefreshAsync(); check(handler.Calls == calls, "Manual-only feed has no subsequent automatic refresh");
        await service.RefreshAsync(true); check(handler.Calls == calls+1, "Manual-only feed supports explicit refresh");
        var second = new App.FeedSource { Title = "Other", Url = "https://other.test/feed", RefreshMinutes = 15 };
        configuration.Sources.Add(second); service.Configure(configuration); await service.RefreshAsync(true);
        check(service.Items(null).Count() == 4 && service.Items(source.Id).Count() == 2, "Multiple RSS sources combine and remain independently selectable");
        configuration.Sources.Remove(second); service.Configure(configuration);
        configuration.Enabled = false; service.Configure(configuration); calls = handler.Calls; await service.RefreshAsync(true); check(handler.Calls == calls, "Disabled widget performs no requests");
        configuration.Enabled = true; service.Configure(configuration); await service.RefreshAsync(true);
        using var widget = new App.FeedWidgetForm(service,configuration,"de",_ => {},_ => {},() => {},() => {});
        using (var cacheWidget = new App.FeedWidgetForm(service,configuration,"de",_=>{},_=>{},()=>{},()=>{}))
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var visible = (HashSet<string>)typeof(App.FeedWidgetForm).GetField("visibleImages",flags)!.GetValue(cacheWidget)!;
            var cache = (Dictionary<string,Bitmap>)typeof(App.FeedWidgetForm).GetField("images",flags)!.GetValue(cacheWidget)!;
            visible.UnionWith(["visible-one","visible-two"]);
            for (int i = 0; i < 48; i++) cacheWidget.CacheImage("old"+i,new Bitmap(2,2));
            cacheWidget.CacheImage("visible-one",new Bitmap(2,2));
            cacheWidget.CacheImage("visible-two",new Bitmap(2,2));
            check(cache.ContainsKey("visible-one") && cache.ContainsKey("visible-two") && cache.Count == 48, "Full image cache retains newly loaded visible covers despite dictionary slot reuse");
            for (int i = 0; i < 60; i++) cacheWidget.CacheImage("later"+i,new Bitmap(2,2));
            check(cache.ContainsKey("visible-one") && cache.ContainsKey("visible-two") && cache.Count == 48, "Repeated image completions cannot evict visible covers or grow cache");
        }
        string outputs = "C:/Users/yasmi/Documents/Codex/2026-09-30/v/outputs/feed-widget"; Directory.CreateDirectory(outputs);
        foreach (var mode in Enum.GetValues<App.FeedDisplayMode>())
        {
            configuration.Display = mode; widget.Apply(configuration,"de"); using var bitmap = widget.RenderBitmap(); bitmap.Save(Path.Combine(outputs,mode+".png"));
            check(bitmap.Width == 390 && bitmap.Height <= configuration.MaximumHeight, "Bounded rendering "+mode);
        }
        foreach (var style in Enum.GetValues<App.SystemWidgetStyle>())
        {
            configuration.Style = style; widget.Apply(configuration,"de"); using var bitmap = widget.RenderBitmap(600,144); check(bitmap.Width == 585 && bitmap.Height <= 600, "DPI-safe rendering "+style);
        }
        configuration.Display = App.FeedDisplayMode.List;
        handler.Body = "<rss><channel>" + string.Concat(Enumerable.Range(0, 20).Select(i => "<item><guid>long"+i+"</guid><title>Article "+i+"</title><pubDate>Wed, 07 Oct 2026 "+(i%24).ToString("00")+":00:00 GMT</pubDate></item>")) + "</channel></rss>";
        await service.RefreshAsync(true); widget.Apply(configuration,"de"); using (var initial = widget.RenderBitmap()) { }
        var viewport = (App.CalendarViewport)typeof(App.FeedWidgetForm).GetField("viewport", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(widget)!;
        viewport.SetOffset(340); using (var scrolled = widget.RenderBitmap()) { }
        handler.Body = handler.Body.Replace("</channel>", "<item><guid>newest</guid><title>New</title><pubDate>Thu, 08 Oct 2026 12:00:00 GMT</pubDate></item></channel>");
        await service.RefreshAsync(true); using (var refreshed = widget.RenderBitmap()) { }
        check(viewport.ScrollOffset == 452, "New article preserves reading anchor during automatic refresh");
        widget.Location = new(-30000,-30000); widget.Show(); Application.DoEvents();
        var thumb = viewport.Thumb; var position = widget.Location; float offset = viewport.ScrollOffset;
        var members = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(App.FeedWidgetForm).GetMethod("OnMouseDown",members)!.Invoke(widget,[new MouseEventArgs(MouseButtons.Left,1,(int)((thumb.X+3)*viewport.Scale),(int)((thumb.Y+5)*viewport.Scale),0)]);
        typeof(App.FeedWidgetForm).GetMethod("OnMouseMove",members)!.Invoke(widget,[new MouseEventArgs(MouseButtons.Left,0,(int)((thumb.X+3)*viewport.Scale),(int)((thumb.Y+45)*viewport.Scale),0)]);
        check(viewport.ScrollOffset > offset && widget.Location == position, "Dragging scrollbar scrolls entries without dragging unlocked widget");
        typeof(App.FeedWidgetForm).GetMethod("OnMouseUp",members)!.Invoke(widget,[new MouseEventArgs(MouseButtons.Left,1,0,0,0)]);
        check(!widget.Capture && !(bool)typeof(App.FeedWidgetForm).GetField("thumbDragging",members)!.GetValue(widget)!, "Scrollbar mouse release ends drag and capture");
        widget.Hide();
        check(service.Items(Guid.NewGuid()).Count() == 0 && service.Items(source.Id).Count() == 21, "Single-feed selection filters aggregate list");
        using (var dialog = new App.FeedEditorForm(service,source,"de"))
        {
            await (Task)typeof(App.FeedEditorForm).GetMethod("Probe", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog,null)!;
            var save = (Button)typeof(App.FeedEditorForm).GetField("save", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
            check(save.Enabled, "Feed editor validates URL and enables save after preview");
            dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new(-30000,-30000); dialog.Show(); Application.DoEvents();
            using var preview = new Bitmap(dialog.Width,dialog.Height); dialog.DrawToBitmap(preview,new Rectangle(Point.Empty,preview.Size)); preview.Save(Path.Combine(outputs,"AddFeed.png")); dialog.Hide();
        }
        handler.Body = Rss;
        using var editor = new App.WidgetSettingsEditor(new() { Feed = configuration });
        check(editor.WidgetKeys.Contains("FeedTitle") && editor.ReadWidgetSettings(false).Feed.Enabled, "Feed settings page integrates with widget preview");
        foreach (var language in new[] { "de","en","fr","es","ja" })
        { editor.ApplyPresentation(false,language); check(App.Localization.Get("FeedModeFocusFirst",language) != "FeedModeFocusFirst", "Feed translation "+language); }
        string registry = @"Software\WallpaperControl.FeedTests\"+Guid.NewGuid().ToString("N");
        try
        {
            configuration.Period = App.FeedPeriod.Week; var settings = new App.WidgetSettings { Feed = configuration }; settings.Save(registry); var restored = App.WidgetSettings.Load(registry);
            check(restored.Feed.Sources.Single().Id == source.Id && restored.Feed.Display == configuration.Display && restored.Feed.Period == App.FeedPeriod.Week, "Feed sources and display persist");
            var clone = settings.Clone(); clone.Feed.Sources.Clear(); check(settings.Feed.Sources.Count == 1, "Feed cloning separates mutable source list");
            check(App.FeedWidgetSettings.Parse("{broken").Sources.Count == 0, "Broken configuration safely defaults");
            new App.WidgetSettings().Save(registry);
            using var manager = new App.WidgetManager(() => {},registryPath: registry);
            var baseline = manager.Settings; manager.SetEditing(true); var draft = manager.Settings; draft.Feed.MaximumHeight = 850; manager.Preview(draft);
            var contents = manager.Settings.Feed; contents.Sources.Add(source); contents.Period = App.FeedPeriod.Day;
            typeof(App.WidgetManager).GetMethod("SaveFeedContents",BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(manager,[contents]);
            check(App.WidgetSettings.Load(registry).Feed.Sources.Count == 1 && App.WidgetSettings.Load(registry).Feed.MaximumHeight == 500, "Feed management persists independently from unsaved visual preview");
            manager.CancelPreview(baseline);
            check(manager.Settings.Feed.Sources.Count == 1 && manager.Settings.Feed.MaximumHeight == 500 && manager.Settings.Feed.Period == App.FeedPeriod.Day, "Discarding visual draft retains independently managed feeds");
            var accepted = baseline.Clone(); accepted.Feed.MaximumHeight = 800; manager.CommitPreview(accepted);
            check(App.WidgetSettings.Load(registry).Feed.Sources.Count == 1 && App.WidgetSettings.Load(registry).Feed.MaximumHeight == 800, "Committing visual preferences does not overwrite newer feed sources");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(registry,false); }
        var alertHandler = new Handler(); using (var alertService = new App.FeedService(alertHandler,false))
        {
            alertService.Fullscreen = () => false; int alerts = 0, entries = 0; alertService.NewEntries += (_,count) => { alerts++; entries=count; };
            var alertSource = source with { Notifications = true }; alertService.Configure(new() { Enabled = true,Sources = [alertSource] });
            await alertService.RefreshAsync(true); check(alerts == 0,"Initial feed service refresh does not announce existing articles");
            alertHandler.Body = Rss.Replace("</channel>","<item><guid>new-a</guid><title>New A</title></item><item><guid>new-b</guid><title>New B</title></item></channel>");
            await alertService.RefreshAsync(true); check(alerts == 1 && entries == 2,"Successful feed response raises one bundle for two new articles");
            await alertService.RefreshAsync(true); check(alerts == 1,"Repeated service refresh does not duplicate feed alert");
            alertService.SetSuspended(true); alertHandler.Body = alertHandler.Body.Replace("</channel>","<item><guid>paused-new</guid><title>Paused</title></item></channel>");
            alertService.SetSuspended(false); while(alertService.Busy) await Task.Delay(5); await alertService.RefreshAsync(true);
            check(alerts == 1,"Feed service resume establishes silent baseline rather than delivering backlog");
        }
        var cancellationHandler = new Handler { Hold = new() }; var cancelService = new App.FeedService(cancellationHandler,false); cancelService.Configure(configuration);
        var pending = cancelService.RefreshAsync(true); cancelService.Dispose(); await pending; check(!cancelService.Busy, "Disposal cancels pending request and releases busy state");
        if (liveUrl != null)
        {
            using var live = new App.FeedService(startTimer: false, notificationHistoryPath: Path.Combine(outputs, "live-notification-history-"+Guid.NewGuid().ToString("N")+".json"));
            var liveSource = new App.FeedSource { Title = new Uri(liveUrl).Host, Url = liveUrl };
            var liveConfiguration = new App.FeedWidgetSettings { Enabled = true, Sources = [liveSource] };
            live.Configure(liveConfiguration); await live.RefreshAsync(true);
            check(live.Snapshots[liveSource.Id].Error == null && live.Items(null).Any(), "Live feed feed loads and parses");
            check(live.Items(null).Any(pair => pair.Entry.ImageUrl != null) && live.Items(null).All(pair => App.FeedParser.WebUri(pair.Entry.Link) != null), "Live feed thumbnails and canonical article links");
            using var liveWidget = new App.FeedWidgetForm(live,liveConfiguration,"de",_=>{},_=>{},()=>{},()=>{});
            using (var first = liveWidget.RenderBitmap()) { }
            var tasks = (List<Task>)typeof(App.FeedWidgetForm).GetField("imageTasks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(liveWidget)!;
            await Task.WhenAll(tasks.ToArray());
            var images = (Dictionary<string,Bitmap>)typeof(App.FeedWidgetForm).GetField("images", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(liveWidget)!;
            check(images.Count > 0, "Live feed cover images download and decode");
            foreach (var mode in Enum.GetValues<App.FeedDisplayMode>())
            {
                liveConfiguration.Display = mode; liveWidget.Apply(liveConfiguration,"de"); using var bitmap = liveWidget.RenderBitmap(); bitmap.Save(Path.Combine(outputs,new Uri(liveUrl).Host+"-"+mode+".png"));
                check(bitmap.Height <= liveConfiguration.MaximumHeight, "Live feed viewport "+mode);
            }
            Console.WriteLine($"Live feed: {live.Items(null).Count()} entries; {images.Count} cached covers");
        }
        using var oversized = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('x',100)) };
        try { await App.FeedService.ReadBoundedAsync(oversized,10,CancellationToken.None); check(false,"Response limit"); } catch (InvalidDataException) { check(true,"Response size limit"); }
    }
}
