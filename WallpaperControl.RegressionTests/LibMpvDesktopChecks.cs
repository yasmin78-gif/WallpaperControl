extern alias WallpaperApp;
using Video=WallpaperApp::WallpaperControl.Video;
using System.Diagnostics;
using System.Runtime.InteropServices;

internal static class LibMpvDesktopChecks
{
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern void keybd_event(byte key,byte scan,uint flags,nuint extra);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    private static void Pump(int ms) { var clock=Stopwatch.StartNew(); while(clock.ElapsedMilliseconds<ms) { Application.DoEvents(); Thread.Sleep(2); } }
    private static Color Pixel(Point point) { DwmFlush(); using var image=new Bitmap(1,1); using(var graphics=Graphics.FromImage(image)) graphics.CopyFromScreen(point,Point.Empty,image.Size); return image.GetPixel(0,0); }
    private static bool Blue(Color p)=>p.B>180 && p.R<50 && p.G<50;
    internal static void Surface(bool fullscreen,int seconds)
    {
        var thread=new Thread(()=>
        {
            using var form=new Form { Text="Isolated libmpv application coverage check", BackColor=Color.Lime,
                StartPosition=FormStartPosition.Manual, FormBorderStyle=fullscreen?FormBorderStyle.None:FormBorderStyle.Sizable,
                Bounds=fullscreen?Screen.PrimaryScreen!.Bounds:new Rectangle(Screen.PrimaryScreen!.Bounds.Width/2-250,Screen.PrimaryScreen.Bounds.Height/2-150,500,300) };
            using var timer=new System.Windows.Forms.Timer { Interval=seconds*1000 };
            timer.Tick+=(_,_)=>form.Close(); form.Shown+=(_,_)=>timer.Start(); Application.Run(form);
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
    }
    internal static void Run(string dll,string file)
    {
        Exception? failure=null;
        var thread=new Thread(()=> { try { Measure(dll,file); } catch(Exception ex) { failure=ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if(failure!=null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static void Measure(string dll,string file)
    {
        int passed=0;
        void Check(bool ok,string name) { if(!ok) throw new InvalidOperationException("FAIL "+name); passed++; Console.WriteLine("PASS "+name); }
        nint foreground=GetForegroundWindow();
        var initialShell = Video.VideoNative.Resolve() ?? throw new InvalidOperationException("Desktop shell unavailable");
        if (!Video.VideoNative.EnsureRaisedComposition(initialShell)) throw new InvalidOperationException("Desktop composition unavailable");
        using var session=new MpvPreparedSession(dll,file,0);
        session.Prepare(()=>Application.DoEvents(),()=>true); session.Present(()=>true); session.Published=true; session.Activate(true,false); Pump(150);
        var host=session.Host; var point=host.ProbeOrigin+new Size(240,101);
        Check(host.Attached,"Host and actual mpv child attached to unchanged shell lease");
        var probeHit = WindowFromPoint(point); Video.VideoNative.GetWindowThreadProcessId(probeHit,out uint probePid);
        Console.WriteLine($"DESKTOP-PROBE pixel={Pixel(point)} hit=0x{probeHit:X} pid={probePid} exposed={host.IsProbeExposed(point)} hierarchy={host.Hierarchy} defViewExStyle=0x{Video.VideoNative.GetStyle(host.Shell.DefView,-20):X}");
        Check(Blue(Pixel(point)),"Real prepared blue pixels visible below icons/widgets");
        Check(foreground==GetForegroundWindow(),"Desktop creation and publication do not steal focus");
        Check((Video.VideoNative.GetStyle(host.Handle,-20)&8)==0 && (Video.VideoNative.GetStyle(host.Child,-20)&8)==0,"No topmost renderer");
        Check((Video.VideoNative.GetStyle(host.Handle,-16)&0x40000000)!=0 && (Video.VideoNative.GetStyle(host.Handle,-20)&0x80)!=0,"Child/tool window excluded from Alt+Tab");
        Check((Video.VideoNative.GetStyle(host.Handle,-16)&0x08000000)!=0 && (Video.VideoNative.GetStyle(host.Child,-16)&0x08000000)!=0,"Host and mpv child disabled for pointer/keyboard input");
        var hit=WindowFromPoint(point); Check(hit!=host.Handle && hit!=host.Child,"Desktop pointer hit passes through renderer");
        Check(session.Player.String("current-vo")=="gpu" && session.Player.String("hwdec-current")=="d3d11va","D3D11 hardware decoder active");
        Check(session.Player.String("panscan")=="1.000000" || session.Player.Double("panscan")==1,"Fill enabled with aspect preserved");
        double position=session.Player.Double("time-pos"); Pump(200); Check(Math.Abs(session.Player.Double("time-pos")-position)<0.01 && Blue(Pixel(point)),"Manual pause retains visible frame and playback position");
        for(int i=0;i<50;i++)
        {
            // Explicitly requested Win+D stress. No foreign HWND mutation.
            keybd_event(0x5b,0,0,0); keybd_event(0x44,0,0,0); keybd_event(0x44,0,2,0); keybd_event(0x5b,0,2,0);
            Pump(80);
            if(!host.Attached || !Blue(Pixel(point))) throw new InvalidOperationException($"Win+D iteration {i+1} lost desktop pixels or lease");
        }
        Check(session.LoadedEvents==1 && MpvPreparedSession.Live==1,"50 Win+D actions preserve player, host, child, paused frame");
        foreach(bool fullscreen in new[]{false,true})
        {
            using var app=Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute=false, ArgumentList={"--libmpv-test-surface",fullscreen?"full":"normal","3"} })!;
            var deadline=Stopwatch.StartNew(); while(app.MainWindowHandle==0 && deadline.ElapsedMilliseconds<2000) { Pump(20); app.Refresh(); }
            Pump(100);
            var appPixel=Pixel(point);
            Check(appPixel.G>180 && appPixel.R<50 && appPixel.B<50,fullscreen?"Fullscreen application visibly above desktop video":"Normal application visibly above desktop video");
            if(fullscreen)
            {
                Check(WallpaperControl.FullscreenActivityDetector.IsFullscreenActive(),"Existing fullscreen detector recognizes independent fullscreen process");
                session.Activate(true,false);
                using var occluded=new MpvPreparedSession(dll,file,host.Handle);
                occluded.Prepare(()=>Application.DoEvents(),()=>true);
                Check(occluded.Prepared && occluded.Player.String("mute")=="yes","Fully occluded candidate can prepare muted beneath fullscreen application");
            }
            while(!app.HasExited) Pump(20);
            Pump(300); // Allow the OS close animation to uncover the desktop.
            Console.WriteLine($"APP-EXIT fullscreen={fullscreen} attached={host.Attached} screen={Pixel(point)}");
            Check(host.Attached && Blue(Pixel(point)),"Application exit exposes retained desktop frame without player recreation");
        }
        session.Activate(false,false); Pump(250); Check(session.Player.Double("time-pos")>position,"Resume advances same player");
        Console.WriteLine("HIERARCHY "+host.Hierarchy);
        session.Dispose(); Check(!Video.VideoNative.IsWindow(host.Handle) && !Video.VideoNative.IsWindow(host.Child) && MpvPreparedSession.Live==0,"Shutdown releases own host, child, native player");
        Console.WriteLine($"All {passed} libmpv desktop checks passed.");
    }
}
