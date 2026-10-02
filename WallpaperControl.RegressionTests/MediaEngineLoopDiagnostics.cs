using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

// Explicit diagnostic only. Uses native automatic looping, never requests a seek
// or restart at a boundary. Does not attach to the desktop or write settings.
public static class MediaEngineLoopDiagnostics
{
    internal static void Run(string file, int cycles)
    {
        if (cycles is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(cycles));
        if (!File.Exists(file)) throw new FileNotFoundException("MP4 unavailable", file);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { Measure(Path.GetFullPath(file), cycles); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Measure(string file, int cycles)
    {
        using var window = new Form { Text = "MediaEngine loop diagnostic â€” keep visible", ClientSize = new(480, 202),
            StartPosition = FormStartPosition.Manual, Location = new(40, 60), BackColor = Color.Black };
        window.Show();
        using var engine = new Engine(window.Handle);
        using var audio = new LoopAudioProbe();
        var events = new List<EngineEvent>();
        var boundaries = new List<double>();
        var changes = new List<double>();
        var rendered = new List<double>();
        long lastRendered = -1;
        using var image = new Bitmap(480, 202, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(image);
        var pixels = new byte[480 * 202 * 4];
        ulong lastHash = 0;
        bool hashReady = false, renderCounterReady = false;
        int occluded = 0, samples = 0;
        double previousPosition = 0, lastCapture = 0, epoch = LoopAudioProbe.Now;
        using var process = Process.GetCurrentProcess();
        var initial = NativeHandleSnapshot.Capture();
        TimeSpan cpuBefore = process.TotalProcessorTime;
        double resourceAt = epoch;
        Console.WriteLine($"CONFIG file={file} mode=native-HWND flags=0 loop=True volume=0.01 muted=False engineInstances=1 HWND=1 callback=1 explicitEngineCOM=2 defaultAudioEndpoint=True noManualRestart=True initialHandles={initial.Total} initialNvidia={initial.NvidiaIpc}");
        engine.Start(file);
        bool drainStarted = false;
        double drainAt = 0;
        while (true)
        {
            Application.DoEvents(); audio.Poll();
            if (window.IsDisposed) throw new OperationCanceledException("Diagnostic window closed before completion");
            double now = LoopAudioProbe.Now, position = engine.CurrentTime;
            while (engine.Events.TryDequeue(out var item))
            {
                events.Add(item);
                if (item.Type == 1009) renderCounterReady = true;
                if (item.Type is not (2 or 18)) Console.WriteLine($"EVENT wallMs={item.At - epoch:F3} type={item.Type} param1={item.Param1} param2=0x{item.Param2:X8}");
                if (item.Type is 5 or 1014) throw new InvalidOperationException($"MediaEngine error event={item.Type} p1={item.Param1} hr=0x{item.Param2:X8}");
            }
            if (previousPosition > 1 && position < previousPosition - 1)
            {
                boundaries.Add(now);
                Console.WriteLine($"BOUNDARY loop={boundaries.Count} wallMs={now - epoch:F3} previousPosition={previousPosition:F6} newPosition={position:F6} seeking={engine.IsSeeking} loopEnabled={engine.LoopEnabled}");
                if (boundaries.Count is 1 or 5 or 10 or 25 or 50 || boundaries.Count == cycles)
                {
                    process.Refresh(); var native = NativeHandleSnapshot.Capture();
                    double cpu = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds * 100 / (now - resourceAt) / Environment.ProcessorCount;
                    cpuBefore = process.TotalProcessorTime; resourceAt = now;
                    Console.WriteLine($"RESOURCES loop={boundaries.Count} wallMs={now - epoch:F3} handles={native.Total} nvidia={native.NvidiaIpc} cpuPercent={cpu:F3} workingSetMiB={process.WorkingSet64 / 1048576d:F3} privateMiB={process.PrivateMemorySize64 / 1048576d:F3} engineInstances=1 rendererHWND=1 callback=1 explicitEngineCOM=2 creationCount={Engine.Created} types=[{string.Join(',', native.Types.Select(x => x.Key + ':' + x.Value))}]");
                }
            }
            previousPosition = position;
            // Screen measurement, not a decoded-frame/readback measurement.
            // Sample near boundaries only to limit instrumentation overhead.
            double duration = engine.Duration;
            if (now - lastCapture >= 4 && (position < 1 || (double.IsFinite(duration) && duration - position < 1)))
            {
                lastCapture = now;
                if (renderCounterReady)
                {
                    long count = engine.RenderedFrames;
                    if (lastRendered >= 0 && count != lastRendered)
                    {
                        rendered.Add(LoopAudioProbe.Now);
                        Console.WriteLine($"RENDER-COUNT wallMs={LoopAudioProbe.Now - epoch:F3} position={position:F6} count={count} delta={count - lastRendered}");
                    }
                    lastRendered = count;
                }
                var origin = window.PointToScreen(Point.Empty);
                if (Uncovered(window.Handle, origin, window.ClientSize))
                {
                    graphics.CopyFromScreen(origin, Point.Empty, image.Size);
                    var data = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    ulong hash = 14695981039346656037;
                    try
                    {
                        Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                        for (int i = 0; i < pixels.Length; i += 16) hash = unchecked((hash ^ BitConverter.ToUInt32(pixels, i)) * 1099511628211);
                    }
                    finally { image.UnlockBits(data); }
                    samples++;
                    if (hashReady && hash != lastHash) changes.Add(LoopAudioProbe.Now);
                    hashReady = true; lastHash = hash;
                }
                else { occluded++; hashReady = false; }
            }
            if (boundaries.Count >= cycles && !drainStarted) { drainStarted = true; drainAt = now; }
            if (drainStarted && now - drainAt > 900) break;
            if (now - epoch > (cycles + 2) * 120000d) throw new TimeoutException("Automatic loops did not complete");
            Thread.Sleep(1);
        }
        foreach (var (boundary, index) in boundaries.Select((b, i) => (b, i + 1)))
        {
            var gaps = audio.Gaps.Where(g => g.Start < boundary + 600 && g.End > boundary - 600).ToArray();
            var before = changes.Where(t => t <= boundary && t > boundary - 800).DefaultIfEmpty(double.NaN).Max();
            var after = changes.Where(t => t > boundary && t < boundary + 800).DefaultIfEmpty(double.NaN).Min();
            var intervals = changes.Zip(changes.Skip(1), (a, b) => (Start: a, End: b))
                .Where(x => x.Start < boundary + 400 && x.End > boundary - 400 && x.End - x.Start < 1600).ToArray();
            var longest = intervals.OrderByDescending(x => x.End - x.Start).FirstOrDefault();
            var seeking = events.Where(e => e.Type == 16 && Math.Abs(e.At - boundary) < 600).OrderBy(e => Math.Abs(e.At - boundary)).FirstOrDefault();
            var seeked = events.Where(e => e.Type == 17 && Math.Abs(e.At - boundary) < 600).OrderBy(e => Math.Abs(e.At - boundary)).FirstOrDefault();
            var playing = events.Where(e => e.Type == 13 && Math.Abs(e.At - boundary) < 600).OrderBy(e => Math.Abs(e.At - boundary)).FirstOrDefault();
            Console.WriteLine($"LOOP loop={index} boundaryWallMs={boundary - epoch:F3} screenChangeIntervalMs={after - before:F3} lastScreenChangeRelativeMs={before - boundary:F3} firstScreenChangeRelativeMs={after - boundary:F3} seekingRelativeMs={Relative(seeking, boundary)} seekedRelativeMs={Relative(seeked, boundary)} playingRelativeMs={Relative(playing, boundary)} audioGapCount={gaps.Length}");
            Console.WriteLine($"SCREEN-INTERVAL loop={index} longestMs={(intervals.Length == 0 ? double.NaN : longest.End - longest.Start):F3} startRelativeMs={longest.Start - boundary:F3} endRelativeMs={longest.End - boundary:F3} metric=screen-content-change-not-decoder-frame");
            var renderIntervals = rendered.Zip(rendered.Skip(1), (a, b) => (Start: a, End: b))
                .Where(x => x.Start < boundary + 400 && x.End > boundary - 400 && x.End - x.Start < 1600).OrderByDescending(x => x.End - x.Start).ToArray();
            if (renderIntervals.Length > 0) Console.WriteLine($"RENDER-INTERVAL loop={index} longestMs={renderIntervals[0].End - renderIntervals[0].Start:F3} startRelativeMs={renderIntervals[0].Start - boundary:F3} endRelativeMs={renderIntervals[0].End - boundary:F3} metric=MediaEngine-frames-rendered-counter-not-screen");
            foreach (var gap in gaps) Console.WriteLine($"AUDIO-GAP loop={index} gapMs={gap.End - gap.Start:F3} startRelativeMs={gap.Start - boundary:F3} endRelativeMs={gap.End - boundary:F3}");
        }
        process.Refresh(); var final = NativeHandleSnapshot.Capture();
        Console.WriteLine($"SUMMARY loops={boundaries.Count} screenSamples={samples} occludedSamples={occluded} sameEngine=True creationCount={Engine.Created} sameHWND=True handles={final.Total} nvidia={final.NvidiaIpc} workingSetMiB={process.WorkingSet64 / 1048576d:F3} privateMiB={process.PrivateMemorySize64 / 1048576d:F3} audioGapsBelow30ms=not-reported");
        engine.Dispose();
        Console.WriteLine($"SHUTDOWN enginePointerReleased={engine.Released} callbackDetached={engine.CallbackDetached}");
        if (!engine.Released || !engine.CallbackDetached || Engine.Created != 1) throw new InvalidOperationException("MediaEngine lifecycle invariant failed");
        audio.Dispose(); window.Close();
        var shutdown = NativeHandleSnapshot.Capture();
        Console.WriteLine($"RESOURCE-SHUTDOWN handles={shutdown.Total} nvidia={shutdown.NvidiaIpc} engineInstances=0 rendererHWND=0 explicitEngineCOM=0 callback=0");
    }

    private static string Relative(EngineEvent e, double b) => e.At == 0 ? "absent" : (e.At - b).ToString("F3");
    private static bool Uncovered(nint hwnd, Point origin, Size size) => new[] { new Point(2, 2), new Point(size.Width / 2, size.Height / 2), new Point(size.Width - 3, size.Height - 3) }
        .All(p => GetAncestor(WindowFromPoint(new(origin.X + p.X, origin.Y + p.Y)), 2) == hwnd);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
    internal readonly record struct EngineEvent(int Type, nuint Param1, uint Param2, double At);

    [ComVisible(true), Guid("fee7c112-e776-42b5-9bbf-0048524e2bd5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface INotify { [PreserveSig] int EventNotify(uint type, nuint param1, uint param2); }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class Notify : INotify
    {
        internal ConcurrentQueue<EngineEvent>? Events;
        public int EventNotify(uint type, nuint param1, uint param2)
        {
            try { Events?.Enqueue(new((int)type, param1, param2, LoopAudioProbe.Now)); return 0; }
            catch { return unchecked((int)0x80004005); }
        }
    }

    private sealed class Engine : IDisposable
    {
        private nint engine, extended;
        private bool started;
        private readonly Notify notify = new();
        internal readonly ConcurrentQueue<EngineEvent> Events = new();
        internal static int Created;
        internal bool Released => engine == 0 && extended == 0;
        internal bool CallbackDetached => notify.Events == null;
        [DllImport("mfplat.dll")] private static extern int MFStartup(int version, int flags);
        [DllImport("mfplat.dll")] private static extern int MFShutdown();
        [DllImport("mfplat.dll")] private static extern int MFCreateAttributes(out nint attributes, uint count);
        [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, nint outer, uint context, ref Guid iid, out nint value);
        [DllImport("ole32.dll")] private static extern int PropVariantClear(ref Variant value);
        [StructLayout(LayoutKind.Explicit, Size = 24)]
        private struct Variant { [FieldOffset(0)] internal ushort Type; [FieldOffset(8)] internal long Value; }
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Statistics(nint self, int statistic, out Variant value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Create(nint self, uint flags, nint attributes, out nint engine);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Unknown(nint self, ref Guid key, [MarshalAs(UnmanagedType.IUnknown)] object value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int UInt64(nint self, ref Guid key, ulong value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Simple(nint self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Boolean(nint self, int value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int BoolResult(nint self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate double DoubleResult(nint self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int DoubleArg(nint self, double value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Source(nint self, [MarshalAs(UnmanagedType.BStr)] string value);
        private static T Slot<T>(nint self, int slot) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(self), slot * IntPtr.Size));
        private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);
        internal double CurrentTime => Slot<DoubleResult>(engine, 16)(engine);
        internal double Duration => Slot<DoubleResult>(engine, 19)(engine);
        internal bool IsSeeking => Slot<BoolResult>(engine, 15)(engine) != 0;
        internal bool LoopEnabled => Slot<BoolResult>(engine, 30)(engine) != 0;
        internal long RenderedFrames
        {
            get
            {
                Check(Slot<Statistics>(extended, 46)(extended, 0, out var value));
                try { return value.Type switch { 19 => unchecked((uint)value.Value), 21 => value.Value, _ => throw new NotSupportedException("Unexpected frames-rendered variant type " + value.Type) }; }
                finally { PropVariantClear(ref value); }
            }
        }
        internal Engine(nint hwnd)
        {
            nint attributes = 0, factory = 0;
            notify.Events = Events;
            try
            {
                Check(MFStartup(0x20070, 0)); started = true;
                Check(MFCreateAttributes(out attributes, 2));
                var key = new Guid("c60381b8-83a4-41f8-a3d0-de05076849a9"); Check(Slot<Unknown>(attributes, 27)(attributes, ref key, notify));
                key = new("d988879b-67c9-4d92-baa7-6eadd446039d"); Check(Slot<UInt64>(attributes, 22)(attributes, ref key, (ulong)hwnd));
                var clsid = new Guid("b44392da-499b-446b-a4cb-005fead0e6d5"); var iid = new Guid("4d645ace-26aa-4688-9be1-df3516990b93");
                Check(CoCreateInstance(ref clsid, 0, 1, ref iid, out factory));
                Check(Slot<Create>(factory, 3)(factory, 0, attributes, out engine)); Created++;
                iid = new("83015ead-b1e6-40d0-a98a-37145ffe1ad1"); Check(Marshal.QueryInterface(engine, in iid, out extended));
                Check(Slot<Boolean>(engine, 31)(engine, 1));
                Check(Slot<Boolean>(engine, 35)(engine, 0)); Check(Slot<DoubleArg>(engine, 37)(engine, .01));
                if (!LoopEnabled) throw new InvalidOperationException("SetLoop did not persist");
            }
            catch { Dispose(); throw; }
            finally { if (factory != 0) Marshal.Release(factory); if (attributes != 0) Marshal.Release(attributes); }
        }
        internal void Start(string file) { Check(Slot<Source>(engine, 6)(engine, file)); Check(Slot<Simple>(engine, 12)(engine)); Check(Slot<Simple>(engine, 32)(engine)); }
        public void Dispose()
        {
            notify.Events = null;
            if (extended != 0) { Marshal.Release(extended); extended = 0; }
            if (engine != 0)
            {
                try { Check(Slot<Simple>(engine, 42)(engine)); }
                finally { Marshal.Release(engine); engine = 0; }
            }
            if (started) { MFShutdown(); started = false; }
            GC.KeepAlive(notify);
        }
    }
}
