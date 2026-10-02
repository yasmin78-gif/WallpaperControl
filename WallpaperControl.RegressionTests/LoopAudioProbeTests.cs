internal static class LoopAudioProbeTests
{
    internal static void Run(Action<bool, string> check)
    {
        var window = new LoopAudioProbe.BoundaryWindow(1000);
        check(double.IsNaN(window.MaximumInterval), "Loop audio diagnostic: absent sound is unknown, not zero gap");
        window.Include(999, 999.0208333333);
        check(Math.Abs(window.MaximumInterval - 1000d / 48000) < 0.000001,
            "Loop audio diagnostic: retains sub-millisecond sample resolution");
        window.Include(2000, 2200);
        check(window.MaximumInterval < 0.021, "Loop audio diagnostic: excludes silence outside boundary window");
        window.Include(1500, 1800);
        check(window.MaximumInterval == 300, "Loop audio diagnostic: includes gap crossing measurement window end");
    }
}
