using System.Diagnostics;

internal static class LibMpvAudioHandoffChecks
{
    internal readonly record struct Energy(double At,double A,double B);
    internal sealed class Tones
    {
        internal readonly List<Energy> Windows=new();
        private long bucket=-1; private int samples; private double ar,ai,br,bi;
        internal void Sample(double at,double value)
        {
            long current=(long)(at/20);
            if(current!=bucket)
            {
                if(samples>400) Windows.Add(new(bucket*20+10,2*Math.Sqrt(ar*ar+ai*ai)/samples,2*Math.Sqrt(br*br+bi*bi)/samples));
                bucket=current; samples=0; ar=ai=br=bi=0;
            }
            double t=(at-current*20)/1000;
            ar+=value*Math.Cos(2*Math.PI*440*t); ai+=value*Math.Sin(2*Math.PI*440*t);
            br+=value*Math.Cos(2*Math.PI*880*t); bi+=value*Math.Sin(2*Math.PI*880*t); samples++;
        }
    }
    internal static void Run(string dll,string fixtures)
    {
        Exception? failure=null;
        var thread=new Thread(()=> { try { Measure(dll,fixtures); } catch(Exception ex) { failure=ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if(failure!=null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static void Measure(string dll,string fixtures)
    {
        int passed=0; void Check(bool ok,string name) { if(!ok) throw new InvalidOperationException("FAIL "+name); Console.WriteLine("PASS "+name); passed++; }
        var positive=new Tones();
        for(int i=0;i<4800;i++)
        {
            double t=i/48000d;
            positive.Sample(1000000+t*1000,0.001*Math.Sin(2*Math.PI*440*t)+0.001*Math.Sin(2*Math.PI*880*t));
        }
        Check(positive.Windows.Any(x=>x.A>0.0008 && x.B>0.0008),"Spectral positive control detects simultaneous 440/880 Hz tones");
        var tones=new Tones(); using var probe=new LoopAudioProbe(sampleObserver:tones.Sample);
        MpvPreparedSession? a=null,b=null;
        void Pump(int ms)
        { var wait=Stopwatch.StartNew(); while(wait.ElapsedMilliseconds<ms) { Application.DoEvents(); a?.Poll(); b?.Poll(); probe.Poll(); Thread.Sleep(2); } }
        try
        {
            a=new(dll,Path.Combine(fixtures,"tone-a.mp4"),0);
            a.Prepare(()=> { Application.DoEvents(); probe.Poll(); },()=>true); a.Present(()=>true); a.Published=true; a.Activate(false,true);
            Pump(500);
            Console.WriteLine($"AUDIO-BASELINE windows={tones.Windows.Count} max440={tones.Windows.Select(x=>x.A).DefaultIfEmpty().Max():F8} max880={tones.Windows.Select(x=>x.B).DefaultIfEmpty().Max():F8} soundFrames={probe.SoundFrames} mute={a.Player.String("mute")} volume={a.Player.String("volume")} ao={a.Player.String("current-ao")}");
            double baseline=tones.Windows.Select(x=>x.A).DefaultIfEmpty().Max();
            Check(baseline>0.000001 && tones.Windows.Any(x=>x.A>baseline*0.5 && x.B<x.A*0.1),"Endpoint identifies audible published A at 440 Hz");
            double preparing=LoopAudioProbe.Now;
            b=new(dll,Path.Combine(fixtures,"tone-b.mp4"),a.Host.Handle);
            b.Prepare(()=> { Application.DoEvents(); a.Poll(); probe.Poll(); },()=>true); Pump(150);
            double prepared=LoopAudioProbe.Now;
            var preparation=tones.Windows.Where(x=>x.At>preparing+30 && x.At<prepared-30).ToArray();
            Console.WriteLine($"AUDIO-PREPARE windows={preparation.Length} max440={preparation.Max(x=>x.A):F8} max880={preparation.Max(x=>x.B):F8}");
            Check(preparation.Length>=3 && preparation.Any(x=>x.A>baseline*0.5) && preparation.All(x=>x.B<Math.Max(0.0000001,baseline*0.1)),"Real B preroll is inaudible while A remains audible");
            b.Present(()=>true); b.Published=true;
            Check(b.Player.String("mute")=="yes" && b.Player.String("pause")=="yes","Visual commit keeps B paused and muted while A is still retained");
            a.Dispose(); a=null;
            double activated=LoopAudioProbe.Now; b.Activate(false,true); Pump(600);
            var after=tones.Windows.Where(x=>x.At>activated+200).ToArray();
            Check(after.Any(x=>x.B>baseline*0.5 && x.A<x.B*0.1),"Endpoint identifies B at 880 Hz after A is completely released");
            var mixed=tones.Windows.Where(x=>x.A>baseline*0.2 && x.B>baseline*0.2 && Math.Min(x.A,x.B)>Math.Max(x.A,x.B)*0.2).ToArray();
            Console.WriteLine($"AUDIO-HANDOFF windows={tones.Windows.Count} mixedWindows={mixed.Length} resolutionMs=20 oldReleasedBeforeActivate=True invalidTimestamps={probe.InvalidTimestampPackets}");
            foreach(var item in mixed) Console.WriteLine($"MIXED qpcMs={item.At:F3} amplitude440={item.A:F8} amplitude880={item.B:F8}");
            Check(mixed.Length==0 && probe.InvalidTimestampPackets==0,"No mixed A/B tone detected at default WASAPI output in 20 ms windows");
        }
        finally { a?.Dispose(); b?.Dispose(); }
        Check(MpvPreparedSession.Live==0,"Audio test releases both real native sessions and HWNDs");
        Console.WriteLine($"All {passed} libmpv audio handoff checks passed.");
    }
}
