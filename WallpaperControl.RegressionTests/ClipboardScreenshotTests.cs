extern alias WallpaperApp;
using App=WallpaperApp::WallpaperControl;
using System.Drawing;
using System.Drawing.Imaging;
using System.Buffers.Binary;
using Microsoft.Win32;
using System.Diagnostics;

internal static class ClipboardScreenshotTests
{
    internal static void Run(Action<bool,string> check)
    {
        string root=Path.Combine(Path.GetTempPath(),"WallpaperControl-ScreenshotChecks-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Storage(root,check).GetAwaiter().GetResult();
            Exception? error=null;
            var thread=new Thread(()=> { try { Ui(root,check); } catch(Exception ex) { error=ex; } });
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if(error!=null)throw error;
        }
        finally { Directory.Delete(root,true); }
    }
    private static byte[] Png(int number=0,int width=640,int height=360)
    {
        using var image=new Bitmap(width,height,PixelFormat.Format32bppArgb);
        using(var graphics=Graphics.FromImage(image))graphics.Clear(Color.FromArgb(150,number%256,60,90));
        image.SetPixel(0,1,Color.FromArgb(255,number%256,60,90));
        using var output=new MemoryStream(); image.Save(output,ImageFormat.Png); return output.ToArray();
    }
    private static async Task Storage(string root,Action<bool,string> check)
    {
        var options=new App.ClipboardWidgetSettings();
        var tiedHistory=new App.ClipboardHistory(); tiedHistory.Configure(50); var tiedTime=DateTimeOffset.UtcNow;
        for(int i=0;i<25;i++)tiedHistory.Observe("same-time "+i,tiedTime);
        var textOrder=tiedHistory.Entries.Select(e=>e.Id).ToArray();
        tiedHistory.ReplaceScreenshots([new(Guid.NewGuid(),tiedTime,2,2,new string('A',64),true)]);
        check(tiedHistory.Entries.Where(e=>!e.IsImage).Select(e=>e.Id).SequenceEqual(textOrder),"Image updates preserve original text order even when timestamps tie");
        check(options.CaptureScreenshots&&options.KeepScreenshots&&options.ScreenshotCapacity==20&&options.ScreenshotLifetimeHours==24,"Screenshot defaults capture, retain, 20 images and 24 hours");
        var png=Png(); using var capture=new App.ClipboardScreenshotCapture(png);
        var decoded=capture.Decode(); using(var bitmap=decoded.Image)
        {
            check(decoded.Png.SequenceEqual(png)&&bitmap.Size==new Size(640,360)&&bitmap.GetPixel(0,0).A==150,"PNG original retained losslessly with alpha and resolution");
            using var thumbnail=App.ClipboardScreenshotStore.CreateThumbnail(bitmap);
            check(thumbnail.Width==160&&thumbnail.Height==90,"Thumbnail preserves aspect ratio and is small");
        }
        foreach(var size in new[]{new Size(40,600),new Size(2000,20),new Size(1,1)})
        {
            using var input=new Bitmap(size.Width,size.Height); using var thumbnail=App.ClipboardScreenshotStore.CreateThumbnail(input);
            check(thumbnail.Width<=160&&thumbnail.Height<=96&&thumbnail.Width>0&&thumbnail.Height>0,"Portrait, panorama and tiny thumbnails remain bounded: "+size);
        }
        check(!App.ClipboardScreenshotCapture.ValidDimensions(0,100)&&!App.ClipboardScreenshotCapture.ValidDimensions(16385,1)&&!App.ClipboardScreenshotCapture.ValidDimensions(8000,8000),"Invalid and excessively large dimensions rejected");
        foreach(var bytes in new[]{Array.Empty<byte>(),new byte[40],new byte[App.ClipboardScreenshotCapture.MaximumPngBytes+1]})
        {
            bool rejected=false; try { using var input=new App.ClipboardScreenshotCapture(bytes); var result=input.Decode(); result.Image.Dispose(); } catch { rejected=true; }
            check(rejected,"Malformed/oversized PNG rejected without crash");
        }
        foreach(int header in new[]{40,124})
        {
            var dib=new byte[header+16]; BinaryPrimitives.WriteInt32LittleEndian(dib,header); BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4),2); BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8),2);
            BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12),1); BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14),24);
            dib[header+2]=255; dib[header+8+2]=255;
            using var input=new App.ClipboardScreenshotCapture(dib,true); var result=input.Decode(); using var image=result.Image;
            check(image.Size==new Size(2,2)&&result.Png[0]==137&&image.GetPixel(0,0).R==255,"CF_DIB / CF_DIBV5 screenshot converted to lossless PNG: "+header);
        }
        bool badDib=false; try { using var input=new App.ClipboardScreenshotCapture(new byte[40],true); var result=input.Decode(); result.Image.Dispose(); } catch { badDib=true; }
        check(badDib,"Malformed DIB rejected");
        string blocked=Path.Combine(root,"blocked-directory"); File.WriteAllText(blocked,"existing file");
        using(var blockedStore=new App.ClipboardScreenshotStore(blocked)) {
            bool accessFailure=false; try { using var unused=await blockedStore.InitializeAsync(options); } catch(IOException) { accessFailure=true; }
            check(accessFailure,"Image directory creation failure is reported rather than silently ignored");
        }
        string directory=Path.Combine(root,"persistent"); Guid id;
        using(var store=new App.ClipboardScreenshotStore(directory))
        {
            using var empty=await store.InitializeAsync(options); check(empty.Entries.Count==0&&Directory.Exists(directory),"Enabled screenshot startup creates directory before any image");
            using var first=(await store.CaptureAsync(capture))!; id=first.Entries.Single().Id;
            check(File.ReadAllBytes(store.Original(id)).SequenceEqual(png),"Original PNG saved independently from source");
            using var repeat=(await store.CaptureAsync(capture))!;
            check(repeat.Entries.Count==1&&repeat.Entries[0].Id==id,"Identical pixels deduplicate with stable identity");
            using var suppressed=await store.CaptureAsync(capture,decoded.Hash); check(suppressed==null,"Own image clipboard echo creates no duplicate or timestamp update");
            var full=await store.LoadOriginalAsync(id); using var fullImage=full.Image; check(full.Png.SequenceEqual(png)&&fullImage.Width==640,"Original loaded only on demand for copy");
        }
        using(var store=new App.ClipboardScreenshotStore(directory))
        {
            using var restored=await store.InitializeAsync(options); check(restored.Entries.Count==1&&restored.Entries[0].Id==id&&restored.Thumbnails.Count==1,"Restart restores screenshot metadata and thumbnail");
            string orphan=store.Original(Guid.NewGuid()); File.WriteAllBytes(orphan,png);
            using var authoritative=await store.InitializeAsync(options);
            check(authoritative.Entries.Count==1&&!File.Exists(orphan),"Valid index does not resurrect unreferenced images");
            string unrelated=Path.Combine(directory,"other.png"); File.WriteAllText(unrelated,"leave alone");
            using var removed=await store.RemoveAsync(id);
            check(removed.Entries.Count==0&&!File.Exists(store.Original(id))&&File.Exists(unrelated),"Manual delete removes original and thumbnail but preserves foreign files");
            check(!Directory.EnumerateFiles(directory,"*.thumb.png").Any(),"Manual delete removes thumbnail file");
        }
        foreach(int capacity in new[]{10,20,50})
        {
            using var store=new App.ClipboardScreenshotStore(Path.Combine(root,"cap"+capacity)); options.ScreenshotCapacity=capacity;
            using var initialized=await store.InitializeAsync(options);
            for(int i=0;i<capacity+2;i++) { using var input=new App.ClipboardScreenshotCapture(Png(i)); using var item=await store.CaptureAsync(input); }
            using var current=await store.CleanupAsync(DateTimeOffset.UtcNow);
            check(current.Entries.Count==capacity&&Directory.GetFiles(Path.GetDirectoryName(store.Original(Guid.Empty))!,"screenshot-*.png").Length==capacity*2,"Screenshot count and files bounded to "+capacity+" actual entries="+current.Entries.Count+" files="+Directory.GetFiles(Path.GetDirectoryName(store.Original(Guid.Empty))!,"screenshot-*.png").Length);
            using var cleared=await store.RemoveAsync(null); check(cleared.Entries.Count==0,"Clear removes screenshots for capacity "+capacity);
        }
        foreach(int hours in new[]{1,24,168,0})
        {
            using var store=new App.ClipboardScreenshotStore(Path.Combine(root,"ttl"+hours)); options.ScreenshotLifetimeHours=hours;
            using var initialized=await store.InitializeAsync(options); using var saved=await store.CaptureAsync(capture);
            using var expired=await store.CleanupAsync(DateTimeOffset.UtcNow.AddHours(hours==0?1000:hours+1));
            check(expired.Entries.Count==(hours==0?1:0),"Lifetime applied: "+hours);
            if(hours>0)check(!File.Exists(store.Original(saved!.Entries.Single().Id)),"Expired screenshot removed from disk: "+hours);
        }
        options.ScreenshotLifetimeHours=24; options.KeepScreenshots=false;
        string temp=Path.Combine(root,"temporary");
        using(var store=new App.ClipboardScreenshotStore(temp)) { using var init=await store.InitializeAsync(options); using var saved=await store.CaptureAsync(capture); check(saved!.Entries.Count==1&&!saved.Entries[0].Persistent,"Temporary images are usable during current run"); }
        using(var store=new App.ClipboardScreenshotStore(temp)) { using var init=await store.InitializeAsync(options); check(init.Entries.Count==0&&!Directory.GetFiles(temp,"screenshot-*").Any(),"Temporary screenshots cleaned on next startup"); }
        using(var store=new App.ClipboardScreenshotStore(temp)) { using var init=await store.InitializeAsync(options); using var saved=await store.CaptureAsync(capture); }
        File.Delete(Path.Combine(temp,"history.json")); options.KeepScreenshots=true;
        using(var store=new App.ClipboardScreenshotStore(temp)) { using var init=await store.InitializeAsync(options); check(init.Entries.Count==0,"Temporary image not recovered as persistent after index loss"); }
        string recovery=Path.Combine(root,"recovery");
        using(var store=new App.ClipboardScreenshotStore(recovery)) { using var init=await store.InitializeAsync(options); using var saved=await store.CaptureAsync(capture); id=saved!.Entries.Single().Id; }
        File.WriteAllText(Path.Combine(recovery,"history.json"),"{broken");
        using(var store=new App.ClipboardScreenshotStore(recovery))
        {
            using var recovered=await store.InitializeAsync(options); check(recovered.Entries.Count==1&&recovered.Entries[0].Id==id,"Valid original recovered after interrupted/corrupt index");
            File.WriteAllBytes(Path.Combine(recovery,"screenshot-"+id.ToString("N")+".thumb.png"),[1,2,3]);
            using var regenerated=await store.CleanupAsync(DateTimeOffset.UtcNow); check(regenerated.Entries.Count==1&&regenerated.Thumbnails.Count==1,"Damaged thumbnail regenerated from valid original in background");
            File.WriteAllBytes(store.Original(id),[1,2,3]); using var cleaned=await store.CleanupAsync(DateTimeOffset.UtcNow); check(cleaned.Entries.Count==0&&!File.Exists(store.Original(id)),"Damaged original removed safely");
        }
    }
    private static void PumpUntil(Func<bool> done)
    {
        var timeout=Stopwatch.StartNew(); while(!done()&&timeout.Elapsed<TimeSpan.FromSeconds(10)) { Application.DoEvents(); Thread.Sleep(5); }
        if(!done())throw new TimeoutException("Screenshot async UI operation did not finish.");
    }
    private static void Ui(string root,Action<bool,string> check)
    {
        using var contextControl=new Control(); _=contextControl.Handle;
        NativeSafety(check);
        using var source=new FakeSource(); var options=new App.ClipboardWidgetSettings();
        using var service=new App.ClipboardService(source,new App.ClipboardScreenshotStore(Path.Combine(root,"ui")),options);
        PumpUntil(()=>source.CaptureImages);
        source.Emit("first   text"); source.EmitImage(Png()); PumpUntil(()=>service.History.Entries.Count==2); source.Emit("second");
        var image=service.History.Entries.Single(e=>e.IsImage); var before=service.History.Entries.ToArray();
        var copied=service.CopyAsync(image.Id); PumpUntil(()=>copied.IsCompleted); Application.DoEvents();
        check(copied.Result&&source.WrittenPng!.SequenceEqual(Png())&&source.WriteThread==Environment.CurrentManagedThreadId,"Image copy restores original PNG on UI STA thread result="+copied.Result+" writeThread="+source.WriteThread+" ui="+Environment.CurrentManagedThreadId+" context="+SynchronizationContext.Current?.GetType().Name+" bytes="+(source.WrittenPng?.SequenceEqual(Png())));
        Thread.Sleep(100); Application.DoEvents(); check(service.History.Entries.SequenceEqual(before),"Copy leaves mixed chronology/timestamps unchanged despite simulated echo");
        source.CanWrite=false; var busyCopy=service.CopyAsync(image.Id); PumpUntil(()=>busyCopy.IsCompleted);
        check(!busyCopy.Result&&service.History.Entries.SequenceEqual(before),"Busy clipboard image write fails safely without changing history"); source.CanWrite=true;
        check(service.Copy(before[^1].Id)&&source.WrittenText=="first   text","Existing exact text copy unaffected");
        service.Configure(5); for(int i=0;i<8;i++)source.Emit("text "+i);
        check(service.History.Entries.Count(e=>!e.IsImage)==5&&service.History.Entries.Count(e=>e.IsImage)==1,"Text and screenshot limits independent");
        using var widget=new App.ClipboardWidgetForm(service,new(){MaximumHeight=300},"de",_=>{});
        var feedback=typeof(App.ClipboardWidgetForm).GetField("feedbackKey",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
        widget.Copy(image.Id); PumpUntil(()=>feedback.GetValue(widget) as string=="ClipboardImageCopied");
        check(source.WrittenPng!.SequenceEqual(Png()),"Widget reports image copied only after successful original image write");
        source.CanWrite=false; widget.Copy(image.Id); PumpUntil(()=>feedback.GetValue(widget) as string=="ClipboardBusy");
        check(feedback.GetValue(widget) as string=="ClipboardBusy","Failed image copy retains busy status instead of success");
        source.CanWrite=true; widget.Copy(service.History.Entries.First(e=>!e.IsImage).Id);
        PumpUntil(()=>feedback.GetValue(widget) as string=="ClipboardCopied");
        check(feedback.GetValue(widget) as string=="ClipboardCopied","Widget retains text copied status for text entries");
        foreach(var language in new[]{"de","en","es","fr","ja"})
            check(App.Localization.Get("ClipboardImageCopied",language)!="ClipboardImageCopied"&&App.Localization.Get("ClipboardImageCopied",language)!=App.Localization.Get("ClipboardCopied",language),"Image copy feedback localized distinctly from text: "+language);
        foreach(var style in Enum.GetValues<App.SystemWidgetStyle>())foreach(bool dark in new[]{false,true})
        {
            widget.Apply(new(){Style=style,MaximumHeight=300},"de"); widget.ApplyTheme(dark); using var rendered=widget.RenderBitmap();
            check(rendered.Width==390&&rendered.Height==300,"Mixed screenshot layout remains same viewport: "+style+" dark="+dark);
            if(style==App.SystemWidgetStyle.Glow&&dark&&Environment.GetEnvironmentVariable("WALLPAPERCONTROL_TEST_ARTIFACTS") is { Length:>0 } outputDirectory) {
                Directory.CreateDirectory(outputDirectory);
                var viewport=(App.CalendarViewport)typeof(App.ClipboardWidgetForm).GetField("viewport",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(widget)!;
                viewport.SetOffset(1000); using var sample=widget.RenderBitmap(); sample.Save(Path.Combine(outputDirectory,"clipboard-screenshots.png"),ImageFormat.Png);
            }
        }
        var showMenu=typeof(App.ClipboardWidgetForm).GetMethod("ShowMenu",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
        var contextMenu=(ContextMenuStrip)typeof(App.ClipboardWidgetForm).GetField("menu",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(widget)!;
        foreach(var language in new[]{"de","en","es","fr","ja"}) {
            widget.Apply(new(){MaximumHeight=300},language);
            foreach(bool isImage in new[]{true,false,true,false}) {
                var selected=isImage?image:service.History.Entries.First(e=>!e.IsImage);
                showMenu.Invoke(widget,[selected.Id]);
                check(contextMenu.Items[0].Text==App.Localization.Get(isImage?"ClipboardImageCopy":"ClipboardCopy",language),"Context copy label follows clicked entry on rapid switch: "+language+" image="+isImage);
                check(contextMenu.Items[0].Text!="ClipboardImageCopy"&&contextMenu.Items.Count==(isImage?5:6),"Localized menu preserves other entries: "+language+" image="+isImage);
                contextMenu.Close();
            }
        }
        widget.Apply(new(){MaximumHeight=300},"de");
        var pausedBefore=service.History.Entries.ToArray();
        service.TogglePause(); source.Emit("paused text"); source.EmitImage(Png(111));
        check(!source.Enabled&&!source.CaptureImages&&service.History.Entries.SequenceEqual(pausedBefore),"Manual pause rejects both new text and screenshots");
        var pausedCopy=service.CopyAsync(image.Id); PumpUntil(()=>pausedCopy.IsCompleted);
        check(pausedCopy.Result&&service.History.Entries.SequenceEqual(pausedBefore),"Existing screenshot copies successfully during manual pause");
        service.TogglePause(); check(service.History.Entries.SequenceEqual(pausedBefore),"Manual resume does not import paused data");
        int originalFiles=Directory.GetFiles(Path.Combine(root,"ui"),"screenshot-*.png").Length;
        source.EmitImage(Png(112));
        var processingWait=Stopwatch.StartNew();
        while(Directory.GetFiles(Path.Combine(root,"ui"),"screenshot-*.png").Length==originalFiles&&processingWait.Elapsed<TimeSpan.FromSeconds(10))Thread.Sleep(5);
        check(processingWait.Elapsed<TimeSpan.FromSeconds(10),"Image processing reaches disk before manual pause without pumping UI completion");
        service.TogglePause(); service.TogglePause(); PumpUntil(()=>Pending(service)==0);
        check(service.History.Entries.SequenceEqual(pausedBefore)&&Directory.GetFiles(Path.Combine(root,"ui"),"screenshot-*.png").Length==originalFiles,"Pause and immediate resume reject in-flight image and roll back its files");
        using(var reopened=new App.ClipboardScreenshotStore(Path.Combine(root,"ui"))) {
            var restart=reopened.InitializeAsync(options); PumpUntil(()=>restart.IsCompleted); using var snapshot=restart.Result;
            check(snapshot.Entries.Select(e=>e.Id).SequenceEqual(pausedBefore.Where(e=>e.IsImage).Select(e=>e.Id)),"Rejected in-flight screenshot does not return after storage restart");
        }
        service.Remove(image.Id); PumpUntil(()=>!File.Exists(Path.Combine(root,"ui","screenshot-"+image.Id.ToString("N")+".png")));
        check(!service.History.Entries.Any(e=>e.IsImage)&&service.History.Entries.Count==5,"Deleting image preserves text history");
        source.EmitImage(Png(2)); service.Clear(); Thread.Sleep(100); Application.DoEvents();
        PumpUntil(()=>!Directory.GetFiles(Path.Combine(root,"ui"),"screenshot-*.png").Any());
        check(service.History.Entries.Count==0,"Clear during pending capture does not resurrect screenshots");
        options.CaptureScreenshots=false; service.Configure(options); source.EmitImage(Png(3)); Application.DoEvents(); check(!source.CaptureImages&&service.History.Entries.Count==0,"Capture setting disables images");
        source.Emit("works"); check(service.History.Entries.Count==1,"Disabled image capture preserves text listener");
        options.CaptureScreenshots=true; service.Configure(options);
        source.EmitImage([1,2,3]); PumpUntil(()=>Pending(service)==0); check(service.History.Entries.Count==1,"Malformed image leaves existing text history intact");
        source.EmitImage(Png(4)); service.Clear(); source.EmitImage(Png(5)); PumpUntil(()=>Pending(service)==0&&service.History.Entries.Count==1);
        check(service.History.Entries.Single().IsImage,"Fresh capture survives clearing during previous capture");
        service.Clear(); PumpUntil(()=>!Directory.GetFiles(Path.Combine(root,"ui"),"screenshot-*.png").Any());
        for(int i=0;i<12;i++) { source.EmitImage(Png(20+i)); PumpUntil(()=>Pending(service)==0); }
        options.ScreenshotCapacity=10; service.Configure(options,false); Application.DoEvents(); check(service.History.Entries.Count==12,"Uncommitted settings preview does not delete screenshot files");
        service.Configure(options,true); PumpUntil(()=>service.History.Entries.Count==10); check(service.History.Entries.Count==10,"Saved capacity change trims screenshot history");
        service.TogglePause(); source.EmitImage(Png(3)); check(!source.CaptureImages,"Pause disables screenshot capture"); service.TogglePause();
        string registry=@"Software\WallpaperControl.ScreenshotTests\"+Guid.NewGuid().ToString("N");
        try
        {
            var settings=new App.WidgetSettings { Clipboard=new(){CaptureScreenshots=false,KeepScreenshots=false,ScreenshotCapacity=50,ScreenshotLifetimeHours=168} }; settings.Save(registry);
            var restored=App.WidgetSettings.Load(registry).Clipboard;
            check(!restored.CaptureScreenshots&&!restored.KeepScreenshots&&restored.ScreenshotCapacity==50&&restored.ScreenshotLifetimeHours==168,"All screenshot preferences persist in registry");
            string directory=Path.Combine(Path.GetTempPath(),"WallpaperControl-ClipboardTests",Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(registry))));
            using(var store=new App.ClipboardScreenshotStore(directory)) { using var init=store.InitializeAsync(settings.Clipboard).GetAwaiter().GetResult(); using var capture=new App.ClipboardScreenshotCapture(Png()); using var saved=store.CaptureAsync(capture).GetAwaiter().GetResult(); }
            using(var manager=new App.WidgetManager(()=>{},registryPath:registry)) {
                manager.Start(); PumpUntil(()=>Directory.GetFiles(directory,"screenshot-*").Length==0);
                check(Directory.GetFiles(directory,"screenshot-*").Length==0,"Startup cleans temporary images even with clipboard widget disabled");
                var persistent=settings.Clipboard.Clone(); persistent.KeepScreenshots=true; persistent.ScreenshotLifetimeHours=1;
                settings.Clipboard=persistent; settings.Save(registry);
                using(var store=new App.ClipboardScreenshotStore(directory)) { using var init=store.InitializeAsync(persistent).GetAwaiter().GetResult(); using var input=new App.ClipboardScreenshotCapture(Png(),copied:DateTimeOffset.UtcNow.AddMinutes(-30)); using var saved=store.CaptureAsync(input).GetAwaiter().GetResult(); }
                // Simulate an old record while retaining a valid PNG and authoritative index.
                string indexPath=Path.Combine(directory,"history.json");
                var metadata=System.Text.Json.JsonSerializer.Deserialize<List<App.ClipboardScreenshot>>(File.ReadAllText(indexPath))!;
                File.WriteAllText(indexPath,System.Text.Json.JsonSerializer.Serialize(metadata.Select(e=>e with { Copied=DateTimeOffset.UtcNow.AddHours(-2) })));
                typeof(App.WidgetManager).GetMethod("MaintainDisabledClipboardImages",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(manager,null);
                PumpUntil(()=>Directory.GetFiles(directory,"screenshot-*").Length==0);
                check(Directory.GetFiles(directory,"screenshot-*").Length==0,"Background maintenance expires screenshots with widget disabled");
                settings.Clipboard.KeepScreenshots=false; settings.Clipboard.ScreenshotLifetimeHours=168; settings.Save(registry);
            }
            Directory.Delete(directory,true);
            using var editor=new App.WidgetSettingsEditor(settings);
            foreach(string language in new[]{"de","en","fr","es","ja"})
            {
                editor.ApplyPresentation(true,language); var value=editor.ReadWidgetSettings(false).Clipboard;
                check(!value.CaptureScreenshots&&!value.KeepScreenshots&&value.ScreenshotCapacity==50&&value.ScreenshotLifetimeHours==168,"Editor localization preserves screenshot settings: "+language);
            }
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(registry,false); }
    }
    private static void NativeSafety(Action<bool,string> check)
    {
        using var listener=new App.ClipboardListener();
        bool wrongThreadRejected=Task.Run(()=> {
            try { using var image=new Bitmap(2,2); listener.WriteImage(image,Png()); return false; }
            catch(InvalidOperationException) { return true; }
        }).GetAwaiter().GetResult();
        check(wrongThreadRejected,"Native image writes reject a non-owner thread before touching clipboard");
        using var locked=new ManualResetEventSlim(); using var release=new ManualResetEventSlim(); bool opened=false;
        var lockThread=new Thread(()=> {
            try {
                for(int i=0;i<5&&!opened;i++) { opened=OpenClipboard(IntPtr.Zero); if(!opened)Thread.Sleep(20); }
                locked.Set(); if(opened)release.Wait(TimeSpan.FromSeconds(3));
            }
            finally { if(opened)CloseClipboard(); }
        });
        int changes=0; listener.TextChanged+=_=>changes++; listener.ImageChanged+=capture=> { changes++; capture.Dispose(); }; listener.CaptureImages=true;
        lockThread.Start(); locked.Wait();
        try {
            if(!opened) { Console.WriteLine("SKIP: Windows clipboard already held by another application; native retry check not run."); return; }
            typeof(App.ClipboardListener).GetField("pendingSequence",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(listener,typeof(App.ClipboardListener).GetField("sequence",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(listener));
            typeof(App.ClipboardListener).GetMethod("ReadUpdate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(listener,null);
            var attempts=typeof(App.ClipboardListener).GetField("attempts",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            PumpUntil(()=>(int)attempts.GetValue(listener)!>=5);
            var timer=(System.Windows.Forms.Timer)typeof(App.ClipboardListener).GetField("retry",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(listener)!;
            check((int)attempts.GetValue(listener)! ==5&&!timer.Enabled&&changes==0,"Native locked clipboard stops after five retries without importing or modifying data");
        }
        finally { release.Set(); lockThread.Join(); }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return:System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr owner);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return:System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool CloseClipboard();
    private static int Pending(App.ClipboardService service)=>(int)typeof(App.ClipboardService).GetField("pendingImages",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(service)!;
    private sealed class FakeSource:App.IClipboardSource,App.IClipboardImageSource
    {
        public event Action<string>? TextChanged;
        public event Action<App.ClipboardScreenshotCapture>? ImageChanged;
        public bool Enabled {get;set;}=true;
        public bool CaptureImages {get;set;}
        internal byte[]? WrittenPng; internal string? WrittenText; internal int WriteThread; internal bool CanWrite=true;
        internal void Emit(string text) { if(Enabled)TextChanged?.Invoke(text); }
        internal void EmitImage(byte[] bytes) { if(Enabled&&CaptureImages)ImageChanged?.Invoke(new(bytes)); }
        public bool Write(string text) { WrittenText=text; return true; }
        public bool WriteImage(Bitmap image,byte[] png) { if(!CanWrite)return false; WrittenPng=png.ToArray(); WriteThread=Environment.CurrentManagedThreadId; EmitImage(png.ToArray()); return Thread.CurrentThread.GetApartmentState()==ApartmentState.STA; }
        public void Dispose() { TextChanged=null; ImageChanged=null; }
    }
}
