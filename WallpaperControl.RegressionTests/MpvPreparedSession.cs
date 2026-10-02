extern alias WallpaperApp;
using Video=WallpaperApp::WallpaperControl.Video;
using System.Diagnostics;

// Pinned-build VO readiness experiment; no production implementation or factory.
// All ownership and polling stay on the STA. No queued callbacks survive Dispose.
internal sealed class MpvPreparedSession : IDisposable
{
    internal readonly long Id;
    internal readonly MpvDesktopHost Host;
    internal readonly LibMpvLoopDiagnostics.Player Player;
    internal bool Loaded, VoPresented, Prepared, Published, Disposed;
    internal int LoadedEvents, RestartEvents;
    internal static int Live, MaximumLive;
    private static long nextId;
    internal MpvPreparedSession(string dll, string file, nint behind, bool hidden=false)
    {
        Id=Interlocked.Increment(ref nextId); Host=new(behind,hidden);
        try
        {
            var options=new Dictionary<string,string>(MpvDesktopHost.Options) { ["mute"]="yes" };
            Player=new(dll,Host.Handle,21.54434690031884,options);
            Live++; MaximumLive=Math.Max(MaximumLive,Live);
            try { Player.Load(file); }
            catch { Dispose(); throw; }
            Console.WriteLine($"CREATE session={Id} live={Live} behind=0x{behind:X} muted={Player.String("mute")}");
        }
        catch { Host.Dispose(); throw; }
    }
    internal void Poll()
    {
        ObjectDisposedException.ThrowIf(Disposed,this);
        for(int i=0;i<100;i++)
        {
            var item=Player.Poll(); if(item.Type==0) break;
            if(item.Type==2)
            {
                string text=Player.LogText(item.Data);
                if(text.Contains("first video frame after restart shown",StringComparison.Ordinal))
                { VoPresented=true; Console.WriteLine($"VO-FIRST session={Id} qpcMs={LoopAudioProbe.Now:F3}"); }
                if(text.Contains("error",StringComparison.OrdinalIgnoreCase)) Console.WriteLine($"MPV session={Id} {text}");
            }
            if(item.Type==8) { Loaded=true; LoadedEvents++; }
            if(item.Type==21) RestartEvents++;
            if(item.Error<0 || item.Type is 1 or 7) throw new InvalidOperationException($"session={Id} mpv event={item.Type} error={item.Error}");
        }
        Host.FindChild();
    }
    internal void Prepare(Action pump, Func<bool> valid, int timeoutMs=15000)
    {
        var deadline=Stopwatch.StartNew();
        while(!Loaded || !VoPresented || RestartEvents==0 || !Host.Attached || !(Player.Double("video-out-params/w")>0))
        {
            if(!valid() || !Host.LeaseValid) throw new OperationCanceledException("Stale generation, shell lease or cancelled candidate");
            if(deadline.ElapsedMilliseconds>=timeoutMs) throw new TimeoutException("VO prepared-frame timeout");
            Poll(); pump(); Thread.Sleep(2);
        }
        Player.Set("pause","yes");
        if(Player.String("pause")!="yes" || Player.String("mute")!="yes") throw new InvalidOperationException("Candidate pause/mute not confirmed");
        if(!valid() || !Host.Attached) throw new OperationCanceledException("Candidate invalid at readiness");
        Prepared=true;
        Console.WriteLine($"PREPARED session={Id} loaded={LoadedEvents} voPresented={VoPresented} restarts={RestartEvents} paused={Player.String("pause")} muted={Player.String("mute")} hwdec={Player.String("hwdec-current")} {Host.Hierarchy}");
    }
    internal void Present(Func<bool> valid)
    {
        if(!Prepared || !valid() || !Host.Attached) throw new OperationCanceledException("Candidate invalid immediately before handoff");
        if(Player.String("mute")!="yes" || Player.String("pause")!="yes") throw new InvalidOperationException("Visual commit requires paused/muted candidate");
        Host.Place(Host.Shell.DefView);
    }
    internal void Activate(bool paused,bool sound,int volume=1)
    {
        if(!Published || Disposed) throw new InvalidOperationException("Only published session can activate");
        Player.Set("volume",(100*Math.Cbrt(Math.Clamp(volume,0,100)/100d)).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Player.Set("pause",paused?"yes":"no");
        Player.Set("mute",sound?"no":"yes");
    }
    public void Dispose()
    {
        if(Disposed) return; Disposed=true;
        try
        {
            Player.Set("mute","yes"); Player.Set("pause","yes");
        }
        finally
        {
            // Native Win32 output teardown may synchronously dispatch to STA.
            // Keep that STA pumping while shutdown completes, without new commands.
            var task=Task.Run(Player.Dispose);
            while(!task.IsCompleted) { Application.DoEvents(); Thread.Sleep(2); }
            try { task.GetAwaiter().GetResult(); }
            finally { Host.Dispose(); Live--; }
            if(Video.VideoNative.IsWindow(Host.Handle) || Video.VideoNative.IsWindow(Host.Child)) throw new InvalidOperationException("Released session retains renderer HWND");
            Console.WriteLine($"RELEASE session={Id} live={Live} playerReleased={Player.Released}");
        }
    }
}
