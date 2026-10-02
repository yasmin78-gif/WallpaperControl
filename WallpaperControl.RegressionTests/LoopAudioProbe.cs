using System.Diagnostics;
using System.Runtime.InteropServices;

// Test-only WASAPI loopback measurement. Keeps timing/amplitudes only, no audio
// recording. Uses the default render endpoint; never changes endpoint settings.
internal sealed class LoopAudioProbe : IDisposable
{
    private nint client, capture, format;
    private int rate, channels, block;
    private bool floating;
    private double lastSound = -1;
    private readonly double minimumGapMs;
    private readonly Action<double,double>? sampleObserver;
    private readonly (long Bucket, double Start, double End)[]? intervalRing;
    private readonly List<BoundaryWindow> activeWindows = new();
    internal sealed class BoundaryWindow(double at)
    {
        internal double At { get; } = at;
        internal double Start = double.NaN, End = double.NaN;
        internal double MaximumInterval => End - Start;
        internal void Include(double start, double end)
        {
            if (start < At + 600 && end > At - 600 && (double.IsNaN(Start) || end - start > MaximumInterval)) { Start = start; End = end; }
        }
    }
    internal int InvalidTimestampPackets { get; private set; }
    internal int DiscontinuousPackets { get; private set; }
    internal long SoundFrames { get; private set; }
    internal readonly List<(double Start, double End)> Gaps = new();
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, nint outer, uint context, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Endpoint(nint self, int flow, int role, out nint device);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Activate(nint self, ref Guid iid, uint context, nint parameters, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Mix(nint self, out nint format);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Initialize(nint self, int share, uint flags, long duration, long period, nint format, ref Guid session);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Service(nint self, ref Guid iid, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Simple(nint self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Next(nint self, out uint frames);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Buffer(nint self, out nint data, out uint frames, out uint flags, out ulong position, out ulong qpc);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Release(nint self, uint frames);
    private static T Slot<T>(nint self, int slot) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(self), slot * IntPtr.Size));
    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);
    internal LoopAudioProbe(double minimumGapMilliseconds = 30, bool trackBoundaryIntervals = false, Action<double,double>? sampleObserver = null)
    {
        if (!double.IsFinite(minimumGapMilliseconds) || minimumGapMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(minimumGapMilliseconds));
        minimumGapMs = minimumGapMilliseconds;
        this.sampleObserver = sampleObserver;
        if (trackBoundaryIntervals) intervalRing = new (long, double, double)[2048];
        nint enumerator = 0, device = 0;
        try
        {
            var clsid = new Guid("bcde0395-e52f-467c-8e3d-c4579291692e"); var iid = new Guid("a95664d2-9614-4f35-a746-de8db63617e6");
            Check(CoCreateInstance(ref clsid, 0, 23, ref iid, out enumerator));
            Check(Slot<Endpoint>(enumerator, 4)(enumerator, 0, 1, out device));
            iid = new("1cb9ad4c-dbfa-4c32-b178-c2f568a703b2"); Check(Slot<Activate>(device, 3)(device, ref iid, 23, 0, out client));
            Check(Slot<Mix>(client, 8)(client, out format));
            int tag = (ushort)Marshal.ReadInt16(format), bits = (ushort)Marshal.ReadInt16(format, 14);
            channels = (ushort)Marshal.ReadInt16(format, 2); rate = Marshal.ReadInt32(format, 4); block = (ushort)Marshal.ReadInt16(format, 12);
            floating = bits == 32 && (tag == 3 || (tag == 65534 && Marshal.PtrToStructure<Guid>(format + 24) == new Guid("00000003-0000-0010-8000-00aa00389b71")));
            if (!floating && bits != 16) throw new NotSupportedException("Audio probe requires float32 or PCM16 mix format");
            var session = Guid.Empty; Check(Slot<Initialize>(client, 3)(client, 0, 0x20000, 10000000, 0, format, ref session));
            iid = new("c8adbd64-e71e-48a0-a4de-185c395cd317"); Check(Slot<Service>(client, 14)(client, ref iid, out capture));
            Check(Slot<Simple>(client, 10)(client));
            Console.WriteLine($"AUDIO-PROBE default-output rate={rate} channels={channels} float={floating} silenceThreshold=1e-7 minimumGapMs={minimumGapMs}; aggregate timing only; other audible apps can invalidate silence measurements");
        }
        catch { Dispose(); throw; }
        finally { if (device != 0) Marshal.Release(device); if (enumerator != 0) Marshal.Release(enumerator); }
    }
    internal BoundaryWindow ObserveBoundary(double at)
    {
        if (intervalRing == null) throw new InvalidOperationException("Boundary tracking was not enabled");
        var window = new BoundaryWindow(at);
        foreach (var item in intervalRing) if (item.End > 0) window.Include(item.Start, item.End);
        activeWindows.Add(window); return window;
    }
    internal void Poll()
    {
        Check(Slot<Next>(capture, 5)(capture, out uint pending));
        while (pending > 0)
        {
            Check(Slot<Buffer>(capture, 3)(capture, out var data, out uint frames, out uint flags, out _, out ulong qpc));
            try
            {
                if ((flags & 4) != 0) InvalidTimestampPackets++;
                if ((flags & 1) != 0) DiscontinuousPackets++;
                if ((flags & 6) == 0)
                {
                    var bytes = new byte[checked((int)frames * block)]; Marshal.Copy(data, bytes, 0, bytes.Length);
                    for (int frame = 0; frame < frames; frame++)
                    {
                        if(sampleObserver!=null)
                        {
                            double value=floating?BitConverter.ToSingle(bytes,frame*block):BitConverter.ToInt16(bytes,frame*block)/32768d;
                            sampleObserver(qpc/10000d+frame*1000d/rate,value);
                        }
                        bool sound = false;
                        for (int channel = 0; channel < channels; channel++)
                        {
                            int offset = frame * block + channel * (floating ? 4 : 2);
                            double sample = floating ? BitConverter.ToSingle(bytes, offset) : BitConverter.ToInt16(bytes, offset) / 32768d;
                            if (Math.Abs(sample) > 0.0000001) { sound = true; break; }
                        }
                        if (!sound) continue;
                        SoundFrames++;
                        double instant = qpc / 10000d + frame * 1000d / rate;
                        if (lastSound >= 0)
                        {
                            if (instant - lastSound > minimumGapMs) Gaps.Add((lastSound, instant));
                            if (intervalRing != null)
                            {
                                long bucket = (long)instant; int index = (int)(bucket % intervalRing.Length);
                                var old = intervalRing[index];
                                if (old.Bucket != bucket || instant - lastSound > old.End - old.Start) intervalRing[index] = (bucket, lastSound, instant);
                                for (int w = activeWindows.Count - 1; w >= 0; w--)
                                {
                                    if (instant > activeWindows[w].At + 600) activeWindows.RemoveAt(w);
                                    else activeWindows[w].Include(lastSound, instant);
                                }
                            }
                        }
                        lastSound = instant;
                    }
                }
                else if(sampleObserver!=null && (flags&4)==0 && (flags&2)!=0)
                    for(int frame=0;frame<frames;frame++) sampleObserver(qpc/10000d+frame*1000d/rate,0);
            }
            finally { Check(Slot<Release>(capture, 4)(capture, frames)); }
            Check(Slot<Next>(capture, 5)(capture, out pending));
        }
    }
    internal static double Now => Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency;
    // Read-only control: no player, no endpoint/session volume changes.
    internal static void MeasureBackground(int seconds)
    {
        if (seconds is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(seconds));
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var probe = new LoopAudioProbe();
                double until = Now + seconds * 1000;
                while (Now < until) { probe.Poll(); Thread.Sleep(5); }
                Console.WriteLine($"BACKGROUND seconds={seconds} soundFrames={probe.SoundFrames} invalidTimestamps={probe.InvalidTimestampPackets} discontinuities={probe.DiscontinuousPackets}; no playback or mixer changes");
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    public void Dispose()
    {
        if (client != 0) { try { Slot<Simple>(client, 11)(client); } catch { } }
        if (capture != 0) { Marshal.Release(capture); capture = 0; }
        if (client != 0) { Marshal.Release(client); client = 0; }
        if (format != 0) { Marshal.FreeCoTaskMem(format); format = 0; }
    }
}
