using System.Net;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
namespace WallpaperControl;

internal sealed class LauncherWebsiteIcons : IDisposable
{
    internal const int MaximumImageBytes=4*1024*1024;
    internal static string DefaultDirectory=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WallpaperControl","Launcher","Icons");
    private readonly string directory;
    private readonly HttpClient http;
    private readonly Func<Uri,int,CancellationToken,Task<HttpResponseMessage?>>? forbiddenIconFallback;
    private readonly SemaphoreSlim downloads=new(2);
    private readonly CancellationTokenSource lifetime=new();
    private bool disposed;
    private readonly object sync=new();
    private int active;
    private bool retired;
    internal LauncherWebsiteIcons(string directory,HttpClient? client=null,Func<Uri,int,CancellationToken,Task<HttpResponseMessage?>>? forbiddenIconFallback=null)
    {
        this.directory=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        http=client??new HttpClient(new HttpClientHandler { AllowAutoRedirect=false,UseCookies=false }) { Timeout=Timeout.InfiniteTimeSpan };
        this.forbiddenIconFallback=forbiddenIconFallback??(client==null?LauncherCurlIcons.DownloadAsync:null);
        if(client==null)http.DefaultRequestHeaders.UserAgent.ParseAdd("WallpaperControl/WebsiteIcons");
    }
    internal bool Owns(string? file)
    {
        if(string.IsNullOrEmpty(file))return false;
        try { return string.Equals(Path.GetDirectoryName(Path.GetFullPath(file)),directory,StringComparison.OrdinalIgnoreCase)&&Path.GetExtension(file).Equals(".png",StringComparison.OrdinalIgnoreCase)&&Guid.TryParseExact(Path.GetFileNameWithoutExtension(file),"N",out _); }
        catch(Exception ex) when(ex is ArgumentException or IOException or NotSupportedException) { return false; }
    }
    private void VerifyDirectory()
    { for(var item=new DirectoryInfo(directory);item!=null;item=item.Parent)if(item.Exists&&(item.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Redirected website icon storage."); }
    internal void DeleteUnused(IEnumerable<string?> candidates,IEnumerable<LauncherEntry> entries)
    {
        var referenced=entries.SelectMany(e=>new[]{e.CustomIcon,e.Favicon}).Where(s=>s!=null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var file in candidates.Where(Owns).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if(referenced.Contains(file))continue;
            try { VerifyDirectory(); var path=Path.GetFullPath(file!); if(System.IO.File.Exists(path)&&(System.IO.File.GetAttributes(path)&FileAttributes.ReparsePoint)==0)System.IO.File.Delete(path); }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { AppLogger.Info($"Launcher website icon cleanup failed: type={ex.GetType().Name}"); }
        }
    }
    internal static Bitmap? Read(string? file)
    {
        if(string.IsNullOrEmpty(file)||!System.IO.File.Exists(file))return null;
        try { using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete); if(stream.Length>MaximumImageBytes)return null; using var image=Image.FromStream(stream); if(image.Width>512||image.Height>512)return null; return new Bitmap(image); }
        catch(Exception ex) when(ex is IOException or ArgumentException or ExternalException or UnauthorizedAccessException) { return null; }
    }
    internal async Task<string> ImportAsync(string source,CancellationToken cancellation=default)
    {
        var extension=Path.GetExtension(source); if(!new[]{".png",".jpg",".jpeg",".ico",".webp"}.Contains(extension,StringComparer.OrdinalIgnoreCase))throw new InvalidDataException();
        using var stream=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read,8192,true);
        var bytes=await LimitedBytes(stream,MaximumImageBytes,cancellation);
        return await SaveImageAsync(bytes,cancellation);
    }
    private async Task<string> SaveImageAsync(byte[] bytes,CancellationToken cancellation)
    {
        using var bitmap=await DecodeAsync(bytes,cancellation); cancellation.ThrowIfCancellationRequested();
        VerifyDirectory(); Directory.CreateDirectory(directory); VerifyDirectory(); var file=Path.Combine(directory,Guid.NewGuid().ToString("N")+".png");
        try { using var output=new MemoryStream(); bitmap.Save(output,System.Drawing.Imaging.ImageFormat.Png); await System.IO.File.WriteAllBytesAsync(file,output.ToArray(),cancellation); return file; }
        catch { DeleteUnused([file],[]); throw; }
    }
    internal static async Task<Bitmap> DecodeAsync(byte[] bytes,CancellationToken cancellation=default)
    {
        if(bytes.Length<12||bytes.Length>MaximumImageBytes)throw new InvalidDataException();
        bool png=bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}),jpg=bytes[0]==255&&bytes[1]==216&&bytes[2]==255;
        bool ico=bytes[0]==0&&bytes[1]==0&&bytes[2]==1&&bytes[3]==0;
        bool webp=System.Text.Encoding.ASCII.GetString(bytes,0,4)=="RIFF"&&System.Text.Encoding.ASCII.GetString(bytes,8,4)=="WEBP";
        if(!png&&!jpg&&!ico&&!webp)throw new InvalidDataException();
        using var input=new MemoryStream(bytes); using var random=input.AsRandomAccessStream();
        var decoder=await BitmapDecoder.CreateAsync(random).AsTask(cancellation);
        var frame=await decoder.GetFrameAsync(0).AsTask(cancellation);
        if(ico)
            for(uint i=1;i<Math.Min(decoder.FrameCount,64);i++) { var candidate=await decoder.GetFrameAsync(i).AsTask(cancellation); if((long)candidate.PixelWidth*candidate.PixelHeight>(long)frame.PixelWidth*frame.PixelHeight)frame=candidate; }
        if(frame.PixelWidth==0||frame.PixelHeight==0||frame.PixelWidth>8192||frame.PixelHeight>8192||(long)frame.PixelWidth*frame.PixelHeight>16_000_000)throw new InvalidDataException();
        double scale=128d/Math.Max(frame.PixelWidth,frame.PixelHeight); uint width=Math.Max(1,(uint)Math.Round(frame.PixelWidth*scale)),height=Math.Max(1,(uint)Math.Round(frame.PixelHeight*scale));
        var pixels=await frame.GetPixelDataAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,new BitmapTransform { ScaledWidth=width,ScaledHeight=height,InterpolationMode=BitmapInterpolationMode.Fant },ExifOrientationMode.IgnoreExifOrientation,ColorManagementMode.ColorManageToSRgb).AsTask(cancellation);
        var data=pixels.DetachPixelData();
        if(data.Length!=(int)(width*height*4))throw new InvalidDataException();
        using var decoded=new Bitmap((int)width,(int)height,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        var bits=decoded.LockBits(new(0,0,decoded.Width,decoded.Height),System.Drawing.Imaging.ImageLockMode.WriteOnly,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        try { Marshal.Copy(data,0,bits.Scan0,data.Length); } finally { decoded.UnlockBits(bits); }
        var result=new Bitmap(128,128,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using(var graphics=Graphics.FromImage(result)) { graphics.Clear(Color.Transparent); graphics.DrawImage(decoded,(128-decoded.Width)/2,(128-decoded.Height)/2,decoded.Width,decoded.Height); }
        return result;
    }
    internal async Task<string?> FetchAsync(string website,CancellationToken cancellation=default)
    {
        CancellationTokenSource linked;
        lock(sync) { if(disposed)return null; active++; linked=CancellationTokenSource.CreateLinkedTokenSource(cancellation,lifetime.Token); }
        using var linkedScope=linked; bool entered=false;
        try
        {
            await downloads.WaitAsync(linked.Token); entered=true; linked.CancelAfter(TimeSpan.FromSeconds(35));
            if(!LauncherTargets.IsWebsite(website))return null;
            var basis=new Uri(website); List<Uri> icons=[];
            try { var page=await GetAsync(basis,1024*1024,linked.Token); if(page!=null) { basis=page.Value.Url; icons=Candidates(System.Text.Encoding.UTF8.GetString(page.Value.Bytes),basis); } }
            catch(Exception ex) when(!linked.IsCancellationRequested) { Diagnose(basis,"page",ex); }
            var fallback=new Uri(basis.GetLeftPart(UriPartial.Authority)+"/favicon.ico"); if(!icons.Contains(fallback))icons.Add(fallback);
            foreach(var icon in icons.Take(9))
            {
                using var candidate=CancellationTokenSource.CreateLinkedTokenSource(linked.Token); candidate.CancelAfter(TimeSpan.FromSeconds(3));
                try { var image=await GetAsync(icon,MaximumImageBytes,candidate.Token,iconDownload:true); if(image!=null) { var file=await SaveImageAsync(image.Value.Bytes,candidate.Token); AppLogger.Info($"Launcher favicon saved: host={icon.Host}; path={icon.AbsolutePath}"); return file; } }
                catch(Exception ex) when(!linked.IsCancellationRequested) { Diagnose(icon,"candidate",ex); }
            }
            AppLogger.Info($"Launcher favicon unavailable: host={basis.Host}; reason=no-valid-candidate; cached=false");
        }
        catch(Exception ex)
        { if(!disposed)AppLogger.Info($"Launcher favicon unavailable: type={ex.GetType().Name}"); }
        finally { if(entered)downloads.Release(); lock(sync) { active--; Retire(); } }
        return null;
    }
    internal static List<Uri> Candidates(string html,Uri basis)
    {
        var values=new List<(Uri Url,int Score)>();
        foreach(Match link in Regex.Matches(html,@"<link\b[^>]{0,8192}>",RegexOptions.IgnoreCase,TimeSpan.FromMilliseconds(150)))
        {
            var attributes=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach(Match attribute in Regex.Matches(link.Value,"([\\w:-]+)\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))",RegexOptions.None,TimeSpan.FromMilliseconds(150)))
                attributes[attribute.Groups[1].Value]=WebUtility.HtmlDecode(attribute.Groups[2].Success?attribute.Groups[2].Value:attribute.Groups[3].Success?attribute.Groups[3].Value:attribute.Groups[4].Value);
            if(!attributes.TryGetValue("rel",out var rel)||!rel.Split(' ',StringSplitOptions.RemoveEmptyEntries).Any(s=>s.Equals("icon",StringComparison.OrdinalIgnoreCase)||s.Equals("apple-touch-icon",StringComparison.OrdinalIgnoreCase)||s.Equals("apple-touch-icon-precomposed",StringComparison.OrdinalIgnoreCase)))continue;
            if(!attributes.TryGetValue("href",out var href)||!Uri.TryCreate(basis,href,out var url)||!LauncherTargets.IsWebsite(url.AbsoluteUri)||url.AbsolutePath.EndsWith(".svg",StringComparison.OrdinalIgnoreCase)||attributes.GetValueOrDefault("type","").Contains("svg",StringComparison.OrdinalIgnoreCase))continue;
            int score=rel.Contains("apple",StringComparison.OrdinalIgnoreCase)?180:32;
            foreach(Match size in Regex.Matches(attributes.GetValueOrDefault("sizes",""),@"(\d{1,4})x(\d{1,4})",RegexOptions.IgnoreCase,TimeSpan.FromMilliseconds(100)))score=Math.Max(score,Math.Min(1024,Math.Min(int.Parse(size.Groups[1].Value),int.Parse(size.Groups[2].Value))));
            values.Add((url,score)); if(values.Count>=40)break;
        }
        return values.OrderByDescending(v=>v.Score).Select(v=>v.Url).Distinct().Take(8).ToList();
    }
    private async Task<(byte[] Bytes,Uri Url)?> GetAsync(Uri url,int limit,CancellationToken token,bool iconDownload=false)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(3)); token=deadline.Token;
        var visited=new HashSet<Uri>();
        for(int redirects=0;redirects<=5;redirects++)
        {
            if(!LauncherTargets.IsWebsite(url.AbsoluteUri)||!visited.Add(url))return null;
            using var response=await http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,token);
            bool challenge=response.Headers.TryGetValues("cf-mitigated",out var mitigation)&&mitigation.Contains("challenge",StringComparer.OrdinalIgnoreCase);
            AppLogger.Info($"Launcher favicon HTTP: host={url.Host}; path={url.AbsolutePath}; status={(int)response.StatusCode}; contentType={response.Content.Headers.ContentType?.MediaType}; bytes={response.Content.Headers.ContentLength}; challenge={challenge}");
            using var fallback=response.StatusCode==HttpStatusCode.Forbidden&&response.Headers.WwwAuthenticate.Count==0&&iconDownload&&forbiddenIconFallback!=null&&string.IsNullOrEmpty(url.UserInfo)
                ?await forbiddenIconFallback(url,limit,token):null;
            var actual=fallback??response;
            if((int)actual.StatusCode is >=300 and <400) { if(actual.Headers.Location==null)return null; url=new Uri(url,actual.Headers.Location); continue; }
            if(!actual.IsSuccessStatusCode||actual.Content.Headers.ContentLength>limit)return null;
            using var stream=await actual.Content.ReadAsStreamAsync(token); return (await LimitedBytes(stream,limit,token),url);
        }
        return null;
    }
    private static void Diagnose(Uri url,string stage,Exception error)=>AppLogger.Info($"Launcher favicon failure: stage={stage}; host={url.Host}; path={url.AbsolutePath}; type={error.GetType().Name}; hresult=0x{error.HResult:X8}");
    private static async Task<byte[]> LimitedBytes(Stream stream,int limit,CancellationToken token)
    {
        using var output=new MemoryStream(); var buffer=new byte[8192]; int count;
        while((count=await stream.ReadAsync(buffer,token))>0) { if(output.Length+count>limit)throw new InvalidDataException(); await output.WriteAsync(buffer.AsMemory(0,count),token); } return output.ToArray();
    }
    private void Retire() { if(disposed&&active==0&&!retired) { retired=true; lifetime.Dispose(); downloads.Dispose(); } }
    public void Dispose() { lock(sync) { if(disposed)return; disposed=true; lifetime.Cancel(); http.Dispose(); Retire(); } }
}
