extern alias WallpaperApp;
using Video = WallpaperApp::WallpaperControl.Video;
using System.Runtime.InteropServices;

// Optional native verification with locally generated fixtures. No desktop
// attachment, registry writes, Explorer restart or network dependencies.
internal static class VideoNativeSmokeTests
{
    internal static void Run(string fixtures, Action<bool, string> check, bool phase2Only = false)
    {
        string supported = Path.Combine(fixtures, "h264.mp4");
        if (!phase2Only)
        {
        Video.VideoFileValidation.ValidateCodec(supported);
        check(Video.VideoFileValidation.IsLocalMp4(supported), "Native video: local MP4/H.264 accepted");
        check(!Video.VideoFileValidation.IsLocalMp4(@"\\server\share\video.mp4"), "Native video: UNC rejected");
        check(!Video.VideoFileValidation.IsLocalMp4("https://example.com/video.mp4"), "Native video: URL rejected");
        foreach (string invalid in new[] { "unsupported.mp4", "corrupt.mp4" })
        {
            bool rejected = false;
            try { Video.VideoFileValidation.ValidateCodec(Path.Combine(fixtures, invalid)); }
            catch (Exception ex) when (ex is NotSupportedException or COMException) { rejected = true; }
            check(rejected, "Native video: rejects " + invalid);
        }
        }
        using Task work = new(() =>
        {
            if (!phase2Only)
            {
                Playback(supported, check);
                Loops(Path.Combine(fixtures, "loop-colors.mp4"), check);
                BlackContent(Path.Combine(fixtures, "loop-black.mp4"), check);
            }
            VideoNativePhase2Checks.Run(fixtures, check);
            VideoAudioNativeChecks.Run(fixtures, check);
        });
        Thread thread = new(() =>
        {
            using var loop = new Video.VideoRenderForm { Location = new(-20000, -20000), Size = new(100, 100) };
            loop.Shown += (_, _) => { try { work.RunSynchronously(TaskScheduler.Default); } finally { loop.Close(); } };
            Application.Run(loop);
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); work.GetAwaiter().GetResult();
        if (!phase2Only) VideoLoopTimingDiagnostics.Run(Path.Combine(fixtures, "loop-colors.mp4"), 10, "queued-seek-play", check);
    }
    private static void Loops(string file, Action<bool, string> check)
    {
        // Blue first second, red last second, plus a real AAC stream. This small
        // non-activating window also verifies the visible front surface: EVR
        // GetCurrentImage alone cannot read the retained surface after EOS flush.
        using var target = new Video.VideoRenderForm { Location = UncoveredLocation(), Size = new(240, 240) };
        target.Show();
        Video.VideoNative.SetWindowPos(target.Handle, 0, 0, 0, 0, 0, 0x1 | 0x2 | 0x10 | 0x40);
        Video.MfPlayer? player = null;
        bool ready = false, desiredPlaying = true;
        int ended = 0;
        DateTime endedAt = default;
        Exception? failure = null;
        Video.MfVideoFrame? finalFrame = null;
        Video.MfVideoFrame? lastPresentedFrame = null;
        bool heldBeforeSeek = true, heldDuringSeek = true, endedRed = true, validAfterFirst = true, nonBlack = true, mute = true;
        int captures = 0, wraps = 0;
        long previousTimestamp = -1;
        player = new(target.Handle, file, header =>
        {
            try
            {
                int type = Marshal.ReadInt32(header);
                Marshal.ThrowExceptionForHR(Marshal.ReadInt32(header, 4));
                if (type == 5) player!.SetCreatedItem(Marshal.ReadIntPtr(header, 32));
                if (type == 6) { player!.Fill(target.ClientSize); ready = true; }
                if (type == 0 && !desiredPlaying) player!.Pause();
                if (type == 11)
                {
                    ended++;
                    endedAt = DateTime.UtcNow;
                    finalFrame = lastPresentedFrame;
                    endedRed &= finalFrame != null && finalFrame.Pixels[2] > 200 && finalFrame.Pixels[0] < 40;
                }
            }
            catch (Exception ex) { failure = ex; }
        });
        target.Paint += (_, e) => player.Update(e.Graphics);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!ready && failure == null && DateTime.UtcNow < deadline) Pump(10);
            if (failure != null) throw failure;
            check(ready && (player.RenderingPreferences & Video.MfVideoRenderer.DoNotRepaintOnStop) != 0,
                "Native loop: actual EVR retains its surface at automatic EOS stop");
            player.Play();
            bool seekSent = false, restarted = false, receivedFirst = false;
            int observedEnd = 0;
            deadline = DateTime.UtcNow.AddSeconds(45);
            while ((ended < 10 || DateTime.UtcNow - endedAt < TimeSpan.FromMilliseconds(600)) && failure == null && DateTime.UtcNow < deadline)
            {
                Pump(10);
                if (ended != observedEnd)
                {
                    observedEnd = ended; seekSent = restarted = false;
                    // Inject a pause at the third loop boundary. No pending Play
                    // acknowledgement may override the user's pause policy.
                    if (ended == 3) { desiredPlaying = false; player.Pause(); }
                }
                if (player.TryCaptureFrame(out var frame) && frame != null)
                {
                    lastPresentedFrame = frame;
                    receivedFirst = true; captures++;
                    validAfterFirst &= frame.Size == target.ClientSize;
                    // Check the entire composited image: no black border/blank.
                    for (int i = 0; i < frame.Pixels.Length; i += 4)
                        if (Math.Max(frame.Pixels[i], Math.Max(frame.Pixels[i + 1], frame.Pixels[i + 2])) < 100)
                        {
                            if (nonBlack) Console.WriteLine($"Native loop unexpected black sample: timestamp={frame.Timestamp}; ended={ended}; pixelOffset={i}; captures={captures}");
                            nonBlack = false; break;
                        }
                    if (previousTimestamp >= 0 && frame.Timestamp < previousTimestamp) wraps++;
                    previousTimestamp = frame.Timestamp;
                }
                // Read the real window pixels even when the EVR's flushed
                // sample buffer is unavailable. Do not mistake flush for a
                // black visible image, or a Play acknowledgement for a frame.
                if (receivedFirst)
                {
                    DwmFlush();
                    using var front = new Bitmap(target.ClientSize.Width, target.ClientSize.Height);
                    using (var graphics = Graphics.FromImage(front))
                        graphics.CopyFromScreen(target.PointToScreen(Point.Empty), Point.Empty, target.ClientSize);
                    var pixel = front.GetPixel(front.Width / 2, front.Height / 2);
                    bool visible = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) > 100;
                    if (!visible && nonBlack) Console.WriteLine($"Native loop unexpected black surface: ended={ended}; captures={captures}; root=0x{GetAncestor(WindowFromPoint(target.PointToScreen(new(120, 120))), 2):X}; target=0x{target.Handle:X}");
                    nonBlack &= visible;
                    if (ended > 0 && !seekSent) heldBeforeSeek &= pixel.R > 200 && pixel.B < 40;
                    if (ended > 0 && seekSent && !restarted) heldDuringSeek &= pixel.R > 200 && pixel.B < 40;
                    validAfterFirst &= front.Width == 240 && front.Height == 240;
                }
                if (ended > 0)
                {
                    var gap = DateTime.UtcNow - endedAt;
                    // Deliberately enlarge both gaps, making a lost surface
                    // observable instead of relying on a fortunate fast seek.
                    if (!seekSent && gap.TotalMilliseconds >= 250) { player.Seek(0); seekSent = true; }
                    if (!restarted && gap.TotalMilliseconds >= (ended == 3 ? 850 : 500) && ended < 10)
                    { desiredPlaying = true; player.Play(); restarted = true; }
                    mute &= player.Muted;
                    target.Invalidate(); target.Update();
                }
            }
            if (failure != null) throw failure;
            check(ended == 10 && wraps == 9 && captures > 500, "Native loop: ten real EOS transitions and nine presented timestamp resets");
            Console.WriteLine($"Native loop observations: captures={captures}, endedRed={endedRed}, heldBeforeSeek={heldBeforeSeek}, heldDuringSeek={heldDuringSeek}, valid={validAfterFirst}, nonBlack={nonBlack}, muted={mute}");
            check(endedRed && heldBeforeSeek, "Native loop: exact last red frame survives delayed restart and paused boundary");
            check(validAfterFirst && heldDuringSeek && nonBlack, "Native loop: no black visible frame through seek, repaint and first new frame");
            check(mute, "Native loop: muted playback preserved across every restart");
            check(target.ClientSize == new Size(240, 240) && nonBlack, "Native loop: centered Fill covers square destination without black bars");
            player.Dispose(); player.Dispose();
            check(!player.TryCaptureFrame(out _), "Native loop: disposed EVR cannot return a stale frame");
        }
        finally { player.Dispose(); target.Hide(); }
    }
    private static void BlackContent(string file, Action<bool, string> check)
    {
        using var target = new Video.VideoRenderForm { Location = new(-20000, -20000), Size = new(320, 180) };
        target.Show();
        Video.MfPlayer? player = null;
        bool ready = false, ended = false;
        Exception? failure = null;
        player = new(target.Handle, file, header =>
        {
            try
            {
                int type = Marshal.ReadInt32(header);
                Marshal.ThrowExceptionForHR(Marshal.ReadInt32(header, 4));
                if (type == 5) player!.SetCreatedItem(Marshal.ReadIntPtr(header, 32));
                if (type == 6) { player!.Fill(target.ClientSize); ready = true; }
                if (type == 11) { ended = true; player!.Seek(0); player.Play(); }
            }
            catch (Exception ex) { failure = ex; }
        });
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!ready && failure == null && DateTime.UtcNow < deadline) Pump(10);
            if (failure != null) throw failure;
            check(ready, "Native loop: black-content fixture initialized");
            player.Play();
            bool newBlackFrame = false;
            deadline = DateTime.UtcNow.AddSeconds(8);
            while (failure == null && DateTime.UtcNow < deadline)
            {
                Pump(10);
                if (ended && player.TryCaptureFrame(out var frame) && frame != null && frame.Timestamp < 9000000)
                {
                    newBlackFrame = frame.Pixels[0] < 10 && frame.Pixels[1] < 10 && frame.Pixels[2] < 10;
                    if (newBlackFrame) break;
                }
            }
            if (failure != null) throw failure;
            check(newBlackFrame, "Native loop: actual black first frame after seek is accepted");
            player.Pause(); Pump(100);
            using var bitmap = new Bitmap(320, 180);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Magenta);
            player.Update(graphics);
            var painted = bitmap.GetPixel(160, 90);
            check(painted.R < 10 && painted.G < 10 && painted.B < 10,
                "Native loop: black sample releases presentation hold and repaints while paused");
            check(player.Muted, "Native loop: black-content restart remains muted");
        }
        finally { player.Dispose(); target.Hide(); }
    }
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
    private static Point UncoveredLocation()
    {
        var host = Video.VideoNative.Resolve()?.Host ?? throw new InvalidOperationException("Native loop check requires desktop shell");
        var bounds = Screen.PrimaryScreen!.Bounds;
        var formerRoot = GetAncestor(WindowFromPoint(new(bounds.Left + 140, bounds.Top + 140)), 2);
        Console.WriteLine($"Native loop visibility: former (20,20) center root=0x{formerRoot:X}; desktop=0x{host:X}");
        for (int y = bounds.Top + 20; y < bounds.Bottom - 260; y += 260)
            for (int x = bounds.Left + 20; x < bounds.Right - 260; x += 260)
            {
                var points = new[] { new Point(x + 5, y + 5), new Point(x + 235, y + 5),
                    new Point(x + 120, y + 120), new Point(x + 5, y + 235), new Point(x + 235, y + 235) };
                if (points.All(p => GetAncestor(WindowFromPoint(p), 2) == host))
                    return new(x, y);
            }
        throw new InvalidOperationException("Native loop check needs an uncovered 240x240 desktop area");
    }
    private static void Playback(string file, Action<bool, string> check)
    {
        using var target = new Video.VideoRenderForm { Location = new(-20000, -20000), Size = new(320, 180) };
        target.Show();
        Video.MfPlayer? player = null;
        bool ready = false;
        Exception? failure = null;
        player = new Video.MfPlayer(target.Handle, file, header =>
        {
            try
            {
                int type = Marshal.ReadInt32(header), hr = Marshal.ReadInt32(header, 4);
                Marshal.ThrowExceptionForHR(hr);
                if (type == 5) player!.SetCreatedItem(Marshal.ReadIntPtr(header, 32));
                if (type == 6) { player!.Fill(target.ClientSize); ready = true; }
            }
            catch (Exception ex) { failure = ex; }
        });
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!ready && failure == null && DateTime.UtcNow < deadline) Pump(10);
            if (failure != null) throw failure;
            check(ready, "Native video: async media item ready with audio output muted");
            player.Pause(); Pump(300);
            check(failure == null, "Native video: initialized paused without starting playback");
            player.Seek(10000000); Pump(300);
            check(Math.Abs(player.Position100ns() - 10000000) < 100000, "Native video: seek restored while initially stopped");
            player.Play(); Pump(1000);
            long position = player.Position100ns();
            check(position > 0 && failure == null, "Native video: MFPlay advances playback position");
            player.Pause(); Pump(300); position = player.Position100ns(); Pump(300);
            check(Math.Abs(player.Position100ns() - position) < 100000, "Native video: paused clock remains stable");
            player.Play(); Pump(300);
            check(player.Position100ns() > position, "Native video: resumes from retained position");
            player.Dispose(); player.Dispose();
            check(failure == null, "Native video: repeated cleanup safe");
        }
        finally { player.Dispose(); target.Hide(); }
    }
    private static void Pump(int milliseconds)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(2); }
    }
}

