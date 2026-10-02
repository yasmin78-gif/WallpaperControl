using System.Diagnostics;
using System.Runtime.InteropServices;

internal static class LibMpvSwitchDiagnostics
{
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    internal static void Run(string dll,string first,string second,int count,bool idle=false)
    {
        if(count is <1 or >1000) throw new ArgumentOutOfRangeException(nameof(count));
        Exception? failure=null;
        var thread=new Thread(()=> { try { Measure(dll,first,second,count,idle); } catch(Exception ex) { failure=ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if(failure!=null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static Color Pixel(Point point)
    {
        DwmFlush(); using var image=new Bitmap(1,1);
        using(var graphics=Graphics.FromImage(image)) graphics.CopyFromScreen(point,Point.Empty,image.Size);
        return image.GetPixel(0,0);
    }
    private static void Measure(string dll,string first,string second,int count,bool idle)
    {
        MpvPreparedSession? stable=null, candidate=null; long generation=0;
        MpvScreenSampler? sampler=null;
        bool colored=Path.GetFileName(first)=="loop-colors.mp4" && Path.GetFileName(second)=="switch-colors.mp4";
        using var process=Process.GetCurrentProcess(); var clock=Stopwatch.StartNew();
        double resourceAt=0; var cpuAt=process.TotalProcessorTime;
        try
        {
            for(int i=0;i<=count;i++)
            {
                long request=++generation;
                string file=i%2==0?first:second;
                Color? old=stable==null?null:Pixel(stable.Host.ProbeOrigin+new Size(240,101));
                candidate=new(dll,file,stable?.Host.Handle??0);
                int sampled=0, black=0;
                candidate.Prepare(()=>
                {
                    Application.DoEvents(); stable?.Poll();
                    if(old!=null)
                    {
                        var pixel=Pixel(stable!.Host.ProbeOrigin+new Size(240,101)); sampled++;
                        if(pixel.R<4 && pixel.G<4 && pixel.B<4) black++;
                    }
                },()=>request==generation);
                candidate.Present(()=>request==generation);
                DwmFlush();
                var presented=Pixel(candidate.Host.ProbeOrigin+new Size(240,101));
                // Arbitrary black source frames are valid. Colored fixtures give
                // an independent screen proof; this is not the readiness predicate.
                Console.WriteLine($"HANDOFF switch={i} generation={request} old={stable?.Id} candidate={candidate.Id} preparedSamples={sampled} preparedBlackSamples={black} screenRGB={presented.R},{presented.G},{presented.B}");
                var retired=stable; stable=candidate; candidate=null; stable.Published=true;
                retired?.Dispose();
                stable.Activate(paused:false,sound:false);
                if(colored && i==0) sampler=new(stable.Host.ProbeOrigin+new Size(240,101));
                if(MpvPreparedSession.Live!=1 || MpvPreparedSession.MaximumLive>2 || !stable.Host.Attached)
                    throw new InvalidOperationException("Session/renderer ownership invariant failed");
                var owned=stable.Host.CountOwned();
                if(owned.Hosts!=1 || owned.Children!=1) throw new InvalidOperationException($"Unexpected enumerated owned HWNDs: {owned}");
                if(i is 0 or 1 or 5 or 10 or 25 or 50 or 100 or 250 || i==count)
                {
                    process.Refresh(); var handles=NativeHandleSnapshot.Capture(); double now=clock.Elapsed.TotalMilliseconds;
                    double cpu=(process.TotalProcessorTime-cpuAt).TotalMilliseconds*100/Math.Max(1,now-resourceAt)/Environment.ProcessorCount;
                    cpuAt=process.TotalProcessorTime; resourceAt=now;
                    Console.WriteLine($"RESOURCES switch={i} handles={handles.Total} nvidia={handles.NvidiaIpc} workingSetMiB={process.WorkingSet64/1048576d:F3} privateMiB={process.PrivateMemorySize64/1048576d:F3} cpuPercent={cpu:F3} live={MpvPreparedSession.Live} maxLive={MpvPreparedSession.MaximumLive} published={stable.Id} candidate=none host=1 mpvChild=1 mute={stable.Player.String("mute")} types=[{string.Join(',',handles.Types.Select(x=>x.Key+":"+x.Value))}]");
                }
            }
            Console.WriteLine($"SUMMARY switches={count} maximumLive={MpvPreparedSession.MaximumLive}");
            if(sampler!=null)
            {
                sampler.Dispose(); Console.WriteLine($"HANDOFF-SCREEN samples={sampler.Samples} blackSamples={sampler.BlackSamples} maxSampleIntervalMs={sampler.MaximumIntervalMs:F3}");
                if(sampler.BlackSamples!=0) throw new InvalidOperationException("Black desktop samples with non-black colored source");
            }
            if(idle)
            {
                foreach(int seconds in new[]{30,90})
                {
                    var wait=Stopwatch.StartNew(); while(wait.Elapsed.TotalSeconds<seconds) { Application.DoEvents(); stable!.Poll(); Thread.Sleep(10); }
                    process.Refresh(); var snapshot=NativeHandleSnapshot.Capture();
                    Console.WriteLine($"IDLE elapsedSeconds={(seconds==30?30:120)} handles={snapshot.Total} nvidia={snapshot.NvidiaIpc} workingSetMiB={process.WorkingSet64/1048576d:F3} privateMiB={process.PrivateMemorySize64/1048576d:F3} live={MpvPreparedSession.Live}");
                }
                stable!.Dispose(); stable=null;
                var stopWait=Stopwatch.StartNew(); while(stopWait.Elapsed.TotalSeconds<30) { Application.DoEvents(); Thread.Sleep(10); }
                process.Refresh(); var snapshotAfter=NativeHandleSnapshot.Capture();
                Console.WriteLine($"STOP-IDLE seconds=30 handles={snapshotAfter.Total} nvidia={snapshotAfter.NvidiaIpc} workingSetMiB={process.WorkingSet64/1048576d:F3} privateMiB={process.PrivateMemorySize64/1048576d:F3} live={MpvPreparedSession.Live} host=0 child=0");
            }
        }
        finally
        {
            sampler?.Dispose(); candidate?.Dispose(); stable?.Dispose(); var snapshot=NativeHandleSnapshot.Capture();
            Console.WriteLine($"SHUTDOWN live={MpvPreparedSession.Live} handles={snapshot.Total} nvidia={snapshot.NvidiaIpc} nvidiaNames=[{string.Join(';',snapshot.NvidiaNames.Select(x=>x.Key+":"+x.Value))}] types=[{string.Join(',',snapshot.Types.Select(x=>x.Key+":"+x.Value))}]");
        }
    }
}
