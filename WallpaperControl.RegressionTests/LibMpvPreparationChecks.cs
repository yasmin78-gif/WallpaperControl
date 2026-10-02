extern alias WallpaperApp;
using Video=WallpaperApp::WallpaperControl.Video;
using System.Diagnostics;
using System.Runtime.InteropServices;

internal static class LibMpvPreparationChecks
{
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    internal static void Fill(string dll,string fixtures)
    {
        Exception? failure=null;
        var thread=new Thread(()=>
        {
            try
            {
                int passed=0;
                foreach(string name in new[]{"fill-wide.mp4","fill-portrait.mp4"})
                {
                    using var session=new MpvPreparedSession(dll,Path.Combine(fixtures,name),0);
                    session.Prepare(()=>Application.DoEvents(),()=>true); session.Present(()=>true); session.Published=true; session.Activate(true,false);
                    DwmFlush(); var bounds=session.Host.Bounds;
                    using var image=new Bitmap(bounds.Width,bounds.Height);
                    using(var graphics=Graphics.FromImage(image)) graphics.CopyFromScreen(bounds.Location,Point.Empty,bounds.Size);
                    int cx=bounds.Width/2,cy=bounds.Height/2;
                    bool White(Color p)=>p.R>220 && p.G>220 && p.B>220;
                    int width=0,height=0;
                    for(int x=cx-600;x<=cx+600;x++) if(White(image.GetPixel(x,cy))) width++;
                    for(int y=cy-600;y<=cy+600;y++) if(White(image.GetPixel(cx,y))) height++;
                    if(width<100 || Math.Abs(width-height)>3) throw new InvalidOperationException($"Distorted/missing square: {width}x{height}");
                    passed++; Console.WriteLine($"PASS {name}: source square remains square on desktop: {width}x{height}; dpi={GetDpiForWindow(session.Host.Handle)}");
                    if(!Blue(image.GetPixel(cx,100)) || !Blue(image.GetPixel(420,cy))) throw new InvalidOperationException("Fill contains bars at exposed source-background points");
                    passed++; Console.WriteLine($"PASS {name}: full desktop filled without stretching or letterbox bars");
                }
                Console.WriteLine($"All {passed} libmpv Fill checks passed.");
            }
            catch(Exception ex) { failure=ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if(failure!=null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    internal static void Run(string dll,string fixtures)
    {
        Exception? failure=null;
        var thread=new Thread(()=> { try { Measure(dll,fixtures); } catch(Exception ex) { failure=ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if(failure!=null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static Color Pixel(Point point)
    { DwmFlush(); using var image=new Bitmap(1,1); using(var g=Graphics.FromImage(image)) g.CopyFromScreen(point,Point.Empty,image.Size); return image.GetPixel(0,0); }
    private static bool Blue(Color p)=>p.B>180 && p.R<50 && p.G<50;
    private static bool Green(Color p)=>p.G>180 && p.R<50 && p.B<50;
    private static void Pump(MpvPreparedSession stable,int milliseconds)
    { var clock=Stopwatch.StartNew(); while(clock.ElapsedMilliseconds<milliseconds) { Application.DoEvents(); stable.Poll(); Thread.Sleep(2); } }
    private static void Measure(string dll,string fixtures)
    {
        int passed=0; long generation=1; int reasons=1; bool audio=false;
        void Check(bool ok,string name) { if(!ok) throw new InvalidOperationException("FAIL "+name); passed++; Console.WriteLine("PASS "+name); }
        string first=Path.Combine(fixtures,"loop-colors.mp4"), second=Path.Combine(fixtures,"switch-colors.mp4");
        using var probe=new LoopAudioProbe();
        MpvPreparedSession stable=new(dll,first,0);
        try
        {
            stable.Prepare(()=>Application.DoEvents(),()=>true); stable.Present(()=>true); stable.Published=true; stable.Activate(true,false);
            Pump(stable,150); probe.Poll();
            var point=stable.Host.ProbeOrigin+new Size(240,101);
            Check(Blue(Pixel(point)),"Preparation baseline is a real visible paused blue frame");
            foreach(string invalid in new[]{"missing-mpv.mp4","corrupt.mp4"})
            {
                bool rejected=false;
                try { using var bad=new MpvPreparedSession(dll,Path.Combine(fixtures,invalid),stable.Host.Handle); bad.Prepare(()=> { Application.DoEvents(); stable.Poll(); },()=>true); }
                catch(InvalidOperationException) { rejected=true; }
                Check(rejected && MpvPreparedSession.Live==1 && Blue(Pixel(point)),invalid+" native startup failure releases candidate and preserves stable video");
            }
            bool policyRejected=false;
            try { Video.VideoFileValidation.ValidateCodec(Path.Combine(fixtures,"unsupported.mp4")); }
            catch(Exception ex) when(ex is NotSupportedException or COMException) { policyRejected=true; }
            Check(policyRejected && Blue(Pixel(point)),"Existing H.264 validation rejects unsupported codec before candidate creation");
            using(var cancel=new CancellationTokenSource())
            {
                bool rejected=false;
                using(var abandoned=new MpvPreparedSession(dll,second,stable.Host.Handle))
                {
                    try { abandoned.Prepare(()=> { Application.DoEvents(); cancel.Cancel(); },()=>!cancel.IsCancellationRequested); }
                    catch(OperationCanceledException) { rejected=true; }
                }
                Check(rejected && MpvPreparedSession.Live==1 && Blue(Pixel(point)),"Cancelled real native preparation releases candidate and preserves A");
            }
            using(var stale=new MpvPreparedSession(dll,second,stable.Host.Handle))
            {
                long request=generation; bool rejected=false;
                try { stale.Prepare(()=> { Application.DoEvents(); generation+=3; },()=>request==generation); }
                catch(OperationCanceledException) { rejected=true; }
                stale.Dispose();
                bool eventBlocked=false; try { stale.Poll(); } catch(ObjectDisposedException) { eventBlocked=true; }
                Check(rejected && eventBlocked && MpvPreparedSession.Live==1 && Blue(Pixel(point)),"Rapid A/B/C generations invalidate obsolete candidate; stale event polling blocked after release");
            }
            using(var timed=new MpvPreparedSession(dll,second,stable.Host.Handle))
            {
                bool rejected=false;
                try { timed.Prepare(()=> { Application.DoEvents(); Thread.Sleep(10); },()=>true,timeoutMs:1); }
                catch(TimeoutException) { rejected=true; }
                timed.Dispose(); Check(rejected && MpvPreparedSession.Live==1 && Blue(Pixel(point)),"Real native preparation timeout rolls back without touching A");
            }
            using(var shellChanged=new MpvPreparedSession(dll,second,stable.Host.Handle))
            {
                shellChanged.Prepare(()=> { Application.DoEvents(); stable.Poll(); },()=>true);
                bool rejected=false;
                // Injected request/lease invalidation: do not restart the user's Explorer.
                shellChanged.Host.LeaseValidOverride=()=>false;
                try { shellChanged.Present(()=>true); } catch(OperationCanceledException) { rejected=true; }
                shellChanged.Dispose(); Check(rejected && MpvPreparedSession.Live==1 && Blue(Pixel(point)),"Lease/generation invalidation immediately before presentation preserves A");
            }
            using(var shellDuring=new MpvPreparedSession(dll,second,stable.Host.Handle))
            {
                bool rejected=false;
                try { shellDuring.Prepare(()=> { Application.DoEvents(); shellDuring.Host.LeaseValidOverride=()=>false; },()=>true); }
                catch(OperationCanceledException) { rejected=true; }
                shellDuring.Dispose(); Check(rejected && MpvPreparedSession.Live==1 && Blue(Pixel(point)),"Injected shell lease change during real native preparation releases B and preserves A");
            }
            foreach(string file in new[]{second,second,first})
            {
                var candidate=new MpvPreparedSession(dll,file,stable.Host.Handle);
                bool transferred=false;
                try
                {
                reasons=0; stable.Activate(false,false);
                bool oldStayed=true;
                candidate.Prepare(()=>
                {
                    Application.DoEvents(); stable.Poll(); probe.Poll(); var p=Pixel(point); oldStayed&=Blue(p)||Green(p);
                    reasons|=1; stable.Activate(true,false); // Manual pause arrives during real preroll.
                    reasons|=2; // Fullscreen pause then arrives independently.
                },()=>true);
                double pausedPosition=stable.Player.Double("time-pos"); Pump(stable,150);
                Check(Math.Abs(stable.Player.Double("time-pos")-pausedPosition)<0.01,"Manual pause arriving during preparation pauses A without cancelling B");
                Check(oldStayed && candidate.Player.String("mute")=="yes","A remains visible while real B prerolls muted");
                candidate.Present(()=>true); DwmFlush();
                Check(file==first?Blue(Pixel(point)):Green(Pixel(point)),"B prepared frame is immediately composited at handoff, including same-file reapply");
                candidate.Published=true;
                var retired=stable; stable=candidate; transferred=true;
                retired.Dispose();
                candidate.Activate(reasons!=0,audio,volume:50);
                Check(candidate.Player.String("pause")=="yes" && candidate.Player.String("mute")=="yes" && Math.Abs(candidate.Player.Double("volume")-79.3700525984)<0.0001,"Latest manual/fullscreen policy and mapped volume survive handoff");
                reasons&=~2; stable.Activate(reasons!=0,audio);
                Check(stable.Player.String("pause")=="yes","Removing fullscreen alone preserves manual pause");
                }
                finally { if(!transferred) candidate.Dispose(); }
            }
            using(var hidden=new MpvPreparedSession(dll,second,stable.Host.Handle,hidden:true))
            {
                hidden.Prepare(()=> { Application.DoEvents(); stable.Poll(); probe.Poll(); },()=>true);
                Check(!IsWindowVisible(hidden.Host.Handle) && Blue(Pixel(point)),"Completely hidden candidate prerolls without covering paused A");
                hidden.Present(()=>true); DwmFlush();
                Check(Green(Pixel(point)) && hidden.Player.String("pause")=="yes","Showing hidden prepared candidate immediately exposes its valid retained D3D11 frame");
            }
            probe.Poll(); Check(probe.SoundFrames==0,"Endpoint loopback confirms candidates are inaudible throughout muted preparation/handoff tests");
            reasons=0; audio=true; stable.Activate(false,true); Pump(stable,400); probe.Poll();
            Check(probe.SoundFrames>0 && stable.Player.String("mute")=="no","Published session becomes audible only after old native player release");
            stable.Activate(true,false);
            using(var black=new MpvPreparedSession(dll,Path.Combine(fixtures,"loop-black.mp4"),stable.Host.Handle))
            {
                black.Prepare(()=> { Application.DoEvents(); stable.Poll(); },()=>true);
                Check(black.Prepared && black.VoPresented,"Black source frame is valid readiness, independent of pixel brightness");
                black.Present(()=>true); DwmFlush(); var pixel=Pixel(point);
                Check(pixel.R<4 && pixel.G<4 && pixel.B<4,"Black first source frame publishes as valid content, not rejected as a blank surface");
            }
            Check(MpvPreparedSession.Live==1 && MpvPreparedSession.MaximumLive<=2,"Success, failure, timeout, cancellation keep at most stable plus candidate");
        }
        finally { stable.Dispose(); }
        Check(MpvPreparedSession.Live==0,"Every test-owned player, HWND and native shutdown task released");
        Console.WriteLine($"All {passed} libmpv preparation checks passed.");
    }
}
