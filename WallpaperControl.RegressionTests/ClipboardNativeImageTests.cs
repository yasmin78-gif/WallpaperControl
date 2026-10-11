extern alias WallpaperApp;
using App=WallpaperApp::WallpaperControl;
using System.Drawing;
using System.Drawing.Imaging;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Diagnostics;

// Exercises the production HWND listener, native formats and complete service/store pipeline.
// Copies/restores raw clipboard formats without logging their contents. Never pastes or changes focus.
internal static class ClipboardNativeImageTests
{
    internal static void Run(Action<bool,string> check)
    {
        Exception? error=null;
        var thread=new Thread(()=> { try { Ui(check); } catch(Exception ex) { error=ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if(error!=null)throw error;
    }
    private static void Pump(Func<bool> done)
    {
        var timeout=Stopwatch.StartNew(); while(!done()&&timeout.ElapsedMilliseconds<5000) { Application.DoEvents(); Thread.Sleep(5); }
        if(!done())throw new TimeoutException("Native clipboard image was not delivered to history.");
    }
    private static byte[] Dib(int header,int marker)
    {
        var bytes=new byte[header+32*24*4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes,header); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4),32); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8),24);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12),1); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14),32);
        if(header==124) {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16),3);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40),0x00FF0000); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(44),0x0000FF00);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48),0x000000FF); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(52),0xFF000000);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(56),0x73524742);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(108),4);
        }
        for(int i=header;i<bytes.Length;i+=4) { bytes[i]=(byte)marker; bytes[i+1]=60; bytes[i+2]=200; bytes[i+3]=255; }
        return bytes;
    }
    private static byte[] Png(int marker)
    {
        using var bitmap=new Bitmap(32,24); using(var g=Graphics.FromImage(bitmap))g.Clear(Color.FromArgb(200,60,marker));
        using var stream=new MemoryStream(); bitmap.Save(stream,ImageFormat.Png); return stream.ToArray();
    }
    private static void Ui(Action<bool,string> check)
    {
        using var dispatcher=new Control(); _=dispatcher.Handle;
        var backup=Backup(); uint lastWrite=0;
        string directory=Path.Combine(Path.GetTempPath(),"WallpaperControl-NativeImages-"+Guid.NewGuid().ToString("N"));
        try
        {
            var options=new App.ClipboardWidgetSettings(); using var native=new App.ClipboardListener();
            using var service=new App.ClipboardService(native,new App.ClipboardScreenshotStore(directory),options);
            service.Configure(options); Pump(()=>native.CaptureImages);
            var startup=(Task)typeof(App.ClipboardService).GetField("initialization",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(service)!;
            Pump(()=>startup.IsCompleted); check(Directory.Exists(directory),"Native screenshot storage initialized before first clipboard image");
            using var widget=new App.ClipboardWidgetForm(service,options,"de",_=>{}); _=widget.Handle;
            void Publish(List<Saved> formats)=>Set(formats,dispatcher.Handle,sequence=>lastWrite=sequence);
            uint png=RegisterClipboardFormat("PNG"),exclude=RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
            int count=0;
            foreach(var item in new[]{(8u,Dib(40,10)),(17u,Dib(124,20)),(png,Png(30))})
            {
                int expected=++count; Publish([new(item.Item1,item.Item2,IntPtr.Zero)]);
                Pump(()=>service.History.Entries.Count==expected);
                check(service.History.Entries[0].IsImage&&service.Thumbnail(service.History.Entries[0].Id)!=null,"Native clipboard event to history and thumbnail: format="+item.Item1);
            }
            int next=++count;
            Publish([new(8,Dib(40,40),IntPtr.Zero),new(exclude,BitConverter.GetBytes(0),IntPtr.Zero)]);
            Pump(()=>service.History.Entries.Count==next);
            check(service.History.Entries.Count==next,"An explicit zero exclusion flag permits screenshot capture");
            next=++count;
            Publish([new(png,new byte[40],IntPtr.Zero),new(17,Dib(124,50),IntPtr.Zero),new(8,Dib(40,50),IntPtr.Zero)]);
            Pump(()=>service.History.Entries.Count==next);
            check(service.History.Entries.Count==next,"Malformed preferred PNG falls back to native DIBV5/DIB");
            next=++count;
            using(var bitmap=new Bitmap(32,24)) {
                using(var g=Graphics.FromImage(bitmap))g.Clear(Color.Blue);
                var handle=bitmap.GetHbitmap(); try { Publish([new(2,null,handle)]); } finally { DeleteObject(handle); }
            }
            Pump(()=>service.History.Entries.Count==next);
            check(service.History.Entries.Count==next,"Native CF_BITMAP-only screenshot capture works");
            var before=service.History.Entries.ToArray();
            Publish([new(8,Dib(40,90),IntPtr.Zero),new(exclude,BitConverter.GetBytes(1),IntPtr.Zero)]);
            var quiet=Stopwatch.StartNew(); while(quiet.ElapsedMilliseconds<150) { Application.DoEvents(); Thread.Sleep(5); }
            check(service.History.Entries.SequenceEqual(before),"Explicit exclusion still protects clipboard content");
            var copied=service.CopyAsync(before[0].Id); Pump(()=>copied.IsCompleted); lastWrite=GetClipboardSequenceNumber();
            check(copied.Result&&Clipboard.ContainsImage(),"Production native image writer puts image back in Windows clipboard");
            using var pasted=Clipboard.GetImage(); check(pasted?.Size==new Size(32,24)&&pasted is Bitmap readBitmap&&readBitmap.GetPixel(0,0).B==255,"Another Windows clipboard reader receives original pixels and full resolution");
            quiet.Restart(); while(quiet.ElapsedMilliseconds<150) { Application.DoEvents(); Thread.Sleep(5); }
            check(service.History.Entries.SequenceEqual(before),"Native write echo leaves chronology unchanged; before="+before.Length+" after="+service.History.Entries.Count+" text="+service.History.Entries.Count(e=>!e.IsImage));
            check(widget.ClientSize.Height>120,"Native image history change updates the actual widget render on STA");
            check(Directory.Exists(directory)&&Directory.GetFiles(directory,"screenshot-*.png").Length==count*2,"Native pipeline creates original PNGs and previews");
            widget.SetActivitySuspended(true);
            check(native.Enabled&&native.CaptureImages,"Fullscreen widget suspension keeps enabled image collection alive");
            Publish([new(png,Png(70),IntPtr.Zero)]); count++; Pump(()=>service.History.Entries.Count(e=>e.IsImage)==count);
            Publish([new(13,System.Text.Encoding.Unicode.GetBytes("text during activity pause\0"),IntPtr.Zero)]);
            Pump(()=>typeof(App.ClipboardListener).GetField("sequence",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(native) is uint value&&value>=lastWrite);
            check(!service.History.Entries.Any(e=>!e.IsImage),"Text suspension behavior remains unchanged during fullscreen capture");
            widget.SetActivitySuspended(false);
            check(service.History.Entries.Count(e=>e.IsImage)==count,"Screenshot received during fullscreen remains available after resume");
            widget.SetPowerSuspended(true); check(!native.Enabled&&!native.CaptureImages,"Power suspension still disables clipboard capture"); widget.SetPowerSuspended(false);
            service.TogglePause(); check(!native.Enabled&&!native.CaptureImages,"Manual recording pause still disables clipboard capture"); service.TogglePause();
            using(var delayed=new DelayedOwner(png,Png(80),sequence=>lastWrite=sequence)) {
                delayed.Publish(); Pump(()=>delayed.Requests>0);
                int firstAttempts=(int)typeof(App.ClipboardListener).GetField("attempts",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(native)!;
                for(int i=0;i<3;i++)SendMessage(native.Handle,0x031D,IntPtr.Zero,IntPtr.Zero);
                check((int)typeof(App.ClipboardListener).GetField("attempts",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(native)! ==firstAttempts,"Duplicate notifications do not prematurely consume image retry budget");
                using var ready=new System.Windows.Forms.Timer { Interval=160 }; ready.Tick+=(_,_)=> { ready.Stop(); delayed.Ready=true; }; ready.Start();
                count++; Pump(()=>service.History.Entries.Count(e=>e.IsImage)==count);
                check(delayed.Requests>=2,"Delayed WM_RENDERFORMAT data acquired by bounded asynchronous retry");
            }
            using(var stale=new DelayedOwner(png,Png(81),sequence=>lastWrite=sequence)) {
                stale.Publish(); Pump(()=>stale.Requests>0);
                Publish([new(13,System.Text.Encoding.Unicode.GetBytes("text between screenshots\0"),IntPtr.Zero)]); stale.Ready=true;
                Pump(()=>service.History.Entries.Any(e=>e.Text=="text between screenshots"));
                Quiet(); check(service.History.Entries.Count(e=>e.IsImage)==count,"A newer text change cancels an older delayed screenshot retry");
            }
            using(var superseded=new DelayedOwner(png,Png(85),sequence=>lastWrite=sequence)) {
                superseded.Publish(); Pump(()=>superseded.Requests>0);
                Publish([new(png,Png(86),IntPtr.Zero)]); superseded.Ready=true; count++;
                Pump(()=>service.History.Entries.Count(e=>e.IsImage)==count); Quiet();
                var newest=service.History.Entries.First(e=>e.IsImage);
                check(File.ReadAllBytes(Path.Combine(directory,"screenshot-"+newest.Id.ToString("N")+".png")).SequenceEqual(Png(86)),"Newer screenshot supersedes an unavailable older image without wrong association");
            }
            Publish([new(png,Png(82),IntPtr.Zero)]); count++; Pump(()=>service.History.Entries.Count(e=>e.IsImage)==count);
            check(service.History.Entries.Count(e=>!e.IsImage)==1,"Text between successive screenshots is retained exactly once");
            Publish([new(png,Png(83),IntPtr.Zero)]);
            using(var locked=new ManualResetEventSlim())using(var release=new ManualResetEventSlim()) {
                bool opened=false;
                var holder=new Thread(()=> { for(int i=0;i<5&&!opened;i++) { opened=OpenClipboard(IntPtr.Zero); if(!opened)Thread.Sleep(20); } locked.Set(); try { if(opened)release.Wait(TimeSpan.FromSeconds(3)); } finally { if(opened)CloseClipboard(); } });
                holder.Start(); locked.Wait();
                try {
                    if(!opened)throw new IOException("Could not lock clipboard for native retry check.");
                    using var unlock=new System.Windows.Forms.Timer { Interval=140 }; unlock.Tick+=(_,_)=> { unlock.Stop(); release.Set(); }; unlock.Start();
                    count++; Pump(()=>service.History.Entries.Count(e=>e.IsImage)==count);
                    check((int)typeof(App.ClipboardListener).GetField("attempts",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(native)!>=2,"Locked Windows clipboard succeeds after asynchronous retry");
                } finally { release.Set(); holder.Join(); }
            }
            using(var unavailable=new DelayedOwner(png,Png(84),sequence=>lastWrite=sequence)) {
                unavailable.Publish();
                var timer=(System.Windows.Forms.Timer)typeof(App.ClipboardListener).GetField("retry",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(native)!;
                Pump(()=>(int)typeof(App.ClipboardListener).GetField("attempts",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(native)!>=5&&!timer.Enabled);
                check(service.History.Entries.Count(e=>e.IsImage)==count,"Unavailable image stops retrying without polling or adding an entry");
            }
            void Quiet() { var wait=Stopwatch.StartNew(); while(wait.ElapsedMilliseconds<200) { Application.DoEvents(); Thread.Sleep(5); } }
            var mixed=service.History.Entries.ToArray(); copied=service.CopyAsync(mixed.First(e=>e.IsImage).Id); Pump(()=>copied.IsCompleted); lastWrite=GetClipboardSequenceNumber(); Quiet();
            check(copied.Result&&service.History.Entries.SequenceEqual(mixed),"Native recopy after delayed and interleaved changes creates no duplicate or reordering");

        }
        finally
        {
            // Do not overwrite a newer user clipboard change.
            try { if(lastWrite!=0&&(GetClipboardSequenceNumber()==lastWrite||ClipboardOwnedByTest()))Set(backup,dispatcher.Handle); }
            finally { foreach(var saved in backup)if(saved.Bitmap!=IntPtr.Zero)DeleteObject(saved.Bitmap); if(Directory.Exists(directory))Directory.Delete(directory,true); }
        }
    }
    private sealed class DelayedOwner:NativeWindow,IDisposable
    {
        private readonly uint format; private readonly byte[] bytes; private readonly Action<uint> changed;
        internal bool Ready; internal int Requests;
        internal DelayedOwner(uint format,byte[] bytes,Action<uint> changed) { this.format=format; this.bytes=bytes; this.changed=changed; CreateHandle(new CreateParams { Parent=new IntPtr(-3),Caption="Clipboard delayed renderer check" }); }
        internal void Publish()
        {
            if(!OpenClipboard(Handle))throw new IOException("Cannot declare delayed clipboard data.");
            try { if(!EmptyClipboard())throw new IOException(); SetClipboardData(format,IntPtr.Zero); changed(GetClipboardSequenceNumber()); }
            finally { CloseClipboard(); }
        }
        protected override void WndProc(ref Message message)
        {
            if(message.Msg==0x0305&&(uint)message.WParam.ToInt64()==format) {
                Requests++;
                if(Ready&&GetClipboardOwner()==Handle) {
                    var handle=GlobalAlloc(2,(UIntPtr)bytes.Length); var pointer=GlobalLock(handle);
                    try { Marshal.Copy(bytes,0,pointer,bytes.Length); } finally { GlobalUnlock(handle); }
                    if(SetClipboardData(format,handle)==IntPtr.Zero)GlobalFree(handle); else changed(GetClipboardSequenceNumber());
                }
                return;
            }
            base.WndProc(ref message);
        }
        public void Dispose()=>DestroyHandle();
    }
    private static bool ClipboardOwnedByTest()
    { var owner=GetClipboardOwner(); if(owner==IntPtr.Zero)return false; GetWindowThreadProcessId(owner,out uint process); return process==(uint)Environment.ProcessId; }
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardOwner();
    private sealed record Saved(uint Format,byte[]? Bytes,IntPtr Bitmap);
    private static List<Saved> Backup()
    {
        if(!OpenClipboard(IntPtr.Zero))throw new IOException("Cannot preserve busy Windows clipboard.");
        var result=new List<Saved>(); long total=0;
        try {
            uint format=0; while((format=EnumClipboardFormats(format))!=0) {
                var handle=GetClipboardData(format);
                if(format==2) { var copy=CopyImage(handle,0,0,0,0x2000); if(copy==IntPtr.Zero)throw new IOException("Cannot preserve bitmap clipboard."); result.Add(new(format,null,copy)); continue; }
                ulong size=GlobalSize(handle).ToUInt64(); total+=(long)size;
                if(size==0||total>128*1024*1024)throw new IOException("Clipboard format cannot safely be preserved; native test aborted before writing.");
                var pointer=GlobalLock(handle); if(pointer==IntPtr.Zero)throw new IOException("Cannot preserve clipboard data.");
                try { var bytes=new byte[(int)size]; Marshal.Copy(pointer,bytes,0,bytes.Length); result.Add(new(format,bytes,IntPtr.Zero)); } finally { GlobalUnlock(handle); }
            }
            return result;
        }
        catch { foreach(var item in result)if(item.Bitmap!=IntPtr.Zero)DeleteObject(item.Bitmap); throw; }
        finally { CloseClipboard(); }
    }
    private static void Set(List<Saved> formats,IntPtr owner,Action<uint>? changed=null)
    {
        bool opened=false; for(int i=0;i<5&&!opened;i++) { opened=OpenClipboard(owner); if(!opened)Thread.Sleep(40); }
        if(!opened)throw new IOException("Windows clipboard busy during native test.");
        try {
            if(!EmptyClipboard())throw new IOException("Cannot replace test clipboard."); changed?.Invoke(GetClipboardSequenceNumber());
            foreach(var item in formats) {
                IntPtr handle;
                if(item.Bytes!=null) {
                    handle=GlobalAlloc(2,(UIntPtr)item.Bytes.Length); if(handle==IntPtr.Zero)throw new OutOfMemoryException();
                    var pointer=GlobalLock(handle); try { Marshal.Copy(item.Bytes,0,pointer,item.Bytes.Length); } finally { GlobalUnlock(handle); }
                }
                else handle=CopyImage(item.Bitmap,0,0,0,0x2000);
                if(SetClipboardData(item.Format,handle)==IntPtr.Zero) { if(item.Bytes!=null)GlobalFree(handle); else DeleteObject(handle); throw new IOException("Cannot publish native format "+item.Format); }
                changed?.Invoke(GetClipboardSequenceNumber());
            }
        }
        finally { CloseClipboard(); }
    }
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern uint EnumClipboardFormats(uint format);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string name);
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint format,IntPtr value);
    [DllImport("user32.dll")] private static extern IntPtr CopyImage(IntPtr image,uint type,int width,int height,uint flags);
    [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr value);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags,UIntPtr size);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr value);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr value);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
}
