using System.Diagnostics;

// Continuous one-pixel desktop sampling for colored-source handoffs. This
// diagnostic worker is joined before renderer cleanup; no production readback.
internal sealed class MpvScreenSampler : IDisposable
{
    private int stop;
    private readonly Task task;
    internal long Samples,BlackSamples;
    internal double MaximumIntervalMs;
    internal MpvScreenSampler(Point point)
    {
        task=Task.Run(()=>
        {
            using var image=new Bitmap(1,1); using var graphics=Graphics.FromImage(image);
            double previous=0;
            while(Volatile.Read(ref stop)==0)
            {
                graphics.CopyFromScreen(point,Point.Empty,image.Size);
                double now=Stopwatch.GetTimestamp()*1000d/Stopwatch.Frequency;
                if(previous!=0) MaximumIntervalMs=Math.Max(MaximumIntervalMs,now-previous);
                previous=now; var pixel=image.GetPixel(0,0); Samples++;
                if(pixel.R<4 && pixel.G<4 && pixel.B<4) BlackSamples++;
                Thread.Sleep(1);
            }
        });
    }
    public void Dispose() { Volatile.Write(ref stop,1); task.GetAwaiter().GetResult(); }
}
