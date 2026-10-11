extern alias WallpaperApp;
using App=WallpaperApp::WallpaperControl;
using System.Drawing.Imaging;
using System.Net;
using System.Text;
using System.Text.Json;

internal static class LauncherWebsiteIconTests
{
    internal static void Probe(string url,string sample)=>Task.Run(async ()=>
    {
        try { using var image=await App.LauncherWebsiteIcons.DecodeAsync(await System.IO.File.ReadAllBytesAsync(sample)); Console.WriteLine("Downloaded ICO decoded: "+image.Size); }
        catch(Exception ex) { Console.WriteLine("Decode failure: "+ex); }
        string root=Path.Combine(Path.GetTempPath(),"WallpaperControl.FaviconProbe-"+Guid.NewGuid().ToString("N"));
        using(var client=new HttpClient(new HttpClientHandler { AllowAutoRedirect=false,UseCookies=false }) { Timeout=TimeSpan.FromSeconds(3) })
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WallpaperControl/WebsiteIcons");
            using var response=await client.GetAsync(new Uri(new Uri(url),"/favicon.ico"));
            Console.WriteLine($"Primary HTTP: status={(int)response.StatusCode}; version={response.Version}; cf-mitigated={(response.Headers.TryGetValues("cf-mitigated",out var values)?string.Join(",",values):"none")}");
        }
        using var store=new App.LauncherWebsiteIcons(root);
        try { string name=App.LauncherWebsiteNames.Host(new Uri(url)); var file=await store.FetchAsync(url,pageName:value=>name=value); using var image=App.LauncherWebsiteIcons.Read(file); if(image==null)throw new Exception("Live favicon missing or invalid: "+url); Console.WriteLine($"Live fetch: host={new Uri(url).Host}; name={name}; decoded={image.Size}; cached={System.IO.File.Exists(file)}"); }
        finally { if(Directory.Exists(root))Directory.Delete(root,true); }
    }).GetAwaiter().GetResult();
    internal static void Run(Action<bool,string> check)
    {
        Task.Run(()=>RunAsync(check)).GetAwaiter().GetResult();
        Exception? error=null;
        var thread=new Thread(()=> { try { DialogChecks(check); } catch(Exception ex) { error=ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if(error!=null)throw error;
    }
    private static void DialogChecks(Action<bool,string> check)
    {
        PreviewChecks(check);
        NameChecks(check);
        foreach(var style in Enum.GetValues<App.SystemWidgetStyle>())foreach(bool dark in new[]{true,false})
        {
            using var web=new App.LauncherEntryForm(new App.LauncherEntry(Guid.NewGuid(),"Example","https://example.test/"),"de",style,dark);
            web.Show(); Application.DoEvents();
            check(web.Controls.OfType<PictureBox>().Single().Visible&&web.Controls.OfType<Button>().Any(b=>b.Visible&&b.Text.Contains("Eigenes Symbol")),"Website-only icon controls visible: "+style+" dark="+dark);
            var target=web.Controls.OfType<TextBox>().Single(t=>t.Text=="https://example.test/"); target.Text=Environment.ProcessPath!;
            check(!web.Controls.OfType<PictureBox>().Single().Visible,"Changing target to program hides website controls"); web.Close();
        }
        using var program=new App.LauncherEntryForm(new App.LauncherEntry(Guid.NewGuid(),"Program",Environment.ProcessPath!),"de",App.SystemWidgetStyle.Glow,true);
        program.Show(); Application.DoEvents();
        check(!program.Controls.OfType<PictureBox>().Single().Visible&&program.Controls.OfType<Label>().Any(l=>l.Visible&&l.Text.Contains("Argumente")),"Program dialog retains arguments and no icon selection"); program.Close();
        string registry=@"Software\WallpaperControl.WebsiteIconChecks-"+Guid.NewGuid().ToString("N");
        try
        {
            var entry=new App.LauncherEntry(Guid.NewGuid(),"Example","https://example.test/");
            new App.WidgetSettings { Launcher=new() { Entries=[entry] } }.Save(registry);
            using var manager=new App.WidgetManager(()=>{},registryPath:registry);
            manager.SaveLauncherEntries([entry with { Favicon="cached.png",FaviconFor=entry.Target }]);
            manager.SaveLauncherEntries([entry with { Name="Renamed from older editor" }]);
            check(manager.LauncherEntries.Single().Favicon=="cached.png","Saving older editor snapshot retains favicon completed in background");
            check(App.WidgetSettings.Load(registry).Launcher.Entries.Single().Favicon=="cached.png","Background favicon survives persisted editor update and reload");
        }
        finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(registry,false); }
    }
    private static void PreviewChecks(Action<bool,string> check)
    {
        string root=Path.Combine(Path.GetTempPath(),"WallpaperControl.PreviewChecks-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var requested=new List<string>(); byte[] png=ImageBytes(ImageFormat.Png);
        var delayed=new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var manualDelay=new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closeDelay=new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client=new HttpClient(new Handler((request,token)=>
        {
            string host=request.RequestUri!.Host; requested.Add(host);
            if(host=="slow.test")return delayed.Task;
            if(host=="slow-manual.test")return manualDelay.Task;
            if(host=="slow-close.test")return closeDelay.Task;
            return Task.FromResult(host=="failed.test"?new HttpResponseMessage(HttpStatusCode.NotFound):request.RequestUri.AbsolutePath=="/favicon.ico"?Response(png):Response(Encoding.UTF8.GetBytes("<html></html>")));
        }));
        static void PumpUntil(Func<bool> condition)
        { var watch=System.Diagnostics.Stopwatch.StartNew(); while(!condition()&&watch.ElapsedMilliseconds<5000) { Application.DoEvents(); Thread.Sleep(10); } if(!condition())throw new Exception("Preview did not reach expected state."); }
        static void Pump(int milliseconds)
        { var watch=System.Diagnostics.Stopwatch.StartNew(); while(watch.ElapsedMilliseconds<milliseconds) { Application.DoEvents(); Thread.Sleep(10); } }
        try
        {
            using var store=new App.LauncherWebsiteIcons(Path.Combine(root,"icons"),client);
            using var dialog=new App.LauncherEntryForm(null,"de",App.SystemWidgetStyle.Glow,true,websiteIcons:store);
            dialog.Show(); Application.DoEvents();
            var target=dialog.Controls.OfType<TextBox>().Single(t=>t.Top==102);
            var preview=dialog.Controls.OfType<PictureBox>().Single();
            var reset=dialog.Controls.OfType<Button>().Single(b=>b.Text.Contains("Website-Symbol"));
            target.Text="https://unused.test/"; target.Text="https://new.test/";
            check(preview.Image==null,"New URL initially shows globe while preview loads");
            PumpUntil(()=>preview.Image!=null);
            check(!requested.Contains("unused.test")&&requested.Contains("new.test"),"Typing debounce downloads only final URL and updates new-entry preview");
            int before=requested.Count; reset.PerformClick(); Application.DoEvents();
            check(preview.Image!=null&&requested.Count==before,"Website reset immediately reuses loaded favicon without HTTP");
            target.Text="https://failed.test/"; PumpUntil(()=>requested.Count>=before+2); Pump(100);
            check(preview.Image==null,"Failed website download retains globe");
            target.Text="https://new.test/";
            check(preview.Image!=null,"Returning to already loaded URL immediately reuses dialog cache");
            string source=Path.Combine(root,"manual.png"); System.IO.File.WriteAllBytes(source,png);
            var import=dialog.SelectCustomIconAsync(source); PumpUntil(()=>import.IsCompleted); import.GetAwaiter().GetResult();
            check(preview.Image!=null,"Manual selection updates preview immediately after import");
            target.Text="https://manual.test/"; Pump(500);
            check(requested.Count(host=>host=="manual.test")==1&&preview.Image!=null,"Manual icon prevents automatic icon download while website name can load");
            target.Text="https://new.test/"; reset.PerformClick();
            check(preview.Image!=null,"Switching manual icon back to favicon immediately restores cached preview");
            target.Text="https://slow.test/"; PumpUntil(()=>requested.Contains("slow.test"));
            target.Text="https://final.test/"; delayed.TrySetResult(Response(png));
            PumpUntil(()=>preview.Image!=null&&requested.Contains("final.test"));
            check(preview.Image!=null,"Late previous-URL result cannot replace current URL preview");
            target.Text="not a URL"; check(preview.Image==null,"Invalid URL clears preview to fallback");
            target.Text="https://final.test/";
            dialog.Controls.OfType<Button>().Single(b=>b.Text=="Speichern").PerformClick();
            check(dialog.Result?.FaviconFor=="https://final.test/"&&App.WidgetManager.HasValidLauncherFavicon(dialog.Result),"Save preserves exactly the automatic favicon shown in preview");
            dialog.Dispose(); Pump(100);
            check(Directory.GetFiles(Path.Combine(root,"icons")).Length==1,"Closing cleans unused preview/custom downloads and retains saved favicon");
            requested.Clear();
            using var cachedStore=new App.LauncherWebsiteIcons(Path.Combine(root,"cached"),new HttpClient(new Handler((_,_)=>throw new Exception("Cached preview must not download"))));
            using var edit=new App.LauncherEntryForm(dialog.Result,"de",App.SystemWidgetStyle.Clean,false,websiteIcons:cachedStore);
            edit.Show(); Application.DoEvents();
            check(edit.Controls.OfType<PictureBox>().Single().Image!=null,"Editing existing entry displays stored favicon immediately");
            edit.Close();
            using(var manualStore=new App.LauncherWebsiteIcons(Path.Combine(root,"manual-race"),new HttpClient(new Handler((request,_)=> { requested.Add(request.RequestUri!.Host); return manualDelay.Task; }))))
            using(var manualDialog=new App.LauncherEntryForm(null,"de",App.SystemWidgetStyle.Glow,true,"https://slow-manual.test/",websiteIcons:manualStore))
            {
                manualDialog.Show(); PumpUntil(()=>requested.Contains("slow-manual.test"));
                string blueFile=Path.Combine(root,"blue.png"); using(var blue=new Bitmap(16,16)) { using(var g=Graphics.FromImage(blue))g.Clear(Color.Blue); blue.Save(blueFile,ImageFormat.Png); }
                var selection=manualDialog.SelectCustomIconAsync(blueFile); PumpUntil(()=>selection.IsCompleted); selection.GetAwaiter().GetResult();
                manualDelay.TrySetResult(Response(png)); Pump(200);
                var image=(Bitmap)manualDialog.Controls.OfType<PictureBox>().Single().Image!;
                check(image.GetPixel(32,32).B>240&&image.GetPixel(32,32).R<10,"Late automatic result never overwrites a newly selected manual image");
                manualDialog.Close();
            }
            using(var closingStore=new App.LauncherWebsiteIcons(Path.Combine(root,"close-race"),new HttpClient(new Handler((request,_)=> { requested.Add(request.RequestUri!.Host); return closeDelay.Task; }))))
            using(var closing=new App.LauncherEntryForm(null,"de",App.SystemWidgetStyle.Glow,true,"https://slow-close.test/",websiteIcons:closingStore))
            {
                closing.Show(); PumpUntil(()=>requested.Contains("slow-close.test")); closing.Close(); closeDelay.TrySetResult(Response(png)); Pump(200);
                check(!Directory.Exists(Path.Combine(root,"close-race"))||Directory.GetFiles(Path.Combine(root,"close-race")).Length==0,"Closing during download prevents preview updates and orphaned icon files");
            }
        }
        finally { Directory.Delete(root,true); }
    }
    private static void NameChecks(Action<bool,string> check)
    {
        var basis=new Uri("https://www.example.test/");
        foreach(var sample in new[]{
            ("<title>Title</title><meta name='application-name' content='App'><meta content='Site &amp; Co' property='og:site_name'>","Site & Co"),
            ("<meta name=application-name content=Application><title>Title</title>","Application"),
            ("<title>Example - Official Website</title>","Example"),
            ("<title>Home | Example</title>","Example"),
            ("<title>Documentation - Example</title>","Documentation - Example"),
            ("<title>  Café &amp; Co\n</title>","Café & Co"),
            ("<!--<meta property='og:site_name' content='Fake'>--><script><meta property='og:site_name' content='Fake'></script><title>Real</title>","Real"),
            ("<title>Just a moment...</title>","example.test"),
            ("<title>Home</title>","example.test"),
            ("<meta property='og:site_name' content=''><title>Useful</title>","Useful"),
            ("<html></html>","example.test")})
            check(App.LauncherWebsiteNames.FromHtml(sample.Item1,basis)==sample.Item2,"Website name parser: "+sample.Item2);
        string root=Path.Combine(Path.GetTempPath(),"WallpaperControl.NameChecks-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string cached=Path.Combine(root,"cached.png"); using(var image=new Bitmap(128,128))image.Save(cached,ImageFormat.Png);
        var slow=new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously); var requested=new List<string>();
        var manualSlow=new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        static void PumpUntil(Func<bool> condition)
        { var watch=System.Diagnostics.Stopwatch.StartNew(); while(!condition()&&watch.ElapsedMilliseconds<5000) { Application.DoEvents(); Thread.Sleep(10); } if(!condition())throw new Exception("Name lookup did not complete."); }
        static void Pump(int milliseconds)
        { var watch=System.Diagnostics.Stopwatch.StartNew(); while(watch.ElapsedMilliseconds<milliseconds) { Application.DoEvents(); Thread.Sleep(10); } }
        App.LauncherEntryForm Create(App.LauncherEntry? entry=null)
        {
            var client=new HttpClient(new Handler((request,token)=>
            {
                string host=request.RequestUri!.Host; requested.Add(host);
                if(host=="slow.test")return slow.Task;
                if(host=="manual-slow.test")return manualSlow.Task;
                if(host=="timeout.test")return Task.Delay(Timeout.Infinite,token).ContinueWith<HttpResponseMessage>(_=>throw new OperationCanceledException(token),TaskScheduler.Default);
                if(host=="failed.test")throw new HttpRequestException("Offline");
                string html=host=="og.test"?"<meta property='og:site_name' content='Website Name'>":host=="title.test"?"<title>Title Site - Official Website</title>":"<html></html>";
                return Task.FromResult(Response(Encoding.UTF8.GetBytes(html)));
            }));
            var entries=new[]{"og.test","title.test","bare.test","slow.test","manual-slow.test","timeout.test","failed.test"}.Select(host=>new App.LauncherEntry(Guid.NewGuid(),"Cached","https://"+host+"/",Favicon:cached,FaviconFor:"https://"+host+"/")).ToArray();
            return new(entry,"de",App.SystemWidgetStyle.Glow,true,cachedEntries:entries,websiteIcons:new(Path.Combine(root,Guid.NewGuid().ToString("N")),client));
        }
        try
        {
            using(var dialog=Create())
            {
                dialog.Show(); var name=dialog.Controls.OfType<TextBox>().Single(t=>t.Top==42); var target=dialog.Controls.OfType<TextBox>().Single(t=>t.Top==102);
                target.Text="https://og.test/"; check(name.Text=="og.test","New entry immediately receives hostname fallback"); PumpUntil(()=>name.Text=="Website Name");
                check(requested.Count(host=>host=="og.test")==1,"Cached icon plus name uses only one HTML request, no icon download");
                target.Text="https://title.test/"; PumpUntil(()=>name.Text=="Title Site"); check(name.Text=="Title Site","Untouched automatic name follows URL change");
                target.Text="https://bare.test/"; PumpUntil(()=>requested.Contains("bare.test")); Pump(50); check(name.Text=="bare.test","Missing metadata falls back to hostname");
                target.Text="https://slow.test/"; PumpUntil(()=>requested.Contains("slow.test")); target.Text="https://og.test/"; slow.TrySetResult(Response(Encoding.UTF8.GetBytes("<meta property='og:site_name' content='Old Name'>"))); Pump(100);
                check(name.Text=="Website Name","Late prior URL response cannot replace automatic name");
                name.Text="My choice"; target.Text="https://title.test/"; Pump(500); check(name.Text=="My choice","User-edited name stays unchanged across URL changes");
                name.Text=""; target.Text="https://og.test/"; Pump(500); check(name.Text=="","Deliberately cleared name is never automatically filled again");
                name.Text="Saved custom name"; dialog.Controls.OfType<Button>().Single(b=>b.Text=="Speichern").PerformClick(); check(dialog.Result?.Name=="Saved custom name","Save preserves manually entered name"); dialog.Close();
            }
            var existing=new App.LauncherEntry(Guid.NewGuid(),"Existing custom name","https://og.test/",Favicon:cached,FaviconFor:"https://og.test/");
            using(var edit=Create(existing))
            { edit.Show(); var name=edit.Controls.OfType<TextBox>().Single(t=>t.Top==42); edit.Controls.OfType<TextBox>().Single(t=>t.Top==102).Text="https://title.test/"; Pump(500); check(name.Text=="Existing custom name","Editing always preserves existing name"); edit.Close(); check(edit.Result==null,"Cancel does not create or save a renamed entry"); }
            using(var failed=Create())
            { failed.Show(); var name=failed.Controls.OfType<TextBox>().Single(t=>t.Top==42); failed.Controls.OfType<TextBox>().Single(t=>t.Top==102).Text="https://failed.test/"; PumpUntil(()=>requested.Contains("failed.test")); Pump(50); check(name.Text=="failed.test","Network error retains automatic hostname fallback"); failed.Close(); }
            using(var manual=Create())
            { manual.Show(); var name=manual.Controls.OfType<TextBox>().Single(t=>t.Top==42); manual.Controls.OfType<TextBox>().Single(t=>t.Top==102).Text="https://manual-slow.test/"; PumpUntil(()=>requested.Contains("manual-slow.test")); name.Text=""; manualSlow.TrySetResult(Response(Encoding.UTF8.GetBytes("<title>Late website name</title>"))); Pump(100); check(name.Text=="","Pending metadata cannot overwrite a deliberate empty user edit"); manual.Close(); }
            using(var timeout=Create())
            { timeout.Show(); var name=timeout.Controls.OfType<TextBox>().Single(t=>t.Top==42); timeout.Controls.OfType<TextBox>().Single(t=>t.Top==102).Text="https://timeout.test/"; PumpUntil(()=>requested.Contains("timeout.test")); Pump(3200); check(name.Text=="timeout.test","Slow server times out without blocking or replacing fallback name"); timeout.Close(); }
        }
        finally { Directory.Delete(root,true); }
    }
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>send(request,cancellationToken); }
    private static HttpResponseMessage Response(byte[] data)=>new(HttpStatusCode.OK) { Content=new ByteArrayContent(data) };
    private static byte[] ImageBytes(ImageFormat format)
    { using var image=new Bitmap(64,32); using(var g=Graphics.FromImage(image))g.Clear(Color.Red); using var output=new MemoryStream(); image.Save(output,format); return output.ToArray(); }
    private static async Task RunAsync(Action<bool,string> check)
    {
        string root=Path.Combine(Path.GetTempPath(),"WallpaperControl.WebsiteIcons-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            byte[] png=ImageBytes(ImageFormat.Png),jpeg=ImageBytes(ImageFormat.Jpeg),webp=Convert.FromBase64String("UklGRhwAAABXRUJQVlA4TA8AAAAvD8ABAAcQ/Y/+ByKi/wEA");
            using var icoStream=new MemoryStream(); SystemIcons.Information.Save(icoStream);
            foreach(var pair in new[]{("PNG",png),("JPG",jpeg),("ICO",icoStream.ToArray()),("WEBP",webp)})
            { using var bitmap=await App.LauncherWebsiteIcons.DecodeAsync(pair.Item2); check(bitmap.Size==new Size(128,128),pair.Item1+" decodes through Windows into normalized icon"); }
            using(var bitmap=await App.LauncherWebsiteIcons.DecodeAsync(png))
            { check(bitmap.GetPixel(64,0).A==0&&bitmap.GetPixel(64,64).R>240,"Proportional scaling retains transparent padding and image colors"); }
            foreach(var bytes in new[]{Encoding.UTF8.GetBytes("<svg>invalid image</svg>"),new byte[App.LauncherWebsiteIcons.MaximumImageBytes+1],png[..16]})
            { bool rejected=false; try { using var image=await App.LauncherWebsiteIcons.DecodeAsync(bytes); } catch { rejected=true; } check(rejected,"Unsupported, oversized or corrupt image rejected"); }
            var candidates=App.LauncherWebsiteIcons.Candidates("<link rel='icon' href='/small.png' sizes='16x16'><link rel='shortcut icon' href='large.png?a=1&amp;b=2' sizes='256x256'><link rel='icon' href='file:///bad.ico'><link rel='icon' href='/vector.svg'>",new Uri("https://example.test/path/"));
            check(candidates.Count==2&&candidates[0].AbsoluteUri=="https://example.test/path/large.png?a=1&b=2","High resolution, relative URLs, HTML entities and HTTP-only candidates");
            var requests=new List<string>();
            using var client=new HttpClient(new Handler((request,token)=>
            { var path=request.RequestUri!.AbsolutePath; requests.Add(path); return Task.FromResult(path switch { "/"=>Response(Encoding.UTF8.GetBytes("<link rel='icon' sizes='16x16' href='/small.png'><link rel='icon' sizes='256x256' href='/large.png'>")),"/large.png"=>Response(png),"/favicon.ico"=>Response(icoStream.ToArray()),_=>new HttpResponseMessage(HttpStatusCode.NotFound) }); }));
            using var store=new App.LauncherWebsiteIcons(Path.Combine(root,"icons"),client);
            string? favicon=await store.FetchAsync("https://example.test/");
            check(favicon!=null&&store.Owns(favicon)&&System.IO.File.Exists(favicon),"New website favicon saved under unique owned filename");
            check(requests.SequenceEqual(new[]{"/","/large.png"}),"Highest-resolution HTML icon downloaded first");
            var source=Path.Combine(root,"custom.png"); await System.IO.File.WriteAllBytesAsync(source,png);
            string custom=await store.ImportAsync(source); System.IO.File.Delete(source);
            using(var bitmap=App.LauncherWebsiteIcons.Read(custom))check(bitmap!=null,"Custom icon survives deletion of original image");
            var entry=new App.LauncherEntry(Guid.NewGuid(),"Website","https://example.test/",CustomIcon:custom,Favicon:favicon,FaviconFor:"https://example.test/");
            check(entry.WebsiteIcon==custom,"Manual image takes precedence");
            check((entry with { CustomIcon=null }).WebsiteIcon==favicon,"Reset immediately reuses cached favicon");
            check(App.WidgetManager.HasValidLauncherFavicon(entry),"Only decodable matching cached icon is valid");
            check(!App.WidgetManager.HasValidLauncherFavicon(entry with { Favicon=externalPath() }),"Existing corrupt cache cannot suppress a new attempt");
            string externalPath() { string path=Path.Combine(root,"corrupt.png"); System.IO.File.WriteAllText(path,"not an image"); return path; }
            check((entry with { CustomIcon=null,Target="https://other.test/" }).WebsiteIcon==null,"Changed address does not show previous website favicon");
            check((entry with { Target="https://other.test/" }).WebsiteIcon==custom,"Changed address retains explicit custom icon");
            var restored=JsonSerializer.Deserialize<App.LauncherEntry>(JsonSerializer.Serialize(entry));
            check(restored==entry&&restored!.WebsiteIcon==custom,"Restart JSON preserves identity, target and both icons");
            var legacy=JsonSerializer.Deserialize<App.LauncherEntry>("{\"Id\":\""+entry.Id+"\",\"Name\":\"Old\",\"Target\":\"https://example.test/\",\"Arguments\":\"\",\"ManagedShortcut\":false}");
            check(legacy!=null&&legacy.CustomIcon==null&&legacy.Favicon==null,"Existing entries load without new icon metadata");
            store.DeleteUnused([custom,favicon],[entry]); check(System.IO.File.Exists(custom)&&System.IO.File.Exists(favicon),"Referenced cache files are retained");
            store.DeleteUnused([custom],[entry with { Id=Guid.NewGuid() }]); check(System.IO.File.Exists(custom),"Shared icon survives while another entry references it");
            var external=Path.Combine(root,"external.png"); await System.IO.File.WriteAllBytesAsync(external,png); store.DeleteUnused([external],[]); check(System.IO.File.Exists(external),"Cleanup never removes unowned images");
            using(var offlineClient=new HttpClient(new Handler((_,_)=>throw new HttpRequestException("Offline"))))
            using(var offline=new App.LauncherWebsiteIcons(Path.Combine(root,"icons"),offlineClient))
            { check(await offline.FetchAsync("https://example.test/")==null,"Offline download safely falls back"); using var cached=App.LauncherWebsiteIcons.Read(favicon); check(cached!=null,"Previously downloaded icon remains readable offline after restart"); }
            requests.Clear(); string? fallback=await store.FetchAsync("https://example.test/no-html"); check(fallback!=null&&requests.Contains("/favicon.ico"),"Root favicon fallback works without HTML links");
            foreach(var status in new[]{HttpStatusCode.Forbidden,HttpStatusCode.Unauthorized,HttpStatusCode.NotFound,HttpStatusCode.InternalServerError,HttpStatusCode.OK})
            {
                int calls=0;
                using var sourceClient=new HttpClient(new Handler((_,_)=>Task.FromResult(status==HttpStatusCode.OK?Response(png):new HttpResponseMessage(status))));
                using var withFallback=new App.LauncherWebsiteIcons(Path.Combine(root,"status-"+(int)status),sourceClient,(url,limit,token)=> { calls++; return Task.FromResult<HttpResponseMessage?>(Response(png)); });
                string? icon=await withFallback.FetchAsync("https://example.test/");
                check(calls==(status==HttpStatusCode.Forbidden?1:0),"curl attempted only once for icon HTTP403, never page/auth/other status: "+status);
                check((icon!=null)==(status is HttpStatusCode.Forbidden or HttpStatusCode.OK),"Successful primary/fallback response retains normal validation: "+status);
            }
            using(var sourceClient=new HttpClient(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)))))
            {
                int calls=0;
                using var credentialUrl=new App.LauncherWebsiteIcons(Path.Combine(root,"credentials"),sourceClient,(_,_,_)=> { calls++; return Task.FromResult<HttpResponseMessage?>(Response(png)); });
                check(await credentialUrl.FetchAsync("https://user:password@example.test/")==null&&calls==0,"Credential-bearing URLs never invoke curl fallback");
            }
            using(var sourceClient=new HttpClient(new Handler((_,_)=> { var response=new HttpResponseMessage(HttpStatusCode.Forbidden); response.Headers.WwwAuthenticate.ParseAdd("Basic realm=private"); return Task.FromResult(response); })))
            {
                int calls=0;
                using var authChallenge=new App.LauncherWebsiteIcons(Path.Combine(root,"auth-challenge"),sourceClient,(_,_,_)=> { calls++; return Task.FromResult<HttpResponseMessage?>(Response(png)); });
                check(await authChallenge.FetchAsync("https://example.test/")==null&&calls==0,"Explicit authentication challenges never invoke fallback even with HTTP403");
            }
            using(var sourceClient=new HttpClient(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)))))
            using(var blockedRedirect=new App.LauncherWebsiteIcons(Path.Combine(root,"curl-redirect"),sourceClient,(_,_,_)=>Task.FromResult<HttpResponseMessage?>(new HttpResponseMessage(HttpStatusCode.Redirect) { Headers={ Location=new Uri("file:///C:/blocked") } })))
                check(await blockedRedirect.FetchAsync("https://example.test/")==null,"Curl redirect retains HTTP/HTTPS-only validation");
            using(var sourceClient=new HttpClient(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)))))
            using(var oversizedFallback=new App.LauncherWebsiteIcons(Path.Combine(root,"curl-large"),sourceClient,(_,_,_)=>Task.FromResult<HttpResponseMessage?>(Response(new byte[App.LauncherWebsiteIcons.MaximumImageBytes+1]))))
                check(await oversizedFallback.FetchAsync("https://example.test/")==null,"Curl fallback retains image byte limit");
            var start=App.LauncherCurlIcons.StartInfo(new Uri("https://example.test/icon?x=1&y=2"),1024);
            check(!start.UseShellExecute&&start.CreateNoWindow&&start.WindowStyle==System.Diagnostics.ProcessWindowStyle.Hidden&&start.FileName==Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"curl.exe"),"Only Windows curl is started without shell or visible window");
            check(start.ArgumentList[0]=="-q"&&!start.ArgumentList.Contains("--location")&&!start.ArgumentList.Contains("--insecure")&&!start.ArgumentList.Contains("--cookie")&&start.ArgumentList.Last()=="https://example.test/icon?x=1&y=2","Curl config, automatic redirects, insecure TLS and cookies remain disabled; URL is separate argument");
            byte[] wire=Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\nHTTP/1.1 200 OK\r\nContent-Type: image/png\r\n\r\n").Concat(png).ToArray();
            using(var parsed=App.LauncherCurlIcons.ParseResponse(wire,png.Length))check(parsed?.StatusCode==HttpStatusCode.OK&&(await parsed.Content.ReadAsByteArrayAsync()).SequenceEqual(png),"Curl HTTP framing preserves exact binary image and interim response handling");
            check(App.LauncherCurlIcons.ParseResponse(wire,png.Length-1)==null,"Curl actual body size checked independently of headers");
            string? discovered=null; int htmlFallbacks=0;
            using(var forbiddenClient=new HttpClient(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)))))
            using(var htmlStore=new App.LauncherWebsiteIcons(Path.Combine(root,"html-curl"),forbiddenClient,(_,limit,_)=> { htmlFallbacks++; check(limit==1024*1024,"HTML curl fallback uses 1MiB bound"); return Task.FromResult<HttpResponseMessage?>(Response(Encoding.UTF8.GetBytes("<meta property='og:site_name' content='Public site'>"))); }))
            { await htmlStore.FetchAsync("https://example.test/",pageName:value=>discovered=value,downloadIcon:false); check(discovered=="Public site"&&htmlFallbacks==1,"Opt-in name lookup reuses safe HTTP403 curl fallback only once"); }
            using(var largeHtmlClient=new HttpClient(new Handler((_,_)=>Task.FromResult(Response(Encoding.UTF8.GetBytes("<meta property='og:site_name' content='Large site'>"+new string(' ',2*1024*1024)))))))
            using(var prefixStore=new App.LauncherWebsiteIcons(Path.Combine(root,"html-prefix"),largeHtmlClient))
            { discovered=null; await prefixStore.FetchAsync("https://example.test/",pageName:value=>discovered=value,downloadIcon:false); check(discovered=="Large site","Large HTML pages use only bounded prefix for metadata"); }
            var listener=new System.Net.Sockets.TcpListener(IPAddress.Loopback,0); listener.Start();
            try
            {
                using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(2));
                int port=((IPEndPoint)listener.LocalEndpoint).Port;
                var pending=App.LauncherCurlIcons.DownloadAsync(new Uri($"http://127.0.0.1:{port}/favicon.ico"),1024,cancel.Token);
                using var connection=await listener.AcceptTcpClientAsync(cancel.Token);
                cancel.Cancel(); bool cancelled=false; try { using var result=await pending; } catch(OperationCanceledException) { cancelled=true; }
                check(cancelled,"Native curl process terminates promptly on shared cancellation");
            }
            finally { listener.Stop(); }
            using(var missingClient=new HttpClient(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)))))
            using(var missing=new App.LauncherWebsiteIcons(Path.Combine(root,"missing"),missingClient))check(await missing.FetchAsync("https://example.test/")==null,"Website without favicon returns globe fallback");
            using(var redirectClient=new HttpClient(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect) { Headers={ Location=new Uri("file:///C:/bad") } }))))
            using(var redirect=new App.LauncherWebsiteIcons(Path.Combine(root,"redirect"),redirectClient))check(await redirect.FetchAsync("https://example.test/")==null,"Redirects to non-HTTP protocols rejected");
            using(var largeClient=new HttpClient(new Handler((request,_)=>Task.FromResult(request.RequestUri!.AbsolutePath=="/"?Response(Encoding.UTF8.GetBytes("<link rel='icon' href='/huge.png'>")):Response(new byte[App.LauncherWebsiteIcons.MaximumImageBytes+1])))))
            using(var large=new App.LauncherWebsiteIcons(Path.Combine(root,"large"),largeClient))check(await large.FetchAsync("https://example.test/")==null,"Oversized HTTP image rejected without storing files");
            foreach(string failure in new[]{"corrupt","unsupported","timeout"})
            {
                var tried=new List<string>();
                using var fallbackClient=new HttpClient(new Handler(async (request,token)=>
                {
                    string path=request.RequestUri!.AbsolutePath; tried.Add(path);
                    if(path=="/")return Response(Encoding.UTF8.GetBytes("<link rel='icon' sizes='256x256' href='/bad'><link rel='icon' href='/ignored.svg' type='image/svg+xml'>"));
                    if(path=="/favicon.ico")return Response(icoStream.ToArray());
                    if(failure=="timeout")await Task.Delay(Timeout.Infinite,token);
                    return Response(failure=="unsupported"?Encoding.UTF8.GetBytes("<svg>unsupported</svg>"):png[..16]);
                }));
                using var fallbackStore=new App.LauncherWebsiteIcons(Path.Combine(root,failure),fallbackClient);
                string? recovered=await fallbackStore.FetchAsync("https://example.test/");
                check(recovered!=null&&tried.SequenceEqual(new[]{"/","/bad","/favicon.ico"}),"Root favicon tried after "+failure+" candidate");
                check(Directory.GetFiles(Path.Combine(root,failure)).Length==1,"Failed "+failure+" candidate leaves no cached file");
            }
            using(var slowClient=new HttpClient(new Handler(async (_,token)=> { await Task.Delay(Timeout.Infinite,token); return Response(png); })))
            using(var slow=new App.LauncherWebsiteIcons(Path.Combine(root,"slow"),slowClient))
            { using var cancel=new CancellationTokenSource(100); var pending=slow.FetchAsync("https://example.test/",cancel.Token); check(!pending.IsCompleted,"Download yields without blocking caller"); check(await pending==null,"Cancelled pending download finishes safely"); }
            store.DeleteUnused([custom,favicon,fallback],[]); check(!System.IO.File.Exists(custom)&&!System.IO.File.Exists(favicon)&&!System.IO.File.Exists(fallback),"Unreferenced managed images removed");
        }
        finally { Directory.Delete(root,true); }
    }
}

