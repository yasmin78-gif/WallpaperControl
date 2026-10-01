namespace WallpaperControl.Video;

// A seek or Play acknowledgement does not establish presentation readiness.
// Until the EVR has a composited sample from the new seek, a WM_PAINT must
// leave its retained front surface alone rather than ask it to repaint black.
internal sealed class MfLoopFrameGate
{
    internal bool Holding { get; private set; }
    private bool seekCompleted;
    internal void BeginLoop() { Holding = true; seekCompleted = false; }
    internal void PositionSet() { if (Holding) seekCompleted = true; }
    internal bool NeedsFrameCheck => Holding && seekCompleted;
    internal bool CanRepaint(bool hasPresentedFrame)
    {
        if (!Holding) return true;
        if (seekCompleted && hasPresentedFrame) Holding = false;
        return !Holding;
    }
}
