using System.Runtime.InteropServices;

namespace WallpaperControl.Video;

[ComVisible(true), Guid("766C8FFB-5FDB-4FEA-A28D-B912996F51BD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMfCallback
{
    [PreserveSig] void OnMediaPlayerEvent(nint header);
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class MfCallback : IMfCallback
{
    internal Action<nint>? Event;
    public void OnMediaPlayerEvent(nint header)
    {
        try { Event?.Invoke(header); }
        catch { /* Exceptions must never cross the native COM callback boundary. */ }
    }
}

// ABI definitions from Windows SDK mfplay.h, targeting x64. Raw COM pointers
// remain on their owning UI thread, behind the production IVideoSession boundary.
internal sealed class MfPlayer : IDisposable
{
    private nint player;
    private bool platformStarted;
    private readonly MfCallback callback;
    private readonly nint videoWindow;
    private MfVideoRenderer? renderer;
    private readonly MfLoopFrameGate loopFrames = new();
    internal bool HasAudio { get; private set; }
    private nint preparedAudioItem;
    private bool audioFallbackAttempted;
    private const int PlaySlot = 3, PauseSlot = 4, SetPositionSlot = 7, GetPositionSlot = 8,
        GetStateSlot = 13, CreateItemSlot = 14, SetItemSlot = 16, GetVolumeSlot = 19, SetVolumeSlot = 20, GetMuteSlot = 23, SetMuteSlot = 24, ShutdownSlot = 38;
    [DllImport("mfplay.dll", CharSet = CharSet.Unicode)]
    private static extern int MFPCreateMediaPlayer(string? url, [MarshalAs(UnmanagedType.Bool)] bool start,
        uint options, IMfCallback callback, nint hwnd, out nint player);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref Variant value);
    [DllImport("mfplat.dll")] private static extern int MFStartup(int version, int flags);
    [DllImport("mfplat.dll")] private static extern int MFShutdown();
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Simple(nint instance);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int StateArg(nint instance, out int state);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FloatArg(nint instance, float value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FloatResult(nint instance, out float value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int BoolArg(nint instance, int value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate int Create(nint instance, [MarshalAs(UnmanagedType.LPWStr)] string url, int synchronous, nuint data, out nint item);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int PtrArg(nint instance, nint item);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Position(nint instance, ref Guid type, ref Variant value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Count(nint instance, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Attribute(nint instance, uint index, ref Guid key, out Variant value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SelectStream(nint instance, uint index, int selected);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int QuerySelection(nint instance, uint index, out int selected);
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct Variant
    {
        [FieldOffset(0)] internal ushort Type;
        [FieldOffset(8)] internal long Value;
    }
    private static T Slot<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));
    private T PlayerSlot<T>(int slot) where T : Delegate
    {
        ObjectDisposedException.ThrowIf(player == 0, this);
        return Slot<T>(player, slot);
    }
    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);
    internal MfPlayer(nint hwnd, string file, Action<nint> events)
    {
        if (!Environment.Is64BitProcess) throw new PlatformNotSupportedException("Video requires x64");
        if (!File.Exists(file)) throw new FileNotFoundException("Selected MP4 is unavailable");
        callback = new() { Event = header =>
        {
            int type = Marshal.ReadInt32(header);
            if (Marshal.ReadInt32(header, 4) >= 0)
            {
                if (type == 11) loopFrames.BeginLoop();
                if (type == 3) loopFrames.PositionSet();
            }
            events(header);
        } };
        videoWindow = hwnd;
        try
        {
            // Our explicit EVR is disposed after MFPlay releases its own MF
            // startup reference. Keep the platform alive through that cleanup.
            Check(MFStartup(0x20070, 0)); platformStarted = true;
            Check(MFPCreateMediaPlayer(null, false, 0, callback, hwnd, out player));
            if (!TrySilence()) throw new InvalidOperationException("Silent preparation unavailable");
            // Async parsing keeps corrupt/slow media off the UI thread. The
            // owning session enforces timeout and cancellation.
            nint item = 0;
            try { Check(PlayerSlot<Create>(CreateItemSlot)(player, file, 0, 0, out item)); }
            finally { if (item != 0) Marshal.Release(item); }
        }
        catch { Dispose(); throw; }
    }
    private uint PrepareStreams(nint item)
    {
        if (item == 0) throw new InvalidOperationException("Media item unavailable");
        HasAudio = false;
        Check(Slot<Count>(item, 14)(item, out uint count));
        var majorType = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        var audio = new Guid("73647561-0000-0010-8000-00aa00389b71");
        var video = new Guid("73646976-0000-0010-8000-00aa00389b71");
        uint? videoStream = null, audioStream = null;
        for (uint index = 0; index < count; index++)
        {
            var value = new Variant();
            try
            {
                Check(Slot<Attribute>(item, 17)(item, index, ref majorType, out value));
                if (value.Type != 72) throw new InvalidOperationException("Invalid stream major type");
                var major = Marshal.PtrToStructure<Guid>(new nint(value.Value));
                if (major == video)
                {
                    Check(Slot<QuerySelection>(item, 15)(item, index, out int enabled));
                    if (enabled != 0) videoStream ??= index;
                }
                if (major != audio) continue;
                Check(Slot<QuerySelection>(item, 15)(item, index, out int selected));
                if (selected != 0) { audioStream ??= index; HasAudio = true; }
                audioStream ??= index;
            }
            finally { PropVariantClear(ref value); }
        }
        if (!HasAudio && audioStream is { } firstAudio)
        {
            Check(Slot<SelectStream>(item, 16)(item, firstAudio, 1));
            HasAudio = true;
        }
        return videoStream ?? throw new InvalidOperationException("Selected video stream unavailable");
    }
    internal void SetCreatedItem(nint item)
    {
        uint videoStream = PrepareStreams(item);
        if (HasAudio && preparedAudioItem == 0) { Marshal.AddRef(item); preparedAudioItem = item; }
        renderer ??= new MfVideoRenderer(videoWindow);
        // Stream indices are those of the MF source, not MP4 track/ffmpeg order.
        // Bind after selection changes, before SetMediaItem builds the topology.
        renderer.Bind(item, videoStream);
        Check(PlayerSlot<PtrArg>(SetItemSlot)(player, item));
    }
    internal void CompletePreparation()
    {
        nint item = preparedAudioItem; preparedAudioItem = 0;
        if (item != 0) Marshal.Release(item);
    }
    internal bool TryVideoOnlyFallback()
    {
        if (preparedAudioItem == 0 || !HasAudio || audioFallbackAttempted) return false;
        audioFallbackAttempted = true;
        if (!TrySilence()) return false;
        Check(Slot<Count>(preparedAudioItem, 14)(preparedAudioItem, out uint count));
        var key = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        var audio = new Guid("73647561-0000-0010-8000-00aa00389b71");
        for (uint i = 0; i < count; i++)
        {
            var value = new Variant();
            try
            {
                Check(Slot<Attribute>(preparedAudioItem, 17)(preparedAudioItem, i, ref key, out value));
                if (value.Type == 72 && Marshal.PtrToStructure<Guid>(new nint(value.Value)) == audio)
                    Check(Slot<SelectStream>(preparedAudioItem, 16)(preparedAudioItem, i, 0));
            }
            finally { PropVariantClear(ref value); }
        }
        HasAudio = false;
        // Retry topology once on the SAME player and bound EVR, without audio.
        Check(PlayerSlot<PtrArg>(SetItemSlot)(player, preparedAudioItem));
        return true;
    }
    internal void SetAudio(bool enabled, int volume)
    {
        // Mute first: volume is configured before an active session becomes audible.
        Check(PlayerSlot<BoolArg>(SetMuteSlot)(player, 1));
        Check(PlayerSlot<FloatArg>(SetVolumeSlot)(player, Math.Clamp(volume, 0, 100) / 100f));
        if (enabled && HasAudio) Check(PlayerSlot<BoolArg>(SetMuteSlot)(player, 0));
    }
    internal bool TrySilence()
    {
        bool silent = false;
        try { Check(PlayerSlot<BoolArg>(SetMuteSlot)(player, 1)); silent = true; } catch { }
        try { Check(PlayerSlot<FloatArg>(SetVolumeSlot)(player, 0)); silent = true; } catch { }
        return silent;
    }
    internal float Volume
    {
        get { Check(PlayerSlot<FloatResult>(GetVolumeSlot)(player, out float value)); return value; }
    }
    internal uint RenderingPreferences => renderer?.RenderingPreferences ?? 0;
    internal bool HasPresentedFrame => renderer?.HasPresentedFrame == true;
    internal bool Muted
    {
        get { Check(PlayerSlot<StateArg>(GetMuteSlot)(player, out int muted)); return muted != 0; }
    }
    internal bool TryCaptureFrame(out MfVideoFrame? frame)
    {
        frame = null;
        return renderer != null && renderer.TryCaptureFrame(out frame);
    }
    internal void Play()
    { Check(PlayerSlot<Simple>(PlaySlot)(player)); }
    internal void Pause()
    {
        // Initial state and the small gap between Ended and loop restart are
        // already stopped. Native Pause in those states is invalid.
        Check(PlayerSlot<StateArg>(GetStateSlot)(player, out int state));
        if (state != 2) return;
        Check(PlayerSlot<Simple>(PauseSlot)(player));
    }
    internal long Position100ns()
    {
        var type = Guid.Empty;
        var value = new Variant();
        try
        {
            Check(PlayerSlot<Position>(GetPositionSlot)(player, ref type, ref value));
            if (value.Type != 20) throw new InvalidOperationException("Unexpected playback position type");
            return value.Value;
        }
        finally { PropVariantClear(ref value); }
    }
    internal void Seek(long position)
    {
        var type = Guid.Empty;
        var value = new Variant { Type = 20, Value = Math.Max(0, position) };
        Check(PlayerSlot<Position>(SetPositionSlot)(player, ref type, ref value));
    }
    internal void Fill(Size target)
    {
        if (renderer == null) throw new InvalidOperationException("Video renderer unavailable");
        renderer.Fill(target);
    }
    internal void Update(Graphics graphics)
    {
        if (player == 0 || renderer == null) return;
        if (PlayerSlot<StateArg>(GetStateSlot)(player, out int state) < 0) return;
        // RepaintVideo on a flushed/stopped EVR synthesizes a black image even
        // with DoNotRepaintOnStop. Keep the existing window pixels instead.
        if (state is not (2 or 3)) return;
        renderer.Paint(graphics, loopFrames);
    }
    public void Dispose()
    {
        callback.Event = null;
        nint owned = player; player = 0;
        bool closePlatform = platformStarted; platformStarted = false;
        try
        {
            if (owned != 0)
            {
                try { Slot<BoolArg>(owned, SetMuteSlot)(owned, 1); } catch { }
                Slot<Simple>(owned, ShutdownSlot)(owned);
            }
        }
        finally
        {
            try { if (owned != 0) Marshal.Release(owned); }
            finally
            {
                var oldRenderer = renderer; renderer = null;
                try { try { CompletePreparation(); } finally { oldRenderer?.Dispose(); } }
                finally
                {
                    try { if (closePlatform) Check(MFShutdown()); }
                    finally { GC.KeepAlive(callback); }
                }
            }
        }
    }
}
