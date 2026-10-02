extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using Video = WallpaperApp::WallpaperControl.Video;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

internal static class MpvProductionNativeChecks
{
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder text, int count);
    private static double resourceAt;
    private static TimeSpan resourceCpu;
    private static Color Pixel()
    {
        DwmFlush(); using var image = new Bitmap(1, 1);
        var bounds = Screen.PrimaryScreen!.Bounds;
        using (var graphics = Graphics.FromImage(image)) graphics.CopyFromScreen(new(bounds.Width/2,bounds.Height/2), Point.Empty, image.Size);
        return image.GetPixel(0,0);
    }
    private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, Fields)!.GetValue(value)!;
    private static Video.MpvVideoSession Active(App.VideoWallpaperController controller)
    {
        object owned = Field<object>(controller, "session");
        return (Video.MpvVideoSession)owned.GetType().GetProperty("Value", Fields)!.GetValue(owned)!;
    }
    internal static void Run(string dll, string fixtures, string mode, int count)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            using Form form = mode == "ui" ? new App.MainForm(null!) : new Form { ShowInTaskbar = false, Size = new(100, 100) };
            form.Shown += async (_, _) =>
            {
                form.Hide();
                try { await RunAsync(form, dll, fixtures, mode, count); }
                catch (Exception ex) { error = ex; }
                finally { form.Close(); }
            };
            Application.Run(form);
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
    private static async Task RunAsync(Control dispatcher, string dll, string fixtures, string mode, int count)
    {
        int passed = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); passed++; Console.WriteLine("PASS: " + name); }
        var desktop = Video.VideoBackendFactory.Create(dispatcher, new[] { "--video-backend=libmpv", "--mpv-runtime=" + dll });
        Check(desktop is Video.MpvVideoDesktop, "production factory selects pinned libmpv");
        Check(Video.VideoBackendFactory.Create(dispatcher, new[] { "--video-backend=mfplay" }) is Video.MediaFoundationVideoDesktop, "explicit MFPlay reference backend retained");
        Check(Video.VideoBackendFactory.Create(dispatcher, new[] { "--video-backend=libmpv", "--mpv-runtime=" + Path.Combine(fixtures, "missing.dll") }) is Video.MediaFoundationVideoDesktop, "missing runtime falls back deterministically to MFPlay");
        using var controller = new App.VideoWallpaperController(desktop, Console.WriteLine);
        if (mode == "ui")
        {
            await UiAsync((App.MainForm)dispatcher, controller, fixtures, Check);
            Console.WriteLine($"All {passed} production libmpv UI checks passed."); return;
        }
        string first = mode == "loop" || mode == "stress" || mode == "recovery" ? @"F:\Lager\Bilder\bgvideos\Cathedral_Dragoness_3440x1440.mp4" : Path.Combine(fixtures, mode == "audio" ? "tone-a.mp4" : mode == "fill" ? "fill-wide.mp4" : "loop-colors.mp4");
        try
        {
            controller.SetAudio(mode == "loop", 1);
            Check(await controller.StartAsync(first), "production controller prepares and publishes real player");
            var initial = Active(controller);
            var state = await initial.SnapshotAsync();
            Check(state["hwdec-current"] == "d3d11va", "production hardware decoder d3d11va active");
            Check(Video.MpvClient.LiveClients == 1 && Video.MpvVideoSession.LiveSessions == 1, "normal production playback owns one client/session");
            if (mode == "audio") { await AudioAsync(controller, fixtures, Check); return; }
            if (mode == "fill") { await FillAsync(controller, fixtures, Check); return; }
            if (mode == "loop") { await LoopsAsync(controller, initial, count); return; }
            if (mode == "recovery") { await RecoveryAsync(controller, initial, Check); return; }
            if (mode is "stress" or "stresscolors")
            {
                if (mode == "stresscolors") controller.SetPause(App.VideoPauseReason.Manual, true);
                using var sampler = mode == "stresscolors" ? new MpvScreenSampler(new(Screen.PrimaryScreen!.Bounds.Width/2, Screen.PrimaryScreen.Bounds.Height/2)) : null;
                for (int i = 1; i <= count; i++)
                {
                    string file = (i % 2 == 1) ? (mode == "stresscolors" ? Path.Combine(fixtures,"switch-colors.mp4") : @"F:\Lager\Bilder\bgvideos\Moonlit_Temptation_3440x1440.mp4") : first;
                    Check(await controller.StartAsync(file), $"production switch {i}");
                    Check(Video.MpvClient.LiveClients == 1 && Video.MpvVideoSession.LiveSessions == 1, $"switch {i} returns to one player");
                    if (i is 1 or 5 or 10 or 25 or 50 or 100 or 250) Resources(i.ToString());
                }
                if (sampler != null)
                {
                    sampler.Dispose(); Console.WriteLine($"PRODUCTION-SCREEN samples={sampler.Samples} black={sampler.BlackSamples} maxSampleGapMs={sampler.MaximumIntervalMs:F3}");
                    Check(sampler.BlackSamples == 0 && sampler.Samples > 100, "production async handoffs have no sampled black intermediate frame");
                    return;
                }
                await Task.Delay(30000); Resources("idle30");
                await Task.Delay(90000); Resources("idle120");
                return;
            }
            controller.SetPause(App.VideoPauseReason.Manual, true);
            var initialWindows = initial.Windows;
            for (int i = 0; i < 10; i++)
            {
                keybd_event(0x5B,0,0,0); keybd_event(0x44,0,0,0); keybd_event(0x44,0,2,0); keybd_event(0x5B,0,2,0);
                await Task.Delay(40); await controller.TickAsync();
            }
            Check(Active(controller).Id == initial.Id && Active(controller).Windows == initialWindows && initial.LoadedEvents == 1,
                "production Win+D keeps exact player/host/child/file without seeking or recreation");
            controller.SetPause(App.VideoPauseReason.Fullscreen, true);
            Check(await controller.StartAsync(Path.Combine(fixtures, "switch-colors.mp4")), "prepared A to B commits using real async retirement");
            controller.SetPause(App.VideoPauseReason.Fullscreen, false);
            state = await Active(controller).SnapshotAsync();
            Color pixel = Pixel();
            Check(pixel.G > 200 && pixel.R < 40 && pixel.B < 40, "production prepared switch presents actual retained green frame");
            Check(!Video.VideoNative.IsWindow(initialWindows.Host) && !Video.VideoNative.IsWindow(initialWindows.Child), "production handoff destroys old host and mpv child HWNDs");
            Check(state["pause"] == "yes" && state["mute"] == "yes" && controller.PauseReasons == App.VideoPauseReason.Manual, "manual survives fullscreen ending and media switch; candidate stays silent");
            foreach (string file in new[] { "corrupt.mp4", "unsupported.mp4", "missing.mp4", "invalid-h264.mp4" })
            {
                long id = Active(controller).Id;
                Check(!await controller.StartAsync(Path.Combine(fixtures, file)) && Active(controller).Id == id, "failed " + file + " retains stable production session");
                Check(Video.MpvClient.LiveClients == 1, "failed preparation releases every candidate client");
            }
            long retainedId = Active(controller).Id;
            using (var locked = new FileStream(first, FileMode.Open, FileAccess.Read, FileShare.None))
                Check(!await controller.StartAsync(first) && Active(controller).Id == retainedId && Video.MpvClient.LiveClients == 1,
                    "production locked file rejects before disturbing stable player");
            var starts = new List<Task<bool>>();
            for (int i = 0; i < 20; i++) starts.Add(controller.StartAsync(Path.Combine(fixtures, (i % 2 == 0) ? "loop-colors.mp4" : "switch-colors.mp4")));
            await Task.WhenAll(starts);
            Check(starts.Count(t => t.Result) == 1 && Video.MpvClient.LiveClients == 1, "rapid native A/B requests publish only latest and join stale clients");
            Check(await controller.StartAsync(Path.Combine(fixtures, "loop-black.mp4")), "black first frame accepted by native readiness");
            state = await Active(controller).SnapshotAsync();
            Check(state["pause"] == "yes" && controller.PauseReasons == App.VideoPauseReason.Manual, "black-frame switch preserves manual pause");
            Check(Video.MpvVideoSession.PeakSessions <= 2, "production lifecycle peak is stable plus one candidate");
            long beforeShutdown = Active(controller).Id;
            Task quit = Field<Video.MpvClient>(Active(controller),"client").CommandAsync("quit");
            try { await quit.WaitAsync(TimeSpan.FromSeconds(2)); } catch (OperationCanceledException) { }
            await Task.Delay(50);
            Check(controller.State == App.VideoWallpaperState.Recovering && !controller.HasSession,
                "real native shutdown retires published session into one bounded recovery incident");
            await Task.Delay(1100); await controller.TickAsync();
            Check(controller.HasSession && Active(controller).Id != beforeShutdown && controller.PauseReasons == App.VideoPauseReason.Manual,
                "native shutdown recovery publishes a new valid paused session");
        }
        finally
        {
            await controller.StopAsync();
            Check(Video.MpvClient.LiveClients == 0 && Video.MpvVideoSession.LiveSessions == 0 && Video.MpvNativeWorker.Pending == 0, "joined production stop leaves zero clients/sessions/queued native work");
            Resources("stopped");
            if (mode == "stress") { await Task.Delay(30000); Resources("stopped30"); }
            Console.WriteLine($"All {passed} production libmpv checks passed.");
        }
    }
    private static void Resources(string phase)
    {
        using var p = Process.GetCurrentProcess(); var h = NativeHandleSnapshot.Capture();
        double at = LoopAudioProbe.Now; var cpu = p.TotalProcessorTime;
        string percent = resourceAt == 0 ? "n/a" : ((cpu-resourceCpu).TotalMilliseconds*100/(at-resourceAt)/Environment.ProcessorCount).ToString("F3",System.Globalization.CultureInfo.InvariantCulture);
        resourceAt = at; resourceCpu = cpu;
        int hosts=0,children=0;
        var shell=Video.VideoNative.Resolve();
        if(shell!=null) Video.VideoNative.EnumChildWindows(shell.Value.Host,(hwnd,_)=>
        {
            Video.VideoNative.GetWindowThreadProcessId(hwnd,out uint pid);
            if(pid==Environment.ProcessId)
            {
                var name=new StringBuilder(256); GetClassName(hwnd,name,name.Capacity);
                if(name.ToString()=="mpv") children++;
                if(Video.VideoNative.GetParent(hwnd)==shell.Value.Host&&name.ToString().StartsWith("WindowsForms",StringComparison.Ordinal)) hosts++;
            }
            return true;
        },0);
        Console.WriteLine($"RESOURCE phase={phase} qpcMs={at:F3} handles={h.Total} nvidia={h.NvidiaIpc} wsMiB={p.WorkingSet64 / 1048576d:F3} privateMiB={p.PrivateMemorySize64 / 1048576d:F3} cpuSeconds={cpu.TotalSeconds:F3} cpuPercentMachine={percent} clients={Video.MpvClient.LiveClients} sessions={Video.MpvVideoSession.LiveSessions} hosts={hosts} mpvChildren={children} peak={Video.MpvVideoSession.PeakSessions} queuedNativeWork={Video.MpvNativeWorker.Pending} types=[{string.Join(',',h.Types.Select(x=>x.Key+':'+x.Value))}]");
    }
    private static async Task LoopsAsync(App.VideoWallpaperController controller, Video.MpvVideoSession session, int count)
    {
        using var audio = new LoopAudioProbe(1, trackBoundaryIntervals: true);
        var boundaries = new List<(int Loop, LoopAudioProbe.BoundaryWindow Window)>();
        int previousRestart = session.RestartEvents;
        double at = LoopAudioProbe.Now;
        while (boundaries.Count < count || LoopAudioProbe.Now - boundaries[^1].Window.At < 900)
        {
            audio.Poll();
            if (session.RestartEvents > previousRestart)
            {
                previousRestart = session.RestartEvents;
                boundaries.Add((boundaries.Count + 1, audio.ObserveBoundary(LoopAudioProbe.Now)));
                Console.WriteLine($"BOUNDARY loop={boundaries.Count} session={session.Id} loaded={session.LoadedEvents} restart={previousRestart}");
            }
            if (!controller.HasSession || !ReferenceEquals(Active(controller), session)) throw new Exception("Loop recreated production session");
            if (LoopAudioProbe.Now - at > (count + 2) * 35000) throw new TimeoutException("Production loops");
            await Task.Delay(4);
        }
        foreach (var b in boundaries) Console.WriteLine($"LOOP loop={b.Loop} audioGapMs={b.Window.MaximumInterval:F3}");
        var state = await session.SnapshotAsync();
        Console.WriteLine($"SUMMARY loops={boundaries.Count} session={session.Id} loaded={session.LoadedEvents} soundFrames={audio.SoundFrames} invalidAudioTimestamps={audio.InvalidTimestampPackets} frameDrops={state["frame-drop-count"]} decoderDrops={state["decoder-frame-drop-count"]}");
        Resources("loop-final");
    }
    private static async Task RecoveryAsync(App.VideoWallpaperController controller, Video.MpvVideoSession initial, Action<bool,string> check)
    {
        controller.SetPause(App.VideoPauseReason.Manual, true); controller.SetAudio(true, 50);
        await initial.SeekAsync(120_000_000, CancellationToken.None); await Task.Delay(400);
        await controller.TickAsync(); await Task.Delay(100); await controller.TickAsync();
        Color beforeRestart = Pixel();
        var shell = Video.VideoNative.Resolve()!.Value;
        using var explorer = Process.GetProcessById((int)shell.ProcessId);
        explorer.Kill(); await explorer.WaitForExitAsync();
        // Restart only the shell the user explicitly authorized, no other Explorer windows.
        // Windows normally restarts its shell automatically. Launching Explorer
        // again after that would open a folder over the desktop measurement.
        var automaticRestart = Stopwatch.StartNew();
        while (Video.VideoNative.Resolve() == null && automaticRestart.Elapsed.TotalSeconds < 3) await Task.Delay(100);
        using var restarted = Video.VideoNative.Resolve() == null
            ? Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden }) : null;
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed.TotalSeconds < 45)
        {
            await controller.TickAsync();
            if (controller.HasSession && Active(controller).Id != initial.Id) break;
            await Task.Delay(250);
        }
        check(controller.HasSession && Active(controller).Id != initial.Id, "real Explorer restart recovers a new valid production player");
        var state = await Active(controller).SnapshotAsync();
        Console.WriteLine("RECOVERY " + string.Join(';', state.Select(x=>x.Key+'='+x.Value)));
        check(controller.PauseReasons == App.VideoPauseReason.Manual && state["pause"] == "yes", "real shell recovery preserves manual pause");
        check(state["mute"] == "no" && Math.Abs(double.Parse(state["volume"]!, System.Globalization.CultureInfo.InvariantCulture) - 79.3700526) < 0.001, "real shell recovery preserves sound and mapped volume");
        check(double.Parse(state["time-pos"]!, System.Globalization.CultureInfo.InvariantCulture) > 10, "real shell recovery seeks to preserved position");
        DwmFlush(); Color afterRestart = Pixel();
        Console.WriteLine($"RECOVERY-PIXEL before={beforeRestart} after={afterRestart}");
        check(Math.Abs(beforeRestart.R-afterRestart.R)<8 && Math.Abs(beforeRestart.G-afterRestart.G)<8 && Math.Abs(beforeRestart.B-afterRestart.B)<8,
            "real shell recovery restores the actual visible retained frame, not only valid HWNDs");
    }
    private static async Task AudioAsync(App.VideoWallpaperController controller, string fixtures, Action<bool,string> check)
    {
        var tones = new LibMpvAudioHandoffChecks.Tones();
        using var probe = new LoopAudioProbe(sampleObserver: tones.Sample);
        async Task Observe(int milliseconds)
        { var clock = Stopwatch.StartNew(); while (clock.ElapsedMilliseconds < milliseconds) { probe.Poll(); await Task.Delay(4); } }
        controller.SetPause(App.VideoPauseReason.Manual, false); controller.SetAudio(true, 1);
        check(await controller.StartAsync(Path.Combine(fixtures,"tone-a.mp4")), "production audible A starts");
        await Observe(500);
        double baseline = tones.Windows.Select(x=>x.A).DefaultIfEmpty().Max();
        check(baseline > 0.000001 && tones.Windows.Any(x=>x.A>baseline*0.5 && x.B<x.A*0.1), "production A measured at output as 440 Hz");
        double begin = LoopAudioProbe.Now;
        Task<bool> change = controller.StartAsync(Path.Combine(fixtures,"tone-b.mp4"));
        while (!change.IsCompleted) { probe.Poll(); await Task.Delay(4); }
        check(await change, "production audio B commits after awaited old retirement");
        double commit = LoopAudioProbe.Now;
        await Observe(600);
        var after = tones.Windows.Where(x=>x.At>commit+200).ToArray();
        check(after.Any(x=>x.B>baseline*0.5 && x.A<x.B*0.1), "production B measured as 880 Hz after handoff");
        int mixed = tones.Windows.Count(x=>x.At>begin && x.A>baseline*0.2 && x.B>baseline*0.2 && Math.Min(x.A,x.B)>Math.Max(x.A,x.B)*0.2);
        Console.WriteLine($"PRODUCTION-AUDIO windows={tones.Windows.Count} baseline={baseline:F8} mixedWindows={mixed} invalidTimestamps={probe.InvalidTimestampPackets}");
        check(mixed == 0 && probe.InvalidTimestampPackets == 0, "real production output has no mixed A/B tone in 20 ms windows");
    }
    private static async Task UiAsync(App.MainForm form, App.VideoWallpaperController controller, string fixtures, Action<bool,string> check)
    {
        typeof(App.MainForm).GetField("videoWallpaper", Fields)!.SetValue(form, controller);
        typeof(App.MainForm).GetField("slideshowPaused", Fields)!.SetValue(form, true);
        var path = Field<TextBox>(form,"videoPathText");
        var combo = Field<ComboBox>(form,"wallpaperModeCombo");
        async Task Apply(App.WallpaperOperatingMode mode)
        { await (Task)typeof(App.MainForm).GetMethod("ApplyWallpaperModeAsync",Fields)!.Invoke(form,new object[]{mode})!; }
        try
        {
            path.Text = Path.Combine(fixtures,"loop-colors.mp4"); combo.SelectedIndex = 1;
            await Apply(App.WallpaperOperatingMode.VideoWallpaper);
            check(controller.HasSession && controller.PauseReasons == App.VideoPauseReason.Manual,
                "real MainForm transfers image manual pause into production libmpv mode");
            var original = Active(controller);
            path.Text = Path.Combine(fixtures,"switch-colors.mp4"); await Apply(App.WallpaperOperatingMode.VideoWallpaper);
            check(controller.HasSession && Active(controller).Id != original.Id && Video.MpvClient.LiveClients == 1,
                "real MainForm video change uses production prepared transaction");
            long stable = Active(controller).Id;
            path.Text = Path.Combine(fixtures,"missing.mp4"); await Apply(App.WallpaperOperatingMode.VideoWallpaper);
            check(controller.HasSession && Active(controller).Id == stable,
                "real MainForm rejected selection retains stable video ownership");
            controller.SetPause(App.VideoPauseReason.Manual,false);
            combo.SelectedIndex = 0; await Apply(App.WallpaperOperatingMode.ImageSlideshow);
            check(!controller.HasSession && Video.MpvClient.LiveClients == 0 && Video.MpvVideoSession.LiveSessions == 0,
                "real MainForm returning to images awaits every native retirement");
            check(Field<bool>(form,"slideshowPaused"), "real MainForm restores original image manual pause independently of video pause");
            check(Field<object>(form,"savedImageMode") == null, "real MainForm consumes and releases its image ownership snapshot");
        }
        finally { await controller.StopAsync(); }
    }
    private static async Task FillAsync(App.VideoWallpaperController controller, string fixtures, Action<bool,string> check)
    {
        controller.SetPause(App.VideoPauseReason.Manual,true);
        foreach (string name in new[]{"fill-wide.mp4","fill-portrait.mp4"})
        {
            check(await controller.StartAsync(Path.Combine(fixtures,name)), "production Fill loads " + name);
            DwmFlush(); var bounds = Screen.PrimaryScreen!.Bounds;
            using var image = new Bitmap(bounds.Width,bounds.Height);
            using (var graphics = Graphics.FromImage(image)) graphics.CopyFromScreen(bounds.Location,Point.Empty,bounds.Size);
            int cx=bounds.Width/2, cy=bounds.Height/2, width=0,height=0;
            bool White(Color p)=>p.R>220&&p.G>220&&p.B>220;
            for(int x=Math.Max(0,cx-600);x<Math.Min(bounds.Width,cx+601);x++) if(White(image.GetPixel(x,cy))) width++;
            for(int y=Math.Max(0,cy-600);y<Math.Min(bounds.Height,cy+601);y++) if(White(image.GetPixel(cx,y))) height++;
            Console.WriteLine($"PRODUCTION-FILL file={name} square={width}x{height}");
            check(width>100&&Math.Abs(width-height)<=3,"production Fill preserves source square without stretching");
            Color top=image.GetPixel(cx,100),left=image.GetPixel(420,cy);
            check(top.B>180&&top.R<50&&left.B>180&&left.R<50,"production Fill fills exposed desktop points without bars");
        }
    }
}
