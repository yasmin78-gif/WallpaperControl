extern alias WallpaperApp;
using Video = WallpaperApp::WallpaperControl.Video;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

// Explicit diagnostic only: no source changes, desktop attachment or settings writes.
internal static class VideoLoopTimingDiagnostics
{
    internal static void Run(string file, int cycles, string? selectedStrategy = null, Action<bool, string>? check = null)
    {
        if (cycles is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(cycles));
        using Task work = new(() =>
        {
            foreach (string strategy in selectedStrategy == null ? new[] { "queued-seek-play", "direct-seek-play", "direct-play-only", "queued-play-only", "queued-seek-play-no-audio" } : new[] { selectedStrategy })
                Measure(file, strategy, cycles, check);
        });
        var thread = new Thread(() =>
        {
            using var loop = new Video.VideoRenderForm { Location = new(-20000, -20000), Size = new(100, 100) };
            loop.Shown += (_, _) => { try { work.RunSynchronously(TaskScheduler.Default); } finally { loop.Close(); } };
            Application.Run(loop);
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); work.GetAwaiter().GetResult();
    }
    private static void Measure(string file, string strategy, int cycles, Action<bool, string>? check)
    {
        bool visibleProbe = Path.GetFileName(file) == "loop-colors.mp4";
        var location = visibleProbe ? (Point)typeof(VideoNativeSmokeTests).GetMethod("UncoveredLocation", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)! : new Point(-20000, -20000);
        using var window = new Video.VideoRenderForm { Location = location, Size = new(240, 240) };
        window.Show();
        var clock = Stopwatch.StartNew();
        using var audio = new LoopAudioProbe();
        var boundaries = new List<double>();
        Video.MfPlayer? player = null;
        bool ready = false;
        bool unsupportedProbe = false;
        Exception? failure = null;
        int ended = 0, firstFrames = 0, activeClocks = 0, visibleFrames = 0;
        bool audioInvariant = true;
        double eosAt = -1, lastChangeAt = 0;
        long lastTimestamp = -1;
        var gaps = new List<double>();
        void Trace(string stage) => Console.WriteLine($"TIMING strategy={strategy} cycle={ended} wallMs={clock.Elapsed.TotalMilliseconds:F3} eosMs={(eosAt < 0 ? -1 : clock.Elapsed.TotalMilliseconds - eosAt):F3} stage={stage}");
        void Restart()
        {
            try
            {
                Trace("restart-request");
                if (strategy.Contains("seek")) { Trace("seek-request-zero"); player!.Seek(0); Trace("seek-call-return"); }
                Trace("play-request"); player!.Play(); Trace("play-call-return");
            }
            catch (Exception ex) { failure = ex; }
        }
        player = new(window.Handle, file, header =>
        {
            try
            {
                int type = Marshal.ReadInt32(header), hr = Marshal.ReadInt32(header, 4);
                Marshal.ThrowExceptionForHR(hr);
                if (type == 5)
                {
                    if (strategy.Contains("low-latency"))
                    {
                        var candidateRenderer = new Video.MfVideoRenderer(window.Handle);
                        typeof(Video.MfPlayer).GetField("renderer", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(player, candidateRenderer);
                        unsupportedProbe = !TryLowLatency(Field<nint>(candidateRenderer, "sink"));
                    }
                    player!.SetCreatedItem(Marshal.ReadIntPtr(header, 32));
                }
                if (type == 9) Trace($"mf-event-{Marshal.ReadInt32(header, 32)} state={Marshal.ReadInt32(header, 16)}");
                if (type == 6) { player!.Fill(window.ClientSize); ready = true; }
                if (type == 11)
                {
                    ended++; eosAt = clock.Elapsed.TotalMilliseconds; boundaries.Add(LoopAudioProbe.Now); Trace("eos-callback");
                    if (ended <= cycles)
                    {
                        if (strategy.StartsWith("queued")) window.BeginInvoke((Action)Restart);
                        else Restart();
                    }
                }
                if (eosAt >= 0 && type is 0 or 1 or 2 or 3) Trace($"callback-{type} position={player!.Position100ns()}");
            }
            catch (Exception ex) { failure = ex; }
        });
        try
        {
            Wait(() => ready || failure != null, 15000);
            if (failure != null) throw failure;
            if (unsupportedProbe) { Console.WriteLine("SKIP: existing EVR does not accept the low-latency sink attribute"); return; }
            if (strategy.EndsWith("no-audio"))
            {
                ready = false; if (!player.TryVideoOnlyFallback()) throw new InvalidOperationException("Audio deselection unavailable");
                Wait(() => ready || failure != null, 15000); if (failure != null) throw failure;
            }
            player.SetAudio(true, 1); player.Play();
            Wait(() => player.HasPresentedFrame, 15000); player.CompletePreparation();
            nint original = Field<nint>(player, "player"); var renderer = Field<object>(player, "renderer");
            var originalCallback = Field<Video.MfCallback>(player, "callback").Event;
            var initialNative = NativeHandleSnapshot.Capture();
            var process = Process.GetCurrentProcess(); process.Refresh(); int initialHandles = process.HandleCount;
            TimeSpan previousCpu = process.TotalProcessorTime; double previousSample = clock.Elapsed.TotalMilliseconds;
            var limit = Stopwatch.StartNew();
            while ((firstFrames < cycles || activeClocks < cycles || (visibleProbe && visibleFrames < cycles)) && failure == null && limit.ElapsedMilliseconds < (cycles + 2) * 120000)
            {
                Application.DoEvents();
                audio.Poll();
                if (ended > activeClocks && player.Position100ns() is > 0 and < 10000000)
                { activeClocks = ended; Trace($"clock-advancing position={player.Position100ns()}"); }
                if (visibleProbe && ended > visibleFrames)
                {
                    using var pixel = new Bitmap(1, 1);
                    using (var graphics = Graphics.FromImage(pixel)) graphics.CopyFromScreen(window.PointToScreen(new(120, 120)), Point.Empty, new Size(1, 1));
                    var color = pixel.GetPixel(0, 0);
                    if (color.B > 200 && color.R < 40) { visibleFrames = ended; Trace("first-visible-blue-frame"); }
                }
                if (player.TryCaptureFrame(out var frame) && frame != null && frame.Timestamp != lastTimestamp)
                {
                    double now = clock.Elapsed.TotalMilliseconds;
                    if (eosAt >= 0 && frame.Timestamp < lastTimestamp)
                    {
                        double freeze = now - lastChangeAt;
                        gaps.Add(now - eosAt); firstFrames++; Trace($"first-new-frame timestamp={frame.Timestamp} lastFrameToFirstMs={freeze:F3} position={player.Position100ns()} muted={player.Muted} volume={player.Volume:F3}");
                        audioInvariant &= player.Muted == strategy.EndsWith("no-audio") && Math.Abs(player.Volume - .01f) < .0001;
                        if (firstFrames is 1 or 5 or 10 or 25 or 50 or 100 || firstFrames == cycles)
                        {
                            process.Refresh(); double elapsed = clock.Elapsed.TotalMilliseconds - previousSample;
                            double cpu = (process.TotalProcessorTime - previousCpu).TotalMilliseconds * 100 / elapsed / Environment.ProcessorCount;
                            previousCpu = process.TotalProcessorTime; previousSample = clock.Elapsed.TotalMilliseconds;
                            var handles = NativeHandleSnapshot.Capture();
                            int ownedCom = new[] { Field<nint>(player, "player"), Field<nint>(renderer, "sink"), Field<nint>(renderer, "display") }.Count(x => x != 0);
                            int subscriptions = Field<Video.MfCallback>(player, "callback").Event?.GetInvocationList().Length ?? 0;
                            Console.WriteLine($"RESOURCES strategy={strategy} loop={firstFrames} handles={handles.Total} nvidiaIpc={handles.NvidiaIpc} types=[{string.Join(',', handles.Types.Select(x => x.Key + ':' + x.Value))}] cpuPercent={cpu:F2} workingSetMiB={process.WorkingSet64 / 1048576d:F2} privateMiB={process.PrivateMemorySize64 / 1048576d:F2} ownedPlayer={(Field<nint>(player, "player") != 0 ? 1 : 0)} ownedEVR={(Field<nint>(renderer, "sink") != 0 ? 1 : 0)} rendererHWND={(Video.VideoNative.IsWindow(window.Handle) ? 1 : 0)} ownedCom={ownedCom} retainedPreparationItem={Field<nint>(player, "preparedAudioItem") != 0} callbackSubscriptions={subscriptions} ownedTimers=0");
                        }
                    }
                    lastTimestamp = frame.Timestamp; lastChangeAt = now;
                }
                Thread.Sleep(1);
            }
            if (failure != null) throw failure;
            // Let the last restarted audio reach the endpoint before summarizing.
            var drain = Stopwatch.StartNew(); while (drain.ElapsedMilliseconds < 500) { Application.DoEvents(); audio.Poll(); Thread.Sleep(1); }
            process.Refresh();
            var finalNative = NativeHandleSnapshot.Capture();
            foreach (var boundary in boundaries)
                foreach (var gap in audio.Gaps.Where(x => x.Start <= boundary + 500 && x.End >= boundary - 500))
                    Console.WriteLine($"AUDIO-GAP strategy={strategy} gapMs={gap.End - gap.Start:F3} beginRelativeEosMs={gap.Start - boundary:F3} endRelativeEosMs={gap.End - boundary:F3}");
            Console.WriteLine($"SUMMARY strategy={strategy} loops={firstFrames}/{cycles} eosToFrameMs=[{string.Join(',', gaps.Select(x => x.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)))}] samePlayer={original == Field<nint>(player, "player")} sameEVR={ReferenceEquals(renderer, Field<object>(player, "renderer"))} handlesBefore={initialHandles} handlesAfter={process.HandleCount} workingSetMiB={process.WorkingSet64 / 1048576d:F2} privateMiB={process.PrivateMemorySize64 / 1048576d:F2}");
            if (firstFrames != cycles) throw new InvalidOperationException("Strategy failed to loop: " + strategy);
            check?.Invoke(original == Field<nint>(player, "player"), "Native loop timing: repeated loops reuse one MFPlay player");
            check?.Invoke(ReferenceEquals(renderer, Field<object>(player, "renderer")), "Native loop timing: repeated loops reuse one EVR");
            check?.Invoke(audioInvariant, "Native loop timing: sound and configured volume survive every boundary");
            check?.Invoke(initialNative.NvidiaIpc == finalNative.NvidiaIpc, "Native loop timing: no NVIDIA IPC growth per loop");
            check?.Invoke(Field<nint>(player, "preparedAudioItem") == 0 && ReferenceEquals(originalCallback, Field<Video.MfCallback>(player, "callback").Event), "Native loop timing: no retained preparation COM or new event subscription");
            player.Dispose();
            check?.Invoke(Field<nint>(player, "player") == 0 && Field<nint>(renderer, "sink") == 0 && Field<nint>(renderer, "display") == 0 && Field<Video.MfCallback>(player, "callback").Event == null, "Native loop timing: shutdown releases all three owned COM pointers and callbacks");
        }
        finally { player.Dispose(); }
    }
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int UIntAttribute(nint self, ref Guid key, uint value);
    private static bool TryLowLatency(nint sink)
    {
        var iid = new Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"); nint attributes = 0;
        try
        {
            int hr = Marshal.QueryInterface(sink, in iid, out attributes);
            Console.WriteLine($"LOW-LATENCY EVR attributes hr=0x{hr:X8}"); if (hr < 0) return false;
            var key = new Guid("9c27891a-ed7a-40e1-88e8-b22727a024ee");
            var set = Marshal.GetDelegateForFunctionPointer<UIntAttribute>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(attributes), 21 * IntPtr.Size));
            hr = set(attributes, ref key, 1); Console.WriteLine($"LOW-LATENCY SetUINT32 hr=0x{hr:X8}"); return hr >= 0;
        }
        finally { if (attributes != 0) Marshal.Release(attributes); }
    }
    private static void Wait(Func<bool> done, int timeout)
    { var clock = Stopwatch.StartNew(); while (!done()) { if (clock.ElapsedMilliseconds > timeout) throw new TimeoutException(); Application.DoEvents(); Thread.Sleep(1); } }
}
