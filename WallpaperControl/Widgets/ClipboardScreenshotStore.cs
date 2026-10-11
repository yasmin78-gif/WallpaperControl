using System.Text.Json;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace WallpaperControl;

internal sealed class ClipboardScreenshotSnapshot : IDisposable
{
    internal List<ClipboardScreenshot> Entries { get; }=[];
    internal Dictionary<Guid,Bitmap> Thumbnails { get; }=[];
    internal long Revision { get; set; }
    public void Dispose() { foreach(var image in Thumbnails.Values)image.Dispose(); Thumbnails.Clear(); }
}
internal sealed class ClipboardScreenshotStore : IDisposable
{
    internal static string DefaultDirectory=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WallpaperControl","Clipboard","Images");
    // Serialize disk mutations even across widget recreation/startup cleanup; never lock the UI thread.
    private static readonly object DiskLock=new();
    private readonly object queueLock=new();
    private Task tail=Task.CompletedTask;
    private readonly string directory;
    private List<ClipboardScreenshot> entries=[];
    private ClipboardWidgetSettings settings=new();
    private volatile bool disposed;
    private long revision;
    internal ClipboardScreenshotStore(string directory)=>this.directory=Path.GetFullPath(directory);
    internal string Original(Guid id)=>Path.Combine(directory,"screenshot-"+id.ToString("N")+".png");
    private string Thumbnail(Guid id)=>Path.Combine(directory,"screenshot-"+id.ToString("N")+".thumb.png");
    private string TemporaryMarker(Guid id)=>Original(id)+".session";
    private string Index=>Path.Combine(directory,"history.json");
    private void CheckDirectory()
    { for(var item=new DirectoryInfo(directory);item!=null;item=item.Parent)if(item.Exists&&(item.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Redirected screenshot directory."); }
    private Task<T> Run<T>(Func<T> work)
    {
        lock(queueLock) {
            var task=tail.ContinueWith(_=> { lock(DiskLock) { if(disposed)throw new ObjectDisposedException(nameof(ClipboardScreenshotStore)); CheckDirectory(); return work(); } },CancellationToken.None,TaskContinuationOptions.None,TaskScheduler.Default);
            tail=task; return task;
        }
    }
    internal Task<bool> EnsureDirectoryAsync()=>Run(()=> { EnsureDirectory(); return true; });
    private void EnsureDirectory()
    {
        CheckDirectory(); bool existed=Directory.Exists(directory); Directory.CreateDirectory(directory); CheckDirectory();
        if(!existed)AppLogger.Info($"Clipboard image directory created: path={directory}");
    }
    internal Task<ClipboardScreenshotSnapshot> InitializeAsync(ClipboardWidgetSettings options,bool thumbnails=true)=>Run(()=>
    {
        settings=options.Clone(); entries=[];
        if(settings.CaptureScreenshots)EnsureDirectory();
        if(!Directory.Exists(directory))return new ClipboardScreenshotSnapshot { Revision=++revision };
        bool validIndex=false;
        try
        {
            using var input=OpenRead(Index); if(input.Length>128*1024)throw new InvalidDataException();
            entries=(JsonSerializer.Deserialize<List<ClipboardScreenshot>>(input)??[]).Where(e=>e!=null&&e.Id!=Guid.Empty&&e.Hash?.Length==64&&e.Copied<=DateTimeOffset.UtcNow.AddMinutes(5)).DistinctBy(e=>e.Id).Take(200).ToList();
            validIndex=true;
        }
        catch(Exception ex) when(ex is IOException or JsonException or ArgumentException)
        { ClipboardDiagnostics.Failure("index recovery",ex); }
        if(!settings.KeepScreenshots)entries.Clear();
        else
        {
            var temporary=entries.Where(e=>!e.Persistent).Select(e=>e.Id).ToHashSet();
            entries.RemoveAll(e=>!e.Persistent||System.IO.File.Exists(TemporaryMarker(e.Id))||!ValidFile(e.Id));
            // Recover only when the index is unavailable. A valid index is authoritative:
            // unreferenced images may be deleted entries whose file removal was delayed.
            foreach(var file in validIndex?Enumerable.Empty<string>():Directory.EnumerateFiles(directory,"screenshot-*.png").Take(250))
            {
                if(!OriginalId(file,out var id)||temporary.Contains(id)||System.IO.File.Exists(TemporaryMarker(id))||entries.Any(e=>e.Id==id)||!ValidFile(id))continue;
                try { using var capture=new ClipboardScreenshotCapture(ReadBytes(file)); var decoded=capture.Decode(); using var image=decoded.Image; entries.Add(new(id,new DateTimeOffset(System.IO.File.GetLastWriteTimeUtc(file)),image.Width,image.Height,decoded.Hash,true)); }
                catch(Exception ex) { ClipboardDiagnostics.Failure("recovery skipped",ex); }
            }
        }
        ApplyLimits(DateTimeOffset.UtcNow); var result=Snapshot(thumbnails); try { SaveIndex(); DeleteUnused(); return result; } catch { result.Dispose(); throw; }
    });
    internal Task<ClipboardScreenshotSnapshot?> CaptureAsync(ClipboardScreenshotCapture capture,string? suppressHash=null)=>Run<ClipboardScreenshotSnapshot?>(()=>
    {
        var decoded=capture.Decode(); using var image=decoded.Image;
        if(decoded.Hash==suppressHash)return null;
        var existing=entries.FirstOrDefault(e=>e.Hash==decoded.Hash&&ValidFile(e.Id)); Guid id=existing?.Id??Guid.NewGuid();
        if(!settings.KeepScreenshots)AtomicBytes(TemporaryMarker(id),[]);
        if(existing==null) { AtomicBytes(Original(id),decoded.Png); AppLogger.Info($"Clipboard PNG stored: id={id:N}; width={image.Width}; height={image.Height}; bytes={decoded.Png.Length}"); using var thumbnail=CreateThumbnail(image); using var output=new MemoryStream(); thumbnail.Save(output,ImageFormat.Png); AtomicBytes(Thumbnail(id),output.ToArray()); }
        entries.RemoveAll(e=>e.Id==id); entries.Insert(0,new(id,capture.Copied,image.Width,image.Height,decoded.Hash,settings.KeepScreenshots));
        ApplyLimits(DateTimeOffset.UtcNow); return CommitSnapshot();
    });
    internal Task<ClipboardScreenshotSnapshot> ConfigureAsync(ClipboardWidgetSettings options)=>Run(()=>
    { settings=options.Clone(); if(!settings.KeepScreenshots)foreach(var entry in entries)AtomicBytes(TemporaryMarker(entry.Id),[]); entries=entries.Select(e=>e with { Persistent=settings.KeepScreenshots }).ToList(); ApplyLimits(DateTimeOffset.UtcNow); return CommitSnapshot(); });
    internal Task<ClipboardScreenshotSnapshot> CleanupAsync(DateTimeOffset now)=>Run(()=>
    { ApplyLimits(now); return CommitSnapshot(); });
    internal Task<ClipboardScreenshotSnapshot> RemoveAsync(Guid? id,DateTimeOffset? before=null)=>Run(()=>
    { entries.RemoveAll(e=>(id==null||e.Id==id)&&(before==null||e.Copied<=before)); return CommitSnapshot(); });
    internal Task<(Bitmap Image,byte[] Png)> LoadOriginalAsync(Guid id)=>Run(()=>
    { var bytes=ReadBytes(Original(id)); using var capture=new ClipboardScreenshotCapture(bytes); var decoded=capture.Decode(); return(decoded.Image,decoded.Png); });
    private ClipboardScreenshotSnapshot CommitSnapshot()
    {
        var snapshot=Snapshot();
        try { SaveIndex(); DeleteUnused(); return snapshot; } catch { snapshot.Dispose(); throw; }
    }
    private void ApplyLimits(DateTimeOffset now)
    {
        entries.RemoveAll(e=>settings.ScreenshotLifetimeHours>0&&e.Copied<=now.AddHours(-settings.ScreenshotLifetimeHours));
        entries=entries.OrderByDescending(e=>e.Copied).Take(ClipboardWidgetSettings.ScreenshotCapacities.Contains(settings.ScreenshotCapacity)?settings.ScreenshotCapacity:20).ToList();
    }
    private bool ValidFile(Guid id)
    {
        try { using var input=OpenRead(Original(id)); if(input.Length<45||input.Length>ClipboardScreenshotCapture.MaximumPngBytes)return false; var header=new byte[33]; input.ReadExactly(header); ClipboardScreenshotCapture.ValidatePng(header); return true; }
        catch(Exception ex) when(ex is IOException or ArgumentException or UnauthorizedAccessException) { return false; }
    }
    private static FileStream OpenRead(string path)
    { if((System.IO.File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Redirected screenshot file."); return new(path,FileMode.Open,FileAccess.Read,FileShare.Read); }
    private static byte[] ReadBytes(string path)
    { using var input=OpenRead(path); if(input.Length>ClipboardScreenshotCapture.MaximumPngBytes)throw new InvalidDataException(); using var output=new MemoryStream(); input.CopyTo(output); if(output.Length>ClipboardScreenshotCapture.MaximumPngBytes)throw new InvalidDataException(); return output.ToArray(); }
    private ClipboardScreenshotSnapshot Snapshot(bool thumbnails=true)
    {
        var result=new ClipboardScreenshotSnapshot { Revision=++revision };
        try
        {
            foreach(var item in entries.ToArray())
            {
                if(!ValidFile(item.Id)) { entries.Remove(item); continue; }
                if(thumbnails)
                {
                    Bitmap? thumbnail=null;
                    try { using var input=OpenRead(Thumbnail(item.Id)); if(input.Length>128*1024)throw new InvalidDataException(); var header=new byte[33]; input.ReadExactly(header); var dimensions=ClipboardScreenshotCapture.ValidatePng(header); if(dimensions.Width>192||dimensions.Height>128)throw new InvalidDataException(); input.Position=0; using var image=Image.FromStream(input); thumbnail=new Bitmap(image); }
                    catch(Exception thumbnailError)
                    { ClipboardDiagnostics.Failure("thumbnail regeneration",thumbnailError);
                        try { using var capture=new ClipboardScreenshotCapture(ReadBytes(Original(item.Id))); var decoded=capture.Decode(); using var image=decoded.Image; thumbnail=CreateThumbnail(image); using var output=new MemoryStream(); thumbnail.Save(output,ImageFormat.Png); AtomicBytes(Thumbnail(item.Id),output.ToArray()); }
                        catch(Exception ex) { entries.Remove(item); ClipboardDiagnostics.Failure("unavailable",ex); }
                    }
                    if(thumbnail==null)continue; result.Thumbnails[item.Id]=thumbnail;
                }
                result.Entries.Add(item);
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }
    internal static Bitmap CreateThumbnail(Image image)
    {
        double scale=Math.Min(160d/image.Width,96d/image.Height); int width=Math.Max(1,(int)Math.Round(image.Width*scale)),height=Math.Max(1,(int)Math.Round(image.Height*scale));
        var result=new Bitmap(width,height,PixelFormat.Format32bppPArgb);
        try { using var graphics=Graphics.FromImage(result); graphics.InterpolationMode=InterpolationMode.HighQualityBicubic; graphics.Clear(Color.Transparent); graphics.DrawImage(image,0,0,width,height); return result; }
        catch { result.Dispose(); throw; }
    }
    private void AtomicBytes(string path,byte[] bytes)
    {
        CheckDirectory(); Directory.CreateDirectory(directory); CheckDirectory(); string temporary=path+".tmp";
        if(System.IO.File.Exists(temporary)&&(System.IO.File.GetAttributes(temporary)&FileAttributes.ReparsePoint)!=0)throw new IOException("Redirected temporary file.");
        if(System.IO.File.Exists(path)&&(System.IO.File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Redirected screenshot file.");
        using(var output=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None,8192,FileOptions.WriteThrough)) { output.Write(bytes); output.Flush(true); }
        System.IO.File.Move(temporary,path,true);
    }
    private void SaveIndex() { if(Directory.Exists(directory)||entries.Count>0)AtomicBytes(Index,JsonSerializer.SerializeToUtf8Bytes(entries)); }
    private static bool OriginalId(string path,out Guid id)
    { id=default; string name=Path.GetFileName(path); return name.StartsWith("screenshot-",StringComparison.Ordinal)&&name.EndsWith(".png",StringComparison.Ordinal)&&Guid.TryParseExact(name[11..^4],"N",out id); }
    private void DeleteUnused()
    {
        if(!Directory.Exists(directory))return;
        var used=entries.SelectMany(e=>e.Persistent?new[]{Original(e.Id),Thumbnail(e.Id)}:new[]{Original(e.Id),Thumbnail(e.Id),TemporaryMarker(e.Id)}).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var file in Directory.EnumerateFiles(directory))
        {
            string name=Path.GetFileName(file),original=name.EndsWith(".tmp",StringComparison.Ordinal)?name[..^4]:name;
            if(original.EndsWith(".session",StringComparison.Ordinal))original=original[..^8];
            original=original.Replace(".thumb.png",".png",StringComparison.Ordinal);
            if(!OriginalId(original,out _)&&name!="history.json.tmp")continue;
            if(used.Contains(file))continue;
            try { if((System.IO.File.GetAttributes(file)&FileAttributes.ReparsePoint)==0)System.IO.File.Delete(file); }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { ClipboardDiagnostics.Failure("cleanup deferred",ex); }
        }
    }
    public void Dispose()=>disposed=true;
}
