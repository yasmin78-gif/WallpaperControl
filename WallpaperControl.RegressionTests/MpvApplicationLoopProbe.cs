using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text.RegularExpressions;

// External measurement only: launches the REAL application, observes its
// existing diagnostic log and the default endpoint/desktop. No production probe.
internal static class MpvApplicationLoopProbe
{
    internal static void Run(string executable, string dll, string file, int count)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            using var window = new Form { ShowInTaskbar = false, Size = new(100, 100) };
            window.Shown += async (_, _) =>
            {
                window.Hide();
                try { await MeasureAsync(executable, dll, file, count); }
                catch (Exception ex) { failure = ex; }
                finally { window.Close(); }
            };
            Application.Run(window);
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static async Task MeasureAsync(string executable, string dll, string file, int count)
    {
        using var audio = new LoopAudioProbe(1, trackBoundaryIntervals: true);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string arg in new[] { "--tray", "--video-backend=libmpv", "--mpv-runtime=" + dll, "--video-file=" + file,
            "--video-test-audio=1", "--video-test-seconds=" + (count * 30 + 12) }) start.ArgumentList.Add(arg);
        using var app = Process.Start(start) ?? throw new Exception("Application did not start");
        string log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WallpaperControl", "Logs", "wallpaper-control.log");
        var clock = Stopwatch.StartNew();
        var seen = new HashSet<string>(); var boundaries = new List<LoopAudioProbe.BoundaryWindow>();
        var screens = new List<(double At, ulong Hash, bool Black)>();
        using var bitmap = new Bitmap(480, 202, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        var origin = new Point(Screen.PrimaryScreen!.Bounds.Width / 2 - 240, Screen.PrimaryScreen.Bounds.Height / 2 - 101);
        string session = ""; int loaded = 0; double lastRestart = 0, readAt = 0;
        while (!app.HasExited)
        {
            audio.Poll(); double now = LoopAudioProbe.Now;
            if (now - readAt > 40 && File.Exists(log))
            {
                readAt = now;
                using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                var lines = (await reader.ReadToEndAsync()).Split('\n').Where(s => s.Contains($"Video mpv: pid={app.Id};", StringComparison.Ordinal));
                foreach (string line in lines)
                {
                    if (!seen.Add(line)) continue;
                    if (line.Contains("file loaded;", StringComparison.Ordinal)) loaded++;
                    var match = Regex.Match(line, @"session=(\d+); playback restart; count=\d+; qpcMs=([\d.]+)");
                    if (!match.Success) continue;
                    if (session.Length != 0 && session != match.Groups[1].Value) throw new Exception("Application recreated session at loop");
                    session = match.Groups[1].Value;
                    double at = double.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                    if (lastRestart != 0 && at - lastRestart > 10000)
                    {
                        boundaries.Add(audio.ObserveBoundary(at));
                        Console.WriteLine($"APPLICATION-BOUNDARY pid={app.Id} loop={boundaries.Count} qpcMs={at:F3} session={session} loaded={loaded}");
                    }
                    lastRestart = at;
                }
            }
            // Sample around predicted 30-second boundaries and actual restart.
            double elapsed = lastRestart == 0 ? double.NaN : now - lastRestart;
            if (elapsed < 800 || elapsed > 28500 && elapsed < 31000)
            {
                graphics.CopyFromScreen(origin, Point.Empty, bitmap.Size);
                var data = bitmap.LockBits(new(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                var bytes = new byte[480 * 202 * 4]; ulong hash = 14695981039346656037; bool black = true;
                try
                {
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                    for (int i = 0; i < bytes.Length; i += 16)
                    { hash = unchecked((hash ^ BitConverter.ToUInt32(bytes, i)) * 1099511628211); if (bytes[i] > 3 || bytes[i + 1] > 3 || bytes[i + 2] > 3) black = false; }
                }
                finally { bitmap.UnlockBits(data); }
                screens.Add((now, hash, black));
            }
            if (clock.Elapsed.TotalSeconds > count * 30 + 60) throw new TimeoutException("Real application loop run");
            await Task.Delay(4);
        }
        audio.Poll();
        var changes = screens.Where((x, i) => i > 0 && x.Hash != screens[i - 1].Hash).ToArray();
        for (int i = 0; i < boundaries.Count; i++)
        {
            var b = boundaries[i];
            var gaps = changes.Zip(changes.Skip(1), (a, c) => (a.At, End: c.At)).Where(g => g.At < b.At + 400 && g.End > b.At - 400 && g.End - g.At < 1600).ToArray();
            Console.WriteLine($"APPLICATION-LOOP loop={i+1} audioGapMs={b.MaximumInterval:F3} blackSamples={screens.Count(s=>s.Black&&Math.Abs(s.At-b.At)<600)} screenChangeMaxMs={(gaps.Length==0?double.NaN:gaps.Max(g=>g.End-g.At)):F3}");
        }
        Console.WriteLine($"APPLICATION-SUMMARY pid={app.Id} loops={boundaries.Count} session={session} loaded={loaded} soundFrames={audio.SoundFrames} invalidTimestamps={audio.InvalidTimestampPackets} screenSamples={screens.Count} exit={app.ExitCode}");
        if (boundaries.Count < count || loaded != 1 || audio.SoundFrames == 0 || app.ExitCode != 0) throw new Exception("Real application loop requirements not met");
    }
}
