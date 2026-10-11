using System.Runtime.InteropServices;
namespace WallpaperControl;
internal interface IClipboardSource : IDisposable
{
    event Action<string>? TextChanged;
    bool Enabled { get; set; }
    bool Write(string text);
}
internal interface IClipboardImageSource
{
    event Action<ClipboardScreenshotCapture>? ImageChanged;
    bool CaptureImages { get; set; }
    bool WriteImage(Bitmap image,byte[] png);
}
internal sealed class ClipboardListener : NativeWindow,IClipboardSource,IClipboardImageSource
{
    public event Action<string>? TextChanged;
    public event Action<ClipboardScreenshotCapture>? ImageChanged;
    private bool captureImages;
    public bool CaptureImages { get=>captureImages; set { if(captureImages==value)return; captureImages=value; AppLogger.Info($"Clipboard image monitoring: enabled={value}; registered={Registered}"); } }
    private readonly uint pngFormat=RegisterClipboardFormat("PNG"),mimePngFormat=RegisterClipboardFormat("image/png");
    private readonly System.Windows.Forms.Timer retry = new() { Interval = 60 };
    private uint sequence;
    private int attempts;
    private uint? pendingSequence;
    private bool reading;
    private static readonly int[] RetryDelays=[60,120,240,480];
    private bool disposed;
    private readonly uint excluded = RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing"), history = RegisterClipboardFormat("CanIncludeInClipboardHistory");
    internal bool Registered { get; private set; }
    private bool enabled = true;
    private readonly int threadId=Environment.CurrentManagedThreadId;
    public bool Enabled { get => enabled; set { if(enabled==value)return; enabled=value; retry.Stop(); pendingSequence=null; sequence=GetClipboardSequenceNumber(); } }
    internal ClipboardListener()
    {
        if(Thread.CurrentThread.GetApartmentState()!=ApartmentState.STA)throw new InvalidOperationException("Clipboard listener requires STA.");
        CreateHandle(new CreateParams { Parent = new IntPtr(-3),Caption = "WallpaperControl clipboard listener" });
        sequence = GetClipboardSequenceNumber();
        Registered = AddClipboardFormatListener(Handle);
        retry.Tick += (_,_) => ReadUpdate();
        AppLogger.Info($"Clipboard listener registration: success={Registered}; sta={Thread.CurrentThread.GetApartmentState()==ApartmentState.STA}");
        if(!Registered) { retry.Dispose(); DestroyHandle(); throw new InvalidOperationException("Clipboard listener registration failed."); }
    }
    protected override void WndProc(ref Message m)
    {
        if(m.Msg==0x031D&&!disposed) {
            uint current=GetClipboardSequenceNumber();
            AppLogger.Info($"Clipboard event WM_CLIPBOARDUPDATE: sequence={current}; enabled={enabled}; images={CaptureImages}");
            if(!reading&&current!=sequence&&!(pendingSequence==current&&retry.Enabled)) {
                if(pendingSequence!=current) { retry.Stop(); pendingSequence=current; attempts=0; }
                ReadUpdate();
            }
        }
        base.WndProc(ref m);
    }
    private void FinishRead(uint current)
    { sequence=current; pendingSequence=null; retry.Stop(); }
    private void RetryRead(string reason)
    {
        if(disposed||!enabled||pendingSequence==null)return;
        uint current=GetClipboardSequenceNumber();
        if(current!=pendingSequence) {
            AppLogger.Info($"Clipboard retry superseded: oldSequence={pendingSequence}; newSequence={current}");
            pendingSequence=null; attempts=0;
            if(current!=sequence) { pendingSequence=current; retry.Interval=60; retry.Start(); } return;
        }
        if(attempts>=5) { AppLogger.Info($"Clipboard read abandoned: sequence={pendingSequence}; attempts={attempts}; reason={reason}"); FinishRead(current); return; }
        retry.Interval=RetryDelays[Math.Max(0,attempts-1)];
        AppLogger.Info($"Clipboard read retry scheduled: sequence={pendingSequence}; attempt={attempts+1}; delayMs={retry.Interval}; reason={reason}"); retry.Start();
    }
    private void ReadUpdate()
    {
        retry.Stop(); if(disposed||reading)return;
        uint current=GetClipboardSequenceNumber();
        if(!enabled) { FinishRead(current); return; }
        if(pendingSequence==null) { if(current==sequence)return; pendingSequence=current; attempts=0; }
        if(pendingSequence!=current) {
            AppLogger.Info($"Clipboard pending read replaced: oldSequence={pendingSequence}; newSequence={current}");
            if(current==sequence) { pendingSequence=null; return; } pendingSequence=current; attempts=0;
        }
        attempts++;
        AppLogger.Info($"Clipboard read attempt: sequence={pendingSequence}; attempt={attempts}");
        if(!OpenClipboard(Handle)) { RetryRead("clipboard locked; win32="+Marshal.GetLastPInvokeError()); return; }
        string? text=null; ClipboardScreenshotCapture? image=null; bool retryImage=false; reading=true;
        try {
            LogFormats();
            AppLogger.Info($"Clipboard change recognized: imagesEnabled={CaptureImages}; PNG={IsClipboardFormatAvailable(pngFormat)||IsClipboardFormatAvailable(mimePngFormat)}; DIBV5={IsClipboardFormatAvailable(17)}; DIB={IsClipboardFormatAvailable(8)}; Bitmap={IsClipboardFormatAvailable(2)}; text={IsClipboardFormatAvailable(13)}");
            if(IsClipboardFormatAvailable(excluded)&&ReadExclusionFlag()) { AppLogger.Info("Clipboard change skipped: explicit monitor exclusion."); FinishRead(GetClipboardSequenceNumber()); return; }
            if(IsClipboardFormatAvailable(history)) {
                var data=GetClipboardData(history); if(data==IntPtr.Zero||GlobalSize(data).ToUInt64()<4) { FinishRead(GetClipboardSequenceNumber()); return; }
                var flag=GlobalLock(data); if(flag==IntPtr.Zero) { FinishRead(GetClipboardSequenceNumber()); return; }
                try { if(Marshal.ReadInt32(flag)==0) { AppLogger.Info("Clipboard change skipped: Windows history opt-out."); FinishRead(GetClipboardSequenceNumber()); return; } } finally { GlobalUnlock(data); }
            }
            if(CaptureImages&&ImageChanged!=null)image=ReadImage(out retryImage);
            if(image!=null) { FinishRead(GetClipboardSequenceNumber()); return; }
            if(retryImage)return;
            if(!IsClipboardFormatAvailable(13)) {
                // EmptyClipboard and delayed format publication can precede the final image.
                retryImage=CaptureImages&&EnumClipboardFormats(0)==0;
                if(!retryImage)FinishRead(GetClipboardSequenceNumber()); return;
            }
            var handle=GetClipboardData(13); if(handle==IntPtr.Zero) { FinishRead(GetClipboardSequenceNumber()); return; }
            ulong bytes=GlobalSize(handle).ToUInt64(); if(bytes<2||bytes>(ClipboardHistory.MaximumTextCharacters+1L)*2) { FinishRead(GetClipboardSequenceNumber()); return; }
            var pointer=GlobalLock(handle); if(pointer==IntPtr.Zero) { FinishRead(GetClipboardSequenceNumber()); return; }
            try { var value=Marshal.PtrToStringUni(pointer,(int)(bytes/2))??""; int end=value.IndexOf('\0'); text=end>=0?value[..end]:null; }
            finally { GlobalUnlock(handle); }
            FinishRead(GetClipboardSequenceNumber());
        }
        finally {
            CloseClipboard(); reading=false;
            if(retryImage)RetryRead("image data temporarily unavailable");
            if(image!=null) { if(!disposed&&ImageChanged!=null)ImageChanged.Invoke(image); else image.Dispose(); }
        }
        if(text!=null&&!disposed)TextChanged?.Invoke(text);
    }
    private void LogFormats()
    {
        var formats=new List<uint>(); uint format=0;
        while(formats.Count<64&&(format=EnumClipboardFormats(format))!=0)formats.Add(format);
        // Numeric IDs avoid leaking custom format names that might contain source metadata.
        AppLogger.Info($"Clipboard available format IDs: sequence={pendingSequence}; ids={string.Join(',',formats)}");
    }
    private bool ReadExclusionFlag()
    {
        var handle=GetClipboardData(excluded); ulong size=GlobalSize(handle).ToUInt64();
        if(size is not (1 or 4)) { AppLogger.Info("Clipboard exclusion flag unavailable or invalid; content remains excluded."); return true; }
        var pointer=GlobalLock(handle); if(pointer==IntPtr.Zero)return true;
        try { return size==1?Marshal.ReadByte(pointer)!=0:Marshal.ReadInt32(pointer)!=0; } finally { GlobalUnlock(handle); }
    }
    private ClipboardScreenshotCapture? ReadImage(out bool transient)
    {
        transient=false; var candidates=new List<ClipboardScreenshotCapture>(); long total=0;
        try {
            foreach(var item in new[]{(pngFormat,"PNG"),(mimePngFormat,"image/png"),(17u,"DIBV5"),(8u,"DIB")}) {
                uint format=item.Item1; if(!IsClipboardFormatAvailable(format))continue;
                var handle=GetClipboardData(format); if(handle==IntPtr.Zero) { transient=true; AppLogger.Info($"Clipboard image format unavailable: format={item.Item2}; win32={Marshal.GetLastPInvokeError()}"); continue; }
                ulong size=GlobalSize(handle).ToUInt64(); int limit=format==pngFormat||format==mimePngFormat?ClipboardScreenshotCapture.MaximumPngBytes:ClipboardScreenshotCapture.MaximumRawBytes;
                if(size<12)transient=true;
                if(size<12||size>(ulong)limit||total+(long)size>ClipboardScreenshotCapture.MaximumRawBytes) { AppLogger.Info($"Clipboard image format skipped: format={item.Item2}; reason=data size/budget; bytes={size}"); continue; }
                var pointer=GlobalLock(handle); if(pointer==IntPtr.Zero) { transient=true; AppLogger.Info($"Clipboard image format could not be locked: format={item.Item2}; win32={Marshal.GetLastPInvokeError()}"); continue; }
                try { var bytes=new byte[(int)size]; Marshal.Copy(pointer,bytes,0,bytes.Length); candidates.Add(new(bytes,format==17||format==8,format:item.Item2)); total+=(long)size; }
                finally { GlobalUnlock(handle); }
            }
            if(IsClipboardFormatAvailable(2)) {
                var handle=GetClipboardData(2); if(handle==IntPtr.Zero) { transient=true; AppLogger.Info($"Clipboard bitmap unavailable: win32={Marshal.GetLastPInvokeError()}"); }
                if(handle!=IntPtr.Zero&&GetObject(handle,Marshal.SizeOf<NativeBitmap>(),out var bitmap)!=0&&ClipboardScreenshotCapture.ValidDimensions(bitmap.Width,bitmap.Height)&&total+(long)bitmap.Width*bitmap.Height*4<=ClipboardScreenshotCapture.MaximumRawBytes) {
                    var owned=CopyImage(handle,0,0,0,0x2000); if(owned!=IntPtr.Zero)candidates.Add(new(owned)); else transient=true;
                }
            }
            if(candidates.Count==0)return null;
            AppLogger.Info($"Clipboard image acquired on STA: candidates={candidates.Count}; rawBytes={total}");
            return new(candidates);
        }
        catch(Exception ex) { foreach(var candidate in candidates)candidate.Dispose(); ClipboardDiagnostics.Failure("native image acquisition",ex); return null; }
    }
    public bool WriteImage(Bitmap image,byte[] png)
    {
        if(disposed)return false;
        if(Environment.CurrentManagedThreadId!=threadId)throw new InvalidOperationException("Clipboard write must use the listener STA thread.");
        try
        {
            var data=new DataObject(); data.SetImage(image); using var original=new MemoryStream(png,false); data.SetData("PNG",false,original);
            using var flag=new MemoryStream(BitConverter.GetBytes(0)); data.SetData("CanIncludeInClipboardHistory",flag);
            using var cloud=new MemoryStream(BitConverter.GetBytes(0)); data.SetData("CanUploadToCloudClipboard",cloud);
            using var excludedFlag=new MemoryStream([1]); data.SetData("ExcludeClipboardContentFromMonitorProcessing",excludedFlag);
            Clipboard.SetDataObject(data,true,3,40); FinishRead(GetClipboardSequenceNumber()); return true;
        }
        catch(ExternalException) { return false; }
    }
    public bool Write(string text)
    {
        if(disposed)return false;
        if(Environment.CurrentManagedThreadId!=threadId)throw new InvalidOperationException("Clipboard write must use the listener STA thread.");
        try
        {
            var data = new DataObject(); data.SetData(DataFormats.UnicodeText,text);
            using var flag = new MemoryStream(BitConverter.GetBytes(0));
            data.SetData("CanIncludeInClipboardHistory",flag);
            using var cloudFlag = new MemoryStream(BitConverter.GetBytes(0)); data.SetData("CanUploadToCloudClipboard",cloudFlag);
            using var excludeFlag = new MemoryStream([1]); data.SetData("ExcludeClipboardContentFromMonitorProcessing",excludeFlag);
            Clipboard.SetDataObject(data,true,3,40); FinishRead(GetClipboardSequenceNumber()); return true;
        }
        catch (ExternalException) { return false; }
    }
    public void Dispose()
    {
        if(disposed)return; disposed=true; retry.Dispose();
        if(Registered) RemoveClipboardFormatListener(Handle); Registered=false; DestroyHandle(); TextChanged=null; ImageChanged=null;
    }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AddClipboardFormatListener(IntPtr handle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveClipboardFormatListener(IntPtr handle);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll",SetLastError=true)] private static extern uint EnumClipboardFormats(uint format);
    [DllImport("user32.dll",SetLastError=true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenClipboard(IntPtr handle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll",SetLastError=true)] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string format);
    [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr handle);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalUnlock(IntPtr handle);
    [StructLayout(LayoutKind.Sequential)] private struct NativeBitmap { internal int Type,Width,Height,WidthBytes; internal ushort Planes,BitsPixel; internal IntPtr Bits; }
    [DllImport("gdi32.dll",EntryPoint="GetObjectW")] private static extern int GetObject(IntPtr bitmap,int size,out NativeBitmap result);
    [DllImport("user32.dll")] private static extern IntPtr CopyImage(IntPtr image,uint type,int width,int height,uint flags);
}
internal sealed class ClipboardService : IDisposable
{
    private readonly IClipboardSource source;
    private readonly ClipboardScreenshotStore? images;
    private readonly Control? dispatcher;
    private readonly Dictionary<Guid,Bitmap> thumbnails=[];
    private readonly System.Windows.Forms.Timer cleanup=new() { Interval=60_000 };
    private ClipboardWidgetSettings options=new();
    private readonly Task initialization;
    private long revision;
    private readonly HashSet<Guid> deletedImages=[];
    private DateTimeOffset clearedAt=DateTimeOffset.MinValue;
    private int captureGeneration;
    private int pendingImages;
    private bool copyBusy,cleanupBusy;
    private string? copiedImageHash;
    private DateTimeOffset copiedImageAt;
    internal ClipboardHistory History { get; } = new();
    internal event Action? Changed;
    private bool suspended,powerSuspended;
    private volatile bool disposed;
    internal ClipboardService(IClipboardSource source,ClipboardScreenshotStore? images=null,ClipboardWidgetSettings? settings=null,Task? beforeStartup=null)
    {
        if(images!=null) { if(Thread.CurrentThread.GetApartmentState()!=ApartmentState.STA)throw new InvalidOperationException("Clipboard service requires an STA UI thread."); dispatcher=new Control(); _=dispatcher.Handle; }
        this.source=source; this.images=images; options=settings?.Clone()??new(); source.TextChanged+=Observe;
        AppLogger.Info($"Clipboard screenshots configured: capture={options.CaptureScreenshots}; imageSource={source is IClipboardImageSource}; storage={images!=null}; persistent={options.KeepScreenshots}; limit={options.ScreenshotCapacity}; lifetimeHours={options.ScreenshotLifetimeHours}");
        if(source is IClipboardImageSource imageSource) { imageSource.CaptureImages=false; imageSource.ImageChanged+=ObserveImage; }
        cleanup.Tick+=(_,_)=>CleanupImages();
        initialization=InitializeImages(beforeStartup); if(images!=null)cleanup.Start();
    }
    private async Task InitializeImages(Task? beforeStartup)
    {
        try {
            if(beforeStartup!=null)await beforeStartup;
            if(images!=null&&!disposed) {
                var snapshot=await images.InitializeAsync(options); int count=snapshot.Entries.Count;
                await ApplyAsync(snapshot);
                if(!disposed)AppLogger.Info($"Clipboard screenshot storage initialized: images={count}; capture={options.CaptureScreenshots}");
            }
        }
        catch(Exception ex) { ClipboardDiagnostics.Failure("startup failed",ex); }
        finally { await UpdateSourceAsync(); }
    }
    private void Observe(string text) { if(!disposed&&!suspended&&!powerSuspended&&History.Observe(text,DateTimeOffset.UtcNow))Changed?.Invoke(); }
    private async void ObserveImage(ClipboardScreenshotCapture capture)
    {
        if(disposed||powerSuspended||History.Paused||!options.CaptureScreenshots||images==null||pendingImages>=3) { capture.Dispose(); return; }
        int generation=captureGeneration; pendingImages++; UpdateSource();
        try
        {
            await initialization; if(disposed||generation!=captureGeneration)return;
            string? suppress=DateTimeOffset.UtcNow-copiedImageAt<TimeSpan.FromSeconds(2)?copiedImageHash:null;
            var snapshot=await images.CaptureAsync(capture,suppress);
            if(snapshot!=null) {
                if(generation!=captureGeneration) {
                    var stale=snapshot.Entries.Where(e=>e.Copied<=clearedAt).Select(e=>e.Id).ToArray(); snapshot.Dispose();
                    foreach(var id in stale) { using var removed=await images.RemoveAsync(id,clearedAt); }
                }
                else await ApplyAsync(snapshot);
            }
        }
        catch(Exception ex) { if(!disposed)ClipboardDiagnostics.Failure("capture failed",ex); }
        finally { capture.Dispose(); await OnUiAsync(()=> { pendingImages--; UpdateSource(); }); }
    }
    private void UpdateSource()
    {
        if(disposed)return;
        bool imagesEnabled=images!=null&&options.CaptureScreenshots&&!powerSuspended&&!History.Paused;
        source.Enabled=!powerSuspended&&!History.Paused&&(!suspended||imagesEnabled);
        if(source is IClipboardImageSource imageSource)imageSource.CaptureImages=imagesEnabled&&pendingImages<3;
    }
    private Task UpdateSourceAsync()=>OnUiAsync(UpdateSource);
    private async Task OnUiAsync(Action action)
    {
        if(disposed||dispatcher==null)return;
        try { await dispatcher.InvokeAsync(()=> { if(!disposed)action(); }); }
        catch(Exception)when(disposed) { }
    }
    private async Task ApplyAsync(ClipboardScreenshotSnapshot snapshot)
    {
        if(disposed||dispatcher==null) { snapshot.Dispose(); return; }
        try { await dispatcher.InvokeAsync(()=>Apply(snapshot)); }
        catch { snapshot.Dispose(); if(!disposed)throw; }
    }
    private void Apply(ClipboardScreenshotSnapshot snapshot)
    {
        if(disposed||snapshot.Revision<revision) { snapshot.Dispose(); return; }
        revision=snapshot.Revision;
        foreach(var item in snapshot.Entries.Where(e=>deletedImages.Contains(e.Id)||e.Copied<=clearedAt).ToArray()) {
            snapshot.Entries.Remove(item); if(snapshot.Thumbnails.Remove(item.Id,out var thumbnail))thumbnail.Dispose();
        }
        foreach(var bitmap in thumbnails.Values)bitmap.Dispose(); thumbnails.Clear();
        foreach(var item in snapshot.Thumbnails)thumbnails[item.Key]=item.Value; snapshot.Thumbnails.Clear();
        int before=History.Entries.Count(e=>e.IsImage); History.ReplaceScreenshots(snapshot.Entries);
        if(before!=snapshot.Entries.Count)AppLogger.Info($"Clipboard screenshot history updated: images={snapshot.Entries.Count}"); Changed?.Invoke();
    }
    internal Bitmap? Thumbnail(Guid id)=>thumbnails.GetValueOrDefault(id);
    internal void Configure(int capacity) { History.Configure(capacity); Changed?.Invoke(); }
    internal void Configure(ClipboardWidgetSettings value,bool commitImages=true)
    {
        bool enableImages=!options.CaptureScreenshots&&value.CaptureScreenshots;
        bool changed=options.ScreenshotCapacity!=value.ScreenshotCapacity||options.ScreenshotLifetimeHours!=value.ScreenshotLifetimeHours||options.KeepScreenshots!=value.KeepScreenshots;
        options.CaptureScreenshots=value.CaptureScreenshots;
        if(commitImages&&changed) { options=value.Clone(); ConfigureImages(options.Clone()); }
        if(enableImages)EnsureImagesDirectory();
        History.Configure(value.Capacity); UpdateSource(); Changed?.Invoke();
    }
    private async void EnsureImagesDirectory()
    { try { await initialization; if(!disposed&&images!=null)await images.EnsureDirectoryAsync(); } catch(Exception ex) { if(!disposed)ClipboardDiagnostics.Failure("create image directory",ex); } }
    private async void ConfigureImages(ClipboardWidgetSettings value)
    { try { await initialization; if(!disposed&&images!=null)await ApplyAsync(await images.ConfigureAsync(value)); } catch(Exception ex) { if(!disposed)ClipboardDiagnostics.Failure("options failed",ex); } }
    internal void SetSuspended(bool value,bool power=false) { suspended=value; powerSuspended=power; AppLogger.Info($"Clipboard suspension: activity={value}; power={power}; imagesContinue={value&&!power&&images!=null&&options.CaptureScreenshots&&!History.Paused}"); UpdateSource(); }
    internal void TogglePause() { History.Paused=!History.Paused; UpdateSource(); Changed?.Invoke(); }
    internal bool Copy(Guid id)
    {
        var entry=History.Entries.FirstOrDefault(e=>e.Id==id);
        return !disposed&&entry!=null&&!entry.IsImage&&source.Write(entry.Text);
    }
    internal async Task<bool> CopyAsync(Guid id)
    {
        var entry=History.Entries.FirstOrDefault(e=>e.Id==id); if(entry==null||disposed)return false;
        if(!entry.IsImage)return Copy(id);
        if(copyBusy||images==null||source is not IClipboardImageSource imageSource)return false;
        copyBusy=true;
        try
        {
            var original=await images.LoadOriginalAsync(id); using var image=original.Image;
            if(disposed||dispatcher==null)return false;
            return await dispatcher.InvokeAsync(()=> {
                if(disposed||!History.Entries.Any(e=>e.Id==id))return false;
                copiedImageHash=entry.Screenshot!.Hash; copiedImageAt=DateTimeOffset.UtcNow;
                return imageSource.WriteImage(image,original.Png);
            });
        }
        catch(Exception ex) { if(!disposed)ClipboardDiagnostics.Failure("copy failed",ex); return false; }
        finally { copyBusy=false; }
    }
    internal void Remove(Guid id)
    {
        bool image=History.Entries.Any(e=>e.Id==id&&e.IsImage); if(image)deletedImages.Add(id); History.Remove(id);
        if(thumbnails.Remove(id,out var bitmap))bitmap.Dispose(); Changed?.Invoke(); if(image)RemoveImages(id);
    }
    internal void Clear()
    { captureGeneration++; clearedAt=DateTimeOffset.UtcNow; source.Enabled=false; History.Clear(); foreach(var bitmap in thumbnails.Values)bitmap.Dispose(); thumbnails.Clear(); UpdateSource(); Changed?.Invoke(); if(images!=null)RemoveImages(null,clearedAt); }
    private async void RemoveImages(Guid? id,DateTimeOffset? before=null)
    { try { await initialization; if(!disposed&&images!=null)await ApplyAsync(await images.RemoveAsync(id,before)); } catch(Exception ex) { if(!disposed)ClipboardDiagnostics.Failure("removal deferred",ex); } }
    private async void CleanupImages()
    {
        if(disposed||cleanupBusy||images==null)return; cleanupBusy=true;
        try { await initialization; if(!disposed)await ApplyAsync(await images.CleanupAsync(DateTimeOffset.UtcNow)); }
        catch(Exception ex) { if(!disposed)ClipboardDiagnostics.Failure("cleanup failed",ex); }
        finally { cleanupBusy=false; }
    }
    public void Dispose()
    {
        if(disposed)return; disposed=true; cleanup.Dispose(); source.TextChanged-=Observe;
        if(source is IClipboardImageSource imageSource)imageSource.ImageChanged-=ObserveImage;
        source.Dispose(); dispatcher?.Dispose(); images?.Dispose(); foreach(var bitmap in thumbnails.Values)bitmap.Dispose(); thumbnails.Clear(); History.Clear(); Changed=null;
    }
}
