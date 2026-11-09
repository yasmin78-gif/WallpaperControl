namespace WallpaperControl;

// A separate clock keeps visitor controls from changing the normal resume snapshot.
internal sealed class VisitorSlideshowClock(Func<long>? milliseconds = null)
{
    private readonly Func<long> now = milliseconds ?? (() => Environment.TickCount64);
    private long deadline, remaining;
    internal bool Paused { get; private set; }
    internal long Remaining => Paused ? remaining : Math.Max(0, deadline - now());
    internal bool Due => !Paused && Remaining == 0;
    internal void Restart(uint interval)
    { remaining = interval; deadline = now() + remaining; }
    internal void SetPaused(bool paused)
    {
        if (Paused == paused) return;
        remaining = Remaining; Paused = paused;
        if (!paused) deadline = now() + remaining;
    }
}
