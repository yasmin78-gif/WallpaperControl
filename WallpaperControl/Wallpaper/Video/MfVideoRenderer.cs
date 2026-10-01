using System.Runtime.InteropServices;

namespace WallpaperControl.Video;

// Keep the EVR's presented surface across EOS/seek. MFPlay's default EVR
// repaints black on Stop (including the automatic stop at end of presentation).
// The presenter itself replaces this surface only when it presents a new sample:
// no timer, playback-position heuristic, screenshot overlay or per-frame copy.
internal sealed class MfVideoRenderer : IDisposable
{
    internal const uint DoNotRepaintOnStop = 0x100;
    private nint sink, display;
    [DllImport("mf.dll")] private static extern int MFCreateVideoRenderer(ref Guid iid, out nint sink);
    [DllImport("mf.dll")] private static extern int MFGetService(nint instance, ref Guid service, ref Guid iid, out nint result);
    [DllImport("gdi32.dll")] private static extern int StretchDIBits(nint dc, int x, int y, int width, int height,
        int sourceX, int sourceY, int sourceWidth, int sourceHeight, nint pixels, ref BitmapHeader header, uint usage, uint operation);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Window(nint instance, nint hwnd);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetFlags(nint instance, out uint flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetFlags(nint instance, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetStream(nint instance, uint index, out nint stream);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetSink(nint instance, uint index, nint stream);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Simple(nint instance);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int VideoSize(nint instance, out Size native, out Size aspect);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int VideoPosition(nint instance, ref NormalRect source, ref PixelRect destination);
    [StructLayout(LayoutKind.Sequential)] internal struct NormalRect { internal float Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct PixelRect { internal int Left, Top, Right, Bottom; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Image(nint instance, ref BitmapHeader header,
        out nint pixels, out uint length, out long timestamp);
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapHeader
    {
        internal uint Size;
        internal int Width, Height;
        internal ushort Planes, Bits;
        internal uint Compression, ImageSize;
        internal int XPixels, YPixels;
        internal uint Colors, Important;
    }
    private static T Slot<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));
    internal static uint PreserveLastFrame(uint preferences) => preferences | DoNotRepaintOnStop;
    internal MfVideoRenderer(nint hwnd)
    {
        try
        {
            var sinkId = new Guid("6ef2a660-47c0-4666-b13d-cbb717f2fa2c");
            Marshal.ThrowExceptionForHR(MFCreateVideoRenderer(ref sinkId, out sink));
            var service = new Guid("1092a86c-ab1a-459a-a336-831fbc4d11ff");
            var displayId = new Guid("a490b1e4-ab84-4d31-a1b2-181e03b1077a");
            Marshal.ThrowExceptionForHR(MFGetService(sink, ref service, ref displayId, out display));
            Marshal.ThrowExceptionForHR(Slot<Window>(display, 9)(display, hwnd));
            Marshal.ThrowExceptionForHR(Slot<SetFlags>(display, 15)(display, PreserveLastFrame(RenderingPreferences)));
        }
        catch { Dispose(); throw; }
    }
    internal uint RenderingPreferences
    {
        get
        {
            ObjectDisposedException.ThrowIf(display == 0, this);
            Marshal.ThrowExceptionForHR(Slot<GetFlags>(display, 16)(display, out var flags));
            return flags;
        }
    }
    internal void Bind(nint item, uint videoStream)
    {
        nint stream = 0;
        try
        {
            Marshal.ThrowExceptionForHR(Slot<GetStream>(sink, 8)(sink, 0, out stream));
            Marshal.ThrowExceptionForHR(Slot<SetSink>(item, 20)(item, videoStream, stream));
        }
        finally { if (stream != 0) Marshal.Release(stream); }
    }
    internal void Fill(Size target)
    {
        Marshal.ThrowExceptionForHR(Slot<VideoSize>(display, 3)(display, out var native, out var aspect));
        var source = FillSource(native, aspect, target);
        var destination = new PixelRect { Right = target.Width, Bottom = target.Height };
        Marshal.ThrowExceptionForHR(Slot<VideoPosition>(display, 5)(display, ref source, ref destination));
        Marshal.ThrowExceptionForHR(Slot<SetFlags>(display, 7)(display, 0));
    }
    internal static NormalRect FillSource(Size native, Size aspect, Size target)
    {
        if (native.Width <= 0 || native.Height <= 0 || target.Width <= 0 || target.Height <= 0)
            throw new InvalidOperationException("Video dimensions unavailable");
        double sourceRatio = aspect.Width > 0 && aspect.Height > 0 ? (double)aspect.Width / aspect.Height : (double)native.Width / native.Height;
        double targetRatio = (double)target.Width / target.Height;
        var source = new NormalRect { Right = 1, Bottom = 1 };
        if (sourceRatio > targetRatio)
        {
            float width = (float)(targetRatio / sourceRatio);
            source.Left = (1 - width) / 2; source.Right = 1 - source.Left;
        }
        else
        {
            float height = (float)(sourceRatio / targetRatio);
            source.Top = (1 - height) / 2; source.Bottom = 1 - source.Top;
        }
        return source;
    }
    internal void Paint(Graphics graphics, MfLoopFrameGate gate)
    {
        if (display == 0) return;
        nint pixels = 0;
        try
        {
            var header = new BitmapHeader { Size = 40 };
            int hr = Slot<Image>(display, 12)(display, ref header, out pixels, out uint length, out _);
            bool valid = hr >= 0 && ValidImage(header, pixels, length);
            if (!valid || !gate.CanRepaint(true)) return;
            // RepaintVideo races the pipeline's EOS flush and can synthesize
            // black even with DoNotRepaintOnStop. Paint this immutable, already
            // composited snapshot instead; no mutable presenter state is used
            // after validation. Normal frames still use EVR's native presenter.
            nint dc = graphics.GetHdc();
            try { StretchDIBits(dc, 0, 0, header.Width, Math.Abs(header.Height), 0, 0, header.Width,
                Math.Abs(header.Height), pixels, ref header, 0, 0x00cc0020); }
            finally { graphics.ReleaseHdc(dc); }
        }
        finally { if (pixels != 0) Marshal.FreeCoTaskMem(pixels); }
    }
    private static bool ValidImage(BitmapHeader header, nint pixels, uint length) =>
        pixels != 0 && header.Width > 0 && header.Height != 0 && header.Height != int.MinValue &&
        header.Bits == 32 && header.Compression == 0 && length == (long)header.Width * Math.Abs(header.Height) * 4;
    internal bool HasPresentedFrame
    {
        get
        {
            if (display == 0) return false;
            nint pixels = 0;
            try
            {
                var header = new BitmapHeader { Size = 40 };
                int hr = Slot<Image>(display, 12)(display, ref header, out pixels, out uint length, out _);
                return hr >= 0 && ValidImage(header, pixels, length);
            }
            finally { if (pixels != 0) Marshal.FreeCoTaskMem(pixels); }
        }
    }
    // Diagnostic readback of the actual composited EVR frame, not the source
    // decoder or playback clock. Never polled in production playback.
    internal bool TryCaptureFrame(out MfVideoFrame? frame)
    {
        frame = null;
        if (display == 0) return false;
        nint pixels = 0;
        try
        {
            var header = new BitmapHeader { Size = 40 };
            int hr = Slot<Image>(display, 12)(display, ref header, out pixels, out uint length, out long timestamp);
            if (hr < 0 || !ValidImage(header, pixels, length))
                return false;
            var copy = new byte[checked((int)length)];
            Marshal.Copy(pixels, copy, 0, copy.Length);
            frame = new(new Size(header.Width, Math.Abs(header.Height)), timestamp, copy);
            return true;
        }
        finally { if (pixels != 0) Marshal.FreeCoTaskMem(pixels); }
    }
    public void Dispose()
    {
        nint ownedSink = sink, ownedDisplay = display;
        sink = display = 0;
        try { if (ownedSink != 0) Slot<Simple>(ownedSink, 11)(ownedSink); }
        finally
        {
            try { if (ownedDisplay != 0) Marshal.Release(ownedDisplay); }
            finally { if (ownedSink != 0) Marshal.Release(ownedSink); }
        }
    }
}

internal sealed record MfVideoFrame(Size Size, long Timestamp, byte[] Pixels);
