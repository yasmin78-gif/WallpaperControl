extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using System.Reflection;
using System.Runtime.CompilerServices;

internal static class LayoutEngineActivationTests
{
    internal static void Run(Action<bool, string> check)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var form = (App.MainForm)RuntimeHelpers.GetUninitializedObject(typeof(App.MainForm));
        void Set(string name, object value) => typeof(App.MainForm).GetField(name, fields)!.SetValue(form, value);
        var policy = new App.FullscreenPausePolicy();
        Set("fullscreenPolicy", policy);
        App.DesktopSlideshowState state = App.DesktopSlideshowState.Enabled | App.DesktopSlideshowState.Slideshow;
        Set("readNativeSlideshowStatus", (Func<App.DesktopSlideshowState>)(() => state));
        int starts = 0;
        Action start = () => { starts++; Set("customSlideshowEngineActive", true); };
        foreach (App.DesktopWallpaperPosition position in Enum.GetValues<App.DesktopWallpaperPosition>())
        {
            Set("customSlideshowEngineActive", false); starts = 0;
            form.TryStartCustomSlideshowAfterLayoutChange(position, 1, start);
            check(starts == 1, "Layout engine activation: " + position);
        }
        foreach (string guard in new[] { "manualPause", "fullscreen", "multipleMonitors", "alreadyActive", "pinned", "remote", "disabled" })
        {
            Set("customSlideshowEngineActive", guard == "alreadyActive");
            Set("slideshowPaused", guard == "manualPause");
            policy.Update(true, guard == "fullscreen", DateTime.UtcNow);
            if (guard != "fullscreen") policy.Update(false, false, DateTime.UtcNow);
            state = guard switch
            {
                "pinned" => App.DesktopSlideshowState.Enabled,
                "remote" => App.DesktopSlideshowState.Enabled | App.DesktopSlideshowState.Slideshow | App.DesktopSlideshowState.DisabledByRemoteSession,
                "disabled" => App.DesktopSlideshowState.None,
                _ => App.DesktopSlideshowState.Enabled | App.DesktopSlideshowState.Slideshow
            };
            starts = 0;
            form.TryStartCustomSlideshowAfterLayoutChange(App.DesktopWallpaperPosition.Fill, guard == "multipleMonitors" ? 2 : 1, start);
            check(starts == 0, "Layout engine activation preserves " + guard);
        }
        Set("readNativeSlideshowStatus", (Func<App.DesktopSlideshowState>)(() => throw new InvalidOperationException("Test status failure")));
        starts = 0;
        form.TryStartCustomSlideshowAfterLayoutChange(App.DesktopWallpaperPosition.Fill, 1, start);
        check(starts == 0, "Layout engine activation preserves native ownership on failed query");
    }
}
