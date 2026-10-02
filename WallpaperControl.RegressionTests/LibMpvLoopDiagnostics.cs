using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

// Explicit native-client diagnostic. The third-party DLL is supplied by absolute
// path, never copied to normal build output or added to application dependencies.
internal static class LibMpvLoopDiagnostics
{
    // mpv's cubic volume scale: this value gives 0.01 linear gain, matching
    // the MFPlay/MediaEngine diagnostics, continuously (never faded at loops).
    internal static void Run(string dll, string file, int cycles, double volume = 21.54434690031884, bool desktop = false)
    {
        if (cycles is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(cycles));
        if (!double.IsFinite(volume) || volume is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(volume));
        if (!File.Exists(file) || !File.Exists(dll)) throw new FileNotFoundException("Explicit libmpv DLL and local video required");
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { Measure(Path.GetFullPath(dll), Path.GetFullPath(file), cycles, volume, desktop); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private readonly record struct Event(int Type, double At);
    private readonly record struct Frame(double At, double Pts);
    private readonly record struct Screen(double At, ulong Hash, bool Black);
    private static void Measure(string dll, string file, int cycles, double volume, bool desktop)
    {
        double epoch = LoopAudioProbe.Now;
        using var window = new Form { Text = "libmpv loop diagnostic - keep visible", ClientSize = new(480, 202),
            StartPosition = FormStartPosition.Manual, Location = new(40, 60), BackColor = Color.Black };
        using var desktopHost = desktop ? new MpvDesktopHost() : null;
        if (!desktop) window.Show();
        using var mpv = new Player(dll, desktopHost?.Handle ?? window.Handle, volume, desktop ? MpvDesktopHost.Options : null);
        using var audio = new LoopAudioProbe(1, trackBoundaryIntervals: true);
        using var image = new Bitmap(480, 202, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(image);
        using var process = Process.GetCurrentProcess();
        var pixels = new byte[480 * 202 * 4];
        var events = new List<Event>(); var frames = new List<Frame>(); var screens = new List<Screen>(); var boundaries = new List<double>();
        var audioWindows = new List<LoopAudioProbe.BoundaryWindow>();
        double previousPosition = 0, previousPts = double.NaN, captureAt = 0;
        bool loaded = false, readyLogged = false;
        int loadedCount = 0, videoReconfig = 0, occluded = 0;
        double resourceAt = epoch; TimeSpan cpuAt = process.TotalProcessorTime;
        double syncAt = 0, maximumAbsoluteSync = 0;
        int syncSamples = 0;
        mpv.Load(file);
        Console.WriteLine($"CONFIG dll={dll} file={file} backendCount=1 HWND=1 options={string.Join(';', mpv.Options.Select(x => x.Key + '=' + x.Value))}");
        double drainAt = -1;
        while (true)
        {
            Application.DoEvents(); audio.Poll();
            if (window.IsDisposed) throw new OperationCanceledException("Diagnostic window closed");
            double now = LoopAudioProbe.Now;
            for (int i = 0; i < 100; i++)
            {
                var item = mpv.Poll();
                if (item.Type == 0) break;
                events.Add(new(item.Type, LoopAudioProbe.Now));
                Console.WriteLine($"EVENT wallMs={LoopAudioProbe.Now - epoch:F3} type={item.Type} error={item.Error}");
                if (item.Type == 2) Console.WriteLine("MPV-LOG " + mpv.LogText(item.Data));
                if (item.Type == 8) { loaded = true; loadedCount++; }
                if (item.Type == 17) videoReconfig++;
                if (item.Error < 0 || item.Type is 1 or 7) throw new InvalidOperationException($"Unexpected mpv event {item.Type}, error {item.Error}");
            }
            double position = mpv.Double("time-pos"), duration = mpv.Double("duration");
            if (loaded && now - syncAt >= 100)
            {
                syncAt = now;
                double sync = mpv.Double("avsync");
                if (double.IsFinite(sync)) { maximumAbsoluteSync = Math.Max(maximumAbsoluteSync, Math.Abs(sync)); syncSamples++; }
            }
            if (loaded && !readyLogged && double.IsFinite(position))
            {
                desktopHost?.FindChild();
                if (desktopHost != null && !desktopHost.Attached) throw new InvalidOperationException("Desktop loop HWNDs are not attached");
                readyLogged = true;
                foreach (string property in new[] { "mpv-version", "ffmpeg-version", "mpv-configuration", "license", "current-vo", "current-ao", "hwdec-current", "video-codec", "audio-codec", "audio-device", "audio-out-params", "video-params" })
                    Console.WriteLine($"PROPERTY {property}={mpv.String(property) ?? "unavailable"}");
            }
            if (double.IsFinite(position))
            {
                if (previousPosition > 1 && position < previousPosition - 1)
                {
                    boundaries.Add(now);
                    audioWindows.Add(audio.ObserveBoundary(now));
                    Console.WriteLine($"BOUNDARY loop={boundaries.Count} wallMs={now - epoch:F3} previousPosition={previousPosition:F6} newPosition={position:F6}");
                    Console.WriteLine($"AUDIO-QUALITY loop={boundaries.Count} soundFrames={audio.SoundFrames} invalidTimestamps={audio.InvalidTimestampPackets} discontinuities={audio.DiscontinuousPackets} maximumAbsoluteAvsyncMs={maximumAbsoluteSync * 1000:F3}");
                    if (boundaries.Count is 1 or 5 or 10 or 25 or 50 || boundaries.Count == cycles)
                    {
                        process.Refresh(); var handles = NativeHandleSnapshot.Capture();
                        double cpu = (process.TotalProcessorTime - cpuAt).TotalMilliseconds * 100 / (now - resourceAt) / Environment.ProcessorCount;
                        cpuAt = process.TotalProcessorTime; resourceAt = now;
                        Console.WriteLine($"RESOURCES loop={boundaries.Count} handles={handles.Total} nvidia={handles.NvidiaIpc} cpuPercent={cpu:F3} workingSetMiB={process.WorkingSet64 / 1048576d:F3} privateMiB={process.PrivateMemorySize64 / 1048576d:F3} backendCount=1 suppliedHWND=1 creationCount={mpv.Created} fileLoadedEvents={loadedCount} videoReconfigEvents={videoReconfig} vo={mpv.String("current-vo")} hwdec={mpv.String("hwdec-current")} types=[{string.Join(',', handles.Types.Select(x => x.Key + ':' + x.Value))}]");
                    }
                }
                previousPosition = position;
            }
            bool near = loaded && double.IsFinite(position) && (position < 1 || (double.IsFinite(duration) && duration - position < 1));
            if (near)
            {
                // time-pos is a playback timeline, NOT a rendered-frame PTS.
                // This libmpv build exposes no public video-pts property.
                double pts = position;
                if (double.IsFinite(pts) && pts != previousPts)
                {
                    frames.Add(new(LoopAudioProbe.Now, pts)); previousPts = pts;
                }
                if (now - captureAt >= 4)
                {
                    captureAt = now; var origin = desktopHost?.ProbeOrigin ?? window.PointToScreen(Point.Empty);
                    bool uncovered = new[] { new Point(2, 2), new Point(240, 101), new Point(477, 199) }
                        .All(p => desktopHost != null ? desktopHost.IsProbeExposed(new(origin.X + p.X, origin.Y + p.Y)) : GetAncestor(WindowFromPoint(new(origin.X + p.X, origin.Y + p.Y)), 2) == window.Handle);
                    if (uncovered)
                    {
                        graphics.CopyFromScreen(origin, Point.Empty, image.Size);
                        var data = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                        ulong hash = 14695981039346656037; bool black = true;
                        try
                        {
                            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                            for (int i = 0; i < pixels.Length; i += 16)
                            {
                                hash = unchecked((hash ^ BitConverter.ToUInt32(pixels, i)) * 1099511628211);
                                if (pixels[i] > 3 || pixels[i + 1] > 3 || pixels[i + 2] > 3) black = false;
                            }
                        }
                        finally { image.UnlockBits(data); }
                        screens.Add(new(LoopAudioProbe.Now, hash, black));
                    }
                    else occluded++;
                }
            }
            if (boundaries.Count >= cycles && drainAt < 0) drainAt = now;
            if (drainAt > 0 && now - drainAt > 900) break;
            if (now - epoch > (cycles + 2) * 120000d) throw new TimeoutException("Automatic loops did not complete");
            Thread.Sleep(1);
        }
        var changes = screens.Where((x, i) => i > 0 && x.Hash != screens[i - 1].Hash).ToArray();
        for (int i = 0; i < boundaries.Count; i++)
        {
            double b = boundaries[i];
            var gaps = audio.Gaps.Where(x => x.Start < b + 600 && x.End > b - 600).ToArray();
            var wrap = frames.Zip(frames.Skip(1), (a, c) => (Old: a, New: c))
                .Where(x => x.Old.Pts > x.New.Pts + 1 && Math.Abs(x.New.At - b) < 800).OrderBy(x => Math.Abs(x.New.At - b)).ToArray();
            var intervals = changes.Zip(changes.Skip(1), (a, c) => (Start: a.At, End: c.At))
                .Where(x => x.Start < b + 400 && x.End > b - 400 && x.End - x.Start < 1600).OrderByDescending(x => x.End - x.Start).ToArray();
            var seek = events.Where(x => x.Type == 20 && Math.Abs(x.At - b) < 800).OrderBy(x => Math.Abs(x.At - b)).FirstOrDefault();
            var restart = events.Where(x => x.Type == 21 && Math.Abs(x.At - b) < 800).OrderBy(x => Math.Abs(x.At - b)).FirstOrDefault();
            int blackSamples = screens.Count(x => x.Black && Math.Abs(x.At - b) < 600);
            double maxGap = audioWindows[i].MaximumInterval;
            double ptsGap = wrap.Length == 0 ? double.NaN : wrap[0].New.At - wrap[0].Old.At;
            double screenGap = intervals.Length == 0 ? double.NaN : intervals[0].End - intervals[0].Start;
            Console.WriteLine($"LOOP loop={i + 1} boundaryWallMs={b - epoch:F3} audioGapMs={maxGap:F3} audioGapCountAbove1Ms={gaps.Length} audioGapStartRelativeMs={audioWindows[i].Start - b:F3} audioGapEndRelativeMs={audioWindows[i].End - b:F3} timelineChangeIntervalMs={ptsGap:F3} firstNewTimelineRelativeMs={(wrap.Length == 0 ? double.NaN : wrap[0].New.At - b):F3} screenChangeMaxMs={screenGap:F3} blackSamples={blackSamples} seekRelativeMs={(seek.At == 0 ? double.NaN : seek.At - b):F3} restartRelativeMs={(restart.At == 0 ? double.NaN : restart.At - b):F3}");
            foreach (var gap in gaps) Console.WriteLine($"AUDIO-GAP loop={i + 1} gapMs={gap.End - gap.Start:F3} startRelativeMs={gap.Start - b:F3} endRelativeMs={gap.End - b:F3}");
        }
        Console.WriteLine($"SUMMARY loops={boundaries.Count} creationCount={mpv.Created} fileLoadedEvents={loadedCount} videoReconfigEvents={videoReconfig} screenSamples={screens.Count} occludedSamples={occluded} soundFrames={audio.SoundFrames} invalidAudioTimestampPackets={audio.InvalidTimestampPackets} discontinuousAudioPackets={audio.DiscontinuousPackets} avsync={mpv.String("avsync")} maximumAbsoluteAvsyncMs={maximumAbsoluteSync * 1000:F3} avsyncSamples={syncSamples} frameDrops={mpv.String("frame-drop-count")} decoderFrameDrops={mpv.String("decoder-frame-drop-count")}");
        if (mpv.Created != 1 || loadedCount != 1) throw new InvalidOperationException("Backend/file was recreated during native looping");
        mpv.Dispose(); audio.Dispose(); window.Close(); desktopHost?.Dispose();
        if(desktopHost!=null && !desktopHost.Released) throw new InvalidOperationException("Desktop loop renderer HWND cleanup failed");
        var final = NativeHandleSnapshot.Capture();
        Console.WriteLine($"SHUTDOWN backendReleased={mpv.Released} desktopHostReleased={desktopHost?.Released??true} handles={final.Total} nvidia={final.NvidiaIpc} backendCount=0 HWND=0");
    }
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);

    internal sealed class Player : IDisposable
    {
        private nint library, player;
        internal int Created { get; private set; }
        internal bool Released => player == 0 && library == 0;
        internal Dictionary<string, string> Options { get; } = new();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint Create();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Simple(nint self);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Destroy(nint self);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Free(nint data);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Text(nint self, [MarshalAs(UnmanagedType.LPUTF8Str)] string key, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int RequestLogs(nint self, [MarshalAs(UnmanagedType.LPUTF8Str)] string level);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint GetString(nint self, [MarshalAs(UnmanagedType.LPUTF8Str)] string key);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetDouble(nint self, [MarshalAs(UnmanagedType.LPUTF8Str)] string key, int format, out double value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint WaitEvent(nint self, double timeout);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Command(nint self, nint argv);
        [StructLayout(LayoutKind.Sequential)] internal struct NativeEvent { internal int Type, Error; internal ulong Userdata; internal nint Data; }
        private readonly Destroy destroy;
        private readonly Free free;
        private readonly GetDouble getDouble;
        private readonly GetString getString;
        private readonly WaitEvent wait;
        private readonly Command command;
        private readonly Text setProperty;
        private T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
        private static void Check(int result) { if (result < 0) throw new InvalidOperationException("libmpv error " + result); }
        internal Player(string dll, nint hwnd, double volume, IReadOnlyDictionary<string, string>? extraOptions = null)
        {
            library = NativeLibrary.Load(dll);
            try
            {
                destroy = Export<Destroy>("mpv_terminate_destroy"); free = Export<Free>("mpv_free"); getDouble = Export<GetDouble>("mpv_get_property");
                getString = Export<GetString>("mpv_get_property_string"); wait = Export<WaitEvent>("mpv_wait_event"); command = Export<Command>("mpv_command");
                setProperty = Export<Text>("mpv_set_property_string");
                player = Export<Create>("mpv_create")();
                if (player == 0) throw new InvalidOperationException("mpv_create failed"); Created++;
                Options = new() { ["config"] = "no", ["load-scripts"] = "no", ["terminal"] = "no", ["osd-level"] = "0",
                    ["input-default-bindings"] = "no", ["input-vo-keyboard"] = "no", ["wid"] = hwnd.ToString(),
                    ["vo"] = "gpu", ["gpu-api"] = "d3d11", ["gpu-context"] = "d3d11", ["hwdec"] = "d3d11va",
                    ["ao"] = "wasapi", ["audio-exclusive"] = "no", ["audio-device"] = "auto", ["loop-file"] = "inf",
                    ["volume"] = volume.ToString(System.Globalization.CultureInfo.InvariantCulture), ["mute"] = "no" };
                var option = Export<Text>("mpv_set_option_string");
                if (extraOptions != null) foreach (var pair in extraOptions) Options[pair.Key] = pair.Value;
                foreach (var pair in Options) Check(option(player, pair.Key, pair.Value));
                Check(Export<RequestLogs>("mpv_request_log_messages")(player, "v"));
                Check(Export<Simple>("mpv_initialize")(player));
            }
            catch { Dispose(); throw; }
        }
        internal void Load(string file)
        {
            nint first = Marshal.StringToCoTaskMemUTF8("loadfile"), second = Marshal.StringToCoTaskMemUTF8(file), args = Marshal.AllocHGlobal(3 * IntPtr.Size);
            try { Marshal.WriteIntPtr(args, first); Marshal.WriteIntPtr(args, IntPtr.Size, second); Marshal.WriteIntPtr(args, 2 * IntPtr.Size, 0); Check(command(player, args)); }
            finally { Marshal.FreeHGlobal(args); Marshal.FreeCoTaskMem(first); Marshal.FreeCoTaskMem(second); }
        }
        internal NativeEvent Poll() => Marshal.PtrToStructure<NativeEvent>(wait(player, 0));
        internal void Set(string key, string value) => Check(setProperty(player, key, value));
        internal double Double(string key) => getDouble(player, key, 5, out double value) < 0 ? double.NaN : value;
        internal string? String(string key)
        {
            nint value = getString(player, key);
            try { return value == 0 ? null : Marshal.PtrToStringUTF8(value); }
            finally { if (value != 0) free(value); }
        }
        internal string LogText(nint data) => string.Join(' ', Enumerable.Range(0, 3).Select(i => Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(data, i * IntPtr.Size))?.Trim()));
        public void Dispose()
        {
            try { if (player != 0) { destroy(player); player = 0; } }
            finally { if (library != 0) { NativeLibrary.Free(library); library = 0; } }
        }
    }
}
