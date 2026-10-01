extern alias WallpaperApp;
using Video = WallpaperApp::WallpaperControl.Video;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class VideoAudioNativeChecks
{
    private sealed class Player : IDisposable
    {
        private readonly Video.VideoRenderForm window = new() { Location = new(-20000, -20000), Size = new(320, 180) };
        internal readonly Video.MfPlayer Native;
        internal int Ready;
        private Exception? error;
        internal Player(string path) { try { window.Show(); Native = new(window.Handle, path, Event); } catch { window.Dispose(); throw; } }
        private void Event(nint header)
        {
            try
            {
                Marshal.ThrowExceptionForHR(Marshal.ReadInt32(header, 4));
                if (Marshal.ReadInt32(header) == 5) Native.SetCreatedItem(Marshal.ReadIntPtr(header, 32));
                if (Marshal.ReadInt32(header) == 6) { Native.Fill(window.ClientSize); Ready++; }
            }
            catch (Exception ex) { error = ex; }
        }
        internal void WaitReady(int count = 1) { Wait(() => Ready >= count || error != null); if (error != null) throw error; }
        public void Dispose() { Native.Dispose(); window.Dispose(); }
    }
    internal static void Run(string fixtures, Action<bool, string> check)
    {
        using (var player = new Player(Path.Combine(fixtures, "h264.mp4")))
        {
            player.WaitReady(); var native = player.Native;
            check(native.HasAudio && native.Muted, "Native audio: AAC selected but candidate muted");
            var originalPlayer = Field<nint>(native, "player"); var renderer = Field<object>(native, "renderer");
            native.SetAudio(false, 50); native.Play(); Wait(() => native.HasPresentedFrame);
            check(native.Muted && Math.Abs(native.Volume - .5f) < .001, "Native audio: disabled playback remains muted at 50 percent");
            native.Pause(); native.CompletePreparation();
            check(Field<nint>(native, "preparedAudioItem") == 0, "Native audio: readiness releases temporary media-item reference");
            native.SetAudio(true, 73); check(!native.Muted && Math.Abs(native.Volume - .73f) < .001, "Native audio: unmute and volume use MFPlay controls");
            native.SetAudio(false, 73); check(native.Muted && Math.Abs(native.Volume - .73f) < .001, "Native audio: disabling retains volume");
            native.SetAudio(true, 0); check(!native.Muted && native.Volume == 0, "Native audio: enabled zero distinct from mute");
            native.SetAudio(true, 1); native.Play(); long before = native.Position100ns(); Pump(120); native.Pause(); Pump(300);
            long paused = native.Position100ns(); Pump(100);
            check(paused > before && Math.Abs(native.Position100ns() - paused) < 100000, "Native audio: one playback clock pauses video and sound");
            native.Play(); Pump(100); native.Pause(); check(native.Position100ns() > paused, "Native audio: shared playback resumes");
            check(originalPlayer == Field<nint>(native, "player") && ReferenceEquals(renderer, Field<object>(native, "renderer")), "Native audio: toggles reuse existing player and EVR");
            native.Dispose(); check(Field<nint>(native, "player") == 0 && Field<nint>(native, "preparedAudioItem") == 0, "Native audio: disposal releases playback and retained source");
        }
        using (var player = new Player(Path.Combine(fixtures, "h264-no-audio.mp4")))
        {
            player.WaitReady(); player.Native.SetAudio(true, 80); player.Native.Play(); Wait(() => player.Native.HasPresentedFrame);
            check(!player.Native.HasAudio && player.Native.Muted && Math.Abs(player.Native.Volume - .8f) < .001, "Native audio: absent audio track plays video safely");
        }
        using (var player = new Player(Path.Combine(fixtures, "h264.mp4")))
        {
            player.WaitReady(); var renderer = Field<object>(player.Native, "renderer"); var instance = Field<nint>(player.Native, "player");
            check(player.Native.TryVideoOnlyFallback(), "Native audio: video-only fallback accepted");
            player.WaitReady(2); player.Native.SetAudio(true, 70); player.Native.Play(); Wait(() => player.Native.HasPresentedFrame);
            check(!player.Native.HasAudio && player.Native.Muted, "Native audio: fallback produces valid silent video");
            check(!player.Native.TryVideoOnlyFallback(), "Native audio: fallback strictly bounded to one attempt");
            check(instance == Field<nint>(player.Native, "player") && ReferenceEquals(renderer, Field<object>(player.Native, "renderer")), "Native audio: fallback creates no extra player or EVR");
        }
    }
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static void Wait(Func<bool> done) { var time = Stopwatch.StartNew(); while (!done()) { if (time.ElapsedMilliseconds > 15000) throw new TimeoutException("Native audio readiness"); Pump(5); } }
    private static void Pump(int milliseconds) { var time = Stopwatch.StartNew(); while (time.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(1); } }
}
