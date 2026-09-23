extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using System.Runtime.InteropServices;

internal static class NativeSlideshowAdvanceTests
{
    internal static void Run(Action<bool, string> check) => Exercise(check).GetAwaiter().GetResult();

    private static async Task Exercise(Action<bool, string> check)
    {
        static IReadOnlyDictionary<string, string> Images(string image = "a", string monitor = "monitor-1") =>
            new Dictionary<string, string> { [monitor] = image };
        App.NativeSlideshowAdvance runner = new();
        int calls = 0, waits = 0;
        bool success = await runner.TryAdvanceAsync(() => calls++, () => Images(), () => { waits++; return Task.CompletedTask; });
        check(success && calls == 1 && waits == 0 && !runner.IsRunning, "Native Next: normal success needs no wait or extra advance");

        foreach (var after in new IReadOnlyDictionary<string, string>?[] { Images(), null, Images(""), Images("b", "different-monitor"), new Dictionary<string, string>() })
        {
            int reads = 0; calls = 0;
            COMException original = new("simulated unexpected failure", App.NativeSlideshowAdvance.Unexpected);
            Exception? caught = null;
            try
            {
                await runner.TryAdvanceAsync(() => { calls++; throw original; }, () => reads++ == 0 ? Images() : after, () => Task.CompletedTask);
            }
            catch (Exception ex) { caught = ex; }
            check(ReferenceEquals(caught, original) && calls == 1 && !runner.IsRunning,
                "Native Next: unconfirmed change preserves error and never retries (case " + (after?.Values.FirstOrDefault() ?? "unknown") + ")");
        }

        calls = 0; int snapshots = 0;
        success = await runner.TryAdvanceAsync(() => { calls++; throw new COMException("collision", App.NativeSlideshowAdvance.Unexpected); },
            () => snapshots++ == 0 ? Images() : Images("b"), () => Task.CompletedTask);
        check(success && calls == 1 && !runner.IsRunning, "Native Next: confirmed concurrent change satisfies request without double advance");
        snapshots = 0;
        success = await runner.TryAdvanceAsync(() => throw new COMException("collision", App.NativeSlideshowAdvance.Unexpected),
            () => new Dictionary<string, string> { ["first"] = "a", ["second"] = snapshots++ == 0 ? "a" : "b" }, () => Task.CompletedTask);
        check(success, "Native Next: concurrent change on second monitor is recognized");

        foreach (Exception failure in new Exception[] { new COMException("access", unchecked((int)0x80070005)), new InvalidOperationException("other") })
        {
            waits = 0; Exception? caught = null;
            try { await runner.TryAdvanceAsync(() => throw failure, () => Images(), () => { waits++; return Task.CompletedTask; }); }
            catch (Exception ex) { caught = ex; }
            check(ReferenceEquals(caught, failure) && waits == 0 && !runner.IsRunning, "Native Next: other failures are not suppressed or retried: " + failure.GetType().Name);
        }

        TaskCompletionSource settle = new(); snapshots = 0; calls = 0;
        Task<bool> first = runner.TryAdvanceAsync(() => { calls++; throw new COMException("collision", App.NativeSlideshowAdvance.Unexpected); },
            () => snapshots++ == 0 ? Images() : Images("b"), () => settle.Task);
        check(!first.IsCompleted && runner.IsRunning, "Native Next: pending observation holds reentrancy guard");
        bool second = await runner.TryAdvanceAsync(() => calls++, () => Images(), () => Task.CompletedTask);
        check(!second && calls == 1, "Native Next: repeated click during observation does not call Windows again");
        settle.SetResult();
        check(await first && !runner.IsRunning, "Native Next: confirmed completion releases guard");
        check(await runner.TryAdvanceAsync(() => calls++, () => Images(), () => Task.CompletedTask) && calls == 2,
            "Native Next: later independent click works normally");
    }
}
