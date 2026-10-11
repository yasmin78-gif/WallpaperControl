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
        try { var file=await store.FetchAsync(url); using var image=App.LauncherWebsiteIcons.Read(file); if(image==null)throw new Exception("Live favicon missing or invalid: "+url); Console.WriteLine($"Live fetch: host={new Uri(url).Host}; decoded={image.Size}; cached={System.IO.File.Exists(file)}"); }
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

